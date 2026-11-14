using System.Collections.Generic;
using UnityEngine;

namespace MafiaSandbox.Player
{
    /// <summary>What the character-creation screen edits. Saved as JSON in PlayerPrefs.</summary>
    [System.Serializable]
    public class Look
    {
        public string name = "Vito";
        public int hair = 0;                      // -1 none, 0..5 → HairNames
        public int hat = -1;                      // -1 none, 0..5 → HatNames
        public int top = 0;                       // TopNames
        public int bottom = 0;                    // BottomNames
        public bool beard, mustache, glasses;
        public Color skin = new Color(0.90f, 0.70f, 0.55f), hairCol = new Color(0.30f, 0.18f, 0.10f);
        public Color topCol = new Color(0.80f, 0.12f, 0.10f), pantsCol = new Color(0.25f, 0.35f, 0.55f), shoesCol = new Color(0.93f, 0.93f, 0.93f), hatCol = new Color(0.45f, 0.25f, 0.60f);
        public float weight = 0f;                 // -1 skeletal … 0 average … +1 very heavy
        public float height = 0f;                 // -1 short … +1 tall

        public static readonly string[] HairNames = { "Short", "Long", "Bun", "Ponytail", "Curly", "Balding" };
        public static readonly string[] HatNames = { "Cap", "Backwards cap", "Beanie", "Fedora", "Hard hat", "Hood" };
        public static readonly string[] TopNames = { "T-shirt", "Long sleeve", "Tank top", "Shirt & tie", "Vest" };
        public static readonly string[] BottomNames = { "Pants", "Shorts", "Skirt" };

        const string Key = "ms_look_v1";
        public static Look Load() { try { var s = PlayerPrefs.GetString(Key, ""); if (!string.IsNullOrEmpty(s)) return JsonUtility.FromJson<Look>(s); } catch { } return new Look(); }
        public void Save() { PlayerPrefs.SetString(Key, JsonUtility.ToJson(this)); PlayerPrefs.Save(); }

        public static Look Random()
        {
            var l = new Look { name = "Vito" };
            Color[] skins = CharacterPalettes.Skin, hairs = CharacterPalettes.Hair, cl = CharacterPalettes.Clothes;
            l.skin = skins[UnityEngine.Random.Range(0, skins.Length)]; l.hairCol = hairs[UnityEngine.Random.Range(0, 7)];
            l.hair = UnityEngine.Random.Range(-1, 6); l.hat = UnityEngine.Random.value < 0.3f ? UnityEngine.Random.Range(0, 6) : -1;
            l.top = UnityEngine.Random.Range(0, 5); l.bottom = UnityEngine.Random.value < 0.15f ? 2 : UnityEngine.Random.value < 0.3f ? 1 : 0;
            l.beard = UnityEngine.Random.value < 0.25f; l.mustache = UnityEngine.Random.value < 0.25f; l.glasses = UnityEngine.Random.value < 0.25f;
            l.topCol = cl[UnityEngine.Random.Range(0, cl.Length)]; l.pantsCol = cl[UnityEngine.Random.Range(0, cl.Length)]; l.shoesCol = cl[UnityEngine.Random.Range(0, cl.Length)]; l.hatCol = cl[UnityEngine.Random.Range(0, cl.Length)];
            l.weight = UnityEngine.Random.Range(-0.8f, 0.9f); l.height = UnityEngine.Random.Range(-0.6f, 0.6f);
            return l;
        }
    }

    public static class CharacterPalettes
    {
        public static readonly Color[] Skin = { C(0.96f, 0.80f, 0.68f), C(0.90f, 0.70f, 0.55f), C(0.80f, 0.56f, 0.40f), C(0.62f, 0.40f, 0.26f), C(0.46f, 0.29f, 0.19f), C(0.36f, 0.22f, 0.14f) };
        public static readonly Color[] Hair = { C(0.08f, 0.07f, 0.07f), C(0.17f, 0.10f, 0.06f), C(0.30f, 0.18f, 0.10f), C(0.60f, 0.25f, 0.10f), C(0.85f, 0.70f, 0.40f), C(0.70f, 0.70f, 0.72f), C(0.92f, 0.92f, 0.92f), C(0.20f, 0.40f, 0.90f), C(0.90f, 0.40f, 0.60f), C(0.25f, 0.70f, 0.35f) };
        public static readonly Color[] Clothes =
        {
            C(0.93f, 0.93f, 0.93f), C(0.45f, 0.46f, 0.50f), C(0.15f, 0.15f, 0.17f), C(0.10f, 0.14f, 0.30f), C(0.25f, 0.35f, 0.55f), C(0.80f, 0.12f, 0.10f), C(0.92f, 0.45f, 0.10f), C(0.95f, 0.80f, 0.20f),
            C(0.25f, 0.50f, 0.30f), C(0.40f, 0.42f, 0.25f), C(0.15f, 0.55f, 0.55f), C(0.45f, 0.25f, 0.60f), C(0.90f, 0.45f, 0.60f), C(0.40f, 0.25f, 0.14f), C(0.76f, 0.65f, 0.48f), C(0.55f, 0.08f, 0.14f),
        };
        static Color C(float r, float g, float b) => new Color(r, g, b);
    }

