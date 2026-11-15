using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using MafiaSandbox.Combat;
using MafiaSandbox.Economy;
using MafiaSandbox.Gang;
using MafiaSandbox.Npc;
using MafiaSandbox.UI;
using MafiaSandbox.Vehicles;
using MafiaSandbox.World;

namespace MafiaSandbox.Core
{
    /// <summary>Press End in play mode: exercises the main systems, then runs the sim fast and collects exceptions. Writes smoke_log.txt next to the project.</summary>
    public class SmokeTest : MonoBehaviour
    {
        readonly StringBuilder log = new StringBuilder(); readonly Dictionary<string, int> errs = new Dictionary<string, int>(); bool running; int pass, fail;
        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; }
        void OnLog(string msg, string stack, LogType t)
        {
            if (t != LogType.Exception && t != LogType.Error) return; string key = msg.Split('\n')[0]; if (key.Length > 160) key = key.Substring(0, 160);
            var first = stack.Split('\n'); key += "  @ " + (first.Length > 0 ? first[0] : ""); errs[key] = errs.TryGetValue(key, out int n) ? n + 1 : 1; if (errs.Count <= 8 && n == 0) { log.AppendLine("EXC " + msg + "\n" + stack); Flush(); }
        }
        void Update() { if (Input.GetKeyDown(KeyCode.End) && !running) StartCoroutine(Run()); }
        void Check(string name, bool ok, string extra = "") { extra = extra ?? ""; if (ok) pass++; else fail++; log.AppendLine((ok ? "PASS " : "FAIL ") + name + (extra.Length > 0 ? "  [" + extra + "]" : "")); Flush(); }
        void Flush() { try { File.WriteAllText(Path.Combine(Application.dataPath, "../smoke_log.txt"), log.ToString()); } catch { } }

