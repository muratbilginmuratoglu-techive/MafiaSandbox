using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.UI;

namespace MafiaSandbox.Economy
{
    /// <summary>Test switches (F10 panel): free ingredients, no waiting, no rank locks.</summary>
    public static class Admin { public static bool On, Unlimited, Instant, UnlockAll; }

    /// <summary>
    /// The hands-on workshop: a 3D bench (built far below the city, shown by its own camera) where every product is made by hand.
    /// Parts are dragged from the tray onto their outline, wheels are cranked, bottles shaken, filled and labelled, powders ground.
    /// Sloppy work costs quality, and quality decides how much the batch yields.
    /// </summary>
    public class Workshop : MonoBehaviour
    {
        public static Workshop I { get; private set; }
        public static bool Active => I && I.active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (I) return; var go = new GameObject("WorkshopSystems"); DontDestroyOnLoad(go); go.AddComponent<Workshop>(); go.AddComponent<AdminPanel>(); }

        class RP { public Transform t; public Vector3 center, slot; public float fit = 1f, scale = 1f; public bool placed; }

        const float TableY = WorkshopModels.TableY;
        static readonly Vector3 BasePos = new Vector3(0f, -300f, 0f), CratePos = new Vector3(0.93f, TableY + 0.1f, -0.3f);
        bool active, result, direct; int recipe, mult, idx; float quality; WorkDef def; Transform root; Camera cam; readonly List<RP> parts = new List<RP>();
        GameObject ghost; string toast = ""; float toastT; float camYaw, camElev = 34f, camDist = 2.35f; Vector3 dragStart; float yaw0, elev0; bool orbit;
        // per-step state
        bool carry, grip; float turns, prevAng, level, flow, heatV, heatT, heatIn, clock, stepT, speedPen; int shakeI, shakeErr, drops, spilled, clicksLeft; int[] shakePat; Vector2 anchor;
        float tilt, dropMeter, pullL, pullR; int stretchDrag; bool creased, snapped; Transform stream, src; Vector3 srcHome; readonly List<(Transform t, float vy, bool ok)> falling = new List<(Transform, float, bool)>();
        readonly List<Transform> targets = new List<Transform>(), decoys = new List<Transform>(); readonly List<(Transform t, Vector3 p)> group = new List<(Transform, Vector3)>(); Vector3 groupC;
        float needle, skipCd; bool wheelTilt;

