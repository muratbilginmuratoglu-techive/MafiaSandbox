using System.Collections.Generic;
using System.Text;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Factions;
using MafiaSandbox.Npc;
using MafiaSandbox.UI;
using MafiaSandbox.World;

namespace MafiaSandbox.Gang
{
    /// <summary>Marks a hired hand: 0 = home guard, 1 = station worker (id = station kind), 2 = turf soldier (id = zone), 3 = counterattacker (id = zone).</summary>
    public class StaffTag : MonoBehaviour { public int kind; public string id; }

    /// <summary>
    /// The part of the organisation that runs without you.
    ///  - Home staff: guards at the door (they beat off raids), a worker per station (one batch every shift from the cupboard)
    ///    and a runner who restocks the cupboard, so product keeps coming while you are out.
    ///  - Turf wars: start a takeover inside a rival's turf, break their numbers, hold their HQ and the block is yours. It pays tribute
    ///    every shift, needs a garrison, and the old owners come back for it.
    /// Everything here is saved, together with the crew, the venues and the mission counters.
    /// </summary>
    public class Empire : MonoBehaviour
    {
        public static Empire I { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (I) return; var go = new GameObject("Empire"); DontDestroyOnLoad(go); go.AddComponent<Empire>(); }

        const string Key = "ms_empire_v1";
        public const int MaxGuards = 4, MaxGarrison = 4, GuardWage = 45, WorkerWage = 60, RunnerWage = 50, SoldierWage = 40, WorkerCost = 700, RunnerCost = 900, SoldierCost = 400, HoldNeed = 20;
        public static int GuardCost(int have) => 500 + 250 * have;

        // ---- home staff
        public int Guards; public readonly bool[] Worker = new bool[3]; public readonly int[] Order = { -1, -1, -1 }; public bool Runner;
        public int WorkerCount { get { int n = 0; foreach (var w in Worker) if (w) n++; return n; } }
        // ---- counters for missions
        public int WorkerBatches, RaidsRepelled, RaidsLost, AutoIncome, Tribute, Batches, RecipeMask, BestQuality, WarsStarted, WarsWon, Defended, ZonesLost;
        public int RecipesMade { get { int n = 0; for (int i = 0; i < 32; i++) if ((RecipeMask & (1 << i)) != 0) n++; return n; } }
        // ---- turf
        readonly HashSet<string> owned = new HashSet<string>(); readonly Dictionary<string, int> garrison = new Dictionary<string, int>();
        public FactionDef War { get; private set; }
        public int WarKills, WarNeed, WarPhase; public float WarHold, WarTime;
        public FactionDef AttackZone { get; private set; }
        readonly List<CharacterBody> attackers = new List<CharacterBody>();
        public bool RaidDue, CounterDue;
        public string LastReport = "";

        readonly List<CrewBrain> guardNpcs = new List<CrewBrain>(); readonly CrewBrain[] workerNpcs = new CrewBrain[3];
        readonly Dictionary<string, List<CrewBrain>> soldierNpcs = new Dictionary<string, List<CrewBrain>>();
        int lastShift = -1; float tendT, saveT, alertT; bool loaded, frozen; string lastSaved = ""; string pendingVenues = "", pendingCrew = "";
        Texture2D dot;

        void Awake() { I = this; dot = new Texture2D(1, 1); dot.SetPixel(0, 0, Color.white); dot.Apply(); }
        void OnEnable() { WorldEvents.OnCharacterDied += OnDied; }
        void OnDisable() { WorldEvents.OnCharacterDied -= OnDied; }

        // ================================================================================================ queries
        public static bool Owns(string zoneId) => I && zoneId != null && I.owned.Contains(zoneId);
        public int ZonesOwned => owned.Count;
        public int Garrison(string zoneId) => garrison.TryGetValue(zoneId, out var n) ? n : 0;
        public int GarrisonTotal { get { int n = 0; foreach (var g in garrison) n += g.Value; return n; } }
        public bool OwnsKind(FactionKind k) { foreach (var f in FactionDefs.All) if (f.kind == k && owned.Contains(f.id)) return true; return false; }
        public bool OwnsBloc(string bloc) { int n = 0, all = 0; foreach (var f in FactionDefs.All) if (f.bloc == bloc) { all++; if (owned.Contains(f.id)) n++; } return all > 0 && n == all; }
        public bool OwnsAnyBloc() { foreach (var f in FactionDefs.All) if (OwnsBloc(f.bloc)) return true; return false; }
        public static int Income(FactionDef f) => (f.kind == FactionKind.Mafia ? 420 : 280) + (I ? 40 * I.Garrison(f.id) : 0);
        public int WagesPerShift => Guards * GuardWage + WorkerCount * WorkerWage + (Runner ? RunnerWage : 0) + GarrisonTotal * SoldierWage;
        public int AttackersLeft { get { int n = 0; foreach (var a in attackers) if (a && !a.Dead) n++; return n; } }
        int LiveMembers(FactionDef f) { int n = 0; foreach (var b in CharacterBody.All) { if (!b || b.Dead) continue; var fb = b.GetComponent<FactionBrain>(); if (fb && fb.factionId == f.id) n++; } return n; }
        public FactionDef ZoneHere() { var gm = GameManager.I; return gm && gm.player ? FactionDefs.At(gm.player.transform.position) : null; }
        /// <summary>The nearest zone that is not yours yet (gangs first unless anyKind).</summary>
        public FactionDef NearestTarget(bool gangsFirst = true)
        {
            var gm = GameManager.I; if (!gm || !gm.player) return null; FactionDef best = null; float bd = 1e12f; Vector3 p = gm.player.transform.position;
            for (int pass = 0; pass < 2 && best == null; pass++)
                foreach (var f in FactionDefs.All) { if (owned.Contains(f.id)) continue; if (pass == 0 && gangsFirst && f.kind != FactionKind.Gang) continue; float d = (f.HqUnity - p).sqrMagnitude; if (d < bd) { bd = d; best = f; } }
            return best;
        }

