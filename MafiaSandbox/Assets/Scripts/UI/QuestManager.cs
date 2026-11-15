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
        public class Quest { public string title, desc, sal, tip; public Func<string> descF, tipF; public int reward; public Func<Vector3?> target; public Func<bool> done; public Action begin; }
        public static string DescOf(Quest q) => q.descF != null ? q.descF() : q.desc;
        public static string TipOf(Quest q) => q.tipF != null ? q.tipF() : q.tip;
        /// <summary>Hint text for the current mission (shown in the tracker and on the workstation panel).</summary>
        public string Coach { get; private set; }
        public Vector3? CurrentTarget { get; private set; }
        int beganIdx = -1, dealsBase; bool introPosted; CharacterBody joshua;
        // ---- Joshua: found by asking around (cafe cashier, then a guest); he smokes at the playground every night
        public static bool AskedCashier, AskedGuest;
        Quest qAskCashier, qAskGuest, qPark;
        public bool CashierAskable(Shop sh) => started && qAskCashier != null && !AllDone && Quests[Index] == qAskCashier && !AskedCashier && sh != null && sh.kind == "cafe";
        public bool GuestAskable(Npc.NpcBrain nb)
        {
            if (!started || qAskGuest == null || AllDone || Quests[Index] != qAskGuest || AskedGuest || !(nb is Npc.CamperBrain) || nb.displayName != "Guest") return false;
            var eco = EconomyManager.I; if (!eco) return false; foreach (var s in eco.shops) if (s.info && s.info.kind == "cafe") { var f = s.info.footprint; var p = nb.transform.position; return p.x > f.min.x - 1f && p.x < f.max.x + 1f && p.z > f.min.z - 1f && p.z < f.max.z + 1f; }
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
        const string Key = "ms_quest_idx_v6";

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
            Vector3? Door(string kind) { if (eco) foreach (var s in eco.shops) if (s.info && s.info.kind == kind) return s.info.door; return null; }
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
            Quests.Add(qAskCashier = new Quest { title = "0. Ask the cafe", desc = "Nobody knows where Joshua went. Start at the Cafe: walk up to the counter and ask the cashier (F).", tip = "Gold marker = the Cafe. The cashier stands behind the counter.", reward = 0, target = () => Career.StashTaken ? null : Door("cafe"), done = () => AskedCashier || Career.StashTaken, sal = null });
            Quests.Add(qAskGuest = new Quest { title = "0. Ask around", desc = "The cashier shrugs. Ask one of the people sitting in the Cafe about Joshua (walk up, press F).", tip = "Guests sit at the tables. Try the ones who look like regulars.", reward = 0, target = () => Career.StashTaken ? null : Door("cafe"), done = () => AskedGuest || Career.StashTaken, sal = null });
            Quests.Add(qPark = new Quest { title = "0. Settle the score", descF = () => NightNow ? "Joshua is at the playground, smoking: look for the glowing ember and the gold arrow. Take him down and search the body (F) for the money he was paid." : "Joshua only goes out to the playground at night. Come back after 20:00 (or sleep: F at your bed after 19:30). Then take him down and search the body (F).", tip = "Fists, the knife (4) or the bat (5). If a cop sees it, they will chase you. He runs when he's hurt.", reward = 0, target = () => Career.StashTaken ? null : ParkPos(), done = () => Career.StashTaken, sal = null });
            Quests.Add(new Quest { title = "0. Choose your trade", desc = "A menu asks how you will make a living: bootleg alcohol, drugs or arms. Read the steps and choose one.", tip = "It decides what you buy, what you make at home and who you sell to.", reward = 0, begin = () => CareerPanel.I?.Open(), done = () => Career.Has, sal = "jeremy|Joshua paid for what he did. Now that money has to work. What are you going to do with it? Think hard, it decides everything." });
            Quests.Add(new Quest { title = "0. Shopping run", descF = ShopDesc, tip = "Walk into the shop and press F at the counter. Gold marker = next stop.", reward = 100, target = ShopTarget, done = ShopDone, sal = null });
            Quests.Add(new Quest { title = "0. Set up at home", descF = () => $"Go home. Inside the house press B, walk to where you want your {StName()}, rotate with Q/E and press F to place it.", tip = "Leave space around it. It can't go near the bed, door, stash or furniture. Press B next to it later to move it.", reward = 100, target = () => gm.homePosition, done = () => Career.Has && HomeLab.I && HomeLab.I.CountStations(C()) > 0, sal = "jeremy|Good. Now bring it all home and put it where you can work. Press B inside the house." });
            Quests.Add(new Quest { title = "0. First batch", descF = () => $"Walk up to your {StName()} and press F. Pick the recipe, check the ingredients, press Start and play the little game. Better work means more product.", tip = "Step 1: the recipe is selected. Step 2: ingredients must be full (2/2 etc). Step 3: press Start and play the puzzle.", reward = 50, target = StationPos, done = () => Career.Has && HomeLab.I && (HomeLab.I.batches[Career.RecipeIndex].state >= 1 || Career.Collects >= 1), sal = "jeremy|Time to learn the craft. Don't rush it, a good batch is worth three bad ones." });
            Quests.Add(new Quest { title = "0. Let it cook", descF = () => "A batch takes game hours. Sleep in your bed to skip time (inside the house, press F at the bed after 19:30) or just wait until the READY message.", tip = "Sleeping finishes everything that is cooking. You can also wander off and come back.", reward = 50, target = () => { var l = HomeLab.I; if (l && HomeLab.CanSleepNow) return l.BedPos; return StationPos(); }, done = () => Career.Has && HomeLab.I && (HomeLab.I.batches[Career.RecipeIndex].state == 2 || Career.Collects >= 1), sal = null });
            Quests.Add(new Quest { title = "0. Collect the product", descF = () => $"Go back to your {StName()}, press F and press Collect. The product lands in your backpack.", tip = "You will be able to sell it on the street later. Open your backpack with I.", reward = 150, target = StationPos, done = () => Career.Collects >= 1, sal = "jeremy|That's the whole loop: buy, make, wait, collect. Now do it again, but bigger." });
            Quests.Add(new Quest { title = "0. Three years of news", desc = "Press T and check Jeremy's message about the war.", reward = 25, done = () => PhonePanel.IsOpen, sal = "jeremy|You want to know who did it? Two families, Morettis and Kaplans, shot up the whole street. Nobody was arrested. Sal knows more. He is the only one who talks." });
            Quests.Add(new Quest { title = "0. Pay your respects", desc = "Be at the ruined house after dark (20:00 to 05:00).", reward = 50, target = JerSpot, done = () => { var d = DayNightCycle.I; return InHouse() && d && (d.timeOfDay >= 20f || d.timeOfDay < 5f); }, sal = "jeremy|Go home after dark and spend a quiet minute with them. Then call Sal, he is waiting." });
            // ACT I - STREET RAT: your uncle Enzo died owing Sal $6000. Sal fronts you product so you can work it off.
            Quests.Add(new Quest { title = "I. A message from Sal", desc = "Press T to open your phone.", reward = 50, done = () => PhonePanel.IsOpen, sal = "sal|Heard you woke up. Good. We should talk, kid. Check your phone." });
            Quests.Add(new Quest { title = "I. Wheels", desc = "Get behind the wheel of any car (walk up, press E).", reward = 100, done = () => vi && vi.Current, sal = "Your uncle Enzo left you his debt: $6000. I'm a reasonable man. Work it off. First: get yourself some wheels." });
            Quests.Add(new Quest { title = "I. First sales", descF = () => Career.Chosen == Career.Arms ? "Sell 3 pistols to armed gang members: find a gang member (red dot on the minimap), walk up and press X." : "Sell 3 units of what you made: walk up to a civilian and press X. Do it where cops can't see.", tip = "Check your backpack (I) for stock. Witnesses talk, but only a cop who sees you can arrest you.", reward = 150, begin = () => dealsBase = cm ? cm.Deals : 0, done = () => cm && cm.Deals - dealsBase >= 3, sal = "sal|Heard you're cooking at home. Good, no cut for me. Now move it: sell it on the street. Watch for cops." });
            Quests.Add(new Quest { title = "I. Earn respect", desc = "Reach the rank of Hustler (K ▸ Status).", reward = 250, done = () => Gang.Respect.Rank >= 1, sal = "People are starting to know your face. Keep going: sell, steal, fight. Respect opens doors." });
            // ACT II - THE CREW
            Quests.Add(new Quest { title = "II. Hire muscle", desc = "Recruit a civilian into your crew: walk up, press F.", reward = 200, done = () => cm && cm.Recruited >= 1, sal = "You can't do this alone. Pick someone with a spine and put a few dollars in their hand." });
            Quests.Add(new Quest { title = "II. Arm your people", desc = "Give a crew member a weapon: K ▸ Crew ▸ click their name ▸ ARM.", reward = 200, target = () => Door("gun"), done = () => cm && cm.ArmedCount >= 1, sal = "An unarmed crew is a funeral procession. The gun shop sells the cheap stuff, but I'll sell it to your boys at a fair price." });
            Quests.Add(new Quest { title = "II. Pay Sal", desc = "Pay back $2000 of Uncle Enzo's debt: K ▸ Supplies.", reward = 300, done = () => cm && cm.DebtPaid >= 2000, sal = "Show me you're good for it. Two grand and I'll start trusting you with bigger things." });
            Quests.Add(new Quest { title = "II. Hot corner", desc = "A neighbour talked. Get a wanted level, then lose the police.", reward = 250, done = () => { var w = WantedSystem.I; if (!w) return false; if (w.Level > 0) heatSeen = true; return heatSeen && w.Level == 0; }, sal = "Somebody tipped the cops. If they find you, break line of sight and wait it out. You'll know the drill." });
            Quests.Add(new Quest { title = "II. Make them pay", desc = "Lean on a shop owner for protection money, or buy one (X at the counter, with armed crew nearby).", reward = 400, done = () => cm && (cm.Racketed + cm.Bought) >= 1, sal = "Shopkeepers pay for peace, or for the right to keep their windows. Bring your armed boys to a small shop and make them an offer." });
            // ACT III - TURF
            Quests.Add(new Quest { title = "III. Make a name", desc = "Reach the rank of Enforcer and take out 3 rival gang members.", reward = 500, target = Moretti, done = () => Gang.Respect.Rank >= 2 && crewKills >= 3, sal = "The families have noticed you. The Morettis in the west are the closest. Show them what you can do." });
            Quests.Add(new Quest { title = "III. Your turf", desc = "Run 3 venues (protected or owned). Check K ▸ Venues.", reward = 800, done = () => cm && (cm.ProtectedCount + cm.OwnedCount) >= 3, sal = "Three shops paying you each shift. That's a business. That's a territory." });
            Quests.Add(new Quest { title = "III. Clear the books", desc = "Finish paying Sal back: $6000 total (K ▸ Supplies).", reward = 1000, done = () => cm && cm.DebtPaid >= Gang.CrewManager.Debt, sal = "Every dollar, and your uncle can rest. Then we talk about the city." });
            Quests.Add(new Quest { title = "III. The Don", desc = "Reach Capo, hold at least 4 crew members and 3 venues. Then go home.", reward = 2500, target = () => gm.homePosition, done = () => cm && Gang.Respect.Rank >= 3 && cm.crew.Count >= 4 && (cm.ProtectedCount + cm.OwnedCount) >= 3 && pc && Vector3.Distance(pc.transform.position, gm.homePosition) < 10f, sal = "Come home. The family wants to meet you." });
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
            if (!introPosted) { introPosted = true; if (Index == 0 && !Career.StashTaken) PhonePanel.Post("jeremy", "Joshua is the one who sold your family out. Nobody has seen him in daylight. Start at the Cafe and ask around, I\u2019ll text you when you know more. Check the map (M)."); }
            TendJoshua();
            if (beganIdx != Index) { beganIdx = Index; q.begin?.Invoke(); }
            if (q.done != null && q.done())
            {
                var eco = EconomyManager.I; if (eco) eco.cash += q.reward;
                gm.Banner("MISSION COMPLETE", $"{q.title}   +${q.reward}", 3f); gm.Say($"Mission complete: {q.title} (+${q.reward})");
                doneText = q.title; doneFlash = 3f;
                Index++; PlayerPrefs.SetInt(Key, Index); PlayerPrefs.Save(); heatSeen = false; crewKills = 0;
                if (AllDone) { gm.Banner("THE CITY IS YOURS", "You're somebody in Port City now.", 5f); PhonePanel.Post("sal", $"{PlayerAppearance.PlayerName}, you did well. There's more work coming. Stay sharp."); }
                else { var n = Quests[Index]; if (!string.IsNullOrEmpty(n.sal)) { int bar = n.sal.IndexOf('|'); if (bar > 0) PhonePanel.Post(n.sal.Substring(0, bar), n.sal.Substring(bar + 1)); else PhonePanel.Post("sal", n.sal); } }
            }
            CurrentTarget = q.target != null ? q.target() : null; Coach = TipOf(q);
            if (Waypoints.I) Waypoints.I.QuestTarget = CurrentTarget;
            SetBeacon(null);   // the target is shown on the maps only, not in the world
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
            GUI.Label(new Rect(x, y, 150, 18), $"MISSION {Index + 1} / {Quests.Count}", UiSkin.H2);
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
