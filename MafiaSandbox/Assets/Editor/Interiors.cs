using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Data;

namespace MafiaSandbox.EditorTools
{
    /// <summary>
    /// Furnishes the enterable buildings. The shell GLBs only carry the big furniture that matches the prototype's solids, so the
    /// rooms felt empty. Everything here is built from primitives in the building's own frame (Unity-local: x flipped vs the JSON),
    /// avoids the existing solids, the door corridor and the NPC spots, and is baked before the navmesh so furniture carves it.
    /// </summary>
    public static class Interiors
    {
        class Occ { public Rect r; public float h; }
        class Ctx
        {
            public Transform root; public List<Occ> occ = new List<Occ>(); public System.Random rng; public float hx, hz, H, doorX; public string kind; public int made;
            public float R() => (float)rng.NextDouble();
        }

        static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
        static Material M(Color c, bool glow = false)
        {
            var key = glow ? new Color(c.r, c.g, c.b, 0.37f) : c; if (mats.TryGetValue(key, out var m) && m) return m;
            m = new Material(Shader.Find("Standard")) { color = c, name = glow ? "InteriorGlow" : "Interior" }; m.SetFloat("_Glossiness", glow ? 0.5f : 0.12f);
            if (glow) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 1.3f); }
            mats[key] = m; return m;
        }
        static Color C(float r, float g, float b) => new Color(r, g, b);
        static readonly Color Wood = new Color(0.47f, 0.33f, 0.2f), DarkWood = new Color(0.3f, 0.2f, 0.12f), Cream = new Color(0.9f, 0.86f, 0.76f), Grey = new Color(0.5f, 0.52f, 0.55f), Steel = new Color(0.72f, 0.75f, 0.78f),
            Green = new Color(0.22f, 0.5f, 0.28f), Red = new Color(0.72f, 0.14f, 0.16f), Blue = new Color(0.22f, 0.38f, 0.62f), Gold = new Color(0.95f, 0.78f, 0.25f), Glass = new Color(0.55f, 0.78f, 0.9f), Char = new Color(0.1f, 0.09f, 0.085f), White = new Color(0.93f, 0.94f, 0.95f);

        // ------------------------------------------------------------------ primitives
        static GameObject Pc(Ctx c, string n, Vector3 pos, Vector3 fwd)
        {
            var g = new GameObject(n); g.transform.SetParent(c.root, false); g.transform.localPosition = pos; g.transform.localRotation = fwd.sqrMagnitude < 0.01f ? Quaternion.identity : Quaternion.LookRotation(fwd, Vector3.up); g.isStatic = true; c.made++; return g;
        }
        static Transform B(GameObject p, string n, float x, float y, float z, float sx, float sy, float sz, Color col, bool glow = false)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; g.transform.SetParent(p.transform, false); g.transform.localPosition = new Vector3(x, y, z); g.transform.localScale = new Vector3(sx, sy, sz);
            Object.DestroyImmediate(g.GetComponent<Collider>()); g.GetComponent<Renderer>().sharedMaterial = M(col, glow); g.isStatic = true; return g.transform;
        }
        static void Solid(GameObject p, float w, float d, float h) { var bc = p.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, h * 0.5f, 0f); bc.size = new Vector3(w, h, d); }

        static bool Free(Ctx c, Rect r, float pad, float minH = 0f)
        {
            var q = new Rect(r.x - pad, r.y - pad, r.width + pad * 2f, r.height + pad * 2f);
            foreach (var o in c.occ) if (o.h >= minH && o.r.Overlaps(q)) return false;
            return true;
        }
        static Rect RectOf(Vector3 pos, Vector3 fwd, float w, float d) { bool side = Mathf.Abs(fwd.x) > Mathf.Abs(fwd.z); float sx = side ? d : w, sz = side ? w : d; return new Rect(pos.x - sx * 0.5f, pos.z - sz * 0.5f, sx, sz); }

        delegate void Build(GameObject p);
        /// <summary>Places a piece against a wall (0 back, 1 front, 2 left, 3 right) at `along`, if the floor there is free.</summary>
        static bool Wall(Ctx c, int wall, float along, float w, float d, float h, Build build, bool solid = true, float pad = 0.38f, string name = "Piece")
        {
            Vector3 pos, fwd;
            switch (wall) { case 0: pos = new Vector3(along, 0f, -c.hz + d * 0.5f); fwd = Vector3.forward; break; case 1: pos = new Vector3(along, 0f, c.hz - d * 0.5f); fwd = Vector3.back; break; case 2: pos = new Vector3(-c.hx + d * 0.5f, 0f, along); fwd = Vector3.right; break; default: pos = new Vector3(c.hx - d * 0.5f, 0f, along); fwd = Vector3.left; break; }
            var r = RectOf(pos, fwd, w, d); float lim = (wall < 2) ? c.hx : c.hz; if (Mathf.Abs(along) + w * 0.5f > lim + 0.01f) return false;
            if (!Free(c, r, pad)) return false;
            var g = Pc(c, name, pos, fwd); build(g); if (solid) Solid(g, w, d, h); c.occ.Add(new Occ { r = r, h = h }); return true;
        }
        static bool Island(Ctx c, float x, float z, Vector3 fwd, float w, float d, float h, Build build, bool solid = true, float pad = 0.5f, string name = "Piece")
        {
            var pos = new Vector3(x, 0f, z); var r = RectOf(pos, fwd, w, d);
            if (r.xMin < -c.hx + 0.1f || r.xMax > c.hx - 0.1f || r.yMin < -c.hz + 0.1f || r.yMax > c.hz - 0.1f) return false;
            if (!Free(c, r, pad)) return false;
            var g = Pc(c, name, pos, fwd); build(g); if (solid) Solid(g, w, d, h); c.occ.Add(new Occ { r = r, h = h }); return true;
        }
        /// <summary>Flat decoration (rug, soot) that does not block anything.</summary>
        static bool Flat(Ctx c, float x, float z, float w, float d, Color col, float y = 0.02f, string name = "Rug")
        {
            var g = Pc(c, name, new Vector3(x, 0f, z), Vector3.forward); B(g, name, 0f, y, 0f, w, 0.025f, d, col); return true;
        }
        /// <summary>Wall-mounted frame / sign: skipped where something tall already stands in front of the wall.</summary>
        static bool Hang(Ctx c, int wall, float along, float w, float h, float y, Color frame, Color art)
        {
            Vector3 pos, fwd; float d = 0.06f;
            switch (wall) { case 0: pos = new Vector3(along, y, -c.hz + d * 0.5f); fwd = Vector3.forward; break; case 1: pos = new Vector3(along, y, c.hz - d * 0.5f); fwd = Vector3.back; break; case 2: pos = new Vector3(-c.hx + d * 0.5f, y, along); fwd = Vector3.right; break; default: pos = new Vector3(c.hx - d * 0.5f, y, along); fwd = Vector3.left; break; }
            var strip = RectOf(new Vector3(pos.x, 0f, pos.z), fwd, w, 0.6f); if (wall == 1 && Mathf.Abs(along - c.doorX) < 2.2f) return false; if (!Free(c, strip, 0.05f, y - h * 0.5f)) return false;
            var g = Pc(c, "Hang", pos, fwd); B(g, "Frame", 0f, 0f, 0f, w, h, d, frame); B(g, "Art", 0f, 0f, d * 0.5f + 0.005f, w - 0.1f, h - 0.1f, 0.01f, art); return true;
        }

        // ------------------------------------------------------------------ the piece library (local: x right, z toward the room, y up)
        static Build Sofa(float w, Color col) => p => { B(p, "Base", 0f, 0.25f, 0f, w, 0.4f, 0.9f, col); B(p, "Back", 0f, 0.65f, -0.36f, w, 0.5f, 0.2f, col * 0.85f); B(p, "ArmL", -w * 0.5f + 0.1f, 0.5f, 0f, 0.2f, 0.3f, 0.9f, col * 0.8f); B(p, "ArmR", w * 0.5f - 0.1f, 0.5f, 0f, 0.2f, 0.3f, 0.9f, col * 0.8f); B(p, "Cushion", 0f, 0.5f, 0.05f, w - 0.5f, 0.1f, 0.6f, col * 1.12f); };
        static Build Table(float w, float d, float h, Color col) => p => { B(p, "Top", 0f, h, 0f, w, 0.06f, d, col); foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f }) B(p, "Leg", sx * (w * 0.5f - 0.06f), h * 0.5f, sz * (d * 0.5f - 0.06f), 0.06f, h, 0.06f, col * 0.75f); };
        static Build Chair(Color col) => p => { B(p, "Seat", 0f, 0.42f, 0f, 0.42f, 0.06f, 0.42f, col); B(p, "Back", 0f, 0.72f, -0.2f, 0.42f, 0.55f, 0.05f, col); foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f }) B(p, "Leg", sx * 0.17f, 0.2f, sz * 0.17f, 0.05f, 0.4f, 0.05f, col * 0.7f); };
        static Build Shelf(float w, float h, Color col, Color[] stock, System.Random rng) => p =>
        {
            B(p, "Frame", 0f, h * 0.5f, -0.1f, w, h, 0.05f, col * 0.8f); B(p, "SideL", -w * 0.5f + 0.02f, h * 0.5f, 0f, 0.04f, h, 0.35f, col); B(p, "SideR", w * 0.5f - 0.02f, h * 0.5f, 0f, 0.04f, h, 0.35f, col);
            for (int i = 0; i <= 4; i++) { float y = 0.05f + i * (h - 0.1f) / 4f; B(p, "Board", 0f, y, 0f, w, 0.04f, 0.35f, col); if (i == 4) break; float x = -w * 0.5f + 0.12f; while (x < w * 0.5f - 0.1f) { float bw = 0.06f + (float)rng.NextDouble() * 0.12f, bh = 0.22f + (float)rng.NextDouble() * 0.12f; B(p, "Item", x + bw * 0.5f, y + 0.02f + bh * 0.5f, 0f, bw, bh, 0.22f, stock[rng.Next(stock.Length)]); x += bw + 0.02f; } }
        };
        static Build Plant(float s) => p => { B(p, "Pot", 0f, 0.2f * s, 0f, 0.38f * s, 0.4f * s, 0.38f * s, C(0.62f, 0.34f, 0.22f)); B(p, "Leaf1", 0f, 0.65f * s, 0f, 0.5f * s, 0.5f * s, 0.5f * s, Green); B(p, "Leaf2", 0.04f, 1.05f * s, -0.03f, 0.32f * s, 0.4f * s, 0.32f * s, Green * 1.15f); };
        static Build Lamp() => p => { B(p, "Base", 0f, 0.03f, 0f, 0.3f, 0.05f, 0.3f, DarkWood); B(p, "Pole", 0f, 0.8f, 0f, 0.05f, 1.55f, 0.05f, DarkWood); B(p, "Shade", 0f, 1.62f, 0f, 0.38f, 0.3f, 0.38f, C(1f, 0.9f, 0.62f), true); };
        static Build Cabinet(float w, float h, Color col) => p => { B(p, "Body", 0f, h * 0.5f, 0f, w, h, 0.5f, col); B(p, "Door", 0f, h * 0.5f, 0.255f, w - 0.1f, h - 0.12f, 0.02f, col * 1.15f); B(p, "Knob", 0.08f, h * 0.55f, 0.275f, 0.04f, 0.04f, 0.03f, Gold); };
        static Build Crate(Color col) => p => { B(p, "Crate", 0f, 0.3f, 0f, 0.62f, 0.6f, 0.62f, col); B(p, "Slat", 0f, 0.3f, 0.315f, 0.5f, 0.1f, 0.01f, col * 0.65f); B(p, "Crate2", 0.05f, 0.82f, 0f, 0.5f, 0.44f, 0.5f, col * 1.1f); };
        static Build Desk(Color col, bool pc) => p => { B(p, "Top", 0f, 0.74f, 0f, 1.5f, 0.05f, 0.75f, col); B(p, "PanelL", -0.68f, 0.37f, 0f, 0.05f, 0.74f, 0.7f, col * 0.8f); B(p, "PanelR", 0.68f, 0.37f, 0f, 0.05f, 0.74f, 0.7f, col * 0.8f); if (pc) { B(p, "Screen", 0f, 1.0f, -0.15f, 0.5f, 0.32f, 0.04f, C(0.1f, 0.12f, 0.15f)); B(p, "Glow", 0f, 1.0f, -0.125f, 0.44f, 0.26f, 0.01f, C(0.3f, 0.7f, 0.95f), true); B(p, "Stand", 0f, 0.84f, -0.15f, 0.08f, 0.14f, 0.06f, Grey); B(p, "Keys", 0f, 0.78f, 0.12f, 0.4f, 0.02f, 0.14f, Grey); } B(p, "Papers", 0.5f, 0.78f, 0.05f, 0.25f, 0.015f, 0.3f, White); };
        static Build Filing() => p => { B(p, "Body", 0f, 0.65f, 0f, 0.5f, 1.3f, 0.6f, Grey); for (int i = 0; i < 3; i++) { B(p, "Drawer", 0f, 0.3f + i * 0.4f, 0.305f, 0.42f, 0.34f, 0.02f, Grey * 1.15f); B(p, "Handle", 0f, 0.3f + i * 0.4f, 0.32f, 0.16f, 0.03f, 0.02f, Steel); } };
        static Build Bench(float w, Color col) => p => { B(p, "Seat", 0f, 0.45f, 0f, w, 0.07f, 0.45f, col); B(p, "Back", 0f, 0.85f, -0.2f, w, 0.45f, 0.05f, col); B(p, "LegL", -w * 0.5f + 0.1f, 0.22f, 0f, 0.07f, 0.44f, 0.4f, col * 0.7f); B(p, "LegR", w * 0.5f - 0.1f, 0.22f, 0f, 0.07f, 0.44f, 0.4f, col * 0.7f); };
        static Build Display(float w, Color body, Color inside) => p => { B(p, "Base", 0f, 0.4f, 0f, w, 0.8f, 0.6f, body); B(p, "Glass", 0f, 1.0f, 0f, w - 0.05f, 0.4f, 0.55f, Glass); B(p, "Goods", 0f, 0.84f, 0f, w - 0.3f, 0.05f, 0.4f, inside); B(p, "Light", 0f, 1.2f, 0f, w - 0.1f, 0.02f, 0.5f, White, true); };
        static Build Vending(Color col) => p => { B(p, "Body", 0f, 0.95f, 0f, 0.9f, 1.9f, 0.8f, col); B(p, "Window", -0.12f, 1.15f, 0.405f, 0.5f, 1.0f, 0.02f, Glass); B(p, "Slot", 0.3f, 0.6f, 0.405f, 0.18f, 0.5f, 0.02f, Grey * 0.5f); B(p, "Glow", -0.12f, 1.15f, 0.42f, 0.4f, 0.9f, 0.005f, C(1f, 0.95f, 0.8f), true); };
        static Build Counter(float w, Color body, Color top) => p => { B(p, "Body", 0f, 0.45f, 0f, w, 0.9f, 0.6f, body); B(p, "Top", 0f, 0.93f, 0f, w + 0.06f, 0.06f, 0.68f, top); };
        static Build GunRack(float w) => p => { B(p, "Board", 0f, 1.5f, 0f, w, 1.1f, 0.06f, DarkWood); for (int i = 0; i < 4; i++) { float y = 1.15f + i * 0.24f; B(p, "Rifle", 0f, y, 0.06f, w - 0.25f, 0.06f, 0.06f, Char); B(p, "Stock", -w * 0.5f + 0.28f, y - 0.03f, 0.06f, 0.35f, 0.1f, 0.06f, Wood); } };
        static Build BarberChair() => p => { B(p, "Base", 0f, 0.1f, 0f, 0.5f, 0.2f, 0.5f, Steel); B(p, "Post", 0f, 0.35f, 0f, 0.14f, 0.4f, 0.14f, Steel); B(p, "Seat", 0f, 0.6f, 0f, 0.6f, 0.14f, 0.6f, Red); B(p, "Back", 0f, 1.0f, -0.28f, 0.6f, 0.75f, 0.12f, Red); B(p, "Rest", 0f, 0.3f, 0.4f, 0.4f, 0.06f, 0.3f, Steel); B(p, "ArmL", -0.35f, 0.8f, 0f, 0.07f, 0.07f, 0.4f, Steel); B(p, "ArmR", 0.35f, 0.8f, 0f, 0.07f, 0.07f, 0.4f, Steel); };
        static Build Mirror(float w) => p => { B(p, "Frame", 0f, 1.5f, 0f, w, 1.2f, 0.05f, DarkWood); B(p, "Glass", 0f, 1.5f, 0.03f, w - 0.1f, 1.1f, 0.01f, C(0.75f, 0.88f, 0.95f)); B(p, "Shelf", 0f, 0.95f, 0.1f, w, 0.04f, 0.2f, White); };
        static Build Cell() => p => { for (int i = 0; i < 9; i++) B(p, "Bar", -1.2f + i * 0.3f, 1.4f, 0f, 0.04f, 2.8f, 0.04f, Char); B(p, "Top", 0f, 2.8f, 0f, 2.6f, 0.06f, 0.06f, Char); B(p, "Bench", 0f, 0.4f, -0.8f, 1.8f, 0.08f, 0.5f, Grey); };
        static Build Barrel(Color col) => p => { B(p, "Barrel", 0f, 0.45f, 0f, 0.6f, 0.9f, 0.6f, col); B(p, "Ring1", 0f, 0.25f, 0f, 0.64f, 0.05f, 0.64f, Steel); B(p, "Ring2", 0f, 0.7f, 0f, 0.64f, 0.05f, 0.64f, Steel); };
        static Build Bin() => p => { B(p, "Bin", 0f, 0.25f, 0f, 0.35f, 0.5f, 0.35f, Grey); B(p, "Lid", 0f, 0.52f, 0f, 0.38f, 0.04f, 0.38f, Grey * 0.7f); };
        static Build Stool(Color col) => p => { B(p, "Seat", 0f, 0.62f, 0f, 0.38f, 0.07f, 0.38f, col); B(p, "Leg", 0f, 0.3f, 0f, 0.07f, 0.6f, 0.07f, Steel); B(p, "Foot", 0f, 0.03f, 0f, 0.3f, 0.04f, 0.3f, Steel); };
        static Build Bed(Color sheet) => p => { B(p, "Frame", 0f, 0.28f, 0f, 1.0f, 0.4f, 2.0f, DarkWood); B(p, "Mattress", 0f, 0.55f, 0.02f, 0.92f, 0.16f, 1.9f, sheet); B(p, "Pillow", 0f, 0.7f, -0.8f, 0.6f, 0.12f, 0.3f, White); };
        static Build Rubble(System.Random rng, float s) => p => { for (int i = 0; i < 6; i++) { float hh = 0.08f + (float)rng.NextDouble() * 0.28f * s; B(p, "Chunk", ((float)rng.NextDouble() - 0.5f) * s, hh * 0.5f, ((float)rng.NextDouble() - 0.5f) * s, 0.12f + (float)rng.NextDouble() * 0.4f, hh, 0.12f + (float)rng.NextDouble() * 0.4f, rng.Next(3) == 0 ? C(0.55f, 0.52f, 0.48f) : rng.Next(2) == 0 ? Char : DarkWood).localRotation = Quaternion.Euler(0f, rng.Next(0, 90), 0f); } };
        static Build Planks(System.Random rng) => p => { for (int i = 0; i < 4; i++) B(p, "Plank", ((float)rng.NextDouble() - 0.5f) * 0.4f, 0.06f + i * 0.05f, ((float)rng.NextDouble() - 0.5f) * 0.3f, 1.4f, 0.05f, 0.14f, i % 2 == 0 ? Char : DarkWood).localRotation = Quaternion.Euler(0f, rng.Next(-70, 70), 0f); };

        // ------------------------------------------------------------------ per-building furnishing
        public static void Furnish(Transform root, BuildingData b, string kind)
        {
            if (b.size == null || b.size.Length < 3 || b.solids == null) return;
            var c = new Ctx { root = root, rng = new System.Random(b.name.GetHashCode() ^ 0x5bd1), kind = kind, hx = Mathf.Abs(b.size[0]) * 0.5f - 0.25f, hz = Mathf.Abs(b.size[1]) * 0.5f - 0.25f, H = Mathf.Abs(b.size[2]) };
            // the solids = the shell's own furniture (x flipped into Unity-local); the four boundary walls stay out of the list
            var front = new List<(float x0, float x1)>();
            foreach (var s in b.solids)
            {
                float ux = -s.x; bool border = Mathf.Abs(ux) >= c.hx + 0.1f || Mathf.Abs(s.z) >= c.hz + 0.1f;
                if (border) { if (s.z > c.hz && s.d < 0.4f) front.Add((ux - s.w * 0.5f, ux + s.w * 0.5f)); continue; }
                c.occ.Add(new Occ { r = new Rect(ux - s.w * 0.5f, s.z - s.d * 0.5f, s.w, s.d), h = s.h });
            }
            front.Sort((a, d) => a.x0.CompareTo(d.x0)); c.doorX = 0f;
            if (front.Count >= 2) { float best = 0f; for (int i = 1; i < front.Count; i++) { float gap = front[i].x0 - front[i - 1].x1; if (gap > best) { best = gap; c.doorX = (front[i].x0 + front[i - 1].x1) * 0.5f; } } }
            c.occ.Add(new Occ { r = new Rect(c.doorX - 1.5f, c.hz - 2.6f, 3f, 3f), h = 9f });           // keep the entrance clear
            void Spot(float x, float z, float rad) { c.occ.Add(new Occ { r = new Rect(-x - rad, z - rad, rad * 2f, rad * 2f), h = 9f }); }
            if (b.keeper != null) Spot(b.keeper.x, b.keeper.z, 0.8f); if (b.keeper2 != null && (b.keeper2.x != 0f || b.keeper2.z != 0f)) Spot(b.keeper2.x, b.keeper2.z, 0.8f);
            if (b.ownerSpot != null && (b.ownerSpot.x != 0f || b.ownerSpot.z != 0f)) Spot(b.ownerSpot.x, b.ownerSpot.z, 0.7f);
            if (b.safe != null && (b.safe.x != 0f || b.safe.z != 0f)) Spot(b.safe.x, b.safe.z, 0.9f); if (b.register != null && (b.register.x != 0f || b.register.z != 0f)) Spot(b.register.x, b.register.z, 0.7f);
            if (b.seats != null) foreach (var s in b.seats) Spot(s.x, s.z, 0.45f);
            if (b.shelves != null) foreach (var s in b.shelves) Spot(s.x, s.z, 0.3f);
            if (kind == "home") c.occ.Add(new Occ { r = new Rect(-3.4f, -3.4f, 6.8f, 6.8f), h = 9f });   // the stash, lab bench and bed spawn around the middle at runtime

            switch (kind)
            {
                case "house": House(c); break;
                case "home": Ruin(c); break;
                case "market": Shop(c, new[] { Red, Blue, Gold, White, Green }, true, false); break;
                case "cafe": Cafe(c); break;
                case "pharmacy": Pharmacy(c); break;
                case "gun": Gun(c); break;
                case "pawn": Pawn(c); break;
                case "restaurant": Restaurant(c); break;
                case "jeweler": Jeweler(c); break;
                case "barber": Barber(c); break;
                case "bank": Bank(c); break;
                case "police": Police(c); break;
                default: Shop(c, new[] { Grey, Blue }, false, false); break;
            }
            Lights(c, kind == "home");
            Debug.Log($"[MafiaSandbox] Interior {b.name}: {c.made} pieces.");
        }

        static void Lights(Ctx c, bool ruined)
        {
            int nx = Mathf.Max(1, Mathf.RoundToInt(c.hx / 2.6f)), nz = Mathf.Max(1, Mathf.RoundToInt(c.hz / 2.6f));
            if (!ruined) for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++)
            {
                float x = (i + 0.5f) / nx * c.hx * 2f - c.hx, z = (j + 0.5f) / nz * c.hz * 2f - c.hz; var g = Pc(c, "CeilingLight", new Vector3(x, c.H - 0.08f, z), Vector3.forward); B(g, "Panel", 0f, 0f, 0f, 1.1f, 0.05f, 0.36f, C(1f, 0.97f, 0.88f), true);
            }
            var lg = new GameObject("InteriorLight"); lg.transform.SetParent(c.root, false); lg.transform.localPosition = new Vector3(0f, c.H - 0.5f, 0f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = Mathf.Max(c.hx, c.hz) * 2.1f; l.intensity = ruined ? 0.55f : 0.95f; l.color = ruined ? C(0.85f, 0.8f, 0.7f) : C(1f, 0.96f, 0.86f); l.shadows = LightShadows.None;
        }

        // a quick helper: fill free wall slots round the room with pieces from a pool
        static void Perimeter(Ctx c, int skipWall, params (float w, float d, float h, Build b, int weight)[] pool)
        {
            var bag = new List<int>(); for (int i = 0; i < pool.Length; i++) for (int k = 0; k < pool[i].weight; k++) bag.Add(i);
            for (int wall = 0; wall < 4; wall++)
            {
                if (wall == skipWall) continue; float len = wall < 2 ? c.hx : c.hz; float a = -len + 0.7f;
                while (a < len - 0.5f)
                {
                    var it = pool[bag[c.rng.Next(bag.Count)]]; float at = a + it.w * 0.5f;
                    if (Wall(c, wall, at, it.w, it.d, it.h, it.b)) a += it.w + 0.5f + (float)c.rng.NextDouble() * 0.8f; else a += 0.6f;
                }
            }
        }
        static void Art(Ctx c, int skipWall = -1)
        {
            var arts = new[] { C(0.75f, 0.35f, 0.25f), C(0.3f, 0.5f, 0.7f), C(0.85f, 0.75f, 0.45f), C(0.4f, 0.6f, 0.4f) };
            for (int wall = 0; wall < 4; wall++) { if (wall == skipWall) continue; float len = wall < 2 ? c.hx : c.hz; for (float a = -len + 1.2f; a < len - 0.8f; a += 2.3f + c.R()) Hang(c, wall, a, 0.8f + c.R() * 0.4f, 0.6f + c.R() * 0.3f, 1.7f, DarkWood, arts[c.rng.Next(arts.Length)]); }
        }
        static void Rug(Ctx c, float x, float z, float w, float d, Color col) { Flat(c, x, z, w, d, col); Flat(c, x, z, w - 0.3f, d - 0.3f, col * 1.25f, 0.035f, "RugInner"); }
        static void Sets(Ctx c, int n, System.Func<float, float, bool> place)
        {
            int made = 0; for (int tries = 0; tries < 80 && made < n; tries++) { float x = (c.R() * 2f - 1f) * (c.hx - 1.6f), z = (c.R() * 2f - 1f) * (c.hz - 1.6f); if (place(x, z)) made++; }
        }

        static void House(Ctx c)
        {
            var sofaCol = new[] { C(0.45f, 0.3f, 0.3f), C(0.3f, 0.42f, 0.5f), C(0.4f, 0.45f, 0.3f), C(0.5f, 0.42f, 0.28f) }[c.rng.Next(4)];
            // living corner: sofa + coffee table + rug in a free spot, facing the room
            Sets(c, 1, (x, z) => { if (!Free(c, new Rect(x - 1.6f, z - 1.5f, 3.2f, 3f), 0.1f)) return false; Island(c, x, z - 0.9f, Vector3.forward, 2.0f, 0.9f, 0.9f, Sofa(2f, sofaCol), true, 0.1f, "Sofa"); Island(c, x, z + 0.3f, Vector3.forward, 1.1f, 0.6f, 0.45f, Table(1.1f, 0.6f, 0.4f, Wood), true, 0.1f, "CoffeeTable"); Rug(c, x, z - 0.1f, 2.6f, 2.4f, C(0.55f, 0.3f, 0.28f)); return true; });
            Perimeter(c, 1, (1.2f, 0.4f, 2f, Shelf(1.2f, 2f, Wood, new[] { Red, Blue, Gold, Green, White }, c.rng), 3), (0.5f, 0.5f, 1.1f, Plant(1f), 3), (1.1f, 0.5f, 1.0f, Cabinet(1.1f, 1f, DarkWood), 2), (0.4f, 0.4f, 1.7f, Lamp(), 2), (0.95f, 0.9f, 0.9f, Sofa(0.95f, sofaCol * 1.1f), 1));
            Art(c);
            Hang(c, 0, 0f, 1.3f, 0.8f, 1.6f, Char, C(0.95f, 0.9f, 0.75f));
        }

        static void Ruin(Ctx c)
        {
            // burnt out: soot, rubble, broken furniture, one thing that survived
            for (int i = 0; i < 10; i++) Flat(c, (c.R() * 2f - 1f) * c.hx * 0.85f, (c.R() * 2f - 1f) * c.hz * 0.85f, 1.2f + c.R() * 2.2f, 1f + c.R() * 2f, C(0.06f, 0.055f, 0.05f), 0.015f + i * 0.001f, "Soot");
            Perimeter(c, -1, (0.9f, 0.9f, 0.7f, Rubble(c.rng, 1.1f), 4), (1.4f, 0.6f, 0.4f, Planks(c.rng), 2), (0.95f, 0.9f, 0.8f, Sofa(0.95f, C(0.14f, 0.1f, 0.09f)), 1), (1.1f, 0.5f, 0.9f, Cabinet(1.1f, 0.9f, Char), 1), (0.6f, 0.6f, 0.7f, Barrel(C(0.2f, 0.17f, 0.15f)), 1));
            Sets(c, 6, (x, z) => Island(c, x, z, Vector3.forward, 0.9f, 0.9f, 0.3f, Rubble(c.rng, 1.0f), false, 0.4f, "Rubble"));
            Sets(c, 2, (x, z) => Island(c, x, z, Vector3.forward, 1.0f, 0.9f, 0.5f, p => { B(p, "BrokenTable", 0f, 0.5f, 0f, 1.1f, 0.06f, 0.7f, Char).localRotation = Quaternion.Euler(0f, 0f, 14f); B(p, "Leg", 0.4f, 0.22f, 0f, 0.06f, 0.4f, 0.06f, DarkWood); }, true, 0.4f, "BrokenTable"));
            Island(c, -c.hx + 1.2f, c.hz - 1.4f, Vector3.forward, 0.4f, 0.3f, 0.1f, p => { B(p, "Photo", 0f, 0.04f, 0f, 0.36f, 0.04f, 0.28f, DarkWood); B(p, "Pic", 0f, 0.065f, 0f, 0.3f, 0.01f, 0.22f, C(0.85f, 0.8f, 0.7f)); }, false, 0.1f, "FamilyPhoto");   // the family photo
            for (int wall = 0; wall < 4; wall++) { float len = wall < 2 ? c.hx : c.hz; for (float a = -len + 1.4f; a < len - 0.8f; a += 2.8f + c.R() * 1.5f) Hang(c, wall, a, 0.7f + c.R() * 0.5f, 0.6f + c.R() * 0.6f, 1.3f + c.R() * 0.7f, C(0.16f, 0.13f, 0.11f), C(0.11f, 0.09f, 0.08f)); }   // soot marks on the walls
        }

        static void Shop(Ctx c, Color[] stock, bool crates, bool x)
        {
            Perimeter(c, 1, (0.65f, 0.65f, 1.4f, Crate(Wood), 3), (0.6f, 0.6f, 0.9f, Barrel(C(0.3f, 0.4f, 0.55f)), 1), (0.5f, 0.5f, 1.1f, Plant(1f), 1), (0.4f, 0.4f, 0.5f, Bin(), 1));
            Art(c, 1); for (float a = -c.hx + 1.5f; a < c.hx - 1f; a += 3.2f) Hang(c, 0, a, 1.4f, 0.5f, 2.3f, Red, C(0.97f, 0.9f, 0.4f));   // SALE banners
            Flat(c, c.doorX, c.hz - 0.8f, 1.8f, 1.1f, C(0.2f, 0.22f, 0.26f), 0.02f, "DoorMat");
        }
        static void Cafe(Ctx c)
        {
            Perimeter(c, 1, (0.5f, 0.5f, 1.1f, Plant(1f), 2), (0.9f, 0.5f, 0.9f, Cabinet(0.9f, 0.9f, Wood), 1), (1.2f, 0.4f, 2f, Shelf(1.2f, 2f, DarkWood, new[] { Cream, Red, Gold, Green }, c.rng), 2), (0.9f, 0.9f, 0.9f, Sofa(0.9f, C(0.5f, 0.26f, 0.22f)), 1));
            Art(c, 1); Rug(c, 0f, 0.5f, 3.5f, 2.2f, C(0.4f, 0.22f, 0.18f));
            for (float x = -1.6f; x <= 1.6f; x += 1.0f) Island(c, x, -2.45f, Vector3.back, 0.38f, 0.38f, 0.7f, Stool(Red), true, 0.05f, "BarStool");
        }
        static void Pharmacy(Ctx c)
        {
            Perimeter(c, 1, (1.2f, 0.4f, 2f, Shelf(1.2f, 2f, White, new[] { Green, White, Blue, Red }, c.rng), 3), (0.5f, 0.5f, 1.1f, Plant(1f), 1), (1.6f, 0.45f, 0.8f, Bench(1.6f, Blue), 1), (0.5f, 0.6f, 1.3f, Filing(), 1));
            Hang(c, 0, 0f, 1.2f, 1.2f, 2.2f, Green, White); Hang(c, 0, 0f, 0.8f, 0.2f, 2.2f, Green, Green); Art(c, 1);
            Rug(c, c.doorX, 0f, 2.4f, 1.6f, C(0.35f, 0.5f, 0.45f));
        }
        static void Gun(Ctx c)
        {
            Perimeter(c, 1, (0.65f, 0.65f, 1.4f, Crate(C(0.28f, 0.32f, 0.24f)), 3), (0.6f, 0.6f, 0.9f, Barrel(Char), 1), (1.4f, 0.4f, 2f, Cabinet(1.4f, 1.8f, Char), 1));
            for (float a = -c.hx + 1.6f; a < c.hx - 1f; a += 2.6f) { var rack = GunRack(1.8f); Wall(c, 0, a, 1.8f, 0.1f, 0.4f, rack, false, 0.05f, "GunRack"); }
            Hang(c, 2, 0f, 1.2f, 1.2f, 1.8f, Char, C(0.85f, 0.15f, 0.15f)); Hang(c, 3, 0f, 1.2f, 1.2f, 1.8f, Char, C(0.9f, 0.9f, 0.9f));
        }
        static void Pawn(Ctx c)
        {
            Perimeter(c, 1, (0.65f, 0.65f, 1.4f, Crate(DarkWood), 3), (1.2f, 0.4f, 2f, Shelf(1.2f, 2f, DarkWood, new[] { Gold, Grey, Blue, Steel, Red }, c.rng), 3), (0.9f, 0.5f, 1.0f, Cabinet(0.9f, 1f, Grey), 1), (0.6f, 0.6f, 0.9f, Barrel(Grey), 1));
            Art(c, 1); Island(c, -c.hx + 1.6f, c.hz - 1.0f, Vector3.forward, 0.9f, 0.9f, 1.2f, p => { B(p, "Guitar", 0f, 0.7f, 0f, 0.3f, 1.1f, 0.06f, Gold); B(p, "Neck", 0f, 1.45f, 0f, 0.07f, 0.6f, 0.04f, DarkWood); }, false, 0.3f, "Guitar");
        }
        static void Restaurant(Ctx c)
        {
            Perimeter(c, 1, (0.5f, 0.5f, 1.1f, Plant(1.1f), 2), (1.2f, 0.4f, 2f, Shelf(1.2f, 2f, DarkWood, new[] { C(0.45f, 0.1f, 0.15f), Green, Gold }, c.rng), 2), (0.4f, 0.4f, 0.5f, Bin(), 1));
            Art(c, 1); Rug(c, 0f, 0f, 3.2f, 2.4f, C(0.45f, 0.18f, 0.18f));
            for (float x = -c.hx + 1.3f; x < c.hx - 0.8f; x += 2.6f) Hang(c, 0, x, 1.4f, 0.9f, 1.9f, Gold, C(0.2f, 0.12f, 0.1f));
        }
        static void Jeweler(Ctx c)
        {
            Perimeter(c, 1, (0.5f, 0.5f, 1.1f, Plant(1f), 1), (1.2f, 0.45f, 1.2f, Display(1.2f, White, Gold), 3));
            Rug(c, 0f, 0f, 3.2f, 2.2f, C(0.2f, 0.1f, 0.3f)); Art(c, 1); Hang(c, 0, 0f, 1.6f, 0.9f, 1.7f, Gold, C(0.15f, 0.08f, 0.2f));
        }
        static void Barber(Ctx c)
        {
            for (float a = -c.hz + 1.2f; a < c.hz - 0.8f; a += 1.9f) { Wall(c, 2, a, 1.3f, 0.2f, 2f, Mirror(1.3f), false, 0.05f, "Mirror"); Island(c, -c.hx + 1.4f, a, Vector3.right, 0.6f, 0.6f, 1.2f, BarberChair(), true, 0.2f, "BarberChair"); }
            Perimeter(c, 1, (1.6f, 0.45f, 0.9f, Bench(1.6f, Blue), 1), (0.5f, 0.5f, 1.1f, Plant(1f), 1), (0.4f, 0.4f, 0.5f, Bin(), 1));
            Flat(c, 0f, 0f, 2.6f, 2.2f, C(0.15f, 0.2f, 0.28f)); Hang(c, 0, 0f, 1.0f, 1.0f, 1.8f, Red, White);
        }
        static void Bank(Ctx c)
        {
            Perimeter(c, 1, (0.5f, 0.5f, 1.1f, Plant(1.2f), 2), (1.6f, 0.45f, 0.9f, Bench(1.6f, DarkWood), 2), (0.5f, 0.6f, 1.3f, Filing(), 1), (0.9f, 0.8f, 1.9f, Vending(C(0.2f, 0.4f, 0.6f)), 1));
            Rug(c, 0f, 2.2f, 5.5f, 2.6f, C(0.2f, 0.28f, 0.2f)); Art(c, 1); Hang(c, 0, 0f, 3f, 0.9f, 3.0f, Gold, C(0.1f, 0.2f, 0.12f));
            for (float x = -3.2f; x <= 3.2f; x += 1.6f) Island(c, x, 0.9f, Vector3.forward, 0.12f, 0.12f, 1.0f, p => { B(p, "Pole", 0f, 0.5f, 0f, 0.06f, 1f, 0.06f, Gold); B(p, "Base", 0f, 0.02f, 0f, 0.3f, 0.04f, 0.3f, Gold); }, false, 0.2f, "QueuePole");
            Island(c, 6.3f, 3.5f, Vector3.left, 0.7f, 0.8f, 1.8f, p => { B(p, "ATM", 0f, 0.9f, 0f, 0.7f, 1.8f, 0.7f, C(0.2f, 0.22f, 0.26f)); B(p, "Screen", 0f, 1.3f, 0.36f, 0.5f, 0.3f, 0.02f, C(0.3f, 0.8f, 0.5f), true); }, true, 0.2f, "ATM");
        }
        static void Police(Ctx c)
        {
            Perimeter(c, 1, (0.5f, 0.6f, 1.3f, Filing(), 3), (0.5f, 0.5f, 1.1f, Plant(1f), 1), (1.6f, 0.45f, 0.9f, Bench(1.6f, Grey), 1), (0.4f, 0.4f, 0.5f, Bin(), 1));
            Sets(c, 2, (x, z) => { bool ok = Island(c, x, z, Vector3.forward, 1.5f, 0.75f, 0.8f, Desk(Grey, true), true, 0.9f, "Desk"); if (ok) Island(c, x, z + 0.95f, Vector3.back, 0.45f, 0.45f, 0.9f, Chair(DarkWood), false, 0.0f, "DeskChair"); return ok; });
            Hang(c, 0, 0f, 1.6f, 1.0f, 1.9f, Char, C(0.85f, 0.8f, 0.6f)); Hang(c, 2, 0f, 1.2f, 0.8f, 1.9f, Char, C(0.25f, 0.4f, 0.7f));
            Flat(c, 0f, 0f, 3f, 2.2f, C(0.18f, 0.22f, 0.3f));
        }
    }
}
