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
        public bool[] owned = { true, true, true, true, true, true, true, true };   // all unlocked until the economy (step 5)

        public WeaponDef Weapon => WeaponDef.All[current];
        public bool Aiming { get; private set; }
        public bool Emoting { get; set; }
        public float carryRoll = 90f;              // degrees around the barrel while carried (the hand hangs palm-in, so the gun would lie flat)
        public System.Func<bool, bool, string> LocomotionFn => Locomotion;           // emote wheel: hide the weapon while waving / dancing
        float unlockedT;
        public float HitMarkerT { get; private set; }
        public bool HitMarkerHead { get; private set; }

        PlayerController pc; PlayerCamera cam; CharacterAnimator anim; CharacterBody body;
        Transform holder, handAnchor; bool fpView; Vector3 fpPos; readonly Dictionary<WeaponKind, GameObject> models = new Dictionary<WeaponKind, GameObject>();
        GameObject model; Transform muzzle;
        float cooldown, heat, aimHold;
        bool swinging;
        static readonly RaycastHit[] hits = new RaycastHit[32];
        static readonly Collider[] overlaps = new Collider[32];

        void Awake()
        {
            pc = GetComponent<PlayerController>(); anim = GetComponent<CharacterAnimator>(); body = GetComponent<CharacterBody>();
            if (owned == null || owned.Length < WeaponDef.Order.Length)   // the scene was saved with fewer weapons: new ones start unlocked
            { var o = new bool[WeaponDef.Order.Length]; for (int i = 0; i < o.Length; i++) o[i] = owned == null || i >= owned.Length || owned[i]; owned = o; }
            cam = FindAnyObjectByType<PlayerCamera>();
            pc.LocomotionOverride = Locomotion;
        }

        void Start()
        {
            EnsureHolder();
            foreach (var k in WeaponDef.Order) GetModel(k);
            Equip(current);
        }

        /// <summary>The hand anchor. Created on demand: EconomyManager may call Equip() before this component's Start().</summary>
        void EnsureHolder()
        {
            if (holder) return;
            if (!body) body = GetComponent<CharacterBody>();
            var hand = body ? body.RightHand : null;
            holder = new GameObject("WeaponHolder").transform; handAnchor = hand ? hand : transform;
            holder.SetParent(handAnchor, false);
            holder.localPosition = new Vector3(0f, 0.06f, 0f); holder.localRotation = Quaternion.identity;
        }

        GameObject GetModel(WeaponKind k)
        {
            EnsureHolder();
            if (models.TryGetValue(k, out var m) && m) { if (m.transform.parent != holder) m.transform.SetParent(holder, false); return m; }
            try
            {
                m = WeaponModels.Build(k); m.transform.SetParent(holder, false); m.SetActive(false);
                var rs = m.GetComponentsInChildren<Renderer>(true); foreach (var r in rs) r.gameObject.layer = gameObject.layer;
            }
            catch (System.Exception e) { Debug.LogError($"[MafiaSandbox] weapon model {k} failed: {e}"); m = new GameObject(k.ToString()); m.transform.SetParent(holder, false); }
            models[k] = m; return m;
        }

        public void Equip(WeaponKind k)
        {
            if (owned.Length < WeaponDef.Order.Length) Awake();
            if (!owned[System.Array.IndexOf(WeaponDef.Order, k)]) return;
            if (model) model.SetActive(false);
            current = k; model = GetModel(k); model.SetActive(true); muzzle = FindDeep(model.transform, "Muzzle"); whipJ = null;
            heat = 0f;
            if (cam) cam.AimFov = Weapon.aimFov;
        }

        static Transform FindDeep(Transform t, string n) { if (t.name == n) return t; for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), n); if (r) return r; } return null; }

        // ---------------------------------------------------------------- fists and whip
        Transform fpFists, fistL, fistR; float punchT = -9f; int punchN; float lastPunchT = -9f; bool punchLeft;
        Transform[] whipJ; float whipT = -9f;

        /// <summary>First-person fists: two skin-toned fists with a bit of sleeve, held up in a guard and thrown at the crosshair.</summary>
        void FpFists(bool show)
        {
            if (!show) { if (fpFists) fpFists.gameObject.SetActive(false); return; }
            if (!fpFists)
            {
                fpFists = new GameObject("FpFists").transform; fpFists.SetParent(cam.transform, false);
                Transform Fist(float side)
                {
                    var root = new GameObject(side > 0 ? "R" : "L").transform; root.SetParent(fpFists, false);
                    var f = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(f.GetComponent<Collider>()); f.name = "fist"; f.transform.SetParent(root, false); f.transform.localScale = new Vector3(0.07f, 0.062f, 0.082f);
                    var k = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(k.GetComponent<Collider>()); k.name = "thumb"; k.transform.SetParent(root, false); k.transform.localScale = new Vector3(0.024f, 0.024f, 0.05f); k.transform.localPosition = new Vector3(-side * 0.036f, -0.012f, 0.012f);
                    var a = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(a.GetComponent<Collider>()); a.name = "sleeve"; a.transform.SetParent(root, false); a.transform.localScale = new Vector3(0.068f, 0.066f, 0.32f); a.transform.localPosition = new Vector3(0f, -0.008f, -0.2f);
                    foreach (var r in root.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.gameObject.layer = gameObject.layer; }
                    return root;
                }
                fistL = Fist(-1f); fistR = Fist(1f);
            }
            fpFists.gameObject.SetActive(true);
            var look = PlayerAppearance.I ? PlayerAppearance.I.Current : null;
            Color skin = look != null ? look.skin : new Color(0.9f, 0.7f, 0.55f), sleeve = look != null ? look.topCol : new Color(0.8f, 0.12f, 0.1f);
            foreach (var f in new[] { fistL, fistR })
            {
                foreach (var r in f.GetComponentsInChildren<Renderer>()) { var c = r.name == "sleeve" ? sleeve : skin; if (r.material.color != c) r.material.color = c; }
            }
            float t = Time.time - punchT;
            float bob = pc.Moving ? Mathf.Sin(Time.time * 9f) * 0.008f : Mathf.Sin(Time.time * 2f) * 0.004f;
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0; var f = left ? fistL : fistR; float side = left ? -1f : 1f;
                Vector3 guard = new Vector3(side * 0.2f, -0.2f + bob, 0.5f);
                Quaternion rot = Quaternion.Euler(-8f, -side * 8f, side * 12f);
                bool active = left == punchLeft && t < 0.32f;
                if (active)
                {
                    bool hook = punchN % 3 == 0;
                    float u = t < 0.1f ? t / 0.1f : 1f - Mathf.Clamp01((t - 0.1f) / 0.22f); u = Mathf.SmoothStep(0f, 1f, u);
                    Vector3 hit = hook ? new Vector3(side * 0.03f, -0.08f, 0.66f) : new Vector3(side * 0.06f, -0.1f, 0.78f);
                    guard = Vector3.Lerp(guard, hit, u);
                    rot = Quaternion.Slerp(rot, hook ? Quaternion.Euler(0f, -side * 60f, side * 80f) : Quaternion.Euler(0f, -side * 4f, side * 70f), u);
                }
                f.localPosition = guard; f.localRotation = rot;
            }
        }

        /// <summary>The lash hangs and sways; a swing curls it back, then it snaps straight out and cracks.</summary>
        void AnimateWhip()
        {
            if (whipJ == null) { whipJ = new Transform[WeaponModels.WhipJoints]; for (int i = 0; i < whipJ.Length; i++) { whipJ[i] = FindDeep(model.transform, "J" + i); if (!whipJ[i]) { whipJ = new Transform[0]; break; } } }
            if (whipJ.Length == 0) return;
            float t = Time.time - whipT, w = Weapon.hitAt / 1.35f;
            for (int i = 0; i < whipJ.Length; i++)
            {
                float k = i / (float)(whipJ.Length - 1);
                float idle = -9f + Mathf.Sin(Time.time * 2.2f - i * 0.6f) * 3f * k;
                float a = idle;
                if (t < w) a = Mathf.Lerp(idle, 16f, Mathf.SmoothStep(0f, 1f, t / w));                     // curled back over the shoulder
                else if (t < w + 0.18f) { float u = (t - w) / 0.18f; a = Mathf.Lerp(16f, -3f, Mathf.Clamp01(u * 2.5f - k)) ; } // the wave runs down the lash
                else if (t < w + 0.6f) a = Mathf.Lerp(-3f, idle, (t - w - 0.18f) / 0.42f);
                whipJ[i].localRotation = Quaternion.Euler(a, 0f, 0f);
            }
        }

        string Locomotion(bool moving, bool running)
        {
            var w = Weapon;
            if (w.melee) return null;   // carried at the side like anything else; the swing clip plays on the upper body
            if (Aiming) return moving ? w.clipPrefix + "_Walk" : w.clipPrefix + "_Aim";
            return null;
        }

        /// <summary>
        /// First person: the body is hidden (shadows only) and the hand hangs below the view, so the weapon becomes a view model riding
        /// the camera, barrel forward (the model is built with +Y along the barrel and +Z up). It is shown at its real size (the
        /// third-person model is enlarged so the blocky hand doesn't swallow it), and placed from the model's own measurements:
        /// aiming, the top of the gun (the sights) sits just under the crosshair and the barrel points straight at it; at the hip it
        /// rests low on the right. The near clip plane is pulled in so the gun can sit close to the eye.
        /// </summary>
        readonly Dictionary<WeaponKind, Bounds> viewBounds = new Dictionary<WeaponKind, Bounds>();
        Camera viewCam; float baseNear = -1f, fpKick, bobT;
        /// <summary>The gun's extent in holder space (independent of how it is currently rotated), measured once per weapon.</summary>
        Bounds ViewBounds()
        {
            if (viewBounds.TryGetValue(current, out var b)) return b;
            bool init = false; b = new Bounds(Vector3.zero, Vector3.zero); var mt = model.transform;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(false))
            {
                if (!mf.sharedMesh) continue; var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 ml = mt.InverseTransformPoint(mf.transform.TransformPoint(c));
                    Vector3 hl = mt.localPosition + Vector3.Scale(mt.localScale, ml);
                    if (!init) { b = new Bounds(hl, Vector3.zero); init = true; } else b.Encapsulate(hl);
                }
            }
            if (!init) return new Bounds(new Vector3(0f, 0.1f, 0.03f), new Vector3(0.04f, 0.25f, 0.12f));   // not measurable this frame: try again next one
            viewBounds[current] = b; return b;
        }

        void LateUpdate()
        {
            if (!holder || !cam) return;
            if (!viewCam) { viewCam = cam.GetComponent<Camera>(); if (viewCam && baseNear < 0f) baseNear = viewCam.nearClipPlane; }
            bool fp = cam.FirstPerson;
            if (fp != fpView)
            {
                fpView = fp;
                holder.SetParent(fp ? cam.transform : handAnchor, false);
                if (!fp) { holder.localPosition = new Vector3(0f, 0.06f, 0f); holder.localRotation = Quaternion.identity; holder.localScale = Vector3.one; }
                if (viewCam && baseNear > 0f) viewCam.nearClipPlane = fp ? Mathf.Min(baseNear, 0.03f) : baseNear;
            }
            if (current == WeaponKind.Whip && model && model.activeSelf) AnimateWhip();
            bool fists = current == WeaponKind.Fists;
            FpFists(fp && fists && model && model.activeSelf);
            if (!fp || !model || fists) return;
            float sc = 1f / Mathf.Max(0.01f, model.transform.localScale.x);   // real size
            var b = ViewBounds();
            // holder-local p maps to camera space as (-p.x, p.z, p.y) * sc + holder position
            float len = b.size.y * sc; bool longGun = len > 0.45f;
            bool melee = Weapon.melee;
            Vector3 aim = new Vector3(b.center.x * sc, -0.008f - b.max.z * sc, (longGun ? 0.06f : 0.3f) - b.min.y * sc);
            Vector3 hip = melee ? new Vector3(0.24f + b.center.x * sc, -0.24f - b.center.z * sc, 0.3f - b.center.y * sc)
                                : new Vector3((longGun ? 0.3f : 0.27f) + b.center.x * sc, (longGun ? -0.2f : -0.2f) - b.max.z * sc, (longGun ? 0.08f : 0.3f) - b.min.y * sc);   // well over to the right and low, like other shooters
            bool whipW = current == WeaponKind.Whip;
            if (whipW) hip = new Vector3(0.2f, -0.17f, 0.36f);              // handle low on the right, lash hanging out of view
            var goal = Aiming ? aim : hip;
            fpPos = Vector3.Lerp(fpPos, goal, 1f - Mathf.Exp(-14f * Time.deltaTime));
            fpKick = Mathf.MoveTowards(fpKick, 0f, Time.deltaTime * 7f);
            // walking bob at the hip, recoil push-back on every shot
            float bob = (!Aiming && pc.Moving) ? 1f : 0f; bobT += Time.deltaTime * (bob > 0f ? 9f : 0f);
            Vector3 sway = new Vector3(Mathf.Sin(bobT) * 0.006f, -Mathf.Abs(Mathf.Cos(bobT)) * 0.006f, 0f) * bob;
            holder.localPosition = fpPos + sway + new Vector3(0f, 0.004f, -0.045f) * fpKick; holder.localScale = Vector3.one * sc;
            var rot = Quaternion.AngleAxis(180f, Vector3.forward) * Quaternion.AngleAxis(90f, Vector3.right);   // model +Y -> view forward, +Z -> view up
            if (!Aiming && !melee) rot = Quaternion.Euler(3f, longGun ? 2f : -4f, 0f) * rot;   // long guns point straight ahead from the hip instead of across the screen   // at the hip the muzzle angles in slightly, like a hand holding it
            if (melee) rot = (whipW ? Quaternion.Euler(Time.time - whipT < 0.6f ? 0f : 14f, -10f, 0f) : Quaternion.Euler(-25f, -15f, 10f)) * rot;   // the whip comes up level to crack at the crosshair
            if (fpKick > 0f) rot = Quaternion.Euler(-7f * fpKick, 0f, 0f) * rot;
            holder.localRotation = rot;
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;   // the camera hides the body's renderers, not the gun
        }

        void Update()
        {
            float dt = Time.deltaTime;

            cooldown -= dt; heat = Mathf.Max(0f, heat - dt * 1.2f); if (HitMarkerT > 0f) HitMarkerT -= dt;
            if (Cursor.lockState != CursorLockMode.Locked || UI.GameUI.Blocking) unlockedT = 0.35f; else unlockedT -= dt;   // the click that re-locks the cursor / closes a window must not fire the gun
            bool locked = pc.InputLocked || (body && body.Dead) || Cursor.lockState != CursorLockMode.Locked || unlockedT > 0f;

            if (!locked)
            {
                for (int i = 0; i < WeaponDef.Order.Length && i < 9; i++) if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Equip(WeaponDef.Order[i]);
            }

            bool wantAim = !locked && (Input.GetMouseButton(1) || aimHold > 0f);
            if (aimHold > 0f) aimHold -= dt;
            SetAiming(wantAim && !Weapon.melee);
            if (model)
            {
                model.SetActive(!pc.InputLocked && !(body && body.Dead) && !Emoting);   // the weapon stays in hand (lowered when not aiming)
                // carried guns hang with the barrel down and the slide upright; the aim pose keeps the model's native orientation
                bool raised = Aiming || Weapon.melee || cooldown > Weapon.rof - 0.35f || (cam && cam.FirstPerson);
                var target = raised ? Quaternion.identity : Quaternion.Euler(0f, carryRoll, 0f);
                model.transform.localRotation = Quaternion.Slerp(model.transform.localRotation, target, 1f - Mathf.Exp(-14f * dt));
            }

            // body faces the camera while aiming (and in first person)
            if (Aiming || swinging || (cam && cam.FirstPerson)) pc.FaceYaw = cam ? cam.Yaw : (float?)null; else pc.FaceYaw = null;

            if (locked) return;
            bool trigger = Weapon.auto ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
            if (trigger && cooldown <= 0f && !swinging)
            {
                if (Weapon.melee) StartCoroutine(Melee()); else Fire();
            }
        }

        float lastShotT = -99f;
        /// <summary>A gun is up as far as bystanders can tell: aimed, or just fired (hip fire in first person counts).</summary>
        public bool GunRaised => !Weapon.melee && (Aiming || Time.time - lastShotT < 3f);

        void SetAiming(bool on)
        {
            if (on == Aiming) return;
            Aiming = on; if (cam) cam.Aiming = on;
        }

        float CurrentSpread() => Weapon.spreadDeg * (1f + heat * 1.6f) * (pc.Moving ? 1.5f : 1f) * (pc.IsCrouching ? 0.7f : 1f);

        void Fire()
        {
            var w = Weapon; cooldown = w.rof * MafiaSandbox.Player.Highs.FireRate; lastShotT = Time.time;
            bool fpShot = cam && cam.FirstPerson;
            if (!Aiming && !fpShot) { SetAiming(true); aimHold = 0.5f; }   // third person raises the gun to shoot; first person fires from the hip
            if (fpShot) fpKick = 1f;
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
            float swingSpeed = 1.35f;   // the generated swing clips are slow; a faster, snappier swing reads better
            bool fists = w.kind == WeaponKind.Fists, whip = w.kind == WeaponKind.Whip, hook = false;
            int dmg = w.damage; float impulse = w.impulse, range = w.range;
            if (fists)
            {
                if (Time.time - lastPunchT > 1.1f) punchN = 0; punchN++; lastPunchT = Time.time; punchLeft = !punchLeft; punchT = Time.time;
                hook = punchN % 3 == 0;                                  // jab, jab, hook
                if (hook) { dmg = 2; impulse = 4f; cooldown = w.rof * 1.6f; }
                swingSpeed = hook ? 1.5f : 1.9f;
                anim.PlayUpper(hook ? "Bat_Attack" : "Knife_Attack", swingSpeed);
            }
            else anim.PlayUpper(w.clipPrefix + "_Attack", swingSpeed);
            if (whip) whipT = Time.time;
            yield return new WaitForSeconds((hook ? 0.3f : w.hitAt) / swingSpeed);
            swinging = false;
            Vector3 fwd = cam ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : transform.forward;
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            Vector3 tip = muzzle ? muzzle.position : origin + fwd;
            if (!fists) CombatFx.I?.Slash(tip, fwd);
            Sfx.At(whip ? "crack" : "swing", whip ? tip : origin, whip ? 0.9f : fists ? 0.35f : 0.6f);
            CharacterBody best = null; float bd = float.MaxValue;
            foreach (var b in CharacterBody.All)
            {
                if (b == body) continue;
                Vector3 to = b.ChestPos - origin; float d = to.magnitude; if (d > range + 0.4f) continue;
                if (Vector3.Dot(to / d, fwd) < w.arcCos) continue;
                if (d < bd) { bd = d; best = b; }
            }
            WorldEvents.Shot(transform.position, w.noise, gameObject, true);
            if (!best) yield break;
            Vector3 point = Vector3.Lerp(tip, best.ChestPos, 0.5f);
            string zone = best.ZoneNear(Vector3.Lerp(best.transform.position + Vector3.up * tip.y - Vector3.up * best.transform.position.y, tip, 0.35f));
            CombatFx.I?.Blood(point, fwd, w.kind == WeaponKind.Bat ? 5 : fists ? (hook ? 3 : 1) : whip ? 2 : 8);
            if (cam) cam.AddKick(fists ? (hook ? 0.7f : 0.3f) : 0.6f);
            bool killed = best.Hit(zone, point, (fwd + Vector3.up * 0.3f).normalized, dmg, MafiaSandbox.Player.Highs.Berserk, impulse / 2.5f * (MafiaSandbox.Player.Highs.Berserk ? 1.8f : 1f), "player", w.headKill);
            Sfx.At(w.kind == WeaponKind.Bat ? "thud" : fists ? "punch" : whip ? "crack" : "stab", point);
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
