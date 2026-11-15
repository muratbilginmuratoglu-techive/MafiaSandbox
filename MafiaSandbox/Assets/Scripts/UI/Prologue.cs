using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Npc;
using MafiaSandbox.Player;
using MafiaSandbox.Vehicles;
using MafiaSandbox.World;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// Story opening. Three years after the families' war you wake from a coma in Port City General; your only remaining friend Jeremy
    /// walks in, tells you what happened, and drives you to what is left of your home. F7 replays it, Backspace skips it.
    /// </summary>
    public class Prologue : MonoBehaviour
    {
        public static Prologue I { get; private set; }
        public static bool Active { get; private set; }       // any phase
        public static bool Cinematic { get; private set; }    // letterboxed / input locked (counts as a blocking UI)

        Hospital hosp; PlayerController pc; PlayerCamera cam; Camera camera; VehicleInteraction vi; CharacterController cc; Transform model;
        JeremyBrain jer; VehicleController car; Vector3 homePos; float homeYaw; Vector3 homeDoor;
        float fade = 1f; string title, subWho, subText, objective; float subUntil; bool boardHint;
        bool camOn, camSnap; Vector3 camPos, camLook; Transform lookTarget; Vector3 lookOffset = Vector3.up * 1.4f;
        Coroutine run;

        void Awake() { I = this; }

        public void Begin()
        {
            if (Active) return; hosp = Hospital.I; var gm = GameManager.I; if (!hosp || !gm || !gm.player) return;
            run = StartCoroutine(Run());
        }

        void Update()
        {
            if (Active) { var dn0 = DayNightCycle.I; if (dn0) dn0.timeOfDay = 8.6f; }   // time stands still while the story plays
            if (!Active) { if (Input.GetKeyDown(KeyCode.F7) && !CharacterCreator.IsOpen && !IntroCinematic.Playing) Begin(); return; }
            if (Input.GetKeyDown(KeyCode.F8) && objective != null && car && pc) { pc.Teleport(car.transform.position + car.transform.right * -2.2f, hosp.yaw); Debug.Log("[Prologue] F8 teleport to car " + car.transform.position); }   // dev: jump to the car
            if (Input.GetKeyDown(KeyCode.Backspace)) { if (run != null) StopCoroutine(run); StopAllCoroutines(); Cleanup(); }
        }

        void LateUpdate()
        {
            if (!Active || !camOn || !camera) return;
            if (lookTarget) camLook = lookTarget.position + lookOffset;
            var t = camera.transform;
            if (camSnap) { t.position = camPos; camSnap = false; } else t.position = Vector3.Lerp(t.position, camPos, 1f - Mathf.Exp(-4f * Time.deltaTime));
            var want = Quaternion.LookRotation((camLook - t.position).normalized, Vector3.up);
            t.rotation = Quaternion.Slerp(t.rotation, want, 1f - Mathf.Exp(-5f * Time.deltaTime));
        }

        // ------------------------------------------------------------------ helpers
        IEnumerator FadeTo(float a, float d) { float s = fade, t = 0f; while (t < d) { t += Time.unscaledDeltaTime; fade = Mathf.Lerp(s, a, Mathf.SmoothStep(0f, 1f, t / d)); yield return null; } fade = a; }
        IEnumerator Wait(float s) { float t = 0f; while (t < s) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 pos, Vector3 look, bool snap = false, Transform target = null) { camPos = pos; camLook = look; lookTarget = target; camOn = true; if (snap) camSnap = true; }
        IEnumerator Line(string who, string text, Transform look = null)
        {
            subWho = who; subText = text; subUntil = float.MaxValue; if (look) { lookTarget = look; }
            if (who == "Jeremy" && jer) jer.Talk(true);
            float t = 0f, auto = 3.2f + text.Length * 0.045f;
            while (true) { t += Time.deltaTime; if (t > 0.7f && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0))) break; if (t > auto) break; yield return null; }
            if (jer) jer.Talk(false); subText = null; subUntil = 0f; yield return Wait(0.25f);
        }
        void Sub(string who, string text, float secs) { subWho = who; subText = text; subUntil = Time.unscaledTime + secs; }

        // ------------------------------------------------------------------ the sequence
        IEnumerator Run()
        {
            var gm = GameManager.I; pc = gm.player; cam = gm.playerCamera; camera = cam.GetComponent<Camera>(); vi = pc.GetComponent<VehicleInteraction>(); cc = pc.GetComponent<CharacterController>();
            model = pc.transform.Find("Model"); homePos = gm.homePosition; homeYaw = gm.homeYaw;
            var eco = EconomyManager.I; homeDoor = eco && eco.homeShop != null ? eco.homeShop.info.door : homePos + Vector3.right * 6f;
            Active = true; Cinematic = true; fade = 1f; title = null; objective = null;
            pc.InputLocked = true; cam.enabled = false; if (vi) vi.enabled = false;
            var dn = DayNightCycle.I; if (dn) dn.timeOfDay = 8.6f;
            camera.fieldOfView = 62f;

            // Jeremy's car waits out front
            VehicleController src = null; foreach (var v in VehicleController.All) if (v && !v.isPlayerCar && !v.GetComponent<TrafficDriver>()) { src = v; break; }
            if (!src) foreach (var v in VehicleController.All) if (v) { src = v; break; }
            if (src)
            {
                var cg = Instantiate(src.gameObject, hosp.carSpot, Quaternion.Euler(0f, hosp.carYaw, 0f)); cg.name = "Jeremy's Car"; var td = cg.GetComponent<TrafficDriver>(); if (td) Destroy(td);
                car = cg.GetComponent<VehicleController>(); car.displayName = "Jeremy's car"; car.isPlayerCar = false;
            }

            // 1. the bed
            pc.Teleport(hosp.bedFeet, hosp.yaw); cc.enabled = false;
            if (model) { model.localRotation = Quaternion.Euler(-90f, 0f, 0f); model.localPosition = Vector3.zero; }
            if (pc.animator) pc.animator.Freeze();
            Cam(hosp.W(-4f, 1.45f, -6.15f), hosp.W(-4f, 3.4f, -2.2f), true);
            title = "PORT CITY\n<size=70%>three years later</size>";
            yield return Wait(0.3f); yield return FadeTo(0.55f, 1.6f); title = null;
            yield return FadeTo(0.95f, 0.5f); yield return FadeTo(0.25f, 1.3f); yield return FadeTo(0.8f, 0.4f); yield return FadeTo(0f, 1.6f);
            yield return Line("", "A steady beeping. A white ceiling. Your body feels like lead.");

            // 2. Jeremy
            var sp = FindAnyObjectByType<NpcSpawner>(); GameObject prefab = null;
            if (sp && sp.prefabs != null) foreach (var p in sp.prefabs) if (p && p.name.StartsWith("Vatandas_03")) prefab = p; if (!prefab && sp && sp.prefabs.Length > 0) prefab = sp.prefabs[0];
            if (sp && prefab) { jer = sp.Spawn<JeremyBrain>(prefab, hosp.jeremyStart, hosp.yaw + 180f); jer.displayName = "Jeremy"; }
            Cam(hosp.W(7.6f, 2.5f, -6.3f), hosp.W(-2f, 1.2f, -2f), true);
            if (jer) { jer.WalkTo(hosp.jeremyTalk); lookTarget = jer.transform; }
            float wt = 0f; while (jer && !jer.Done && wt < 28f) { wt += Time.deltaTime; yield return null; }
            if (jer && !jer.Done) { var ag0 = jer.GetComponent<NavMeshAgent>(); Debug.LogWarning($"[Prologue] Jeremy did not arrive: pos {jer.transform.position} onNavMesh {ag0.isOnNavMesh} hasPath {ag0.hasPath} status {ag0.pathStatus} target {hosp.jeremyTalk} start {hosp.jeremyStart}"); var ag = jer.GetComponent<NavMeshAgent>(); if (ag && NavMesh.SamplePosition(hosp.jeremyTalk, out var nh, 4f, NavMesh.AllAreas)) ag.Warp(nh.position); }
            if (jer) { jer.Halt(); jer.Face(hosp.bedFeet); }
            var bedLook = new GameObject("bedLook").transform; bedLook.position = hosp.bedFeet; Destroy(bedLook.gameObject, 600f);
            Cam(hosp.W(1.2f, 1.9f, -1.0f), hosp.W(-3.2f, 1.2f, -4.6f), false);
            yield return Line("Jeremy", "Easy. Easy, don't try to sit up yet. It's me. It's Jeremy.", jer ? jer.transform : null);
            yield return Line("You", "...Jeremy? Where am I? What... what happened?", bedLook);
            yield return Line("Jeremy", "You've been out for three years. The war between the Morettis and the Kaplans... it swallowed half the city.", jer ? jer.transform : null);
            yield return Line("Jeremy", "Your family was caught in the crossfire. I'm sorry. There's no one left. I'm the only one who stayed.", jer ? jer.transform : null);
            yield return Line("You", "...The house?", bedLook);
            yield return Line("Jeremy", "Burned to the studs. Come on. I'll drive you there. My car's right out front.", jer ? jer.transform : null);

            // 3. get up and follow him
            yield return FadeTo(1f, 0.6f);
            if (model) { model.localRotation = Quaternion.identity; model.localPosition = Vector3.zero; }
            pc.Teleport(hosp.standPos, hosp.standYaw); if (pc.animator) pc.animator.Unfreeze();
            if (jer && car) jer.WalkTo(car.transform.TransformPoint(-1.7f, 0f, 0.4f));
            cam.enabled = true; cam.SetYaw(hosp.standYaw); camOn = false; lookTarget = null; subText = null;
            yield return FadeTo(0f, 0.8f);
            pc.InputLocked = false; Cinematic = false; objective = "Follow Jeremy outside to his car.";
            GameManager.I.Say("Your legs are weak, but they move. Follow Jeremy.");
            Debug.Log($"[Prologue] walk phase: cc {cc.enabled} locked {pc.InputLocked} cam {cam.enabled} player {pc.transform.position} car {(car ? car.transform.position.ToString() : "NULL")} hospital {hosp.transform.position} carSpot {hosp.carSpot} jeremy {(jer ? jer.transform.position.ToString() : "NULL")}");
            float walkT = 0f;
            while (true)
            {
                boardHint = car && Vector3.Distance(pc.transform.position, car.transform.position) < 4.2f;
                if (boardHint && Input.GetKeyDown(KeyCode.E)) break;
                walkT += Time.deltaTime;
                if (jer && car && jer.gameObject.activeSelf && walkT > 12f && Vector3.Distance(jer.transform.position, car.transform.position) > 3.5f)   // no path out of the building: place him by the car
                { var agw = jer.GetComponent<NavMeshAgent>(); if (agw && NavMesh.SamplePosition(car.transform.TransformPoint(-1.7f, 0f, 0.4f), out var nw, 5f, NavMesh.AllAreas)) agw.Warp(nw.position); walkT = -999f; }
                yield return null;
            }
            boardHint = false; objective = null;

            // 4. the drive
            Cinematic = true; pc.InputLocked = true; yield return FadeTo(1f, 0.7f);
            if (model) model.gameObject.SetActive(false); cc.enabled = false; if (jer) jer.gameObject.SetActive(false); cam.enabled = false;
            var path = car ? BuildPath(car.transform.position, homePos) : null;
            float total = 0f; var cum = new List<float>(); if (path != null) { cum.Add(0f); for (int i = 1; i < path.Count; i++) { total += Vector3.Distance(path[i - 1], path[i]); cum.Add(total); } }
            if (car && path != null && total > 5f)
            {
                var rb = car.GetComponent<Rigidbody>(); if (rb) rb.isKinematic = true; car.enabled = false;
                Vector3 t0; Sample(path, cum, 0f, out t0); car.transform.rotation = Quaternion.LookRotation(t0, Vector3.up);
                Cam(car.transform.position - t0 * 8f + Vector3.up * 3.4f, car.transform.position + Vector3.up, true, car.transform);
                yield return FadeTo(0f, 1f);
                float s = 0f, v = 0f; int lineIx = 0;
                var lines = new (float at, string who, string text)[] {
                    (0.08f, "Jeremy", "The Morettis took the west side, the Kaplans the east. The cops just collect from both."),
                    (0.42f, "Jeremy", "Your uncle Enzo died owing a fixer named Sal. Sal's been asking about you. His debt didn't die with him."),
                    (0.75f, "Jeremy", "Whatever you do next, do it quietly. This city eats loud people.") };
                while (s < total - 0.05f)
                {
                    float remain = total - s; float target = Mathf.Min(11f, Mathf.Max(2.2f, remain * 0.6f)); v = Mathf.MoveTowards(v, target, (v < target ? 4f : 9f) * Time.deltaTime); s = Mathf.Min(total, s + v * Time.deltaTime);
                    var p = Sample(path, cum, s, out var tan); if (Ground(p, out float gy)) p.y = gy + 0.06f;
                    car.transform.position = p; car.transform.rotation = Quaternion.RotateTowards(car.transform.rotation, Quaternion.LookRotation(tan, Vector3.up), 140f * Time.deltaTime);
                    camPos = car.transform.position - car.transform.forward * 8f + Vector3.up * 3.4f;
                    if (lineIx < lines.Length && s / total >= lines[lineIx].at) { Sub(lines[lineIx].who, lines[lineIx].text, 7f); lineIx++; }
                    if (Input.GetKeyDown(KeyCode.Space)) subUntil = 0f;
                    yield return null;
                }
            }
            else { if (car) car.transform.position = homePos + Vector3.right * 4f; }
            subText = null; yield return FadeTo(1f, 0.6f);

            // 5. what's left of home
            Vector3 stopFwd = car ? car.transform.forward : Vector3.forward;
            if (model) model.gameObject.SetActive(true);
            float faceYaw = Mathf.Atan2(homeDoor.x - homePos.x, homeDoor.z - homePos.z) * Mathf.Rad2Deg;
            pc.Teleport(homePos, faceYaw); if (pc.animator) pc.animator.Unfreeze();
            if (jer) { jer.gameObject.SetActive(true); var ag = jer.GetComponent<NavMeshAgent>(); Vector3 want = (car ? car.transform.position : homePos) + (car ? car.transform.right : Vector3.right) * -2.4f; if (ag && NavMesh.SamplePosition(want, out var nh2, 6f, NavMesh.AllAreas)) ag.Warp(nh2.position); jer.Face(homePos); }
            cam.enabled = true; cam.SetYaw(faceYaw, 8f); camOn = false; lookTarget = null;
            yield return FadeTo(0f, 1.2f);
            yield return Line("Jeremy", "This is it. What's left of it.", jer ? jer.transform : null);
            yield return Line("You", "...", null);
            yield return Line("Jeremy", "Here. It's all I could scrape together. Sal will find you. Be careful who you trust in this town.", jer ? jer.transform : null);
            if (EconomyManager.I) EconomyManager.I.cash += 300; GameManager.I.Say("Jeremy gave you $300.");
            yield return Line("Jeremy", "I'll come by when I can. Don't die on me, alright?", jer ? jer.transform : null);

            // 6. he drives off; you're on your own
            if (jer) { jer.Face(null); jer.WalkTo(car ? car.transform.TransformPoint(-1.7f, 0f, 0.4f) : homePos); }
            pc.InputLocked = false; Cinematic = false; camOn = false;
            float w2 = 0f; while (jer && !jer.Done && w2 < 6f) { w2 += Time.deltaTime; yield return null; }
            if (jer) { Destroy(jer.gameObject); jer = null; }
            Finish(true);
            if (car && path != null && total > 5f)
            {
                var back = new List<Vector3>(); float cut = Mathf.Max(0f, total - 45f);
                for (int i = path.Count - 1; i >= 0; i--) if (cum[i] >= cut) back.Add(path[i]);
                var bc = new List<float> { 0f }; float bt = 0f; for (int i = 1; i < back.Count; i++) { bt += Vector3.Distance(back[i - 1], back[i]); bc.Add(bt); }
                var fix = car.transform.rotation; float s = 0f, v = 0f;
                while (s < bt - 0.1f && car) { v = Mathf.MoveTowards(v, 7f, 4f * Time.deltaTime); s = Mathf.Min(bt, s + v * Time.deltaTime); var p = Sample(back, bc, s, out _); if (Ground(p, out float gy)) p.y = gy + 0.06f; car.transform.position = p; car.transform.rotation = fix; yield return null; }
            }
            if (car) Destroy(car.gameObject);
            run = null;
        }

        void Finish(bool normal)
        {
            Active = false; Cinematic = false; camOn = false; fade = 0f; subText = null; title = null; objective = null; boardHint = false;
            if (model) { model.gameObject.SetActive(true); model.localRotation = Quaternion.identity; model.localPosition = Vector3.zero; }
            if (cc) cc.enabled = true; if (pc) { pc.InputLocked = false; if (pc.animator) pc.animator.Unfreeze(); }
            if (cam) { cam.enabled = true; } if (camera) camera.fieldOfView = 60f; if (vi) vi.enabled = true;
            var gm = GameManager.I; if (gm && normal) { gm.Banner("DAY ONE", "Three years gone. Your family is dead. Time to make a name for yourself.", 5f); gm.Say("Your phone buzzes. Sal wants to talk."); }
        }

        /// <summary>Backspace: drop everything and put the player at home.</summary>
        void Cleanup()
        {
            if (jer) Destroy(jer.gameObject); if (car) Destroy(car.gameObject); jer = null; car = null;
            if (pc) pc.Teleport(homePos, homeYaw);
            Finish(false); if (cam) cam.SetYaw(homeYaw);
            GameManager.I?.Say("Prologue skipped.");
        }

        // ------------------------------------------------------------------ the route: road graph, lane offset, last stretch to the house
        static bool Ground(Vector3 p, out float y)
        {
            if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var h, 14f, ~0, QueryTriggerInteraction.Ignore)) { y = h.point.y; return true; }
            y = p.y; return false;
        }

        List<Vector3> BuildPath(Vector3 from, Vector3 to)
        {
            var g = FindAnyObjectByType<RoadGraph>(); var pts = new List<Vector3> { from };
            if (g != null && g.roadNodes != null && g.roadNodes.Length > 1)
            {
                int a = g.Nearest(g.roadNodes, from), b = g.Nearest(g.roadNodes, to); var n = g.roadNodes;
                var dist = new float[n.Length]; var prev = new int[n.Length]; var done = new bool[n.Length]; for (int i = 0; i < n.Length; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
                dist[a] = 0f;
                for (int it = 0; it < n.Length; it++)
                {
                    int u = -1; float bd = float.MaxValue; for (int i = 0; i < n.Length; i++) if (!done[i] && dist[i] < bd) { bd = dist[i]; u = i; }
                    if (u < 0 || u == b) break; done[u] = true;
                    foreach (int w in n[u].adj ?? new int[0]) { float d = dist[u] + Vector3.Distance(n[u].pos, n[w].pos); if (d < dist[w]) { dist[w] = d; prev[w] = u; } }
                }
                var chain = new List<int>(); for (int c = b; c >= 0; c = prev[c]) { chain.Add(c); if (c == a) break; }
                chain.Reverse();
                if (chain.Count > 0 && chain[0] == a)
                {
                    for (int i = 0; i < chain.Count; i++)
                    {
                        Vector3 p = n[chain[i]].pos; Vector3 dir = Vector3.zero;
                        if (i > 0) dir += (p - n[chain[i - 1]].pos).normalized; if (i < chain.Count - 1) dir += (n[chain[i + 1]].pos - p).normalized; dir.y = 0f;
                        if (dir.sqrMagnitude > 0.01f) p += Vector3.Cross(Vector3.up, dir.normalized) * 2.3f;   // keep to the right-hand lane
                        pts.Add(p);
                    }
                }
            }
            pts.Add(to);
            // trim the last 4.5 m so the car stops short of where you'll stand
            float cut = 4.5f; while (pts.Count > 2 && cut > 0f)
            {
                float seg = Vector3.Distance(pts[pts.Count - 2], pts[pts.Count - 1]);
                if (seg <= cut) { cut -= seg; pts.RemoveAt(pts.Count - 1); } else { pts[pts.Count - 1] = Vector3.Lerp(pts[pts.Count - 1], pts[pts.Count - 2], cut / seg); cut = 0f; }
            }
            // drop points that sit right on top of each other
            for (int i = pts.Count - 1; i > 0; i--) if (Vector3.Distance(pts[i], pts[i - 1]) < 0.5f) pts.RemoveAt(i);
            return pts;
        }

        static Vector3 Sample(List<Vector3> pts, List<float> cum, float s, out Vector3 tangent)
        {
            int i = 1; while (i < pts.Count - 1 && cum[i] < s) i++;
            if (i >= pts.Count) { tangent = Vector3.forward; return pts[pts.Count - 1]; }
            Vector3 a = pts[i - 1], b = pts[i]; float seg = Mathf.Max(0.001f, cum[i] - cum[i - 1]); float k = Mathf.Clamp01((s - cum[i - 1]) / seg);
            tangent = b - a; tangent.y = 0f; tangent = tangent.sqrMagnitude < 0.0001f ? Vector3.forward : tangent.normalized;
            return Vector3.Lerp(a, b, k);
        }

        // ------------------------------------------------------------------ overlay
        void OnGUI()
        {
            if (!Active) return;
            float W = Screen.width, H = Screen.height;
            if (Cinematic) { float bar = H * 0.09f; GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, W, bar), UiSkin.White); GUI.DrawTexture(new Rect(0, H - bar, W, bar), UiSkin.White); }
            if (fade > 0.001f) { GUI.color = new Color(0, 0, 0, fade); GUI.DrawTexture(new Rect(0, 0, W, H), UiSkin.White); }
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(title))
            {
                var ts = new GUIStyle(UiSkin.Big) { fontSize = Mathf.RoundToInt(H * 0.07f), alignment = TextAnchor.MiddleCenter, richText = true }; ts.normal.textColor = new Color(1f, 1f, 1f, Mathf.Clamp01(1.3f - fade));
                GUI.Label(new Rect(0, H * 0.35f, W, H * 0.3f), title, ts);
            }
            if (subText != null && Time.unscaledTime < subUntil)
            {
                var box = new Rect(W * 0.18f, H * 0.74f, W * 0.64f, H * 0.14f); GUI.color = new Color(0, 0, 0, 0.72f); GUI.DrawTexture(box, UiSkin.White); GUI.color = Color.white;
                var who = new GUIStyle(UiSkin.Big) { fontSize = Mathf.RoundToInt(H * 0.026f), richText = true }; who.normal.textColor = subWho == "Jeremy" ? UiSkin.Accent : new Color(0.7f, 0.85f, 1f);
                if (!string.IsNullOrEmpty(subWho)) GUI.Label(new Rect(box.x + 16, box.y + 8, box.width - 32, 28), subWho, who);
                var tx = new GUIStyle(UiSkin.Big) { fontSize = Mathf.RoundToInt(H * 0.028f), wordWrap = true, richText = true, fontStyle = string.IsNullOrEmpty(subWho) ? FontStyle.Italic : FontStyle.Normal }; tx.normal.textColor = Color.white;
                GUI.Label(new Rect(box.x + 16, box.y + (string.IsNullOrEmpty(subWho) ? 14 : 38), box.width - 32, box.height - 44), subText, tx);
                var hint = new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleRight }; GUI.Label(new Rect(box.xMax - 200, box.yMax - 22, 190, 20), "Space ▸", hint);
            }
            if (!string.IsNullOrEmpty(objective) && !Cinematic)
            {
                var r = new Rect(W - 330, 150, 310, 62); GUI.color = new Color(0.06f, 0.08f, 0.12f, 0.85f); GUI.DrawTexture(r, UiSkin.White); GUI.color = Color.white;
                var h1 = new GUIStyle(UiSkin.Small) { fontSize = 11 }; h1.normal.textColor = UiSkin.Accent; GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 20, 18), "PROLOGUE", h1);
                var h2 = new GUIStyle(UiSkin.Big) { fontSize = 15, wordWrap = true }; h2.normal.textColor = Color.white; GUI.Label(new Rect(r.x + 10, r.y + 24, r.width - 20, 36), objective, h2);
            }
            if (boardHint) { var hs = new GUIStyle(UiSkin.Big) { fontSize = Mathf.RoundToInt(H * 0.03f), alignment = TextAnchor.MiddleCenter }; hs.normal.textColor = Color.white; GUI.Label(new Rect(0, H * 0.78f, W, 40), "E  Get in Jeremy's car", hs); }
            var sk = new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleRight }; GUI.Label(new Rect(W - 260, H - 26, 240, 20), "Backspace: skip prologue", sk);
        }
    }
}
