using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Gang;

namespace MafiaSandbox.UI
{
    /// <summary>K: your organisation. Crew (roles, weapons), Venues (lean on / buy the shop you stand in), Supplies (Sal's product and your debt), Hustles (every way to earn) and Status.</summary>
    public class CrewPanel : MonoBehaviour
    {
        public static CrewPanel I { get; private set; }
        public static bool IsOpen => I && I.open;
        bool open; int tab; Shop venue; Vector2 scroll; CrewBrain selected;
        static readonly string[] Tabs = { "Crew", "Venues", "Supplies", "Hustles", "Status" };

        void Awake() { I = this; }
        public void Open(int t = 0) { open = true; tab = t; scroll = Vector2.zero; }
        public void OpenVenue(Shop s) { open = true; tab = 1; venue = s; }
        public void OpenCrew(CrewBrain m) { open = true; tab = 0; selected = m; }

        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player || CharacterCreator.IsOpen || IntroCinematic.Playing) return;
            bool dead = gm.player.GetComponent<CharacterBody>()?.Dead ?? false;
            if (Input.GetKeyDown(KeyCode.K) && !PhonePanel.IsOpen) { if (open) open = false; else if (!dead && !GameUI.Blocking) Open(0); }
            if (open && (Input.GetKeyDown(KeyCode.Escape) || dead)) open = false;
        }

        void OnGUI()
        {
            if (!open || CharacterCreator.IsOpen) return; var cm = CrewManager.I; var eco = EconomyManager.I; if (!cm || !eco) return;
            float W = Screen.width, H = Screen.height; var r = new Rect(W / 2f - 380, Mathf.Max(20, H / 2f - 290), 760, Mathf.Min(H - 40, 580)); UiSkin.DrawPanel(r, true);
            GUILayout.BeginArea(new Rect(r.x + 16, r.y + 12, r.width - 32, r.height - 24));
            GUILayout.BeginHorizontal(); GUILayout.Label("Your organisation", UiSkin.Title); GUILayout.FlexibleSpace();
            GUILayout.Label($"<b>{Respect.RankName}</b>  ·  ${eco.cash}  ·  K / Esc closes", UiSkin.LabelDim); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Tabs.Length; i++) if (GUILayout.Button(Tabs[i], tab == i ? UiSkin.ButtonOn : UiSkin.Button, GUILayout.Height(28))) { tab = i; scroll = Vector2.zero; }
            GUILayout.EndHorizontal(); GUILayout.Space(6);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(r.height - 100));
            switch (tab) { case 0: TabCrew(cm); break; case 1: TabVenues(cm, eco); break; case 2: TabSupplies(cm, eco); break; case 3: TabHustles(); break; default: TabStatus(cm); break; }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void TabCrew(CrewManager cm)
        {
            GUILayout.Label($"CREW  {cm.crew.Count}/{cm.MaxCrew}   ·   next recruit ${cm.RecruitCost}   ·   walk up to a civilian and press F to recruit", UiSkin.H2);
            if (cm.crew.Count == 0) GUILayout.Label("Nobody yet. Recruits ask for sign-on cash and can say no. Wages are paid every shift.", UiSkin.LabelDim);
            foreach (var m in cm.crew.ToArray())
            {
                if (!m) continue; bool sel = selected == m;
                GUILayout.BeginVertical(UiSkin.Panel);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(m.displayName, sel ? UiSkin.ButtonOn : UiSkin.Button, GUILayout.Width(150))) selected = sel ? null : m;
                GUILayout.Label(m.Armed ? WeaponDef.All[m.weapon.Value].name : "unarmed", UiSkin.Label, GUILayout.Width(90));
                foreach (var ro in new[] { CrewBrain.Role.Follow, CrewBrain.Role.Guard, CrewBrain.Role.Dealer })
                    if (GUILayout.Button(ro.ToString(), m.role == ro ? UiSkin.ButtonOn : UiSkin.Button, GUILayout.Width(70))) m.SetRole(ro);
                GUILayout.FlexibleSpace(); GUILayout.Label($"sold {m.sales}", UiSkin.LabelDim, GUILayout.Width(60));
                if (GUILayout.Button("Dismiss", UiSkin.Button, GUILayout.Width(70))) { cm.Dismiss(m); GUILayout.EndHorizontal(); GUILayout.EndVertical(); break; }
                GUILayout.EndHorizontal();
                if (sel)
                {
                    GUILayout.Label("ARM  (pay once, they keep it)", UiSkin.H2); GUILayout.BeginHorizontal();
                    foreach (var k in CrewManager.ArmOptions) if (GUILayout.Button($"{WeaponDef.All[k].name} ${CrewManager.ArmPrice(k)}", m.weapon == k ? UiSkin.ButtonOn : UiSkin.Button)) cm.Arm(m, k);
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Follow: stays with you · Guard: holds this spot (stand at a venue and set it) · Dealer: sells your stock on the street each shift", UiSkin.LabelDim);
                }
                GUILayout.EndVertical(); GUILayout.Space(3);
            }
        }

        void TabVenues(CrewManager cm, EconomyManager eco)
        {
            var gm = GameManager.I; Shop near = venue;
            if (!near || Vector3.Distance(near.info.counter, gm.player.transform.position) > 8f) { near = null; foreach (var s in eco.shops) if (CrewManager.Eligible(s) && Vector3.Distance(s.info.counter, gm.player.transform.position) < 6f) { near = s; break; } }
            venue = near;
            GUILayout.Label("THIS VENUE", UiSkin.H2);
            if (!near) GUILayout.Label("Stand at a shop counter and press X (or open this tab there) to lean on the owner or buy the place.", UiSkin.LabelDim);
            else
            {
                var mode = cm.Mode(near); float st = cm.Strength(near.info.counter);
                GUILayout.Label($"<b>{near.label}</b>   status: {mode}   ·   muscle here {st:0.#} / needed {cm.NeedFor(near)}   ·   keeper present: {(near.HasLiveKeeper ? "yes" : "no")}", UiSkin.Label);
                GUILayout.Label($"Protection pays ${(int)(Mathf.Round(cm.BuyPrice(near) / 10f * 0.5f))} per shift. Owning it pays ${(int)(Mathf.Round(cm.BuyPrice(near) / 10f * 1.2f))} per shift, price ${cm.BuyPrice(near)}.", UiSkin.LabelDim);
                GUILayout.BeginHorizontal();
                if (mode == CrewManager.VenueMode.None && GUILayout.Button("Lean on the owner (protection)", UiSkin.Button, GUILayout.Height(34))) cm.Protect(near);
                if (mode != CrewManager.VenueMode.Owned && GUILayout.Button($"Buy it  ${cm.BuyPrice(near)}", UiSkin.ButtonOn, GUILayout.Height(34))) cm.Buy(near);
                GUILayout.EndHorizontal();
                string err = cm.RacketCheck(near); if (err != null && mode == CrewManager.VenueMode.None) GUILayout.Label("<color=#f08060>" + err + "</color>", UiSkin.Label);
            }
            GUILayout.Space(10); GUILayout.Label("YOUR VENUES", UiSkin.H2);
            int total = 0; foreach (var v in cm.venues) { if (!v.Key || v.Value == CrewManager.VenueMode.None) continue; int pay = cm.ShiftPay(v.Key); total += pay; GUILayout.Label($"{v.Key.label}  ·  {v.Value}  ·  ${pay}/shift", UiSkin.Label); }
            if (total == 0) GUILayout.Label("None yet.", UiSkin.LabelDim); else GUILayout.Label($"<b>Total ${total} per shift</b> (a shift is 6 game hours)", UiSkin.Label);
        }

        void TabSupplies(CrewManager cm, EconomyManager eco)
        {
            GUILayout.Label("SAL'S SUPPLIES (wholesale, delivered to your backpack)", UiSkin.H2);
            for (int i = 0; i < CrewManager.Products.Length; i++)
            {
                bool un = CrewManager.Unlocked(i);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{Items.Get(CrewManager.Products[i]).name}  ·  buy ${cm.WholeNow(i)}  ·  street ~${cm.StreetNow(i)} {cm.Trend(i)}  ·  you have {cm.Stock(i)}" + (un ? "" : $"   (needs {Respect.Ranks[Mathf.Min(5, CrewManager.UnlockAt(i))]})"), un ? UiSkin.Label : UiSkin.LabelDim, GUILayout.Width(470));
                GUI.enabled = un; if (GUILayout.Button("Order 1", UiSkin.Button)) cm.Order(i, 1); if (GUILayout.Button("Order 5", UiSkin.Button)) cm.Order(i, 5); GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Drugs: press X next to a civilian. Guns & rifles: press X next to a gang member (not hostile). Dealers sell up to 4 drug units per shift and keep 30%. Prices drift every shift.", UiSkin.LabelDim);
            GUILayout.Space(12); GUILayout.Label("YOUR DEBT TO SAL", UiSkin.H2);
            GUILayout.Label($"Paid ${cm.DebtPaid} of ${CrewManager.Debt}  (remaining ${CrewManager.Debt - cm.DebtPaid})", UiSkin.Label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pay $500", UiSkin.Button)) cm.PayDebt(500); if (GUILayout.Button("Pay $2000", UiSkin.Button)) cm.PayDebt(2000); if (GUILayout.Button("Pay all you can", UiSkin.ButtonOn)) cm.PayDebt(Mathf.Min(CrewManager.Debt - cm.DebtPaid, eco.cash));
            GUILayout.EndHorizontal();
        }

        static readonly string[,] Hustle =
        {
            { "Mug pedestrians", "Search the bodies you drop (F). Risky: witnesses make you a suspect." },
            { "Shoplift", "Take shelf items with G. Only a cop who sees you adds a star." },
            { "Rob registers", "Aim at a keeper until the hands go up. Big cash, silent alarm." },
            { "Bank & jeweller vaults", "Gold bars and jewellery. Highest payout, highest heat." },
            { "Fence stolen goods", "Sell loot at the pawn shop (stolen goods fetch less)." },
            { "Street dealing", "Order from Sal (K ▸ Supplies), sell with X. Pills at Hustler, powder at Enforcer. Prices swing every shift." },
            { "Bootleg liquor", "Brew moonshine at home (workbench), sell it to civilians with X. Cheap to make, easy money." },
            { "Home production", "Buy ingredients, cook weed, pills, powder and pistols at your workbench (small puzzles raise the yield)." },
            { "Arms trade", "Order pistols (Enforcer) and rifles (Capo) from Sal, sell to friendly gang members with X." },
            { "Protection racket", "Bring armed crew to a shop and lean on the owner (X at the counter)." },
            { "Own venues", "Buy shops outright (Hustler+). They pay every shift." },
            { "Dealer crew", "Recruit (F on civilians), set role Dealer, keep them stocked." },
            { "Stolen car → chop shop", "Phone ▸ Jobs: deliver the marked car to the pawn shop garage." },
            { "Smuggling runs", "Phone ▸ Jobs: carry a package across town before time runs out." },
            { "Contract hits", "Phone ▸ Jobs: Sal marks a target. Kill them unseen." },
        };
        void TabHustles()
        {
            GUILayout.Label("15 WAYS TO MAKE MONEY", UiSkin.H2);
            for (int i = 0; i < Hustle.GetLength(0); i++) { GUILayout.Label($"<b>{i + 1}. {Hustle[i, 0]}</b>", UiSkin.Label); GUILayout.Label(Hustle[i, 1], UiSkin.LabelDim); GUILayout.Space(2); }
        }

        void TabStatus(CrewManager cm)
        {
            GUILayout.Label($"<b>{Respect.RankName}</b>   {Respect.XP} / {Respect.NextAt} respect", UiSkin.Title);
            var rc = GUILayoutUtility.GetRect(10, 16, GUILayout.ExpandWidth(true)); UiSkin.DrawBar(rc, Respect.Progress, UiSkin.Accent);
            GUILayout.Space(8);
            GUILayout.Label($"Crew {cm.crew.Count}/{cm.MaxCrew} (armed {cm.ArmedCount}, dealers {cm.DealerCount})\nVenues: {cm.ProtectedCount} protected, {cm.OwnedCount} owned\nDeals made {cm.Deals}   ·   dealer income ${cm.DealerCash}\nJobs done {cm.JobsDone}   ·   cars chopped {cm.Chopped}   ·   hits {cm.Hits}\nDebt paid ${cm.DebtPaid} / ${CrewManager.Debt}", UiSkin.Label);
            if (!string.IsNullOrEmpty(cm.LastReport)) GUILayout.Label(cm.LastReport, UiSkin.LabelDim);
            GUILayout.Space(8); GUILayout.Label("RANK PERKS", UiSkin.H2);
            GUILayout.Label("Street Rat: weed, small shops (barber, cafe, market)\nHustler: pills, restaurants & pharmacies, buying venues, crew 3+\nEnforcer: powder, pawn shop & gun shop, crew 4+\nCapo / Underboss: jeweller, crew 5-7, the families take notice\nBoss: crew 8, the city is yours", UiSkin.LabelDim);
        }
    }
}
