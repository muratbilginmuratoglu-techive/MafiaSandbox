using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Core;

namespace MafiaSandbox.World
{
    /// <summary>
    /// Street furniture (lamps, signs, traffic lights, fences, bins, hydrants, bus stops…) knocks over when a car hits it fast enough.
    /// The city's AABB collider under each prop is found at start; on impact it is disabled and the visual prop becomes a physics body.
    /// </summary>
    public class BreakableProps : MonoBehaviour
    {
        public static BreakableProps I { get; private set; }
        class Info { public Transform prop; public float minSpeed, slow, mass; public Collider[] cols; public bool broken; }
        readonly Dictionary<Collider, Info> map = new Dictionary<Collider, Info>();
        static readonly Dictionary<string, Vector3> Table = new Dictionary<string, Vector3>   // x = min speed (m/s), y = speed lost, z = mass
        {
            { "Sokak_Lambasi", new Vector3(6f, 0.30f, 90f) }, { "Trafik_Isigi", new Vector3(6f, 0.30f, 90f) }, { "Tabela_Dur", new Vector3(3.5f, 0.10f, 20f) }, { "Tabela_Park", new Vector3(3.5f, 0.10f, 20f) }, { "Tabela_Yon", new Vector3(3.5f, 0.10f, 20f) },
            { "Cit_Metal_2m", new Vector3(3.5f, 0.12f, 40f) }, { "Cit_Cali_2m", new Vector3(3f, 0.08f, 25f) }, { "Cit_Ahsap_2m", new Vector3(3f, 0.08f, 25f) }, { "Santiye_Citi", new Vector3(3f, 0.08f, 25f) },
            { "Cop_Kutusu", new Vector3(3f, 0.08f, 25f) }, { "Posta_Kutusu", new Vector3(4f, 0.12f, 45f) }, { "Yangin_Musluk", new Vector3(5f, 0.20f, 60f) }, { "Otobus_Duragi", new Vector3(7f, 0.35f, 150f) }, { "Telefon_Kulubesi", new Vector3(7f, 0.35f, 150f) },
            { "Saksi", new Vector3(2.5f, 0.04f, 12f) }, { "Saksi_Buyuk", new Vector3(3.5f, 0.08f, 30f) }, { "Palet", new Vector3(2.5f, 0.04f, 15f) }, { "Ates_Varili", new Vector3(3f, 0.08f, 35f) }, { "Market_Arabasi", new Vector3(2.5f, 0.03f, 12f) }, { "Bisiklet", new Vector3(2.5f, 0.03f, 12f) },
            { "Reklam_Panosu", new Vector3(5f, 0.15f, 60f) }, { "Lastik_Yigini", new Vector3(3f, 0.05f, 25f) }, { "Kasa_Yigini", new Vector3(3.5f, 0.08f, 40f) }, { "Piknik_Masasi", new Vector3(4.5f, 0.12f, 60f) }, { "Camasir_Ipi", new Vector3(3f, 0.04f, 15f) }, { "ATM", new Vector3(8f, 0.4f, 200f) }, { "Cop_Konteyneri", new Vector3(6f, 0.25f, 180f) },
        };
        public static bool IsBreakable(string name) => Table.ContainsKey(name);

        void Awake() { I = this; }
        void Start()
        {
            var world = GameObject.Find("World"); if (!world) return; var props = world.transform.Find("Props"); var colRoot = world.transform.Find("Colliders"); if (!props || !colRoot) return;
            var cols = new List<BoxCollider>(); foreach (Transform t in colRoot) { var b = t.GetComponent<BoxCollider>(); if (b && t.name == "Col") cols.Add(b); }
            foreach (Transform p in props)
            {
                if (!Table.TryGetValue(p.name, out var v)) continue;
                var rends = p.GetComponentsInChildren<Renderer>(); if (rends.Length == 0) continue; var bounds = rends[0].bounds; foreach (var r in rends) bounds.Encapsulate(r.bounds);
                var big = bounds; big.Expand(0.5f); var list = new List<Collider>();
                foreach (var c in cols) { var cb = c.bounds; if (cb.size.x > 6f || cb.size.z > 6f) continue; if (big.Contains(cb.center) && cb.Intersects(big)) list.Add(c); }
                if (list.Count == 0) continue;
                var info = new Info { prop = p, minSpeed = v.x, slow = v.y, mass = v.z, cols = list.ToArray() };
                foreach (var c in list) map[c] = info;
            }
        }

        /// <summary>Returns the fraction of speed the car loses (0 = not broken).</summary>
        public float TryBreak(Collider hit, float speed, Vector3 dir)
        {
            if (!hit || !map.TryGetValue(hit, out var info) || info.broken || speed < info.minSpeed) return 0f;
            info.broken = true; foreach (var c in info.cols) if (c) { c.enabled = false; map.Remove(c); }
            var go = info.prop.gameObject; foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
            var rends = go.GetComponentsInChildren<Renderer>(); var b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
            var box = go.AddComponent<BoxCollider>(); box.center = go.transform.InverseTransformPoint(b.center); Vector3 ls = go.transform.lossyScale; box.size = new Vector3(b.size.x / Mathf.Max(0.01f, ls.x), b.size.y / Mathf.Max(0.01f, ls.y), b.size.z / Mathf.Max(0.01f, ls.z));
            int ch = LayerMask.NameToLayer("Characters"); if (ch >= 0) go.layer = ch;   // cars ignore this layer: debris never stops them
            var rb = go.AddComponent<Rigidbody>(); rb.mass = info.mass; rb.angularDamping = 0.6f; rb.interpolation = RigidbodyInterpolation.None;
            Vector3 push = dir * Mathf.Clamp(speed * 0.9f, 3f, 12f) + Vector3.up * 1.5f;
            rb.AddForceAtPosition(push * info.mass * 0.5f, b.center + Vector3.up * b.extents.y * 0.8f, ForceMode.Impulse);
            Sfx.At("thud", b.center); StartCoroutine(Clean(go, 40f));
            return info.slow;
        }

        System.Collections.IEnumerator Clean(GameObject go, float after)
        {
            yield return new WaitForSeconds(after);
            if (!go) yield break; var rb = go.GetComponent<Rigidbody>(); var bc = go.GetComponent<BoxCollider>(); if (rb) Destroy(rb); if (bc) Destroy(bc);   // settle in place, stop costing physics
        }
    }
}