        // ================================================================================================ hiring
        static bool Pay(int cost, string what)
        {
            var eco = EconomyManager.I; var gm = GameManager.I; if (!eco) return false;
            if (eco.cash < cost) { gm?.Say($"Not enough cash for {what} (${cost})."); return false; }
            eco.cash -= cost; return true;
        }
        public bool HireGuard()
        {
            if (Guards >= MaxGuards) { GameManager.I?.Say("The door is covered: four guards is all the house needs."); return false; }
            if (!Pay(GuardCost(Guards), "a guard")) return false;
            Guards++; Respect.Add(10); GameManager.I?.Say($"A guard takes the door. {Guards}/{MaxGuards} on duty, ${GuardWage} per shift each."); tendT = 0f; return true;
        }
        public void FireGuard() { if (Guards <= 0) return; Guards--; if (guardNpcs.Count > 0) { var g = guardNpcs[guardNpcs.Count - 1]; guardNpcs.RemoveAt(guardNpcs.Count - 1); if (g) Destroy(g.gameObject); } GameManager.I?.Say("You let a guard go."); }
        public bool HireWorker(int kind)
        {
            var lab = HomeLab.I; if (kind < 0 || kind > 2 || Worker[kind]) return false;
            if (!lab || lab.CountStations(kind) == 0) { GameManager.I?.Say($"You need a {Career.StationNames[kind].ToLower()} at home first."); return false; }
            if (!Pay(WorkerCost, "a worker")) return false;
            Worker[kind] = true; Respect.Add(10); GameManager.I?.Say($"A worker starts on the {Career.StationNames[kind].ToLower()}: one batch every shift from what is in the cupboard. ${WorkerWage} per shift."); tendT = 0f; return true;
        }
        public void FireWorker(int kind) { if (kind < 0 || kind > 2 || !Worker[kind]) return; Worker[kind] = false; if (workerNpcs[kind]) Destroy(workerNpcs[kind].gameObject); workerNpcs[kind] = null; GameManager.I?.Say("You let the worker go."); }
        public bool HireRunner()
        {
            if (Runner) return false; if (!Pay(RunnerCost, "a runner")) return false;
            Runner = true; GameManager.I?.Say($"The runner keeps the cupboard stocked for your workers (he charges the shop price plus 20%). ${RunnerWage} per shift."); return true;
        }
        public void FireRunner() { if (Runner) { Runner = false; GameManager.I?.Say("You let the runner go."); } }
        public bool HireSoldier(FactionDef f)
        {
            if (f == null || !owned.Contains(f.id)) return false;
            if (Garrison(f.id) >= MaxGarrison) { GameManager.I?.Say("That block is fully garrisoned."); return false; }
            if (!Pay(SoldierCost, "a soldier")) return false;
            garrison[f.id] = Garrison(f.id) + 1; GameManager.I?.Say($"A soldier takes a post in {f.name} turf ({Garrison(f.id)}/{MaxGarrison}). ${SoldierWage} per shift, +$40 tribute."); tendT = 0f; return true;
        }
        /// <summary>The recipe a station's worker makes: the standing order if it is open to you, otherwise the first one you can make.</summary>
        public int RecipeFor(int kind)
        {
            int o = Order[kind]; if (o >= 0 && o < HomeLab.Recipes.Length && !HomeLab.Locked(HomeLab.Recipes[o]) && System.Array.IndexOf(HomeLab.RecipesFor(kind), o) >= 0) return o;
            foreach (int r in HomeLab.RecipesFor(kind)) if (!HomeLab.Locked(HomeLab.Recipes[r])) return r;
            return -1;
        }

        // ================================================================================================ mission hooks
        public static void NoteBatch(int recipe, int units) { if (!I) return; I.Batches++; if (recipe >= 0 && recipe < 31) I.RecipeMask |= 1 << recipe; }
        public static void NoteQuality(float q) { if (I) I.BestQuality = Mathf.Max(I.BestQuality, Mathf.RoundToInt(q)); }
        public static void NoteAuto(int cash) { if (I && cash > 0) I.AutoIncome += cash; }

