using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Npc;
using MafiaSandbox.World;

namespace MafiaSandbox.Gang
{
    /// <summary>
    /// Your own organisation: recruit civilians, arm and assign them (follow / guard a venue / deal on a corner), lean on shops for protection money
    /// or buy them outright, sell product on the street, pay Sal's debt. Venues and dealers pay out every 6 in-game hours; the crew costs wages.
    /// </summary>
    public class CrewManager : MonoBehaviour
    {
        public static CrewManager I { get; private set; }
        public readonly List<CrewBrain> crew = new List<CrewBrain>();
        public enum VenueMode { None, Protected, Owned }
        public readonly Dictionary<Shop, VenueMode> venues = new Dictionary<Shop, VenueMode>();
        public int MaxCrew => Mathf.Min(8, 2 + Respect.Rank);
        public const int Debt = 6000;
        public int DebtPaid { get; private set; }
        // counters for missions
        public int Recruited, Deals, DealerCash, JobsDone, Chopped, Hits, Racketed, Bought; public int ShiftIncome;
        readonly Dictionary<NpcBrain, float> pitchCd = new Dictionary<NpcBrain, float>();
        int lastShift = -1; string lastReport = "";

        void Awake() { I = this; DebtPaid = PlayerPrefs.GetInt("ms_debt_paid_v1", 0); }

        // ------------------------------------------------------------------ products
        public static readonly string[] Products = { "Weed", "Pills", "Powder", "Guns", "Rifles", "Moonshine" };   // 0-2 drugs (sold to civilians), 3-4 arms (sold to gang members)
        static readonly int[] Wholesale = { 20, 55, 120, 180, 500, 25 }; static readonly int[] Street = { 45, 110, 240, 340, 950, 70 }; static readonly int[] UnlockRank = { 0, 1, 2, 2, 3, 0 };
        /// <summary>Market mood per product (0.7..1.4): drifts every shift, shifts both what Sal charges and what the street pays.</summary>
        public readonly float[] Mood = { 1f, 1f, 1f, 1f, 1f, 1f };
        public static bool IsArms(int i) => i == 3 || i == 4;
        public static int UnlockAt(int i) => UnlockRank[i];
        public int WholeNow(int i) => Mathf.Max(1, Mathf.RoundToInt(Wholesale[i] * Mood[i]));
        public int StreetNow(int i) => Mathf.Max(1, Mathf.RoundToInt(Street[i] * Mood[i]));
        public string Trend(int i) => Mood[i] > 1.12f ? "▲ hot" : Mood[i] < 0.9f ? "▼ slow" : "steady";
        public static bool Unlocked(int i) => Respect.Rank >= UnlockRank[i] || Career.IsStarterProduct(Products[i]);
        public static int WholesalePrice(int i) => Wholesale[i];
        public static int StreetBase(int i) => Street[i];
        public int Stock(int i) => EconomyManager.I ? EconomyManager.I.Count(Products[i]) : 0;
        public int TotalStock { get { int n = 0; for (int i = 0; i < 3; i++) n += Stock(i); return n; } }

        public bool Order(int i, int qty)
        {
            var eco = EconomyManager.I; var gm = GameManager.I; if (!eco) return false;
            if (!Unlocked(i)) { gm.Say($"Sal won't sell {Products[i]} to a {Respect.RankName}."); return false; }
            int cost = WholeNow(i) * qty; if (eco.cash < cost) { gm.Say($"Not enough cash (${cost})"); return false; }
            eco.cash -= cost; eco.AddItem(Products[i], qty, false); gm.Say($"Sal's runner dropped off {qty} × {Items.Get(Products[i]).name} (${cost})"); return true;
        }
        bool Take(string id, int n = 1)
        {
            var eco = EconomyManager.I; if (!eco || eco.Count(id) < n) return false;
            for (int k = 0; k < n; k++) { var it = eco.inv.Find(x => x.id == id); if (it == null) break; it.qty--; if (it.qty <= 0) eco.inv.Remove(it); }
            return true;
        }
        static readonly int[] SaleOrder = { 2, 1, 5, 0 };   // best first: powder, pills, moonshine, weed
        int BestProduct() { foreach (int i in SaleOrder) if (Unlocked(i) && Stock(i) > 0) return i; return -1; }

