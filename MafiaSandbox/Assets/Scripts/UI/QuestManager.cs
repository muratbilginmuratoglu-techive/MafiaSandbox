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
    /// Chapter 1: an 8-step mission chain (phone → car → gun shop → cash → the family → crew fights → shake the heat → home).
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
        const string Key = "ms_quest_idx_v1";

        int crewKills; bool heatSeen, started, phoneSeen; float doneFlash; string doneText; GameObject beacon; PlayerController pc; float startT = -1f;

        void Awake() { I = this; }
        void OnEnable() { WorldEvents.OnCharacterDied += OnDied; }
        void OnDisable() { WorldEvents.OnCharacterDied -= OnDied; }
        void OnDied(CharacterBody b, string by) { if (by == "player" && b && b.GetComponent<FactionBrain>()) crewKills++; }

        void Build()
        {
            var gm = GameManager.I; var eco = EconomyManager.I;
            Vector3? Door(string kind) { if (eco) foreach (var s in eco.shops) if (s.info && s.info.kind == kind) return s.info.door; return null; }
            bool Inside(string kind) { if (!eco || !pc) return false; foreach (var s in eco.shops) if (s.info && s.info.kind == kind) { var f = s.info.footprint; var p = pc.transform.position; return p.x > f.min.x && p.x < f.max.x && p.z > f.min.z && p.z < f.max.z; } return false; }
            FactionDef mor = FactionDefs.Get("moretti"), kap = FactionDefs.Get("kaplan");
            Vector3? NearestHq() { if (mor == null || kap == null || !pc) return null; var p = pc.transform.position; return (mor.HqUnity - p).sqrMagnitude < (kap.HqUnity - p).sqrMagnitude ? mor.HqUnity : kap.HqUnity; }
            bool NearHq() { if (!pc) return false; var p = pc.transform.position; foreach (var f in new[] { mor, kap }) if (f != null && Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(f.HqUnity.x, 0, f.HqUnity.z)) < 14f) return true; return false; }
            var vi = pc.GetComponent<VehicleInteraction>();
            Quests.Add(new Quest { title = "Check your phone", desc = "Press T to open your phone.", reward = 50, done = () => PhonePanel.IsOpen, sal = null });
            Quests.Add(new Quest { title = "Wheels", desc = "Get behind the wheel of any car (walk up, press E).", reward = 100, done = () => vi && vi.Current, sal = "First rule of this town: never walk when you can drive. Borrow something with wheels." });
            Quests.Add(new Quest { title = "Gear up", desc = "Visit the Gun Shop.", reward = 100, target = () => Door("gun"), done = () => Inside("gun"), sal = "You'll want protection. The gun shop is marked on your map — go have a look." });
            Quests.Add(new Quest { title = "Pocket money", desc = "Get your cash up to $1000: rob a register, loot, or fence stolen goods at the pawn shop.", reward = 150, target = () => Door("pawn"), done = () => eco && eco.cash >= 1000, sal = "Nobody respects an empty wallet. Get your cash to a grand. The pawn shop buys no questions." });
            Quests.Add(new Quest { title = "Meet the family", desc = "Pay a visit to a crime family: the Moretti (west) or Kaplan (east) HQ.", reward = 200, target = NearestHq, done = NearHq, sal = "The Morettis are west, the Kaplans east. Show your face at one of their restaurants — politely." });
            Quests.Add(new Quest { title = "Prove yourself", desc = "Take out 2 gang members, any crew.", reward = 300, done = () => crewKills >= 2, sal = "Respect is earned. Two crew members down and people will remember your name." });
            Quests.Add(new Quest { title = "Shake the heat", desc = "Get a wanted level, then lose the police.", reward = 250, done = () => { var w = WantedSystem.I; if (!w) return false; if (w.Level > 0) heatSeen = true; return heatSeen && w.Level == 0; }, sal = "If the law notices you, don't panic. Break line of sight and wait it out. Prove you can." });
            Quests.Add(new Quest { title = "Home sweet home", desc = "Go home.", reward = 500, target = () => gm.homePosition, done = () => pc && Vector3.Distance(pc.transform.position, gm.homePosition) < 8f, sal = "Good work. Get back to your place — I'll have something for you." });
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
                if (AllDone) { gm.Banner("CHAPTER 1 COMPLETE", "You're somebody in Port City now.", 5f); PhonePanel.Post("sal", $"{PlayerAppearance.PlayerName}, you did well. There's more work coming. Stay sharp."); }
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
