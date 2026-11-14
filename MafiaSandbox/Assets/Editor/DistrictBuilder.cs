using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MafiaSandbox.Data;
using MafiaSandbox.Factions;

namespace MafiaSandbox.EditorTools
{
    /// <summary>
    /// Grows the prototype city with two new districts (west x ∈ [-160,-78] and east x ∈ [78,160], prototype coordinates),
    /// each a 2 × 4 grid of blocks: gang turf north and south, a mafia family in the middle. Everything is appended to the
    /// CityData lists (props, colliders, buildings, vehicles, road / pedestrian graph nodes) so CityBuilder places it with the
    /// same code as the original city; only the ground and road strips are built here as simple quads.
    /// </summary>
    public static class DistrictBuilder
    {
        const float SW = 7f, ROAD = 10f, ZMIN = -128f, ZTOP = 50f;
        static readonly float[] NSRoadsWest = { -110f, -160f }, NSRoadsEast = { 110f, 160f };
        static readonly float[] EWRoads = { -100f, -50f, 0f, 50f };

        [System.Serializable] class PropMetaFile { public PropMeta[] props; }
        [System.Serializable] class PropMeta { public string name; public float fw, fd; public string kind; public float[] size, door, counter, keeper, keeper2, owner, safe; public Row[] solids, shelves, seats; }
        [System.Serializable] class Row { public float[] v; }

        static Dictionary<string, PropMeta> meta;
        static List<PropData> props; static List<ColliderData> cols; static List<BuildingData> blds; static List<VehicleData> vehs;
        static List<GraphNode> road; static List<GraphNode> ped; static Transform geoRoot;
        static Material matGrass, matCurb, matAsphalt, matPaint; static Mesh quad;
        static System.Random rng = new System.Random(7);
        static float R() => (float)rng.NextDouble();

