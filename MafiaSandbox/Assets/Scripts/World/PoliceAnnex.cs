using System.Collections.Generic;
using UnityEngine;

namespace MafiaSandbox.World
{
    /// <summary>
    /// Extension of the police station: a booking &amp; holding wing built against the side of the old station (own street door),
    /// plus roof sign, beacon, flag pole and bollards on the plaza. Local space: +Z front (street), +X toward the old station.
    /// </summary>
    public class PoliceAnnex : MonoBehaviour
    {
        public static PoliceAnnex I { get; private set; }
        public const float W = 8.6f, D = 11.5f, H = 3.4f;
        void Awake() { I = this; }
        public static bool Inside(Vector3 p) { if (!I) return false; var l = I.transform.InverseTransformPoint(p); return Mathf.Abs(l.x) < W / 2f + 0.2f && Mathf.Abs(l.z) < D / 2f + 0.2f && l.y > -1f && l.y < 4.2f; }
        public Vector3 L(float x, float y, float z) => transform.TransformPoint(new Vector3(x, y, z));

        static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
        static Material Mat(Color c, bool glow)
        {
            var key = glow ? new Color(c.r, c.g, c.b, 0.5f) : c; if (mats.TryGetValue(key, out var m) && m) return m;
            m = new Material(Shader.Find("Standard")) { color = c }; m.SetFloat("_Glossiness", glow ? 0.6f : 0.15f);
            if (glow) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 1.4f); }
            mats[key] = m; return m;
        }
        Transform Box(Transform parent, string n, Vector3 lp, Vector3 size, Color col, bool collide = true, bool structural = false, bool glow = false, PrimitiveType t = PrimitiveType.Cube)
        {
            var g = GameObject.CreatePrimitive(t); g.name = n; g.transform.SetParent(parent, false); g.transform.localPosition = lp; g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = Mat(col, glow);
            var c = g.GetComponent<Collider>();
            if (!collide) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
            else if (structural) { var cc = g.AddComponent<CityCollider>(); cc.opaque = true; cc.building = true; }
            return g.transform;
        }
        Transform Box(string n, Vector3 lp, Vector3 size, Color col, bool collide = true, bool structural = false, bool glow = false, PrimitiveType t = PrimitiveType.Cube) => Box(transform, n, lp, size, col, collide, structural, glow, t);
        static void Text(Transform parent, string text, Vector3 lp, float size, Color col, float yaw)
        {
            var g = new GameObject("Label_" + text); g.transform.SetParent(parent, false); g.transform.localPosition = lp; g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var tm = g.AddComponent<TextMesh>(); tm.text = text; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.fontSize = 64; tm.characterSize = size; tm.color = col;
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); if (f) { tm.font = f; var lm = new Material(Shader.Find("Sprites/Default")); lm.mainTexture = f.material.mainTexture; g.GetComponent<MeshRenderer>().sharedMaterial = lm; }   // depth-tested (the stock text shader draws through walls)
        }

        /// <summary>Builds the wing and the plaza props. `station` is the old station's centre (Unity), `halfX` half its width along X, `halfZ` along Z.</summary>
        public static PoliceAnnex Build(Transform parent, Vector3 station, float halfX, float halfZ, Transform props)
        {
            var go = new GameObject("Police annex: Booking & Holding"); go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(station.x - halfX - W / 2f, station.y, station.z);
            var a = go.AddComponent<PoliceAnnex>(); a.BuildParts(station, halfX, halfZ); return a;
        }

        void BuildParts(Vector3 station, float halfX, float halfZ)
        {
            var wall = new Color(0.78f, 0.8f, 0.84f); var blue = new Color(0.16f, 0.28f, 0.6f); var floorC = new Color(0.45f, 0.47f, 0.5f); var dark = new Color(0.14f, 0.15f, 0.18f);
            var wood = new Color(0.4f, 0.28f, 0.17f); var steel = new Color(0.62f, 0.65f, 0.7f); var glass = new Color(0.45f, 0.65f, 0.85f); var paper = new Color(0.95f, 0.95f, 0.88f);
            var red = new Color(0.8f, 0.15f, 0.15f); var bars = new Color(0.25f, 0.26f, 0.3f); var cot = new Color(0.55f, 0.5f, 0.42f);
            float hw = W / 2f, hd = D / 2f, cy = H / 2f + 0.15f;
            // shell
            Box("Floor", new Vector3(0, 0.075f, 0), new Vector3(W + 0.2f, 0.15f, D + 0.2f), floorC);
            Box("Ceiling", new Vector3(0, H + 0.25f, 0), new Vector3(W + 0.4f, 0.25f, D + 0.4f), wall, true, true);
            Box("WallBack", new Vector3(0, cy, -hd), new Vector3(W + 0.3f, H, 0.3f), wall, true, true);
            Box("WallLeft", new Vector3(-hw, cy, 0), new Vector3(0.3f, H, D), wall, true, true);
            Box("WallRight", new Vector3(hw - 0.05f, cy, 0), new Vector3(0.3f, H, D), wall, true, true);
            // street front with a 2.2 m door at x = -1.9
            Box("WallFrontL", new Vector3(-3.55f, cy, hd), new Vector3(1.6f, H, 0.3f), wall, true, true);
            Box("WallFrontR", new Vector3(1.35f, cy, hd), new Vector3(5.8f, H, 0.3f), wall, true, true);
            Box("DoorLintel", new Vector3(-1.9f, 2.95f, hd), new Vector3(2.2f, 1.0f, 0.3f), wall, true, true);
            Box("BlueBandFront", new Vector3(0, 2.75f, hd + 0.17f), new Vector3(W + 0.3f, 0.45f, 0.06f), blue, false);
            foreach (float x in new[] { 0.6f, 2.8f }) Box("Window", new Vector3(x, 1.8f, hd + 0.17f), new Vector3(1.6f, 1.2f, 0.06f), glass, false);
            Box("Canopy", new Vector3(-1.9f, 3.0f, hd + 1.2f), new Vector3(3.2f, 0.14f, 2.4f), blue, false);
            Box("Step", new Vector3(-1.9f, 0.06f, hd + 0.9f), new Vector3(3f, 0.12f, 1.6f), new Color(0.7f, 0.7f, 0.72f), false);
            // roof sign + beacon on the annex and on the old station
            Box("RoofSignBoard", new Vector3(0, H + 1.1f, hd - 0.4f), new Vector3(5.6f, 1.2f, 0.25f), blue, false);
            Text(transform, "POLICE", new Vector3(0, H + 1.1f, hd - 0.2f), 0.2f, Color.white, 180f);
            Text(transform, "BOOKING & HOLDING", new Vector3(-1.9f, 2.55f, hd + 0.2f), 0.045f, Color.white, 180f);
            Box("BeaconRed", new Vector3(-0.35f, H + 0.65f, hd - 1.2f), new Vector3(0.5f, 0.35f, 0.4f), red, false, false, true); Box("BeaconBlue", new Vector3(0.35f, H + 0.65f, hd - 1.2f), new Vector3(0.5f, 0.35f, 0.4f), new Color(0.2f, 0.4f, 1f), false, false, true);
            // lights
            for (int i = 0; i < 4; i++) Box("Light", new Vector3(i % 2 == 0 ? -1.8f : 1.8f, H + 0.05f, i < 2 ? 3.2f : -2.4f), new Vector3(1.4f, 0.06f, 0.35f), new Color(1f, 0.98f, 0.9f), false, false, true);

            // ---- front office (z 0.7 .. 5.5): booking counter, desk sergeant, wanted board, bench
            Box("BookingCounter", new Vector3(-0.3f, 0.6f, 2.0f), new Vector3(4.6f, 1.0f, 0.8f), wood); Box("CounterTop", new Vector3(-0.3f, 1.13f, 2.0f), new Vector3(4.8f, 0.07f, 1.0f), new Color(0.3f, 0.22f, 0.14f), false);
            Box("BookingPC", new Vector3(0.9f, 1.4f, 1.9f), new Vector3(0.5f, 0.32f, 0.05f), dark, false); Box("BookingPCGlow", new Vector3(0.9f, 1.4f, 2.03f), new Vector3(0.44f, 0.26f, 0.01f), new Color(0.3f, 0.7f, 0.95f), false, false, true);
            Box("SergeantChair", new Vector3(-0.3f, 0.45f, 1.1f), new Vector3(0.5f, 0.08f, 0.5f), dark); Box("SergeantChairBack", new Vector3(-0.3f, 0.85f, 0.88f), new Vector3(0.5f, 0.6f, 0.06f), dark, false);
            Box("WantedBoard", new Vector3(-hw + 0.2f, 1.9f, 3.4f), new Vector3(0.06f, 1.2f, 2.0f), new Color(0.35f, 0.25f, 0.15f), false);
            for (int i = 0; i < 4; i++) Box("WantedPoster", new Vector3(-hw + 0.25f, 1.9f + (i % 2) * 0.3f - 0.15f, 2.75f + i * 0.45f), new Vector3(0.03f, 0.35f, 0.3f), paper, false);
            Text(transform, "WANTED", new Vector3(-hw + 0.27f, 2.35f, 3.4f), 0.05f, red, 270f);
            Box("WaitBench", new Vector3(-2.9f, 0.3f, 4.8f), new Vector3(2.0f, 0.12f, 0.6f), wood); Box("WaitBenchLegA", new Vector3(-3.7f, 0.15f, 4.8f), new Vector3(0.1f, 0.3f, 0.5f), dark, false); Box("WaitBenchLegB", new Vector3(-2.1f, 0.15f, 4.8f), new Vector3(0.1f, 0.3f, 0.5f), dark, false);
            Box("NoticeBoard", new Vector3(3.0f, 1.8f, hd - 0.2f), new Vector3(1.4f, 0.9f, 0.05f), new Color(0.35f, 0.25f, 0.15f), false);
            Box("Plant", new Vector3(3.7f, 0.5f, 4.9f), new Vector3(0.45f, 0.8f, 0.45f), new Color(0.2f, 0.5f, 0.25f), false, false, false, PrimitiveType.Cylinder);
            Box("WaterCooler", new Vector3(3.7f, 0.55f, 1.2f), new Vector3(0.35f, 1.0f, 0.35f), new Color(0.7f, 0.85f, 0.95f));
            // ---- partition between the office and the back (door gap x -0.6 .. 0.6)
            Box("PartL", new Vector3(-2.45f, cy, 0.0f), new Vector3(3.7f, H, 0.2f), wall, true, true); Box("PartR", new Vector3(2.45f, cy, 0.0f), new Vector3(3.7f, H, 0.2f), wall, true, true); Box("PartLintel", new Vector3(0, 2.95f, 0f), new Vector3(1.2f, 1.0f, 0.2f), wall, true, true);
            Text(transform, "HOLDING", new Vector3(0, 3.0f, 0.12f), 0.045f, red, 180f);
            // ---- holding cells along the back wall (three 1.6 x 2.4 cells, left side); bars face the corridor at z = -2.9
            float cz = -hd + 1.4f;
            for (int i = 0; i < 3; i++)
            {
                float x = -hw + 0.95f + i * 1.75f;
                Box("CellCot", new Vector3(x - 0.2f, 0.4f, cz - 0.2f), new Vector3(0.7f, 0.2f, 1.7f), cot); Box("CellMat", new Vector3(x - 0.2f, 0.53f, cz - 0.2f), new Vector3(0.65f, 0.08f, 1.6f), new Color(0.3f, 0.35f, 0.42f), false);
                Box("CellToilet", new Vector3(x + 0.5f, 0.3f, -hd + 0.4f), new Vector3(0.35f, 0.5f, 0.4f), steel, false);
                Box("CellDivider", new Vector3(x + 0.875f, cy, cz), new Vector3(0.12f, H, 2.7f), wall, true, true);
                for (int b = 0; b < 8; b++) Box("Bar", new Vector3(x - 0.7f + b * 0.2f, 1.55f, cz + 1.35f), new Vector3(0.04f, 2.8f, 0.04f), bars, i == 1 && b >= 3 && b <= 4 ? false : true);   // the middle cell's door gap has no collider -> not used, bars stay solid
                Box("BarTop", new Vector3(x, 2.95f, cz + 1.35f), new Vector3(1.75f, 0.06f, 0.06f), bars, false); Box("BarMid", new Vector3(x, 1.0f, cz + 1.35f), new Vector3(1.75f, 0.05f, 0.05f), bars, false);
            }
            Text(transform, "CELL 1   CELL 2   CELL 3", new Vector3(-hw + 2.7f, 3.15f, cz + 1.4f), 0.03f, Color.white, 180f);
            // ---- interrogation room (right-back corner x 1.4 .. hw, z -hd .. -1.2), door facing the corridor
            Box("InterWallFront", new Vector3(2.75f, cy, -1.2f), new Vector3(2.9f, H, 0.15f), wall, true, true);
            Box("InterWallSide", new Vector3(1.35f, cy, -3.5f), new Vector3(0.15f, H, 4.6f), wall, true, true);
            Box("InterDoorFrame", new Vector3(1.35f, 2.95f, -1.0f), new Vector3(0.15f, 1.0f, 0.9f), wall, false);
            Box("InterTable", new Vector3(2.9f, 0.45f, -3.3f), new Vector3(1.5f, 0.07f, 0.8f), steel); Box("InterTableLegA", new Vector3(2.3f, 0.22f, -3.3f), new Vector3(0.08f, 0.4f, 0.7f), dark, false); Box("InterTableLegB", new Vector3(3.5f, 0.22f, -3.3f), new Vector3(0.08f, 0.4f, 0.7f), dark, false);
            foreach (float z in new[] { -2.5f, -4.1f }) { Box("InterChair", new Vector3(2.9f, 0.4f, z), new Vector3(0.5f, 0.08f, 0.5f), dark); Box("InterChairBack", new Vector3(2.9f, 0.75f, z + (z > -3.3f ? 0.22f : -0.22f)), new Vector3(0.5f, 0.6f, 0.06f), dark, false); }
            Box("InterLamp", new Vector3(2.9f, 2.4f, -3.3f), new Vector3(0.5f, 0.1f, 0.5f), new Color(1f, 0.95f, 0.8f), false, false, true);
            Box("TwoWayMirror", new Vector3(hw - 0.05f, 1.8f, -3.3f), new Vector3(0.04f, 1.0f, 2.2f), new Color(0.3f, 0.4f, 0.5f), false);
            Text(transform, "INTERROGATION", new Vector3(2.75f, 3.0f, -1.1f), 0.035f, Color.white, 180f);
            // ---- corridor detectives' bullpen (z -1.2 .. -2.8 is the walkway; desks between the partition and the cells)
            foreach (float x in new[] { -2.6f, -0.9f })
            {
                Box("DetectiveDesk", new Vector3(x, 0.4f, -1.2f), new Vector3(1.4f, 0.8f, 0.7f), wood); Box("DetectivePC", new Vector3(x, 1.0f, -1.35f), new Vector3(0.4f, 0.28f, 0.05f), dark, false); Box("DetectiveGlow", new Vector3(x, 1.0f, -1.32f), new Vector3(0.34f, 0.22f, 0.01f), new Color(0.3f, 0.7f, 0.95f), false, false, true);
            }
            // ---- lockers and evidence cage on the side walls
            for (int i = 0; i < 4; i++) Box("Locker", new Vector3(hw - 0.4f, 0.95f, 0.8f + i * 0.55f), new Vector3(0.5f, 1.8f, 0.5f), i % 2 == 0 ? steel : new Color(0.5f, 0.55f, 0.65f));
            Box("EvidenceShelf", new Vector3(hw - 0.35f, 1.0f, 4.2f), new Vector3(0.5f, 2.0f, 1.2f), steel);
            Box("EvidenceBox", new Vector3(hw - 0.35f, 1.35f, 4.0f), new Vector3(0.3f, 0.3f, 0.4f), wood, false); Box("EvidenceBox2", new Vector3(hw - 0.35f, 0.75f, 4.4f), new Vector3(0.3f, 0.3f, 0.4f), wood, false);
            Text(transform, "EVIDENCE", new Vector3(hw - 0.62f, 2.3f, 4.2f), 0.03f, Color.white, 90f);
            Box("ArmoryCage", new Vector3(-hw + 0.5f, 1.0f, -0.9f), new Vector3(0.12f, 2.0f, 1.4f), bars);
            Text(transform, "ARMORY", new Vector3(-hw + 0.7f, 2.3f, -0.9f), 0.03f, Color.white, 270f);

            // ---- plaza: flag pole, bollards, roof sign + beacons on the old station, a police-car-coloured shuttle parked in front
            var plaza = new GameObject("PolicePlaza").transform; plaza.SetParent(transform.parent, false);
            Vector3 front = new Vector3(station.x, station.y, station.z + halfZ);
            Box(plaza, "FlagPole", front + new Vector3(halfX - 1.0f, 3.5f, 2.2f), new Vector3(0.12f, 7f, 0.12f), steel, false, false, false, PrimitiveType.Cylinder);
            Box(plaza, "Flag", front + new Vector3(halfX - 1.0f + 0.75f, 6.5f, 2.2f), new Vector3(1.4f, 0.9f, 0.04f), new Color(0.15f, 0.25f, 0.6f), false);
            Box(plaza, "OldRoofSign", front + new Vector3(0f, 4.9f, -0.5f), new Vector3(5.4f, 1.1f, 0.25f), blue, false); Text(plaza, "POLICE STATION", front + new Vector3(0f, 4.9f, -0.3f), 0.14f, Color.white, 180f);
            Box(plaza, "OldBeaconRed", front + new Vector3(-0.4f, 4.55f, -1.4f), new Vector3(0.5f, 0.3f, 0.4f), red, false, false, true); Box(plaza, "OldBeaconBlue", front + new Vector3(0.4f, 4.55f, -1.4f), new Vector3(0.5f, 0.3f, 0.4f), new Color(0.2f, 0.4f, 1f), false, false, true);
            Box(plaza, "OldBlueBand", front + new Vector3(0f, 3.4f, 0.17f), new Vector3(halfX * 2f + 0.3f, 0.45f, 0.06f), blue, false);
            for (int i = 0; i < 5; i++) Box(plaza, "Bollard", front + new Vector3(-halfX - 9f + 0.4f + i * 3.1f, 0.4f, 3.4f), new Vector3(0.25f, 0.8f, 0.25f), new Color(0.9f, 0.85f, 0.2f), true, false, false, PrimitiveType.Cylinder);
            // staff
            StartCoroutine(SpawnStaff());
        }

        System.Collections.IEnumerator SpawnStaff()
        {
            yield return new WaitForSeconds(1.0f);
            var sp = FindAnyObjectByType<MafiaSandbox.Npc.NpcSpawner>(); if (!sp || sp.prefabs == null || sp.prefabs.Length == 0) yield break;
            GameObject Find(string pre) { foreach (var p in sp.prefabs) if (p && p.name.StartsWith(pre)) return p; return sp.prefabs[0]; }
            void Place(string pre, Vector3 lp, string pose, string nm, Vector3 faceLocal)
            {
                var pos = L(lp.x, lp.y, lp.z); var face = L(faceLocal.x, faceLocal.y, faceLocal.z);
                var c = sp.Spawn<MafiaSandbox.Npc.CamperBrain>(Find(pre), pos, Mathf.Atan2(face.x - pos.x, face.z - pos.z) * Mathf.Rad2Deg);
                c.homeSpot = c.transform.position; c.pose = pose; c.fire = face; c.displayName = nm; c.walkSpeed = 1.4f;
            }
            Place("Polis_02", new Vector3(-0.3f, 0.2f, 1.15f), "Idle", "Booking sergeant", new Vector3(-0.3f, 0.2f, 5f));
            Place("Polis_03", new Vector3(-1.9f, 0.2f, -2.2f), "Idle", "Detective", new Vector3(-1.9f, 0.2f, -1.2f));
        }
    }
}