        /// <summary>Offer something to a civilian: who is buying, how much, and who might be watching.</summary>
        public void Deal(CivilianBrain c)
        {
            var gm = GameManager.I; var eco = EconomyManager.I; if (!eco || !c) return;
            int p = BestProduct(); if (p < 0) { gm.Say("You've got nothing to sell. Order supplies from Sal (phone → Contacts)."); return; }
            if (pitchCd.TryGetValue(c, out var t) && Time.time < t) { gm.Say($"{c.displayName} isn't interested."); return; }
            var pp = gm.player.transform.position;
            if (WantedSystem.I && WantedSystem.I.CopWitness(pp, 22f, 1, "dealing in front of the police")) { return; }
            float chance = 0.62f + (Shop.IsNight ? 0.12f : 0f) + Respect.Rank * 0.03f;
            if (Random.value < chance)
            {
                int price = Mathf.RoundToInt(StreetNow(p) * Random.Range(0.85f, 1.3f) * (Shop.IsNight ? 1.1f : 1f)); Take(Products[p]); eco.cash += price; Deals++;
                gm.Say($"Sold {Items.Get(Products[p]).name} for ${price}"); Respect.Add(p == 0 || p == 5 ? 3 : p == 1 ? 6 : 12); Sfx.At("click", pp);
            }
            else
            {
                gm.Say($"{c.displayName}: No thanks!"); pitchCd[c] = Time.time + 40f;
                if (Random.value < 0.3f && WantedSystem.I) { WantedSystem.I.Suspect(1, 50f); gm.Say($"{c.displayName} looks like they'll tell someone."); }
            }
        }

        /// <summary>Sell smuggled guns to a gang member (X on them). They pay well, but only if their family doesn't hate you.</summary>
        public void SellArms(MafiaSandbox.Factions.FactionBrain fb)
        {
            var gm = GameManager.I; var eco = EconomyManager.I; var fm = MafiaSandbox.Factions.FactionManager.I; if (!fb || !eco) return;
            int p = Stock(4) > 0 && Unlocked(4) ? 4 : Stock(3) > 0 && Unlocked(3) ? 3 : -1;
            if (p < 0) { gm.Say("You've got no guns to sell. Order them from Sal (K ▸ Supplies)."); return; }
            if (fm != null && fb.Def != null && fm.Hostile(fb.Def)) { gm.Say($"{fb.displayName}: Get lost, we don't deal with you."); return; }
            var pp = gm.player.transform.position;
            if (WantedSystem.I && WantedSystem.I.CopWitness(pp, 22f, 1, "arms dealing in front of the police")) return;
            int price = Mathf.RoundToInt(StreetNow(p) * Random.Range(0.95f, 1.25f)); Take(Products[p]); eco.cash += price; Deals++;
            gm.Say($"{fb.displayName} bought {Items.Get(Products[p]).name} for ${price}"); Respect.Add(p == 3 ? 20 : 45); Sfx.At("click", pp);
        }

        // ------------------------------------------------------------------ recruiting / arming
        public int RecruitCost => 100 + 60 * crew.Count;
        public bool TryRecruit(CivilianBrain c)
        {
            var gm = GameManager.I; var eco = EconomyManager.I; if (!c || !eco) return false;
            if (c.isMedic) { gm.Say($"{c.displayName}: I save lives, I don't take them."); return false; }
            if (crew.Count >= MaxCrew) { gm.Say($"Your crew is full ({MaxCrew}). Rank up to take on more."); return false; }
            if (pitchCd.TryGetValue(c, out var t) && Time.time < t) { gm.Say($"{c.displayName} already said no."); return false; }
            int cost = RecruitCost; if (eco.cash < cost) { gm.Say($"{c.displayName}: Sign-on is ${cost}, cash up front."); return false; }
            if (Random.value > 0.5f + 0.08f * Respect.Rank) { gm.Say($"{c.displayName}: Not my kind of work."); pitchCd[c] = Time.time + 60f; return false; }
            eco.cash -= cost; var go = c.gameObject; string nm = c.displayName;
            var sp = FindAnyObjectByType<NpcSpawner>(); if (sp) sp.npcs.Remove(c);
            Destroy(c); var m = go.AddComponent<CrewBrain>(); m.displayName = nm; m.name = "Crew " + nm; m.walkSpeed = 2.4f; m.runSpeed = 5.8f;
            crew.Add(m); Recruited++; Respect.Add(15); gm.Say($"{nm} joined your crew (${cost}). F opens the crew menu on them, K shows everyone.");
            return true;
        }