        public static void Extend(CityData city, Transform world)
        {
            var mf = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/props_meta.json");
            meta = new Dictionary<string, PropMeta>(); if (mf) foreach (var p in JsonUtility.FromJson<PropMetaFile>(mf.text).props) meta[p.name] = p;
            props = new List<PropData>(city.props); cols = new List<ColliderData>(city.colliders); blds = new List<BuildingData>(city.buildings); vehs = new List<VehicleData>(city.vehicles);
            road = new List<GraphNode>(city.roadNodes); ped = new List<GraphNode>(city.pedNodes);
            geoRoot = new GameObject("Districts").transform; geoRoot.SetParent(world, false);
            MakeMaterials();

            // The prototype put the gun shop right where the overpass stairs come down (x −18…−10, z −31…−21), so its door
            // was buried in the staircase. It moves to the old car park by the scrapyard; the stairs get their landing back.
            props.RemoveAll(pp => pp.name == "Dukkan_Silah");
            blds.RemoveAll(bb => bb.name == "Dukkan_Silah");
            cols.RemoveAll(cc => cc.min[0] >= -18.2f && cc.max[0] <= -9.8f && cc.min[2] >= -31.2f && cc.max[2] <= -20.8f && cc.max[1] < 4f);
            Building("Dukkan_Silah", 66f, -66f, 0f, "Gun Shop");
            Prop("Reklam_Panosu", 60f, -60f, 0f); Prop("Sokak_Lambasi", 72f, -61f, 0f);

            // ground beyond the prototype's plane (which reaches x = ±118)
            Ground(-212f, -118f, -160f, 84f); Ground(118f, 212f, -160f, 84f);

            foreach (int side in new[] { -1, 1 })
            {
                var ns = side < 0 ? NSRoadsWest : NSRoadsEast;
                // roads
                foreach (var x in ns) { Strip(SW * 2f, ZTOP - ZMIN + 4f, matCurb, x, (ZTOP + ZMIN) / 2f, false, 0.012f); Strip(ROAD, ZTOP - ZMIN + 4f, matAsphalt, x, (ZTOP + ZMIN) / 2f, false, 0.02f); }
                foreach (var z in EWRoads)
                {
                    float x0 = side < 0 ? -162f : (z == 0f ? 108f : 76f), x1 = side < 0 ? -76f : 162f;   // overlaps the prototype stubs at x = ±78 by 2 m; east z=0 starts past the camp dead end
                    Strip(SW * 2f, Mathf.Abs(x1 - x0), matCurb, (x0 + x1) / 2f, z, true, 0.012f); Strip(ROAD, Mathf.Abs(x1 - x0), matAsphalt, (x0 + x1) / 2f, z, true, 0.02f);
                }
                // lane dashes
                foreach (var x in ns) for (float t = ZMIN + 2f; t < ZTOP; t += 6f) { if (NearAny(t, EWRoads, 7f)) continue; Strip(0.25f, 3f, matPaint, x, t, false, 0.03f); }
                foreach (var z in EWRoads) { float x0 = side < 0 ? -158f : (z == 0f ? 112f : 84f), x1 = side < 0 ? -84f : 158f; for (float t = Mathf.Min(x0, x1); t < Mathf.Max(x0, x1); t += 6f) { if (NearAny(t, ns, 7f) || Mathf.Abs(Mathf.Abs(t) - 78f) < 7f) continue; Strip(0.25f, 3f, matPaint, t, z, true, 0.03f); } }
                // crosswalks at the new junctions
                foreach (var x in ns) foreach (var z in EWRoads) { if (side > 0 && z == 0f && x == 110f) continue; Crosswalk(x, z); }
                // road graph: a column per N-S road, linked across to the prototype stubs at x = ±78
                var col0 = new List<int>(); var col1 = new List<int>();
                foreach (var z in new[] { ZTOP, 0f, -50f, -100f, ZMIN }) { col0.Add(AddRoad(ns[0], z)); col1.Add(AddRoad(ns[1], z)); }
                for (int i = 0; i + 1 < col0.Count; i++) { Link(road, col0[i], col0[i + 1]); Link(road, col1[i], col1[i + 1]); }
                Link(road, col0[4], col1[4]);   // the southern connector at z = ZMIN
                { float x0 = ns[0] - side * 7f, x1 = ns[1] + side * 2f; Strip(SW * 2f, Mathf.Abs(x1 - x0), matCurb, (x0 + x1) / 2f, ZMIN, true, 0.012f); Strip(ROAD, Mathf.Abs(x1 - x0), matAsphalt, (x0 + x1) / 2f, ZMIN, true, 0.02f); for (float t = Mathf.Min(Mathf.Abs(x0), Mathf.Abs(x1)) + 9f; t < Mathf.Max(Mathf.Abs(x0), Mathf.Abs(x1)) - 9f; t += 6f) Strip(0.25f, 3f, matPaint, side * t, ZMIN, true, 0.03f); }
                for (int i = 0; i < 4; i++)
                {
                    float z = new[] { ZTOP, 0f, -50f, -100f }[i]; Link(road, col0[i], col1[i]);
                    if (side > 0 && z == 0f) continue;
                    int stub = FindRoad(side * 78f, z); if (stub >= 0) Link(road, stub, col0[i]);
                }
                // pedestrian graph: sidewalk corners around every new junction + chains along the roads
                PedGrid(side, ns);
                // street furniture along the sidewalks
                foreach (var x in ns) for (float z = ZMIN + 6f; z < ZTOP; z += 16f) { if (NearAny(z, EWRoads, 9f)) continue; Prop("Sokak_Lambasi", x + SW * (((int)(z / 16f) & 1) == 0 ? 1f : -1f) * 1.0f, z, Mathf.PI / 2f); if (R() < 0.6f) Prop(R() < 0.5f ? "Agac_Yuvarlak" : "Agac_Cam_Kucuk", x + SW * (((int)(z / 16f) & 1) == 0 ? -1f : 1f) * 1.1f, z + 4f, R() * 6f); }
                foreach (var z in EWRoads) for (float x = 86f; x < 158f; x += 16f) { float xx = side * x; if (NearAny(xx, ns, 9f) || (side > 0 && z == 0f && x < 112f)) continue; Prop("Sokak_Lambasi", xx, z + SW * (((int)(x / 16f) & 1) == 0 ? 1f : -1f) * 1.0f, 0f); if (R() < 0.5f) Prop("Agac_Yuvarlak_Sonbahar", xx + 4f, z + SW * (((int)(x / 16f) & 1) == 0 ? -1f : 1f) * 1.1f, R() * 6f); }
                // blocks
                string gangN = side < 0 ? "mavi" : "sari", gangS = side < 0 ? "yesil" : "kirmizi", mafia = side < 0 ? "moretti" : "kaplan";
                GangBlock(side, 7f, 43f, gangN, side < 0 ? "Depo_Mavi" : "Depo_Gri");
                MafiaBlock(side, -43f, -7f, mafia);
                GangBlock(side, -93f, -57f, gangS, side < 0 ? "Depo_Gri" : "Depo_Mavi");
                ScrapStrip(side, -121f, -107f, gangS);
                // the inner column (x ∈ ±[85,103]) is housing for all four rows
                foreach (var (z0, z1) in new[] { (7f, 43f), (-43f, -7f), (-93f, -57f), (-121f, -107f) }) HousingColumn(side, z0, z1);
            }

            city.props = props.ToArray(); city.colliders = cols.ToArray(); city.buildings = blds.ToArray(); city.vehicles = vehs.ToArray();
            city.roadNodes = road.ToArray(); city.pedNodes = ped.ToArray();
            Debug.Log($"[MafiaSandbox] Districts: +{props.Count} props total, {blds.Count} buildings, {road.Count} road nodes, {ped.Count} ped nodes");
        }

