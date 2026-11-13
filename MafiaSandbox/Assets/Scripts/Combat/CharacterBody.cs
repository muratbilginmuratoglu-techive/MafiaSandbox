using System;
using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Player;

namespace MafiaSandbox.Combat
{
    /// <summary>Per-bone hit collider tag. Zone names match the prototype / clip names: Hit_Head, Hit_ArmLeft, Hit_LegRight, torso…</summary>
    public class HitZone : MonoBehaviour
    {
        public string zone;
        public CharacterBody body;
        public Rigidbody rb;
    }

    /// <summary>
    /// Health, hit zones, hit reactions, injuries and a runtime-built ragdoll for any Mixamo-rigged character (player and NPCs).
    /// Built from bone names (mixamorig:*) so it works on every GLB from build_crowd.py.
    /// </summary>
    public class CharacterBody : MonoBehaviour
    {
        public int maxHp = 3;
        public int hp = 3;
        public bool isPlayer;
        public bool invulnerable;
        public bool Dead { get; private set; }
        public bool Ragdolled { get; private set; }
        public float Stun { get; private set; }
        public int InjuryLeg { get; private set; }   // 1 = limping
        public int InjuryArm { get; private set; }
        public string KilledBy { get; private set; }
        public Rigidbody Hips => hipsRb;

        public event Action<CharacterBody, string, Vector3> OnHit;     // body, zone, point
        public event Action<CharacterBody, string> OnDeath;            // body, killer tag
        public event Action<CharacterBody> OnRespawn;

        // zone capsules: (fromBone, toBone, zone, radius) — same table as the prototype's HITCAPS
        static readonly (string a, string b, string zone, float r)[] Caps = {
            ("Head","HeadTop_End","Hit_Head",0.15f), ("Neck","Head","Hit_Head",0.09f),
            ("Spine2","Neck","torso",0.19f), ("Spine1","Spine2","torso",0.19f), ("Spine","Spine1","torso",0.18f), ("Hips","Spine","torso",0.18f),
            ("LeftArm","LeftForeArm","Hit_ArmLeft",0.08f), ("LeftForeArm","LeftHand","Hit_ArmLeft",0.07f),
            ("RightArm","RightForeArm","Hit_ArmRight",0.08f), ("RightForeArm","RightHand","Hit_ArmRight",0.07f),
            ("LeftUpLeg","LeftLeg","Hit_LegLeft",0.11f), ("LeftLeg","LeftFoot","Hit_LegLeft",0.09f),
            ("RightUpLeg","RightLeg","Hit_LegRight",0.11f), ("RightLeg","RightFoot","Hit_LegRight",0.09f),
        };
        // ragdoll bodies: bone → parent body bone
        static readonly (string bone, string parent, float mass)[] RagBones = {
            ("Hips", null, 12f), ("Spine1", "Hips", 10f), ("Head", "Spine1", 4f),
            ("LeftArm", "Spine1", 2f), ("LeftForeArm", "LeftArm", 1.5f), ("RightArm", "Spine1", 2f), ("RightForeArm", "RightArm", 1.5f),
            ("LeftUpLeg", "Hips", 6f), ("LeftLeg", "LeftUpLeg", 3f), ("RightUpLeg", "Hips", 6f), ("RightLeg", "RightUpLeg", 3f),
        };

        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly List<Rigidbody> rbs = new List<Rigidbody>();
        readonly List<Collider> hitCols = new List<Collider>();
        readonly List<Transform> boneList = new List<Transform>();
        readonly List<(Vector3 p, Quaternion r)> bindPose = new List<(Vector3, Quaternion)>();
        Rigidbody hipsRb;
        CharacterAnimator animator;
        CharacterController cc;
        Vector3 rootOffsetAtDeath;
        public Transform RightHand { get; private set; }

        public static readonly List<CharacterBody> All = new List<CharacterBody>();
        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static int HitboxLayer { get; private set; } = -1;
        public static int CharactersLayer { get; private set; } = -1;

