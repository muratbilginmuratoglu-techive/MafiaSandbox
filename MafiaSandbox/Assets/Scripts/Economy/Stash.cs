using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.World;

namespace MafiaSandbox.Economy
{
    /// <summary>A lockable chest in your home. Items put in it survive arrests and deaths and are saved between launches.</summary>
    public class Stash : MonoBehaviour
    {
        public static Stash I { get; private set; }
        public readonly List<InvItem> items = new List<InvItem>();
        public const int Capacity = 400;   // units
        public Vector3 Pos { get; private set; }
        const string Key = "ms_stash_v1"; bool built;

        void Awake() { I = this; Load(); }
        public int Units { get { int n = 0; foreach (var i in items) n += i.qty; return n; } }
        public int Worth { get { int n = 0; foreach (var i in items) n += i.qty * Items.Get(i.id).value; return n; } }

        /// <summary>The home building has no counter (it is 0,0,0): use the room centre, at floor height, instead.</summary>
        public static Vector3 HomeAnchor(BuildingInfo bi)
        {
            if (bi.counter.sqrMagnitude > 1f) return bi.counter;
            var c = bi.footprint.center; return new Vector3(c.x, bi.door.y, c.z);
        }
        /// <summary>Keeps a point inside the building footprint (0.7 m from the walls).</summary>
        public static Vector3 InHome(BuildingInfo bi, Vector3 p)
        {
            var f = bi.footprint; if (f.size.sqrMagnitude < 1f) return p;
            return new Vector3(Mathf.Clamp(p.x, f.min.x + 0.7f, f.max.x - 0.7f), p.y, Mathf.Clamp(p.z, f.min.z + 0.7f, f.max.z - 0.7f));
        }
        void Update()
        {
            if (built) return; var eco = EconomyManager.I; var gm = GameManager.I; if (!eco || !gm || eco.homeShop == null) return;
            var bi = eco.homeShop.info; var anc = HomeAnchor(bi); Vector3 toDoor = bi.door - anc; toDoor.y = 0f; toDoor = toDoor.sqrMagnitude < 0.01f ? Vector3.forward : toDoor.normalized;
            Pos = InHome(bi, anc + toDoor * 1.1f); Pos = new Vector3(Pos.x, anc.y, Pos.z);
            Build(Pos, Mathf.Atan2(toDoor.x, toDoor.z) * Mathf.Rad2Deg); built = true;
        }

        static Material M(Color c) { var m = new Material(Shader.Find("Standard")) { color = c }; m.SetFloat("_Glossiness", 0.15f); return m; }
        void Build(Vector3 pos, float yaw)
        {
            var root = new GameObject("HomeStash").transform; root.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            GameObject Part(string n, Vector3 lp, Vector3 sc, Color c) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; Destroy(g.GetComponent<Collider>()); g.transform.SetParent(root, false); g.transform.localPosition = lp; g.transform.localScale = sc; g.GetComponent<Renderer>().sharedMaterial = M(c); return g; }
            var wood = new Color(0.36f, 0.22f, 0.11f); var dark = new Color(0.22f, 0.13f, 0.07f); var iron = new Color(0.16f, 0.16f, 0.18f);
            Part("Body", new Vector3(0f, 0.28f, 0f), new Vector3(0.95f, 0.56f, 0.55f), wood);
            Part("Lid", new Vector3(0f, 0.62f, 0f), new Vector3(1.0f, 0.14f, 0.6f), dark);
            Part("BandL", new Vector3(-0.3f, 0.34f, 0f), new Vector3(0.07f, 0.74f, 0.6f), iron); Part("BandR", new Vector3(0.3f, 0.34f, 0f), new Vector3(0.07f, 0.74f, 0.6f), iron);
            Part("Lock", new Vector3(0f, 0.5f, 0.29f), new Vector3(0.12f, 0.16f, 0.04f), new Color(0.85f, 0.7f, 0.25f));
        }

        // ---- transfers
        public void Put(InvItem it, int n)
        {
            n = Mathf.Min(n, it.qty); if (n <= 0 || Units + n > Capacity) { GameManager.I?.Say(n > 0 ? "The stash is full." : ""); return; }
            var eco = EconomyManager.I; var s = items.Find(x => x.id == it.id && x.stolen == it.stolen); if (s != null) s.qty += n; else items.Add(new InvItem { id = it.id, qty = n, stolen = it.stolen });
            it.qty -= n; if (it.qty <= 0) eco.inv.Remove(it); Save();
        }
        public void Take(InvItem it, int n)
        {
            n = Mathf.Min(n, it.qty); if (n <= 0) return; EconomyManager.I.AddItem(it.id, n, it.stolen); it.qty -= n; if (it.qty <= 0) items.Remove(it); Save();
        }
        public void SavePublic() => Save();
        /// <summary>Staff put things straight into the cupboard (false if it is full).</summary>
        public bool AddDirect(string id, int n)
        {
            if (n <= 0) return true; if (Units + n > Capacity) return false;
            var s = items.Find(x => x.id == id && !x.stolen); if (s != null) s.qty += n; else items.Add(new InvItem { id = id, qty = n, stolen = false }); Save(); return true;
        }
        /// <summary>Takes units out of the cupboard (dealers selling from the house). Returns how many came out.</summary>
        public int TakeDirect(string id, int n)
        {
            int got = 0; for (int i = items.Count - 1; i >= 0 && got < n; i--) { var it = items[i]; if (it.id != id) continue; int k = Mathf.Min(it.qty, n - got); it.qty -= k; got += k; if (it.qty <= 0) items.RemoveAt(i); }
            if (got > 0) Save(); return got;
        }
        public int CountOf(string id) { int n = 0; foreach (var i in items) if (i.id == id) n += i.qty; return n; }
        void Save() { var sb = new StringBuilder(); foreach (var i in items) sb.Append(i.id).Append(':').Append(i.qty).Append(':').Append(i.stolen ? 1 : 0).Append(';'); PlayerPrefs.SetString(Key, sb.ToString()); PlayerPrefs.Save(); }
        void Load()
        {
            items.Clear(); foreach (var e in PlayerPrefs.GetString(Key, "").Split(';')) { var p = e.Split(':'); if (p.Length == 3 && Items.Get(p[0]) != null && int.TryParse(p[1], out int q) && q > 0) items.Add(new InvItem { id = p[0], qty = q, stolen = p[2] == "1" }); }
        }
    }
}