    /// <summary>
    /// Builds the player's body from the parts in Player_Parts.glb (base head/body, hair styles, facial hair, glasses, hats, tops, bottoms —
    /// all skinned to one skeleton, so they share the animations) and recolours the vertex colours per role. Body weight / height scale the model root.
    /// </summary>
    public class PlayerAppearance : MonoBehaviour
    {
        public Look Current { get; private set; }
        public static string PlayerName => I && I.Current != null && !string.IsNullOrWhiteSpace(I.Current.name) ? I.Current.name.Trim() : "Vito";
        public static PlayerAppearance I { get; private set; }

        enum Role { None, Skin, Hair, Top, Pants, Shoes, Hat }
        // the sRGB colours the Blender builder used for each role
        static readonly Color[] RoleSrc = { Color.clear, new Color(0.90f, 0.70f, 0.55f), new Color(0.30f, 0.18f, 0.10f), new Color(0.80f, 0.12f, 0.10f), new Color(0.25f, 0.35f, 0.55f), new Color(0.93f, 0.93f, 0.93f), new Color(0.45f, 0.25f, 0.60f) };

        class PartInfo { public SkinnedMeshRenderer smr; public Mesh mesh; public Color[] src; public Role[] roles; public bool[] gamma; }
        readonly Dictionary<string, PartInfo> parts = new Dictionary<string, PartInfo>();
        Transform model;

        void Awake() { I = this; Init(); }
        void Start() { if (Current == null) Apply(Look.Load()); }

        public void Init()
        {
            if (parts.Count > 0) return;
            model = transform.Find("Model") ?? transform;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr.sharedMesh) continue;
                var pi = new PartInfo { smr = smr, mesh = Instantiate(smr.sharedMesh) };
                pi.mesh.name = smr.sharedMesh.name + " (look)"; smr.sharedMesh = pi.mesh;
                pi.src = pi.mesh.colors; if (pi.src == null || pi.src.Length == 0) { var c32 = pi.mesh.colors32; pi.src = new Color[c32.Length]; for (int i = 0; i < c32.Length; i++) pi.src[i] = c32[i]; }
                pi.roles = new Role[pi.src.Length]; pi.gamma = new bool[pi.src.Length];
                for (int i = 0; i < pi.src.Length; i++) pi.roles[i] = Classify(pi.src[i], out pi.gamma[i]);
                parts[smr.gameObject.name] = pi;
            }
        }

        static Role Classify(Color c, out bool gamma)
        {
            gamma = false; float best = 0.03f; Role r = Role.None;
            for (int k = 1; k < RoleSrc.Length; k++)
            {
                var lin = RoleSrc[k].linear;
                float dl = Mathf.Max(Mathf.Abs(c.r - lin.r), Mathf.Abs(c.g - lin.g), Mathf.Abs(c.b - lin.b));
                float dg = Mathf.Max(Mathf.Abs(c.r - RoleSrc[k].r), Mathf.Abs(c.g - RoleSrc[k].g), Mathf.Abs(c.b - RoleSrc[k].b));
                if (dl < best) { best = dl; r = (Role)k; gamma = false; }
                if (dg < best) { best = dg; r = (Role)k; gamma = true; }
            }
            return r;
        }

        public void Apply(Look l)
        {
            Init(); Current = l;
            foreach (var kv in parts)
            {
                string n = kv.Key; bool on;
                if (n == "Base") on = true;
                else if (n.StartsWith("Hair_")) on = l.hair >= 0 && n == "Hair_" + new[] { "short", "long", "bun", "ponytail", "curly", "sides" }[Mathf.Clamp(l.hair, 0, 5)];
                else if (n.StartsWith("Hat_")) on = l.hat >= 0 && n == "Hat_" + new[] { "cap", "bcap", "beanie", "fedora", "hardhat", "hood" }[Mathf.Clamp(l.hat, 0, 5)];
                else if (n.StartsWith("Top_")) on = n == "Top_" + new[] { "short", "long", "tank", "tie", "vest" }[Mathf.Clamp(l.top, 0, 4)];
                else if (n.StartsWith("Bottom_")) on = n == "Bottom_" + new[] { "pants", "shorts", "skirt" }[Mathf.Clamp(l.bottom, 0, 2)];
                else if (n == "Beard") on = l.beard; else if (n == "Mustache") on = l.mustache; else if (n == "Glasses") on = l.glasses;
                else on = false;
                kv.Value.smr.gameObject.SetActive(on);
                if (on) Recolor(kv.Value, l);
            }
            // body shape: weight widens, height stretches (the CharacterController keeps its size)
            float w = l.weight < 0f ? Mathf.Lerp(1f, 0.80f, -l.weight) : Mathf.Lerp(1f, 1.38f, l.weight);
            float h = 1f + 0.08f * l.height;
            if (model != transform) model.localScale = new Vector3(w, h, w);
        }

        void Recolor(PartInfo p, Look l)
        {
            var cols = new Color[p.src.Length];
            for (int i = 0; i < cols.Length; i++)
            {
                Color c = p.src[i]; Role r = p.roles[i];
                if (r != Role.None)
                {
                    Color t = r == Role.Skin ? l.skin : r == Role.Hair ? l.hairCol : r == Role.Top ? l.topCol : r == Role.Pants ? l.pantsCol : r == Role.Shoes ? l.shoesCol : l.hatCol;
                    c = p.gamma[i] ? t : t.linear; c.a = 1f;
                }
                cols[i] = c;
            }
            p.mesh.colors = cols;
        }
    }
}