        // ------------------------------------------------------------------ blocks
        static void GangBlock(int side, float z0, float z1, string faction, string depot)
        {
            float xOut = side * 153f;   // block interior x: between the two N-S roads
            float zTop = Mathf.Max(z0, z1), zBot = Mathf.Min(z0, z1);
            // bar (gang hang-out, a real shop with a keeper) facing the street on the "top" side (north for the north block, north too for the south block)
            var f = FactionDefs.Get(faction);
            Building("Dukkan_Kafe", side * 124f, zTop - 5f, 0f, f.name + " Bar");
            Prop(depot, side * 141f, zTop - 12f, 0f);
            Prop("Apartman_03", side * 142f, zBot + 8f, side < 0 ? Mathf.PI : 0f);
            // the yard: barrels, junk, graffiti, a couple of cars
            Prop("Ates_Varili", side * 126f, zTop - 15f, R() * 6f); Prop("Hurda_Yigini", side * 119f, zBot + 4f, R() * 6f); Prop("Lastik_Yigini", side * 129f, zBot + 5f, R() * 6f);
            Prop("Kasa_Yigini", side * 120f, zTop - 12f, R() * 6f); Prop("Palet", side * 131f, zBot + 10f, R() * 6f); Prop("Cop_Konteyneri", side * 119f, zTop - 28f, 0.2f);
            for (float z = zBot + 2f; z < zTop - 2f; z += 4f) Prop("Grafiti_Duvari", xOut + side * 0.8f, z, Mathf.PI / 2f);
            Prop("Reklam_Panosu", side * 130f, zBot + 1.5f, side < 0 ? Mathf.PI : 0f);
            Vehicle(side < 0 ? "Araba_Sedan_Kirmizi" : "Araba_Hatchback_Beyaz", side * 121f, zBot + 16f, Mathf.PI / 2f);
            Prop("Motosiklet_Siyah", side * 125f, zBot + 20f, 0.4f); Prop("Motosiklet_Kirmizi", side * 127f, zBot + 20f, 0.2f);
            Prop("Sokak_Lambasi", side * 122f, zTop - 24f, 0f);
        }

