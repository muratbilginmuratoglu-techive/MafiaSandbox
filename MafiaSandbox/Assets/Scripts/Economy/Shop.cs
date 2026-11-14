using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Npc;
using MafiaSandbox.World;

namespace MafiaSandbox.Economy
{
    /// <summary>
    /// Runtime state of one building's economy: stock (restocks 1 piece / 90 s), what the player recently sold here
    /// (saturation, forgets 1 piece / 2 min), the register, the silent alarm, and who keeps the shop.
    /// Price model:  buy = value × markup × scarcity × night ;  sell = value × buyRate × (stolen ? 0.45 : 1) × saturation × heat
    ///   scarcity = 1 + 0.6 × (1 − stock/max) ; saturation = 1 / (1 + 0.25 × soldHere) ; heat: stolen goods −15 % per wanted star ; night (22–06) +15 % on purchases.
    /// </summary>
    public class Shop : MonoBehaviour
    {
        public BuildingInfo info; public ShopDef def; public string kind => info.kind; public string label => info.label;
        public class Stock { public int n, max; }
        public readonly Dictionary<string, Stock> stock = new Dictionary<string, Stock>();
        public readonly Dictionary<string, float> sold = new Dictionary<string, float>();
        public int register; public float registerEmptiedAt = -999f, alarmAt = -999f, restockT;
        public KeeperBrain keeper, keeper2, owner;
        public readonly List<Pickable> items = new List<Pickable>();

        public void Init(BuildingInfo bi)
        {
            info = bi; def = Items.ShopFor(bi.kind) ?? Items.ShopFor("house");
            register = Mathf.RoundToInt(Mathf.Lerp(def.registerMin, def.registerMax, Random.value));
            foreach (var id in def.stock) { var it = Items.Get(id); int mx = it.value > 500 ? 2 : it.value > 30 ? 4 : 8; stock[id] = new Stock { n = 1 + Random.Range(0, mx), max = mx }; }
        }

        void Update()
        {
            if (def == null) { if (info) Init(info); else return; }   // a script reload in Play mode drops the non-serialized state
            float dt = Time.deltaTime;
            restockT += dt;
            if (restockT > 90f) { restockT = 0f; foreach (var s in stock.Values) if (s.n < s.max) s.n++; }
            var keys = new List<string>(sold.Keys); foreach (var k in keys) sold[k] = Mathf.Max(0f, sold[k] - dt / 120f);
            // the register refills slowly after a robbery (customers keep coming)
            if (register < def.registerMin && Time.time - registerEmptiedAt > 30f && Random.value < dt * 0.05f) register += Mathf.Max(1, def.registerMin / 10);
        }

        public static bool IsNight => DayNightCycle.I && DayNightCycle.I.IsNight;
        public float Scarcity(string id) => stock.TryGetValue(id, out var s) && s.max > 0 ? 1f + 0.6f * (1f - s.n / (float)s.max) : 1f;
        public int BuyPrice(string id)
        {
            var it = Items.Get(id); float mk = Items.Markup.TryGetValue(kind, out var m) ? m : 1f;
            return Mathf.Max(1, Mathf.RoundToInt(it.value * mk * Scarcity(id) * (IsNight ? 1.15f : 1f)));
        }
        public int SellPrice(string id, bool stolen)
        {
            var it = Items.Get(id); float br = Items.BuyRate.TryGetValue(kind, out var r) ? r : 0.4f;
            float sat = 1f / (1f + 0.25f * (sold.TryGetValue(id, out var sd) ? sd : 0f));
            int wanted = WantedSystem.I ? WantedSystem.I.Level : 0; float heat = stolen ? Mathf.Max(0.3f, 1f - 0.15f * wanted) : 1f;
            return Mathf.Max(1, Mathf.RoundToInt(it.value * br * (stolen ? 0.45f : 1f) * sat * heat));
        }
        public int WeaponPrice(WeaponKind k) => def.weapons != null && def.weapons.TryGetValue(k, out var p) ? Mathf.RoundToInt(p * (IsNight ? 1.15f : 1f)) : 0;
        public bool Buys(string kind) { if (def.buysKinds == null) return false; foreach (var k in def.buysKinds) if (k == kind) return true; return false; }
        public bool HasLiveKeeper => (keeper && !keeper.Dead) || (keeper2 && !keeper2.Dead);
        public bool Contains(Vector3 p) => info.footprint.Contains(new Vector3(p.x, Mathf.Clamp(p.y, info.footprint.min.y, info.footprint.max.y), p.z));
    }
}
