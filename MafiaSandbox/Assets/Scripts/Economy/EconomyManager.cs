using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Npc;
using MafiaSandbox.Player;
using MafiaSandbox.World;

namespace MafiaSandbox.Economy
{
    /// <summary>
    /// Cash, backpack, shops and the things lying around in them. Buying, stealing (and who saw it), robbing registers,
    /// looting bodies, fencing stolen goods, confiscation on arrest. Item models come from <see cref="itemPrefabs"/>
    /// (the Esya_* props, assigned by CityBuilder).
    /// </summary>
    public class EconomyManager : MonoBehaviour
    {
        public static EconomyManager I { get; private set; }
        public GameObject[] itemPrefabs;
        public int cash = 250;
        public readonly List<InvItem> inv = new List<InvItem>();
        public readonly List<Shop> shops = new List<Shop>();
        public readonly List<Pickable> pickables = new List<Pickable>();
        public Shop homeShop;
        readonly Dictionary<string, GameObject> prefabByName = new Dictionary<string, GameObject>();
        Transform itemRoot;

        void Awake()
        {
            I = this;
            if (itemPrefabs != null) foreach (var p in itemPrefabs) if (p) prefabByName[p.name] = p;
            itemRoot = new GameObject("Items").transform;
        }

        void Start()
        {
            foreach (var bi in FindObjectsByType<BuildingInfo>(FindObjectsSortMode.None))
            {
                var shop = bi.gameObject.GetComponent<Shop>() ?? bi.gameObject.AddComponent<Shop>(); shop.Init(bi); shops.Add(shop);
                if (bi.kind == "home") homeShop = shop;
                // shelves
                var pool = shop.def.shelf; int k = 0;
                if (bi.shelves != null && pool != null && pool.Length > 0)
                    foreach (var sl in bi.shelves) { if (Random.value < 0.15f) continue; string id = pool[k++ % pool.Length]; var pk = SpawnPickable(id, sl, bi.transform.eulerAngles.y + Random.Range(-17f, 17f), shop, false); if (pk) shop.items.Add(pk); }
                // vault
                if (shop.def.vault != null && bi.hasSafe)
                    for (int i = 0; i < shop.def.vault.Length; i++) { var pk = SpawnPickable(shop.def.vault[i], bi.safePos + bi.transform.right * ((i - 1.5f) * 0.35f) + Vector3.up * 0.25f, bi.transform.eulerAngles.y, shop, true); if (pk) shop.items.Add(pk); }
            }
            StartCoroutine(LinkKeepers());
            var pc = GameManager.I ? GameManager.I.player.GetComponent<PlayerCombat>() : null;
            if (pc) { for (int i = 0; i < pc.owned.Length; i++) pc.owned[i] = WeaponDef.Order[i] == WeaponKind.Pistol || WeaponDef.Order[i] == WeaponKind.Knife; pc.Equip(WeaponKind.Pistol); }
        }

        IEnumerator LinkKeepers()
        {
            yield return new WaitForSeconds(0.5f);
            foreach (var kb in FindObjectsByType<KeeperBrain>(FindObjectsSortMode.None))
            {
                var shop = ShopFor(kb.building); if (!shop) continue;
                if (kb.role == "owner") shop.owner = kb; else if (!shop.keeper) shop.keeper = kb; else shop.keeper2 = kb;
                kb.shop = shop;
            }
        }

        void Update()
        {
            float now = Time.time;
            foreach (var pk in pickables) if (pk && pk.taken && pk.respawns && pk.respawnAt > 0f && now > pk.respawnAt) pk.Respawn();
        }

        public Shop ShopFor(BuildingInfo bi) { if (!bi) return null; foreach (var s in shops) if (s.info == bi) return s; return null; }
        public Shop ShopAt(Vector3 p) { foreach (var s in shops) if (s.Contains(p)) return s; return null; }

