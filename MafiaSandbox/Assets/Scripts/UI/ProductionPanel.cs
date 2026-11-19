using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Gang;
using MafiaSandbox.World;

namespace MafiaSandbox.UI
{
    /// <summary>The home workbench: pick a recipe, check ingredients, play a tiny puzzle (it sets the yield), come back when the batch is done.</summary>
    public class ProductionPanel : MonoBehaviour
    {
        public static ProductionPanel I { get; private set; }
        public static bool IsOpen => (I && I.open) || Workshop.Active || AdminPanel.IsOpen;
        int mult = 1;
        public bool PanelOpen => open;
        bool open; int sel; Puzzle puzzle; float resultT; int puzzleRecipe;
        void Awake() { I = this; }
        int station = -1; Vector3 stPos;
        public void Open(int st = -1)
        {
            open = true; puzzle = null; station = st; var lab = HomeLab.I; var gm = GameManager.I; stPos = gm && gm.player ? gm.player.transform.position : Vector3.zero;
            if (lab) { int i = lab.NearStation(stPos, 3f); if (i >= 0) stPos = lab.stations[i].pos; }
            sel = HomeLab.RecipesFor(st)[0]; mult = 1;
        }

        void Update()
        {
            var gm = GameManager.I; if (!open || !gm || !gm.player) return; if (Workshop.Active) { open = false; return; }
            if (puzzle != null) { puzzle.Tick(Time.unscaledDeltaTime); if (puzzle.Done && resultT <= 0f) resultT = 1.6f; if (resultT > 0f) { resultT -= Time.unscaledDeltaTime; if (resultT <= 0f) { HomeLab.I.StartBatch(puzzleRecipe, puzzle.Score); puzzle = null; } } return; }
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F) || HomeLab.I == null || (gm.player.transform.position - stPos).sqrMagnitude > 5f * 5f) open = false;
        }

        void OnGUI()
        {
            if (!open) return; var lab = HomeLab.I; if (!lab) return;
            float W = Screen.width, H = Screen.height; var r = new Rect(W / 2f - 400, H / 2f - 250, 800, 500); UiSkin.DrawPanel(r, true);
            GUILayout.BeginArea(new Rect(r.x + 16, r.y + 12, r.width - 32, r.height - 24));
            GUILayout.BeginHorizontal(); GUILayout.Label(station >= 0 ? Career.StationNames[station] : "Workbench", UiSkin.Title); GUILayout.FlexibleSpace(); var dn = DayNightCycle.I; GUILayout.Label($"Day {dn.Day}  ·  {(int)dn.timeOfDay:00}:{(int)((dn.timeOfDay % 1f) * 60f):00}  ·  Esc / F closes", UiSkin.LabelDim); GUILayout.EndHorizontal();
            if (puzzle != null) { DrawPuzzle(r); GUILayout.EndArea(); return; }
            var tip = QuestManager.I ? QuestManager.I.Coach : null;
            if (!string.IsNullOrEmpty(tip)) { var ts = new GUIStyle(UiSkin.Label) { wordWrap = true, fontSize = 13 }; ts.normal.textColor = new Color(1f, 0.85f, 0.35f); GUILayout.Label("MISSION TIP: " + tip, ts); }
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(300));
            foreach (int i in HomeLab.RecipesFor(station))
            {
                var rc = HomeLab.Recipes[i]; var b = lab.batches[i]; bool locked = HomeLab.Locked(rc);
                string tag = locked ? $"  (needs {Respect.Ranks[Mathf.Min(5, rc.rank)]})" : b.state == 1 ? $"  · {lab.HoursLeft(i):0.0} h" : b.state == 2 ? "  · READY" : "";
                if (GUILayout.Button(rc.name + tag, sel == i ? UiSkin.ButtonOn : UiSkin.Button, GUILayout.Height(32))) { sel = i; mult = 1; }
            }
            GUILayout.EndVertical(); GUILayout.Space(14);
            GUILayout.BeginVertical(GUILayout.Width(440));   // fixed width: a long line must wrap instead of pushing the buttons' text out of the panel
            var wrap = new GUIStyle(UiSkin.LabelDim) { wordWrap = true };
            var s = HomeLab.Recipes[sel]; var sb = lab.batches[sel];
            GUILayout.Label(s.name, UiSkin.H2); GUILayout.Label(s.blurb, wrap);
            GUILayout.Space(6); GUILayout.Label(Admin.Unlimited ? "INGREDIENTS (admin: unlimited)" : "INGREDIENTS (backpack + home cupboard)", UiSkin.H2);
            int maxB = Mathf.Clamp(lab.MaxBatches(sel), 1, 5); mult = Mathf.Clamp(mult, 1, maxB);
            foreach (var (id, n) in s.inputs) { int have = lab.Have(id); GUILayout.Label($"{Items.Get(id).name}   {(Admin.Unlimited ? "∞" : have.ToString())} / {n * mult}", have >= n * mult ? UiSkin.Label : UiSkin.LabelDim); }
            GUILayout.BeginHorizontal(); GUILayout.Label($"Batch size  ×{mult}   (you have enough for ×{Mathf.Min(5, lab.MaxBatches(sel))})", UiSkin.LabelDim, GUILayout.Width(300));
            if (GUILayout.Button("−", UiSkin.Button, GUILayout.Width(36))) mult = Mathf.Max(1, mult - 1); if (GUILayout.Button("+", UiSkin.Button, GUILayout.Width(36))) mult = Mathf.Min(maxB, mult + 1); GUILayout.EndHorizontal();
            GUILayout.Label($"Makes about {s.yield * mult} × {Items.Get(s.product).name} (better handiwork = more)  ·  {s.hours:0} game hours", wrap);
            GUILayout.Label("Buy ingredients at the market (mash, sugar, yeast, oak, botanicals, seeds, medicine, solvent, precursor) and the gun shop (metal parts, gunpowder, stock blanks). Keep them in your backpack or the cupboard at home.", wrap);
            GUILayout.Space(8);
            if (sb.state == 1) { GUILayout.Label($"Cooking… {sb.yield} units expected", UiSkin.Label); var pr = GUILayoutUtility.GetRect(10, 16, GUILayout.ExpandWidth(true)); UiSkin.DrawBar(pr, lab.Progress(sel), UiSkin.Accent); GUILayout.Label("Sleep to speed up the wait.", UiSkin.LabelDim); }
            else if (sb.state == 2) { if (GUILayout.Button($"Collect {sb.yield} × {Items.Get(s.product).name}", UiSkin.ButtonOn, GUILayout.Height(38))) lab.Collect(sel); }
            else
            {
                string err = lab.CanStart(sel, mult); GUI.enabled = err == null;
                if (GUILayout.Button(err == null ? "Get to work at the bench" : err, UiSkin.ButtonOn, GUILayout.Height(38))) { if (Workshop.I && Workshop.I.Begin(sel, mult)) open = false; }
                GUI.enabled = true;
            }
            GUILayout.EndVertical(); GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        static Puzzle MakePuzzle(HomeLab.Recipe r)
        {
            switch (r.puzzle)
            {
                case "heat": return new HeatPuzzle(12f);
                case "timing": return new TimingPuzzle(r.level, r.id == "pow" ? 0.12f : 0.22f);
                case "sequence": return new SequencePuzzle(r.level);
                default: return new AssemblyPuzzle(r.level, r.id == "rifle");
            }
        }
        void DrawPuzzle(Rect panel)
        {
            GUILayout.Space(8); GUILayout.Label(puzzle.Title, UiSkin.H2); GUILayout.Label(puzzle.Hint, UiSkin.LabelDim);
            var area = GUILayoutUtility.GetRect(10, 330, GUILayout.ExpandWidth(true)); puzzle.Draw(area);
            if (puzzle.Done) { var st = new GUIStyle(UiSkin.Title) { alignment = TextAnchor.MiddleCenter }; GUI.Label(new Rect(area.x, area.y + area.height / 2f - 30, area.width, 60), puzzle.Score >= 0.9f ? "PERFECT" : puzzle.Score >= 0.55f ? "Good work" : puzzle.Score >= 0.25f ? "Sloppy" : "Botched", st); }
        }

        // ================================================================== puzzles
        abstract class Puzzle
        {
            public bool Done; public float Score; public string Title, Hint;
            public abstract void Tick(float dt); public abstract void Draw(Rect r);
            protected static void Box(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }
        }

        /// <summary>Marker sweeps a bar: stop it inside the green zone. Several rounds, speeding up.</summary>
        class TimingPuzzle : Puzzle
        {
            int rounds, round; float pos, dir = 1f, center, zone; readonly List<float> scores = new List<float>(); bool stop; string last = "";
            public TimingPuzzle(int rounds, float zone) { this.rounds = rounds; this.zone = zone; Title = "Timing"; Hint = "Press SPACE (or STOP) when the marker is inside the green zone."; NewRound(); }
            void NewRound() { center = Random.Range(0.2f, 0.8f); pos = Random.value < 0.5f ? 0f : 1f; dir = pos < 0.5f ? 1f : -1f; }
            public override void Tick(float dt)
            {
                if (Done) return; pos += dir * dt * (0.8f + round * 0.18f); if (pos > 1f) { pos = 1f; dir = -1f; } if (pos < 0f) { pos = 0f; dir = 1f; }
                if (Input.GetKeyDown(KeyCode.Space)) stop = true;
                if (stop)
                {
                    stop = false; float d = Mathf.Abs(pos - center); float s = d <= zone / 2f ? 1f : Mathf.Max(0f, 1f - (d - zone / 2f) / 0.25f); scores.Add(s); last = s >= 0.95f ? "Bullseye" : s > 0.5f ? "Close" : "Miss";
                    round++; if (round >= rounds) { Done = true; float t = 0; foreach (var x in scores) t += x; Score = t / scores.Count; } else NewRound();
                }
            }
            public override void Draw(Rect r)
            {
                var bar = new Rect(r.x + 20, r.y + 110, r.width - 40, 46); Box(bar, new Color(0.1f, 0.12f, 0.16f)); Box(new Rect(bar.x + (center - zone / 2f) * bar.width, bar.y, zone * bar.width, bar.height), new Color(0.2f, 0.7f, 0.35f));
                Box(new Rect(bar.x + pos * bar.width - 3f, bar.y - 8, 6, bar.height + 16), Color.white);
                GUI.Label(new Rect(r.x + 20, r.y + 60, 400, 30), $"Round {Mathf.Min(round + 1, rounds)} / {rounds}   {last}", UiSkin.Label);
                if (!Done && GUI.Button(new Rect(r.x + r.width / 2f - 80, r.y + 200, 160, 50), "STOP", UiSkin.ButtonOn)) stop = true;
            }
        }

        /// <summary>Hold SPACE / the HEAT button to raise the temperature and keep the needle inside the moving band.</summary>
        class HeatPuzzle : Puzzle
        {
            float dur, t, v = 0.15f, inBand, clock; bool btnHeld;
            public HeatPuzzle(float dur) { this.dur = dur; Title = "Keep the still hot"; Hint = "Hold SPACE or the HEAT button to heat, release to cool. Keep the needle in the band."; }
            float Lo => 0.45f + Mathf.Sin(clock * 0.7f) * 0.14f; float Hi => Lo + 0.2f;
            public override void Tick(float dt)
            {
                if (Done) return; clock += dt; t += dt; bool heat = Input.GetKey(KeyCode.Space) || btnHeld; v = Mathf.Clamp01(v + (heat ? 0.5f : -0.32f) * dt + Mathf.Sin(clock * 3.1f) * 0.05f * dt);
                if (v >= Lo && v <= Hi) inBand += dt; if (t >= dur) { Done = true; Score = inBand / dur; }
            }
            public override void Draw(Rect r)
            {
                var bar = new Rect(r.x + 20, r.y + 90, r.width - 40, 56); Box(bar, new Color(0.1f, 0.12f, 0.16f)); Box(new Rect(bar.x + Lo * bar.width, bar.y, (Hi - Lo) * bar.width, bar.height), new Color(0.2f, 0.7f, 0.35f));
                Box(new Rect(bar.x + v * bar.width - 3f, bar.y - 8, 6, bar.height + 16), v >= Lo && v <= Hi ? Color.white : new Color(1f, 0.4f, 0.3f));
                GUI.Label(new Rect(r.x + 20, r.y + 50, 400, 30), $"Time left {Mathf.Max(0f, dur - t):0.0}s    in band {(inBand / Mathf.Max(0.01f, t)) * 100f:0}%", UiSkin.Label);
                if (!Done) { btnHeld = GUI.RepeatButton(new Rect(r.x + r.width / 2f - 100, r.y + 190, 200, 60), "HEAT (hold)", UiSkin.ButtonOn); }
            }
        }

        /// <summary>Watch the coloured pads flash, then repeat the order.</summary>
        class SequencePuzzle : Puzzle
        {
            readonly int len; readonly List<int> seq = new List<int>(); int shown, input, mistakes; float timer; bool showing = true; int lit = -1; int flashLit = -1; float flashT;
            static readonly Color[] Cols = { new Color(0.85f, 0.25f, 0.25f), new Color(0.25f, 0.6f, 0.9f), new Color(0.3f, 0.8f, 0.4f), new Color(0.95f, 0.8f, 0.25f) };
            public SequencePuzzle(int len) { this.len = len; for (int i = 0; i < len; i++) seq.Add(Random.Range(0, 4)); Title = "Press sequence"; Hint = "Watch the pads, then repeat the sequence. Three mistakes and it's ruined."; timer = 0.8f; }
            public override void Tick(float dt)
            {
                if (Done) return; if (flashT > 0f) { flashT -= dt; if (flashT <= 0f) flashLit = -1; }
                if (!showing) return; timer -= dt;
                if (timer <= 0f) { if (lit >= 0) { lit = -1; timer = 0.25f; } else if (shown < seq.Count) { lit = seq[shown++]; timer = 0.55f; } else { showing = false; } }
            }
            public override void Draw(Rect r)
            {
                float s = 110f; float x0 = r.x + r.width / 2f - s - 8f, y0 = r.y + 40f;
                for (int i = 0; i < 4; i++)
                {
                    var rc = new Rect(x0 + (i % 2) * (s + 16f), y0 + (i / 2) * (s + 16f), s, s); bool on = i == lit || i == flashLit; var c = Cols[i]; if (!on) c = Color.Lerp(c, Color.black, 0.65f); Box(rc, c);
                    if (!showing && !Done && GUI.Button(rc, GUIContent.none, GUIStyle.none)) Press(i);
                }
                GUI.Label(new Rect(r.x + 20, r.y + 4, 500, 30), showing ? "Watch…" : $"Your turn  {input} / {len}   mistakes {mistakes}/3", UiSkin.Label);
            }
            void Press(int i)
            {
                flashLit = i; flashT = 0.2f;
                if (seq[input] == i) { input++; if (input >= len) { Done = true; Score = Mathf.Clamp01(1f - mistakes * 0.25f); } }
                else { mistakes++; if (mistakes >= 3) { Done = true; Score = 0.1f; } }
            }
        }

        /// <summary>Click the parts in blueprint order. Rifles hide the blueprint after a few seconds.</summary>
        class AssemblyPuzzle : Puzzle
        {
            readonly string[] names = { "Barrel", "Spring", "Trigger", "Grip", "Slide", "Bolt", "Stock", "Sight", "Mag" }; readonly List<string> order = new List<string>(), shuffled = new List<string>(); readonly HashSet<string> placed = new HashSet<string>();
            int next, mistakes; readonly bool hide; float t; string flash = ""; float flashT;
            public AssemblyPuzzle(int n, bool hide)
            {
                this.hide = hide; var pool = new List<string>(names); for (int i = 0; i < n; i++) { int k = Random.Range(0, pool.Count); order.Add(pool[k]); pool.RemoveAt(k); }
                shuffled.AddRange(order); for (int i = shuffled.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]); }
                Title = "Assemble"; Hint = hide ? "Memorise the blueprint, it fades. Then click the parts in order." : "Click the parts in the blueprint's order.";
            }
            public override void Tick(float dt) { t += dt; if (flashT > 0f) flashT -= dt; }
            public override void Draw(Rect r)
            {
                bool showBp = !hide || t < 5f;
                GUI.Label(new Rect(r.x + 20, r.y + 6, r.width - 40, 60), showBp ? "Blueprint: " + string.Join("  →  ", order) : "Blueprint hidden. From memory!  Mistakes: " + mistakes, UiSkin.Label);
                int cols = 3; float w = 150f, h = 56f;
                for (int i = 0; i < shuffled.Count; i++)
                {
                    var p = shuffled[i]; var rc = new Rect(r.x + r.width / 2f - (cols * (w + 10f)) / 2f + (i % cols) * (w + 10f), r.y + 90f + (i / cols) * (h + 10f), w, h);
                    bool done = placed.Contains(p); GUI.enabled = !done && !Done;
                    if (GUI.Button(rc, (done ? "✓ " : "") + p, done ? UiSkin.Button : UiSkin.ButtonOn)) Click(p);
                    GUI.enabled = true;
                }
                if (flashT > 0f) GUI.Label(new Rect(r.x + 20, r.y + 300, 400, 24), flash, UiSkin.Label);
            }
            void Click(string p)
            {
                if (order[next] == p) { placed.Add(p); next++; if (next >= order.Count) { Done = true; Score = Mathf.Clamp01(1f - mistakes * 0.2f); } }
                else { mistakes++; flash = "Wrong part!"; flashT = 0.8f; if (mistakes >= 5) { Done = true; Score = 0.1f; } }
            }
        }
    }
}