        void Awake()
        {
            if (HitboxLayer < 0) { HitboxLayer = LayerMask.NameToLayer("Hitbox"); CharactersLayer = LayerMask.NameToLayer("Characters"); }
            hp = maxHp;
            animator = GetComponent<CharacterAnimator>();
            cc = GetComponent<CharacterController>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                int i = t.name.IndexOf("mixamorig:", StringComparison.Ordinal);
                if (i >= 0) bones[t.name.Substring(i + 10)] = t;
            }
            bones.TryGetValue("RightHand", out var rh); RightHand = rh;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true)) { smr.updateWhenOffscreen = true; smr.skinnedMotionVectors = false; }   // bounds follow the ragdoll / animation, never culled wrongly
            if (bones.Count < 10) { Debug.LogWarning($"{name}: rig bones not found ({bones.Count})", this); return; }
            BuildRagdoll();
            BuildHitZones();
            if (CharactersLayer >= 0) gameObject.layer = CharactersLayer;
        }

        Transform B(string n) => bones.TryGetValue(n, out var t) ? t : null;

        void BuildRagdoll()
        {
            var rbByBone = new Dictionary<string, Rigidbody>();
            foreach (var (bone, parent, mass) in RagBones)
            {
                var t = B(bone); if (!t) continue;
                var rb = t.gameObject.AddComponent<Rigidbody>();
                rb.mass = mass; rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.linearDamping = 0.3f; rb.angularDamping = 1.5f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rbs.Add(rb); rbByBone[bone] = rb;
                if (bone == "Hips") hipsRb = rb;
                boneList.Add(t); bindPose.Add((t.localPosition, t.localRotation));
            }
            foreach (var (bone, parent, mass) in RagBones)
            {
                if (parent == null || !rbByBone.TryGetValue(bone, out var rb) || !rbByBone.TryGetValue(parent, out var prb)) continue;
                var j = rb.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = prb; j.enableCollision = false; j.enableProjection = true;
                var lo = j.lowTwistLimit; lo.limit = -30f; j.lowTwistLimit = lo;
                var hi = j.highTwistLimit; hi.limit = 30f; j.highTwistLimit = hi;
                var s1 = j.swing1Limit; s1.limit = bone.Contains("Arm") ? 70f : 40f; j.swing1Limit = s1;
                var s2 = j.swing2Limit; s2.limit = bone.Contains("Arm") ? 70f : 25f; j.swing2Limit = s2;
            }
        }

        void BuildHitZones()
        {
            foreach (var (a, b, zone, r) in Caps)
            {
                var ta = B(a); var tb = B(b);
                if (!ta) continue;
                Vector3 pa = ta.position, pb = tb ? tb.position : ta.position + ta.up * 0.2f;
                var go = new GameObject("HZ_" + zone);
                go.layer = HitboxLayer >= 0 ? HitboxLayer : 0;
                go.transform.SetParent(ta, false);
                go.transform.position = (pa + pb) * 0.5f;
                go.transform.rotation = Quaternion.LookRotation((pb - pa).sqrMagnitude > 1e-6f ? (pb - pa) : ta.up, Vector3.up);
                var cap = go.AddComponent<CapsuleCollider>();
                cap.direction = 2; cap.radius = r; cap.height = Vector3.Distance(pa, pb) + 2f * r;
                var hz = go.AddComponent<HitZone>(); hz.zone = zone; hz.body = this;
                hz.rb = ta.GetComponentInParent<Rigidbody>();
                hitCols.Add(cap);
            }
        }

        void Update()
        {
            if (Stun > 0f) Stun -= Time.deltaTime;
        }

        public string ZoneFromDirection(Vector3 hitDir)
        {
            // torso reaction clip chosen by where the hit came from
            Vector3 d = transform.InverseTransformDirection(hitDir);   // direction the bullet travels, in body space
            if (Mathf.Abs(d.z) > Mathf.Abs(d.x)) return d.z > 0 ? "Hit_Back" : "Hit_Front";   // bullet travelling along +z came from behind
            return d.x > 0 ? "Hit_Left" : "Hit_Right";
        }

        /// <summary>Apply a hit. zone is a HitZone.zone value ("torso" is resolved to a directional clip). Returns true if this hit killed.</summary>
        public bool Hit(string zone, Vector3 point, Vector3 dir, int dmg, bool lethal, float impulse, string by, bool meleeHeadKill = true)
        {
            if (Dead) { Impulse(point, dir, 16f * impulse); return false; }
            if (invulnerable) return false;
            string clip = zone == "torso" ? ZoneFromDirection(dir) : zone;
            hp -= dmg;
            if (zone == "Hit_Head" && meleeHeadKill) lethal = true;
            KilledBy = by;
            OnHit?.Invoke(this, zone, point);
            if (hp <= 0 || lethal) { Die(point, dir, zone, impulse, by); return true; }
            if (animator) animator.PlayUpper(clip);
            Stun = zone.StartsWith("Hit_Leg") ? 0.75f : 0.6f;
            if (zone.StartsWith("Hit_Leg")) InjuryLeg = 1;
            if (zone.StartsWith("Hit_Arm")) InjuryArm = 1;
            return false;
        }

        public void Die(Vector3 point, Vector3 dir, string zone, float impulse, string by)
        {
            if (Dead) return;
            Dead = true; KilledBy = by; Stun = 99f;
            float strength = (zone == "Hit_Head" ? 4.5f : zone != null && zone.StartsWith("Hit_Leg") ? 3.5f : 5f) * impulse;
            EnterRagdoll();
            Impulse(point, (dir + Vector3.up * 0.25f).normalized, strength * 8f);
            OnDeath?.Invoke(this, by);
        }

        void EnterRagdoll()
        {
            if (Ragdolled) return;
            Ragdolled = true;
            if (animator) animator.Freeze();
            if (cc) cc.enabled = false;
            foreach (var rb in rbs) { rb.isKinematic = false; rb.WakeUp(); }
        }

        public void Impulse(Vector3 point, Vector3 dir, float force)
        {
            if (!Ragdolled) return;
            Rigidbody best = null; float bd = float.MaxValue;
            foreach (var rb in rbs) { float d = (rb.worldCenterOfMass - point).sqrMagnitude; if (d < bd) { bd = d; best = rb; } }
            if (best) best.AddForceAtPosition(dir * force, point, ForceMode.Impulse);
        }

        /// <summary>Where the body ended up (feet position for respawn / loot prompts).</summary>
        public Vector3 GroundPos()
        {
            Vector3 p = hipsRb ? hipsRb.position : transform.position;
            if (Physics.Raycast(p + Vector3.up, Vector3.down, out var h, 6f, ~(1 << HitboxLayer | 1 << CharactersLayer), QueryTriggerInteraction.Ignore)) return h.point;
            return new Vector3(p.x, transform.position.y, p.z);
        }

        /// <summary>Restore the animated character at a position.</summary>
        public void Respawn(Vector3 pos, float yaw)
        {
            foreach (var rb in rbs) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            for (int i = 0; i < boneList.Count; i++) { boneList[i].localPosition = bindPose[i].p; boneList[i].localRotation = bindPose[i].r; }
            Ragdolled = false; Dead = false; hp = maxHp; Stun = 0f; InjuryLeg = 0; InjuryArm = 0; KilledBy = null;
            if (animator) animator.Unfreeze();
            if (cc) cc.enabled = true;
            OnRespawn?.Invoke(this);
        }

        /// <summary>Closest zone to a world point (melee hits, car impacts).</summary>
        public string ZoneNear(Vector3 p)
        {
            string best = "torso"; float bd = float.MaxValue;
            foreach (var c in hitCols) { if (!c) continue; float d = (c.ClosestPoint(p) - p).sqrMagnitude; if (d < bd) { bd = d; best = c.GetComponent<HitZone>().zone; } }
            return best;
        }

        public Vector3 ChestPos => B("Spine1") ? B("Spine1").position : transform.position + Vector3.up * 1.2f;
    }
}