        public static readonly WeaponKind[] ArmOptions = { WeaponKind.Bat, WeaponKind.Knife, WeaponKind.Pistol, WeaponKind.Revolver, WeaponKind.Shotgun, WeaponKind.Rifle };
        public static int ArmPrice(WeaponKind k) => k == WeaponKind.Bat ? 40 : k == WeaponKind.Knife ? 60 : k == WeaponKind.Pistol ? 350 : k == WeaponKind.Revolver ? 900 : k == WeaponKind.Shotgun ? 1800 : 2400;
        public bool Arm(CrewBrain m, WeaponKind k)
        {
            var eco = EconomyManager.I; var gm = GameManager.I; int price = ArmPrice(k);
            if (eco.cash < price) { gm.Say($"Not enough cash (${price})"); return false; }
            eco.cash -= price; m.SetWeapon(k); gm.Say($"{m.displayName} is carrying a {WeaponDef.All[k].name}"); return true;
        }
        public void Dismiss(CrewBrain m) { crew.Remove(m); if (m) { GameManager.I?.Say($"{m.displayName} left your crew"); var cv = m.gameObject.AddComponent<CivilianBrain>(); cv.displayName = m.displayName; var sp = FindAnyObjectByType<NpcSpawner>(); if (sp) sp.npcs.Add(cv); Destroy(m); } }
        public void OnMemberDied(CrewBrain m) { crew.Remove(m); GameManager.I?.Say($"{m.displayName} was killed."); }
        public int ArmedCount { get { int n = 0; foreach (var m in crew) if (m && m.Armed) n++; return n; } }
        public int DealerCount { get { int n = 0; foreach (var m in crew) if (m && m.role == CrewBrain.Role.Dealer) n++; return n; } }

        // ------------------------------------------------------------------ venues
        static readonly Dictionary<string, int> BaseIncome = new Dictionary<string, int> { { "barber", 60 }, { "cafe", 70 }, { "market", 90 }, { "pharmacy", 110 }, { "restaurant", 140 }, { "pawn", 150 }, { "gun", 220 }, { "jeweler", 260 } };
        static readonly Dictionary<string, int> NeedStrength = new Dictionary<string, int> { { "barber", 1 }, { "cafe", 2 }, { "market", 2 }, { "pharmacy", 3 }, { "restaurant", 3 }, { "pawn", 3 }, { "gun", 4 }, { "jeweler", 4 } };
        static readonly Dictionary<string, int> NeedRank = new Dictionary<string, int> { { "barber", 0 }, { "cafe", 0 }, { "market", 0 }, { "pharmacy", 1 }, { "restaurant", 1 }, { "pawn", 2 }, { "gun", 2 }, { "jeweler", 2 } };
        public static bool Eligible(Shop s) => s && s.info && BaseIncome.ContainsKey(s.kind);
        public VenueMode Mode(Shop s) => s && venues.TryGetValue(s, out var m) ? m : VenueMode.None;
        public int ProtectedCount { get { int n = 0; foreach (var v in venues) if (v.Value == VenueMode.Protected) n++; return n; } }
        public int OwnedCount { get { int n = 0; foreach (var v in venues) if (v.Value == VenueMode.Owned) n++; return n; } }
        public int ShiftPay(Shop s) { int b = BaseIncome[s.kind]; var m = Mode(s); return m == VenueMode.Owned ? Mathf.RoundToInt(b * 1.2f) : m == VenueMode.Protected ? Mathf.RoundToInt(b * 0.5f) : 0; }
        public int BuyPrice(Shop s) => BaseIncome[s.kind] * 10;
        public float Strength(Vector3 pos)
        {
            float st = 1f; foreach (var m in crew) if (m && !m.Dead && Vector3.Distance(m.transform.position, pos) < 18f) st += m.Armed ? 1f : 0.5f; return st;
        }
        public int NeedFor(Shop s) => NeedStrength[s.kind];

        public string RacketCheck(Shop s)
        {
            if (!Eligible(s)) return "Nobody here to lean on.";
            if (Mode(s) != VenueMode.None) return "Already yours.";
            if (!s.HasLiveKeeper) return "Nobody's behind the counter.";
            if (Respect.Rank < NeedRank[s.kind]) return $"They don't take a {Respect.RankName} seriously yet (needs {Respect.Ranks[NeedRank[s.kind]]}).";
            float st = Strength(s.info.counter); if (st < NeedStrength[s.kind]) return $"Not enough muscle here: {st:0.#}/{NeedStrength[s.kind]} (bring armed crew).";
            return null;
        }

