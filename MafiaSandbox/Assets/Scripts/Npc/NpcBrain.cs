using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Player;
using MafiaSandbox.World;

namespace MafiaSandbox.Npc
{
    /// <summary>Procedural arm poses laid over whatever clip is playing (the character set has no clips for these).</summary>
    public enum ArmPose { None, HandsUp, Cower, PhoneCall, PhoneText, Point, Crossed, Hips }

    /// <summary>Something happened at a place: shots, a body, a fight. Civilians gossip about it, gather round, phone it in; the police check it out.</summary>
    public class Incident { public Vector3 pos; public float time; public bool byPlayer; public string kind; public bool reported; public int watchers; public float Age => Time.time - time; }

    /// <summary>
    /// Common NPC machinery: NavMeshAgent locomotion + animation, perception helpers, noise events, stun/injury handling,
    /// death → ragdoll → respawn, an optional weapon, and the small things that make a person look alive: the head turns
    /// towards whatever catches their eye, arms take poses the clips don't have (hands up, phone to the ear, arms crossed),
    /// they turn and speed up gently, say things in speech bubbles and get unstuck by themselves.
    /// Subclasses implement Think() as a state machine.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public abstract class NpcBrain : MonoBehaviour
    {
        public string displayName = "Citizen";
        public float walkSpeed = 2.0f, runSpeed = 5.6f;
        public RoadGraph graph;
        public bool dummy;                       // debug: stand still
        public string State { get; protected set; } = "init";

        float offMeshT;
        protected NavMeshAgent agent; protected CharacterBody body; protected CharacterAnimator anim; protected CapsuleCollider capsule;
        protected float stateT, thinkT, respawnT, thinkDt = 0.2f; float lastThink;
        protected Vector3 spawnPos; protected float spawnYaw;
        protected Vector3? lookAt;              // turn the body to face this point while standing
        protected Vector3? faceWhileMoving;     // keep facing this point while moving (strafing with a gun)
        protected GameObject gun; protected Transform gunMuzzle; protected float fireCd; protected WeaponKind? gunKind;
        protected string idleClip = "Idle";

        protected static PlayerController Player => GameManager.I ? GameManager.I.player : null;
        protected static CharacterBody PlayerBody => GameManager.I && GameManager.I.player ? GameManager.I.player.GetComponent<CharacterBody>() : null;
        protected static WantedSystem Wanted => WantedSystem.I;
        public bool Dead => body && body.Dead;
        public CharacterBody Body => body;
        protected Vector3 Eye => transform.position + Vector3.up * 1.6f;
        protected float DistToPlayer => Player ? Vector3.Distance(transform.position, Player.transform.position) : 999f;
        protected bool PlayerInVehicle { get { if (!Player) return false; var vi = Player.GetComponent<Vehicles.VehicleInteraction>(); return vi != null && vi.Current != null; } }
        protected bool GunShown => gun && gun.activeSelf;
        protected static float CamDist(Vector3 p) { var c = Camera.main; return c ? Vector3.Distance(c.transform.position, p) : 999f; }

        protected virtual void Awake()
        {
            agent = GetComponent<NavMeshAgent>(); body = GetComponent<CharacterBody>(); anim = GetComponent<CharacterAnimator>(); capsule = GetComponent<CapsuleCollider>();
            agent.updateRotation = false; agent.radius = 0.35f; agent.height = 1.8f; agent.angularSpeed = 720f; agent.acceleration = 8f; agent.stoppingDistance = 0.4f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance; agent.autoBraking = true; agent.autoRepath = true;
            agent.avoidancePriority = Random.Range(40, 60);   // people of equal standing don't all shove the same way
            spawnPos = transform.position; spawnYaw = transform.eulerAngles.y;
            gaitScale = Random.Range(0.92f, 1.08f);
            uid = ++uidCounter;
        }

        protected virtual void OnEnable()
        {
            WorldEvents.OnNoise += OnNoise;
            if (body) { body.OnHit += OnHitInternal; body.OnDeath += OnDeathInternal; }
        }
        protected virtual void OnDisable()
        {
            WorldEvents.OnNoise -= OnNoise;
            if (body) { body.OnHit -= OnHitInternal; body.OnDeath -= OnDeathInternal; }
        }

        public const int RoadArea = 3, SidewalkArea = 4; public const float RoadCost = 60f, OpenCost = 5f;   // lawns / yards / interiors cost 5, sidewalks 1, mid-block road 14

        protected virtual void Start()
        {
            if (!graph) graph = FindAnyObjectByType<RoadGraph>();
            agent.SetAreaCost(RoadArea, RoadCost); agent.SetAreaCost(0, OpenCost); agent.SetAreaCost(SidewalkArea, 1f);
            if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var h, 4f, NavMesh.AllAreas)) agent.Warp(h.position);
            thinkT = Random.Range(0f, 0.2f); lastThink = Time.time;
            OnSpawned();
        }

        protected abstract void OnSpawned();
        protected abstract void Think();
        protected virtual void OnNoise(Vector3 pos, float radius, GameObject src, bool melee) { }
        protected virtual void OnHit(string zone, Vector3 point) { }
        /// <summary>Called when the agent has had a path for a while but isn't getting anywhere. Default: try once more, then give up the walk.</summary>
        protected virtual void OnStuck(int times)
        {
            if (!agent.enabled || !agent.isOnNavMesh) return;
            if (times <= 2) { Vector3 d = agent.destination; Vector3 side = Quaternion.Euler(0f, Random.value < 0.5f ? 90f : -90f, 0f) * transform.forward; if (NavMesh.SamplePosition(transform.position + side * 1.2f, out var h, 1.5f, NavMesh.AllAreas)) agent.Warp(h.position); agent.SetDestination(d); }
            else Stop();
        }
        /// <summary>A car is honking at us from `carPos`: step off the road. Default: hop 4 m to the side away from the car's path.</summary>
        public virtual void Honk(Vector3 carPos, Vector3 carForward)
        {
            if (dummy || State == "dead" || !agent || !agent.enabled) return;
            Vector3 side = Vector3.Cross(Vector3.up, carForward).normalized; Vector3 rel = transform.position - carPos;
            if (Vector3.Dot(rel, side) < 0f) side = -side;
            Vector3 tgt = transform.position + side * 4f + carForward * 1.5f; if (OffRoadPoint(tgt, 14f, out var safe)) tgt = safe;
            GoTo(tgt, runSpeed);
        }
        protected virtual void OnDied(string by) { }
        protected virtual Vector3 RespawnPoint() => spawnPos;
        /// <summary>How often Think runs. Brains whose timers count 0.2 s per call keep the default.</summary>
        protected virtual float ThinkInterval => 0.2f;

        void OnHitInternal(CharacterBody b, string zone, Vector3 point) { if (!b.Dead) OnHit(zone, point); }
        void OnDeathInternal(CharacterBody b, string by)
        {
            State = "dead"; respawnT = 1e9f;   // the dead stay dead until the next morning (ReviveAll, when the player sleeps)
            if (agent.enabled) { agent.isStopped = true; agent.enabled = false; }
            if (capsule) capsule.enabled = false;
            ShowGun(false); pose = ArmPose.None; poseW = 0f; headTarget = null;
            WorldEvents.OnCharacterDied?.Invoke(b, by);
            OnDied(by);
        }

        /// <summary>Morning after sleeping: everybody who died during the day comes back.</summary>
        public static void ReviveAll() { foreach (var n in FindObjectsByType<NpcBrain>(FindObjectsSortMode.None)) if (n && n.Dead && n.displayName != "Joshua") n.respawnT = 0f; }

        protected void SetState(string s) { if (State != s) { State = s; stateT = 0f; Stat(GetType().Name.Replace("Brain", "") + "." + s); } }

        float stuckT; int stuckTimes;
        protected virtual void Update()
        {
            float dt = Time.deltaTime; stateT += dt; fireCd -= dt; if (glanceT > 0f) glanceT -= dt; if (glanceCd > 0f) glanceCd -= dt;
            if (body && body.Dead)
            {
                respawnT -= dt;
                if (respawnT <= 0f) Respawn();
                return;
            }
            if (agent.enabled && !agent.isOnNavMesh)
            {
                // a carving car (or a ragdoll pile) ate the navmesh under us: hop to the nearest walkable spot instead of freezing
                offMeshT += dt;
                if (offMeshT > 0.3f) { Vector3 wp; if (OffRoadPoint(transform.position, 3.5f, out wp) || NavMesh.SamplePosition(transform.position, out var nh, 3f, NavMesh.AllAreas) && (wp = nh.position) == nh.position) { agent.Warp(wp); offMeshT = 0f; } }
            }
            else offMeshT = 0f;
            if (!agent.enabled || !agent.isOnNavMesh) return;
            if (dummy) { agent.isStopped = true; anim.Play(idleClip); return; }
            if (body && body.Stun > 0f) { agent.isStopped = true; return; }
            // stuck: a path, somewhere to go, and no progress
            if (agent.hasPath && !agent.isStopped && !agent.pathPending && agent.remainingDistance > agent.stoppingDistance + 0.6f && agent.velocity.sqrMagnitude < 0.03f) { stuckT += dt; if (stuckT > 2.2f) { stuckT = 0f; stuckTimes++; Stat("stuck"); OnStuck(stuckTimes); } }
            else { stuckT = 0f; if (agent.velocity.sqrMagnitude > 0.5f) stuckTimes = 0; }
            thinkT -= dt; if (thinkT <= 0f) { thinkDt = Mathf.Clamp(Time.time - lastThink, 0.02f, 1f); lastThink = Time.time; thinkT = ThinkInterval; Think(); }
            if (AllowGlance && glanceCd <= 0f) PickGlance();
            Animate(dt);
        }

        protected float gaitScale = 1f;   // everybody walks a little differently
        /// <summary>A stable number per person (who leads a pair, which side of a target to take).</summary>
        protected int uid; static int uidCounter;
        protected virtual void Animate(float dt)
        {
            Vector3 v = agent.velocity; v.y = 0f; float speed = v.magnitude;
            float limp = body && body.InjuryLeg > 0 ? 0.7f : 1f;
            if (speed > 0.2f)
            {
                bool run = speed > 3.3f;
                Vector3 face = v;
                if (faceWhileMoving.HasValue) { face = faceWhileMoving.Value - transform.position; face.y = 0f; if (face.sqrMagnitude < 0.01f) face = v; }
                // gentle turns while strolling, sharper when running; turning on the spot at walking pace looks robotic
                float rate = faceWhileMoving.HasValue ? 420f : run ? 540f : 260f;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), rate * dt);
                string clip = run ? "Run" : WalkClip();
                anim.Play(clip, limp * (run ? Mathf.Clamp(speed / 5.6f, 0.8f, 1.3f) : Mathf.Clamp(speed / 2.0f * gaitScale, 0.7f, 1.4f)));
            }
            else
            {
                Vector3? face = lookAt ?? faceWhileMoving;
                if (face.HasValue) { Vector3 d = face.Value - transform.position; d.y = 0f; if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 220f * dt); }
                anim.Play(IdleClipNow());
            }
        }
        /// <summary>The walk cycle that goes with what is in their hands.</summary>
        protected string WalkClip()
        {
            if (!GunShown || !gunKind.HasValue) return "Walk";
            switch (gunKind.Value) { case WeaponKind.Rifle: return "Rifle_Walk"; case WeaponKind.Shotgun: return "Shotgun_Walk"; case WeaponKind.Knife: return "Knife_Walk"; case WeaponKind.Bat: return "Bat_Walk"; default: return "Pistol_Walk"; }
        }
        string IdleClipNow()
        {
            if (idleClip == "Pistol_Aim" && gunKind.HasValue) return AimClip;
            if (idleClip == "Idle" && GunShown && gunKind.HasValue) { if (gunKind == WeaponKind.Bat) return "Bat_Idle"; if (gunKind == WeaponKind.Knife) return "Knife_Idle"; }
            return idleClip;
        }
        protected string AimClip => gunKind == WeaponKind.Rifle ? "Rifle_Aim" : gunKind == WeaponKind.Shotgun ? "Shotgun_Aim" : "Pistol_Aim";
        string FireClip => gunKind == WeaponKind.Rifle ? "Rifle_Fire" : gunKind == WeaponKind.Shotgun ? "Shotgun_Fire" : "Pistol_Fire";

        // ------------------------------------------------------------ movement helpers
        protected bool GoTo(Vector3 p, float speed, float sampleRadius = 4f)
        {
            if (!agent.enabled || !agent.isOnNavMesh) return false;
            if (!NavMesh.SamplePosition(p, out var h, sampleRadius, NavMesh.AllAreas)) return false;
            agent.speed = speed * (body && body.InjuryLeg > 0 ? 0.55f : 1f); agent.isStopped = false; lookAt = null;
            // walking NPCs keep to the sidewalks and cross at the junctions (road area 3 is expensive); anyone running
            // (fleeing, chasing, rushing to a scene) cuts straight across
            bool running = speed >= runSpeed * 0.8f;
            agent.acceleration = running ? 12f : 4.5f;   // nobody goes from standing to full stride in a single step
            if (!running && (h.mask & (1 << RoadArea)) != 0 && OffRoadPoint(h.position, 12f, out var offRoad)) h.position = offRoad;   // strollers never aim for the middle of the road
            agent.SetAreaCost(RoadArea, running ? 1f : RoadCost); agent.SetAreaCost(0, running ? 1f : OpenCost); agent.SetAreaCost(SidewalkArea, 1f);
            return agent.SetDestination(h.position);
        }
        /// <summary>Is the NPC standing on the carriageway (road area, not a crossing at a junction)?</summary>
        protected bool OnRoad()
        {
            return agent && agent.enabled && NavMesh.SamplePosition(transform.position, out var h, 1.2f, NavMesh.AllAreas) && (h.mask & (1 << RoadArea)) != 0;
        }
        /// <summary>Nearest walkable spot that is not carriageway (sidewalk, lawn, junction crossing).</summary>
        protected bool OffRoadPoint(Vector3 from, float radius, out Vector3 res)
        {
            var f = new NavMeshQueryFilter { agentTypeID = agent ? agent.agentTypeID : 0, areaMask = NavMesh.AllAreas & ~(1 << RoadArea) };
            if (NavMesh.SamplePosition(from, out var h, radius, f)) { res = h.position; return true; }
            res = from; return false;
        }
        protected void Stop() { if (agent.enabled && agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); } }
        protected bool Arrived(float d = 0.7f) => !agent.pathPending && (!agent.hasPath || agent.remainingDistance <= Mathf.Max(d, agent.stoppingDistance + 0.05f));
        protected bool Moving => agent.enabled && agent.velocity.sqrMagnitude > 0.04f;

        protected Vector3 RandomPedNode(Vector3 near, float maxDist)
        {
            if (!graph || graph.pedNodes.Length == 0) return near + Random.insideUnitSphere * 10f;
            for (int i = 0; i < 16; i++) { var n = graph.pedNodes[Random.Range(0, graph.pedNodes.Length)]; if ((n.pos - near).sqrMagnitude < maxDist * maxDist && !(NavMesh.SamplePosition(n.pos, out var nh, 1.5f, NavMesh.AllAreas) && (nh.mask & (1 << RoadArea)) != 0)) return n.pos; }
            return graph.pedNodes[Random.Range(0, graph.pedNodes.Length)].pos;
        }

        /// <summary>A reachable point roughly `dist` metres away from `threat`, preferring directions away from it.</summary>
        protected Vector3? EscapePoint(Vector3 threat, float dist, bool outOfSight)
        {
            Vector3 away = transform.position - threat; away.y = 0f; if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0f; away.Normalize();
            Vector3? best = null; float bestScore = -1f;
            for (int i = 0; i < 10; i++)
            {
                float ang = Random.Range(-100f, 100f); Vector3 dir = Quaternion.Euler(0f, ang, 0f) * away;
                Vector3 cand = transform.position + dir * dist * Random.Range(0.7f, 1.2f);
                if (!NavMesh.SamplePosition(cand, out var h, 5f, NavMesh.AllAreas)) continue;
                float score = Vector3.Distance(h.position, threat) + (outOfSight && !Perception.Clear(threat + Vector3.up * 1.6f, h.position + Vector3.up * 1.2f) ? 25f : 0f);
                if (score > bestScore) { bestScore = score; best = h.position; }
            }
            return best;
        }

        // ------------------------------------------------------------ perception helpers
        public bool SeesPlayer(float range, float fov = 120f) => CanSeePlayer(range, fov);
        protected bool CanSeePlayer(float range, float fov = 120f) => PlayerBody && !PlayerBody.Dead && Perception.CanSee(Eye, transform.forward, PlayerBody, range, fov);
        protected bool CanSee(CharacterBody b, float range, float fov = 120f) => b && Perception.CanSee(Eye, transform.forward, b, range, fov);
        /// <summary>The player has a firearm raised (not necessarily at us).</summary>
        protected static bool PlayerGunUp { get { var pc = Player ? Player.GetComponent<PlayerCombat>() : null; return pc && pc.GunRaised; } }
        /// <summary>The player is aiming a firearm at us, within `range`.</summary>
        protected bool PlayerAimingAtMe(float range)
        {
            if (!PlayerGunUp) return false;
            Vector3 to = transform.position - Player.transform.position; to.y = 0f; float d = to.magnitude; if (d > range) return false;
            return Vector3.Dot(Player.transform.forward, to / Mathf.Max(0.01f, d)) > 0.8f;
        }

        // ------------------------------------------------------------ talking
        /// <summary>Say a line in a speech bubble (and, for lines that matter, in the message feed).</summary>
        protected void Bark(string text, Color? col = null, float dur = 2.8f, bool feed = false, float range = 26f)
        {
            Barks.Say(transform, text, dur, col, range);
            if (feed) GameManager.I?.Say($"{displayName}: {text}");
        }

        // ------------------------------------------------------------ the player talking to us (Y greet, U heckle)
        /// <summary>How many times the player has needled this person lately (cools off after a minute).</summary>
        protected int heckled; protected float heckleAt = -99f;
        protected int HeckleCount { get { if (Time.time - heckleAt > 60f) heckled = 0; return heckled; } }
        /// <summary>The player said something to us: rude = a jibe, otherwise a greeting. Default: turn the head, answer.</summary>
        public virtual void HearPlayer(bool rude)
        {
            if (Dead || !Player) return;
            if (rude) { _ = HeckleCount; heckled++; heckleAt = Time.time; }
            headTarget = Player.transform.position + Vector3.up * 1.5f; CancelInvoke(nameof(ForgetPlayerHead)); Invoke(nameof(ForgetPlayerHead), 3f);
            ReplyLater(rude ? Barks.Pick("Hey!", "Whatever.", "Real nice.") : Barks.Pick("Hi.", "Hey.", "Hello."));
        }
        protected void ForgetPlayerHead() { if (Player && headTarget.HasValue && (headTarget.Value - Player.transform.position).sqrMagnitude < 9f) headTarget = null; }
        /// <summary>Answer after a short beat, so it reads like a conversation.</summary>
        protected void ReplyLater(string text, float delay = 0.8f, Color? col = null, bool wave = false) { if (isActiveAndEnabled) StartCoroutine(Reply(text, delay, col, wave)); }
        System.Collections.IEnumerator Reply(string text, float delay, Color? col, bool wave)
        {
            yield return new WaitForSeconds(delay);
            if (Dead) yield break;
            if (wave && !anim.UpperBusy) anim.PlayUpper("Wave");
            Bark(text, col, 2.6f);
        }

        // ------------------------------------------------------------ counters for the NPC report (admin)
        public static readonly Dictionary<string, int> Stats = new Dictionary<string, int>();
        public static void Stat(string key) { Stats.TryGetValue(key, out var n); Stats[key] = n + 1; }

        // ------------------------------------------------------------ incidents (shared memory of the street)
        public static readonly List<Incident> Incidents = new List<Incident>();
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void HookIncidents()
        {
            Incidents.Clear(); if (hooked) return; hooked = true;
            WorldEvents.OnNoise += (pos, radius, src, melee) => { if (melee || radius < 18f) return; var gm = GameManager.I; bool byP = gm && gm.player && src == gm.player.gameObject; AddIncident(pos, "shots", byP); };
            WorldEvents.OnCharacterDied += (b, by) => { if (b) AddIncident(b.transform.position, b.isPlayer ? "player down" : "body", by == "player"); };
        }
        public static Incident AddIncident(Vector3 pos, string kind, bool byPlayer)
        {
            Incidents.RemoveAll(i => i.Age > 300f);
            foreach (var i in Incidents) if (i.kind == kind && i.Age < 15f && (i.pos - pos).sqrMagnitude < 18f * 18f) { i.time = Time.time; i.byPlayer |= byPlayer; return i; }
            var n = new Incident { pos = pos, time = Time.time, byPlayer = byPlayer, kind = kind }; Incidents.Add(n); if (Incidents.Count > 30) Incidents.RemoveAt(0); return n;
        }
        public static Incident NearestIncident(Vector3 p, float radius, float maxAge, string kind = null)
        {
            Incident best = null; float bd = radius * radius;
            foreach (var i in Incidents) { if (i.Age > maxAge || (kind != null && i.kind != kind)) continue; float d = (i.pos - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        // ------------------------------------------------------------ head, eyes and arms
        /// <summary>The brain wants to look at this (head and neck turn, the body doesn't). Overrides the casual glances.</summary>
        protected Vector3? headTarget;
        /// <summary>Head down at a phone / the ground.</summary>
        protected bool headDown;
        /// <summary>Nodding along while listening (0 = still).</summary>
        protected float nod;
        protected ArmPose pose; protected Vector3 pointAt;
        ArmPose poseShown; float poseW, yawCur, pitchCur; Transform glanceAt; float glanceT, glanceCd;
        Transform[] bones; Quaternion[] baseLocal, written; bool bonesTried; Renderer vis;
        const int B_Spine2 = 0, B_Neck = 1, B_Head = 2, B_LArm = 3, B_LFore = 4, B_LHand = 5, B_RArm = 6, B_RFore = 7, B_RHand = 8;
        static readonly string[] BoneNames = { "Spine2", "Neck", "Head", "LeftArm", "LeftForeArm", "LeftHand", "RightArm", "RightForeArm", "RightHand" };

        /// <summary>May the eyes wander to passers-by right now? (Not while fleeing or fighting.)</summary>
        protected virtual bool AllowGlance => false;
        /// <summary>Pick something to glance at: the player when close (longer if they are running, armed or right in our face), otherwise someone passing.</summary>
        void PickGlance()
        {
            glanceCd = Random.Range(0.8f, 2.2f);
            if (CamDist(transform.position) > 35f) return;
            Vector3 me = transform.position, fwd = transform.forward; Transform best = null; float bestW = 0f;
            var p = Player;
            if (p && !PlayerInVehicle)
            {
                Vector3 to = p.transform.position - me; float d = to.magnitude;
                if (d < 10f && Vector3.Dot(fwd, to / Mathf.Max(0.01f, d)) > -0.3f)
                {
                    float w = 1.5f + (d < 3.5f ? 3f : 0f) + (p.IsRunning && p.Moving ? 1.5f : 0f) + (PlayerGunUp ? 4f : 0f);
                    if (w > bestW && Perception.Clear(Eye, p.transform.position + Vector3.up * 1.5f)) { bestW = w; best = p.transform; }
                }
            }
            if (bestW < 3f)
                foreach (var b in CharacterBody.All)
                {
                    if (!b || b == body || b.isPlayer || b.Dead) continue; Vector3 to = b.transform.position - me; float d2 = to.sqrMagnitude; if (d2 > 36f || d2 < 0.25f) continue;
                    if (Vector3.Dot(fwd, to) < 0f) continue; float w = Random.value * 1.4f; if (w > bestW) { bestW = w; best = b.transform; }
                }
            if (best && Random.value < 0.75f) { glanceAt = best; glanceT = best == (p ? p.transform : null) ? Random.Range(1.5f, 3.2f) : Random.Range(0.8f, 1.8f); glanceCd = glanceT + Random.Range(1.5f, 4.5f); }
            else if (Random.value < 0.3f) { glanceAt = null; glanceT = Random.Range(1f, 2f); glancePt = me + Quaternion.Euler(0f, Random.Range(-70f, 70f), 0f) * fwd * 6f + Vector3.up * Random.Range(0.8f, 2.2f); glanceCd = glanceT + Random.Range(1f, 3f); }
        }
        Vector3 glancePt;

        void FindBones()
        {
            bonesTried = true; bones = new Transform[BoneNames.Length]; baseLocal = new Quaternion[BoneNames.Length]; written = new Quaternion[BoneNames.Length];
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name; int c = n.LastIndexOf(':'); if (c >= 0) n = n.Substring(c + 1);
                for (int i = 0; i < BoneNames.Length; i++) if (bones[i] == null && n == BoneNames[i]) bones[i] = t;
            }
            vis = GetComponentInChildren<SkinnedMeshRenderer>();
        }

        protected virtual void LateUpdate()
        {
            if (!bonesTried) FindBones();
            if (body && (body.Dead || body.Ragdolled)) return;
            if (bones[B_Head] == null) return;
            float dt = Time.deltaTime; float cd = CamDist(transform.position);
            bool show = cd < 45f && (!vis || vis.isVisible);
            // a bone the clip didn't overwrite since last frame still carries our offset: put it back first, so offsets never pile up
            for (int i = 0; i < bones.Length; i++) { var b = bones[i]; if (!b) continue; if (written[i] == b.localRotation && baseLocal[i] != written[i]) b.localRotation = baseLocal[i]; baseLocal[i] = b.localRotation; }
            if (!show) { yawCur = pitchCur = 0f; poseW = 0f; for (int i = 0; i < bones.Length; i++) if (bones[i]) written[i] = bones[i].localRotation; return; }

            Vector3 up = transform.up, right = transform.right, fwd = transform.forward;
            // ---- where the head wants to point
            Vector3? tgt = headTarget;
            if (!tgt.HasValue && glanceT > 0f) tgt = glanceAt ? glanceAt.position + Vector3.up * 1.55f : glancePt;
            float wantYaw = 0f, wantPitch = 0f;
            if (tgt.HasValue)
            {
                Vector3 local = Quaternion.Inverse(transform.rotation) * (tgt.Value - bones[B_Head].position);
                float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, horiz = new Vector2(local.x, local.z).magnitude;
                if (Mathf.Abs(yaw) < 115f) { wantYaw = Mathf.Clamp(yaw, -75f, 75f); wantPitch = Mathf.Clamp(-Mathf.Atan2(local.y, Mathf.Max(0.2f, horiz)) * Mathf.Rad2Deg, -28f, 35f); }
            }
            if (headDown) { wantPitch = 32f; wantYaw *= 0.2f; }
            if (nod > 0f) wantPitch += Mathf.Sin(Time.time * 6.5f) * 5f * nod;
            float k = 1f - Mathf.Exp(-6f * dt); yawCur = Mathf.Lerp(yawCur, wantYaw, k); pitchCur = Mathf.Lerp(pitchCur, wantPitch, k);

            // ---- spine takes a little of the turn, then the arms (world-space targets), then neck and head
            if (bones[B_Spine2] && !anim.UpperBusy) bones[B_Spine2].rotation = Quaternion.AngleAxis(yawCur * 0.22f, up) * bones[B_Spine2].rotation;
            ApplyPose(dt, up, right, fwd);
            if (bones[B_Neck]) bones[B_Neck].rotation = Quaternion.AngleAxis(yawCur * 0.3f, up) * Quaternion.AngleAxis(pitchCur * 0.3f, right) * bones[B_Neck].rotation;
            bones[B_Head].rotation = Quaternion.AngleAxis(yawCur * 0.48f, up) * Quaternion.AngleAxis(pitchCur * 0.7f, right) * bones[B_Head].rotation;

            for (int i = 0; i < bones.Length; i++) if (bones[i]) written[i] = bones[i].localRotation;
        }

        void ApplyPose(float dt, Vector3 U, Vector3 R, Vector3 F)
        {
            ArmPose want = pose;
            bool busy = anim.UpperBusy || (GunShown && want != ArmPose.HandsUp && want != ArmPose.Cower && want != ArmPose.Point);
            if (busy) want = ArmPose.None;
            if (want != poseShown) { poseW = Mathf.MoveTowards(poseW, 0f, dt * 5f); if (poseW <= 0f) poseShown = want; }
            else poseW = Mathf.MoveTowards(poseW, want == ArmPose.None ? 0f : 1f, dt * 4f);
            if (poseShown == ArmPose.None || poseW <= 0.001f) return;
            if (!bones[B_LArm] || !bones[B_LFore] || !bones[B_LHand] || !bones[B_RArm] || !bones[B_RFore] || !bones[B_RHand]) return;
            float w = poseW * poseW * (3f - 2f * poseW);
            var head = bones[B_Head];
            switch (poseShown)
            {
                case ArmPose.HandsUp:
                    Arm(B_LArm, B_LFore, B_LHand, -R * 0.9f + U * 0.35f + F * 0.12f, U * 0.95f + F * 0.08f - R * 0.1f, w);
                    Arm(B_RArm, B_RFore, B_RHand, R * 0.9f + U * 0.35f + F * 0.12f, U * 0.95f + F * 0.08f + R * 0.1f, w);
                    break;
                case ArmPose.Cower:
                    Arm(B_LArm, B_LFore, B_LHand, F * 0.55f + U * 0.55f - R * 0.45f, head ? head.position + U * 0.12f - bones[B_LFore].position : U, w);
                    Arm(B_RArm, B_RFore, B_RHand, F * 0.55f + U * 0.55f + R * 0.45f, head ? head.position + U * 0.12f - bones[B_RFore].position : U, w);
                    break;
                case ArmPose.PhoneCall:
                    if (bones[B_RFore]) Arm(B_RArm, B_RFore, B_RHand, -U * 0.55f + R * 0.35f + F * 0.38f, head ? head.position + R * 0.1f + U * 0.03f - bones[B_RFore].position : U, w);
                    break;
                case ArmPose.PhoneText:
                    Arm(B_LArm, B_LFore, B_LHand, -U * 0.9f + F * 0.28f - R * 0.08f, F * 0.85f + U * 0.38f + R * 0.3f, w);
                    Arm(B_RArm, B_RFore, B_RHand, -U * 0.9f + F * 0.28f + R * 0.08f, F * 0.85f + U * 0.38f - R * 0.3f, w);
                    break;
                case ArmPose.Point:
                    if (bones[B_RArm]) { Vector3 d = pointAt - bones[B_RArm].position; if (d.sqrMagnitude < 0.01f) d = F; d.Normalize(); if (Vector3.Dot(d, F) < -0.2f) d = Vector3.Slerp(F, d, 0.4f); Arm(B_RArm, B_RFore, B_RHand, d, d, w); }
                    break;
                case ArmPose.Crossed:
                    Arm(B_LArm, B_LFore, B_LHand, -U * 0.8f + F * 0.38f - R * 0.16f, R * 0.95f + U * 0.18f + F * 0.12f, w);
                    Arm(B_RArm, B_RFore, B_RHand, -U * 0.8f + F * 0.42f + R * 0.16f, -R * 0.95f + U * 0.22f + F * 0.14f, w);
                    break;
                case ArmPose.Hips:
                    Arm(B_LArm, B_LFore, B_LHand, -U * 0.72f - R * 0.6f - F * 0.08f, R * 0.62f - U * 0.55f + F * 0.18f, w);
                    Arm(B_RArm, B_RFore, B_RHand, -U * 0.72f + R * 0.6f - F * 0.08f, -R * 0.62f - U * 0.55f + F * 0.18f, w);
                    break;
            }
        }
        /// <summary>Swing the upper arm and the forearm towards the given world directions (blended by w). Works whatever the rig's bone axes are.</summary>
        void Arm(int upper, int fore, int hand, Vector3 upperDir, Vector3 foreDir, float w)
        {
            Transform u = bones[upper], f = bones[fore], h = bones[hand]; if (!u || !f || !h) return;
            Vector3 cur = f.position - u.position; if (cur.sqrMagnitude < 1e-6f) return; cur.Normalize();
            u.rotation = Quaternion.FromToRotation(cur, Vector3.Slerp(cur, upperDir.normalized, w)) * u.rotation;
            cur = h.position - f.position; if (cur.sqrMagnitude < 1e-6f) return; cur.Normalize();
            f.rotation = Quaternion.FromToRotation(cur, Vector3.Slerp(cur, foreDir.normalized, w)) * f.rotation;
        }

        /// <summary>Admin: freeze this person in an arm pose, looking at `look` (null = release).</summary>
        public void DebugPose(ArmPose? p, Vector3? look)
        {
            if (p.HasValue) { dummy = true; pose = p.Value; headTarget = look; lookAt = look; headDown = p.Value == ArmPose.PhoneText; if (p.Value == ArmPose.Point && look.HasValue) pointAt = look.Value; }
            else { dummy = false; pose = ArmPose.None; headTarget = null; headDown = false; }
        }

        // ------------------------------------------------------------ respawn
        protected virtual void Respawn()
        {
            Vector3 p = RespawnPoint();
            if (NavMesh.SamplePosition(p, out var h, 6f, NavMesh.AllAreas)) p = h.position;
            body.Respawn(p + Vector3.up * 0.05f, spawnYaw);
            if (capsule) capsule.enabled = true;
            agent.enabled = true; agent.Warp(p); lookAt = null; faceWhileMoving = null; headTarget = null; pose = ArmPose.None;
            OnSpawned();
        }

        // ------------------------------------------------------------ weapons (police sidearm, gang bats, crew guns)
        protected void EquipGun(WeaponKind k, bool alwaysVisible)
        {
            if (gun) Destroy(gun);
            var hand = body ? body.RightHand : null;
            gun = WeaponModels.Build(k); gun.transform.SetParent(hand ? hand : transform, false); gun.transform.localPosition = new Vector3(0f, 0.06f, 0f) + gun.transform.localPosition;
            gunMuzzle = gun.transform.Find("Muzzle"); gun.SetActive(alwaysVisible); gunKind = k;
        }
        protected void ShowGun(bool on) { if (gun) gun.SetActive(on); }

        /// <summary>Fire the weapon at a body: hit chance scales with accuracy and distance (shotguns hit hard up close and badly far away); misses spark near the target.</summary>
        protected void ShootAt(CharacterBody target, float accuracy, string tag)
        {
            if (!target || fireCd > 0f) return;
            var k = gunKind ?? WeaponKind.Pistol;
            fireCd = (k == WeaponKind.Rifle ? 0.45f : k == WeaponKind.Shotgun ? 1.2f : k == WeaponKind.Revolver ? 1.0f : 0.8f) + Random.Range(0f, 0.35f);
            anim.PlayUpper(FireClip);
            Vector3 mz = gunMuzzle ? gunMuzzle.position : Eye;
            Vector3 aim = target.ChestPos + Random.insideUnitSphere * 0.25f; Vector3 dir = (aim - mz).normalized;
            CombatFx.I?.Muzzle(mz, dir, k == WeaponKind.Shotgun); Sfx.At(k == WeaponKind.Rifle ? "rifle" : k == WeaponKind.Shotgun ? "shotgun" : k == WeaponKind.Revolver ? "revolver" : "pistol", mz, 0.9f);
            float dist = Vector3.Distance(mz, aim);
            float range = k == WeaponKind.Shotgun ? Mathf.Clamp01(1.4f - dist / 14f) : k == WeaponKind.Rifle ? Mathf.Clamp01(1.3f - dist / 60f) : Mathf.Clamp01(1.25f - dist / 40f);
            bool hit = Random.value < accuracy * range && Perception.Clear(mz, aim);
            if (hit)
            {
                string zone = Random.value < 0.12f ? "Hit_Head" : Random.value < 0.6f ? "torso" : (Random.value < 0.5f ? "Hit_LegLeft" : "Hit_ArmRight");
                CombatFx.I?.Tracer(mz, aim); CombatFx.I?.Blood(aim, dir, 5);
                target.Hit(zone, aim, dir, 1, false, k == WeaponKind.Shotgun ? 2f : 1f, tag, false);
            }
            else
            {
                Vector3 end = aim + Random.insideUnitSphere * 1.2f + dir * 3f;
                if (Physics.Raycast(mz, (end - mz).normalized, out var h, 60f, ~((1 << CharacterBody.HitboxLayer) | (1 << CharacterBody.CharactersLayer)), QueryTriggerInteraction.Ignore)) { end = h.point; CombatFx.I?.Impact(h.point, h.normal, false); }
                CombatFx.I?.Tracer(mz, end);
            }
            WorldEvents.Shot(transform.position, 28f, gameObject);
        }

        /// <summary>A spot to step to while keeping the gun on someone: a couple of metres to the side, so a gunfight isn't two statues.</summary>
        protected bool StrafePoint(Vector3 target, out Vector3 p)
        {
            Vector3 to = target - transform.position; to.y = 0f; if (to.sqrMagnitude < 0.01f) to = transform.forward; to.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, to) * (Random.value < 0.5f ? 1f : -1f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 c = transform.position + side * Random.Range(2f, 3.5f) - to * Random.Range(-0.5f, 1f);
                if (NavMesh.SamplePosition(c, out var h, 1.5f, NavMesh.AllAreas) && Perception.Clear(h.position + Vector3.up * 1.5f, target + Vector3.up * 1.2f)) { p = h.position; return true; }
                side = -side;
            }
            p = transform.position; return false;
        }
    }
}