        static void MafiaBlock(int side, float z0, float z1, string faction)
        {
            var f = FactionDefs.Get(faction); float zTop = Mathf.Max(z0, z1), zBot = Mathf.Min(z0, z1);
            Building("Dukkan_Restoran", side * 124f, zTop - 5.5f, 0f, f.name + " Restaurant");
            Prop(side < 0 ? "Villa_05" : "Villa_11", side * 142f, zTop - 9f, side < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f);
            Prop(side < 0 ? "Villa_12" : "Villa_06", side * 142f, zBot + 9f, side < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f);
            Prop("Cesme", side * 124f, zBot + 14f, 0f);
            foreach (var dz in new[] { -3f, 3f }) { Prop("Bank", side * 119f, zBot + 14f + dz, side < 0 ? -Mathf.PI / 2f : Mathf.PI / 2f); Prop("Bank", side * 129f, zBot + 14f + dz, side < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f); }
            Prop("Saksi_Buyuk", side * 121f, zBot + 20f, 0f); Prop("Saksi_Buyuk", side * 127f, zBot + 20f, 0f);
            for (float z = zBot + 1f; z < zTop - 1f; z += 2f) Prop("Cit_Cali_2m", side * 153.5f, z, Mathf.PI / 2f);
            for (float x = 118f; x < 152f; x += 2f) Prop("Cit_Cali_2m", side * x, zBot + 0.7f, 0f);
            Prop("Agac_Palmiye", side * 133f, zTop - 19f, 0f); Prop("Agac_Palmiye", side * 124f, zBot + 3.5f, 0f);
            Prop("Sokak_Lambasi", side * 121f, zTop - 14f, 0f); Prop("Sokak_Lambasi", side * 127f, zTop - 14f, 0f);
            Vehicle("Araba_SUV_Siyah", side * 118.5f, zBot + 5f, Mathf.PI / 2f); Vehicle("Araba_SUV_Siyah", side * 118.5f, zTop - 16f, Mathf.PI / 2f); Vehicle("Araba_SUV_Gri", side * 151.5f, zBot + 5f, Mathf.PI / 2f);
        }

        static void ScrapStrip(int side, float z0, float z1, string faction)
        {
            float zc = (z0 + z1) / 2f;
            Prop("Santiye_Konteyneri", side * 122f, zc, 0.1f); Prop("Santiye_Konteyneri_Beyaz", side * 134f, zc + 2f, -0.2f); Prop("Hurda_Yigini", side * 145f, zc - 2f, R() * 6f); Prop("Hurda_Yigini", side * 150f, zc + 3f, R() * 6f);
            Prop("Lastik_Yigini", side * 128f, zc - 4f, 0f); Prop("Kasa_Yigini", side * 140f, zc + 4f, 0.5f); Prop("Ates_Varili", side * 138f, zc - 3f, 0f); Prop("Cadir_Mavi", side * 148f, zc - 5f, 0.3f);
            for (float x = 116f; x < 154f; x += 2f) Prop("Cit_Metal_2m", side * x, z0 + 0.5f, 0f);
            Prop("Grafiti_Duvari", side * 153.5f, zc, Mathf.PI / 2f); Prop("Grafiti_Duvari", side * 153.5f, zc + 4f, Mathf.PI / 2f);
            Vehicle("Araba_Pickup_Turuncu", side * 116f, zc + 4f, 0f);
        }

        static readonly string[] HouseInteriors = { "Ev_Ic_Krem", "Ev_Ic_Mavi", "Ev_Ic_Sari", "Ev_Ic_Yesil" };
        static int houseIdx;
        static void HousingColumn(int side, float z0, float z1)
        {
            // x ∈ ±[85, 103]: 18 m deep lots; houses face the inner N-S road (x = ±110) or the outer one alternately
            float zTop = Mathf.Max(z0, z1), zBot = Mathf.Min(z0, z1); float cx = side * 94f;
            if (zTop - zBot < 20f) { Prop("Depo_Gri", cx, (zTop + zBot) / 2f, 0f); return; }
            float z = zBot + 8f;
            while (z + 6f < zTop)
            {
                bool interior = R() < 0.55f;
                if (interior) { string h = HouseInteriors[houseIdx++ % HouseInteriors.Length]; float rot = side < 0 ? -Mathf.PI / 2f : Mathf.PI / 2f; Building(h, cx, z, rot, null); }
                else Prop("Villa_0" + (1 + rng.Next(9)), cx, z + 1f, side < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f);
                if (R() < 0.7f) Prop(R() < 0.5f ? "Agac_Yuvarlak" : "Cali", cx + side * 7f, z + 6.5f, R() * 6f);
                z += 16f;
            }
        }