        IEnumerator Run()
        {
            running = true; Time.timeScale = 1f; var gm = GameManager.I; var eco = EconomyManager.I; var pc = gm.player; var dn = DayNightCycle.I;
            log.AppendLine("SMOKE " + System.DateTime.Now); errs.Clear();
            // 1. components
            Check("CrewManager", CrewManager.I); Check("JobManager", JobManager.I); Check("HomeLab", HomeLab.I); Check("Stash", Stash.I); Check("ProductionPanel", ProductionPanel.I); Check("CrewPanel", CrewPanel.I);
            Check("StashPanel", StashPanel.I); Check("BreakableProps", BreakableProps.I); Check("QuestManager", QuestManager.I); Check("PhonePanel", PhonePanel.I); Check("NightLife", FindAnyObjectByType<NightLife>());
            Check("Police OFF default", WantedSystem.PoliceOff); yield return null;
            var hs = Hospital.I; Check("Hospital built", hs);
            if (hs) { log.AppendLine("  hospital @ " + hs.transform.position + " yaw " + hs.yaw + ", " + Vector3.Distance(hs.transform.position, gm.homePosition).ToString("0") + " m from home");
                foreach (var (nm, pos) in new[] { ("hospital stand", hs.standPos), ("hospital jeremyTalk", hs.jeremyTalk), ("hospital doorOut", hs.doorOut), ("hospital jeremyStart", hs.jeremyStart) })
                    Check(nm + " on navmesh", NavMesh.SamplePosition(pos, out var hn, 1.5f, NavMesh.AllAreas), pos.ToString()); }
            Check("Prologue component", Prologue.I);
            // 2. home props placement
            var lab = HomeLab.I; Check("HomeLab built", lab && lab.Ready);
            if (lab && lab.Ready)
                foreach (var (nm, pos) in new[] { ("Workbench", lab.LabPos), ("Bed", lab.BedPos), ("Stash", Stash.I.Pos) })
                {
                    var hits = Physics.OverlapBox(pos + Vector3.up * 0.8f, new Vector3(0.4f, 0.5f, 0.4f)); var names = new List<string>(); foreach (var h in hits) if (!h.transform.IsChildOf(pc.transform)) names.Add(h.name);
                    Check(nm + " inside home footprint", BuildingInfo.At(pos) == eco.homeShop.info, "fp " + eco.homeShop.info.footprint.center + " " + eco.homeShop.info.footprint.size); Check(nm + " not inside colliders", names.Count == 0, string.Join(",", names));
                    bool nav = NavMesh.SamplePosition(pos, out var nh, 1.8f, NavMesh.AllAreas); Check(nm + " reachable on navmesh", nav, nav ? "" : "no navmesh within 1.8m");
                }
            log.AppendLine("  home door/counter: " + (eco.homeShop != null ? eco.homeShop.info.door + " / " + eco.homeShop.info.counter : "none") + " lab " + lab.LabPos + " bed " + lab.BedPos + " stash " + Stash.I.Pos + " player " + pc.transform.position);
            // 3. production
            Respect.Add(6000); eco.cash += 5000;
            for (int r = 0; r < HomeLab.Recipes.Length; r++)
            {
                var rc = HomeLab.Recipes[r]; foreach (var (id, n) in rc.inputs) eco.AddItem(id, n, false);
                Check("recipe " + rc.name + " can start", lab.CanStart(r) == null, lab.CanStart(r));
                int before = eco.Count(rc.product); lab.StartBatch(r, 1f); Check("recipe " + rc.name + " cooking", lab.batches[r].state == 1);
            }
            float hrs = dn.TotalHours; dn.SkipTo((dn.timeOfDay + 8f) % 24f); yield return null; yield return null;
            for (int r = 0; r < HomeLab.Recipes.Length; r++) { var rc = HomeLab.Recipes[r]; Check("batch ready " + rc.name, lab.batches[r].state == 2); int b = eco.Count(rc.product); lab.Collect(r); Check("collected " + rc.name, eco.Count(rc.product) > b); }
            // 4. stash
            var inv0 = eco.inv.Find(i => i.id == "Weed"); if (inv0 != null) { int q = inv0.qty; Stash.I.Put(inv0, 2); Check("stash put", Stash.I.items.Exists(i => i.id == "Weed")); var s = Stash.I.items.Find(i => i.id == "Weed"); Stash.I.Take(s, 2); Check("stash take", eco.Count("Weed") >= q); }
            // 5. dealing
            var cm = CrewManager.I; int d0 = cm.Deals; int tried = 0; foreach (var cv in FindObjectsByType<CivilianBrain>(FindObjectsSortMode.None)) { if (cv.isMedic) continue; cm.Deal(cv); yield return null; if (cm.Deals > d0 || ++tried >= 15) break; } Check("deal sells something (15 civilians)", cm.Deals > d0, "tried " + tried);
            // 6. crew: recruit, arm, roles
            int recruited = 0; foreach (var c in FindObjectsByType<CivilianBrain>(FindObjectsSortMode.None)) { if (c.isMedic) continue; for (int t = 0; t < 6 && cm.crew.Count < 3; t++) if (cm.TryRecruit(c)) { recruited++; break; } if (cm.crew.Count >= 3) break; }
            Check("recruit works", cm.crew.Count > 0, "crew " + cm.crew.Count); yield return null;
            if (cm.crew.Count > 0) { var m = cm.crew[0]; Check("arm crew", cm.Arm(m, WeaponKind.Pistol)); m.SetRole(CrewBrain.Role.Dealer); Check("role dealer", m.role == CrewBrain.Role.Dealer); eco.AddItem("Weed", 8, false); }
            // 7. venues
            int bought = 0; foreach (var s in eco.shops) if (CrewManager.Eligible(s) && cm.Mode(s) == CrewManager.VenueMode.None && bought < 3) { if (cm.Buy(s)) bought++; }
            Check("buy venues", bought == 3, "bought " + bought); float before2 = eco.cash; dn.SkipTo((dn.timeOfDay + 13f) % 24f); yield return null; yield return null; Check("shift payout adds cash", eco.cash > before2, $"{before2}->{eco.cash}");
            // 8. jobs
            var jm = JobManager.I; foreach (var j in new List<JobManager.Job>(jm.Offers)) { bool ok = jm.Accept(j); Check("job accept " + j.type, ok && jm.Active != null); yield return null; jm.Abandon(); }
            // 9. quests
            var q2 = QuestManager.I; Check("quest view", q2.View().Count > 5, "count " + q2.View().Count);
            // 10. wanted off
            WantedSystem.I.Add(3, "test"); Check("police off: no stars", WantedSystem.I.Level == 0);
            // 11. night population
            dn.timeOfDay = 2f; yield return new WaitForSeconds(6f); int inactive = 0, total = 0; foreach (var c in FindObjectsByType<CivilianBrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { total++; if (!c.gameObject.activeSelf) inactive++; }
            Check("night hides pedestrians", inactive > total * 0.3f, $"{inactive}/{total} hidden"); 
            // 12. sleep
            dn.timeOfDay = 22f; var sh = Vector3.Distance(pc.transform.position, lab.BedPos); lab.TrySleep(); Check("sleep starts", HomeLab.Sleeping); yield return new WaitForSeconds(7f); Check("sleep ends at morning", !HomeLab.Sleeping && Mathf.Abs(dn.timeOfDay - 7f) < 1f, "t=" + dn.timeOfDay);
            // 13. breakables + vehicles
            Check("vehicles registered", VehicleController.All.Count > 3, "n=" + VehicleController.All.Count);
            var f = typeof(BreakableProps).GetField("map", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); int mapped = f != null ? ((System.Collections.IDictionary)f.GetValue(BreakableProps.I)).Count : -1; Check("breakable colliders mapped", mapped > 20, "n=" + mapped);
            // 14. run the sim fast, collect exceptions
            Flush(); dn.timeOfDay = 12f; Time.timeScale = 6f; float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 45f) yield return null; Time.timeScale = 1f;
            foreach (var kv in errs) log.AppendLine("ERR x" + kv.Value + "  " + kv.Key);
            log.AppendLine($"DONE pass={pass} fail={fail} errors={errs.Count}"); Flush(); gm.Say($"Smoke test: pass {pass} / fail {fail} / errors {errs.Count}"); running = false;
        }
    }
}