        public Pickable SpawnPickable(string id, Vector3 pos, float yaw, Shop shop, bool vault, bool respawns = true)
        {
            var def = Items.Get(id); if (def == null) return null;
            if (!prefabByName.TryGetValue(def.prop, out var prefab)) { Debug.LogWarning("[Economy] no prefab for " + def.prop); return null; }
            var go = Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), itemRoot); go.name = "Item " + id;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            var pk = go.AddComponent<Pickable>(); pk.Setup(id, shop, vault); pk.respawns = respawns; pickables.Add(pk); return pk;
        }

        // ------------------------------------------------------------------ backpack
        public void AddItem(string id, int qty = 1, bool stolen = false)
        {
            var it = inv.Find(i => i.id == id && i.stolen == stolen);
            if (it != null) it.qty += qty; else inv.Add(new InvItem { id = id, qty = qty, stolen = stolen });
        }
        void Consume(InvItem it) { it.qty--; if (it.qty <= 0) inv.Remove(it); }
        public int Count(string id) { int n = 0; foreach (var i in inv) if (i.id == id) n += i.qty; return n; }

        public void UseItem(InvItem it)
        {
            var def = Items.Get(it.id); var gm = GameManager.I; var body = gm ? gm.player.GetComponent<CharacterBody>() : null; var v = Vitals.I;
            if (def.kind == "food")
            {
                if (v && v.hunger >= 100f && body && body.hp >= body.maxHp) { gm.Say("You are full and healthy already"); return; }
                if (v) { v.hunger = Mathf.Min(100f, v.hunger + def.heal * 2.2f); v.stamina = Mathf.Min(100f, v.stamina + def.heal); }
                if (body) body.Treat(Mathf.Max(0, Mathf.RoundToInt(def.heal * 0.06f)));
                Consume(it); gm.Say($"Ate {def.name} (food +{Mathf.RoundToInt(def.heal * 2.2f)})"); Sfx.At("click", gm.player.transform.position);
            }
            else if (def.kind == "med")
            {
                if (body && body.hp >= body.maxHp) { gm.Say("Your health is already full"); return; }
                if (body) body.Treat(Mathf.Max(1, Mathf.RoundToInt(def.heal * 0.1f)));
                Consume(it); gm.Say($"Used {def.name} (health +{def.heal}%)"); Sfx.At("click", gm.player.transform.position);
            }
            else gm.Say("You can't use this; sell it at the pawn shop");
        }

        public void DropItem(InvItem it)
        {
            var gm = GameManager.I; if (!gm) return; var t = gm.player.transform;
            Consume(it);
            var pk = SpawnPickable(it.id, t.position + t.forward * 0.8f + Vector3.up * 0.05f, t.eulerAngles.y, null, false, false);
            if (pk) pk.cashAmount = 0;
        }

        // ------------------------------------------------------------------ taking things
        public bool FreeTake(Pickable pk)
        {
            var b = pk.shop; return !b || b.kind == "house" || b.kind == "home" || b.kind == "police" || b.kind == "bank" || pk.vault || !b.HasLiveKeeper;
        }

        public void TakeItem(Pickable pk, bool steal)
        {
            var gm = GameManager.I; var b = pk.shop; Vector3 pp = gm.player.transform.position;
            if (pk.id == "Para") { cash += pk.cashAmount; gm.Say($"+${pk.cashAmount} cash"); }
            else
            {
                bool free = FreeTake(pk);
                if (!steal && !free)
                {
                    int price = b.BuyPrice(pk.id);
                    if (cash < price) { gm.Say($"Not enough cash: ${price} (you could steal it with G…)"); return; }
                    cash -= price; AddItem(pk.id, 1, false); gm.Say($"Bought {pk.def.name} (${price})");
                }
                else { bool stolen = b && b.kind != "home"; AddItem(pk.id, 1, stolen); gm.Say($"Took {pk.def.name}{(stolen ? " (stolen)" : "")}"); }
            }
            Sfx.At("click", pp); pk.Take();
            // witnesses: the keeper / owner, or an officer who happens to look
            bool stolenNow = b && b.kind != "home" && (pk.id == "Para" || steal || !Items.IsRetail(b.kind));
            if (b && stolenNow)
            {
                KeeperBrain w = null;
                foreach (var c in new[] { b.keeper, b.keeper2, b.owner }) if (c && !c.Dead && (c.State == "keeper" || c.State == "handsup") && c.SeesPlayer(16f, 300f)) { w = c; break; }
                if (w) { gm.Say($"{w.displayName}: HEY! Put that back, thief!"); WitnessCrime(b, pp, pk.vault ? 3 : 1); w.Startled(); }
                else if (pk.vault || b.kind == "bank") { gm.Say("The vault alarm went off!"); WitnessCrime(b, pp, 3); }
                else CopSaw(pp, 20f, "saw the theft");
            }
        }

        /// <summary>An officer in line of sight of the player reacts to a petty crime.</summary>
        public bool CopSaw(Vector3 pp, float range, string what)
        {
            foreach (var cop in FindObjectsByType<PoliceBrain>(FindObjectsSortMode.None))
                if (!cop.Dead && cop.SeesPlayer(range, 150f)) { GameManager.I.Say($"{cop.displayName} {what}!"); WantedSystem.I?.Raise(1, what); WantedSystem.I?.Seen(pp); cop.Chase(); return true; }
            return false;
        }

        /// <summary>The shop calls the police: wanted level, and the nearest idle officers come to look even if nobody heard anything.</summary>
        public void WitnessCrime(Shop b, Vector3 pos, int level)
        {
            if (!WantedSystem.I) return;
            WantedSystem.I.Raise(Mathf.Min(level, 3), level >= 2 ? "robbery" : "theft"); WantedSystem.I.Seen(pos); b.alarmAt = Time.time;
            var cops = new List<PoliceBrain>(); foreach (var c in FindObjectsByType<PoliceBrain>(FindObjectsSortMode.None)) if (!c.Dead && c.IsIdle) cops.Add(c);
            cops.Sort((a, c2) => Vector3.Distance(a.transform.position, pos).CompareTo(Vector3.Distance(c2.transform.position, pos)));
            int n = Mathf.Min(cops.Count, level >= 2 ? 3 : 1);
            for (int i = 0; i < n; i++) StartCoroutine(DispatchLater(cops[i], b.info.door, pos, 2.5f + Random.value * 3f));
            GameManager.I.Say(level >= 2 ? "Silent alarm! Police are on their way." : "The shopkeeper called the police.");
        }
        IEnumerator DispatchLater(PoliceBrain c, Vector3 door, Vector3 threat, float delay) { yield return new WaitForSeconds(delay); if (c && !c.Dead && c.IsIdle) c.Dispatch(door, threat); }

        /// <summary>Called by a keeper with hands up for long enough: hand over the register.</summary>
        public void Rob(Shop b, KeeperBrain by)
        {
            var gm = GameManager.I; int amt = b.register; b.register = 0; b.registerEmptiedAt = Time.time;
            if (amt > 0) { cash += amt; gm.Say($"Robbery: you took ${amt} from the {b.label} register"); Sfx.At("click", gm.player.transform.position); }
            else gm.Say($"{by.displayName}: The register is empty, they just emptied it!");
            WitnessCrime(b, gm.player.transform.position, b.kind == "bank" ? 3 : 2);
        }

        public void LootBody(CharacterBody c)
        {
            var gm = GameManager.I; var brain = c.GetComponent<NpcBrain>(); c.Looted = true; var got = new List<string>(); int money = 0;
            void Give(string id, float p, int qty = 1) { if (Random.value < p) { AddItem(id, qty, id == "Paket"); got.Add(Items.Get(id).name + (qty > 1 ? " x" + qty : "")); } }
            if (brain is PoliceBrain) { money = 20 + Random.Range(0, 60); Give("Mermi", 0.8f); Give("Telsiz", 0.5f); Give("Kelepce", 0.6f); }
            else if (brain is GangBrain) { money = 60 + Random.Range(0, 240); Give("Paket", 0.7f); Give("Sigara", 0.6f); Give("Telefon", 0.3f); }
            else if (brain is KeeperBrain) { money = 40 + Random.Range(0, 160); Give("Anahtar", 0.5f); Give("Telefon", 0.5f); }
            else { money = Random.value < 0.75f ? 10 + Random.Range(0, 110) : 0; Give("Telefon", 0.45f); Give("Saat", 0.15f); Give("Anahtar", 0.3f); Give("Sigara", 0.3f); }
            if (money > 0) { cash += money; got.Insert(0, "$" + money); }
            gm.Say(got.Count > 0 ? "Found on the body: " + string.Join(", ", got) : "Nothing on the body");
            CopSaw(gm.player.transform.position, 22f, "saw you loot the body");
        }

        // ------------------------------------------------------------------ shop counter
        public bool Buy(Shop b, string id)
        {
            if (!b.stock.TryGetValue(id, out var st) || st.n <= 0) return false; int price = b.BuyPrice(id); if (cash < price) return false;
            cash -= price; st.n--; AddItem(id); Sfx.At("click", b.info.counter); return true;
        }
        public bool Sell(Shop b, InvItem it)
        {
            var def = Items.Get(it.id); if (!b.Buys(def.kind) || (it.stolen && !b.def.fence)) return false;
            int price = b.SellPrice(it.id, it.stolen); cash += price; b.sold[it.id] = (b.sold.TryGetValue(it.id, out var s) ? s : 0f) + 1f; Consume(it); Sfx.At("click", b.info.counter); return true;
        }
        public bool BuyWeapon(Shop b, WeaponKind k)
        {
            var pc = GameManager.I.player.GetComponent<PlayerCombat>(); int idx = System.Array.IndexOf(WeaponDef.Order, k); int price = b.WeaponPrice(k);
            if (idx < 0 || pc.owned[idx] || cash < price || price <= 0) return false;
            cash -= price; pc.owned[idx] = true; GameManager.I.Say($"Bought {WeaponDef.All[k].name} (${price})"); Sfx.At("click", b.info.counter); return true;
        }

        /// <summary>Arrest: stolen goods are confiscated, and there's a fine.</summary>
        public void OnArrested()
        {
            int before = inv.Count; inv.RemoveAll(i => i.stolen); if (inv.Count < before) GameManager.I.Say("Your stolen items were confiscated");
            int w = WantedSystem.I ? Mathf.Max(1, WantedSystem.I.Level) : 1; int fine = Mathf.Min(cash, 100 * w);
            if (fine > 0) { cash -= fine; GameManager.I.Say($"Fine: ${fine}"); }
        }
    }
}
