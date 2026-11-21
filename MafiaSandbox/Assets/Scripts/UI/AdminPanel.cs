using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Gang;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// Admin / test mode (P): unlimited ingredients, instant batches, every recipe unlocked, and a button per product that opens
    /// its workshop job right where you stand. Also stocks the cupboard, hands out cash and sets up the three stations at home.
    /// </summary>
    public class AdminPanel : MonoBehaviour
    {
        public static AdminPanel I { get; private set; }
        public static bool IsOpen => I && I.open;
        bool open; Vector2 scroll;
        void Awake() { I = this; }

        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player) { open = false; return; }
            bool key = Input.GetKeyDown(KeyCode.P); bool debugOpen = DebugPanel.I && DebugPanel.I.Open;
            if (key && !debugOpen && !Workshop.Active && !CharacterCreator.IsOpen && !IntroCinematic.Playing && !HomeLab.Sleeping && !PhonePanel.IsOpen && !CrewPanel.IsOpen && !StashPanel.IsOpen && !(GameUI.I && GameUI.I.MapOpen) && !(ProductionPanel.I && ProductionPanel.I.PanelOpen))
            {
                open = !open;
                // P = admin mode: everything can be made straight away (unlimited ingredients, no waiting, every recipe)
                if (open && !Admin.On) { Admin.On = Admin.Unlimited = Admin.Instant = Admin.UnlockAll = true; gm.Say("Admin mode on: unlimited ingredients, instant batches, every recipe unlocked."); }
            }
            if (open && Input.GetKeyDown(KeyCode.Escape)) open = false;
        }

        static void Stock(string id, int n)
        {
            var st = Stash.I; if (!st) { EconomyManager.I?.AddItem(id, n, false); return; }
            var it = st.items.Find(x => x.id == id && !x.stolen); if (it != null) it.qty = Mathf.Max(it.qty, n); else st.items.Add(new InvItem { id = id, qty = n, stolen = false }); st.SavePublic();
        }


        /// <summary>(Re)places the still, the chemistry table and the gunsmith bench in free corners of the home: clear of the cupboard, the bed, the door, the shelves and the furniture.</summary>
        static void PlaceStations(HomeLab lab, World.BuildingInfo bi)
        {
            var gm = GameManager.I; while (lab.stations.Count > 0) lab.RemoveStation(0);
            var avoid = new System.Collections.Generic.List<Vector3>(); if (Stash.I) avoid.Add(Stash.I.Pos); avoid.Add(lab.BedPos); avoid.Add(bi.door); if (bi.shelves != null) avoid.AddRange(bi.shelves);
            var f = bi.footprint; float y = lab.BedPos.y; Vector3 centre = new Vector3(f.center.x, y, f.center.z); int placed = 0;
            for (int k = 0; k < 3; k++)
            {
                float best = 0f; Vector3 bp = Vector3.zero; bool found = false;
                for (float x = f.min.x + 1.2f; x <= f.max.x - 1.2f; x += 0.5f) for (float z = f.min.z + 1.2f; z <= f.max.z - 1.2f; z += 0.5f)
                {
                    var p = new Vector3(x, y, z); float d = float.MaxValue; foreach (var a in avoid) { Vector3 q = a - p; q.y = 0f; d = Mathf.Min(d, q.magnitude); }
                    if (d < 1.9f || d <= best) continue;
                    if (Physics.CheckBox(p + Vector3.up * 0.7f, new Vector3(0.8f, 0.45f, 0.5f), Quaternion.identity, 1 << 0, QueryTriggerInteraction.Ignore)) continue;
                    best = d; bp = p; found = true;
                }
                if (!found) break;
                Vector3 look = centre - bp; lab.AddStation(k, bp, Mathf.Abs(look.x) > Mathf.Abs(look.z) ? (look.x > 0f ? 90f : -90f) : (look.z > 0f ? 0f : 180f)); avoid.Add(bp); placed++;
            }
            gm?.Say(placed == 3 ? "The still, the chemistry table and the gunsmith bench are set up at home." : $"Only {placed} station(s) fitted in the house.");
        }

        /// <summary>Places one station of the given kind in the freest corner of the home (clear of the cupboard, the bed, the door and the other stations).</summary>
        public static bool PlaceOne(HomeLab lab, World.BuildingInfo bi, int kind)
        {
            var avoid = new System.Collections.Generic.List<Vector3>(); if (Stash.I) avoid.Add(Stash.I.Pos); avoid.Add(lab.BedPos); avoid.Add(bi.door); if (bi.shelves != null) avoid.AddRange(bi.shelves); foreach (var st in lab.stations) avoid.Add(st.pos);
            var f = bi.footprint; float y = lab.BedPos.y; Vector3 centre = new Vector3(f.center.x, y, f.center.z); float best = 0f; Vector3 bp = Vector3.zero; bool found = false;
            for (int pass = 0; pass < 2 && !found; pass++)
                for (float x = f.min.x + 1.2f; x <= f.max.x - 1.2f; x += 0.5f) for (float z = f.min.z + 1.2f; z <= f.max.z - 1.2f; z += 0.5f)
                {
                    var p = new Vector3(x, y, z); float d = float.MaxValue; foreach (var a in avoid) { Vector3 q = a - p; q.y = 0f; d = Mathf.Min(d, q.magnitude); }
                    if (d < (pass == 0 ? 1.9f : 1.3f) || d <= best) continue;
                    if (pass == 0 && Physics.CheckBox(p + Vector3.up * 0.7f, new Vector3(0.8f, 0.45f, 0.5f), Quaternion.identity, 1 << 0, QueryTriggerInteraction.Ignore)) continue;
                    best = d; bp = p; found = true;
                }
            if (!found) return false;
            Vector3 look = centre - bp; lab.AddStation(kind, bp, Mathf.Abs(look.x) > Mathf.Abs(look.z) ? (look.x > 0f ? 90f : -90f) : (look.z > 0f ? 0f : 180f)); return true;
        }

        /// <summary>The person nearest to the player shows every arm pose in turn, 3 seconds each, facing the player.</summary>
        System.Collections.IEnumerator PoseTest()
        {
            var gm = GameManager.I; if (!gm || !gm.player) yield break; Npc.NpcBrain best = null; float bd = 20f * 20f; Vector3 pp = gm.player.transform.position;
            foreach (var n in FindObjectsByType<Npc.NpcBrain>(FindObjectsSortMode.None)) { if (!n || n.Dead || n is Npc.JeremyBrain) continue; float d = (n.transform.position - pp).sqrMagnitude; if (d < bd) { bd = d; best = n; } }
            if (!best) { gm.Say("Nobody close enough for a pose test."); yield break; }
            foreach (Npc.ArmPose p in System.Enum.GetValues(typeof(Npc.ArmPose)))
            {
                if (!best || best.Dead) yield break; best.DebugPose(p, gm.player.transform.position + Vector3.up * 1.5f); gm.Say($"Pose test: {best.displayName} · {p}"); yield return new WaitForSeconds(3f);
            }
            if (best) best.DebugPose(null, null);
        }

        void OnGUI()
        {
            var gm = GameManager.I; if (!gm || !gm.player) return;
            if (Admin.On && !open && !Workshop.Active) { var chip = new Rect(Screen.width / 2f - 150, 36, 300, 22); UiSkin.DrawChip(chip, "ADMIN MODE  ·  P opens the panel", new Color(1f, 0.6f, 0.35f)); }
            if (!open) return; GUI.depth = -40; var lab = HomeLab.I; var eco = EconomyManager.I;
            float W = Screen.width, H = Screen.height; var r = new Rect(W / 2f - 380, 40, 760, Mathf.Min(H - 70, 640)); UiSkin.DrawPanel(r, true);
            GUILayout.BeginArea(new Rect(r.x + 16, r.y + 12, r.width - 32, r.height - 24));
            GUILayout.BeginHorizontal(); GUILayout.Label("Admin mode", UiSkin.Title); GUILayout.FlexibleSpace(); if (GUILayout.Button("Debug panel (teleport, time, weather)", UiSkin.Button) && DebugPanel.I) { open = false; DebugPanel.I.Show(); } GUILayout.Space(10); GUILayout.Label("P / Esc closes", UiSkin.LabelDim); GUILayout.EndHorizontal();
            GUILayout.Label("SWITCHES", UiSkin.H2); GUILayout.BeginHorizontal();
            bool on = GUILayout.Toggle(Admin.On, Admin.On ? "Admin: ON" : "Admin: OFF", UiSkin.Button, GUILayout.Height(34));
            if (on != Admin.On) { Admin.On = Admin.Unlimited = Admin.Instant = Admin.UnlockAll = on; gm.Say(on ? "Admin mode on: unlimited ingredients, instant batches, all recipes." : "Admin mode off."); }
            GUI.enabled = Admin.On;
            Admin.Unlimited = GUILayout.Toggle(Admin.Unlimited, "Unlimited ingredients", UiSkin.Button, GUILayout.Height(34)) && Admin.On;
            Admin.Instant = GUILayout.Toggle(Admin.Instant, "Instant batches", UiSkin.Button, GUILayout.Height(34)) && Admin.On;
            Admin.UnlockAll = GUILayout.Toggle(Admin.UnlockAll, "All recipes unlocked", UiSkin.Button, GUILayout.Height(34)) && Admin.On;
            GUI.enabled = true; GUILayout.EndHorizontal();
            GUILayout.Label("In the workshop, admin mode adds: N = skip the current step, and a Finish now button.", UiSkin.LabelDim);

            GUILayout.Space(6); GUILayout.Label("HOME AND SUPPLIES", UiSkin.H2); GUILayout.BeginHorizontal();
            if (GUILayout.Button("+$5,000", UiSkin.Button) && eco) eco.cash += 5000;
            if (GUILayout.Button("Cupboard: 20 of every ingredient", UiSkin.Button)) { foreach (var kv in Items.All) if (kv.Value.kind == "material") Stock(kv.Key, 20); gm.Say("The cupboard at home is stocked."); }
            if (GUILayout.Button("Backpack: products + rides", UiSkin.Button) && eco) { foreach (var p in CrewManager.Products) eco.AddItem(p, 5, false); foreach (var ride in new[] { "Kaykay", "Skuter", "Bisiklet" }) if (eco.Count(ride) == 0) eco.AddItem(ride, 1, false); gm.Say("Five of every product, plus a skateboard, a scooter and a bicycle, in your backpack."); }
            GUILayout.EndHorizontal(); GUILayout.BeginHorizontal();
            if (GUILayout.Button("Set up the three stations at home", UiSkin.Button) && lab && lab.Ready && eco && eco.homeShop != null) PlaceStations(lab, eco.homeShop.info);
            if (GUILayout.Button("Teleport inside home", UiSkin.Button))
            {
                Vector3 hp = gm.homePosition; float yaw = gm.homeYaw;
                if (Stash.I && lab && lab.Ready) { Vector3 side = Stash.I.Pos - lab.BedPos; side.y = 0f; side.Normalize(); Vector3 doorDir = Vector3.Cross(side, Vector3.up); hp = Stash.I.Pos + doorDir * 1.3f; yaw = Mathf.Atan2(-doorDir.x, -doorDir.z) * Mathf.Rad2Deg; }
                gm.player.Teleport(hp, yaw); if (gm.playerCamera) gm.playerCamera.SetYaw(yaw); open = false;
            }
            if (GUILayout.Button("Time 20:00", UiSkin.Button) && World.DayNightCycle.I) World.DayNightCycle.I.timeOfDay = 20f;
            if (GUILayout.Button("Sleep", UiSkin.Button) && lab) { open = false; lab.TrySleep(); }
            GUILayout.EndHorizontal();

            // missions, staff and turf
            var qm = QuestManager.I; var emp = Empire.I;
            GUILayout.BeginHorizontal(); GUILayout.Label(qm ? $"Mission {Mathf.Min(qm.Index + 1, qm.Quests.Count)}/{qm.Quests.Count}" + (qm.Testing ? "  testing..." : qm.TestSummary.Length > 0 ? "  last test: " + qm.TestSummary : "") : "Missions", UiSkin.LabelDim, GUILayout.Width(250));
            if (GUILayout.Button("Skip mission", UiSkin.Button) && qm) qm.SkipCurrent();
            if (GUILayout.Button("Test all missions", UiSkin.Button) && qm) { open = false; qm.RunSelfTest(); }
            if (GUILayout.Button("Run a shift now", UiSkin.Button) && emp) emp.ForceShift();
            GUILayout.EndHorizontal(); GUILayout.BeginHorizontal();
            if (GUILayout.Button("Raid at next shift", UiSkin.Button) && emp) { emp.RaidDue = true; gm.Say("A raid will hit the house at the next shift."); }
            if (GUILayout.Button("Counterattack at next shift", UiSkin.Button) && emp) { emp.CounterDue = true; gm.Say("One of your blocks will be attacked at the next shift."); }
            if (GUILayout.Button("Teleport to the nearest rival HQ", UiSkin.Button) && emp) { var tgt = emp.NearestTarget(); if (tgt != null) { gm.player.Teleport(tgt.HqUnity + new Vector3(0f, 0f, 6f), 180f); open = false; } }
            if (GUILayout.Button("Pose test", UiSkin.Button)) { open = false; StartCoroutine(PoseTest()); }
            if (GUILayout.Button(Npc.Barks.Reporting ? "NPC report..." : "NPC report (60 s)", UiSkin.Button) && !Npc.Barks.Reporting) { open = false; Npc.Barks.RunReport(60f, 3f); }
            if (GUILayout.Button("Rank 0", UiSkin.Button)) { Respect.Reset(); gm.Say("Respect reset: you are a Street Rat again."); }
            if (GUILayout.Button("Rank up", UiSkin.Button)) { int rk = Respect.Rank; if (rk < Respect.Thresholds.Length - 1) Respect.Add(Respect.Thresholds[rk + 1] - Respect.XP); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUILayout.Label("Station panel (normal rules):", UiSkin.LabelDim, GUILayout.Width(200));
            for (int k = 0; k < 3; k++) if (GUILayout.Button(Career.StationNames[k], UiSkin.Button) && ProductionPanel.I) { open = false; ProductionPanel.I.Open(k); }
            GUILayout.EndHorizontal();
            GUILayout.Space(6); GUILayout.Label("OPEN A WORKSHOP JOB HERE (the product goes straight to your backpack)", UiSkin.H2);
            scroll = GUILayout.BeginScrollView(scroll);
            if (lab) for (int st = 0; st < 3; st++)
            {
                GUILayout.Label(Career.StationNames[st].ToUpperInvariant(), UiSkin.H2);
                foreach (int i in HomeLab.RecipesFor(st))
                {
                    var rc = HomeLab.Recipes[i]; GUILayout.BeginHorizontal();
                    string need = ""; foreach (var (id, n) in rc.inputs) need += (need.Length > 0 ? ", " : "") + n + " " + Items.Get(id).name;
                    GUILayout.Label($"{rc.name}  →  {rc.yield} × {Items.Get(rc.product).name}   ({need})", UiSkin.Label, GUILayout.Width(540));
                    if (GUILayout.Button("Open", UiSkin.ButtonOn, GUILayout.Width(150)))
                    {
                        if (!Admin.On) gm.Say("Turn admin mode on first.");
                        else { if (lab.batches[i].state != 0) lab.CollectNow(i); if (Workshop.I && Workshop.I.Begin(i, 1, true)) open = false; }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
