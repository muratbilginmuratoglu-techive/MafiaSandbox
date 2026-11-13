using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Player;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// IMGUI windows: backpack (Tab / I) and the shop counter (F at a counter): buy list with stock and live prices, weapons
    /// at the gun shop, sell list for what the shop buys (the pawn shop fences stolen goods). Opening a window frees the
    /// cursor and locks movement; Esc / Tab closes.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        public static GameUI I { get; private set; }
        public static bool Blocking => I && (I.invOpen || I.shop != null || I.mapOpen);
        bool invOpen, mapOpen; Shop shop; Vector2 scrollA, scrollB;
        GUIStyle win, title, row, dim, btn, price, head;
        PlayerController pc;

        void Awake() { I = this; }

        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player) return; if (!pc) pc = gm.player;
            bool dead = gm.player.GetComponent<CharacterBody>()?.Dead ?? false;
            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.I)) { if (shop != null) CloseAll(); else if (!dead) { invOpen = !invOpen; mapOpen = false; } }
            if (Input.GetKeyDown(KeyCode.M)) { if (mapOpen) CloseAll(); else if (!dead) { mapOpen = true; invOpen = false; shop = null; } }
            if (Input.GetKeyDown(KeyCode.Escape) && Blocking) CloseAll();
            if (dead && Blocking) CloseAll();
            if (shop != null && Vector3.Distance(gm.player.transform.position, shop.info.counter) > 4f) CloseAll();
            ApplyLock();
        }

        void ApplyLock()
        {
            if (!pc) return;
            if (Blocking) { pc.InputLocked = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; wasBlocking = true; }
            else if (wasBlocking) { wasBlocking = false; var vi = pc.GetComponent<Vehicles.VehicleInteraction>(); if (!(vi && vi.Current) && !GameManager.I.Arrested) pc.InputLocked = false; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }
        bool wasBlocking;

        public void OpenShop(Shop s) { shop = s; invOpen = false; mapOpen = false; }
        public void CloseAll() { invOpen = false; mapOpen = false; shop = null; }
        public bool MapOpen => mapOpen;

        void Styles()
        {
            if (win != null) return;
            win = new GUIStyle(GUI.skin.box) { padding = new RectOffset(14, 14, 10, 10) }; win.normal.background = Tex(new Color(0.08f, 0.09f, 0.11f, 0.94f));
            title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold }; title.normal.textColor = Color.white;
            head = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold }; head.normal.textColor = new Color(0.85f, 0.85f, 0.9f);
            row = new GUIStyle(GUI.skin.label) { fontSize = 14 }; row.normal.textColor = Color.white;
            dim = new GUIStyle(row); dim.normal.textColor = new Color(0.65f, 0.65f, 0.7f);
            price = new GUIStyle(row) { alignment = TextAnchor.MiddleRight }; price.normal.textColor = new Color(0.56f, 0.9f, 0.6f);
            btn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
        }
        static Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

        void OnGUI()
        {
            var eco = EconomyManager.I; var gm = GameManager.I; if (!eco || !gm) return; Styles();
            if (invOpen) DrawInventory(eco, gm);
            else if (shop != null) DrawShop(eco, gm);
        }

        void DrawInventory(EconomyManager eco, GameManager gm)
        {
            float W = Screen.width, H = Screen.height; var r = new Rect(W / 2f - 260, H / 2f - 220, 520, 440);
            GUI.Box(r, GUIContent.none, win);
            GUILayout.BeginArea(new Rect(r.x + 14, r.y + 10, r.width - 28, r.height - 20));
            GUILayout.BeginHorizontal(); GUILayout.Label("Backpack", title); GUILayout.FlexibleSpace(); GUILayout.Label($"${eco.cash}", new GUIStyle(title) { normal = { textColor = new Color(0.56f, 0.9f, 0.6f) } }); GUILayout.EndHorizontal();
            GUILayout.Label("Tab / Esc closes · food restores hunger & stamina · stolen goods only sell at the pawn shop", dim);
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
