using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Player;

namespace MafiaSandbox.Combat
{
    /// <summary>
    /// Player weapons: 1–6 select, right mouse aim, left mouse fire / swing. Hitscan bullets from the camera centre with spread,
    /// shotgun pellets, recoil, muzzle flash, tracers, melee arc checks — the prototype's fire()/melee() ported.
    /// Also raises <see cref="WorldEvents.Shot"/> so NPCs (step 3) can hear gunfire.
    /// </summary>
    public class PlayerCombat : MonoBehaviour
    {
        public WeaponKind current = WeaponKind.Pistol;
        public LayerMask shootMask;                 // set by CityBuilder: everything except Characters (the capsules) — hitboxes + world
        public bool[] owned = { true, true, true, true, true, true };   // all unlocked until the economy (step 5)

        public WeaponDef Weapon => WeaponDef.All[current];
        public bool Aiming { get; private set; }
        float unlockedT;
        public float HitMarkerT { get; private set; }
        public bool HitMarkerHead { get; private set; }

        PlayerController pc; PlayerCamera cam; CharacterAnimator anim; CharacterBody body;
        Transform holder; readonly Dictionary<WeaponKind, GameObject> models = new Dictionary<WeaponKind, GameObject>();
        GameObject model; Transform muzzle;
        float cooldown, heat, aimHold;
        bool swinging;
        static readonly RaycastHit[] hits = new RaycastHit[32];
        static readonly Collider[] overlaps = new Collider[32];

        void Awake()
        {
            pc = GetComponent<PlayerController>(); anim = GetComponent<CharacterAnimator>(); body = GetComponent<CharacterBody>();
            cam = FindAnyObjectByType<PlayerCamera>();
            pc.LocomotionOverride = Locomotion;
        }

        void Start()
        {
            var hand = body ? body.RightHand : null;
            holder = new GameObject("WeaponHolder").transform;
            holder.SetParent(hand ? hand : transform, false);
            holder.localPosition = new Vector3(0f, 0.06f, 0f); holder.localRotation = Quaternion.identity;
            foreach (var k in WeaponDef.Order) GetModel(k);
            Equip(current);
        }

        GameObject GetModel(WeaponKind k)
        {
            if (models.TryGetValue(k, out var m) && m) return m;
            try
            {
                m = WeaponModels.Build(k); m.transform.SetParent(holder, false); m.SetActive(false);
                foreach (var r in m.GetComponentsInChildren<Renderer>()) r.gameObject.layer = gameObject.layer;
            }
            catch (System.Exception e) { Debug.LogError($"[MafiaSandbox] weapon model {k} failed: {e}"); m = new GameObject(k.ToString()); m.transform.SetParent(holder, false); }
            models[k] = m; return m;
        }

        public void Equip(WeaponKind k)
        {
            if (!owned[System.Array.IndexOf(WeaponDef.Order, k)]) return;
            if (model) model.SetActive(false);
            current = k; model = GetModel(k); model.SetActive(true); muzzle = model.transform.Find("Muzzle");
            heat = 0f;
            if (cam) cam.AimFov = Weapon.aimFov;
        }