        // ================================================================================================ turf war
        public string WarCheck(FactionDef f, bool force = false)
        {
            if (f == null) return "Stand inside a rival's turf to start a takeover.";
            if (owned.Contains(f.id)) return "This block is already yours.";
            if (War != null) return $"You are already at war with {War.name}.";
            if (force) return null;
            if (Respect.Rank < 2 && !Admin.UnlockAll) return $"Nobody follows a {Respect.RankName} into a war. Reach Enforcer first.";
            var gm = GameManager.I; if (!gm || !gm.player || !f.Contains(gm.player.transform.position)) return $"Go into {f.name} turf to start it.";
            return null;
        }
        public bool StartWar(FactionDef f, bool force = false)
        {
            var gm = GameManager.I; string err = WarCheck(f, force); if (err != null) { gm?.Say(err); return false; }
            War = f; WarKills = 0; WarPhase = 0; WarHold = 0f; WarTime = 0f; WarsStarted++;
            int live = LiveMembers(f); WarNeed = live <= 0 ? 0 : Mathf.Clamp(Mathf.CeilToInt(live * 0.75f), 3, 8);
            var fm = FactionManager.I; if (fm) { fm.Change(f, -60f, true); fm.Alert(f, 60f); }
            gm?.Banner("TURF WAR", $"{f.name}: take down {WarNeed} of them, then hold their HQ", 5f);
            PhonePanel.Post("sal", $"So you're going after the {f.name}. Break their numbers first ({WarNeed} of them), then stand at their front door until nobody is left to argue. Bring your armed people. If you go down, it's over.");
            return true;
        }
        void EndWar(bool won, string why)
        {
            var f = War; if (f == null) return; War = null; WarPhase = 0; WarHold = 0f;
            if (!won) { GameManager.I?.Banner("TAKEOVER FAILED", why, 4f); PhonePanel.Post("sal", $"The {f.name} are still standing. Lick your wounds and try again when you're stronger."); }
        }
        void Capture(FactionDef f)
        {
            var gm = GameManager.I; var eco = EconomyManager.I; War = null; WarPhase = 0; WarHold = 0f;
            owned.Add(f.id); garrison[f.id] = 0; WarsWon++;
            foreach (var fb in FindObjectsByType<FactionBrain>(FindObjectsSortMode.None)) if (fb && fb.factionId == f.id) Destroy(fb.gameObject);
            int loot = 600 + 100 * f.members; if (eco) eco.cash += loot; Respect.Add(f.kind == FactionKind.Mafia ? 320 : 200);
            var fm = FactionManager.I; if (fm) foreach (var id in f.enemies) fm.Change(FactionDefs.Get(id), 10f, true);
            gm?.Banner("THE BLOCK IS YOURS", $"{f.name} turf taken  ·  +${loot}  ·  ${Income(f)} tribute every shift", 6f);
            PhonePanel.Post("sal", $"The whole town heard. {f.name} turf pays you now: ${Income(f)} a shift. Put soldiers on it (K ▸ Turf) or they will take it back.");
            Minimap.Refresh(); tendT = 0f; SaveNow();
        }
        void LoseZone(FactionDef f, string why)
        {
            owned.Remove(f.id); garrison.Remove(f.id); ZonesLost++;
            if (soldierNpcs.TryGetValue(f.id, out var l)) { foreach (var s in l) if (s) Destroy(s.gameObject); l.Clear(); }
            foreach (var a in attackers) if (a) Destroy(a.gameObject); attackers.Clear();
            var sp = FindAnyObjectByType<NpcSpawner>(); if (sp) sp.SpawnFaction(f, f.members, false);
            GameManager.I?.Banner("TURF LOST", $"{f.name} took their block back", 5f); PhonePanel.Post("sal", $"{why} The {f.name} are back on their block. An empty street is an invitation, kid.");
            Minimap.Refresh(); SaveNow();
        }
        /// <summary>The old owners come back for a block: a few of them turn up at the HQ. Kill them or let the garrison handle it by the next shift.</summary>
        public bool StartCounter(FactionDef f = null)
        {
            if (AttackZone != null || owned.Count == 0) return false;
            if (f == null || !owned.Contains(f.id)) { var l = new List<string>(owned); f = FactionDefs.Get(l[Random.Range(0, l.Count)]); }
            var sp = FindAnyObjectByType<NpcSpawner>(); if (!sp || f == null) return false;
            int n = Mathf.Clamp(2 + owned.Count, 3, 6); attackers.Clear();
            foreach (var m in sp.SpawnFaction(f, n, true)) { var tag = m.gameObject.AddComponent<StaffTag>(); tag.kind = 3; tag.id = f.id; attackers.Add(m.GetComponent<CharacterBody>()); }
            AttackZone = f; var fm = FactionManager.I; if (fm) fm.Alert(f, 60f);
            GameManager.I?.Banner("YOUR TURF IS UNDER ATTACK", $"{n} {f.name} are at the old HQ. You have until the next shift.", 6f);
            PhonePanel.Post("sal", $"The {f.name} are back for their block, {n} of them at the old HQ. Get over there, or pray your garrison ({Garrison(f.id)}) can hold.");
            return true;
        }
        void ResolveCounter()
        {
            var f = AttackZone; if (f == null) return; int left = AttackersLeft, g = Garrison(f.id); AttackZone = null;
            if (!owned.Contains(f.id)) { foreach (var a in attackers) if (a) Destroy(a.gameObject); attackers.Clear(); return; }
            if (left == 0 || g >= left)
            {
                foreach (var a in attackers) if (a) Destroy(a.gameObject); attackers.Clear(); Defended++; Respect.Add(60);
                GameManager.I?.Banner("TURF DEFENDED", left == 0 ? $"The {f.name} attack was wiped out" : $"Your garrison held {f.name} turf", 5f);
            }
            else LoseZone(f, $"{left} of them were still standing and you had {g} soldier(s) there.");
        }
        /// <summary>A raid on the house: the guards hold it or you lose cash and product.</summary>
        void Raid()
        {
            var gm = GameManager.I; var eco = EconomyManager.I; if (!eco) return; int strength = Mathf.Clamp(1 + owned.Count / 2, 1, MaxGuards);
            if (Guards >= strength)
            {
                RaidsRepelled++; Respect.Add(25); gm?.Banner("RAID BEATEN OFF", $"{strength} men came for the house. Your guards sent them running.", 5f);
                PhonePanel.Post("jeremy", "Somebody tried the door last night. Your guards handled it. Nothing is missing.");
            }
            else
            {
                RaidsLost++; int loss = Mathf.Min(eco.cash / 4, 2500); eco.cash -= loss; int taken = 0;
                if (Stash.I) for (int i = Stash.I.items.Count - 1; i >= 0 && taken < 8; i--) { var it = Stash.I.items[i]; var d = Items.Get(it.id); if (d == null || (d.kind != "drug" && d.kind != "arms" && d.kind != "contraband")) continue; int n = Mathf.Min(it.qty, 8 - taken); it.qty -= n; taken += n; if (it.qty <= 0) Stash.I.items.RemoveAt(i); }
                if (Stash.I && taken > 0) Stash.I.SavePublic();
                if (Guards > 0) { Guards--; if (guardNpcs.Count > 0) { var g = guardNpcs[guardNpcs.Count - 1]; guardNpcs.RemoveAt(guardNpcs.Count - 1); if (g) Destroy(g.gameObject); } }
                gm?.Banner("YOUR HOUSE WAS RAIDED", $"{strength} men, {Guards} guard(s) left. Lost ${loss}" + (taken > 0 ? $" and {taken} units of product" : ""), 6f);
                PhonePanel.Post("jeremy", $"They kicked the door in. ${loss} is gone" + (taken > 0 ? $", and {taken} units from the cupboard" : "") + $". You need {strength} guard(s) on that door (K ▸ Home).");
            }
        }

