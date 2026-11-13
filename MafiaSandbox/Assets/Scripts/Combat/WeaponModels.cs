using System.Collections.Generic;
using UnityEngine;

namespace MafiaSandbox.Combat
{
    /// <summary>
    /// Procedural low-poly weapon meshes (boxes + cylinders), ported from the prototype's buildPistol/…/buildBat.
    /// Built in the right-hand bone frame: +Y along the fingers (barrel direction), +Z up. Each model gets a "Muzzle" child.
    /// </summary>
    public static class WeaponModels
    {
        static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
        static Mesh cubeMesh, cylMesh;

        static readonly Color gun = Hex(0x2a2d33), gunDark = Hex(0x16181c), wood = Hex(0x6b4a2e), woodDark = Hex(0x4a3220), metal = Hex(0x5a5f68),
            chrome = Hex(0x9aa0a8), blade = Hex(0xd8dce2), tape = Hex(0x222222), brass = Hex(0xc9a24a), sight = Hex(0xff5a3c);

        static Color Hex(int h) => new Color(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f);

        static Material Mat(Color c)
        {
            if (mats.TryGetValue(c, out var m)) return m;
            var sh = Shader.Find("Standard"); if (!sh) sh = Shader.Find("Legacy Shaders/Diffuse"); if (!sh) sh = Shader.Find("Sprites/Default");
            m = new Material(sh) { color = c, name = "wpn_" + ColorUtility.ToHtmlStringRGB(c) };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", c == chrome || c == blade ? 0.75f : 0.25f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", c == chrome || c == blade || c == metal ? 0.8f : 0.1f);
            mats[c] = m; return m;
        }

        static Mesh Cube() { if (!cubeMesh) { var t = GameObject.CreatePrimitive(PrimitiveType.Cube); cubeMesh = t.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(t); } return cubeMesh; }
        static Mesh Cyl() { if (!cylMesh) { var t = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cylMesh = t.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(t); } return cylMesh; }

