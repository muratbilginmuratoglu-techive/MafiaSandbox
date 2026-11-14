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
        public class Quest { public string title, desc, sal; public int reward; public Func<Vector3?> target; public Func<bool> done; }
        public readonly List<Quest> Quests = new List<Quest>();
        public int Index { get; private set; }
        public bool AllDone => Index >= Quests.Count;
        const string Key = "ms_quest_idx_v2";

        int crewKills; bool heatSeen, started, phoneSeen; float doneFlash; string doneText; GameObject beacon; PlayerController pc; float startT = -1f;

        void Awake() { I = this; }
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
            // ACT I - STREET RAT: your uncle Enzo died owing Sal $6000. Sal fronts you product so you can work it off.
            Quests.Add(new Quest { title = "I. A message from Sal", desc = "Press T to open your phone.", reward = 50, done = () => PhonePanel.IsOpen, sal = null });
            Quests.Add(new Quest { title = "I. Wheels", desc = "Get behind the wheel of any car (walk up, press E).", reward = 100, done = () => vi && vi.Current, sal = "Your uncle Enzo left you his debt: $6000. I'm a reasonable man. Work it off. First: get yourself some wheels." });
            Quests.Add(new Quest { title = "I. The starter pack", desc = "Order weed from Sal: K ▸ Supplies. Have 3 baggies in your backpack.", reward = 100, done = () => cm && cm.Stock(0) >= 3 || (cm && cm.Deals >= 1), sal = "Here's the deal: I front you product, you sell it on the street. Open your organisation menu (K) and order some weed." });
            Quests.Add(new Quest { title = "I. First sales", desc = "Sell 3 baggies: walk up to a civilian and press X. Do it where cops can't see.", reward = 150, done = () => cm && cm.Deals >= 3, sal = "Walk up to people, press X. Watch for cops. Witnesses talk, but only a cop who sees you can arrest you." });
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
            if (IntroCinematic.Playing || CharacterCreator.IsOpen) return;
            if (!started) { started = true; Build(); startT = Time.time; }
            if (Time.time - startT < 6f) return;   // let the welcome text land first
            if (doneFlash > 0f) doneFlash -= Time.deltaTime;
            if (AllDone) { SetBeacon(null); return; }
            var q = Quests[Index];
            if (q.done != null && q.done())
            {
                var eco = EconomyManager.I; if (eco) eco.cash += q.reward;
                gm.Banner("MISSION COMPLETE", $"{q.title}   +${q.reward}", 3f); gm.Say($"Mission complete: {q.title} (+${q.reward})");
                doneText = q.title; doneFlash = 3f;
                Index++; PlayerPrefs.SetInt(Key, Index); PlayerPrefs.Save(); heatSeen = false; crewKills = 0;
                if (AllDone) { gm.Banner("THE CITY IS YOURS", "You're somebody in Port City now.", 5f); PhonePanel.Post("sal", $"{PlayerAppearance.PlayerName}, you did well. There's more work coming. Stay sharp."); }
                else { var n = Quests[Index]; if (!string.IsNullOrEmpty(n.sal)) PhonePanel.Post("sal", n.sal); }
            }
            SetBeacon(q.target != null ? q.target() : null);
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
            for (int i = 0; i < Quests.Count; i++) l.Add((Quests[i].title, Quests[i].desc, i < Index ? 0 : i == Index ? 1 : 2));
            return l;
        }

        void OnGUI()
        {
            if (!started || IntroCinematic.Playing || CharacterCreator.IsOpen || !pc) return;
            float W = Screen.width;
            if (AllDone) return;
            var q = Quests[Index]; var r = new Rect(W - 330, 112, 318, 92);
            UiSkin.DrawPanel(r);
            GUI.Label(new Rect(r.x + 12, r.y + 6, r.width - 24, 20), $"MISSION {Index + 1}/{Quests.Count}", UiSkin.H2);
            GUI.Label(new Rect(r.x + 12, r.y + 22, r.width - 24, 22), "<b>" + q.title + "</b>", UiSkin.Label);
            var wrap = new GUIStyle(UiSkin.LabelDim) { wordWrap = true, fontSize = 12 };
            GUI.Label(new Rect(r.x + 12, r.y + 44, r.width - 24, 34), q.desc, wrap);
            var tp = q.target != null ? q.target() : null;
            if (tp.HasValue)
            {
                var cam = Camera.main; Vector3 d = tp.Value - pc.transform.position; d.y = 0f;
                string dir = ""; if (cam) { Vector3 f = cam.transform.forward; f.y = 0f; float ang = Vector3.SignedAngle(f, d, Vector3.up); dir = Mathf.Abs(ang) < 40f ? "ahead" : Mathf.Abs(ang) > 140f ? "behind you" : ang > 0f ? "to your right" : "to your left"; }
                UiSkin.DrawChip(new Rect(r.xMax - 150, r.y + 4, 140, 20), $"{d.magnitude:0} m  ·  {dir}", UiSkin.Accent);
            }
        }
    }
}
