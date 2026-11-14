using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Player;

using MafiaSandbox.Factions;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// IMGUI windows: backpack (I) and the shop counter (F at a counter): buy list with stock and live prices, weapons
    /// at the gun shop, sell list for what the shop buys (the pawn shop fences stolen goods). Opening a window frees the
    /// cursor and locks movement; Esc / Tab closes.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        public static GameUI I { get; private set; }
        public static bool Blocking => (I && (I.invOpen || I.shop != null || I.mapOpen || I.loot != null)) || (DebugPanel.I && DebugPanel.I.Open);
        bool invOpen, mapOpen; Shop shop; CharacterBody loot; Vector2 scrollA, scrollB;
        GUIStyle win, title, row, dim, btn, price, head;
        PlayerController pc;

        void Awake() { I = this; }

        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player) return; if (!pc) pc = gm.player;
            bool dead = gm.player.GetComponent<CharacterBody>()?.Dead ?? false;
            if (Input.GetKeyDown(KeyCode.I)) { if (shop != null) CloseAll(); else if (!dead) { invOpen = !invOpen; mapOpen = false; } }
            if (Input.GetKeyDown(KeyCode.M)) { if (mapOpen) CloseAll(); else if (!dead) { mapOpen = true; invOpen = false; shop = null; } }
            if (Input.GetKeyDown(KeyCode.Escape) && Blocking) CloseAll();
            if (dead && Blocking) CloseAll();
            if (shop != null && Vector3.Distance(gm.player.transform.position, shop.info.counter) > 4f) CloseAll();
            if (loot != null && (!loot || Vector3.Distance(gm.player.transform.position, loot.transform.position) > 4f)) CloseAll();
            ApplyLock();
        }

        void ApplyLock()
        {
            if (!pc) return;
            if (Blocking) { pc.InputLocked = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; wasBlocking = true; }
            else if (wasBlocking) { wasBlocking = false; var vi = pc.GetComponent<Vehicles.VehicleInteraction>(); if (!(vi && vi.Current) && !GameManager.I.Arrested) pc.InputLocked = false; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }
        bool wasBlocking;

        public void OpenShop(Shop s) { shop = s; invOpen = false; mapOpen = false; loot = null; }
        public void OpenLoot(CharacterBody b) { loot = b; shop = null; invOpen = false; mapOpen = false; }
        public void CloseAll() { invOpen = false; mapOpen = false; shop = null; loot = null; }
        public bool MapOpen => mapOpen;

        void Styles()
        {
            if (win != null) return;
            win = UiSkin.PanelSolid; title = UiSkin.Title; head = UiSkin.H2; row = UiSkin.Label; dim = UiSkin.LabelDim;
            price = new GUIStyle(UiSkin.Label) { alignment = TextAnchor.MiddleRight }; price.normal.textColor = UiSkin.Green;
            btn = UiSkin.Button;
        }
        static Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        void OnGUI()
        {
            var eco = EconomyManager.I; var gm = GameManager.I; if (!eco || !gm) return; Styles();
            if (invOpen) DrawInventory(eco, gm);
            else if (shop != null) DrawShop(eco, gm);
            else if (loot != null) DrawLoot(eco, gm);
        }

        void DrawLoot(EconomyManager eco, GameManager gm)
        {
            var l = eco.LootOf(loot);
            float W = Screen.width, H = Screen.height; var r = new Rect(W / 2f - 230, H / 2f - 190, 460, 380);
            GUI.Box(r, GUIContent.none, win);
            GUILayout.BeginArea(new Rect(r.x + 14, r.y + 10, r.width - 28, r.height - 20));
            GUILayout.BeginHorizontal(); GUILayout.Label("Search: " + l.who, title); GUILayout.FlexibleSpace(); GUILayout.Label($"${eco.cash}", new GUIStyle(title) { normal = { textColor = UiSkin.Green } }); GUILayout.EndHorizontal();
            GUILayout.Label("Esc closes · take what you want", dim);
            scrollA = GUILayout.BeginScrollView(scrollA);
            InvItem take = null; bool takeCash = false;
            if (l.cash > 0)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(28)); GUILayout.Label("Cash", row, GUILayout.Width(240)); GUILayout.Label($"${l.cash}", price, GUILayout.Width(70));
                if (GUILayout.Button("Take", btn, GUILayout.Width(64))) takeCash = true; GUILayout.EndHorizontal();
            }
            foreach (var it in l.items)
            {
                var def = Items.Get(it.id);
                GUILayout.BeginHorizontal(GUILayout.Height(28));
                GUILayout.Label($"{def.name}{(it.qty > 1 ? "  x" + it.qty : "")}{(it.stolen ? "   (hot)" : "")}", it.stolen ? dim : row, GUILayout.Width(240)); GUILayout.Label($"~${def.value}", price, GUILayout.Width(70));
                if (GUILayout.Button("Take", btn, GUILayout.Width(64))) take = it;
                GUILayout.EndHorizontal();
            }
            if (l.cash == 0 && l.items.Count == 0) GUILayout.Label("Nothing left on the body.", dim);
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace();
            if ((l.cash > 0 || l.items.Count > 0) && GUILayout.Button("Take all", btn, GUILayout.Width(90))) { takeCash = l.cash > 0; foreach (var it in new List<InvItem>(l.items)) eco.TakeLoot(loot, it); }
            if (GUILayout.Button("Close", btn, GUILayout.Width(70))) CloseAll();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            if (takeCash) eco.TakeLoot(loot, null); if (take != null) eco.TakeLoot(loot, take);
        }

        void DrawInventory(EconomyManager eco, GameManager gm)
        {
            float W = Screen.width, H = Screen.height; var r = new Rect(W / 2f - 300, H / 2f - 250, 600, 500);
            GUI.Box(r, GUIContent.none, win);
            GUILayout.BeginArea(new Rect(r.x + 14, r.y + 10, r.width - 28, r.height - 20));
            GUILayout.BeginHorizontal(); GUILayout.Label("Backpack", title); GUILayout.FlexibleSpace(); GUILayout.Label($"${eco.cash}", new GUIStyle(title) { normal = { textColor = new Color(0.56f, 0.9f, 0.6f) } }); GUILayout.EndHorizontal();
            GUILayout.Label("I / Esc closes · food restores hunger & stamina · stolen goods only sell at the pawn shop", dim);
            scrollA = GUILayout.BeginScrollView(scrollA);
            if (eco.inv.Count == 0) GUILayout.Label("Your backpack is empty. Buy from shops, take what's lying around (F) or search bodies.", dim);
            InvItem use = null, drop = null;
            foreach (var it in eco.inv)
            {
                var def = Items.Get(it.id);
                GUILayout.BeginHorizontal(GUILayout.Height(28));
                GUILayout.Label($"{def.name}{(it.qty > 1 ? "  x" + it.qty : "")}{(it.stolen ? "   (stolen)" : "")}", it.stolen ? dim : row, GUILayout.Width(300));
                GUILayout.Label($"~${def.value}", price, GUILayout.Width(70));
                if (def.kind == "food" || def.kind == "med") { if (GUILayout.Button(def.kind == "food" ? "Eat" : "Use", btn, GUILayout.Width(52))) use = it; }
                else GUILayout.Label("", GUILayout.Width(52));
                if (GUILayout.Button("Drop", btn, GUILayout.Width(52))) drop = it;
                GUILayout.EndHorizontal();
            }
            if (use != null) eco.UseItem(use); if (drop != null) eco.DropItem(drop);
            GUILayout.EndScrollView();
            var pc = gm.player.GetComponent<PlayerCombat>();
            if (pc) { var owned = new List<string>(); for (int i = 0; i < WeaponDef.Order.Length; i++) if (pc.owned[i]) owned.Add(WeaponDef.All[WeaponDef.Order[i]].name); GUILayout.Label("Weapons: " + string.Join(", ", owned), dim); }
            if (Vitals.I) GUILayout.Label($"Stamina {Vitals.I.stamina:0}%   ·   Food {Vitals.I.hunger:0}%", dim);
            var fm = FactionManager.I;
            if (fm)
            {
                GUILayout.Space(4); GUILayout.Label("Reputation", title);
                GUILayout.BeginHorizontal();
                foreach (var f in FactionDefs.All)
                {
                    float v = fm.Rep(f); var st = new GUIStyle(dim) { alignment = TextAnchor.MiddleCenter }; st.normal.textColor = Color.Lerp(f.color, Color.white, 0.4f);
                    GUILayout.Label($"{f.name}\n{FactionManager.Standing(v)} ({v:+0;-0;0})", st, GUILayout.Width((r.width - 28) / FactionDefs.All.Count));
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        void DrawShop(EconomyManager eco, GameManager gm)
        {
            float W = Screen.width, H = Screen.height; var r = new Rect(W / 2f - 380, H / 2f - 240, 760, 480);
            GUI.Box(r, GUIContent.none, win);
            GUILayout.BeginArea(new Rect(r.x + 14, r.y + 10, r.width - 28, r.height - 20));
            GUILayout.BeginHorizontal(); GUILayout.Label(shop.label, title); GUILayout.FlexibleSpace(); GUILayout.Label($"${eco.cash}", new GUIStyle(title) { normal = { textColor = new Color(0.56f, 0.9f, 0.6f) } }); GUILayout.EndHorizontal();
            GUILayout.Label($"Esc closes · prices move with stock and time of day{(Shop.IsNight ? " (night surcharge +15%)" : "")}{(shop.def.fence ? " · fences stolen goods at 45%" : "")}", dim);
            GUILayout.BeginHorizontal();
            // buy column
            GUILayout.BeginVertical(GUILayout.Width(360));
            GUILayout.Label("Buy", head);
            scrollA = GUILayout.BeginScrollView(scrollA, GUILayout.Height(340));
            bool any = false; string buyId = null; WeaponKind? buyW = null;
            foreach (var id in shop.def.stock)
            {
                any = true; var def = Items.Get(id); var st = shop.stock[id]; int p = shop.BuyPrice(id);
                GUILayout.BeginHorizontal(GUILayout.Height(28));
                GUILayout.Label($"{def.name}", row, GUILayout.Width(170)); GUILayout.Label(st.n > 0 ? $"stock {st.n}" : "sold out", dim, GUILayout.Width(70)); GUILayout.Label($"${p}", price, GUILayout.Width(60));
                GUI.enabled = st.n > 0 && eco.cash >= p; if (GUILayout.Button("Buy", btn, GUILayout.Width(50))) buyId = id; GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (shop.def.weapons != null)
            {
                var pc = gm.player.GetComponent<PlayerCombat>();
                foreach (var kv in shop.def.weapons)
                {
                    any = true; int idx = System.Array.IndexOf(WeaponDef.Order, kv.Key); bool own = pc && pc.owned[idx]; int p = shop.WeaponPrice(kv.Key);
                    GUILayout.BeginHorizontal(GUILayout.Height(28));
                    GUILayout.Label(WeaponDef.All[kv.Key].name, row, GUILayout.Width(170)); GUILayout.Label(own ? "owned" : "", dim, GUILayout.Width(70)); GUILayout.Label(own ? "" : $"${p}", price, GUILayout.Width(60));
                    GUI.enabled = !own && eco.cash >= p; if (GUILayout.Button("Buy", btn, GUILayout.Width(50))) buyW = kv.Key; GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
            }
            if (!any) GUILayout.Label("Nothing for sale over the counter.", dim);
            GUILayout.EndScrollView(); GUILayout.EndVertical();
            if (buyId != null) eco.Buy(shop, buyId); if (buyW.HasValue) eco.BuyWeapon(shop, buyW.Value);
            // sell column
            GUILayout.BeginVertical(GUILayout.Width(340));
            GUILayout.Label("Sell", head);
            scrollB = GUILayout.BeginScrollView(scrollB, GUILayout.Height(340));
            InvItem sell = null; bool anyS = false;
            foreach (var it in eco.inv)
            {
                var def = Items.Get(it.id); if (!shop.Buys(def.kind) || (it.stolen && !shop.def.fence)) continue; anyS = true;
                int p = shop.SellPrice(it.id, it.stolen);
                GUILayout.BeginHorizontal(GUILayout.Height(28));
                GUILayout.Label($"{def.name}{(it.qty > 1 ? " x" + it.qty : "")}{(it.stolen ? " (stolen)" : "")}", it.stolen ? dim : row, GUILayout.Width(200)); GUILayout.Label($"${p}", price, GUILayout.Width(60));
                if (GUILayout.Button("Sell", btn, GUILayout.Width(50))) sell = it;
                GUILayout.EndHorizontal();
            }
            if (!anyS) GUILayout.Label(shop.def.buysKinds == null || shop.def.buysKinds.Length == 0 ? "This shop doesn't buy anything." : "Nothing in your backpack they'd buy" + (shop.def.fence ? "." : " (stolen goods go to the pawn shop)."), dim);
            GUILayout.EndScrollView(); GUILayout.EndVertical();
            if (sell != null) eco.Sell(shop, sell);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
