using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MafiaSandbox.Economy
{
    /// <summary>
    /// Small procedural modelling kit for the workshop: cached materials (opaque, glass, glow, ghost), lathe meshes (bottles, flasks, pots),
    /// extruded profiles (gun frames, slides, grips) and one-line box / cylinder / sphere parts. Nothing here has a collider.
    /// </summary>
    public static class Kit
    {
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static Mesh cube, cyl, sph;

        public static Color H(int h, float a = 1f) => new Color(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f, a);

        public static Material Mat(Color c, float gloss = 0.25f, float metal = 0f, float glow = 0f)
        {
            string k = c.r.ToString("0.000") + c.g.ToString("0.000") + c.b.ToString("0.000") + c.a.ToString("0.00") + "|" + gloss.ToString("0.00") + "|" + metal.ToString("0.00") + "|" + glow.ToString("0.00");
            if (mats.TryGetValue(k, out var m) && m) return m;
            var sh = Shader.Find("Standard"); if (!sh) sh = Shader.Find("Legacy Shaders/Diffuse");
            m = new Material(sh) { color = c };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (glow > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(c.r, c.g, c.b) * glow); }
            if (c.a < 0.99f)
            {
                m.SetFloat("_Mode", 3f); m.SetInt("_SrcBlend", (int)BlendMode.One); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON"); m.renderQueue = 3000;
            }
            mats[k] = m; return m;
        }

        /// <summary>Unlit see-through silhouette that shows where the next part goes.</summary>
        public static Material Ghost(Color c)
        {
            string k = "ghost" + c.r.ToString("0.00") + c.g.ToString("0.00") + c.b.ToString("0.00") + c.a.ToString("0.00");
            if (mats.TryGetValue(k, out var m) && m) return m;
            var sh = Shader.Find("Sprites/Default"); if (!sh) sh = Shader.Find("Standard");
            m = new Material(sh) { color = c }; m.renderQueue = 3100; mats[k] = m; return m;
        }

        static Mesh Prim(PrimitiveType t, ref Mesh cache)
        {
            if (!cache) { var g = GameObject.CreatePrimitive(t); cache = g.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(g); }
            return cache;
        }
        public static Mesh Cube => Prim(PrimitiveType.Cube, ref cube);
        public static Mesh Cylinder => Prim(PrimitiveType.Cylinder, ref cyl);
        public static Mesh Sphere => Prim(PrimitiveType.Sphere, ref sph);

        /// <summary>Surface of revolution around Y. prof = radius, height pairs from bottom to top. Flat shaded between profile points.</summary>
        public static Mesh Lathe(float[] prof, int seg = 18)
        {
            var sb = new System.Text.StringBuilder("L" + seg); foreach (var f in prof) sb.Append('_').Append(f.ToString("0.000")); string key = sb.ToString();
            if (meshes.TryGetValue(key, out var mesh) && mesh) return mesh;
            var v = new List<Vector3>(); var t = new List<int>(); int n = prof.Length / 2;
            for (int i = 0; i < n - 1; i++)
            {
                float r0 = prof[i * 2], y0 = prof[i * 2 + 1], r1 = prof[i * 2 + 2], y1 = prof[i * 2 + 3]; int b = v.Count;
                for (int j = 0; j <= seg; j++) { float a = j * Mathf.PI * 2f / seg; float c = Mathf.Cos(a), s = Mathf.Sin(a); v.Add(new Vector3(c * r0, y0, s * r0)); v.Add(new Vector3(c * r1, y1, s * r1)); }
                for (int j = 0; j < seg; j++) { int p = b + j * 2; t.Add(p); t.Add(p + 1); t.Add(p + 3); t.Add(p); t.Add(p + 3); t.Add(p + 2); }
            }
            void Cap(float r, float y, bool top)
            {
                if (r <= 0.0005f) return; int b = v.Count; v.Add(new Vector3(0f, y, 0f));
                for (int j = 0; j <= seg; j++) { float a = j * Mathf.PI * 2f / seg; v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)); }
                for (int j = 0; j < seg; j++) { t.Add(b); if (top) { t.Add(b + j + 2); t.Add(b + j + 1); } else { t.Add(b + j + 1); t.Add(b + j + 2); } }
            }
            Cap(prof[0], prof[1], false); Cap(prof[n * 2 - 2], prof[n * 2 - 1], true);
            mesh = new Mesh { name = key }; mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes[key] = mesh; return mesh;
        }

        /// <summary>Convex polygon in the XY plane (x, y pairs) extruded along Z. Flat shaded.</summary>
        public static Mesh Prism(float[] poly, float depth)
        {
            var sb = new System.Text.StringBuilder("P" + depth.ToString("0.000")); foreach (var f in poly) sb.Append('_').Append(f.ToString("0.000")); string key = sb.ToString();
            if (meshes.TryGetValue(key, out var mesh) && mesh) return mesh;
            int n = poly.Length / 2; var p = new Vector2[n]; for (int i = 0; i < n; i++) p[i] = new Vector2(poly[i * 2], poly[i * 2 + 1]);
            float area = 0f; for (int i = 0; i < n; i++) { var a = p[i]; var b = p[(i + 1) % n]; area += a.x * b.y - b.x * a.y; }
            if (area < 0f) System.Array.Reverse(p);   // counter-clockwise from here on
            var v = new List<Vector3>(); var t = new List<int>(); float z = depth * 0.5f;
            int f0 = v.Count; for (int i = 0; i < n; i++) v.Add(new Vector3(p[i].x, p[i].y, z));
            for (int i = 1; i < n - 1; i++) { t.Add(f0); t.Add(f0 + i); t.Add(f0 + i + 1); }
            int b0 = v.Count; for (int i = 0; i < n; i++) v.Add(new Vector3(p[i].x, p[i].y, -z));
            for (int i = 1; i < n - 1; i++) { t.Add(b0); t.Add(b0 + i + 1); t.Add(b0 + i); }
            for (int i = 0; i < n; i++)
            {
                var a = p[i]; var b = p[(i + 1) % n]; int s = v.Count;
                v.Add(new Vector3(a.x, a.y, z)); v.Add(new Vector3(b.x, b.y, z)); v.Add(new Vector3(b.x, b.y, -z)); v.Add(new Vector3(a.x, a.y, -z));
                t.Add(s); t.Add(s + 2); t.Add(s + 1); t.Add(s); t.Add(s + 3); t.Add(s + 2);
            }
            mesh = new Mesh { name = key }; mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes[key] = mesh; return mesh;
        }

        public static Transform Mk(Transform parent, string name, Mesh mesh, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
        {
            var go = new GameObject(name); var t = go.transform; t.SetParent(parent, false); t.localPosition = pos; t.localRotation = rot; t.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; return t;
        }
        public static Transform Node(Transform parent, string name, float x = 0f, float y = 0f, float z = 0f)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = new Vector3(x, y, z); return t;
        }
        /// <summary>Box: centre and real size.</summary>
        public static Transform Box(Transform p, float x, float y, float z, float sx, float sy, float sz, Color c, float gloss = 0.25f, float metal = 0f, string n = "Box", float glow = 0f)
            => Mk(p, n, Cube, new Vector3(x, y, z), new Vector3(sx, sy, sz), Quaternion.identity, Mat(c, gloss, metal, glow));
        /// <summary>Cylinder: centre, diameter and length; axis 0 = X, 1 = Y, 2 = Z.</summary>
        public static Transform Cy(Transform p, float x, float y, float z, float dia, float len, Color c, int axis = 1, float gloss = 0.25f, float metal = 0f, string n = "Cyl", float glow = 0f)
            => Mk(p, n, Cylinder, new Vector3(x, y, z), new Vector3(dia, len * 0.5f, dia), axis == 0 ? Quaternion.Euler(0f, 0f, 90f) : axis == 2 ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity, Mat(c, gloss, metal, glow));
        public static Transform Ball(Transform p, float x, float y, float z, float sx, float sy, float sz, Color c, float gloss = 0.25f, float metal = 0f, string n = "Ball", float glow = 0f)
            => Mk(p, n, Sphere, new Vector3(x, y, z), new Vector3(sx, sy, sz), Quaternion.identity, Mat(c, gloss, metal, glow));
        /// <summary>Lathe: position of the profile's y = 0 point.</summary>
        public static Transform La(Transform p, float x, float y, float z, float[] prof, Color c, float gloss = 0.25f, float metal = 0f, string n = "Lathe")
            => Mk(p, n, Lathe(prof), new Vector3(x, y, z), Vector3.one, Quaternion.identity, Mat(c, gloss, metal));
        /// <summary>Extruded profile, coordinates are the polygon's own (plus the offset).</summary>
        public static Transform Pr(Transform p, float[] poly, float depth, Color c, float gloss = 0.25f, float metal = 0f, float z = 0f, string n = "Prism")
            => Mk(p, n, Prism(poly, depth), new Vector3(0f, 0f, z), Vector3.one, Quaternion.identity, Mat(c, gloss, metal));

        public static Transform Find(Transform root, string name)
        {
            if (!root) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
        public static Bounds BoundsOf(Transform root)
        {
            var rs = root.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(root.position, Vector3.one * 0.1f);
            var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds); return b;
        }
    }
}
