using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Gang;
using MafiaSandbox.Player;
using MafiaSandbox.World;

namespace MafiaSandbox.Economy
{
    /// <summary>
    /// Your home: a workbench where you cook bootleg liquor, drugs and guns from shop-bought ingredients (a small puzzle decides the yield),
    /// and a bed to sleep through the night. Batches finish in game time, so sleeping is the fast way to get them done.
    /// </summary>
    public class HomeLab : MonoBehaviour
    {
        public static HomeLab I { get; private set; }
        public static bool Sleeping => I && I.sleeping;
        public Vector3 LabPos => stations.Count > 0 ? stations[0].pos : fallbackLab;
        Vector3 fallbackLab;
        public class Station { public int kind; public Vector3 pos; public float yaw; public GameObject go; }
        public readonly List<Station> stations = new List<Station>();
        public static readonly Vector3 StationSize = new Vector3(1.5f, 0.95f, 0.8f);
        public int CountStations(int kind) { int n = 0; foreach (var st in stations) if (st.kind == kind) n++; return n; }
        public int NearStation(Vector3 p, float r) { int best = -1; float bd = r * r; for (int i = 0; i < stations.Count; i++) { var d = stations[i].pos - p; d.y = 0f; if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = i; } } return best; }
        public static int[] RecipesFor(int station) { switch (station) { case 0: return new[] { 0, 6, 7 }; case 1: return new[] { 1, 2, 3, 8 }; case 2: return new[] { 4, 9, 10, 5 }; default: return new[] { 0, 6, 7, 1, 2, 3, 8, 4, 9, 10, 5 }; } }
        public Vector3 BedPos { get; private set; }
        public bool Ready { get; private set; }
        bool sleeping; float fade; string wakeText = "";

        public class Recipe
        {
            public string id, name, product, blurb, puzzle; public int yield, rank, level; public float hours; public (string id, int n)[] inputs;
        }
        public static readonly Recipe[] Recipes =
        {
            new Recipe { id = "moon",  name = "Moonshine",        product = "Moonshine", yield = 6, rank = 0, hours = 3f, puzzle = "heat",     level = 12, blurb = "Cook the mash and keep the still in the sweet spot.", inputs = new[] { ("Corn", 2), ("Sugar", 2), ("Yeast", 1) } },
            new Recipe { id = "weed",  name = "Weed",             product = "Weed",      yield = 6, rank = 0, hours = 4f, puzzle = "timing",   level = 3, blurb = "Trim the buds at the right moment.",                 inputs = new[] { ("Seeds", 2), ("PlantFood", 1) } },
            new Recipe { id = "pills", name = "Pills",            product = "Pills",     yield = 5, rank = 1, hours = 3f, puzzle = "sequence", level = 5, blurb = "Repeat the press sequence without a slip.",           inputs = new[] { ("ColdMed", 2), ("Solvent", 1) } },
            new Recipe { id = "pow",   name = "White powder",     product = "Powder",    yield = 4, rank = 2, hours = 4f, puzzle = "timing",   level = 5, blurb = "A delicate reaction. Hit the window every time.",    inputs = new[] { ("Precursor", 1), ("Solvent", 2) } },
            new Recipe { id = "gun",   name = "Pistols",          product = "Guns",      yield = 2, rank = 2, hours = 3f, puzzle = "assembly", level = 5, blurb = "Assemble the parts in blueprint order.",              inputs = new[] { ("MetalParts", 3), ("Gunpowder", 1) } },
            new Recipe { id = "rifle", name = "Assault rifle",    product = "Rifles",    yield = 1, rank = 3, hours = 5f, puzzle = "assembly", level = 7, blurb = "Seven parts and the blueprint fades. Remember it.", inputs = new[] { ("MetalParts", 6), ("Gunpowder", 2) } },
            // appended (the save file stores batches by index): more to make at each station
            new Recipe { id = "whisk",    name = "Barrel whiskey", product = "Whiskey",   yield = 4, rank = 1, hours = 5f, puzzle = "heat",     level = 10, blurb = "Run the still, then rest the spirit on charred oak.",   inputs = new[] { ("Corn", 3), ("Sugar", 1), ("Yeast", 1), ("OakChips", 1) } },
            new Recipe { id = "gin",      name = "Bathtub gin",    product = "Gin",       yield = 5, rank = 1, hours = 3f, puzzle = "heat",     level = 8,  blurb = "A neutral run, sharpened with botanicals and a few drops of essence.", inputs = new[] { ("Sugar", 2), ("Yeast", 1), ("Juniper", 2) } },
            new Recipe { id = "crystal",  name = "Blue crystal",   product = "Crystal",   yield = 3, rank = 3, hours = 5f, puzzle = "timing",   level = 8,  blurb = "The hardest cook in town. Every step has to land.",        inputs = new[] { ("Precursor", 2), ("Solvent", 1), ("ColdMed", 2) } },
            new Recipe { id = "revolver", name = "Revolvers",      product = "Revolvers", yield = 2, rank = 2, hours = 3f, puzzle = "assembly", level = 6,  blurb = "Six chambers, five parts. Old and dependable.",           inputs = new[] { ("MetalParts", 3), ("Gunpowder", 1), ("WoodStock", 1) } },
            new Recipe { id = "shotgun",  name = "Pump shotguns",  product = "Shotguns",  yield = 1, rank = 3, hours = 4f, puzzle = "assembly", level = 7,  blurb = "Receiver, bolt, barrel, forend and a walnut stock.",       inputs = new[] { ("MetalParts", 5), ("Gunpowder", 2), ("WoodStock", 1) } },
        };
        public class Batch { public int state; public float readyAt; public int yield; public float score; }   // 0 idle, 1 cooking, 2 ready
        public readonly Batch[] batches = new Batch[Recipes.Length];