        string Locomotion(bool moving, bool running)
        {
            var w = Weapon;
            if (w.melee) return moving ? (running ? "Run" : w.clipPrefix + "_Walk") : w.clipPrefix + "_Idle";
            if (Aiming) return moving ? w.clipPrefix + "_Walk" : w.clipPrefix + "_Aim";
            return null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            cooldown -= dt; heat = Mathf.Max(0f, heat - dt * 1.2f); if (HitMarkerT > 0f) HitMarkerT -= dt;
            if (Cursor.lockState != CursorLockMode.Locked) unlockedT = 0.25f; else unlockedT -= dt;   // the click that re-locks the cursor must not fire the gun
            bool locked = pc.InputLocked || (body && body.Dead) || Cursor.lockState != CursorLockMode.Locked || unlockedT > 0f;

            if (!locked)
            {
                for (int i = 0; i < 6; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Equip(WeaponDef.Order[i]);
            }

            bool wantAim = !locked && (Input.GetMouseButton(1) || aimHold > 0f);
            if (aimHold > 0f) aimHold -= dt;
            SetAiming(wantAim && !Weapon.melee);
            if (model) model.SetActive(!pc.InputLocked && !(body && body.Dead) && (Aiming || Weapon.melee || cooldown > Weapon.rof - 0.35f));

            // body faces the camera while aiming (and in first person)
            if (Aiming || (cam && cam.FirstPerson)) pc.FaceYaw = cam ? cam.Yaw : (float?)null; else pc.FaceYaw = null;

            if (locked) return;
            bool trigger = Weapon.auto ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
            if (trigger && cooldown <= 0f && !swinging)
            {
                if (Weapon.melee) StartCoroutine(Melee()); else Fire();
            }
        }

        void SetAiming(bool on)
        {
            if (on == Aiming) return;
            Aiming = on; if (cam) cam.Aiming = on;
        }

        float CurrentSpread() => Weapon.spreadDeg * (1f + heat * 1.6f) * (pc.Moving ? 1.5f : 1f) * (pc.IsCrouching ? 0.7f : 1f);

        void Fire()
        {
            var w = Weapon; cooldown = w.rof;
            if (!Aiming) { SetAiming(true); aimHold = 0.5f; }
            anim.PlayUpper(w.clipPrefix + "_Fire");
            heat = Mathf.Min(1.2f, heat + (w.auto ? 0.16f : 0.45f));
            if (cam) cam.AddKick(w.kick);

            Vector3 mz = muzzle ? muzzle.position : transform.position + Vector3.up * 1.4f;
            Vector3 camPos = cam.transform.position, camFwd = cam.transform.forward;
            CombatFx.I?.Muzzle(mz, camFwd, w.kind == WeaponKind.Shotgun);
            Sfx.At(w.kind == WeaponKind.Pistol ? "pistol" : w.kind == WeaponKind.Revolver ? "revolver" : w.kind == WeaponKind.Rifle ? "rifle" : "shotgun", mz);
            if (w.kind == WeaponKind.Shotgun) StartCoroutine(Pump());

            float spread = CurrentSpread() * Mathf.Deg2Rad;
            var hitsBy = new Dictionary<CharacterBody, (int n, string zone, Vector3 point, Vector3 dir, float dist)>();
            bool anyHit = false, headHit = false;
            float minDist = Vector3.Distance(camPos, transform.position) * 0.45f;
            for (int p = 0; p < w.pellets; p++)
            {
                float r = Mathf.Sqrt(Random.value) * spread, a = Random.value * Mathf.PI * 2f;
                Vector3 right = Vector3.Cross(camFwd, Vector3.up).normalized, up = Vector3.Cross(right, camFwd);
                Vector3 dir = (camFwd + right * (Mathf.Cos(a) * Mathf.Tan(r)) + up * (Mathf.Sin(a) * Mathf.Tan(r))).normalized;
                Vector3 end = camPos + dir * 120f;
                int n = Physics.RaycastNonAlloc(camPos, dir, hits, 120f, shootMask, QueryTriggerInteraction.Ignore);
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    var h = hits[i]; if (h.distance < minDist) continue;
                    var hz = h.collider.GetComponent<HitZone>();
                    if (hz && hz.body == body) continue;                       // own hitboxes
                    if (h.collider.GetComponentInParent<PlayerCombat>() == this) continue;
                    if (h.distance < bd) { bd = h.distance; best = i; }
                }
                if (best >= 0)
                {
                    var h = hits[best]; end = h.point;
                    var hz = h.collider.GetComponent<HitZone>();
                    if (hz && hz.body)
                    {
                        anyHit = true; if (hz.zone == "Hit_Head") headHit = true;
                        hitsBy.TryGetValue(hz.body, out var e); if (e.n == 0) { e.point = h.point; e.dir = dir; e.dist = h.distance; e.zone = hz.zone; }
                        e.n++; if (hz.zone == "Hit_Head") e.zone = "Hit_Head"; hitsBy[hz.body] = e;
                        CombatFx.I?.Blood(h.point, dir, w.kind == WeaponKind.Shotgun ? 3 : 7);
                    }
                    else if (h.collider.GetComponentInParent<World.Animal>() is World.Animal an)
                    {
                        anyHit = true; an.Hit(h.point, dir);
                    }
                    else
                    {
                        bool metal = h.collider.GetComponentInParent<Vehicles.VehicleController>() != null;
                        CombatFx.I?.Impact(h.point, h.normal, metal);
                        if (p == 0) Sfx.At(metal ? "ricochet" : "impact", h.point, 0.6f);
                    }
                }
                CombatFx.I?.Tracer(mz, end);
            }
            foreach (var kv in hitsBy)
            {
                var e = kv.Value; bool lethal = w.kind == WeaponKind.Shotgun && (e.dist < w.killRange || e.n >= 4);
                int dmg = w.kind == WeaponKind.Shotgun ? Mathf.Max(1, Mathf.RoundToInt(e.n / 2f)) : w.damage;
                kv.Key.Hit(e.zone, e.point, e.dir, dmg, lethal, w.kind == WeaponKind.Shotgun ? 1.5f : 1f, "player");
            }
            if (anyHit) { HitMarkerT = 0.16f; HitMarkerHead = headHit; }
            WorldEvents.Shot(transform.position, w.noise, gameObject);
        }