        // ================================================================================================ the shift
        /// <summary>Every 6 game hours: wages, the workers' batches, tribute, raids and counterattacks.</summary>
        public void Shift()
        {
            var eco = EconomyManager.I; var gm = GameManager.I; var lab = HomeLab.I; if (!eco) return;
            ResolveCounter();
            // workers: one batch each, the runner tops the cupboard up first
            int made = 0, spent = 0; string madeText = "";
            if (lab) for (int k = 0; k < 3; k++)
            {
                if (!Worker[k] || lab.CountStations(k) == 0) continue; int r = RecipeFor(k); if (r < 0) continue; var rc = HomeLab.Recipes[r];
                if (Runner) foreach (var (id, n) in rc.inputs)
                {
                    int miss = n - lab.Have(id); if (miss <= 0) continue; int cost = Mathf.CeilToInt(Items.Get(id).value * 1.2f) * miss; if (eco.cash < cost) continue;
                    eco.cash -= cost; spent += cost; if (!Stash.I || !Stash.I.AddDirect(id, miss)) eco.AddItem(id, miss, false);
                }
                int y = lab.WorkerBatch(r, 0.62f); if (y > 0) { made += y; WorkerBatches++; madeText += (madeText.Length > 0 ? ", " : "") + $"{y} {Items.Get(rc.product).name}"; }
            }
            int tribute = 0; foreach (var id in owned) { var f = FactionDefs.Get(id); if (f != null) tribute += Income(f); }
            int wages = WagesPerShift; bool walked = false;
            eco.cash += tribute; Tribute += tribute; AutoIncome += tribute;
            if (eco.cash < wages) { wages = eco.cash; if (Guards > 0) { Guards--; walked = true; if (guardNpcs.Count > 0) { var g = guardNpcs[guardNpcs.Count - 1]; guardNpcs.RemoveAt(guardNpcs.Count - 1); if (g) Destroy(g.gameObject); } } }
            eco.cash -= wages;
            if (made > 0 || tribute > 0 || wages > 0 || spent > 0)
            {
                LastReport = "House report: " + (made > 0 ? $"workers made {madeText}; " : WorkerCount > 0 ? "workers idle (cupboard is short); " : "") + (spent > 0 ? $"runner spent ${spent}; " : "") + (tribute > 0 ? $"turf tribute +${tribute}; " : "") + $"staff wages -${wages}" + (walked ? "; unpaid, a guard walked off" : "");
                gm?.Say(LastReport);
            }
            if (RaidDue || ((owned.Count > 0 || RaidsRepelled + RaidsLost > 0) && Random.value < 0.15f)) { RaidDue = false; Raid(); }
            if (War == null && (CounterDue || (owned.Count > 0 && Random.value < 0.22f))) { CounterDue = false; StartCounter(); }
            SaveNow();
        }

        void OnDied(CharacterBody b, string by)
        {
            if (!b) return;
            var fb = b.GetComponent<FactionBrain>();
            if (War != null && fb && fb.factionId == War.id && (by == "player" || by == "crew") && !b.GetComponent<StaffTag>()) WarKills++;
            var tag = b.GetComponent<StaffTag>(); if (!tag) return; var gm = GameManager.I;
            if (tag.kind == 0) { Guards = Mathf.Max(0, Guards - 1); gm?.Say("One of your house guards is dead."); }
            else if (tag.kind == 1) { int k; if (int.TryParse(tag.id, out k) && k >= 0 && k < 3) { Worker[k] = false; gm?.Say("One of your workers is dead."); } }
            else if (tag.kind == 2) { if (garrison.ContainsKey(tag.id)) garrison[tag.id] = Mathf.Max(0, garrison[tag.id] - 1); gm?.Say("You lost a soldier on your turf."); }
            if (tag.kind != 3) Destroy(tag);
        }

        // ================================================================================================ update
        void Update()
        {
            var gm = GameManager.I; var dn = DayNightCycle.I; var eco = EconomyManager.I; if (!gm || !gm.player || !dn || !eco) return;
            if (!loaded) { if (eco.shops.Count == 0 || !CrewManager.I || !FindAnyObjectByType<NpcSpawner>()) return; Load(); loaded = true; }
            int shift = Mathf.FloorToInt(dn.TotalHours / 6f); if (lastShift < 0) lastShift = shift;
            if (shift > lastShift) { int n = Mathf.Min(4, shift - lastShift); lastShift = shift; for (int k = 0; k < n; k++) Shift(); }
            float dt = Time.deltaTime; var fm = FactionManager.I;
            if (War != null)
            {
                WarTime += dt; var pb = gm.player.GetComponent<CharacterBody>(); Vector3 pp = gm.player.transform.position;
                alertT -= dt; if (alertT <= 0f) { alertT = 2f; if (fm) fm.Alert(War, 20f); }
                if ((pb && pb.Dead) || gm.Arrested) EndWar(false, "You went down");
                else if (WarTime > 900f) EndWar(false, "They dug in and you ran out of steam");
                else if (WarPhase == 0)
                {
                    if (WarKills >= WarNeed)
                    {
                        WarPhase = 1; var sp = FindAnyObjectByType<NpcSpawner>(); int extra = Mathf.Min(2, LiveMembers(War) == 0 ? 2 : 1); if (sp) sp.SpawnFaction(War, extra, true);
                        gm.Banner("THEY'RE BREAKING", $"Now hold the {War.name} HQ for {HoldNeed} seconds with nobody left to fight", 5f);
                    }
                }
                else
                {
                    Vector3 hq = War.HqUnity; float d = Vector2.Distance(new Vector2(pp.x, pp.z), new Vector2(hq.x, hq.z)); bool enemy = false;
                    foreach (var b in CharacterBody.All) { if (!b || b.Dead) continue; var fb = b.GetComponent<FactionBrain>(); if (fb && fb.factionId == War.id && (b.transform.position - pp).sqrMagnitude < 20f * 20f) { enemy = true; break; } }
                    if (d < 15f && !enemy) WarHold += dt; else WarHold = Mathf.Max(0f, WarHold - dt * 0.5f);
                    if (WarHold >= HoldNeed) Capture(War);
                }
            }
            if (AttackZone != null) { alertT -= dt; if (alertT <= 0f) { alertT = 2f; if (fm) fm.Alert(AttackZone, 20f); } }
            tendT -= dt; if (tendT <= 0f) { tendT = 1.5f; TendStaff(); }
            saveT -= dt; if (saveT <= 0f) { saveT = 5f; SaveNow(); }
        }