        // ------------------------------------------------------------------ primitives (prototype coordinates)
        static void Prop(string name, float x, float z, float rotY)
        {
            props.Add(new PropData { name = name, pos = new[] { x, 0f, z }, rotY = rotY, scale = 1f });
            if (!meta.TryGetValue(name, out var m)) return;
            float w = m.fw, d = m.fd; int q = Mathf.RoundToInt(rotY / (Mathf.PI / 2f)) % 2; if (q != 0) { var t = w; w = d; d = t; }
            float h = name.StartsWith("Villa") || name.StartsWith("Apartman") || name.StartsWith("Depo") ? 14f : name.StartsWith("Agac") ? 6f : name.StartsWith("Cit") || name.StartsWith("Duvar") || name.StartsWith("Beton") ? 1.5f
                : name.StartsWith("Cadir") || name.StartsWith("Branda") || name.StartsWith("Bufe") || name.StartsWith("Reklam") || name.StartsWith("Grafiti") || name.StartsWith("Sokak") || name.StartsWith("Santiye_Konteyneri") ? 3.2f : 2.2f;
            if (name.StartsWith("Agac")) { w = Mathf.Min(w, 0.8f); d = Mathf.Min(d, 0.8f); }
            cols.Add(new ColliderData { min = new[] { x - w / 2f, -0.5f, z - d / 2f }, max = new[] { x + w / 2f, h, z + d / 2f }, opaque = name.StartsWith("Villa") || name.StartsWith("Apartman") || name.StartsWith("Depo"), building = false });
        }

        static void Vehicle(string name, float x, float z, float heading)
        {
            float len = 4.5f, wid = 1.8f; if (meta.TryGetValue(name, out var m)) { len = Mathf.Max(m.fw, m.fd); wid = Mathf.Min(m.fw, m.fd); }
            vehs.Add(new VehicleData { name = name, pos = new[] { x, 0f, z }, heading = heading, ai = false, player = false, length = len, width = wid });
        }

