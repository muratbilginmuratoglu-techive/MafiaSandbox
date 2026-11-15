using System.Collections.Generic;
using UnityEngine;

namespace MafiaSandbox.World
{
    /// <summary>
    /// Port City General: a procedural low-poly hospital (lobby + ward with three beds). Built by CityBuilder on a clear lot near a road;
    /// the Prologue wakes the player here. Local space: +Z is the front (entrance), +X right.
    /// </summary>
    public class Hospital : MonoBehaviour
    {
        public static Hospital I { get; private set; }
        public Vector3 bedFeet, standPos, jeremyStart, jeremyTalk, doorOut, carSpot, wardLook; public float yaw, carYaw, standYaw;
        void Awake() { I = this; }
        /// <summary>True when the point is inside the hospital shell (the camera uses the tight indoor mode there).</summary>
        public static bool Inside(Vector3 p) { if (!I) return false; var l = I.transform.InverseTransformPoint(p); return Mathf.Abs(l.x) < 9.4f && l.z > -7.3f - EXT && l.z < 7.3f && l.y > -1f && l.y < 4.2f; }
        public Vector3 W(float x, float y, float z) => transform.TransformPoint(new Vector3(x, y, z));

        // ------------------------------------------------------------------ site search (editor, before the navmesh bake)
        const float HX = 9f, HZ = 7f, EXT = 5f;   // EXT: how far the building runs further back (doctor wing)
        static bool Ground(Vector3 p, out float y) { if (Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out var h, 120f, ~0, QueryTriggerInteraction.Ignore)) { y = h.point.y; return true; } y = 0f; return false; }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b) { a.y = b.y = p.y = 0f; var ab = b - a; float t = ab.sqrMagnitude < 0.001f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude); return Vector3.Distance(p, a + ab * t); }

        static bool Fits(Vector3 c, float yaw, List<Bounds> buildings, bool strict, out float gy, List<Collider> clear)
        {
            gy = 0f; var rot = Quaternion.Euler(0f, yaw, 0f); var ys = new List<float>();
            for (int ix = -1; ix <= 1; ix++) for (int iz = -2; iz <= 2; iz++)
            {
                var p = c + rot * new Vector3(ix * 9.5f, 0f, iz * 5.5f - 1f); if (!Ground(p, out float y)) return false; ys.Add(y);
            }
            ys.Sort(); gy = ys[ys.Count / 2]; if (ys[ys.Count - 1] - ys[0] > 0.8f) return false;
            Vector3 fwd = rot * Vector3.forward; Vector3 mid = c + fwd * (2.75f - EXT / 2f); mid.y = gy + 2.6f;
            var half = new Vector3(HX + 1.2f, 1.6f, 10.75f + EXT / 2f);
            foreach (var h in Physics.OverlapBox(mid, half, rot, ~0, QueryTriggerInteraction.Ignore))
            {
                var cc = h.GetComponent<CityCollider>();
                if (cc && cc.building) return false;
                if (h is MeshCollider) { if (strict) return false; continue; }
                if (cc) { clear?.Add(h); continue; }
                if (strict) return false;
            }
            foreach (var b in buildings) { var e = b; e.Expand(new Vector3(3f, 0f, 3f)); var fp = new Bounds(c + fwd * (2.75f - EXT / 2f), new Vector3(22f, 40f, 24f + EXT)); if (e.Intersects(fp)) return false; }
            return true;
        }

        /// <summary>Finds a clear lot facing a road, roughly 100 m from `home`. Returns false when nothing fits.</summary>
        public static bool FindSite(RoadGraph g, Vector3 home, List<Bounds> buildings, out Vector3 pos, out float yaw, List<Collider> clear)
        {
            pos = default; yaw = 0f; Physics.SyncTransforms();
            foreach (bool strict in new[] { true, false })
            {
                float best = float.MaxValue; var cl = new List<Collider>();
                for (int i = 0; i < g.roadNodes.Length; i++) foreach (int j in g.roadNodes[i].adj ?? new int[0])
                {
                    if (j <= i) continue; var a = g.roadNodes[i].pos; var b = g.roadNodes[j].pos; var d = b - a; d.y = 0f; float len = d.magnitude; if (len < 24f) continue; d /= len; var right = Vector3.Cross(Vector3.up, d);
                    foreach (float t in new[] { 0.25f, 0.4f, 0.5f, 0.6f, 0.75f }) foreach (float side in new[] { -1f, 1f }) foreach (float dist in new[] { 24f, 26f, 29f, 33f })
                    {
                        var c = a + d * (len * t) + right * side * dist; Vector3 fwd = -right * side; float y = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
                        bool tooClose = false; for (int k = 0; k < g.roadNodes.Length && !tooClose; k++) foreach (int m in g.roadNodes[k].adj ?? new int[0]) if (m > k && SegDist(c, g.roadNodes[k].pos, g.roadNodes[m].pos) < 19f) { tooClose = true; break; }
                        if (tooClose) continue;
                        float hd = Vector3.Distance(new Vector3(c.x, 0f, c.z), new Vector3(home.x, 0f, home.z)); if (hd < 45f) continue;
                        float score = Mathf.Abs(hd - 100f) + dist * 0.2f; if (score >= best) continue;
                        var tmp = new List<Collider>(); if (!Fits(c, y, buildings, strict, out float gy, tmp)) continue;
                        best = score; pos = new Vector3(c.x, gy, c.z); yaw = y; cl = tmp;
                    }
                }
                if (best < float.MaxValue) { clear?.AddRange(cl); return true; }
            }
            return false;
        }

        // ------------------------------------------------------------------ the building
        static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
        static Material Mat(Color c, bool glow = false)
        {
            var key = glow ? new Color(c.r, c.g, c.b, 0.5f) : c; if (mats.TryGetValue(key, out var m) && m) return m;
            m = new Material(Shader.Find("Standard")) { color = c }; m.SetFloat("_Glossiness", glow ? 0.6f : 0.15f);
            if (glow) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 1.4f); }
            mats[key] = m; return m;
        }
        static void Kill(Collider c) { if (!c) return; if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }

        Transform Box(string n, Vector3 lp, Vector3 size, Color col, bool collide = true, bool structural = false, bool glow = false, PrimitiveType t = PrimitiveType.Cube)
        {
            var g = GameObject.CreatePrimitive(t); g.name = n; g.transform.SetParent(transform, false); g.transform.localPosition = lp; g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = Mat(col, glow);
            if (!collide) Kill(g.GetComponent<Collider>());
            else if (structural) { var cc = g.AddComponent<CityCollider>(); cc.opaque = true; cc.building = true; }
            return g.transform;
        }

        public static Hospital Build(Transform parent, Vector3 pos, float yawDeg)
        {
            var go = new GameObject("Hospital: Port City General"); go.transform.SetParent(parent, false); go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDeg, 0f));
            var h = go.AddComponent<Hospital>(); h.yaw = yawDeg; h.BuildParts();
            h.bedFeet = h.W(-4f, 0.86f, -4.55f); h.standPos = h.W(-1.8f, 0.2f, -1.6f); h.standYaw = yawDeg + 59f; h.jeremyStart = h.W(1.4f, 0.2f, 4.6f); h.jeremyTalk = h.W(-2.5f, 0.2f, -4.2f);
            h.doorOut = h.W(0f, 0.1f, 9f); h.carSpot = h.W(-6.5f, 0.05f, 11f); h.carYaw = yawDeg + 90f; h.wardLook = h.W(-3f, 1.2f, -5f);
            return h;
        }

        void Label(string text, Vector3 lp, float size, Color col, float yaw = 180f)
        {
            var g = new GameObject("Label_" + text); g.transform.SetParent(transform, false); g.transform.localPosition = lp; g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var tm = g.AddComponent<TextMesh>(); tm.text = text; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.fontSize = 64; tm.characterSize = size; tm.color = col;
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); if (f) { tm.font = f; var lm = new Material(Shader.Find("Sprites/Default")); lm.mainTexture = f.material.mainTexture; g.GetComponent<MeshRenderer>().sharedMaterial = lm; }   // depth-tested (the stock text shader draws through walls)
        }

        /// <summary>Doctor's wing behind the ward (doctor's office left, treatment room right), plus the signs that make the lobby read as a check-in.</summary>
        void BuildWing(Color white, Color grey, Color wood, Color steel, Color dark, Color glass, Color red)
        {
            var blueSign = new Color(0.15f, 0.4f, 0.65f); var green = new Color(0.2f, 0.65f, 0.4f); var paper = new Color(0.95f, 0.95f, 0.9f);
            float zb = -6.825f - EXT + 0.25f;   // inner face of the back wall
            // signs
            Box("CheckInBoard", new Vector3(-8.62f, 2.45f, 3.6f), new Vector3(0.06f, 0.55f, 3.0f), blueSign, false); Label("CHECK-IN", new Vector3(-8.57f, 2.45f, 3.6f), 0.12f, Color.white, 270f);
            Box("DoctorBoard", new Vector3(-7.8f, 2.95f, -6.62f), new Vector3(1.5f, 0.4f, 0.04f), green, false, false, true); Label("DOCTOR", new Vector3(-7.8f, 2.95f, -6.58f), 0.07f, Color.white);
            Box("TreatBoard", new Vector3(7.6f, 2.95f, -6.62f), new Vector3(1.9f, 0.4f, 0.04f), green, false, false, true); Label("TREATMENT", new Vector3(7.6f, 2.95f, -6.58f), 0.07f, Color.white);
            Label("WAITING AREA", new Vector3(6.2f, 2.6f, 6.62f), 0.08f, new Color(0.15f, 0.3f, 0.5f), 0f);
            // windows on the side walls of the wing
            Box("WindowWingL", new Vector3(-9.02f, 1.9f, -9.3f), new Vector3(0.06f, 1.4f, 2.2f), glass, false); Box("WindowWingR", new Vector3(9.02f, 1.9f, -9.3f), new Vector3(0.06f, 1.4f, 2.2f), glass, false);
            // ---- doctor's office (x -8.65 .. -0.15)
            Box("DocDesk", new Vector3(-4.6f, 0.4f, -9.4f), new Vector3(2.2f, 0.8f, 0.95f), wood); Box("DocDeskTop", new Vector3(-4.6f, 0.83f, -9.4f), new Vector3(2.3f, 0.06f, 1.05f), new Color(0.35f, 0.24f, 0.14f), false);
            Box("DocPC", new Vector3(-4.2f, 1.1f, -9.45f), new Vector3(0.5f, 0.32f, 0.05f), dark, false); Box("DocPCGlow", new Vector3(-4.2f, 1.1f, -9.42f), new Vector3(0.44f, 0.26f, 0.01f), new Color(0.3f, 0.7f, 0.95f), false, false, true);
            Box("DocPaper", new Vector3(-5.2f, 0.87f, -9.35f), new Vector3(0.4f, 0.02f, 0.3f), paper, false);
            Box("DocChair", new Vector3(-4.6f, 0.45f, -10.25f), new Vector3(0.55f, 0.08f, 0.55f), dark); Box("DocChairBack", new Vector3(-4.6f, 0.85f, -10.5f), new Vector3(0.55f, 0.7f, 0.06f), dark, false);
            foreach (float x in new[] { -5.4f, -3.8f }) { Box("PatientChair", new Vector3(x, 0.4f, -8.4f), new Vector3(0.5f, 0.06f, 0.5f), glass); Box("PatientChairBack", new Vector3(x, 0.7f, -8.15f), new Vector3(0.5f, 0.5f, 0.06f), glass, false); }
            Box("FilingCabinet", new Vector3(-8.2f, 0.7f, -8.1f), new Vector3(0.7f, 1.4f, 0.8f), grey); Box("FilingCabinet2", new Vector3(-8.2f, 0.7f, -9.0f), new Vector3(0.7f, 1.4f, 0.8f), grey);
            Box("BookShelf", new Vector3(-2.2f, 1.1f, zb + 0.2f), new Vector3(2.4f, 2.0f, 0.4f), wood);
            for (int i = 0; i < 6; i++) Box("Book", new Vector3(-3.1f + i * 0.35f, 1.5f, zb + 0.43f), new Vector3(0.22f, 0.5f, 0.04f), new Color[] { red, blueSign, green, paper, wood, steel }[i], false);
            Box("ExamCouch", new Vector3(-7.4f, 0.45f, -10.7f), new Vector3(0.8f, 0.7f, 1.9f), white); Box("ExamCouchPad", new Vector3(-7.4f, 0.83f, -10.7f), new Vector3(0.75f, 0.1f, 1.8f), blueSign, false);
            Box("EyeChart", new Vector3(-1.0f, 1.9f, zb + 0.05f), new Vector3(0.7f, 0.9f, 0.03f), paper, false); Box("Diploma", new Vector3(-6.4f, 2.0f, zb + 0.05f), new Vector3(0.8f, 0.55f, 0.03f), new Color(0.85f, 0.75f, 0.4f), false);
            Box("DocPlant", new Vector3(-0.9f, 0.55f, -10.9f), new Vector3(0.45f, 0.8f, 0.45f), new Color(0.2f, 0.5f, 0.25f), false, false, false, PrimitiveType.Cylinder);
            Box("DocRug", new Vector3(-4.6f, 0.17f, -9.2f), new Vector3(3.2f, 0.02f, 2.2f), new Color(0.55f, 0.65f, 0.75f), false);
            // ---- treatment room (x 0.15 .. 8.65)
            for (int i = 0; i < 2; i++)
            {
                float x = 2.6f + i * 3.6f;
                Box("ExamBed", new Vector3(x, 0.45f, -10.2f), new Vector3(1.0f, 0.7f, 2.1f), grey); Box("ExamPad", new Vector3(x, 0.85f, -10.2f), new Vector3(0.95f, 0.1f, 2.0f), white, false);
                Box("ExamLampPole", new Vector3(x + 0.8f, 1.2f, -8.6f), new Vector3(0.05f, 2.4f, 0.05f), steel, false); Box("ExamLampHead", new Vector3(x + 0.8f, 2.35f, -8.8f), new Vector3(0.5f, 0.12f, 0.5f), new Color(1f, 1f, 0.9f), false, false, true);
            }
            Box("TreatCurtain", new Vector3(4.4f, 1.75f, -9.8f), new Vector3(0.05f, 2.2f, 2.8f), new Color(0.65f, 0.82f, 0.88f), false);
            Box("XRayBase", new Vector3(1.2f, 0.9f, -8.0f), new Vector3(0.9f, 1.8f, 0.7f), steel); Box("XRayArm", new Vector3(1.9f, 1.7f, -8.0f), new Vector3(1.0f, 0.2f, 0.25f), grey, false); Box("XRayScreen", new Vector3(1.2f, 1.3f, -7.62f), new Vector3(0.5f, 0.4f, 0.03f), new Color(0.3f, 0.7f, 0.95f), false, false, true);
            Box("MedCartTop", new Vector3(7.3f, 0.85f, -8.0f), new Vector3(0.9f, 0.06f, 0.55f), steel); Box("MedCartBody", new Vector3(7.3f, 0.42f, -8.0f), new Vector3(0.85f, 0.8f, 0.5f), white);
            Box("TreatSink", new Vector3(8.4f, 0.9f, -11.0f), new Vector3(0.5f, 0.12f, 0.8f), white); Box("TreatSinkBase", new Vector3(8.4f, 0.45f, -11.0f), new Vector3(0.5f, 0.9f, 0.9f), grey);
            Box("TreatCross", new Vector3(8.55f, 2.0f, -9.0f), new Vector3(0.03f, 0.5f, 0.15f), red, false); Box("TreatCross2", new Vector3(8.55f, 2.0f, -9.0f), new Vector3(0.03f, 0.15f, 0.5f), red, false);
        }

        // ------------------------------------------------------------------ the staff: a receptionist at check-in and a doctor in the office (runtime)
        void Start() { if (Application.isPlaying) StartCoroutine(SpawnStaff()); }
        System.Collections.IEnumerator SpawnStaff()
        {
            yield return new WaitForSeconds(1.0f);
            var sp = FindAnyObjectByType<MafiaSandbox.Npc.NpcSpawner>(); if (!sp || sp.prefabs == null || sp.prefabs.Length == 0) yield break;
            GameObject Find(string pre) { foreach (var p in sp.prefabs) if (p && p.name.StartsWith(pre)) return p; return sp.prefabs[0]; }
            void Place(string pre, Vector3 lp, string pose, string nm, Vector3 faceLocal)
            {
                var pos = W(lp.x, lp.y, lp.z); var face = W(faceLocal.x, faceLocal.y, faceLocal.z);
                var c = sp.Spawn<MafiaSandbox.Npc.CamperBrain>(Find(pre), pos, Mathf.Atan2(face.x - pos.x, face.z - pos.z) * Mathf.Rad2Deg);
                c.homeSpot = c.transform.position; c.pose = pose; c.fire = face; c.displayName = nm; c.walkSpeed = 1.4f;
            }
            Place("Doktor_01", new Vector3(-4.6f, 0.2f, -10.9f), "Idle", "Dr. Hale", new Vector3(-4.6f, 0.2f, 0f));
            Place("Doktor_02", new Vector3(-3.3f, 0.2f, 2.5f), "Idle", "Nurse (check-in)", new Vector3(-3.3f, 0.2f, 9f));
        }

        void BuildParts()
        {
            var white = new Color(0.93f, 0.95f, 0.97f); var tile = new Color(0.8f, 0.86f, 0.88f); var red = new Color(0.82f, 0.12f, 0.14f); var glass = new Color(0.25f, 0.45f, 0.6f);
            var wood = new Color(0.5f, 0.36f, 0.22f); var grey = new Color(0.55f, 0.58f, 0.62f); var paving = new Color(0.62f, 0.63f, 0.65f);
            // floor, apron, ceiling
            Box("Floor", new Vector3(0, 0.075f, -EXT / 2f), new Vector3(18.8f, 0.15f, 14.8f + EXT), tile);
            Box("Apron", new Vector3(0, 0.03f, 9.3f), new Vector3(9f, 0.06f, 4f), paving, false);
            Box("Ceiling", new Vector3(0, 3.85f, -EXT / 2f), new Vector3(18.8f, 0.2f, 14.8f + EXT), white, true, true);
            // outer walls (door gap 3.4 m wide, 2.6 m high on the front)
            float cy = 1.95f;
            Box("WallBack", new Vector3(0, cy, -6.825f - EXT), new Vector3(18f, 3.6f, 0.35f), white, true, true);
            // ward back wall (now an inner wall) with a door to each room of the doctor wing: left x -8.4..-7.2, right x 7.0..8.2
            Box("WardBackA", new Vector3(-8.525f, cy, -6.825f), new Vector3(0.25f, 3.6f, 0.3f), white, true, true);
            Box("WardBackB", new Vector3(-0.1f, cy, -6.825f), new Vector3(14.2f, 3.6f, 0.3f), white, true, true);
            Box("WardBackC", new Vector3(8.425f, cy, -6.825f), new Vector3(0.45f, 3.6f, 0.3f), white, true, true);
            Box("DoorLintelL", new Vector3(-7.8f, 3.25f, -6.825f), new Vector3(1.2f, 1f, 0.3f), white, true, true); Box("DoorLintelR", new Vector3(7.6f, 3.25f, -6.825f), new Vector3(1.2f, 1f, 0.3f), white, true, true);
            Box("RearSplit", new Vector3(0f, cy, -6.95f - EXT / 2f + 0.1f), new Vector3(0.25f, 3.6f, EXT - 0.4f), white, true, true);
            Box("WallLeft", new Vector3(-8.825f, cy, -EXT / 2f), new Vector3(0.35f, 3.6f, 14f + EXT), white, true, true);
            Box("WallRight", new Vector3(8.825f, cy, -EXT / 2f), new Vector3(0.35f, 3.6f, 14f + EXT), white, true, true);
            Box("WallFrontL", new Vector3(-5.35f, cy, 6.825f), new Vector3(7.3f, 3.6f, 0.35f), white, true, true);
            Box("WallFrontR", new Vector3(5.35f, cy, 6.825f), new Vector3(7.3f, 3.6f, 0.35f), white, true, true);
            Box("Lintel", new Vector3(0, 3.25f, 6.825f), new Vector3(3.4f, 1f, 0.35f), white, true, true);
            // windows
            foreach (float x in new[] { -7f, -3.7f, 3.7f, 7f }) Box("Window", new Vector3(x, 1.9f, 7.02f), new Vector3(2.4f, 1.4f, 0.06f), glass, false);
            foreach (float z in new[] { -3.5f, 2.5f }) { Box("WindowL", new Vector3(-9.02f, 1.9f, z), new Vector3(0.06f, 1.4f, 2.2f), glass, false); Box("WindowR", new Vector3(9.02f, 1.9f, z), new Vector3(0.06f, 1.4f, 2.2f), glass, false); }
            Box("StripeL", new Vector3(-5.35f, 0.75f, 7.02f), new Vector3(7.3f, 0.22f, 0.05f), red, false); Box("StripeR", new Vector3(5.35f, 0.75f, 7.02f), new Vector3(7.3f, 0.22f, 0.05f), red, false);   // split around the door gap
            // partition between lobby and ward (door gap x 0.5..2.3)
            Box("PartL", new Vector3(-4.075f, cy, 0.4f), new Vector3(9.15f, 3.6f, 0.25f), white, true, true);
            Box("PartR", new Vector3(5.475f, cy, 0.4f), new Vector3(6.35f, 3.6f, 0.25f), white, true, true);
            Box("PartLintel", new Vector3(1.4f, 3.25f, 0.4f), new Vector3(1.8f, 1f, 0.25f), white, true, true);
            // roof sign: white board with a red cross, canopy over the door
            Box("SignBoard", new Vector3(0, 5.35f, 6.5f), new Vector3(5.5f, 2.3f, 0.3f), white, false);
            Box("SignPost", new Vector3(0, 4.35f, 6.5f), new Vector3(0.5f, 1f, 0.3f), grey, false);
            Box("CrossV", new Vector3(0, 5.35f, 6.68f), new Vector3(0.6f, 1.8f, 0.1f), red, false, false, true);
            Box("CrossH", new Vector3(0, 5.35f, 6.68f), new Vector3(1.8f, 0.6f, 0.1f), red, false, false, true);
            Box("Canopy", new Vector3(0, 3.15f, 8.5f), new Vector3(7f, 0.18f, 3.4f), red, false);
            Box("PillarL", new Vector3(-3.2f, 1.6f, 9.9f), new Vector3(0.25f, 3.1f, 0.25f), white);
            Box("PillarR", new Vector3(3.2f, 1.6f, 9.9f), new Vector3(0.25f, 3.1f, 0.25f), white);
            Box("AC", new Vector3(4f, 4.5f, -2f), new Vector3(2.2f, 1f, 1.8f), grey, false);
            // lobby: reception desk, chairs, plant
            Box("Reception", new Vector3(-4.5f, 0.65f, 3.6f), new Vector3(4.2f, 1f, 0.9f), wood);
            Box("DeskTop", new Vector3(-4.5f, 1.17f, 3.6f), new Vector3(4.4f, 0.06f, 1.05f), white, false);
            for (int i = 0; i < 4; i++) { Box("Chair", new Vector3(4.2f + i * 1.0f, 0.4f, 6.2f), new Vector3(0.6f, 0.5f, 0.6f), glass, false); Box("ChairBack", new Vector3(4.2f + i * 1.0f, 0.8f, 6.45f), new Vector3(0.6f, 0.5f, 0.08f), glass, false); }
            Box("Plant", new Vector3(7.6f, 0.55f, 2f), new Vector3(0.5f, 0.8f, 0.5f), new Color(0.2f, 0.5f, 0.25f), false, false, false, PrimitiveType.Cylinder);
            // ward: three beds, monitor, IV stand
            for (int i = 0; i < 3; i++)
            {
                float x = -4f + i * 4f;
                Box("BedFrame", new Vector3(x, 0.425f, -5.4f), new Vector3(1.0f, 0.55f, 2.1f), grey);
                Box("Mattress", new Vector3(x, 0.775f, -5.4f), new Vector3(0.95f, 0.15f, 2f), white, false);
                Box("Pillow", new Vector3(x, 0.9f, -6.2f), new Vector3(0.6f, 0.12f, 0.35f), new Color(0.85f, 0.9f, 0.95f), false);
                Box("Headboard", new Vector3(x, 0.95f, -6.55f), new Vector3(1.05f, 0.9f, 0.08f), grey, false);
            }
            Box("Monitor", new Vector3(-5.5f, 1.0f, -6.2f), new Vector3(0.4f, 1.3f, 0.3f), new Color(0.15f, 0.16f, 0.18f));
            Box("MonitorScreen", new Vector3(-5.5f, 1.35f, -6.04f), new Vector3(0.3f, 0.22f, 0.02f), new Color(0.2f, 0.95f, 0.45f), false, false, true);
            Box("IVPole", new Vector3(-3.05f, 1.1f, -6.1f), new Vector3(0.04f, 1.9f, 0.04f), grey, false);
            Box("IVBag", new Vector3(-3.05f, 1.85f, -6.1f), new Vector3(0.16f, 0.25f, 0.08f), new Color(0.7f, 0.85f, 1f), false);
            // ---- detail: ward
            var blue = new Color(0.55f, 0.75f, 0.85f); var blanket = new Color(0.35f, 0.62f, 0.75f); var steel = new Color(0.75f, 0.78f, 0.8f); var dark = new Color(0.12f, 0.13f, 0.15f);
            for (int i = 0; i < 3; i++)
            {
                float x = -4f + i * 4f;
                Box("Blanket", new Vector3(x, 0.89f, -5.0f), new Vector3(0.98f, 0.1f, 1.2f), blanket, false);
                Box("RailL", new Vector3(x - 0.52f, 0.95f, -5.3f), new Vector3(0.04f, 0.3f, 1.4f), steel, false); Box("RailR", new Vector3(x + 0.52f, 0.95f, -5.3f), new Vector3(0.04f, 0.3f, 1.4f), steel, false);
                Box("BedsideTable", new Vector3(x + 1.2f, 0.4f, -6.35f), new Vector3(0.55f, 0.8f, 0.45f), white); Box("Drawer", new Vector3(x + 1.2f, 0.55f, -6.11f), new Vector3(0.45f, 0.2f, 0.02f), grey, false);
                Box("Flowers", new Vector3(x + 1.2f, 0.95f, -6.35f), new Vector3(0.2f, 0.25f, 0.2f), new Color(0.9f, 0.45f, 0.55f), false); Box("Vase", new Vector3(x + 1.2f, 0.84f, -6.35f), new Vector3(0.14f, 0.14f, 0.14f), glass, false);
                Box("LampWall", new Vector3(x, 1.9f, -6.7f), new Vector3(0.6f, 0.08f, 0.1f), new Color(1f, 1f, 0.9f), false, false, true);
            }
            foreach (float cx in new[] { -2f, 2f }) Box("Curtain", new Vector3(cx, 1.75f, -5.4f), new Vector3(0.05f, 2.2f, 2.0f), new Color(0.65f, 0.82f, 0.88f), false);
            Box("CurtainRail", new Vector3(0f, 2.9f, -5.4f), new Vector3(12.5f, 0.05f, 0.05f), steel, false);
            Box("MonitorArm", new Vector3(-5.5f, 1.35f, -6.04f), new Vector3(0.38f, 0.04f, 0.02f), new Color(1f, 0.3f, 0.3f), false, false, true);
            Box("Sink", new Vector3(8.4f, 0.9f, -3.2f), new Vector3(0.5f, 0.12f, 0.8f), white); Box("SinkBase", new Vector3(8.4f, 0.45f, -3.2f), new Vector3(0.5f, 0.9f, 0.9f), grey); Box("SinkMirror", new Vector3(8.7f, 1.6f, -3.2f), new Vector3(0.04f, 0.9f, 0.8f), new Color(0.78f, 0.9f, 0.96f), false);
            Box("MedCabinet", new Vector3(8.5f, 1.3f, 0f), new Vector3(0.4f, 1.6f, 1.6f), white); Box("MedCross", new Vector3(8.29f, 1.45f, 0f), new Vector3(0.03f, 0.4f, 0.1f), red, false); Box("MedCross2", new Vector3(8.29f, 1.45f, 0f), new Vector3(0.03f, 0.1f, 0.4f), red, false);
            Box("Trolley", new Vector3(6.8f, 0.5f, -2.2f), new Vector3(1.0f, 0.06f, 0.6f), steel); Box("TrolleyLegs", new Vector3(6.8f, 0.25f, -2.2f), new Vector3(0.9f, 0.5f, 0.5f), grey, false); Box("TrolleyTray", new Vector3(6.8f, 0.56f, -2.2f), new Vector3(0.5f, 0.05f, 0.3f), new Color(0.9f, 0.9f, 0.4f), false);
            Box("WheelchairSeat", new Vector3(6.6f, 0.5f, -4.3f), new Vector3(0.55f, 0.08f, 0.55f), dark); Box("WheelchairBack", new Vector3(6.6f, 0.85f, -4.58f), new Vector3(0.55f, 0.7f, 0.06f), dark, false); Box("WheelL", new Vector3(6.28f, 0.3f, -4.35f), new Vector3(0.06f, 0.6f, 0.6f), grey, false); Box("WheelR", new Vector3(6.92f, 0.3f, -4.35f), new Vector3(0.06f, 0.6f, 0.6f), grey, false);
            Box("Handrail", new Vector3(0f, 0.95f, -6.62f), new Vector3(16f, 0.05f, 0.08f), steel, false); Box("HandrailL", new Vector3(-8.62f, 0.95f, -3f), new Vector3(0.08f, 0.05f, 6f), steel, false);
            Box("Defib", new Vector3(8.62f, 1.4f, 3.2f), new Vector3(0.12f, 0.35f, 0.5f), red, false); Box("DefibSign", new Vector3(8.55f, 1.9f, 3.2f), new Vector3(0.05f, 0.3f, 0.3f), new Color(0.2f, 0.75f, 0.35f), false, false, true);
            // ---- detail: lobby
            Box("ReceptionPC", new Vector3(-4.0f, 1.35f, 3.5f), new Vector3(0.5f, 0.32f, 0.05f), dark, false); Box("ReceptionPCGlow", new Vector3(-4.0f, 1.35f, 3.47f), new Vector3(0.44f, 0.26f, 0.01f), new Color(0.3f, 0.7f, 0.95f), false, false, true);
            Box("ReceptionBell", new Vector3(-5.6f, 1.23f, 3.5f), new Vector3(0.14f, 0.1f, 0.14f), new Color(0.95f, 0.8f, 0.25f), false); Box("Clipboard", new Vector3(-3.0f, 1.21f, 3.7f), new Vector3(0.3f, 0.02f, 0.4f), wood, false);
            Box("ReceptionChair", new Vector3(-4.5f, 0.45f, 2.7f), new Vector3(0.5f, 0.08f, 0.5f), dark); Box("ReceptionChairBack", new Vector3(-4.5f, 0.85f, 2.5f), new Vector3(0.5f, 0.6f, 0.06f), dark, false);
            Box("Vending", new Vector3(7.7f, 0.95f, 4.2f), new Vector3(0.9f, 1.9f, 0.8f), new Color(0.2f, 0.4f, 0.65f)); Box("VendingGlass", new Vector3(7.7f, 1.15f, 3.78f), new Vector3(0.6f, 1.1f, 0.03f), glass, false, false, true);
            Box("WaterCooler", new Vector3(-8.0f, 0.55f, 5.8f), new Vector3(0.4f, 1.1f, 0.4f), white); Box("WaterBottle", new Vector3(-8.0f, 1.3f, 5.8f), new Vector3(0.28f, 0.4f, 0.28f), glass, false);
            Box("Bench2", new Vector3(-7.3f, 0.45f, 1.6f), new Vector3(0.5f, 0.07f, 2.4f), glass); Box("Bench2Back", new Vector3(-7.7f, 0.85f, 1.6f), new Vector3(0.06f, 0.5f, 2.4f), glass, false);
            Box("PlantB", new Vector3(-8.1f, 0.55f, 3.6f), new Vector3(0.5f, 0.8f, 0.5f), new Color(0.2f, 0.5f, 0.25f), false, false, false, PrimitiveType.Cylinder);
            Box("InfoBoard", new Vector3(5.5f, 1.8f, 0.52f), new Vector3(2.2f, 1.1f, 0.04f), new Color(0.15f, 0.35f, 0.55f), false); Box("InfoStripe", new Vector3(5.5f, 1.8f, 0.55f), new Vector3(1.9f, 0.1f, 0.01f), white, false);
            Box("WardSign", new Vector3(1.4f, 3.1f, 0.58f), new Vector3(1.6f, 0.4f, 0.04f), new Color(0.2f, 0.65f, 0.4f), false, false, true);
            Box("ExitSign", new Vector3(0f, 3.1f, 6.6f), new Vector3(1.0f, 0.3f, 0.04f), new Color(0.2f, 0.9f, 0.4f), false, false, true);
            Box("Clock", new Vector3(-8.7f, 2.9f, -0.5f), new Vector3(0.04f, 0.5f, 0.5f), white, false); Box("ClockHand", new Vector3(-8.66f, 2.9f, -0.5f), new Vector3(0.02f, 0.2f, 0.03f), dark, false);
            Box("PosterL", new Vector3(8.7f, 2.0f, 1.6f), new Vector3(0.04f, 0.9f, 0.7f), new Color(0.9f, 0.85f, 0.5f), false);
            for (int i = 0; i < 5; i++) Box("FloorLine", new Vector3(1.4f, 0.16f, 5.5f - i * 1.4f), new Vector3(0.3f, 0.01f, 1.0f), i % 2 == 0 ? new Color(0.2f, 0.65f, 0.4f) : new Color(0.3f, 0.5f, 0.85f), false);   // follow the green line to the ward
            Box("EntryMat", new Vector3(0f, 0.17f, 6.2f), new Vector3(2.6f, 0.02f, 1.2f), new Color(0.2f, 0.22f, 0.26f), false);
            foreach (float x in new[] { -3f, 3f }) Box("VentCover", new Vector3(x, 3.72f, 3.5f), new Vector3(0.7f, 0.04f, 0.7f), grey, false);
            BuildWing(white, grey, wood, steel, dark, glass, red);
            // lights
            foreach (var lp in new[] { new Vector3(-3f, 3.55f, -3.5f), new Vector3(3f, 3.55f, -3.5f), new Vector3(-3f, 3.55f, 3.5f), new Vector3(4f, 3.55f, 3.5f), new Vector3(-4.3f, 3.55f, -9.3f), new Vector3(4.3f, 3.55f, -9.3f) })
            {
                Box("Fluorescent", lp + Vector3.up * 0.25f, new Vector3(1.6f, 0.05f, 0.4f), new Color(1f, 1f, 0.95f), false, false, true);
                var l = new GameObject("Light").AddComponent<Light>(); l.transform.SetParent(transform, false); l.transform.localPosition = lp; l.type = LightType.Point; l.range = 9f; l.intensity = 0.9f; l.color = new Color(1f, 0.97f, 0.9f); l.shadows = LightShadows.None;
            }
        }
    }
}