        void Awake()
        {
            I = this; for (int i = 0; i < batches.Length; i++) batches[i] = new Batch();
            var sv = PlayerPrefs.GetString("ms_lab_v1", "");   // a batch that was cooking when you quit is done when you return
            var parts = sv.Split(';'); for (int i = 0; i < batches.Length && i < parts.Length; i++) { var q = parts[i].Split(','); if (q.Length == 2 && int.TryParse(q[0], out int st) && int.TryParse(q[1], out int y) && st > 0) { batches[i].state = 2; batches[i].yield = y; } }
        }
        void Save() { var sb = new System.Text.StringBuilder(); foreach (var b in batches) sb.Append(b.state).Append(',').Append(b.yield).Append(';'); PlayerPrefs.SetString("ms_lab_v1", sb.ToString()); PlayerPrefs.Save(); }

        // ------------------------------------------------------------------ building the room props
        void Update()
        {
            if (!Ready) TryBuild();
            var dn = DayNightCycle.I; var gm = GameManager.I;
            for (int i = 0; i < batches.Length; i++) if (batches[i] == null) batches[i] = new Batch();
            for (int i = 0; i < batches.Length; i++)
                if (batches[i].state == 1 && dn && dn.TotalHours >= batches[i].readyAt) { batches[i].state = 2; Save(); gm?.Say($"Your batch of {Recipes[i].name} is ready at the workbench."); }
        }
        static Material M(Color c) { var m = new Material(Shader.Find("Standard")) { color = c }; m.SetFloat("_Glossiness", 0.2f); return m; }
        void TryBuild()
        {
            var eco = EconomyManager.I; if (!eco || eco.homeShop == null) return;
            var bi = eco.homeShop.info; var anc = Stash.HomeAnchor(bi); Vector3 toDoor = bi.door - anc; toDoor.y = 0f; toDoor = toDoor.sqrMagnitude < 0.01f ? Vector3.forward : toDoor.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, toDoor); float y = anc.y; float yaw = Mathf.Atan2(toDoor.x, toDoor.z) * Mathf.Rad2Deg;
            var lp = Stash.InHome(bi, anc + toDoor * 1.1f + right * 1.7f); var bp = Stash.InHome(bi, anc + toDoor * 1.1f - right * 1.9f); lp.y = bp.y = y; fallbackLab = lp; BedPos = bp;
            BuildBed(BedPos, yaw); LoadStations(); Ready = true;
        }
        GameObject Part(Transform root, string n, Vector3 lp, Vector3 sc, Color c, PrimitiveType t = PrimitiveType.Cube) { var g = GameObject.CreatePrimitive(t); g.name = n; Destroy(g.GetComponent<Collider>()); g.transform.SetParent(root, false); g.transform.localPosition = lp; g.transform.localScale = sc; g.GetComponent<Renderer>().sharedMaterial = M(c); return g; }
        const string StKey = "ms_stations_v2";
        void SaveStations() { var sb = new System.Text.StringBuilder(); var ci = System.Globalization.CultureInfo.InvariantCulture; foreach (var st in stations) sb.Append(st.kind).Append(',').Append(st.pos.x.ToString(ci)).Append(',').Append(st.pos.y.ToString(ci)).Append(',').Append(st.pos.z.ToString(ci)).Append(',').Append(st.yaw.ToString(ci)).Append(';'); PlayerPrefs.SetString(StKey, sb.ToString()); PlayerPrefs.Save(); }
        void LoadStations()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var part in PlayerPrefs.GetString(StKey, "").Split(';'))
            {
                var q = part.Split(','); if (q.Length != 5) continue;
                if (int.TryParse(q[0], out int k) && float.TryParse(q[1], System.Globalization.NumberStyles.Float, ci, out float x) && float.TryParse(q[2], System.Globalization.NumberStyles.Float, ci, out float yy) && float.TryParse(q[3], System.Globalization.NumberStyles.Float, ci, out float z) && float.TryParse(q[4], System.Globalization.NumberStyles.Float, ci, out float yaw))
                    AddStation(k, new Vector3(x, yy, z), yaw, false);
            }
        }
        public Station AddStation(int kind, Vector3 pos, float yaw, bool save = true)
        {
            var st = new Station { kind = kind, pos = pos, yaw = yaw }; st.go = BuildStation(kind, pos, yaw); stations.Add(st); if (save) SaveStations(); return st;
        }
        public void RemoveStation(int i) { if (i < 0 || i >= stations.Count) return; if (stations[i].go) Destroy(stations[i].go); stations.RemoveAt(i); SaveStations(); }
        GameObject BuildStation(int kind, Vector3 pos, float yaw)
        {
            var r = new GameObject("HomeStation_" + Career.StationNames[Mathf.Clamp(kind, 0, 2)]).transform; r.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var wood = new Color(0.45f, 0.3f, 0.16f); var steel = new Color(0.55f, 0.58f, 0.62f); var copper = new Color(0.72f, 0.42f, 0.2f);
            Part(r, "Top", new Vector3(0, 0.9f, 0), new Vector3(1.5f, 0.08f, 0.75f), kind == 2 ? steel : wood);
            foreach (var x in new[] { -0.65f, 0.65f }) foreach (var z in new[] { -0.3f, 0.3f }) Part(r, "Leg", new Vector3(x, 0.44f, z), new Vector3(0.08f, 0.88f, 0.08f), kind == 2 ? new Color(0.25f, 0.25f, 0.28f) : wood);
            WorkshopModels.StationDecor(kind, r);
            var col = r.gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0f, StationSize.y / 2f, 0f); col.size = StationSize;
            return r.gameObject;
        }
        void BuildBed(Vector3 pos, float yaw)
        {
            var r = new GameObject("HomeBed").transform; r.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            Part(r, "Frame", new Vector3(0, 0.22f, 0), new Vector3(1.1f, 0.3f, 2.0f), new Color(0.33f, 0.2f, 0.1f));
            Part(r, "Mattress", new Vector3(0, 0.46f, 0), new Vector3(1.0f, 0.2f, 1.9f), new Color(0.82f, 0.82f, 0.88f));
            Part(r, "Blanket", new Vector3(0, 0.58f, -0.3f), new Vector3(1.02f, 0.08f, 1.2f), new Color(0.55f, 0.2f, 0.22f));
            Part(r, "Pillow", new Vector3(0, 0.6f, 0.75f), new Vector3(0.7f, 0.14f, 0.35f), new Color(0.95f, 0.95f, 0.95f));
        }

        // ------------------------------------------------------------------ ingredients & batches
        public int Have(string id) { if (Admin.Unlimited) return 999; var eco = EconomyManager.I; int n = eco ? eco.Count(id) : 0; if (Stash.I) foreach (var s in Stash.I.items) if (s.id == id) n += s.qty; return n; }
        void Use(string id, int n)
        {
            var eco = EconomyManager.I; if (Admin.Unlimited) return;
            for (int k = 0; k < n; k++)
            {
                var it = eco.inv.Find(x => x.id == id);
                if (it != null) { it.qty--; if (it.qty <= 0) eco.inv.Remove(it); continue; }
                var s = Stash.I ? Stash.I.items.Find(x => x.id == id) : null; if (s != null) { s.qty--; if (s.qty <= 0) Stash.I.items.Remove(s); }
            }
            if (Stash.I) Stash.I.SavePublic();
        }
        /// <summary>How many batches the ingredients in the backpack and the cupboard allow.</summary>
        public int MaxBatches(int r) { int m = 99; foreach (var (id, n) in Recipes[r].inputs) m = Mathf.Min(m, Have(id) / n); return m; }
        public static bool Locked(Recipe rc) => !Admin.UnlockAll && Respect.Rank < rc.rank && !Career.IsStarterRecipe(rc.id);
        public static int YieldFor(Recipe rc, float score)
        {
            float f = score < 0.25f ? 0.3f : 0.55f + score * 0.65f + (score >= 0.9f ? 0.15f : 0f);
            int y = Mathf.Max(1, Mathf.RoundToInt(rc.yield * f)); if (score >= 0.9f && rc.yield <= 2) y = rc.yield + 1; return y;
        }
        public void CollectNow(int r) { if (batches[r].state == 1) batches[r].state = 2; Collect(r); }
        public string CanStart(int r, int mult = 1)
        {
            var rc = Recipes[r]; if (Locked(rc)) return $"Needs rank {Respect.Ranks[Mathf.Min(5, rc.rank)]}";
            if (batches[r].state != 0) return batches[r].state == 1 ? "Already cooking" : "Collect the finished batch first";
            foreach (var (id, n) in rc.inputs) if (Have(id) < n * mult) return $"Missing {Items.Get(id).name}";
            return null;
        }
        public void StartBatch(int r, float score, int mult = 1)
        {
            var rc = Recipes[r]; var gm = GameManager.I; mult = Mathf.Max(1, mult); if (CanStart(r, mult) != null) return;
            foreach (var (id, n) in rc.inputs) Use(id, n * mult);
            int y = YieldFor(rc, score) * mult;
            batches[r].state = 1; batches[r].yield = y; batches[r].score = score; batches[r].readyAt = DayNightCycle.I.TotalHours + (Admin.Instant ? 0f : rc.hours); Save();
            gm.Say(score < 0.25f ? $"You botched the {rc.name}. Maybe {y} usable." : score >= 0.9f ? $"Perfect work! {rc.name} will yield {y}." : $"{rc.name} is cooking: expect {y}.");
        }
        public void Collect(int r)
        {
            var rc = Recipes[r]; var b = batches[r]; if (b.state != 2) return; var eco = EconomyManager.I;
            eco.AddItem(rc.product, b.yield, false); GameManager.I.Say($"Collected {b.yield} × {Items.Get(rc.product).name}"); Respect.Add(rc.level + b.yield); if (Career.IsStarterRecipe(rc.id)) Career.AddCollect();
            b.state = 0; b.yield = 0; Save(); Sfx.At("click", LabPos);
        }
        public float Progress(int r) { var rc = Recipes[r]; var b = batches[r]; if (b.state == 2) return 1f; if (b.state != 1) return 0f; return Mathf.Clamp01(1f - (b.readyAt - DayNightCycle.I.TotalHours) / rc.hours); }
        public float HoursLeft(int r) => Mathf.Max(0f, batches[r].readyAt - DayNightCycle.I.TotalHours);

        // ------------------------------------------------------------------ sleeping
        public static bool CanSleepNow { get { var d = DayNightCycle.I; return d && (d.timeOfDay >= 19.5f || d.timeOfDay < 6f); } }
        public void TrySleep()
        {
            var gm = GameManager.I; if (sleeping) return;
            if (!CanSleepNow) { gm.Say("You're not tired. Sleep after 19:30."); return; }
            if (WantedSystem.I && WantedSystem.I.Level > 0) { gm.Say("You can't sleep with the police after you."); return; }
            StartCoroutine(SleepRoutine());
        }
        IEnumerator SleepRoutine()
        {
            sleeping = true; var gm = GameManager.I; var dn = DayNightCycle.I; var pc = gm.player;
            for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime) { fade = t / 1.2f; yield return null; }
            fade = 1f; float from = dn.timeOfDay; float slept = (6f - from + 24f) % 24f; dn.SkipTo(6f);
            Npc.NpcBrain.ReviveAll(); MafiaSandbox.Core.SaveGame.Save();   // a new day: everybody who died yesterday is back, and the game is saved
            var body = pc.GetComponent<CharacterBody>(); if (body) body.Treat(999);
            if (Vitals.I) { Vitals.I.stamina = 100f; Vitals.I.hunger = Mathf.Max(25f, Vitals.I.hunger - 20f); }
            if (WantedSystem.I) WantedSystem.I.Clear();
            var cm = CrewManager.I; string rep = cm ? cm.LastReport : "";
            wakeText = $"DAY {dn.Day}\nslept {slept:0} hours\ngame saved" + (string.IsNullOrEmpty(rep) ? "" : "\n" + rep);
            yield return new WaitForSecondsRealtime(2.6f); wakeText = "";
            for (float t = 0f; t < 1.4f; t += Time.unscaledDeltaTime) { fade = 1f - t / 1.4f; yield return null; }
            fade = 0f; sleeping = false; gm.Say("Good morning. Check the workbench and your venues.");
        }
        void OnGUI()
        {
            if (fade <= 0.001f) return; var old = GUI.color; GUI.color = new Color(0, 0, 0, Mathf.Clamp01(fade)); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = old;
            if (!string.IsNullOrEmpty(wakeText)) { var st = new GUIStyle(MafiaSandbox.UI.UiSkin.Title) { alignment = TextAnchor.MiddleCenter, fontSize = 30, wordWrap = true }; st.normal.textColor = Color.white; GUI.Label(new Rect(0, Screen.height / 2f - 90, Screen.width, 200), wakeText, st); }
        }
    }
}
