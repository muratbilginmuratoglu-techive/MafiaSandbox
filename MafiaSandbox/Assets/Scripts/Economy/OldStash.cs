using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.World;

namespace MafiaSandbox.Economy
{
    /// <summary>The family's hidden cash under the loose floorboards of the ruined house. The first mission is to find it.</summary>
    public class OldStash : MonoBehaviour
    {
        public static OldStash I { get; private set; }
        public const int Amount = 1200;
        public bool Active { get; private set; }
        public Vector3 Pos { get; private set; }
        GameObject root; Light glow; bool built;

        void Awake() { I = this; }
        void Update()
        {
            if (Career.StashTaken) { if (root) Destroy(root); Active = false; return; }
            if (!built) TryBuild();
            if (glow) glow.intensity = 1.2f + Mathf.Sin(Time.time * 3f) * 0.5f;
        }
        void TryBuild()
        {
            var eco = EconomyManager.I; if (!eco || eco.homeShop == null || !eco.homeShop.info) return;
            var bi = eco.homeShop.info; var f = bi.footprint; if (f.size.sqrMagnitude < 1f) return;
            var anc = Stash.HomeAnchor(bi); Vector3 away = anc - bi.door; away.y = 0f; away = away.sqrMagnitude < 0.01f ? Vector3.back : away.normalized;
            var c = f.center; var p = new Vector3(c.x + away.x * (f.extents.x - 1.5f), anc.y, c.z + away.z * (f.extents.z - 1.5f));
            Pos = Stash.InHome(bi, p); Pos = new Vector3(Pos.x, anc.y, Pos.z);
            root = new GameObject("OldStash"); root.transform.position = Pos;
            GameObject Part(string n, Vector3 lp, Vector3 sc, Color col, float rotZ = 0f) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; Destroy(g.GetComponent<Collider>()); g.transform.SetParent(root.transform, false); g.transform.localPosition = lp; g.transform.localScale = sc; g.transform.localRotation = Quaternion.Euler(0, 0, rotZ); var m = new Material(Shader.Find("Standard")) { color = col }; g.GetComponent<Renderer>().material = m; return g; }
            Part("Board1", new Vector3(-0.35f, 0.05f, 0f), new Vector3(0.9f, 0.05f, 0.22f), new Color(0.3f, 0.2f, 0.1f));
            Part("Board2", new Vector3(0.1f, 0.12f, 0.12f), new Vector3(0.9f, 0.05f, 0.22f), new Color(0.26f, 0.17f, 0.09f), 14f);
            Part("Hole", new Vector3(0.1f, 0.01f, -0.1f), new Vector3(0.6f, 0.02f, 0.4f), Color.black);
            var tin = Part("Tin", new Vector3(0.1f, 0.08f, -0.1f), new Vector3(0.38f, 0.12f, 0.26f), new Color(0.75f, 0.6f, 0.15f));
            var em = tin.GetComponent<Renderer>().material; em.EnableKeyword("_EMISSION"); em.SetColor("_EmissionColor", new Color(0.8f, 0.55f, 0.1f));
            var lg = new GameObject("Glow"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0.1f, 0.6f, -0.1f);
            glow = lg.AddComponent<Light>(); glow.type = LightType.Point; glow.color = new Color(1f, 0.8f, 0.35f); glow.range = 4f; glow.intensity = 1.4f;
            built = true; Active = true;
        }
        public void Take()
        {
            if (!Active) return; var eco = EconomyManager.I; var gm = GameManager.I;
            eco.cash += Amount; Career.StashTaken = true; Active = false; if (root) Destroy(root);
            gm.Banner("FOUND IT", $"+${Amount} from the old stash", 3.5f); gm.Say($"Under the floorboards: an old tin with ${Amount}. Dad's emergency money.");
            Sfx.At("click", Pos);
        }
    }
}