        /// <summary>Port of the prototype's placeBuilding: a shop / house shell with interior wall colliders and the economy marker.</summary>
        static void Building(string name, float x, float z, float rotY, string label)
        {
            if (!meta.TryGetValue(name, out var m) || string.IsNullOrEmpty(m.kind)) { Prop(name, x, z, rotY); return; }
            props.Add(new PropData { name = name, pos = new[] { x, 0f, z }, rotY = rotY, scale = 1f });
            float c = Mathf.Cos(rotY), s = Mathf.Sin(rotY);
            Vector2 W(float lx, float lz) => new Vector2(x + lx * c + lz * s, z - lx * s + lz * c);
            bool q = Mathf.RoundToInt(rotY / (Mathf.PI / 2f)) % 2 != 0;
            var solids = new List<SolidData>();
            foreach (var r in m.solids) { var v = r.v; var p = W(v[0], v[1]); float ww = q ? v[3] : v[2], dd = q ? v[2] : v[3]; cols.Add(new ColliderData { min = new[] { p.x - ww / 2f, -0.5f, p.y - dd / 2f }, max = new[] { p.x + ww / 2f, v[4], p.y + dd / 2f }, opaque = v[4] > 1.7f, building = true }); solids.Add(new SolidData { x = v[0], z = v[1], w = v[2], d = v[3], h = v[4] }); }
            float hw = Mathf.Abs(m.size[0]) / 2f, hd = Mathf.Abs(m.size[1]) / 2f; var corners = new[] { W(-hw, -hd), W(hw, -hd), W(hw, hd), W(-hw, hd) };
            float minx = float.MaxValue, minz = float.MaxValue, maxx = float.MinValue, maxz = float.MinValue; foreach (var p in corners) { minx = Mathf.Min(minx, p.x); maxx = Mathf.Max(maxx, p.x); minz = Mathf.Min(minz, p.y); maxz = Mathf.Max(maxz, p.y); }
            var door = W(m.door[0], m.door[1]); var counter = m.counter != null && (m.counter[0] != 0f || m.counter[1] != 0f) ? W(m.counter[0], m.counter[1]) : Vector2.zero;
            var b = new BuildingData
            {
                name = name, kind = m.kind, label = label ?? (m.kind == "house" ? "House" : name), owner = null, pos = new[] { x, 0f, z }, rotY = rotY,
                min = new[] { minx, minz }, max = new[] { maxx, maxz }, door = new[] { door.x, 0f, door.y }, counter = new[] { counter.x, 0f, counter.y }, size = new[] { m.size[0], m.size[1], m.size[2] },
                solids = solids.ToArray(), shelves = Rows3(m.shelves), seats = Rows2(m.seats),
                keeper = new KeeperData { x = m.keeper[0], z = m.keeper[1], rot = m.keeper[2] }, keeper2 = new KeeperData { x = m.keeper2[0], z = m.keeper2[1], rot = m.keeper2[2] },
                ownerSpot = new KeeperData { x = m.owner[0], z = m.owner[1], rot = m.owner[2] }, safe = new SeatData { x = m.safe[0], z = m.safe[1] }, register = new SeatData()
            };
            blds.Add(b);
        }
        static ShelfData[] Rows3(Row[] rows) { var l = new List<ShelfData>(); if (rows != null) foreach (var r in rows) l.Add(new ShelfData { x = r.v[0], y = r.v[1], z = r.v[2] }); return l.ToArray(); }
        static SeatData[] Rows2(Row[] rows) { var l = new List<SeatData>(); if (rows != null) foreach (var r in rows) l.Add(new SeatData { x = r.v[0], z = r.v[1] }); return l.ToArray(); }

        static int AddRoad(float x, float z) { road.Add(new GraphNode { x = x, y = 0f, z = z, adj = new int[0] }); return road.Count - 1; }
        static int FindRoad(float x, float z) { for (int i = 0; i < road.Count; i++) if (Mathf.Abs(road[i].x - x) < 0.5f && Mathf.Abs(road[i].z - z) < 0.5f) return i; return -1; }
        static void Link(List<GraphNode> g, int a, int b)
        {
            if (a < 0 || b < 0 || a == b) return;
            if (System.Array.IndexOf(g[a].adj, b) < 0) { var l = new List<int>(g[a].adj) { b }; g[a].adj = l.ToArray(); }
            if (System.Array.IndexOf(g[b].adj, a) < 0) { var l = new List<int>(g[b].adj) { a }; g[b].adj = l.ToArray(); }
        }
        static bool NearAny(float v, float[] arr, float d) { foreach (var a in arr) if (Mathf.Abs(v - a) < d) return true; return false; }