        public bool Protect(Shop s)
        {
            var gm = GameManager.I; string err = RacketCheck(s); if (err != null) { gm.Say(err); return false; }
            var pp = gm.player.transform.position;
            venues[s] = VenueMode.Protected; Racketed++; Respect.Add(40 + NeedStrength[s.kind] * 10);
            gm.Say($"The {s.label} agrees to pay for \"protection\": ${ShiftPay(s)} per shift.");
            // leaning on someone in front of a cop is a crime; a civilian watching only makes you a suspect
            if (WantedSystem.I && !WantedSystem.I.CopWitness(pp, 30f, 1, "extortion in front of the police")) WantedSystem.I.Suspect(1, 60f);
            return true;
        }
        public bool Buy(Shop s)
        {
            var gm = GameManager.I; var eco = EconomyManager.I; if (!Eligible(s)) { gm.Say("Not for sale."); return false; }
            if (Mode(s) == VenueMode.Owned) { gm.Say("You already own it."); return false; }
            if (Respect.Rank < 1) { gm.Say("Nobody sells to a Street Rat. Get some respect first."); return false; }
            int price = BuyPrice(s); if (eco.cash < price) { gm.Say($"Not enough cash (${price})"); return false; }
            eco.cash -= price; venues[s] = VenueMode.Owned; Bought++; Respect.Add(80 + NeedStrength[s.kind] * 15);
            gm.Say($"You bought the {s.label} for ${price}. It earns ${ShiftPay(s)} per shift."); return true;
        }

        // ------------------------------------------------------------------ debt
        public bool PayDebt(int amount)
        {
            var eco = EconomyManager.I; var gm = GameManager.I; amount = Mathf.Min(amount, Debt - DebtPaid); if (amount <= 0) return false;
            if (eco.cash < amount) { gm.Say("Not enough cash."); return false; }
            eco.cash -= amount; DebtPaid += amount; PlayerPrefs.SetInt("ms_debt_paid_v1", DebtPaid); Respect.Add(amount / 20);
            gm.Say($"Paid ${amount} to Sal. Remaining: ${Debt - DebtPaid}"); return true;
        }

        // ------------------------------------------------------------------ shifts
        void Update()
        {
            var dn = DayNightCycle.I; if (!dn || !GameManager.I || !GameManager.I.player) return;
            int shift = Mathf.FloorToInt(dn.TotalHours / 6f);
            if (lastShift < 0) lastShift = shift;
            if (shift > lastShift) { int n = Mathf.Min(8, shift - lastShift); lastShift = shift; for (int k = 0; k < n; k++) Payout(); }
        }

        void Payout()
        {
            var eco = EconomyManager.I; var gm = GameManager.I; if (!eco) return;
            for (int i = 0; i < Mood.Length; i++) Mood[i] = Mathf.Clamp(Mood[i] + Random.Range(-0.18f, 0.18f) + (1f - Mood[i]) * 0.25f, 0.7f, 1.4f);
            int venueIncome = 0; foreach (var v in venues) if (v.Key && v.Value != VenueMode.None) venueIncome += ShiftPay(v.Key);
            int dealIncome = 0;
            foreach (var m in crew)
            {
                if (!m || m.role != CrewBrain.Role.Dealer) continue;
                for (int n = 0; n < 4; n++) { int p = BestProduct(); if (p < 0) break; Take(Products[p]); int rev = Mathf.RoundToInt(StreetNow(p) * 0.7f); dealIncome += rev; m.sales++; Deals++; }
            }
            DealerCash += dealIncome; int wages = 0; foreach (var m in crew) if (m) wages += Mathf.RoundToInt(m.wage * 0.25f);
            wages = Mathf.Min(wages, eco.cash + venueIncome + dealIncome);
            eco.cash += venueIncome + dealIncome - wages; ShiftIncome = venueIncome + dealIncome - wages;
            if (venueIncome + dealIncome + wages > 0)
            {
                lastReport = $"Shift report: venues +${venueIncome}, dealers +${dealIncome}, wages -${wages}";
                gm.Say(lastReport); Respect.Add(Mathf.Max(1, (venueIncome + dealIncome) / 25));
            }
        }
        public string LastReport => lastReport;

        // ------------------------------------------------------------------ world labels
        void OnGUI()
        {
            var cam = Camera.main; if (!cam || MafiaSandbox.UI.GameUI.Blocking || crew.Count == 0) return;
            var st = new GUIStyle(MafiaSandbox.UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 11 }; st.normal.textColor = MafiaSandbox.UI.UiSkin.Green;
            foreach (var m in crew)
            {
                if (!m || m.Dead) continue; var wp = m.transform.position + Vector3.up * 2.15f; if ((wp - cam.transform.position).sqrMagnitude > 30f * 30f) continue;
                var sp = cam.WorldToScreenPoint(wp); if (sp.z <= 0f) continue;
                string tag = m.role == CrewBrain.Role.Follow ? "crew" : m.role == CrewBrain.Role.Guard ? "guard" : "dealer";
                GUI.Label(new Rect(sp.x - 60, Screen.height - sp.y - 10, 120, 20), $"{m.displayName} · {tag}", st);
            }
        }
    }
}