        // ================================================================================================ the people you can see
        CrewBrain SpawnHand(Vector3 pos, float yaw, string name, WeaponKind? weapon, int kind, string id)
        {
            var sp = FindAnyObjectByType<NpcSpawner>(); if (!sp) return null;
            var m = sp.Spawn<CrewBrain>(sp.PickPrefab("Vatandas"), pos, yaw); if (!m) return null;
            m.displayName = name; m.name = name; m.role = CrewBrain.Role.Guard; m.post = m.transform.position; m.weapon = weapon; m.wage = 0; m.walkSpeed = 2.2f; m.runSpeed = 5.6f;
            var tag = m.gameObject.AddComponent<StaffTag>(); tag.kind = kind; tag.id = id; return m;
        }
        static readonly string[] GuardNames = { "Guard Tony", "Guard Vito", "Guard Marco", "Guard Luca" }, SoldierNames = { "Soldier Nico", "Soldier Dante", "Soldier Rocco", "Soldier Enzo" };
        static readonly string[] WorkerNames = { "Distiller Aldo", "Cook Benny", "Gunsmith Carlo" };
        void TendStaff()
        {
            var gm = GameManager.I; var lab = HomeLab.I; if (!gm || !gm.player || IntroCinematic.Playing || CharacterCreator.IsOpen) return;
            // house guards stand either side of the front door
            guardNpcs.RemoveAll(g => !g || g.Dead); while (guardNpcs.Count > Guards) { var g = guardNpcs[guardNpcs.Count - 1]; guardNpcs.RemoveAt(guardNpcs.Count - 1); if (g) Destroy(g.gameObject); }
            if (guardNpcs.Count < Guards)
            {
                int i = guardNpcs.Count; var rot = Quaternion.Euler(0f, gm.homeYaw, 0f); Vector3 p = gm.homePosition + rot * new Vector3(i % 2 == 0 ? -1.5f : 1.5f, 0f, 0.4f + (i / 2) * 1.6f);
                var g = SpawnHand(p, gm.homeYaw, GuardNames[i % GuardNames.Length], WeaponKind.Pistol, 0, "home"); if (g) guardNpcs.Add(g);
            }
            // a worker at each staffed station
            if (lab && lab.Ready) for (int k = 0; k < 3; k++)
            {
                if (workerNpcs[k] && workerNpcs[k].Dead) workerNpcs[k] = null;
                bool want = Worker[k] && lab.CountStations(k) > 0;
                if (!want) { if (workerNpcs[k]) { Destroy(workerNpcs[k].gameObject); workerNpcs[k] = null; } continue; }
                if (workerNpcs[k]) continue;
                HomeLab.Station st = null; foreach (var s in lab.stations) if (s.kind == k) { st = s; break; } if (st == null) continue;
                Vector3 p = st.pos + Quaternion.Euler(0f, st.yaw, 0f) * new Vector3(0.9f, 0f, -0.9f);
                workerNpcs[k] = SpawnHand(p, st.yaw, WorkerNames[k], null, 1, k.ToString());
            }
            // soldiers on the blocks you hold
            foreach (var id in owned)
            {
                var f = FactionDefs.Get(id); if (f == null) continue; if (!soldierNpcs.TryGetValue(id, out var l)) soldierNpcs[id] = l = new List<CrewBrain>();
                l.RemoveAll(s => !s || s.Dead); int want = Garrison(id); while (l.Count > want) { var s = l[l.Count - 1]; l.RemoveAt(l.Count - 1); if (s) Destroy(s.gameObject); }
                if (l.Count < want)
                {
                    int i = l.Count; var spot = f.spots[i % f.spots.Length]; Vector3 p = MafiaSandbox.Data.Coords.ToUnity(spot.x, 0f, spot.y) + new Vector3(i % 2 == 0 ? -1.2f : 1.2f, 0f, 0f);
                    var s = SpawnHand(p, Random.Range(0f, 360f), SoldierNames[i % SoldierNames.Length], WeaponKind.Pistol, 2, id); if (s) l.Add(s);
                }
            }
        }