        IEnumerator Pump()
        {
            var pump = model ? model.transform.Find("Pump") : null; if (!pump) yield break;
            Vector3 p0 = pump.localPosition; float t = 0f;
            yield return new WaitForSeconds(0.35f);
            Sfx.At("pump", transform.position, 0.5f);
            while (t < 0.55f) { t += Time.deltaTime; pump.localPosition = p0 - new Vector3(0f, 0.12f * Mathf.Sin(Mathf.Clamp01(t / 0.55f) * Mathf.PI), 0f); yield return null; }
            pump.localPosition = p0;
        }

        IEnumerator Melee()
        {
            var w = Weapon; cooldown = w.rof; swinging = true;
            anim.PlayUpper(w.clipPrefix + "_Attack");
            yield return new WaitForSeconds(w.hitAt);
            swinging = false;
            Vector3 fwd = cam ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : transform.forward;
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            Vector3 tip = muzzle ? muzzle.position : origin + fwd;
            CombatFx.I?.Slash(tip, fwd); Sfx.At("swing", origin, 0.6f);
            CharacterBody best = null; float bd = float.MaxValue;
            foreach (var b in CharacterBody.All)
            {
                if (b == body) continue;
                Vector3 to = b.ChestPos - origin; float d = to.magnitude; if (d > w.range + 0.4f) continue;
                if (Vector3.Dot(to / d, fwd) < w.arcCos) continue;
                if (d < bd) { bd = d; best = b; }
            }
            WorldEvents.Shot(transform.position, w.noise, gameObject, true);
            if (!best) yield break;
            Vector3 point = Vector3.Lerp(tip, best.ChestPos, 0.5f);
            string zone = best.ZoneNear(Vector3.Lerp(best.transform.position + Vector3.up * tip.y - Vector3.up * best.transform.position.y, tip, 0.35f));
            CombatFx.I?.Blood(point, fwd, w.kind == WeaponKind.Bat ? 5 : 8);
            if (cam) cam.AddKick(0.6f);
            bool killed = best.Hit(zone, point, (fwd + Vector3.up * 0.3f).normalized, w.damage, false, w.impulse / 2.5f, "player", w.headKill);
            Sfx.At(w.kind == WeaponKind.Bat ? "thud" : "stab", point);
            HitMarkerT = 0.16f; HitMarkerHead = zone == "Hit_Head";
        }
    }

    /// <summary>Global gameplay events other systems subscribe to (NPC hearing in step 3, wanted level in step 3/5).</summary>
    public static class WorldEvents
    {
        public static System.Action<Vector3, float, GameObject, bool> OnNoise;   // position, radius, source, isMelee
        public static System.Action<CharacterBody, string> OnCharacterDied;      // body, killer tag
        public static void Shot(Vector3 pos, float radius, GameObject src, bool melee = false) => OnNoise?.Invoke(pos, radius, src, melee);
    }
}