        /// <summary>Sidewalk graph: corner nodes at every junction, chains between them on both sides of each road, crossings at junctions.</summary>
        static void PedGrid(int side, float[] ns)
        {
            const float O = 5.8f; var zs = new[] { ZTOP, 0f, -50f, -100f, ZMIN };
            int Node(float x, float z) { ped.Add(new GraphNode { x = x, y = 0f, z = z, adj = new int[0] }); return ped.Count - 1; }
            int Nearest(float x, float z, float maxD) { int best = -1; float bd = maxD * maxD; for (int i = 0; i < ped.Count; i++) { float d = (ped[i].x - x) * (ped[i].x - x) + (ped[i].z - z) * (ped[i].z - z); if (d < bd) { bd = d; best = i; } } return best; }
            var corner = new Dictionary<(float, float, int, int), int>();
            int Corner(float ix, float iz, int sx, int sz) { var k = (ix, iz, sx, sz); if (!corner.TryGetValue(k, out int n)) { n = Node(ix + sx * O, iz + sz * O); corner[k] = n; } return n; }
            // along each N-S road
            foreach (var x in ns) foreach (int sx in new[] { -1, 1 })
            {
                int prev = -1;
                for (int i = 0; i < zs.Length; i++)
                {
                    float z = zs[i];
                    int entry = Corner(x, z, sx, 1), exit = Corner(x, z, sx, -1);
                    if (prev >= 0) Link(ped, prev, entry);
                    Link(ped, entry, exit); prev = exit;
                }
            }
            // along each E-W road, from the prototype stub (x = ±78) to the outer road
            foreach (var z in EWRoads) foreach (int sz in new[] { -1, 1 })
            {
                var xsAll = new List<float>(); if (!(side > 0 && z == 0f)) xsAll.Add(side * 78f); xsAll.Add(ns[0]); xsAll.Add(ns[1]);
                int prev = -1;
                for (int i = 0; i < xsAll.Count; i++)
                {
                    float x = xsAll[i];
                    if (Mathf.Abs(Mathf.Abs(x) - 78f) < 0.1f) { prev = Nearest(x, z + sz * O, 4f); if (prev < 0) prev = Node(x, z + sz * O); continue; }
                    int entry = Corner(x, z, -side, sz), exit = Corner(x, z, side, sz);
                    if (prev >= 0) Link(ped, prev, entry);
                    Link(ped, entry, exit); prev = exit;
                }
            }
            // crossings at every junction: the four corners form a ring
            foreach (var x in ns) foreach (var z in zs)
            {
                int a = Corner(x, z, -1, -1), b = Corner(x, z, 1, -1), c = Corner(x, z, 1, 1), d = Corner(x, z, -1, 1);
                Link(ped, a, b); Link(ped, b, c); Link(ped, c, d); Link(ped, d, a);
            }
        }

        // ------------------------------------------------------------------ geometry
        static void MakeMaterials()
        {
            Material M(Color c) { var sh = Shader.Find("Standard"); var m = new Material(sh) { color = c }; m.SetFloat("_Glossiness", 0.05f); return m; }
            matGrass = M(new Color(0.498f, 0.639f, 0.416f)); matCurb = M(new Color(0.725f, 0.737f, 0.765f)); matAsphalt = M(new Color(0.29f, 0.306f, 0.341f)); matPaint = M(new Color(0.914f, 0.894f, 0.784f));
            quad = new Mesh { name = "unitQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
            quad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up }; quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 }; quad.RecalculateBounds();
        }
        static void Ground(float x0, float x1, float z0, float z1)
        {
            var c = Coords.ToUnity((x0 + x1) / 2f, 0f, (z0 + z1) / 2f);
            Quad("Ground", matGrass, c, Mathf.Abs(x1 - x0), Mathf.Abs(z1 - z0), true);
        }
        /// <summary>A flat strip at (x, z) in prototype coordinates; w across, l along; ew = true lays it along X.</summary>
        static void Strip(float w, float l, Material m, float x, float z, bool ew, float y)
        {
            var c = Coords.ToUnity(x, y, z); if (ew) { var t = w; w = l; l = t; }
            Quad("Strip", m, c, w, l, y < 0.015f);
        }
        static void Quad(string name, Material m, Vector3 center, float sx, float sz, bool collider)
        {
            var go = new GameObject(name); go.transform.SetParent(geoRoot, false); go.transform.position = center; go.transform.localScale = new Vector3(sx, 1f, sz);
            go.AddComponent<MeshFilter>().sharedMesh = quad; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (collider) { var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(1f, 0.02f, 1f); bc.center = new Vector3(0f, -0.01f, 0f); }
            go.isStatic = true;
        }
        static void Crosswalk(float x, float z)
        {
            for (int k = -4; k <= 4; k++) { float o = k * 1.2f; Strip(0.6f, 2.6f, matPaint, x + o, z + (SW - 1.4f), false, 0.032f); Strip(0.6f, 2.6f, matPaint, x + o, z - (SW - 1.4f), false, 0.032f); Strip(2.6f, 0.6f, matPaint, x + (SW - 1.4f), z + o, false, 0.032f); Strip(2.6f, 0.6f, matPaint, x - (SW - 1.4f), z + o, false, 0.032f); }
        }
    }
}