        void Awake() { I = this; Camera.onPreCull += PreCull; Camera.onPostRender += PostRender; }
        void OnDestroy() { Camera.onPreCull -= PreCull; Camera.onPostRender -= PostRender; }
        // the bench is lit by its own lamps only: the city's sun, sky light and fog are switched off while the workshop camera renders
        Light sun; bool sunWas, fogWas, swapped; Color ambWas; UnityEngine.Rendering.AmbientMode modeWas;
        void PreCull(Camera c)
        {
            if (!active || c != cam || swapped) return; swapped = true;
            if (!sun) { sun = RenderSettings.sun; if (!sun) { var g = GameObject.Find("Directional Light"); if (g) sun = g.GetComponent<Light>(); } }
            if (sun) { sunWas = sun.enabled; sun.enabled = false; }
            ambWas = RenderSettings.ambientLight; modeWas = RenderSettings.ambientMode; fogWas = RenderSettings.fog;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.27f, 0.29f, 0.3f); RenderSettings.fog = false;
        }
        void PostRender(Camera c)
        {
            if (c != cam || !swapped) return; swapped = false;
            if (sun) sun.enabled = sunWas; RenderSettings.ambientMode = modeWas; RenderSettings.ambientLight = ambWas; RenderSettings.fog = fogWas;
        }
        WStep Step => def != null && idx < def.steps.Length ? def.steps[idx] : null;
        Vector3 W(Vector3 local) => BasePos + local;
        Vector3 OriginW => W(def.origin);
        static Vector2 Mouse => Input.mousePosition;
        float SD(Vector3 world) { Vector3 sp = cam.WorldToScreenPoint(world); if (sp.z < 0f) return 9f; return Vector2.Distance(new Vector2(sp.x, sp.y), Mouse) / Screen.height; }
        Vector3 OnPlane(float worldY) { var ray = cam.ScreenPointToRay(Input.mousePosition); float t = Mathf.Abs(ray.direction.y) < 0.0001f ? 0f : (worldY - ray.origin.y) / ray.direction.y; return ray.origin + ray.direction * Mathf.Max(0f, t); }
        void Say(string s) { toast = s; toastT = 2.4f; }
        void Lose(float q, string why) { quality = Mathf.Max(0f, quality - q); if (q > 0f) Say($"{why}: -{q:0} quality"); else Say(why); }
        static void Click() { var gm = GameManager.I; if (gm && gm.player) Sfx.At("click", gm.player.transform.position); }

        // ------------------------------------------------------------------ open / close
        /// <summary>Start a job. direct = opened from the admin panel: the product goes straight to the backpack.</summary>
        public bool Begin(int r, int batchMult = 1, bool directToBackpack = false)
        {
            var lab = HomeLab.I; var gm = GameManager.I; if (active || !lab || !gm) return false;
            string err = lab.CanStart(r, batchMult); if (err != null) { gm.Say(err); return false; }
            recipe = r; mult = Mathf.Max(1, batchMult); direct = directToBackpack; def = WorkshopModels.Get(HomeLab.Recipes[r].id);
            if (GameUI.I) GameUI.I.CloseAll(); BuildScene(); quality = 100f; idx = 0; result = false; active = true; camYaw = 0f; camElev = 31f; camDist = def.camDist; toast = ""; EnterStep(); return true;
        }

        void Close()
        {
            active = false; result = false; carry = grip = false; parts.Clear(); falling.Clear(); targets.Clear(); decoys.Clear(); group.Clear(); ghost = null; stream = null; src = null;
            if (swapped) { swapped = false; if (sun) sun.enabled = sunWas; RenderSettings.ambientMode = modeWas; RenderSettings.ambientLight = ambWas; RenderSettings.fog = fogWas; }
            if (root) Destroy(root.gameObject); root = null; cam = null; def = null;
        }

        void BuildScene()
        {
            root = new GameObject("WorkshopScene").transform; root.position = BasePos; var r = root;
            Color wood = Kit.H(0x8a6848), woodD = Kit.H(0x5c4330), wall = Kit.H(0x55665d);
            Kit.Box(r, 0f, -0.05f, 0.5f, 9f, 0.1f, 7f, Kit.H(0x3a4640), 0.1f); Kit.Box(r, 0f, 1.7f, 1.5f, 9f, 3.6f, 0.1f, wall, 0.05f); Kit.Box(r, -3.2f, 1.7f, 0f, 0.1f, 3.6f, 5f, wall, 0.05f); Kit.Box(r, 3.2f, 1.7f, 0f, 0.1f, 3.6f, 5f, wall, 0.05f);
            for (int i = 0; i < 14; i++) Kit.Box(r, -2.9f + i * 0.45f, 1.5f, 1.44f, 0.02f, 3f, 0.02f, Kit.H(0x3a4841), 0.05f);
            Kit.Box(r, 0f, 1.95f, 1.36f, 2.6f, 0.05f, 0.24f, woodD, 0.1f);
            for (int i = 0; i < 6; i++) { Kit.La(r, -1.1f + i * 0.44f, 1.975f, 1.36f, new[] { 0f, 0f, 0.05f, 0f, 0.05f, 0.13f, 0.02f, 0.17f, 0.02f, 0.22f }, i % 2 == 0 ? Kit.H(0x6e8f86) : Kit.H(0xa9764a), 0.6f); }
            for (int i = 0; i < 5; i++) Kit.Box(r, 0f, TableY - 0.03f, -0.42f + i * 0.21f, 2.36f, 0.06f, 0.205f, i % 2 == 0 ? wood : Kit.H(0x846248), 0.2f);
            Kit.Box(r, 0f, TableY - 0.09f, 0f, 2.3f, 0.06f, 1f, woodD, 0.1f); foreach (float x in new[] { -1.08f, 1.08f }) foreach (float z in new[] { -0.42f, 0.42f }) Kit.Box(r, x, (TableY - 0.1f) * 0.5f, z, 0.09f, TableY - 0.1f, 0.09f, Kit.H(0x3a3f3a), 0.3f, 0.4f);
            Kit.Box(r, 0.12f, TableY + 0.003f, 0f, 1.42f, 0.006f, 0.86f, Kit.H(0x26403a), 0.05f); Kit.Box(r, 0.12f, TableY + 0.0065f, 0f, 1.36f, 0.002f, 0.8f, Kit.H(0x31504a), 0.05f);
            // the parts tray: eight felt-lined bins on the left
            Kit.Box(r, -0.91f, TableY + 0.01f, 0f, 0.52f, 0.02f, 1f, Kit.H(0x47594b), 0.2f, 0.3f);
            for (int i = 0; i < 8; i++) { var s = Slot(i); Kit.Box(r, s.x, TableY + 0.022f, s.z, 0.225f, 0.004f, 0.225f, Kit.H(0x1d2a25), 0.05f); }
            foreach (float z in new[] { -0.48f, -0.24f, 0f, 0.24f, 0.48f }) Kit.Box(r, -0.91f, TableY + 0.035f, z, 0.5f, 0.03f, 0.012f, Kit.H(0x66786a), 0.3f, 0.4f);
            foreach (float x in new[] { -1.155f, -0.91f, -0.665f }) Kit.Box(r, x, TableY + 0.035f, 0f, 0.012f, 0.03f, 0.97f, Kit.H(0x66786a), 0.3f, 0.4f);
            // the shipping crate
            var c = CratePos; Color cw = Kit.H(0x9a7650);
            Kit.Box(r, c.x, TableY + 0.01f, c.z, 0.34f, 0.02f, 0.26f, cw, 0.1f); Kit.Box(r, c.x, c.y, c.z - 0.125f, 0.34f, 0.2f, 0.014f, cw, 0.1f); Kit.Box(r, c.x, c.y, c.z + 0.125f, 0.34f, 0.2f, 0.014f, cw, 0.1f);
            Kit.Box(r, c.x - 0.165f, c.y, c.z, 0.014f, 0.2f, 0.26f, cw, 0.1f); Kit.Box(r, c.x + 0.165f, c.y, c.z, 0.014f, 0.2f, 0.26f, cw, 0.1f); Kit.Box(r, c.x, TableY + 0.03f, c.z, 0.31f, 0.02f, 0.23f, Kit.H(0xd9c9a0), 0.05f, 0f, "Straw");
            var lid = Kit.Box(r, c.x, TableY + 0.21f, c.z, 0.36f, 0.02f, 0.28f, Kit.H(0xb08a60), 0.1f, 0f, "CrateLid"); Kit.Box(lid, 0f, 0.6f, 0f, 0.5f, 0.2f, 0.4f, Kit.H(0x2a2420)); lid.gameObject.SetActive(false);
            // lights and the camera
            void Lamp(float x, float y, float z, float inten, Color col) { var l = new GameObject("WorkLight").AddComponent<Light>(); l.transform.SetParent(r, false); l.transform.localPosition = new Vector3(x, y, z); l.type = LightType.Point; l.range = 9f; l.intensity = inten; l.color = col; l.shadows = LightShadows.None; }
            Lamp(0.2f, 2.3f, -0.6f, 1.9f, new Color(1f, 0.95f, 0.86f)); Lamp(-1.4f, 1.9f, -1.5f, 0.9f, new Color(0.85f, 0.92f, 1f)); Lamp(1.5f, 1.8f, -1.2f, 0.8f, new Color(1f, 0.9f, 0.8f));
            Kit.Box(r, 0.2f, 2.75f, -0.2f, 1.2f, 0.05f, 0.4f, Kit.H(0xf0dcae), 0.1f, 0f, "Tube", 1.4f);
            cam = new GameObject("WorkshopCamera").AddComponent<Camera>(); cam.transform.SetParent(r, false); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.08f, 0.11f, 0.1f); cam.fieldOfView = 40f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 40f; cam.depth = 50f;
            def.apparatus?.Invoke(r);
            parts.Clear();
            for (int i = 0; i < def.parts.Length; i++)
            {
                var t = new GameObject("Part_" + def.parts[i].name).transform; t.SetParent(r, false); t.position = OriginW; def.parts[i].build(t);
                var b = Kit.BoundsOf(t); var rp = new RP { t = t, center = b.center - t.position, slot = W(Slot(i)) + Vector3.up * 0.12f };
                float m = Mathf.Max(b.size.x, b.size.y, b.size.z); rp.fit = Mathf.Min(1f, 0.27f / Mathf.Max(0.01f, m)); rp.slot.y = BasePos.y + TableY + 0.03f + b.size.y * rp.fit * 0.5f; ToSlot(rp); parts.Add(rp);
            }
            foreach (var n in def.hidden) { var t = Kit.Find(r, n); if (t) t.gameObject.SetActive(false); }
            PlaceCamera();
        }
        static Vector3 Slot(int i) => new Vector3(i % 2 == 0 ? -1.033f : -0.787f, TableY, 0.36f - (i / 2) * 0.24f);
        void ToSlot(RP p) { p.scale = p.fit; p.t.localScale = Vector3.one * p.fit; p.t.position = p.slot - p.center * p.fit; }
        void PlaceCamera()
        {
            if (!cam) return; var target = W(new Vector3(-0.06f, TableY + 0.23f, 0.02f)); var rot = Quaternion.Euler(camElev, camYaw, 0f);
            cam.transform.position = target - rot * Vector3.forward * camDist; cam.transform.rotation = rot;
        }
        void Toggle(string names, bool on)
        {
            if (string.IsNullOrEmpty(names)) return;
            foreach (var raw in names.Split(',')) { string n = raw.Trim(); bool v = on; if (n.StartsWith("-")) { n = n.Substring(1); v = !on; } var t = Kit.Find(root, n); if (t) t.gameObject.SetActive(v); }
        }

        // ------------------------------------------------------------------ step flow
        void EnterStep()
        {
            var s = Step; carry = grip = false; turns = 0f; level = 0f; flow = 0f; heatV = 0.15f; heatT = 0f; heatIn = 0f; clock = 0f; stepT = 0f; speedPen = 0f; shakeI = 0; shakeErr = 0; drops = 0; spilled = 0;
            tilt = 0f; dropMeter = 0f; pullL = pullR = 0f; stretchDrag = 0; creased = false; snapped = false; src = null; targets.Clear(); decoys.Clear(); group.Clear();
            if (ghost) { ghost.SetActive(false); ghost.transform.SetParent(null); Destroy(ghost); } ghost = null; if (stream) Destroy(stream.gameObject); stream = null;
            if (s == null) { result = true; return; }
            Toggle(s.pre, true);
            switch (s.k)
            {
                case WK.Place:
                    {
                        var rp = parts[s.part]; ghost = Instantiate(rp.t.gameObject, root); ghost.name = "Ghost"; ghost.transform.position = OriginW; ghost.transform.localScale = Vector3.one;
                        var gm = Kit.Ghost(new Color(0.8f, 1f, 0.45f, 0.3f)); foreach (var rd in ghost.GetComponentsInChildren<Renderer>(true)) rd.sharedMaterial = gm; break;
                    }
                case WK.Shake: shakePat = new int[Mathf.Max(3, Mathf.RoundToInt(s.f))]; for (int i = 0; i < shakePat.Length; i++) { int d; do d = Random.Range(0, 4); while (i > 0 && d == shakePat[i - 1]); shakePat[i] = d; } break;
                case WK.Fill:
                    {
                        var l = Kit.Find(root, s.a); if (l) { l.gameObject.SetActive(true); l.localScale = new Vector3(1f, 0.001f, 1f); }
                        if (s.v != Vector3.zero) { stream = Kit.Cy(root, 0f, 0f, 0f, 0.014f, 1f, new Color(0.85f, 0.93f, 0.95f, 0.8f), 1, 0.9f, 0f, "Stream"); stream.gameObject.SetActive(false); }
                        break;
                    }
                case WK.Drops: src = Kit.Find(root, s.a); if (src) srcHome = src.position; break;
                case WK.Clicks:
                    foreach (var t in root.GetComponentsInChildren<Transform>(false)) { if (t.name.StartsWith(s.a) && t.name.Length <= s.a.Length + 2 && t.name != s.a + "s") targets.Add(t); else if (!string.IsNullOrEmpty(s.b) && t.name.StartsWith(s.b) && t.name.Length <= s.b.Length + 2) decoys.Add(t); }
                    clicksLeft = targets.Count; break;
            }
        }
        void CompleteStep(string msg = null)
        {
            var s = Step; if (s == null) return; Toggle(s.on, true); Toggle(s.off, false); if (msg != null) Say(msg); Click();
            if (s.k == WK.Drops && src) { src.position = srcHome; src.rotation = Quaternion.identity; }
            foreach (var f in falling) if (f.t) Destroy(f.t.gameObject); falling.Clear();
            idx++; EnterStep();
        }

        void Finish()
        {
            var lab = HomeLab.I; int r = recipe, m = mult; bool d = direct; float score = Mathf.Clamp01(quality / 100f); Close();
            if (!lab) return; lab.StartBatch(r, score, m); if (d || Admin.Instant) lab.CollectNow(r);
        }

        // ------------------------------------------------------------------ input
        void Update()
        {
            if (!active) return; float dt = Time.unscaledDeltaTime; if (toastT > 0f) toastT -= dt; if (skipCd > 0f) skipCd -= dt;
            if (!cam || !root) { Close(); return; }
            // camera: right mouse orbits, wheel zooms
            if (Input.GetMouseButtonDown(1)) { orbit = true; dragStart = Input.mousePosition; yaw0 = camYaw; elev0 = camElev; }
            if (Input.GetMouseButtonUp(1)) orbit = false;
            if (orbit) { camYaw = Mathf.Clamp(yaw0 + (Input.mousePosition.x - dragStart.x) * 0.18f, -35f, 35f); camElev = Mathf.Clamp(elev0 - (Input.mousePosition.y - dragStart.y) * 0.14f, 16f, 62f); }
            float wheel = Input.mouseScrollDelta.y; bool tipping = carry && Step != null && Step.k == WK.Drops; if (tipping && Mathf.Abs(wheel) > 0.01f) { tilt = Mathf.Clamp(tilt + wheel * 12f, 0f, 105f); wheelTilt = true; } else if (Mathf.Abs(wheel) > 0.01f) camDist = Mathf.Clamp(camDist - wheel * 0.16f, 1.1f, 3.2f);
            PlaceCamera();
            if (ghost) { float a = 0.22f + 0.12f * Mathf.Sin(Time.unscaledTime * 4f); foreach (var rd in ghost.GetComponentsInChildren<Renderer>()) rd.sharedMaterial.color = new Color(0.8f, 1f, 0.45f, a); }
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); GameManager.I?.Say("You put the work down. Nothing was used."); return; }
            if (result) { if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Finish(); return; }
            var s = Step; if (s == null) { result = true; return; }
            if (Admin.On && Input.GetKeyDown(KeyCode.N)) { AutoStep(); return; }
            stepT += dt; bool down = Input.GetMouseButtonDown(0), held = Input.GetMouseButton(0), up = Input.GetMouseButtonUp(0), enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if (OverButtons()) { down = false; if (!carry && !grip) held = false; }
            switch (s.k)
            {
                case WK.Place: DoPlace(s, down, up, dt); break;
                case WK.Crank: case WK.Grind: DoTurn(s, down, held, dt); break;
                case WK.Shake: DoShake(s, down, held); break;
                case WK.Fill: DoFill(s, held || Input.GetKey(KeyCode.Space), enter, dt); break;
                case WK.Drops: DoDrops(s, down, held, enter, dt); break;
                case WK.Heat: DoHeat(s, held || Input.GetKey(KeyCode.Space), dt); break;
                case WK.Stretch: DoStretch(down, held); break;
                case WK.Timing: clock += dt; needle = 0.5f + 0.495f * Mathf.Sin(clock * 3.5f - 1.45f); if (Input.GetKeyDown(KeyCode.Space) || (down && TimingRect().Contains(GuiMouse))) StopTiming(); break;
                case WK.Clicks: DoClicks(s, down); break;
                case WK.Pack: DoPack(down, up); break;
            }
        }

        /// <summary>Admin: finish the current step without a penalty (N).</summary>
        void AutoStep()
        {
            var s = Step; if (s == null || skipCd > 0f) return; skipCd = 0.3f;
            if (s.k == WK.Place) { var rp = parts[s.part]; rp.placed = true; rp.scale = 1f; rp.t.localScale = Vector3.one; rp.t.position = OriginW; if (def.parts[s.part].hide) rp.t.gameObject.SetActive(false); }
            if (s.k == WK.Fill) { var l = Kit.Find(root, s.a); if (l) l.localScale = new Vector3(1f, s.f, 1f); }
            if (s.k == WK.Clicks) foreach (var t in targets) if (t && !string.IsNullOrEmpty(s.b)) t.gameObject.SetActive(false);
            if (s.k == WK.Pack) { foreach (var p in parts) if (p.placed) p.t.gameObject.SetActive(false); Toggle("CrateLid", true); }
            CompleteStep("Step skipped (admin)");
        }

        void DoPlace(WStep s, bool down, bool up, float dt)
        {
            var rp = parts[s.part]; Vector3 goal = OriginW + rp.center;
            if (down && !carry)
            {
                if (SD(rp.slot) < 0.075f) carry = true;
                else for (int i = 0; i < parts.Count; i++) if (!parts[i].placed && i != s.part && SD(parts[i].slot) < 0.06f) { Say("Not yet. Next up: " + def.parts[s.part].name); break; }
            }
            if (carry)
            {
                rp.scale = Mathf.Lerp(rp.scale, 1f, 1f - Mathf.Exp(-9f * dt)); Vector3 hit = OnPlane(goal.y); hit.x = Mathf.Clamp(hit.x, BasePos.x - 1.3f, BasePos.x + 1.3f); hit.z = Mathf.Clamp(hit.z, BasePos.z - 0.7f, BasePos.z + 0.9f);
                rp.t.localScale = Vector3.one * rp.scale; rp.t.position = hit + Vector3.up * (0.012f * Mathf.Sin(Time.unscaledTime * 5f)) - rp.center * rp.scale;
                if (up)
                {
                    carry = false; float d = SD(goal);
                    if (d > 0.1f) { ToSlot(rp); Lose(5f, "Missed the mount, the part went back to the tray"); return; }
                    float loss = d < 0.022f ? 0f : d < 0.04f ? 3f : d < 0.065f ? 8f : 14f;
                    rp.placed = true; rp.scale = 1f; rp.t.localScale = Vector3.one; rp.t.position = OriginW; if (def.parts[s.part].hide) rp.t.gameObject.SetActive(false);
                    quality = Mathf.Max(0f, quality - loss); CompleteStep(loss <= 0f ? "Perfect fit!" : loss <= 3f ? "A hair off: -3 quality" : loss <= 8f ? "Sloppy fit: -8 quality" : "Forced it in: -14 quality");
                }
            }
        }

        void DoTurn(WStep s, bool down, bool held, float dt)
        {
            var obj = Kit.Find(root, s.a); if (!obj) { CompleteStep(); return; }
            Vector3 c3 = obj.position; Vector3 sp = cam.WorldToScreenPoint(c3); Vector2 c = new Vector2(sp.x, sp.y); Vector2 m = Mouse; float rad = Vector2.Distance(m, c) / Screen.height;
            if (down && rad < 0.24f) { grip = true; prevAng = Mathf.Atan2(m.y - c.y, m.x - c.x); }
            if (!held) grip = false;
            if (!grip) return;
            float ang = Mathf.Atan2(m.y - c.y, m.x - c.x), d = Mathf.DeltaAngle(prevAng * Mathf.Rad2Deg, ang * Mathf.Rad2Deg) * Mathf.Deg2Rad; prevAng = ang;
            if (rad < 0.015f) return;
            float step = Mathf.Min(Mathf.Abs(d), 0.6f); turns += step / (Mathf.PI * 2f);
            if (s.k == WK.Crank) obj.Rotate(0f, 0f, d * Mathf.Rad2Deg, Space.Self);
            else
            {
                var pestle = Kit.Find(root, s.b); if (pestle) pestle.position = new Vector3(c3.x + Mathf.Cos(ang) * 0.04f, pestle.position.y, c3.z + Mathf.Sin(ang) * 0.04f);
                var content = Kit.Find(root, "MortarContent"); if (content) content.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.35f, turns / s.f), 1f);
                float speed = step / Mathf.Max(0.004f, dt); if (speed > 16f) { speedPen += dt * 9f; if (speedPen > 1f) { speedPen = 0f; Lose(2f, "Too fast, powder is flying out"); } }
            }
            if (turns >= s.f) CompleteStep(s.k == WK.Crank ? "Done, tight and true." : "Ground fine.");
        }

        void DoShake(WStep s, bool down, bool held)
        {
            var rp = parts[s.part]; Vector3 home = OriginW;
            if (down && SD(home + rp.center) < 0.16f) { grip = true; anchor = Mouse; }
            if (!held && grip) { grip = false; rp.t.position = home; rp.t.rotation = Quaternion.identity; }
            if (!grip) return;
            Vector2 off = (Mouse - anchor) / Screen.height; rp.t.position = home + new Vector3(Mathf.Clamp(off.x, -0.1f, 0.1f) * 0.9f, Mathf.Clamp(off.y, -0.1f, 0.1f) * 0.6f, 0f);
            // tilt around the bottle's own middle
            Quaternion q = Quaternion.Euler(0f, 0f, -Mathf.Clamp(off.x, -0.1f, 0.1f) * 250f); rp.t.rotation = q; rp.t.position += rp.center - q * rp.center;
            if (off.magnitude > 0.07f)
            {
                int dir = Mathf.Abs(off.x) > Mathf.Abs(off.y) ? (off.x > 0f ? 0 : 1) : (off.y > 0f ? 2 : 3); anchor = Mouse;
                if (dir == shakePat[shakeI]) { shakeI++; Click(); if (shakeI >= shakePat.Length) { rp.t.position = home; rp.t.rotation = Quaternion.identity; CompleteStep("Well shaken."); } }
                else { shakeErr++; Lose(4f, "Wrong way"); }
            }
        }

        void DoFill(WStep s, bool pour, bool enter, float dt)
        {
            var l = Kit.Find(root, s.a);
            if (pour) { flow = Mathf.Min(0.42f, flow + dt * 0.5f); level += flow * dt; } else flow = 0f;
            if (l) l.localScale = new Vector3(1f, Mathf.Clamp(level, 0.001f, 1.06f), 1f);
            if (stream)
            {
                stream.gameObject.SetActive(pour && l); if (pour && l) { Vector3 a = W(s.v), b = l.position; b.x = a.x; b.z = a.z; float len = Mathf.Max(0.01f, a.y - b.y); stream.position = (a + b) * 0.5f; stream.localScale = new Vector3(0.012f + flow * 0.03f, len * 0.5f, 0.012f + flow * 0.03f); }
            }
            if (level >= 1.06f) { if (stream) stream.gameObject.SetActive(false); quality = Mathf.Max(0f, quality - 25f); CompleteStep("It ran over the top: -25 quality"); return; }
            if (enter)
            {
                if (level < 0.15f) { Say("There's barely anything in it yet."); return; }
                float e = Mathf.Abs(level - s.f), loss = e < 0.035f ? 0f : e < 0.08f ? 5f : e < 0.15f ? 12f : 22f; quality = Mathf.Max(0f, quality - loss);
                if (stream) stream.gameObject.SetActive(false); CompleteStep(loss <= 0f ? "Right on the line!" : (level < s.f ? "Short measure" : "Overfilled") + $": -{loss:0} quality");
            }
        }

        void DoDrops(WStep s, bool down, bool held, bool enter, float dt)
        {
            if (!src) { CompleteStep(); return; }
            const float h = 0.17f; Vector3 mouth = W(s.v); int target = Mathf.RoundToInt(s.f);
            if (down && SD(src.position + Vector3.up * 0.06f) < 0.09f) carry = true;
            if (!held && carry) { carry = false; tilt = 0f; wheelTilt = false; src.position = srcHome; src.rotation = Quaternion.identity; }
            Vector3 nozzle = src.position + src.rotation * (Vector3.up * h);
            if (carry)
            {
                if (Input.GetKey(KeyCode.Space)) { tilt = Mathf.Min(105f, tilt + 95f * dt); wheelTilt = false; } else if (!wheelTilt) tilt = Mathf.Max(0f, tilt - 160f * dt);
                Vector3 want = OnPlane(mouth.y + 0.1f); want.x = Mathf.Clamp(want.x, BasePos.x - 0.9f, BasePos.x + 1.1f); want.z = Mathf.Clamp(want.z, BasePos.z - 0.6f, BasePos.z + 0.6f);
                Vector3 over = new Vector3(mouth.x, mouth.y + 0.1f, mouth.z); snapped = Vector2.Distance(new Vector2(want.x, want.z), new Vector2(over.x, over.z)) < 0.09f || SD(over) < 0.06f; if (snapped) want = over;
                var q = Quaternion.Euler(0f, 0f, -tilt); src.rotation = q; src.position = want - q * (Vector3.up * h); nozzle = want;
                float rate = tilt < 40f ? 0f : tilt < 65f ? 1.3f : tilt < 90f ? 3f : 6.5f; dropMeter += rate * dt;
                while (dropMeter >= 1f) { dropMeter -= 1f; var d = Kit.Ball(root, 0f, 0f, 0f, 0.016f, 0.024f, 0.016f, new Color(0.45f, 0.8f, 0.5f), 0.9f, 0f, "Drop", 0.4f); d.position = nozzle; falling.Add((d, 0f, snapped)); }
            }
            for (int i = falling.Count - 1; i >= 0; i--)
            {
                var f = falling[i]; f.vy += 4.5f * dt; f.t.position += Vector3.down * f.vy * dt; falling[i] = f;
                if (f.t.position.y <= mouth.y)
                {
                    if (f.ok) { drops++; if (drops > target) Lose(3f, "One drop too many"); else Click(); } else { spilled++; Lose(3f, "A drop missed the mouth"); }
                    Destroy(f.t.gameObject); falling.RemoveAt(i);
                }
            }
            if (enter && falling.Count == 0)
            {
                float loss = Mathf.Min(40f, Mathf.Max(0, target - drops) * 6f + (drops == 0 ? 10f : 0f)); quality = Mathf.Max(0f, quality - loss);
                carry = false; CompleteStep(loss <= 0f && drops == target ? "Exactly right." : loss > 0f ? $"Too little essence: -{loss:0} quality" : "Dosed.");
            }
        }

        float HeatLo => 0.45f + Mathf.Sin(clock * 0.7f) * 0.14f;
        float HeatHi => HeatLo + 0.2f;
        void DoHeat(WStep s, bool heat, float dt)
        {
            clock += dt; heatT += dt; heatV = Mathf.Clamp01(heatV + (heat ? 0.5f : -0.32f) * dt + Mathf.Sin(clock * 3.1f) * 0.05f * dt); if (heatV >= HeatLo && heatV <= HeatHi) heatIn += dt;
            var fl = Kit.Find(root, s.a); if (fl) { float k = 0.35f + heatV * 1.5f + Mathf.Sin(Time.unscaledTime * 23f) * 0.07f; fl.localScale = new Vector3(0.7f + heatV * 0.7f, k, 0.7f + heatV * 0.7f); }
            var nd = Kit.Find(root, "Needle"); if (nd) nd.localRotation = Quaternion.Euler(0f, 0f, 120f - heatV * 240f);
            if (heatT >= s.f) { float frac = heatIn / s.f, loss = frac > 0.9f ? 0f : Mathf.Round((0.9f - frac) * 34f); quality = Mathf.Max(0f, quality - loss); CompleteStep(loss <= 0f ? "A clean, steady run!" : $"Uneven heat ({frac * 100f:0}% in the band): -{loss:0} quality"); }
        }

        static Vector2 GuiMouse => new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        static Rect PadRect() => new Rect(Screen.width / 2f - 230f, 14f, 460f, 120f);
        static Rect TimingRect() => new Rect(Screen.width / 2f - 240f, 14f, 480f, 84f);
        float HandleX(bool left) { var p = PadRect(); return left ? p.center.x - 70f - pullL * 110f : p.center.x + 70f + pullR * 110f; }
        void DoStretch(bool down, bool held)
        {
            var p = PadRect(); var m = GuiMouse;
            if (down) { if (Vector2.Distance(m, new Vector2(HandleX(true), p.center.y + 14f)) < 26f) stretchDrag = 1; else if (Vector2.Distance(m, new Vector2(HandleX(false), p.center.y + 14f)) < 26f) stretchDrag = 2; }
            if (!held) stretchDrag = 0;
            if (stretchDrag == 1) pullL = Mathf.Clamp((p.center.x - 70f - m.x) / 110f, 0f, 1.15f); else if (stretchDrag == 2) pullR = Mathf.Clamp((m.x - p.center.x - 70f) / 110f, 0f, 1.15f);
            if (!creased && Mathf.Abs(pullL - pullR) > 0.7f) { creased = true; Lose(4f, "The label creased, pull both ends evenly"); }
            if (pullL > 0.86f && pullR > 0.86f && Mathf.Abs(pullL - pullR) < 0.16f) CompleteStep("Label on straight.");
        }
        void StopTiming()
        {
            float e = Mathf.Abs(needle - 0.5f), loss = e < 0.08f ? 0f : e < 0.14f ? 6f : e < 0.23f ? 14f : e < 0.35f ? 24f : 36f; quality = Mathf.Max(0f, quality - loss);
            CompleteStep(loss <= 0f ? "Perfect timing!" : $"Timing slipped: -{loss:0} quality");
        }

        void DoClicks(WStep s, bool down)
        {
            if (stepT > s.f + 2f) { stepT = s.f; Lose(1f, "Taking too long"); }
            if (!down) return; Transform best = null; float bd = 0.045f; bool decoy = false;
            foreach (var t in targets) if (t && t.gameObject.activeInHierarchy && t.name != "Done") { float d = SD(Kit.BoundsOf(t).center); if (d < bd) { bd = d; best = t; decoy = false; } }
            foreach (var t in decoys) if (t && t.gameObject.activeInHierarchy) { float d = SD(Kit.BoundsOf(t).center); if (d < bd * 0.8f) { bd = d; best = t; decoy = true; } }
            if (!best) return;
            if (decoy) { Lose(5f, "You nicked a bud"); return; }
            if (string.IsNullOrEmpty(s.b)) { best.name = "Done"; best.localRotation = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f)); best.localScale = Vector3.one * 0.8f; best.localPosition += new Vector3(Random.Range(-0.012f, 0.012f), 0.004f, Random.Range(-0.012f, 0.012f)); }
            else best.gameObject.SetActive(false);
            Click(); clicksLeft--; if (clicksLeft <= 0) CompleteStep("Clean work.");
        }

        void DoPack(bool down, bool up)
        {
            if (group.Count == 0)
            {
                var b = new Bounds(); bool any = false; foreach (var p in parts) if (p.placed && p.t.gameObject.activeSelf) { var pb = Kit.BoundsOf(p.t); if (!any) { b = pb; any = true; } else b.Encapsulate(pb); group.Add((p.t, p.t.position)); }
                if (!any) { CompleteStep(); return; } groupC = b.center;
            }
            Vector3 crate = W(CratePos) + Vector3.up * 0.08f;
            if (down && !carry && SD(groupC) < 0.13f) carry = true;
            if (!carry) return;
            Vector3 hit = OnPlane(groupC.y); foreach (var g in group) g.t.position = g.p + (hit - groupC);
            if (up)
            {
                carry = false;
                if (SD(crate) < 0.12f) { foreach (var g in group) g.t.gameObject.SetActive(false); Toggle("CrateLid", true); CompleteStep("Packed."); }
                else { foreach (var g in group) g.t.position = g.p; Lose(3f, "That's not the crate"); }
            }
        }

        // ------------------------------------------------------------------ HUD
        Rect ButtonsRect => new Rect(Screen.width - 250f, Screen.height - 60f, 236f, 46f);
        bool OverButtons() => ButtonsRect.Contains(GuiMouse) || (Admin.On && new Rect(Screen.width - 250f, Screen.height - 112f, 236f, 46f).Contains(GuiMouse));
        static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }
        static readonly string[] Arrow = { "→", "←", "↑", "↓" };

        void OnGUI()
        {
            if (!active || def == null) return; GUI.depth = -50; float W = Screen.width, H = Screen.height; var rc = HomeLab.Recipes[recipe];
            // title + quality
            var top = new Rect(14, 12, 330, 96); UiSkin.DrawPanel(top, true);
            GUI.Label(new Rect(top.x + 14, top.y + 8, 300, 28), def.title + (mult > 1 ? $"  ×{mult}" : ""), UiSkin.Title);
            GUI.Label(new Rect(top.x + 14, top.y + 38, 300, 20), $"QUALITY  {quality:0}%   ·   expect about {HomeLab.YieldFor(rc, quality / 100f) * mult} × {Items.Get(rc.product).name}", UiSkin.LabelDim);
            UiSkin.DrawBar(new Rect(top.x + 14, top.y + 64, 300, 12), quality / 100f, quality < 35f ? new Color(0.95f, 0.45f, 0.4f) : quality < 60f ? UiSkin.Accent : new Color(0.6f, 0.9f, 0.45f));
            // step list
            int n = def.steps.Length; var list = new Rect(W - 300, 12, 286, 46 + n * 24); UiSkin.DrawPanel(list, true); GUI.Label(new Rect(list.x + 12, list.y + 8, 260, 20), "STEPS", UiSkin.H2);
            for (int i = 0; i < n; i++)
            {
                var st = i == idx && !result ? UiSkin.LabelBold : UiSkin.LabelDim; string mark = i < idx || result ? "✓" : (i + 1).ToString();
                if (i == idx && !result) Fill(new Rect(list.x + 6, list.y + 32 + i * 24, list.width - 12, 22), new Color(1f, 0.78f, 0.25f, 0.16f));
                GUI.Label(new Rect(list.x + 12, list.y + 33 + i * 24, 24, 20), mark, st); GUI.Label(new Rect(list.x + 36, list.y + 33 + i * 24, 240, 20), def.steps[i].text, st);
            }
            // tray labels
            var s = Step;
            if (!result) for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i]; if (p.placed || (carry && s != null && s.k == WK.Place && s.part == i)) continue; Vector3 sp = cam.WorldToScreenPoint(p.slot + Vector3.up * 0.1f); if (sp.z < 0f) continue;
                bool next = s != null && s.k == WK.Place && s.part == i; var ls = new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter }; ls.normal.textColor = next ? new Color(0.85f, 1f, 0.5f) : new Color(0.75f, 0.78f, 0.75f, 0.7f);
                var lr = new Rect(sp.x - 70, H - sp.y - 22, 140, 18); if (next) Fill(new Rect(lr.x + 14, lr.y, lr.width - 28, lr.height), new Color(0.1f, 0.18f, 0.12f, 0.85f)); GUI.Label(lr, def.parts[i].name, ls);
            }
            if (result) { DrawResult(W, H, rc); DrawToast(W); return; }
            if (s == null) return;
            // current step
            var hint = new Rect(W / 2f - 330, H - 92, 660, 78); UiSkin.DrawPanel(hint, true);
            GUI.Label(new Rect(hint.x + 16, hint.y + 8, 628, 26), $"{idx + 1}. {s.text}", UiSkin.Title); var hs = new GUIStyle(UiSkin.Label) { wordWrap = true }; GUI.Label(new Rect(hint.x + 16, hint.y + 38, 628, 38), s.hint, hs);
            switch (s.k)
            {
                case WK.Crank: case WK.Grind: Gauge(W, H, $"{turns:0.0} / {s.f:0.0} turns", turns / s.f, -1f); break;
                case WK.Shake:
                    {
                        var r = new Rect(W / 2f - 150, 14, 300, 60); UiSkin.DrawPanel(r, true);
                        for (int i = 0; i < shakePat.Length; i++) { var b = new Rect(r.x + 12 + i * ((r.width - 24) / shakePat.Length), r.y + 10, 38, 38); Fill(b, i < shakeI ? new Color(0.25f, 0.5f, 0.3f) : i == shakeI ? new Color(1f, 0.78f, 0.25f, 0.9f) : new Color(1f, 1f, 1f, 0.12f)); var a = new GUIStyle(UiSkin.Big); a.normal.textColor = i == shakeI ? Color.black : Color.white; GUI.Label(b, i < shakeI ? "✓" : Arrow[shakePat[i]], a); }
                        break;
                    }
                case WK.Fill: Gauge(W, H, $"{Mathf.Min(level, 1f) * 100f:0}%   (mark at {s.f * 100f:0}%)   ENTER when done", Mathf.Clamp01(level), s.f); break;
                case WK.Drops: Gauge(W, H, $"{drops} / {s.f:0} drops   spilled {spilled}   {(carry ? (snapped ? "over the mouth: hold SPACE or roll the wheel" : "move over the bottle's mouth") : "pick up the little bottle")}   ENTER when done", Mathf.Clamp01(drops / s.f), 1f); break;
                case WK.Heat:
                    {
                        var r = new Rect(W / 2f - 260, 14, 520, 70); UiSkin.DrawPanel(r, true); var bar = new Rect(r.x + 16, r.y + 30, r.width - 32, 26); Fill(bar, new Color(0.1f, 0.12f, 0.16f));
                        Fill(new Rect(bar.x + HeatLo * bar.width, bar.y, (HeatHi - HeatLo) * bar.width, bar.height), new Color(0.2f, 0.7f, 0.35f)); bool ok = heatV >= HeatLo && heatV <= HeatHi; Fill(new Rect(bar.x + heatV * bar.width - 3, bar.y - 6, 6, bar.height + 12), ok ? Color.white : new Color(1f, 0.4f, 0.3f));
                        GUI.Label(new Rect(r.x + 16, r.y + 6, 480, 20), $"TEMPERATURE   {Mathf.Max(0f, s.f - heatT):0.0}s left   in the band {(heatIn / Mathf.Max(0.01f, heatT)) * 100f:0}%", UiSkin.LabelDim);
                        break;
                    }
                case WK.Stretch:
                    {
                        var p = PadRect(); UiSkin.DrawPanel(p, true); GUI.Label(new Rect(p.x + 14, p.y + 6, 430, 20), $"LABEL TENSION  {Mathf.Min(100f, (pullL + pullR) * 50f):0}%", UiSkin.LabelDim);
                        float l = HandleX(true), rr = HandleX(false), cy = p.center.y + 14f; float skew = (pullR - pullL) * 14f;
                        var m = GUI.matrix; GUIUtility.RotateAroundPivot(skew, new Vector2(p.center.x, cy)); Fill(new Rect(l, cy - 18, rr - l, 36), new Color(0.91f, 0.86f, 0.75f)); Fill(new Rect(l + 8, cy - 10, rr - l - 16, 6), new Color(0.54f, 0.18f, 0.13f)); Fill(new Rect(l + (rr - l) * 0.2f, cy + 4, (rr - l) * 0.6f, 4), new Color(0.2f, 0.16f, 0.14f)); GUI.matrix = m;
                        foreach (float hx in new[] { l, rr }) { Fill(new Rect(hx - 13, cy - 13, 26, 26), new Color(0.85f, 0.95f, 0.5f)); Fill(new Rect(hx - 8, cy - 8, 16, 16), new Color(0.3f, 0.36f, 0.2f)); }
                        break;
                    }
                case WK.Timing:
                    {
                        var r = TimingRect(); UiSkin.DrawPanel(r, true); var bar = new Rect(r.x + 16, r.y + 34, r.width - 32, 30); Fill(bar, new Color(0.1f, 0.12f, 0.16f)); Fill(new Rect(bar.x + 0.42f * bar.width, bar.y, 0.16f * bar.width, bar.height), new Color(0.2f, 0.7f, 0.35f));
                        Fill(new Rect(bar.x + needle * bar.width - 3, bar.y - 6, 6, bar.height + 12), Color.white); GUI.Label(new Rect(r.x + 16, r.y + 8, 440, 20), "SPACE (or click here) to stop the marker in the green", UiSkin.LabelDim);
                        break;
                    }
                case WK.Clicks: Gauge(W, H, $"{targets.Count - clicksLeft} / {targets.Count}", 1f - clicksLeft / (float)Mathf.Max(1, targets.Count), -1f); break;
            }
            // buttons
            var br = ButtonsRect; if (GUI.Button(new Rect(br.x, br.y, br.width, br.height), "Put it down  (Esc)", UiSkin.Button)) { Close(); GameManager.I?.Say("You put the work down. Nothing was used."); return; }
            if (Admin.On) { if (GUI.Button(new Rect(br.x, br.y - 52, 114, 46), "Skip step (N)", UiSkin.ButtonOn)) { AutoStep(); return; } if (GUI.Button(new Rect(br.x + 122, br.y - 52, 114, 46), "Finish now", UiSkin.ButtonOn)) { result = true; return; } }
            GUI.Label(new Rect(14, H - 30, 520, 20), "Left mouse: grab and drag   ·   Right mouse: look around   ·   Wheel: zoom", UiSkin.Small);
            DrawToast(W);
        }
        void Gauge(float W, float H, string text, float v, float mark)
        {
            var r = new Rect(W / 2f - 260, 14, 520, 54); UiSkin.DrawPanel(r, true); GUI.Label(new Rect(r.x + 16, r.y + 6, 490, 20), text, UiSkin.LabelDim);
            var bar = new Rect(r.x + 16, r.y + 30, r.width - 32, 12); UiSkin.DrawBar(bar, Mathf.Clamp01(v), UiSkin.Accent); if (mark > 0f && mark < 1f) Fill(new Rect(bar.x + mark * bar.width - 1.5f, bar.y - 5, 3, bar.height + 10), new Color(0.6f, 1f, 0.5f));
        }
        void DrawToast(float W)
        {
            if (toastT <= 0f || string.IsNullOrEmpty(toast)) return; var st = new GUIStyle(UiSkin.Center) { fontSize = 16, fontStyle = FontStyle.Bold }; float w = Mathf.Max(260f, st.CalcSize(new GUIContent(toast)).x + 40f);
            var r = new Rect(W / 2f - w / 2f, 142, w, 38); UiSkin.DrawPanel(r, true); GUI.Label(r, toast, st);
        }
        void DrawResult(float W, float H, HomeLab.Recipe rc)
        {
            var r = new Rect(W / 2f - 220, H / 2f - 170, 440, 340); UiSkin.DrawPanel(r, true); float q = quality;
            string grade = q >= 90f ? "MASTERWORK" : q >= 75f ? "PREMIUM" : q >= 55f ? "STANDARD" : q >= 35f ? "FLAWED" : "BOTCHED";
            GUI.Label(new Rect(r.x, r.y + 14, r.width, 20), "QUALITY CONTROL", new GUIStyle(UiSkin.H2) { alignment = TextAnchor.MiddleCenter }); GUI.Label(new Rect(r.x, r.y + 38, r.width, 34), def.title, UiSkin.Big);
            var big = new GUIStyle(UiSkin.Big) { fontSize = 64 }; big.normal.textColor = q < 35f ? new Color(0.95f, 0.45f, 0.4f) : q < 60f ? UiSkin.Accent : new Color(0.7f, 0.95f, 0.5f); GUI.Label(new Rect(r.x, r.y + 78, r.width, 80), $"{q:0}%", big);
            GUI.Label(new Rect(r.x, r.y + 162, r.width, 24), grade, UiSkin.Big);
            int y = HomeLab.YieldFor(rc, q / 100f) * mult; var mid = new GUIStyle(UiSkin.Label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUI.Label(new Rect(r.x + 20, r.y + 196, r.width - 40, 44), $"This batch gives {y} × {Items.Get(rc.product).name}." + (direct || Admin.Instant ? "" : $"\nReady in {rc.hours:0} game hours. Sleep to pass the time."), mid);
            if (GUI.Button(new Rect(r.x + 60, r.y + 262, r.width - 120, 50), "Done  (Enter)", UiSkin.ButtonOn)) Finish();
        }
    }
}