        // ================================================================================================ save / load
        static string VenueKey(Shop s) => s.kind + "@" + Mathf.RoundToInt(s.info.door.x) + "_" + Mathf.RoundToInt(s.info.door.z);
        public static void Wipe() { if (I) I.frozen = true; }
        string State()
        {
            var cm = CrewManager.I; var sb = new StringBuilder();
            sb.Append("g=").Append(Guards).Append(';').Append("w=").Append(Worker[0] ? 1 : 0).Append(Worker[1] ? 1 : 0).Append(Worker[2] ? 1 : 0).Append(';');
            sb.Append("o=").Append(Order[0]).Append(',').Append(Order[1]).Append(',').Append(Order[2]).Append(';').Append("r=").Append(Runner ? 1 : 0).Append(';');
            sb.Append("z="); foreach (var id in owned) sb.Append(id).Append(':').Append(Garrison(id)).Append(','); sb.Append(';');
            sb.Append("s=").Append(WorkerBatches).Append(',').Append(RaidsRepelled).Append(',').Append(RaidsLost).Append(',').Append(AutoIncome).Append(',').Append(Tribute).Append(',').Append(Batches).Append(',').Append(RecipeMask).Append(',').Append(BestQuality).Append(',').Append(WarsStarted).Append(',').Append(WarsWon).Append(',').Append(Defended).Append(',').Append(ZonesLost).Append(';');
            if (cm)
            {
                sb.Append("c=").Append(cm.Recruited).Append(',').Append(cm.Deals).Append(',').Append(cm.DealerCash).Append(',').Append(cm.JobsDone).Append(',').Append(cm.Racketed).Append(',').Append(cm.Bought).Append(';');
                sb.Append("v="); foreach (var v in cm.venues) if (v.Key && v.Key.info && v.Value != CrewManager.VenueMode.None) sb.Append(VenueKey(v.Key)).Append(':').Append(v.Value == CrewManager.VenueMode.Owned ? 2 : 1).Append(','); sb.Append(';');
                sb.Append("m="); foreach (var m in cm.crew) if (m && !m.Dead) sb.Append(m.displayName.Replace(',', ' ').Replace(';', ' ').Replace('|', ' ')).Append('|').Append(m.weapon.HasValue ? (int)m.weapon.Value : -1).Append('|').Append((int)m.role).Append('|').Append(m.post.x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append('|').Append(m.post.y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append('|').Append(m.post.z.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(','); sb.Append(';');
            }
            return sb.ToString();
        }
        public void SaveNow() { if (!loaded || frozen) return; string s = State(); if (s == lastSaved) return; lastSaved = s; PlayerPrefs.SetString(Key, s); PlayerPrefs.Save(); }
        static int Int(string s, int d = 0) { int v; return int.TryParse(s, out v) ? v : d; }
        static float Flt(string s) { float v; return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0f; }
        void Load()
        {
            string s = PlayerPrefs.GetString(Key, ""); var cm = CrewManager.I; var eco = EconomyManager.I; if (string.IsNullOrEmpty(s)) return;
            foreach (var part in s.Split(';'))
            {
                int eq = part.IndexOf('='); if (eq <= 0) continue; string k = part.Substring(0, eq), v = part.Substring(eq + 1);
                switch (k)
                {
                    case "g": Guards = Mathf.Clamp(Int(v), 0, MaxGuards); break;
                    case "w": for (int i = 0; i < 3 && i < v.Length; i++) Worker[i] = v[i] == '1'; break;
                    case "o": { var a = v.Split(','); for (int i = 0; i < 3 && i < a.Length; i++) Order[i] = Int(a[i], -1); break; }
                    case "r": Runner = v == "1"; break;
                    case "z": foreach (var z in v.Split(',')) { var a = z.Split(':'); if (a.Length == 2 && FactionDefs.Get(a[0]) != null) { owned.Add(a[0]); garrison[a[0]] = Mathf.Clamp(Int(a[1]), 0, MaxGarrison); } } break;
                    case "s":
                        {
                            var a = v.Split(','); if (a.Length < 12) break;
                            WorkerBatches = Int(a[0]); RaidsRepelled = Int(a[1]); RaidsLost = Int(a[2]); AutoIncome = Int(a[3]); Tribute = Int(a[4]); Batches = Int(a[5]); RecipeMask = Int(a[6]); BestQuality = Int(a[7]); WarsStarted = Int(a[8]); WarsWon = Int(a[9]); Defended = Int(a[10]); ZonesLost = Int(a[11]); break;
                        }
                    case "c": { var a = v.Split(','); if (cm && a.Length >= 6) { cm.Recruited = Int(a[0]); cm.Deals = Int(a[1]); cm.DealerCash = Int(a[2]); cm.JobsDone = Int(a[3]); cm.Racketed = Int(a[4]); cm.Bought = Int(a[5]); } break; }
                    case "v": pendingVenues = v; break;
                    case "m": pendingCrew = v; break;
                }
            }
            // the blocks you hold: their old crews are gone
            foreach (var fb in FindObjectsByType<FactionBrain>(FindObjectsSortMode.None)) if (fb && owned.Contains(fb.factionId)) Destroy(fb.gameObject);
            if (cm && eco && pendingVenues.Length > 0)
                foreach (var e in pendingVenues.Split(',')) { var a = e.Split(':'); if (a.Length != 2) continue; foreach (var sh in eco.shops) if (sh && sh.info && VenueKey(sh) == a[0]) { cm.venues[sh] = a[1] == "2" ? CrewManager.VenueMode.Owned : CrewManager.VenueMode.Protected; break; } }
            var sp = FindAnyObjectByType<NpcSpawner>(); var gm = GameManager.I;
            if (cm && sp && cm.crew.Count == 0 && pendingCrew.Length > 0)
            {
                int i = 0;
                foreach (var e in pendingCrew.Split(','))
                {
                    var a = e.Split('|'); if (a.Length != 6) continue; int role = Mathf.Clamp(Int(a[2]), 0, 2); Vector3 post = new Vector3(Flt(a[3]), Flt(a[4]), Flt(a[5]));
                    var rot = Quaternion.Euler(0f, gm.homeYaw, 0f); Vector3 p = role == 0 ? gm.homePosition + rot * new Vector3(-2.5f + i * 1.2f, 0f, 2.5f) : post;
                    var m = sp.Spawn<CrewBrain>(sp.PickPrefab("Vatandas"), p, Random.Range(0f, 360f)); if (!m) continue;
                    m.displayName = a[0]; m.name = "Crew " + a[0]; m.walkSpeed = 2.4f; m.runSpeed = 5.8f; m.role = (CrewBrain.Role)role; m.post = role == 0 ? m.transform.position : post; int w = Int(a[1], -1); if (w >= 0) m.weapon = (WeaponKind)w;
                    cm.crew.Add(m); i++;
                }
            }
            lastSaved = State(); Minimap.Refresh();
        }

        // ================================================================================================ panels (drawn inside the crew panel, K)
        const float PW = 690f; static GUIStyle wrap;
        static GUIStyle Wrap => wrap ?? (wrap = new GUIStyle(UiSkin.LabelDim) { wordWrap = true });
        public void DrawHomeTab()
        {
            var eco = EconomyManager.I; var lab = HomeLab.I; if (!eco) return;
            GUILayout.Label($"HOUSE GUARDS  {Guards}/{MaxGuards}", UiSkin.H2);
            GUILayout.Label("Guards stand at your front door. Rivals raid the house once you hold turf: a raid needs 1 guard, more as your turf grows. Without them you lose cash and product.", Wrap, GUILayout.Width(PW));
            GUILayout.BeginHorizontal();
            GUI.enabled = Guards < MaxGuards; if (GUILayout.Button($"Hire a guard  ${GuardCost(Guards)}  (${GuardWage}/shift)", UiSkin.ButtonOn, GUILayout.Height(32), GUILayout.Width(PW - 130f))) HireGuard();
            GUI.enabled = Guards > 0; if (GUILayout.Button("Let one go", UiSkin.Button, GUILayout.Height(32), GUILayout.Width(120))) FireGuard(); GUI.enabled = true;
            GUILayout.EndHorizontal(); GUILayout.Space(10);
            GUILayout.Label("STATION WORKERS", UiSkin.H2);
            GUILayout.Label("A worker makes one batch every shift (6 game hours) from the ingredients in the cupboard and puts the product back in it. Dealers sell straight from the cupboard.", Wrap, GUILayout.Width(PW));
            for (int k = 0; k < 3; k++)
            {
                GUILayout.BeginVertical(UiSkin.Panel, GUILayout.Width(PW)); GUILayout.BeginHorizontal(); bool has = lab && lab.CountStations(k) > 0;
                GUILayout.Label($"<b>{Career.StationNames[k]}</b>   " + (!has ? "not built at home yet" : Worker[k] ? "worker on shift" : "nobody working it"), UiSkin.Label, GUILayout.Width(330)); GUILayout.FlexibleSpace();
                if (!Worker[k]) { GUI.enabled = has; if (GUILayout.Button($"Hire  ${WorkerCost}  (${WorkerWage}/shift)", UiSkin.ButtonOn, GUILayout.Width(200))) HireWorker(k); GUI.enabled = true; }
                else if (GUILayout.Button("Let go", UiSkin.Button, GUILayout.Width(100))) FireWorker(k);
                GUILayout.EndHorizontal();
                if (Worker[k] && has)
                {
                    GUILayout.BeginHorizontal(); GUILayout.Label("Standing order:", UiSkin.LabelDim, GUILayout.Width(100)); int cur = RecipeFor(k);
                    foreach (int r in HomeLab.RecipesFor(k)) { var rc = HomeLab.Recipes[r]; bool locked = HomeLab.Locked(rc); GUI.enabled = !locked; if (GUILayout.Button(rc.name + (locked ? " (locked)" : ""), cur == r ? UiSkin.ButtonOn : UiSkin.Button)) Order[k] = r; GUI.enabled = true; }
                    GUILayout.EndHorizontal();
                    if (cur >= 0 && lab) { string need = ""; foreach (var (id, n) in HomeLab.Recipes[cur].inputs) need += (need.Length > 0 ? ", " : "") + $"{Items.Get(id).name} {lab.Have(id)}/{n}"; GUILayout.Label("Needs per batch: " + need, Wrap, GUILayout.Width(PW - 20f)); }
                }
                GUILayout.EndVertical(); GUILayout.Space(3);
            }
            GUILayout.Space(8); GUILayout.Label("RUNNER", UiSkin.H2);
            GUILayout.BeginHorizontal(); GUILayout.Label(Runner ? "Your runner buys whatever the workers are short of before every shift (shop price + 20%)." : "A runner keeps the cupboard stocked so the workers never stop.", UiSkin.Label, GUILayout.Width(400)); GUILayout.FlexibleSpace();
            if (!Runner) { if (GUILayout.Button($"Hire  ${RunnerCost}  (${RunnerWage}/shift)", UiSkin.ButtonOn, GUILayout.Width(200))) HireRunner(); } else if (GUILayout.Button("Let go", UiSkin.Button, GUILayout.Width(100))) FireRunner();
            GUILayout.EndHorizontal(); GUILayout.Space(8);
            GUILayout.Label($"Staff wages ${WagesPerShift} per shift  ·  batches by workers {WorkerBatches}  ·  raids beaten off {RaidsRepelled}, lost {RaidsLost}  ·  earned without you ${AutoIncome}", Wrap, GUILayout.Width(PW));
            if (LastReport.Length > 0) GUILayout.Label(LastReport, Wrap, GUILayout.Width(PW));
        }

        public void DrawTurfTab()
        {
            var here = ZoneHere(); var fm = FactionManager.I;
            GUILayout.Label($"TURF  ·  you hold {owned.Count} of {FactionDefs.All.Count} blocks", UiSkin.H2);
            GUILayout.Label("Go into a rival's turf and start a takeover: break their numbers, then hold their HQ. A block pays tribute every shift. The old owners come back for it, so garrison it.", Wrap, GUILayout.Width(PW));
            if (War != null) GUILayout.Label($"<color=#ffcf5a><b>AT WAR with {War.name}</b>: " + (WarPhase == 0 ? $"{WarKills}/{WarNeed} down" : $"hold the HQ {WarHold:0}/{HoldNeed}s") + "</color>", UiSkin.Label);
            if (AttackZone != null) GUILayout.Label($"<color=#ff8060><b>UNDER ATTACK: {AttackZone.name} turf</b>, {AttackersLeft} attackers left, garrison {Garrison(AttackZone.id)}. Settled at the next shift.</color>", UiSkin.Label);
            foreach (var f in FactionDefs.All)
            {
                bool mine = owned.Contains(f.id); GUILayout.BeginVertical(UiSkin.Panel, GUILayout.Width(PW)); GUILayout.BeginHorizontal();
                string status = mine ? $"<color=#ffd76a>YOURS</color>  ·  garrison {Garrison(f.id)}/{MaxGarrison}  ·  ${Income(f)}/shift" : (f.kind == FactionKind.Mafia ? "family" : "gang") + $"  ·  {LiveMembers(f)} on the street  ·  " + (fm ? FactionManager.Standing(fm.Rep(f)) : "") + $"  ·  worth ${Income(f)}/shift";
                GUILayout.Label($"<b>{f.name}</b> ({f.bloc} side){(here == f ? "  ◄ you are here" : "")}\n{status}", UiSkin.Label, GUILayout.Width(400)); GUILayout.FlexibleSpace();
                if (mine) { GUI.enabled = Garrison(f.id) < MaxGarrison; if (GUILayout.Button($"Station a soldier  ${SoldierCost}", UiSkin.ButtonOn, GUILayout.Width(190), GUILayout.Height(34))) HireSoldier(f); GUI.enabled = true; }
                else if (War == f) GUILayout.Label("at war", UiSkin.LabelDim, GUILayout.Width(200));
                else { string err = WarCheck(f); GUI.enabled = err == null; if (GUILayout.Button(here == f ? "Start the takeover" : "Go there to start", UiSkin.Button, GUILayout.Width(190), GUILayout.Height(34))) StartWar(f); GUI.enabled = true; }
                GUILayout.EndHorizontal(); GUILayout.EndVertical(); GUILayout.Space(3);
            }
            GUILayout.Label($"Wars won {WarsWon}  ·  attacks beaten {Defended}  ·  blocks lost {ZonesLost}  ·  tribute so far ${Tribute}", Wrap, GUILayout.Width(PW));
        }

        void OnGUI()
        {
            if ((War == null && AttackZone == null) || GameUI.Blocking || Workshop.Active || PhonePanel.IsOpen || CrewPanel.IsOpen) return;
            float W = Screen.width; var r = new Rect(W / 2f - 190, 140, 380, 46); GUI.color = new Color(0f, 0f, 0f, 0.62f); GUI.DrawTexture(r, dot);
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, richText = true }; st.normal.textColor = Color.white; GUI.color = Color.white;
            if (War != null)
            {
                float v = WarPhase == 0 ? (WarNeed > 0 ? WarKills / (float)WarNeed : 1f) : WarHold / HoldNeed;
                GUI.Label(new Rect(r.x, r.y + 4, r.width, 20), WarPhase == 0 ? $"TURF WAR  ·  {War.name}  ·  {WarKills}/{WarNeed} down" : $"HOLD THEIR HQ  ·  {WarHold:0}/{HoldNeed}s", st);
                GUI.color = new Color(1f, 1f, 1f, 0.18f); GUI.DrawTexture(new Rect(r.x + 14, r.y + 30, r.width - 28, 8), dot); GUI.color = new Color(1f, 0.78f, 0.25f); GUI.DrawTexture(new Rect(r.x + 14, r.y + 30, (r.width - 28) * Mathf.Clamp01(v), 8), dot); GUI.color = Color.white;
            }
            else GUI.Label(new Rect(r.x, r.y + 4, r.width, 40), $"<color=#ff8060>YOUR TURF IS UNDER ATTACK</color>\n{AttackZone.name}  ·  {AttackersLeft} left  ·  garrison {Garrison(AttackZone.id)}", st);
        }

        // ================================================================================================ test helpers (admin / mission self test)
        /// <summary>Runs one shift right now (payouts, workers, raids, counterattacks).</summary>
        public void ForceShift() { if (CrewManager.I) CrewManager.I.ForcePayout(); Shift(); }
        /// <summary>Kills members of a crew as if the player did it. Returns how many went down.</summary>
        public int DebugKill(string factionId, int max, bool onlyAttackers = false)
        {
            int n = 0;
            foreach (var b in new List<CharacterBody>(CharacterBody.All))
            {
                if (n >= max) break; if (!b || b.Dead) continue; var fb = b.GetComponent<FactionBrain>(); if (!fb || fb.factionId != factionId) continue; if (onlyAttackers && !b.GetComponent<StaffTag>()) continue;
                b.Die(b.ChestPos, Vector3.forward, "torso", 1f, "player"); n++;
            }
            return n;
        }
        /// <summary>Finishes the hold phase of the current war (the capture itself runs through the normal path).</summary>
        public void DebugHold() { if (War == null) return; DebugKill(War.id, 99); WarKills = Mathf.Max(WarKills, WarNeed); WarPhase = 1; Capture(War); }
        public bool DebugCapture(FactionDef f) { if (f == null || owned.Contains(f.id)) return false; if (War != null && War != f) EndWar(true, ""); if (War == null && !StartWar(f, true)) return false; DebugHold(); return owned.Contains(f.id); }
    }
}
