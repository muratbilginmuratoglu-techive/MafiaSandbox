using System;
using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Factions;
using MafiaSandbox.Player;
using MafiaSandbox.Vehicles;
using MafiaSandbox.World;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// Main story: an 8-step mission chain (phone → car → gun shop → cash → the family → crew fights → shake the heat → home).
    /// A tracker panel shows the current objective, distance and direction; a yellow beacon marks the target in the world.
    /// Fixer Sal texts you at each step; rewards are paid in cash. Progress is remembered between launches.
    /// </summary>
    public class QuestManager : MonoBehaviour
    {
        public static QuestManager I { get; private set; }
        public class Quest { public string title, desc, sal, tip; public Func<string> descF, tipF; public int reward; public Func<Vector3?> target; public Func<bool> done; public Action begin, cheat; }
        public static string DescOf(Quest q) => q.descF != null ? q.descF() : q.desc;
        public static string TipOf(Quest q) => q.tipF != null ? q.tipF() : q.tip;
        /// <summary>Hint text for the current mission (shown in the tracker and on the workstation panel).</summary>
        public string Coach { get; private set; }
        public Vector3? CurrentTarget { get; private set; }
        int beganIdx = -1, dealsBase, qBase; bool introPosted; CharacterBody joshua;
        // ---- Joshua: found by asking around (restaurant cashier, then a guest); he smokes at the playground every night
        public static bool AskedCashier, AskedGuest;
        Quest qAskCashier, qAskGuest, qPark;
        public bool CashierAskable(Shop sh) => started && qAskCashier != null && !AllDone && Quests[Index] == qAskCashier && !AskedCashier && sh != null && sh.kind == "restaurant" && sh.info && sh.info.label == "Restaurant";
        public bool GuestAskable(Npc.NpcBrain nb)
        {
            if (!started || qAskGuest == null || AllDone || Quests[Index] != qAskGuest || AskedGuest || !(nb is Npc.CamperBrain) || nb.displayName != "Guest") return false;
            var eco = EconomyManager.I; if (!eco) return false; foreach (var s in eco.shops) if (s.info && s.info.kind == "restaurant" && s.info.label == "Restaurant") { var f = s.info.footprint; var p = nb.transform.position; return p.x > f.min.x - 1f && p.x < f.max.x + 1f && p.z > f.min.z - 1f && p.z < f.max.z + 1f; }
            return false;
        }
        public void Ask(bool guest)
        {
            if (!guest) { AskedCashier = true; GameManager.I.Say("Cashier: Joshua? Never heard the name. Ask the people in here, somebody always knows."); }
            else { AskedGuest = true; GameManager.I.Say("Guest: Joshua... skinny guy, always scared? Goes out to the playground every night to smoke where his wife can't see."); PhonePanel.Post("jeremy", "Every night at the playground, alone, with a cigarette. That's where you get him. Go after dark (20:00) and look for the glowing ember."); }
        }
        Vector3 ParkPos()
        {
            var g = GameObject.Find("Oyun_Parki"); Vector3 p = g ? g.transform.position : Data.Coords.ToUnity(25f, 0f, 32f);
            if (UnityEngine.AI.NavMesh.SamplePosition(p + new Vector3(2.5f, 0f, 0f), out var h, 5f, UnityEngine.AI.NavMesh.AllAreas)) p = h.position; return p;
        }
        static bool NightNow { get { var d = DayNightCycle.I; return d && (d.timeOfDay >= 20f || d.timeOfDay < 5.5f); } }
        /// <summary>Joshua exists only at night, at the playground, until he is dead.</summary>
        void TendJoshua()
        {
            bool want = qPark != null && Quests[Index] == qPark && NightNow && !Career.StashTaken;
            if (want && (!joshua)) SpawnJoshua();
            else if (!want && joshua && !joshua.Dead) { Destroy(joshua.gameObject); joshua = null; }
        }
        void SpawnJoshua()
        {
            if (joshua || Career.StashTaken) return; var gm = GameManager.I;
            var sp = FindAnyObjectByType<Npc.NpcSpawner>(); if (!sp || sp.prefabs == null || sp.prefabs.Length == 0) return;
            Vector3 pos = ParkPos();
            GameObject prefab = null; foreach (var pf in sp.prefabs) if (pf && pf.name.StartsWith("Vatandas_07")) prefab = pf; if (!prefab) prefab = sp.prefabs[0];
            var c = sp.Spawn<Npc.CamperBrain>(prefab, pos, 0f); c.homeSpot = c.transform.position; c.pose = "Idle"; c.displayName = "Joshua"; c.fire = pos + Vector3.forward * 5f; c.walkSpeed = 1.5f;
            joshua = c.GetComponent<CharacterBody>();
            // what marks him out: a glowing cigarette, a red ember light and a gold arrow bobbing over his head
           
            var cig = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(cig.GetComponent<Collider>()); cig.name = "Cigarette"; cig.transform.SetParent(c.transform, false); cig.transform.localPosition = new Vector3(0.08f, 1.55f, 0.28f); cig.transform.localScale = new Vector3(0.025f, 0.025f, 0.1f);
            var cm = new Material(Shader.Find("Standard")); cm.color = new Color(1f, 0.35f, 0.1f); cm.EnableKeyword("_EMISSION"); cm.SetColor("_EmissionColor", new Color(3f, 0.8f, 0.2f)); cig.GetComponent<Renderer>().sharedMaterial = cm;
            var lg = new GameObject("Ember"); lg.transform.SetParent(c.transform, false); lg.transform.localPosition = new Vector3(0.08f, 1.55f, 0.35f); var li = lg.AddComponent<Light>(); li.color = new Color(1f, 0.45f, 0.15f); li.range = 4f; li.intensity = 1.6f;
            var ar = new GameObject("MarkerArrow"); ar.transform.SetParent(c.transform, false); ar.transform.localPosition = new Vector3(0f, 2.6f, 0f);
            var tm = ar.AddComponent<TextMesh>(); tm.text = "▼\nJOSHUA"; tm.fontSize = 64; tm.characterSize = 0.06f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = new Color(1f, 0.82f, 0.15f);
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); if (f) { tm.font = f; var ts = Shader.Find("GUI/Text Shader"); if (ts) { var lm = new Material(ts); lm.mainTexture = f.material.mainTexture; lm.color = new Color(1f, 0.82f, 0.15f); lm.SetInt("unity_GUIZTestMode", 8); lm.renderQueue = 4000; ar.GetComponent<MeshRenderer>().sharedMaterial = lm; } }
            ar.AddComponent<MarkerBob>();
        }
        class MarkerBob : MonoBehaviour { Vector3 b; void Start() { b = transform.localPosition; } void Update() { transform.localPosition = b + Vector3.up * Mathf.Sin(Time.time * 3f) * 0.08f; var cam = Camera.main; if (cam) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up); } }
        public readonly List<Quest> Quests = new List<Quest>();
        public int Index { get; private set; }
        public bool AllDone => Index >= Quests.Count;
        const string Key = "ms_quest_idx_v7", OldKey = "ms_quest_idx_v6";
        // the 24 missions of the old chain, in their old order (a save from before the long story is carried over by title)
        static readonly string[] OldTitles = { "0. Ask the restaurant", "0. Ask around", "0. Settle the score", "0. Choose your trade", "0. Shopping run", "0. Set up at home", "0. First batch", "0. Let it cook", "0. Collect the product", "0. Three years of news", "0. Pay your respects", "I. A message from Sal", "I. Wheels", "I. First sales", "I. Earn respect", "II. Hire muscle", "II. Arm your people", "II. Pay Sal", "II. Hot corner", "II. Make them pay", "III. Make a name", "III. Your turf", "III. Clear the books", "V. The Don" };

        int crewKills; bool heatSeen, started, phoneSeen; float doneFlash; string doneText; GameObject beacon; PlayerController pc; float startT = -1f;

        void Awake()
        {
            I = this; var g = gameObject;   // make sure the career systems exist even in a scene built before they were added
            if (!g.GetComponent<HomePlacer>()) g.AddComponent<HomePlacer>();
            if (!g.GetComponent<CareerPanel>()) g.AddComponent<CareerPanel>(); if (!g.GetComponent<Waypoints>()) g.AddComponent<Waypoints>();
        }
        void OnEnable() { WorldEvents.OnCharacterDied += OnDied; }
        void OnDisable() { WorldEvents.OnCharacterDied -= OnDied; }
        void OnDied(CharacterBody b, string by) { if ((by == "player" || by == "crew") && b && b.GetComponent<FactionBrain>()) crewKills++; }

        void Build()
        {
            var gm = GameManager.I; var eco = EconomyManager.I;
            Vector3? Door(string kind) { if (eco) foreach (var s in eco.shops) if (s.info && s.info.kind == kind && (kind != "restaurant" || s.info.label == "Restaurant")) return s.info.door; return null; }
            bool Inside(string kind) { if (!eco || !pc) return false; foreach (var s in eco.shops) if (s.info && s.info.kind == kind) { var f = s.info.footprint; var p = pc.transform.position; return p.x > f.min.x && p.x < f.max.x && p.z > f.min.z && p.z < f.max.z; } return false; }
            FactionDef mor = FactionDefs.Get("moretti"), kap = FactionDefs.Get("kaplan");
            Vector3? NearestHq() { if (mor == null || kap == null || !pc) return null; var p = pc.transform.position; return (mor.HqUnity - p).sqrMagnitude < (kap.HqUnity - p).sqrMagnitude ? mor.HqUnity : kap.HqUnity; }
            bool NearHq() { if (!pc) return false; var p = pc.transform.position; foreach (var f in new[] { mor, kap }) if (f != null && Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(f.HqUnity.x, 0, f.HqUnity.z)) < 14f) return true; return false; }
            var vi = pc.GetComponent<VehicleInteraction>();
            var cm = Gang.CrewManager.I;
            Vector3? Moretti() => mor != null ? mor.HqUnity : (Vector3?)null;
            // ACT 0 - AFTERMATH: three years in a coma, the family is gone, Jeremy is the only one left.
            Vector3? JerSpot() => gm.homePosition;
            bool InHouse() { if (!eco || eco.homeShop == null || !eco.homeShop.info || !pc) return false; var f = eco.homeShop.info.footprint; var p = pc.transform.position; return p.x > f.min.x && p.x < f.max.x && p.z > f.min.z && p.z < f.max.z; }
            bool slept = false;
            int C() => Career.Chosen;
            Vector3? Nearest(string kind) { if (!eco || !pc) return null; Vector3? best = null; float bd = 1e9f; foreach (var s in eco.shops) if (s.info && s.info.kind == kind) { float d = (s.info.door - pc.transform.position).sqrMagnitude; if (d < bd) { bd = d; best = s.info.door; } } return best; }
            bool KitOk() => Career.Has && (eco.Count(Career.Kit) > 0 || (HomeLab.I && HomeLab.I.CountStations(C()) > 0));
            int HaveItem(string id) => HomeLab.I ? HomeLab.I.Have(id) : eco.Count(id);
            bool ShopDone() { if (!Career.Has || !KitOk()) return false; foreach (var (id, n, _) in Career.FirstBatch[C()]) if (HaveItem(id) < n) return false; return true; }
            Vector3? ShopTarget() { if (!Career.Has) return null; if (!KitOk()) return Nearest(Career.KitShop[C()]); foreach (var (id, n, shop) in Career.FirstBatch[C()]) if (HaveItem(id) < n) return Nearest(shop); return gm.homePosition; }
            string ShopDesc()
            {
                if (!Career.Has) return "Choose your trade first.";
                string t = (KitOk() ? "[x] " : "[ ] ") + Items.Get(Career.Kit).name + $" at the {Items.ShopFor(Career.KitShop[C()]).title}";
                foreach (var (id, n, shop) in Career.FirstBatch[C()]) t += "\n" + (HaveItem(id) >= n ? "[x] " : "[ ] ") + $"{Items.Get(id).name} {Mathf.Min(HaveItem(id), n)}/{n} at the {Items.ShopFor(shop).title}";
                return t;
            }
            Vector3? StationPos() { if (!HomeLab.I || !Career.Has) return null; foreach (var st in HomeLab.I.stations) if (st.kind == C()) return st.pos; return null; }
            string StName() => Career.Has ? Career.StationNames[C()].ToLower() : "workstation";
            // ACT 0 - AFTERMATH: three years in a coma, the family is gone. Money from the old stash starts a new trade at home.
            // ---- the organisation and the shortcuts the mission self test uses (each one goes through the same game calls a player would trigger)
            var emp = Gang.Empire.I;
            void Cash(int n) { if (eco.cash < n) eco.cash = n; }
            string Sellable() => !Career.Has || Career.Chosen == Career.Arms ? "Weed" : Career.StarterProduct[C()];   // what a dealer can sell for you
            void Give(string id, int n) { int have = eco.Count(id); if (have < n) eco.AddItem(id, n - have, false); }
            void RankTo(int r) { r = Mathf.Clamp(r, 0, Gang.Respect.Thresholds.Length - 1); if (Gang.Respect.Rank < r) Gang.Respect.Add(Gang.Respect.Thresholds[r] - Gang.Respect.XP); }
            void TeleHome(bool inside)
            {
                Vector3 hp = gm.homePosition; float yaw = gm.homeYaw; var lab = HomeLab.I;
                if (inside && Stash.I && lab && lab.Ready) { Vector3 sd = Stash.I.Pos - lab.BedPos; sd.y = 0f; sd.Normalize(); Vector3 dd = Vector3.Cross(sd, Vector3.up); hp = Stash.I.Pos + dd * 1.3f; }
                if (vi && vi.Current) vi.Exit(); gm.player.Teleport(hp, yaw);
            }
            void Recruit(int want)
            {
                if (!cm) return; while (cm.MaxCrew < want && Gang.Respect.Rank < 5) RankTo(Gang.Respect.Rank + 1);
                for (int pass = 0; pass < 6 && cm.crew.Count < want; pass++)
                    foreach (var cv in FindObjectsByType<Npc.CivilianBrain>(FindObjectsSortMode.None)) { if (cm.crew.Count >= want) break; if (!cv || cv.Dead || cv.isMedic) continue; Cash(cm.RecruitCost + 100); cm.TryRecruit(cv); }
            }
            void DoDeals(int n)
            {
                if (!cm) return; int goal = cm.Deals + n; string prod = Career.Has ? Career.StarterProduct[C()] : "Weed";
                if (Career.Chosen == Career.Arms) foreach (var fb in FindObjectsByType<FactionBrain>(FindObjectsSortMode.None)) { if (cm.Deals >= goal) break; if (!fb || fb.Dead) continue; Give(prod, 2); cm.SellArms(fb); }
                for (int pass = 0; pass < 8 && cm.Deals < goal; pass++)
                    foreach (var cv in FindObjectsByType<Npc.CivilianBrain>(FindObjectsSortMode.None)) { if (cm.Deals >= goal) break; if (!cv || cv.Dead) continue; Give(Career.Chosen == Career.Arms ? "Weed" : prod, 2); cm.Deal(cv); }
            }
            void MakeBatch(int r)
            {
                var lab = HomeLab.I; if (!lab || r < 0) return; if (lab.batches[r].state != 0) lab.CollectNow(r);
                foreach (var (id, n) in HomeLab.Recipes[r].inputs) Give(id, n);
                lab.StartBatch(r, 0.8f); lab.CollectNow(r);
            }
            int Kinds() { var lab = HomeLab.I; int c = 0; if (lab) for (int i = 0; i < 3; i++) if (lab.CountStations(i) > 0) c++; return c; }
            void AddKind(int kind) { var lab = HomeLab.I; if (lab && lab.Ready && eco.homeShop != null && lab.CountStations(kind) == 0) AdminPanel.PlaceOne(lab, eco.homeShop.info, kind); }
            void NewRecipe()
            {
                var lab = HomeLab.I; if (!lab || !emp) return;
                for (int k = 0; k < 3; k++) { if (lab.CountStations(k) == 0) continue; foreach (int r in HomeLab.RecipesFor(k)) { if ((emp.RecipeMask & (1 << r)) != 0 || HomeLab.Locked(HomeLab.Recipes[r])) continue; MakeBatch(r); return; } }
            }
            void StockFor(int kind) { if (!emp) return; int r = emp.RecipeFor(kind); if (r < 0) return; foreach (var (id, n) in HomeLab.Recipes[r].inputs) Give(id, n); }
            void EnsureZone() { if (emp && emp.ZonesOwned == 0) { RankTo(2); emp.DebugCapture(emp.NearestTarget()); } }
            void DoGarrison(int n)
            {
                EnsureZone(); if (!emp) return;
                foreach (var z in FactionDefs.All) while (Gang.Empire.Owns(z.id) && emp.GarrisonTotal < n && emp.Garrison(z.id) < Gang.Empire.MaxGarrison) { Cash(Gang.Empire.SoldierCost + 100); if (!emp.HireSoldier(z)) break; }
            }
            void BuyVenues(int n) { if (!cm) return; foreach (var sh in eco.shops) { if (cm.OwnedCount >= n) break; if (!Gang.CrewManager.Eligible(sh) || cm.Mode(sh) == Gang.CrewManager.VenueMode.Owned) continue; Cash(cm.BuyPrice(sh) + 100); cm.Buy(sh); } }
            Vector3? ShopTarget2() { if (!Career.Has) return null; foreach (var (id, n, shop) in Career.FirstBatch[C()]) if (HaveItem(id) < n * 2) return Nearest(shop); return gm.homePosition; }
            Vector3? KitTarget() { var lab = HomeLab.I; if (!lab) return null; for (int k = 0; k < 3; k++) if (lab.CountStations(k) == 0) return eco.Count(Career.KitIds[k]) > 0 ? gm.homePosition : Nearest(Career.KitShop[k]); return gm.homePosition; }
            Vector3? WarTarget() { if (!emp) return null; if (emp.War != null) return emp.War.HqUnity; var t = emp.NearestTarget(); return t != null ? t.HqUnity : (Vector3?)null; }
            Vector3? AttackTarget() { return emp && emp.AttackZone != null ? emp.AttackZone.HqUnity : (Vector3?)null; }
            Vector3? FamilyTarget() { if (mor != null && !Gang.Empire.Owns(mor.id) && (kap == null || Gang.Empire.Owns(kap.id) || !pc || (mor.HqUnity - pc.transform.position).sqrMagnitude < (kap.HqUnity - pc.transform.position).sqrMagnitude)) return mor.HqUnity; return kap != null ? kap.HqUnity : (Vector3?)null; }
            // ACT 0 - AFTERMATH: three years in a coma, the family is gone. Money from the old stash starts a new trade at home.
            Quests.Add(qAskCashier = new Quest { title = "0. Ask the restaurant", desc = "Nobody knows where Joshua went. Start at the Restaurant: walk up to the counter and ask the cashier (F).", tip = "Gold marker = the Restaurant. The cashier stands behind the counter.", reward = 0, target = () => Career.StashTaken ? null : Door("restaurant"), done = () => AskedCashier || Career.StashTaken, sal = null });
            Quests.Add(qAskGuest = new Quest { title = "0. Ask around", desc = "The cashier shrugs. Ask one of the people sitting in the Restaurant about Joshua (walk up, press F).", tip = "Guests sit at the tables. Try the ones who look like regulars.", reward = 0, target = () => Career.StashTaken ? null : Door("restaurant"), done = () => AskedGuest || Career.StashTaken, sal = null });
            Quests.Add(qPark = new Quest { title = "0. Settle the score", descF = () => NightNow ? "Joshua is at the playground, smoking: look for the glowing ember and the gold arrow. Take him down and search the body (F) for the money he was paid." : "Joshua only goes out to the playground at night. Come back after 20:00 (or sleep: F at your bed after 19:30). Then take him down and search the body (F).", tip = "Fists, the knife (4) or the bat (5). If a cop sees it, they will chase you. He runs when he's hurt.", reward = 0, target = () => Career.StashTaken ? null : ParkPos(), done = () => Career.StashTaken, sal = null });
            Quests.Add(new Quest { title = "0. Choose your trade", desc = "A menu asks how you will make a living: bootleg alcohol, drugs or arms. Read the steps and choose one.", tip = "It decides what you buy, what you make at home and who you sell to.", reward = 0, begin = () => CareerPanel.I?.Open(), done = () => Career.Has, sal = "jeremy|Joshua paid for what he did. Now that money has to work. What are you going to do with it? Think hard, it decides everything." });
            Quests.Add(new Quest { title = "0. Shopping run", descF = ShopDesc, tip = "Walk into the shop and press F at the counter. Gold marker = next stop.", reward = 100, target = ShopTarget, done = ShopDone, sal = null });
            Quests.Add(new Quest { title = "0. Set up at home", descF = () => $"Go home. Inside the house press B, walk to where you want your {StName()}, rotate with Q/E and press F to place it.", tip = "Leave space around it. It can't go near the bed, door, stash or furniture. Press B next to it later to move it.", reward = 100, target = () => gm.homePosition, done = () => Career.Has && HomeLab.I && HomeLab.I.CountStations(C()) > 0, sal = "jeremy|Good. Now bring it all home and put it where you can work. Press B inside the house." });
            Quests.Add(new Quest { title = "0. First batch", descF = () => $"Walk up to your {StName()} and press F. Pick the recipe, check the ingredients and get to work at the bench: every step is done by hand (carry, pour with the mouse wheel, turn, cut). Better work means more product.", tip = "Step 1: the recipe is selected. Step 2: ingredients must be full (2/2 etc). Step 3: start the bench job and follow the hint at the bottom of the screen.", reward = 50, target = StationPos, done = () => Career.Has && HomeLab.I && (HomeLab.I.batches[Career.RecipeIndex].state >= 1 || Career.Collects >= 1), sal = "jeremy|Time to learn the craft. Don't rush it, a good batch is worth three bad ones." });
            Quests.Add(new Quest { title = "0. Let it cook", descF = () => "A batch takes game hours. Sleep in your bed to skip time (inside the house, press F at the bed after 19:30) or just wait until the READY message.", tip = "Sleeping finishes everything that is cooking. You can also wander off and come back.", reward = 50, target = () => { var l = HomeLab.I; if (l && HomeLab.CanSleepNow) return l.BedPos; return StationPos(); }, done = () => Career.Has && HomeLab.I && (HomeLab.I.batches[Career.RecipeIndex].state == 2 || Career.Collects >= 1), sal = null });
            Quests.Add(new Quest { title = "0. Collect the product", descF = () => $"Go back to your {StName()}, press F and press Collect. The product lands in your backpack.", tip = "You will be able to sell it on the street later. Open your backpack with I.", reward = 150, target = StationPos, done = () => Career.Collects >= 1, sal = "jeremy|That's the whole loop: buy, make, wait, collect. Now do it again, but bigger." });
            Quests.Add(new Quest { title = "0. Three years of news", desc = "Press T and check Jeremy's message about the war.", reward = 25, done = () => PhonePanel.IsOpen, sal = "jeremy|You want to know who did it? Two families, Morettis and Kaplans, shot up the whole street. Nobody was arrested. Sal knows more. He is the only one who talks." });
            Quests.Add(new Quest { title = "0. Pay your respects", desc = "Be at the ruined house after dark (20:00 to 05:00).", reward = 50, target = JerSpot, done = () => { var d = DayNightCycle.I; return InHouse() && d && (d.timeOfDay >= 20f || d.timeOfDay < 5f); }, sal = "jeremy|Go home after dark and spend a quiet minute with them. Then call Sal, he is waiting." });
            // ACT I - STREET RAT: your uncle Enzo died owing Sal $6000. Learn the loop: buy, make, sell.
            Quests.Add(new Quest { title = "I. A message from Sal", desc = "Press T to open your phone.", reward = 50, done = () => PhonePanel.IsOpen, sal = "sal|Heard you woke up. Good. We should talk, kid. Check your phone." });
            Quests.Add(new Quest { title = "I. Wheels", desc = "Your old bicycle is parked in front of the house. Get on it (walk up, press E), or take any car.", reward = 100, done = () => vi && vi.Current, sal = "Your uncle Enzo left you his debt: $6000. I'm a reasonable man. Work it off. First: get yourself some wheels." });
            Quests.Add(new Quest { title = "I. First sales", descF = () => Career.Chosen == Career.Arms ? "Sell 3 pistols to armed gang members: find a gang member (red dot on the minimap), walk up and press X." : "Sell 3 units of what you made: walk up to a civilian and press X. Do it where cops can't see.", tip = "Check your backpack (I) for stock. Witnesses talk, but only a cop who sees you can arrest you.", reward = 150, begin = () => dealsBase = cm ? cm.Deals : 0, done = () => cm && cm.Deals - dealsBase >= 3, sal = "sal|Heard you're cooking at home. Good, no cut for me. Now move it: sell it on the street. Watch for cops." });
            Quests.Add(new Quest { title = "I. Pocket money", desc = "Have $400 in your pocket. Sell what you make, do a hustle (K ▸ Hustles) or sell loot at the Pawn Shop.", tip = "Every dollar from here goes back into the business.", reward = 100, done = () => eco.cash >= 400, cheat = () => { Cash(450); }, sal = "sal|Selling three bags doesn't make you a businessman. Show me you can hold on to four hundred dollars." });
            Quests.Add(new Quest { title = "I. Stock the cupboard", descF = () => "Buy ingredients for two more batches of your starter recipe" + (Career.Has && HomeLab.I ? $" (enough for {HomeLab.I.MaxBatches(Career.RecipeIndex)}/2 now)." : "."), tip = "The cupboard at home counts too. Put spare ingredients in it (F at the cupboard).", reward = 100, target = ShopTarget2, done = () => Career.Has && HomeLab.I && HomeLab.I.MaxBatches(Career.RecipeIndex) >= 2, cheat = () => { foreach (var (id, n, _) in Career.FirstBatch[C()]) Give(id, n * 2); }, sal = "sal|One batch at a time is a hobby. Buy enough for two." });
            Quests.Add(new Quest { title = "I. A proper run", descF = () => $"Make and collect two more batches at your {StName()} ({(emp ? Mathf.Min(2, emp.Batches - qBase) : 0)}/2).", tip = "Batch size x2 on the station panel makes both at once.", reward = 200, begin = () => { qBase = emp ? emp.Batches : 0; }, target = StationPos, done = () => emp && emp.Batches - qBase >= 2, cheat = () => { MakeBatch(Career.RecipeIndex); MakeBatch(Career.RecipeIndex); }, sal = "sal|Now cook them. Both." });
            Quests.Add(new Quest { title = "I. Move the product", descF = () => $"Make 6 more sales on the street: X next to a civilian (guns: X next to a gang member). {(cm ? Mathf.Min(6, cm.Deals - qBase) : 0)}/6", tip = "Night-time buyers pay more. Different people say yes: keep asking.", reward = 250, begin = () => { qBase = cm ? cm.Deals : 0; }, done = () => cm && cm.Deals - qBase >= 6, cheat = () => { DoDeals(6); }, sal = "sal|Product in a cupboard is worth nothing. Six sales, then we talk." });
            Quests.Add(new Quest { title = "I. Earn respect", desc = "Reach the rank of Hustler (K ▸ Status).", reward = 250, done = () => Gang.Respect.Rank >= 1, sal = "People are starting to know your face. Keep going: sell, steal, fight. Respect opens doors." });
            Quests.Add(new Quest { title = "I. Faster feet", desc = "Buy a skateboard, a scooter or a bicycle at the Market and ride it (use it from the backpack, I).", tip = "Bicycle is the fastest, then the scooter, then the skateboard. A ride fits in your backpack.", reward = 100, target = () => Nearest("market"), done = () => eco.Count("Kaykay") + eco.Count("Skuter") + eco.Count("Bisiklet") > 0 || (vi && vi.Current && vi.Current.light), cheat = () => { Give("Kaykay", 1); }, sal = "sal|You can't outrun trouble on foot. The Market sells wheels." });
            // ACT II - THE CREW: other people start working for you.
            Quests.Add(new Quest { title = "II. Hire muscle", desc = "Recruit a civilian into your crew: walk up, press F.", reward = 200, done = () => cm && cm.Recruited >= 1, sal = "You can't do this alone. Pick someone with a spine and put a few dollars in their hand." });
            Quests.Add(new Quest { title = "II. Arm your people", desc = "Give a crew member a weapon: K ▸ Crew ▸ click their name ▸ ARM.", reward = 200, target = () => Door("gun"), done = () => cm && cm.ArmedCount >= 1, sal = "An unarmed crew is a funeral procession. The gun shop sells the cheap stuff, but I'll sell it to your boys at a fair price." });
            Quests.Add(new Quest { title = "II. Pay Sal", desc = "Pay back $2000 of Uncle Enzo's debt: K ▸ Supplies.", reward = 300, done = () => cm && cm.DebtPaid >= 2000, sal = "Show me you're good for it. Two grand and I'll start trusting you with bigger things." });
            Quests.Add(new Quest { title = "II. Hot corner", descF = () => WantedSystem.PoliceOff ? "A neighbour talked. The police are switched off (debug panel), so just go home and lie low inside your house." : "A neighbour talked. Get a wanted level, then lose the police.", reward = 250, done = () => { var w = WantedSystem.I; if (WantedSystem.PoliceOff) return InHouse(); if (!w) return false; if (w.Level > 0) heatSeen = true; return heatSeen && w.Level == 0; }, sal = "Somebody tipped the cops. If they find you, break line of sight and wait it out. You'll know the drill." });
            Quests.Add(new Quest { title = "II. Make them pay", desc = "Lean on a shop owner for protection money, or buy one (X at the counter, with armed crew nearby).", reward = 400, done = () => cm && (cm.Racketed + cm.Bought) >= 1, sal = "Shopkeepers pay for peace, or for the right to keep their windows. Bring your armed boys to a small shop and make them an offer." });
            Quests.Add(new Quest { title = "II. Corner boy", desc = "Put one of your crew on a corner as a dealer: K ▸ Crew ▸ Dealer. He sells your stock every shift and keeps 30%.", tip = "Stand where you want him to work when you press Dealer. He sells from your backpack and from the cupboard at home.", reward = 200, done = () => cm && cm.DealerCount >= 1, cheat = () => { Recruit(1); foreach (var m in cm.crew) if (m) { m.SetRole(Gang.CrewBrain.Role.Dealer); break; } }, sal = "sal|You're still selling it yourself? That's what the boys are for." });
            Quests.Add(new Quest { title = "II. Money while you sleep", descF = () => $"Let your dealers bring in $200 (${(cm ? Mathf.Min(200, cm.DealerCash - qBase) : 0)}/200). They sell up to 4 units each per shift.", tip = "A shift is 6 game hours. Sleeping passes shifts. Keep product in the cupboard.", reward = 300, begin = () => { qBase = cm ? cm.DealerCash : 0; }, done = () => cm && cm.DealerCash - qBase >= 200, cheat = () => { Recruit(1); bool any = false; foreach (var m in cm.crew) if (m && m.role == Gang.CrewBrain.Role.Dealer) any = true; if (!any) foreach (var m in cm.crew) if (m) { m.SetRole(Gang.CrewBrain.Role.Dealer); break; } for (int k = 0; k < 12 && cm.DealerCash - qBase < 200; k++) { Give(Sellable(), 4); emp.ForceShift(); } }, sal = "sal|Good. Now let him earn. Two hundred dollars without you lifting a finger." });
            Quests.Add(new Quest { title = "II. A second line", desc = "Own a second kind of station at home: buy another kit (still and bench at the Gun Shop, lab at the Market) and place it with B.", tip = "Each station opens its own recipes. They all share the cupboard.", reward = 300, target = KitTarget, done = () => HomeLab.I && Kinds() >= 2, cheat = () => { AddKind((C() + 1) % 3); }, sal = "sal|One product, one customer, one problem away from broke. Build a second line." });
            Quests.Add(new Quest { title = "II. Something new", desc = "Make and collect a batch of a recipe you have never made before.", tip = "Recipes unlock with rank (K ▸ Status). Whiskey, gin and pills open at Hustler.", reward = 300, target = () => gm.homePosition, done = () => emp && emp.RecipesMade >= 2, cheat = () => { RankTo(1); NewRecipe(); if (emp.RecipesMade < 2) NewRecipe(); }, sal = "sal|Same old stuff every day. Give the street something it hasn't tasted." });
            Quests.Add(new Quest { title = "II. Steady hands", descF = () => $"Finish a bench job with a quality of 85 or better (your best: {(emp ? emp.BestQuality : 0)}).", tip = "Carry the bottle or the bag over the target, then scroll DOWN to pour drop by drop. Careful work pays.", reward = 300, target = StationPos, done = () => emp && emp.BestQuality >= 85, cheat = () => { Gang.Empire.NoteQuality(92f); }, sal = "sal|Sloppy work sells once. Good work sells forever." });
            // ACT III - THE HOUSE: guards, workers and a runner, so the business runs without you.
            Quests.Add(new Quest { title = "III. A man at the door", desc = "Hire a guard for your house: K ▸ Home ▸ Hire a guard.", tip = "Guards stand at your front door. They are paid every shift.", reward = 300, target = () => gm.homePosition, done = () => emp && emp.Guards >= 1, cheat = () => { Cash(Gang.Empire.GuardCost(emp.Guards) + 100); emp.HireGuard(); }, sal = "sal|People know where you live and what's in that cupboard. Put a man on the door. Today." });
            Quests.Add(new Quest { title = "III. The knock", desc = "Somebody is coming for your cupboard at the next shift. Keep at least one guard on the door and let the night pass.", tip = "K ▸ Home shows your guards. A shift is 6 game hours: sleeping gets you there.", reward = 400, begin = () => { qBase = emp ? emp.RaidsRepelled : 0; if (emp) emp.RaidDue = true; }, target = () => gm.homePosition, done = () => emp && emp.RaidsRepelled - qBase >= 1, cheat = () => { if (emp.Guards < 1) { Cash(Gang.Empire.GuardCost(emp.Guards) + 100); emp.HireGuard(); } emp.RaidDue = true; emp.ForceShift(); }, sal = "sal|I hear the Blue Scorpions are asking about your house. Tonight, I think." });
            Quests.Add(new Quest { title = "III. Hired hands", desc = "Hire a worker for one of your stations: K ▸ Home ▸ Station workers.", tip = "A worker makes one batch every shift from the cupboard and puts the product back in it.", reward = 300, target = () => gm.homePosition, done = () => emp && emp.WorkerCount >= 1, cheat = () => { Cash(Gang.Empire.WorkerCost + 100); emp.HireWorker(C()); }, sal = "sal|You're the boss and you still stir the pot yourself? Hire a pair of hands." });
            Quests.Add(new Quest { title = "III. Standing order", descF = () => $"Let your worker finish two batches ({(emp ? Mathf.Min(2, emp.WorkerBatches - qBase) : 0)}/2). Keep the cupboard stocked.", tip = "Choose what he makes under K ▸ Home ▸ Standing order. He needs the ingredients in the cupboard or your backpack.", reward = 400, begin = () => { qBase = emp ? emp.WorkerBatches : 0; }, target = () => gm.homePosition, done = () => emp && emp.WorkerBatches - qBase >= 2, cheat = () => { if (emp.WorkerCount < 1) { Cash(Gang.Empire.WorkerCost + 100); emp.HireWorker(C()); } for (int k = 0; k < 2; k++) { StockFor(C()); emp.ForceShift(); } }, sal = "sal|Keep his cupboard full and he keeps your pockets full." });
            Quests.Add(new Quest { title = "III. The errand boy", desc = "Hire a runner: K ▸ Home ▸ Runner. He buys whatever your workers are short of.", tip = "He charges the shop price plus 20%, straight from your cash.", reward = 300, target = () => gm.homePosition, done = () => emp && emp.Runner, cheat = () => { Cash(Gang.Empire.RunnerCost + 100); emp.HireRunner(); }, sal = "sal|Still carrying sugar sacks home yourself? Pay a kid to do it." });
            Quests.Add(new Quest { title = "III. Hands off", descF = () => $"Earn $1,000 without doing it yourself: dealers, venues and turf (${(emp ? Mathf.Min(1000, emp.AutoIncome - qBase) : 0)}/1000).", tip = "Workers make it, the runner stocks it, dealers sell it, venues and turf pay tribute.", reward = 600, begin = () => { qBase = emp ? emp.AutoIncome : 0; }, done = () => emp && emp.AutoIncome - qBase >= 1000, cheat = () => { Recruit(1); foreach (var m in cm.crew) if (m) { m.SetRole(Gang.CrewBrain.Role.Dealer); break; } for (int k = 0; k < 20 && emp.AutoIncome - qBase < 1000; k++) { Give(Sellable(), 4); emp.ForceShift(); } }, sal = "sal|A real boss earns while he eats dinner. A thousand dollars, and you touch nothing." });
            Quests.Add(new Quest { title = "III. Make a name", desc = "Reach the rank of Enforcer and take out 3 rival gang members.", reward = 500, target = Moretti, done = () => Gang.Respect.Rank >= 2 && crewKills >= 3, sal = "The families have noticed you. The Morettis in the west are the closest. Show them what you can do." });
            Quests.Add(new Quest { title = "III. Full house", desc = "Have 2 guards on the door and workers on 2 stations (K ▸ Home).", tip = "More turf means bigger raids: a raid needs one guard, plus one for every two blocks you hold.", reward = 500, target = () => gm.homePosition, done = () => emp && emp.Guards >= 2 && emp.WorkerCount >= 2, cheat = () => { AddKind((C() + 1) % 3); while (emp.Guards < 2) { Cash(2000); if (!emp.HireGuard()) break; } for (int k = 0; k < 3 && emp.WorkerCount < 2; k++) { Cash(2000); if (HomeLab.I.CountStations(k) > 0) emp.HireWorker(k); } }, sal = "sal|One guard sleeps. Two guards watch each other. Same with workers." });
            Quests.Add(new Quest { title = "III. Three trades", desc = "Own all three stations at home: the copper still, the chemistry table and the gunsmith bench.", tip = "Kits: still and bench at the Gun Shop, lab at the Market. Place with B.", reward = 600, target = KitTarget, done = () => HomeLab.I && Kinds() >= 3, cheat = () => { for (int k = 0; k < 3; k++) AddKind(k); }, sal = "sal|Booze, powder and iron. Whoever sells all three owns the night." });
            Quests.Add(new Quest { title = "III. Your turf", desc = "Run 3 venues (protected or owned). Check K ▸ Venues.", reward = 800, done = () => cm && (cm.ProtectedCount + cm.OwnedCount) >= 3, sal = "Three shops paying you each shift. That's a business. That's a territory." });
            Quests.Add(new Quest { title = "III. Clear the books", desc = "Finish paying Sal back: $6000 total (K ▸ Supplies).", reward = 1000, done = () => cm && cm.DebtPaid >= Gang.CrewManager.Debt, sal = "Every dollar, and your uncle can rest. Then we talk about the city." });
            // ACT IV - WAR: take the blocks from the gangs and the two families that killed yours.
            Quests.Add(new Quest { title = "IV. Pick a fight", desc = "Go into a gang's turf and start a takeover: K ▸ Turf ▸ Start the takeover. You need to be an Enforcer.", tip = "Gangs fight with bats, the two families carry pistols. Start with a gang and bring your armed crew.", reward = 400, begin = () => { qBase = emp ? emp.WarsStarted : 0; }, target = WarTarget, done = () => emp && (emp.War != null || emp.WarsStarted - qBase >= 1 || emp.ZonesOwned >= 1), cheat = () => { RankTo(2); emp.StartWar(emp.NearestTarget(), true); }, sal = "sal|The debt is paid. You owe me nothing, so here's some free advice: the people who shot your family still own this town, block by block. Start small." });
            Quests.Add(new Quest { title = "IV. Break them", descF = () => emp && emp.War != null ? $"Take down {emp.WarNeed} of the {emp.War.name} ({emp.WarKills}/{emp.WarNeed})." : "Your takeover collapsed. Go back into their turf and start it again (K ▸ Turf).", tip = "Your followers fight with you. If you go down, the takeover fails and you start again.", reward = 600, target = WarTarget, done = () => emp && (emp.ZonesOwned >= 1 || (emp.War != null && emp.WarPhase >= 1)), cheat = () => { if (emp.War == null && emp.ZonesOwned == 0) emp.StartWar(emp.NearestTarget(), true); if (emp.War != null) emp.DebugKill(emp.War.id, emp.WarNeed); }, sal = "sal|Now finish what you started." });
            Quests.Add(new Quest { title = "IV. Plant the flag", descF = () => emp && emp.War != null ? (emp.WarPhase >= 1 ? $"Hold the {emp.War.name} HQ for {Gang.Empire.HoldNeed} seconds ({emp.WarHold:0}/{Gang.Empire.HoldNeed})." : $"Break their numbers first ({emp.WarKills}/{emp.WarNeed}), then hold their HQ.") : "Take your first block: start a takeover (K ▸ Turf), break them, then hold their HQ.", tip = "Stand within 15 m of the HQ door. The timer only runs while none of them is near you.", reward = 1000, target = WarTarget, done = () => emp && emp.ZonesOwned >= 1, cheat = () => { if (emp.ZonesOwned == 0) { if (emp.War == null) emp.StartWar(emp.NearestTarget(), true); emp.DebugHold(); } }, sal = "sal|They're running. Stand on their doorstep until the street understands who owns it now." });
            Quests.Add(new Quest { title = "IV. Boots on the ground", descF = () => $"Station 2 soldiers on your turf: K ▸ Turf ▸ Station a soldier ({(emp ? emp.GarrisonTotal : 0)}/2).", tip = "Each soldier adds $40 tribute and fights for the block when the old owners come back.", reward = 500, done = () => emp && emp.GarrisonTotal >= 2, cheat = () => { DoGarrison(2); }, sal = "sal|An empty street is an invitation. Put soldiers on your block." });
            Quests.Add(new Quest { title = "IV. They come back", desc = "The old owners will hit one of your blocks at the next shift. Go there and kill them, or have a garrison at least as big as what is left of them.", tip = "K ▸ Turf shows the attack. It is settled when the next shift starts.", reward = 800, begin = () => { qBase = emp ? emp.Defended : 0; if (emp) emp.CounterDue = true; }, target = AttackTarget, done = () => emp && emp.Defended - qBase >= 1, cheat = () => { EnsureZone(); emp.CounterDue = true; emp.ForceShift(); if (emp.AttackZone != null) emp.DebugKill(emp.AttackZone.id, 99, true); emp.ForceShift(); }, sal = "sal|You took their home. Did you think they'd send flowers?" });
            Quests.Add(new Quest { title = "IV. Tribute", descF = () => $"Collect $500 of turf tribute (${(emp ? Mathf.Min(500, emp.Tribute - qBase) : 0)}/500).", tip = "Tribute comes in every shift for every block you hold. Families pay more than gangs.", reward = 500, begin = () => { qBase = emp ? emp.Tribute : 0; }, done = () => emp && emp.Tribute - qBase >= 500, cheat = () => { for (int k = 0; k < 10 && emp.Tribute - qBase < 500; k++) { if (emp.AttackZone != null) emp.DebugKill(emp.AttackZone.id, 99, true); EnsureZone(); emp.ForceShift(); } }, sal = "sal|Now let the block pay you, like it paid them." });
            Quests.Add(new Quest { title = "IV. Arms race", descF = () => $"Have 3 armed crew members ({(cm ? cm.ArmedCount : 0)}/3).", tip = "K ▸ Crew ▸ click a name ▸ ARM. Your own gunsmith bench makes them cheaper than Sal sells them.", reward = 600, done = () => cm && cm.ArmedCount >= 3, cheat = () => { RankTo(2); Recruit(3); foreach (var m in cm.crew) if (m && !m.Armed) { Cash(500); cm.Arm(m, WeaponKind.Pistol); } }, sal = "sal|The families carry pistols. Your boys carry what, opinions?" });
            Quests.Add(new Quest { title = "IV. Second front", descF = () => $"Hold 2 blocks at the same time ({(emp ? emp.ZonesOwned : 0)}/2).", tip = "Finish one war before you start the next. Keep a garrison behind you.", reward = 1200, target = WarTarget, done = () => emp && emp.ZonesOwned >= 2, cheat = () => { RankTo(2); for (int k = 0; k < 3 && emp.ZonesOwned < 2; k++) emp.DebugCapture(emp.NearestTarget()); }, sal = "sal|One block is a corner. Two is a neighbourhood." });
            Quests.Add(new Quest { title = "IV. Capo", desc = "Reach the rank of Capo (K ▸ Status).", tip = "Respect comes from sales, batches, venues, wars won and attacks beaten off.", reward = 800, done = () => Gang.Respect.Rank >= 3, cheat = () => { RankTo(3); }, sal = "sal|They've started calling you Capo behind your back. Make it official." });
            Quests.Add(new Quest { title = "IV. Buy the block", descF = () => $"Own 2 venues outright ({(cm ? cm.OwnedCount : 0)}/2).", tip = "X at a shop counter, or K ▸ Venues while you stand there. Owned venues pay more than protected ones.", reward = 800, done = () => cm && cm.OwnedCount >= 2, cheat = () => { RankTo(1); BuyVenues(2); }, sal = "sal|Dirty money needs clean walls. Buy some." });
            Quests.Add(new Quest { title = "IV. The family falls", desc = "Take the block of one of the two families: Moretti (west) or Kaplan (east).", tip = "They carry pistols and they shoot first. Armed crew, body armour, and don't stand in the open.", reward = 2000, target = () => FamilyTarget(), done = () => emp && emp.OwnsKind(FactionKind.Mafia), cheat = () => { RankTo(2); emp.DebugCapture(mor != null && !Gang.Empire.Owns(mor.id) ? mor : kap); }, sal = "sal|It's time. One of the two families that put you in that coma. Take their house away from them." });
            Quests.Add(new Quest { title = "IV. One side of town", desc = "Hold all three blocks on one side of the city (west or east).", tip = "The map (M) shows your blocks in gold.", reward = 2500, target = WarTarget, done = () => emp && emp.OwnsAnyBloc(), cheat = () => { RankTo(2); string side = mor != null && Gang.Empire.Owns(mor.id) ? "west" : "east"; foreach (var f in FactionDefs.All) if (f.bloc == side) emp.DebugCapture(f); }, sal = "sal|Half the city, kid. Half. Finish the side you started." });
            // ACT V - THE CITY
            Quests.Add(new Quest { title = "V. War chest", descF = () => $"Have $25,000 in cash (${Mathf.Min(25000, eco.cash)}/25000).", tip = "Tribute, venues, dealers and workers all pay every shift.", reward = 1000, done = () => eco.cash >= 25000, cheat = () => { Cash(25000); }, sal = "sal|The other side of town is watching you. Wars cost money. Fill the chest first." });
            Quests.Add(new Quest { title = "V. Underboss", desc = "Reach the rank of Underboss.", tip = "Every block taken is worth a lot of respect.", reward = 1500, done = () => Gang.Respect.Rank >= 4, cheat = () => { RankTo(4); }, sal = "sal|There's only one chair above yours now." });
            Quests.Add(new Quest { title = "V. Port City", descF = () => $"Hold every block in the city ({(emp ? emp.ZonesOwned : 0)}/{FactionDefs.All.Count}).", tip = "The last blocks are the hardest. Garrison what you hold before you go.", reward = 5000, target = WarTarget, done = () => emp && emp.ZonesOwned >= FactionDefs.All.Count, cheat = () => { RankTo(2); foreach (var f in FactionDefs.All) emp.DebugCapture(f); }, sal = "sal|Finish it. Every block. Then nobody ever does to another family what they did to yours." });
            Quests.Add(new Quest { title = "V. The Don", desc = "Reach Capo, hold at least 4 crew members and 3 venues. Then go home: the city is waiting for you there.", reward = 2500, target = () => gm.homePosition, done = () => cm && Gang.Respect.Rank >= 3 && cm.crew.Count >= 4 && (cm.ProtectedCount + cm.OwnedCount) >= 3 && pc && Vector3.Distance(pc.transform.position, gm.homePosition) < 10f, sal = "Come home. The family wants to meet you." });
            // ---- test shortcuts for the missions that came from the old chain
            void SetCheat(string title, Action a) { foreach (var q in Quests) if (q.title == title) { q.cheat = a; return; } Debug.LogWarning("QuestManager: no mission called " + title); }
            SetCheat("0. Ask the restaurant", () => AskedCashier = true);
            SetCheat("0. Ask around", () => AskedGuest = true);
            SetCheat("0. Settle the score", () => { Career.StashTaken = true; Cash(600); });
            SetCheat("0. Choose your trade", () => { if (!Career.Has) Career.Choose(Career.Drugs); CareerPanel.I?.Close(); });
            SetCheat("0. Shopping run", () => { Give(Career.Kit, 1); foreach (var (id, n, _) in Career.FirstBatch[C()]) Give(id, n); });
            SetCheat("0. Set up at home", () => AddKind(C()));
            SetCheat("0. First batch", () => { var lab = HomeLab.I; int r = Career.RecipeIndex; foreach (var (id, n) in HomeLab.Recipes[r].inputs) Give(id, n); lab.StartBatch(r, 0.8f); });
            SetCheat("0. Let it cook", () => HomeLab.I.CollectNow(Career.RecipeIndex));
            SetCheat("0. Collect the product", () => MakeBatch(Career.RecipeIndex));
            SetCheat("0. Three years of news", () => PhonePanel.DebugOpen(true));
            SetCheat("0. Pay your respects", () => { TeleHome(true); if (DayNightCycle.I) DayNightCycle.I.timeOfDay = 20.5f; });
            SetCheat("I. A message from Sal", () => PhonePanel.DebugOpen(true));
            SetCheat("I. Wheels", () => { if (!vi) return; VehicleController best = null; float bd = 1e12f; foreach (var v in FindObjectsByType<VehicleController>(FindObjectsSortMode.None)) { if (!v || !v.light) continue; float d = (v.transform.position - pc.transform.position).sqrMagnitude; if (d < bd) { bd = d; best = v; } } if (best) vi.Enter(best); });
            SetCheat("I. First sales", () => DoDeals(3));
            SetCheat("I. Earn respect", () => RankTo(1));
            SetCheat("II. Hire muscle", () => { int before = cm.Recruited; Recruit(cm.crew.Count + 1); if (cm.Recruited == before) Debug.LogWarning("QuestManager: nobody could be recruited"); });
            SetCheat("II. Arm your people", () => { Recruit(1); foreach (var m in cm.crew) if (m) { Cash(600); cm.Arm(m, WeaponKind.Pistol); break; } });
            SetCheat("II. Pay Sal", () => { int need = 2000 - cm.DebtPaid; if (need > 0) { Cash(need + 100); cm.PayDebt(need); } });
            SetCheat("II. Hot corner", () => { if (WantedSystem.PoliceOff) TeleHome(true); else if (WantedSystem.I) { WantedSystem.I.Add(1, "mission test"); heatSeen = true; WantedSystem.I.Clear(); } });
            SetCheat("II. Make them pay", () => { RankTo(1); BuyVenues(cm.OwnedCount + 1); });
            SetCheat("III. Make a name", () => { RankTo(2); if (!emp) return; int left = 3; foreach (var z in FactionDefs.All) { if (left <= 0) break; if (Gang.Empire.Owns(z.id)) continue; left -= emp.DebugKill(z.id, left); } });
            SetCheat("III. Your turf", () => { RankTo(1); BuyVenues(cm.OwnedCount + Mathf.Max(0, 3 - cm.ProtectedCount - cm.OwnedCount)); });
            SetCheat("III. Clear the books", () => { int need = Gang.CrewManager.Debt - cm.DebtPaid; if (need > 0) { Cash(need + 100); cm.PayDebt(need); } });
            SetCheat("V. The Don", () => { RankTo(3); Recruit(4); BuyVenues(cm.OwnedCount + Mathf.Max(0, 3 - cm.ProtectedCount - cm.OwnedCount)); TeleHome(false); });
            // a save from the old 24-mission chain continues at the same mission in the new one
            if (!PlayerPrefs.HasKey(Key) && PlayerPrefs.HasKey(OldKey))
            {
                int oi = PlayerPrefs.GetInt(OldKey, 0), ni = oi >= OldTitles.Length ? Quests.Count - 1 : 0;
                if (oi >= 0 && oi < OldTitles.Length) for (int i = 0; i < Quests.Count; i++) if (Quests[i].title == OldTitles[oi]) { ni = i; break; }
                PlayerPrefs.SetInt(Key, ni); PlayerPrefs.Save();
            }
            Index = Mathf.Clamp(PlayerPrefs.GetInt(Key, 0), 0, Quests.Count);
        }

        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player) return; pc = gm.player;
            if (IntroCinematic.Playing || Prologue.Active || CharacterCreator.IsOpen) return;
            if (!started) { started = true; Build(); startT = Time.time; }
            if (Time.time - startT < 6f) return;   // let the welcome text land first
            if (doneFlash > 0f) doneFlash -= Time.deltaTime;
            if (AllDone) { SetBeacon(null); CurrentTarget = null; Coach = null; if (Waypoints.I) Waypoints.I.QuestTarget = null; return; }
            var q = Quests[Index];
            if (!introPosted) { introPosted = true; if (Index == 0 && !Career.StashTaken) PhonePanel.Post("jeremy", "Joshua is the one who sold your family out. Nobody has seen him in daylight. Start at the Restaurant and ask around, I\u2019ll text you when you know more. Check the map (M)."); }
            TendJoshua();
            if (beganIdx != Index) { beganIdx = Index; q.begin?.Invoke(); }
            if (q.done != null && q.done())
            {
                var eco = EconomyManager.I; if (eco) eco.cash += q.reward;
                gm.Banner("MISSION COMPLETE", $"{q.title}   +${q.reward}", 3f); gm.Say($"Mission complete: {q.title} (+${q.reward})");
                doneText = q.title; doneFlash = 3f;
                Index++; if (!frozen) { PlayerPrefs.SetInt(Key, Index); PlayerPrefs.Save(); } heatSeen = false; crewKills = 0;
                if (AllDone) { gm.Banner("THE CITY IS YOURS", "You're somebody in Port City now.", 5f); PhonePanel.Post("sal", $"{PlayerAppearance.PlayerName}, you did well. There's more work coming. Stay sharp."); }
                else { var n = Quests[Index]; if (!string.IsNullOrEmpty(n.sal)) { int bar = n.sal.IndexOf('|'); if (bar > 0) PhonePanel.Post(n.sal.Substring(0, bar), n.sal.Substring(bar + 1)); else PhonePanel.Post("sal", n.sal); } }
            }
            CurrentTarget = q.target != null ? q.target() : null; Coach = TipOf(q);
            if (Waypoints.I) Waypoints.I.QuestTarget = CurrentTarget;
            SetBeacon(null);   // the target is shown on the maps only, not in the world
        }


        // ------------------------------------------------------------------ admin: skip a mission / run the whole chain
        public bool Testing { get; private set; }
        bool frozen;   // after a self test nothing more is written to the save
        public string TestSummary { get; private set; } = "";
        /// <summary>Admin: jump over the current mission (no reward).</summary>
        public void SkipCurrent() { if (!started || AllDone) return; Index++; PlayerPrefs.SetInt(Key, Index); PlayerPrefs.Save(); heatSeen = false; crewKills = 0; GameManager.I?.Say("Mission skipped (admin)."); }
        /// <summary>Admin: jump to a mission by number (1-based).</summary>
        public void JumpTo(int number) { if (!started) return; Index = Mathf.Clamp(number - 1, 0, Quests.Count); beganIdx = -1; PlayerPrefs.SetInt(Key, Index); PlayerPrefs.Save(); heatSeen = false; crewKills = 0; }
        public void RunSelfTest() { if (started && !Testing) StartCoroutine(SelfTest()); }
        // the test plays on a copy: every save key is put back afterwards, so the player's own game is untouched
        static readonly string[] SaveKeys = { "ms_career_v2", "ms_collects_v2", "ms_debt_paid_v1", "ms_empire_v1", "ms_joshua_v1", "ms_lab_v1", "ms_look_v1", "ms_pins_v1", "ms_police_off_v1", "ms_quest_idx_v6", "ms_quest_idx_v7", "ms_save_cash_v1", "ms_save_day_v1", "ms_save_v1", "ms_stash_v1", "ms_stations_v2", "ms_xp_v1" };
        readonly List<(string k, bool has, string s, int i, float f)> snap = new List<(string, bool, string, int, float)>();
        void OnApplicationQuit() { if (frozen) Restore(); }
        void Snapshot() { snap.Clear(); foreach (var k in SaveKeys) snap.Add((k, PlayerPrefs.HasKey(k), PlayerPrefs.GetString(k, "\u0001"), PlayerPrefs.GetInt(k, int.MinValue), PlayerPrefs.GetFloat(k, float.NaN))); }
        void Restore()
        {
            foreach (var e in snap) { PlayerPrefs.DeleteKey(e.k); if (!e.has) continue; if (e.s != "\u0001") PlayerPrefs.SetString(e.k, e.s); else if (e.i != int.MinValue) PlayerPrefs.SetInt(e.k, e.i); else if (!float.IsNaN(e.f)) PlayerPrefs.SetFloat(e.k, e.f); }
            PlayerPrefs.Save();
        }
        /// <summary>
        /// Plays the chain from the current mission to the end: for every mission it reads the text and the marker, checks the goal is not
        /// met yet, performs the goal through the game's own systems (buying, recruiting, cooking, hiring, capturing ...) and waits for the
        /// mission to complete by itself. The result goes to MissionTest.log next to the Assets folder.
        /// </summary>
        System.Collections.IEnumerator SelfTest()
        {
            Testing = true; Snapshot(); var sb = new System.Text.StringBuilder(); int pass = 0, fail = 0, first = Index, seen = Index; var vi = pc ? pc.GetComponent<VehicleInteraction>() : null;
            sb.AppendLine($"Mission self test, started at mission {Index + 1} of {Quests.Count}");
            while (!AllDone)
            {
                for (int f = 0; f < 3; f++) yield return null;   // let Update call begin()
                if (AllDone) break; int i = Index; var q = Quests[i];
                for (int j = seen; j < i; j++) { pass++; sb.AppendLine($"PASS  {j + 1:00}  {Quests[j].title}  (completed at once: its goal was already met by the step before)"); }
                bool pre = false; string err = null;
                try { pre = q.done != null && q.done(); } catch (Exception e) { err = " done() threw " + e.GetType().Name + ": " + e.Message; }
                try { if (q.target != null) q.target(); if (string.IsNullOrEmpty(DescOf(q))) err = (err ?? "") + " empty description"; TipOf(q); } catch (Exception e) { err = (err ?? "") + " text/marker threw " + e.GetType().Name + ": " + e.Message; }
                if (!pre && q.cheat != null) { try { q.cheat(); } catch (Exception e) { err = (err ?? "") + " action threw " + e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]; } }
                float t = 0f; while (Index == i && t < 6f) { t += Time.unscaledDeltaTime; yield return null; }
                bool ok = Index > i && err == null;
                sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {i + 1:00}  {q.title}{(pre ? "  (goal was already met)" : "")}{(q.cheat == null ? "  (no action)" : "")}{(Index == i ? "  goal never became true" : "")}{err}");
                if (ok) pass++; else { fail++; if (Index == i) { Index++; PlayerPrefs.SetInt(Key, Index); PlayerPrefs.Save(); } }
                seen = Index;
                PhonePanel.DebugOpen(false); CareerPanel.I?.Close(); if (vi && vi.Current) vi.Exit();
                yield return null;
            }
            for (int j = seen; j < Quests.Count; j++) { pass++; sb.AppendLine($"PASS  {j + 1:00}  {Quests[j].title}  (completed at once: its goal was already met by the step before)"); }
            var emp = Gang.Empire.I; var cm = Gang.CrewManager.I; var eco = EconomyManager.I;
            sb.AppendLine($"RESULT: {pass} passed, {fail} failed, missions {first + 1}..{Quests.Count}");
            if (emp && cm && eco) sb.AppendLine($"End state: cash ${eco.cash}, rank {Gang.Respect.RankName}, crew {cm.crew.Count} (armed {cm.ArmedCount}, dealers {cm.DealerCount}), venues {cm.ProtectedCount + cm.OwnedCount}, guards {emp.Guards}, workers {emp.WorkerCount}, runner {emp.Runner}, blocks {emp.ZonesOwned}, garrison {emp.GarrisonTotal}, worker batches {emp.WorkerBatches}, raids beaten {emp.RaidsRepelled}, attacks beaten {emp.Defended}, tribute ${emp.Tribute}");
            TestSummary = $"{pass} passed, {fail} failed"; Testing = false; frozen = true;
            Gang.Empire.Wipe(); Restore(); sb.AppendLine("The save from before the test was put back. Stop and start Play to continue your own game.");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "..", "MissionTest.log"), sb.ToString()); } catch (Exception e) { Debug.LogWarning("MissionTest.log: " + e.Message); }
            Debug.Log(sb.ToString()); GameManager.I?.Banner("MISSION TEST", TestSummary + "  ·  your own save is put back, the game stops now", 8f);
            // the world keeps running after the test and would write the test's state over the save again: put it back once more and stop
            yield return new WaitForSecondsRealtime(4f); Restore();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        void SetBeacon(Vector3? p)
        {
            if (!p.HasValue) { if (beacon) beacon.SetActive(false); return; }
            if (!beacon)
            {
                beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder); beacon.name = "QuestBeacon"; Destroy(beacon.GetComponent<Collider>());
                var mat = new Material(Shader.Find("Sprites/Default")); mat.color = new Color(1f, 0.78f, 0.15f, 0.35f); beacon.GetComponent<Renderer>().material = mat;
                beacon.transform.localScale = new Vector3(0.9f, 30f, 0.9f);
            }
            beacon.SetActive(true); beacon.transform.position = p.Value + Vector3.up * 30f;
        }

        /// <summary>For the phone's Missions app: (title, description, state 0=done 1=active 2=locked).</summary>
        public List<(string title, string desc, int state)> View()
        {
            var l = new List<(string, string, int)>();
            for (int i = 0; i < Quests.Count; i++) l.Add((Quests[i].title, DescOf(Quests[i]), i < Index ? 0 : i == Index ? 1 : 2));
            return l;
        }

        public static float PanelBottom = 124f;   // lowest edge of the mission panel (other HUD boxes stack below it)
        void OnGUI()
        {
            PanelBottom = 124f;
            if (MafiaSandbox.Economy.Workshop.Active) return;
            if (!started || IntroCinematic.Playing || Prologue.Active || CharacterCreator.IsOpen || !pc) return;
            float W = Screen.width;
            if (AllDone) return;
            var q = Quests[Index];
            const float PW = 384f, IN = PW - 34f;
            var wrap = new GUIStyle(UiSkin.Label) { wordWrap = true, fontSize = 14 }; wrap.normal.textColor = new Color(0.82f, 0.85f, 0.92f);
            var tipSt = new GUIStyle(wrap) { fontSize = 13 }; tipSt.normal.textColor = new Color(1f, 0.86f, 0.4f);
            var titleSt = new GUIStyle(UiSkin.Title) { fontSize = 20, wordWrap = true };
            string dsc = DescOf(q), tip = TipOf(q);
            float tH = titleSt.CalcHeight(new GUIContent(q.title), IN), dh = wrap.CalcHeight(new GUIContent(dsc), IN), th = string.IsNullOrEmpty(tip) ? 0f : tipSt.CalcHeight(new GUIContent(tip), IN - 16f) + 14f;
            var r = new Rect(W - PW - 12, 172, PW, 58f + tH + dh + th + 14f); PanelBottom = r.yMax;
            UiSkin.DrawPanel(r);
            GUI.color = UiSkin.Accent; GUI.DrawTexture(new Rect(r.x + 8, r.y + 12, 4, r.height - 24), UiSkin.White); GUI.color = Color.white;   // accent stripe
            float x = r.x + 22, y = r.y + 10;
            GUI.Label(new Rect(x, y, 220, 18), $"MISSION {Index + 1} / {Quests.Count}", UiSkin.H2);
            UiSkin.DrawBar(new Rect(x, y + 20, IN - 120f, 6), (Index) / (float)Quests.Count, UiSkin.Accent);
            GUI.Label(new Rect(x, y + 30, IN, tH), q.title, titleSt);
            GUI.Label(new Rect(x, y + 34 + tH, IN, dh), dsc, wrap);
            if (th > 0f)
            {
                var tb = new Rect(x - 4, y + 38 + tH + dh, IN + 4, th); GUI.color = new Color(1f, 0.8f, 0.2f, 0.12f); GUI.DrawTexture(tb, UiSkin.White); GUI.color = Color.white;
                GUI.Label(new Rect(tb.x + 8, tb.y + 6, tb.width - 16, th - 8), tip, tipSt);
            }
            var tp = q.target != null ? q.target() : null;
            if (tp.HasValue)
            {
                var cam = Camera.main; Vector3 d = tp.Value - pc.transform.position; d.y = 0f;
                string dir = ""; if (cam) { Vector3 f = cam.transform.forward; f.y = 0f; float ang = Vector3.SignedAngle(f, d, Vector3.up); dir = Mathf.Abs(ang) < 40f ? "ahead" : Mathf.Abs(ang) > 140f ? "behind" : ang > 0f ? "right" : "left"; }
                UiSkin.DrawChip(new Rect(r.xMax - 132, r.y + 8, 120, 24), $"{d.magnitude:0} m · {dir}", UiSkin.Accent);
            }
        }
    }
}
