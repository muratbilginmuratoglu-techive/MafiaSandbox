using System.Collections.Generic;
using UnityEngine;

namespace MafiaSandbox.Economy
{
    /// <summary>
    /// Procedural product models for shop shelves: cola cans, bread, tins, cigarette cartons, medicine bottles, sacks ... (low-poly, origin on the floor of the item).
    /// Returns null for ids without a model, so EconomyManager falls back to the Esya_* prop.
    /// </summary>
    public static class ItemModels
    {
        static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
        static Mesh cube, cyl;
        static Color H(int h) => new Color(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f);

        static Material Mat(Color c, float gloss = 0.2f, float metal = 0f)
        {
            var key = new Color(c.r, c.g, c.b, gloss + metal * 7f);
            if (mats.TryGetValue(key, out var m) && m) return m;
            var sh = Shader.Find("Standard"); if (!sh) sh = Shader.Find("Legacy Shaders/Diffuse"); if (!sh) sh = Shader.Find("Sprites/Default");
            m = new Material(sh) { color = c };
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            mats[key] = m; return m;
        }
        static Mesh Prim(PrimitiveType t, ref Mesh cache)
        {
            if (!cache) { var g = GameObject.CreatePrimitive(t); cache = g.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(g); }
            return cache;
        }
        static GameObject Part(Transform parent, bool round, Vector3 pos, Vector3 size, Color c, float gloss = 0.2f, float metal = 0f, Quaternion? rot = null)
        {
            var go = new GameObject(round ? "Cyl" : "Box"); go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = round ? new Vector3(size.x, size.y * 0.5f, size.z) : size;   // size = real width / height / depth
            go.AddComponent<MeshFilter>().sharedMesh = round ? Prim(PrimitiveType.Cylinder, ref cyl) : Prim(PrimitiveType.Cube, ref cube);
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = Mat(c, gloss, metal); return go;
        }
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>Workshop products and their ingredients (built with the workshop's modelling kit).</summary>
        static GameObject Extra(string id)
        {
            var root = new GameObject("Item " + id); var t = root.transform;
            void Bottle(Color glass, Color cap) { Kit.La(t, 0f, 0f, 0f, new[] { 0f, 0f, 0.035f, 0f, 0.037f, 0.008f, 0.037f, 0.11f, 0.026f, 0.14f, 0.014f, 0.155f, 0.014f, 0.19f }, glass, 0.9f); Kit.Cy(t, 0f, 0.197f, 0f, 0.034f, 0.018f, cap, 1, 0.4f); Kit.Box(t, 0f, 0.06f, 0.038f, 0.05f, 0.05f, 0.002f, Kit.H(0xe9dcc0)); }
            void Baggie(Color c) { Kit.Box(t, 0f, 0.012f, 0f, 0.09f, 0.022f, 0.07f, c, 0.2f); Kit.Box(t, 0f, 0.014f, 0f, 0.1f, 0.024f, 0.076f, Kit.H(0xdfe8ee, 0.3f), 0.9f); Kit.Box(t, -0.048f, 0.014f, 0f, 0.006f, 0.026f, 0.078f, Kit.H(0x3a6fc4)); }
            void Crate(Color mark) { Kit.Box(t, 0f, 0.07f, 0f, 0.42f, 0.14f, 0.2f, Kit.H(0x7a5a36), 0.1f); foreach (float x in new[] { -0.16f, 0.16f }) Kit.Box(t, x, 0.07f, 0f, 0.03f, 0.146f, 0.206f, Kit.H(0x5c4330), 0.1f); Kit.Box(t, 0f, 0.142f, 0f, 0.16f, 0.004f, 0.08f, mark); }
            void Sack(Color c) { Kit.Box(t, 0f, 0.06f, 0f, 0.12f, 0.12f, 0.07f, c, 0.05f); Kit.Box(t, 0f, 0.13f, 0f, 0.08f, 0.03f, 0.05f, new Color(c.r * 0.8f, c.g * 0.8f, c.b * 0.8f), 0.05f); }
            switch (id)
            {
                case "Moonshine": Kit.La(t, 0f, 0f, 0f, new[] { 0f, 0f, 0.045f, 0f, 0.047f, 0.01f, 0.047f, 0.1f, 0.036f, 0.118f, 0.036f, 0.136f }, Kit.H(0xd8eef0, 0.45f), 0.9f); Kit.Cy(t, 0f, 0.142f, 0f, 0.08f, 0.016f, Kit.H(0xc9b25a), 1, 0.7f, 0.9f); Kit.Box(t, 0f, 0.06f, 0.048f, 0.06f, 0.05f, 0.002f, Kit.H(0xe9dcc0)); break;
                case "Whiskey": Bottle(Kit.H(0x9a4f12, 0.85f), Kit.H(0x3a2416)); break;
                case "Gin": Bottle(Kit.H(0xa9dcc8, 0.55f), Kit.H(0xc8ccd2)); break;
                case "Weed": Baggie(Kit.H(0x6fa63f)); break;
                case "Powder": Baggie(Kit.H(0xf4f4f0)); break;
                case "Crystal": Baggie(Kit.H(0x58c4f2)); break;
                case "Pills": Kit.La(t, 0f, 0f, 0f, new[] { 0f, 0f, 0.028f, 0f, 0.028f, 0.065f, 0.024f, 0.07f, 0.024f, 0.08f }, Kit.H(0xe07a1c, 0.7f), 0.9f); Kit.Cy(t, 0f, 0.088f, 0f, 0.056f, 0.018f, Kit.H(0xf1f1ec), 1, 0.4f); Kit.Box(t, 0f, 0.035f, 0.029f, 0.04f, 0.035f, 0.002f, Kit.H(0xf1f1ec)); break;
                case "Guns": case "Revolvers": Crate(Kit.H(0x2a2420)); break;
                case "Rifles": case "Shotguns": Crate(Kit.H(0x8a2f22)); break;
                case "KitStill": case "KitBench": Crate(Kit.H(0x2d7dd2)); break;
                case "MetalParts": Kit.Box(t, 0f, 0.02f, 0f, 0.16f, 0.04f, 0.1f, Kit.H(0x3a3d42), 0.4f, 0.6f); for (int i = 0; i < 5; i++) Kit.Cy(t, -0.05f + i * 0.025f, 0.05f, 0f, 0.02f, 0.08f, Kit.H(0x8d9299), 2, 0.7f, 0.9f); break;
                case "Gunpowder": Kit.Cy(t, 0f, 0.05f, 0f, 0.07f, 0.1f, Kit.H(0x1c1c1e), 1, 0.4f, 0.4f); Kit.Cy(t, 0f, 0.105f, 0f, 0.03f, 0.014f, Kit.H(0xb23a2e)); Kit.Box(t, 0f, 0.05f, 0.036f, 0.045f, 0.04f, 0.002f, Kit.H(0xe9b824)); break;
                case "OakChips": Sack(Kit.H(0x5a3a22)); break;
                case "Juniper": Sack(Kit.H(0x4f7a4a)); break;
                case "WoodStock": Kit.Box(t, 0f, 0.025f, 0f, 0.3f, 0.05f, 0.08f, Kit.H(0x6a3f20), 0.3f); break;
                default: Object.Destroy(root); return null;
            }
            return root;
        }

        public static GameObject Build(string id)
        {
            var root = new GameObject("Item " + id); var t = root.transform;
            Color silver = H(0xc9ced6), white = H(0xf2f2ee), red = H(0xd02a26), black = H(0x1c1c20);
            switch (id)
            {
                case "Kola":      // cola can
                    Part(t, true, V(0, 0.09f, 0), V(0.066f, 0.18f, 0.066f), red, 0.6f, 0.4f);
                    Part(t, true, V(0, 0.181f, 0), V(0.058f, 0.012f, 0.058f), silver, 0.8f, 0.8f);
                    Part(t, true, V(0, 0.09f, 0), V(0.069f, 0.05f, 0.069f), white, 0.5f);
                    Part(t, true, V(0, 0.003f, 0), V(0.06f, 0.006f, 0.06f), silver, 0.8f, 0.8f); break;
                case "Ekmek":     // bread loaf
                    Part(t, false, V(0, 0.05f, 0), V(0.34f, 0.1f, 0.13f), H(0xb97a3d));
                    Part(t, false, V(0, 0.115f, 0), V(0.3f, 0.04f, 0.115f), H(0xcf9150));
                    for (int i = -1; i <= 1; i++) Part(t, false, V(i * 0.08f, 0.138f, 0), V(0.012f, 0.006f, 0.1f), H(0xe8c387), 0.2f, 0f, Quaternion.Euler(0, 20, 0)); break;
                case "Konserve":  // tin can
                    Part(t, true, V(0, 0.045f, 0), V(0.075f, 0.09f, 0.075f), silver, 0.7f, 0.7f);
                    Part(t, true, V(0, 0.045f, 0), V(0.078f, 0.056f, 0.078f), H(0x3f8f3a), 0.3f);
                    Part(t, true, V(0, 0.045f, 0), V(0.079f, 0.016f, 0.079f), H(0xe8d24a), 0.3f); break;
                case "Sigara":    // cigarette carton: red, white band, gold stripe
                    Part(t, false, V(0, 0.05f, 0), V(0.22f, 0.1f, 0.045f), red);
                    Part(t, false, V(0, 0.05f, 0.0235f), V(0.22f, 0.04f, 0.003f), white);
                    Part(t, false, V(0, 0.082f, 0.0235f), V(0.22f, 0.008f, 0.003f), H(0xd4af37), 0.7f, 0.6f);
                    Part(t, false, V(0, 0.05f, 0.0265f), V(0.05f, 0.03f, 0.002f), black); break;
                case "Corn":      // grain sack
                    Part(t, false, V(0, 0.15f, 0), V(0.2f, 0.3f, 0.1f), H(0xd9c28a), 0.05f);
                    Part(t, false, V(0, 0.29f, 0), V(0.17f, 0.05f, 0.085f), H(0xc7ae72), 0.05f);
                    Part(t, false, V(0, 0.16f, 0.052f), V(0.13f, 0.1f, 0.004f), H(0xe9b824)); break;
                case "Sugar":     // sugar sack
                    Part(t, false, V(0, 0.15f, 0), V(0.2f, 0.3f, 0.1f), white, 0.05f);
                    Part(t, false, V(0, 0.29f, 0), V(0.17f, 0.05f, 0.085f), H(0xe4e4de), 0.05f);
                    Part(t, false, V(0, 0.16f, 0.052f), V(0.14f, 0.1f, 0.004f), H(0x3a6fc4)); break;
                case "Yeast":     // small packet
                    Part(t, false, V(0, 0.06f, 0), V(0.08f, 0.12f, 0.025f), H(0xe0861f));
                    Part(t, false, V(0, 0.115f, 0), V(0.085f, 0.012f, 0.03f), H(0xb96b14));
                    Part(t, false, V(0, 0.06f, 0.014f), V(0.06f, 0.05f, 0.002f), white); break;
                case "Seeds":     // seed packet
                    Part(t, false, V(0, 0.065f, 0), V(0.09f, 0.13f, 0.02f), H(0x59a845));
                    Part(t, false, V(0, 0.07f, 0.0115f), V(0.07f, 0.07f, 0.002f), white);
                    Part(t, true, V(0, 0.07f, 0.0135f), V(0.03f, 0.002f, 0.03f), H(0xd7452f), 0.3f, 0f, Quaternion.Euler(90, 0, 0)); break;
                case "PlantFood": // plant food bottle
                    Part(t, true, V(0, 0.09f, 0), V(0.07f, 0.18f, 0.07f), H(0x2f7d3f), 0.6f);
                    Part(t, true, V(0, 0.195f, 0), V(0.03f, 0.03f, 0.03f), white);
                    Part(t, true, V(0, 0.09f, 0), V(0.072f, 0.07f, 0.072f), white, 0.3f); break;
                case "Ilac":      // pill bottle
                    Part(t, true, V(0, 0.055f, 0), V(0.055f, 0.11f, 0.055f), H(0xd77a1c), 0.7f);
                    Part(t, true, V(0, 0.118f, 0), V(0.06f, 0.025f, 0.06f), white);
                    Part(t, true, V(0, 0.055f, 0), V(0.057f, 0.06f, 0.057f), white, 0.3f); break;
                case "ColdMed":   // cold medicine box with a red cross
                    Part(t, false, V(0, 0.07f, 0), V(0.13f, 0.14f, 0.05f), H(0x3a8fd8));
                    Part(t, false, V(0, 0.07f, 0.0255f), V(0.07f, 0.07f, 0.002f), white);
                    Part(t, false, V(0, 0.07f, 0.0275f), V(0.05f, 0.014f, 0.002f), red); Part(t, false, V(0, 0.07f, 0.0275f), V(0.014f, 0.05f, 0.002f), red); break;
                case "Solvent":   // jerry can
                    Part(t, false, V(0, 0.11f, 0), V(0.17f, 0.22f, 0.08f), H(0xc9361f), 0.5f);
                    Part(t, false, V(0.04f, 0.235f, 0), V(0.05f, 0.03f, 0.05f), black);
                    Part(t, false, V(0, 0.11f, 0.042f), V(0.1f, 0.1f, 0.002f), H(0xf4d03a)); break;
                case "Precursor": // dark glass flask with a label
                    Part(t, true, V(0, 0.07f, 0), V(0.09f, 0.14f, 0.09f), H(0x5a2f7a), 0.9f);
                    Part(t, true, V(0, 0.17f, 0), V(0.035f, 0.06f, 0.035f), H(0x5a2f7a), 0.9f);
                    Part(t, true, V(0, 0.205f, 0), V(0.04f, 0.015f, 0.04f), black);
                    Part(t, true, V(0, 0.07f, 0), V(0.093f, 0.05f, 0.093f), H(0xe8e2c8), 0.2f); break;
                case "KitLab":    // equipment crate
                    Part(t, false, V(0, 0.11f, 0), V(0.4f, 0.22f, 0.28f), H(0x6c7480), 0.4f, 0.3f);
                    Part(t, false, V(0, 0.11f, 0), V(0.405f, 0.05f, 0.285f), H(0x2d7dd2));
                    Part(t, false, V(0, 0.23f, 0), V(0.3f, 0.02f, 0.06f), black); break;
                default: Object.Destroy(root); return Extra(id);
            }
            return root;
        }
    }
}