        static GameObject Part(Transform parent, Mesh mesh, Vector3 pos, Vector3 scale, Quaternion rot, Color c, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = scale;
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Mat(c); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        /// <summary>Box with size (w along X, l along Y, h along Z), tilted about X by rx radians (prototype convention).</summary>
        static GameObject Bx(Transform p, float w, float l, float h, float x, float y, float z, Color c, float rx = 0f) =>
            Part(p, Cube(), new Vector3(x, y, z), new Vector3(w, l, h), Quaternion.Euler(rx * Mathf.Rad2Deg, 0f, 0f), c, "box");

        /// <summary>Cylinder with radius r, length l along Y (prototype CylinderGeometry orientation).</summary>
        static GameObject Cy(Transform p, float r, float l, float x, float y, float z, Color c, float r2 = -1f)
        {
            float rr = r2 < 0 ? r : (r + r2) * 0.5f;
            return Part(p, Cyl(), new Vector3(x, y, z), new Vector3(rr * 2f, l * 0.5f, rr * 2f), Quaternion.identity, c, "cyl");
        }

        static GameObject Root(string name, Vector3 muzzle, Vector3 offset)
        {
            var g = new GameObject(name); g.transform.localPosition = offset;
            var m = new GameObject("Muzzle"); m.transform.SetParent(g.transform, false); m.transform.localPosition = muzzle;
            return g;
        }

        public static GameObject Build(WeaponKind k)
        {
            switch (k)
            {
                case WeaponKind.Pistol: return Pistol();
                case WeaponKind.Revolver: return Revolver();
                case WeaponKind.Rifle: return Rifle();
                case WeaponKind.Shotgun: return Shotgun();
                case WeaponKind.Knife: return Knife();
                default: return Bat();
            }
        }

        static GameObject Pistol()
        {
            var g = Root("Pistol", new Vector3(0, 0.21f, 0.062f), Vector3.zero); var t = g.transform;
            Bx(t, 0.03f, 0.19f, 0.034f, 0, 0.11f, 0.062f, gun);
            Bx(t, 0.022f, 0.07f, 0.012f, 0, 0.14f, 0.082f, gunDark);
            Bx(t, 0.034f, 0.17f, 0.028f, 0, 0.10f, 0.032f, gunDark);
            Bx(t, 0.012f, 0.03f, 0.03f, 0, 0.17f, 0.02f, gunDark);
            Bx(t, 0.03f, 0.045f, 0.11f, 0, 0.0f, -0.035f, gunDark, 0.3f);
            Bx(t, 0.026f, 0.02f, 0.05f, 0, 0.012f, -0.03f, tape, 0.3f);
            Bx(t, 0.004f, 0.06f, 0.005f, 0, 0.07f, -0.002f, gunDark); Bx(t, 0.004f, 0.005f, 0.03f, 0, 0.1f, 0.012f, gunDark);
            Bx(t, 0.005f, 0.006f, 0.02f, 0, 0.065f, 0.012f, metal, 0.3f);
            Bx(t, 0.006f, 0.008f, 0.012f, 0, 0.195f, 0.084f, gunDark); Bx(t, 0.018f, 0.008f, 0.01f, 0, 0.03f, 0.084f, gunDark);
            Cy(t, 0.006f, 0.012f, 0, 0.206f, 0.062f, gunDark);
            return g;
        }

        static GameObject Revolver()
        {
            var g = Root("Revolver", new Vector3(0, 0.28f, 0.072f), Vector3.zero); var t = g.transform;
            Bx(t, 0.03f, 0.09f, 0.05f, 0, 0.07f, 0.055f, chrome);
            Cy(t, 0.011f, 0.17f, 0, 0.19f, 0.072f, chrome);
            Bx(t, 0.016f, 0.15f, 0.022f, 0, 0.18f, 0.05f, chrome);
            Bx(t, 0.008f, 0.15f, 0.01f, 0, 0.18f, 0.09f, chrome);
            Cy(t, 0.022f, 0.05f, 0, 0.09f, 0.06f, metal);
            Bx(t, 0.012f, 0.02f, 0.02f, 0, 0.03f, 0.09f, gunDark, -0.5f);
            Bx(t, 0.03f, 0.04f, 0.1f, 0, 0.0f, -0.025f, wood, 0.45f);
            Bx(t, 0.022f, 0.03f, 0.08f, 0, 0.0f, -0.025f, woodDark, 0.45f);
            Bx(t, 0.004f, 0.05f, 0.005f, 0, 0.065f, 0.02f, chrome); Bx(t, 0.004f, 0.005f, 0.03f, 0, 0.09f, 0.03f, chrome);
            Bx(t, 0.005f, 0.006f, 0.018f, 0, 0.06f, 0.03f, gunDark, 0.3f);
            Bx(t, 0.004f, 0.006f, 0.012f, 0, 0.27f, 0.085f, sight);
            return g;
        }

        static GameObject Rifle()
        {
            var g = Root("Rifle", new Vector3(0, 1.02f, 0.045f), new Vector3(0, -0.02f, 0)); var t = g.transform;
            Bx(t, 0.04f, 0.3f, 0.07f, 0, 0.3f, 0.03f, gun);
            Bx(t, 0.03f, 0.08f, 0.02f, 0, 0.34f, 0.075f, gunDark);
            Bx(t, 0.044f, 0.26f, 0.06f, 0, 0.56f, 0.03f, wood);
            Bx(t, 0.046f, 0.03f, 0.062f, 0, 0.44f, 0.03f, gunDark); Bx(t, 0.046f, 0.03f, 0.062f, 0, 0.68f, 0.03f, gunDark);
            Cy(t, 0.009f, 0.3f, 0, 0.84f, 0.045f, metal);
            Cy(t, 0.007f, 0.26f, 0, 0.78f, 0.015f, metal);
            Cy(t, 0.013f, 0.05f, 0, 0.99f, 0.045f, gunDark);
            Bx(t, 0.006f, 0.006f, 0.05f, 0, 0.9f, 0.08f, gunDark); Bx(t, 0.024f, 0.01f, 0.012f, 0, 0.9f, 0.1f, gunDark);
            Bx(t, 0.03f, 0.05f, 0.03f, 0, 0.26f, 0.08f, gunDark); Bx(t, 0.006f, 0.02f, 0.012f, 0, 0.26f, 0.1f, gunDark);
            Bx(t, 0.032f, 0.06f, 0.2f, 0, 0.3f, -0.1f, gunDark, -0.25f);
            Bx(t, 0.03f, 0.045f, 0.1f, 0, 0.14f, -0.05f, gunDark, 0.4f);
            Bx(t, 0.004f, 0.08f, 0.005f, 0, 0.21f, -0.005f, gunDark); Bx(t, 0.005f, 0.006f, 0.02f, 0, 0.2f, 0.01f, metal, 0.3f);
            Bx(t, 0.04f, 0.26f, 0.085f, 0, 0.0f, -0.01f, wood, 0.1f);
            Bx(t, 0.044f, 0.03f, 0.1f, 0, -0.14f, -0.02f, gunDark);
            Bx(t, 0.012f, 0.04f, 0.012f, 0, 0.78f, -0.01f, metal);
            return g;
        }

        static GameObject Shotgun()
        {
            var g = Root("Shotgun", new Vector3(0, 0.88f, 0.052f), new Vector3(0, -0.02f, 0)); var t = g.transform;
            var pump = Bx(t, 0.05f, 0.17f, 0.05f, 0, 0.5f, 0.012f, woodDark); pump.name = "Pump";
            for (int i = 0; i < 5; i++) { var r = Bx(pump.transform, 1.08f, 0.012f / 0.17f, 1.08f, 0, -0.41f + i * 0.206f, 0, wood); }
            Bx(t, 0.045f, 0.3f, 0.075f, 0, 0.2f, 0.025f, gun);
            Bx(t, 0.02f, 0.06f, 0.02f, 0.03f, 0.17f, 0.02f, gunDark);
            Cy(t, 0.011f, 0.56f, 0, 0.6f, 0.052f, metal);
            Cy(t, 0.009f, 0.5f, 0, 0.58f, 0.01f, metal);
            Bx(t, 0.02f, 0.04f, 0.05f, 0, 0.84f, 0.03f, gunDark);
            Bx(t, 0.004f, 0.006f, 0.01f, 0, 0.88f, 0.068f, brass);
            Bx(t, 0.004f, 0.07f, 0.005f, 0, 0.09f, -0.015f, gunDark); Bx(t, 0.005f, 0.006f, 0.02f, 0, 0.08f, 0.0f, metal, 0.3f);
            Bx(t, 0.04f, 0.08f, 0.1f, 0, 0.0f, -0.035f, wood, 0.35f);
            Bx(t, 0.045f, 0.3f, 0.1f, 0, -0.17f, -0.02f, wood, 0.08f);
            Bx(t, 0.05f, 0.025f, 0.11f, 0, -0.325f, -0.03f, tape);
            Bx(t, 0.05f, 0.04f, 0.03f, 0, 0.26f, 0.075f, gunDark);
            return g;
        }

        static GameObject Knife()
        {
            var g = Root("Knife", new Vector3(0, 0.3f, 0), new Vector3(0, 0.06f, 0)); var t = g.transform;
            var handle = Bx(t, 0.028f, 0.12f, 0.034f, 0, 0.0f, 0, tape);
            for (int i = 0; i < 3; i++) Bx(t, 0.032f, 0.006f, 0.006f, 0, -0.04f + i * 0.04f, 0.008f, brass);
            Bx(t, 0.05f, 0.015f, 0.06f, 0, 0.068f, 0, metal); Bx(t, 0.01f, 0.21f, 0.04f, 0, 0.18f, 0.004f, blade); Bx(t, 0.004f, 0.21f, 0.012f, 0, 0.18f, -0.02f, chrome);
            Bx(t, 0.01f, 0.05f, 0.02f, 0, 0.3f, 0.012f, blade); Bx(t, 0.03f, 0.02f, 0.03f, 0, -0.07f, 0, metal);
            return g;
        }

        static GameObject Bat()
        {
            var g = Root("Bat", new Vector3(0, 0.85f, 0), new Vector3(0, 0.02f, 0)); var t = g.transform;
            Cy(t, 0.036f, 0.85f, 0, 0.42f, 0, wood, 0.02f);
            Cy(t, 0.02f, 0.22f, 0, 0.08f, 0, tape, 0.021f); Cy(t, 0.03f, 0.025f, 0, -0.03f, 0, woodDark);
            Cy(t, 0.034f, 0.015f, 0, 0.6f, 0, woodDark); Bx(t, 0.004f, 0.14f, 0.03f, 0.034f, 0.5f, 0, woodDark);
            return g;
        }
    }
}
