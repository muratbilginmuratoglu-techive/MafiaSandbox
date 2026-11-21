using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Economy;
using MafiaSandbox.UI;

namespace MafiaSandbox.Npc
{
    /// <summary>
    /// Speech bubbles over people's heads: small talk, greetings, screams, police shouts. Only the ones near the camera
    /// and in plain view are drawn, a person has one bubble at a time, and they fade in and out.
    /// </summary>
    public class Barks : MonoBehaviour
    {
        class Line { public Transform who; public string text; public float t, dur; public Color col; public float range; public bool visible; public float checkT; }
        static readonly List<Line> lines = new List<Line>();
        static Barks inst;
        GUIStyle style; Texture2D bg, tail;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (inst) return; var go = new GameObject("Barks"); DontDestroyOnLoad(go); inst = go.AddComponent<Barks>(); }

        public static readonly Color Calm = new Color(1f, 1f, 1f), Alarm = new Color(1f, 0.82f, 0.75f), Police = new Color(0.78f, 0.88f, 1f), Gang = new Color(1f, 0.9f, 0.7f);

        /// <summary>Show a line over `who` for `dur` seconds. `range`: how far away it can still be read.</summary>
        public static void Say(Transform who, string text, float dur = 2.8f, Color? col = null, float range = 26f)
        {
            if (!who || string.IsNullOrEmpty(text)) return;
            var cam = Camera.main; if (cam && (cam.transform.position - who.position).sqrMagnitude > range * range * 1.4f) return;   // nobody near enough to hear it
            lines.RemoveAll(l => l.who == who); NpcBrain.Stat("bark");
            lines.Add(new Line { who = who, text = text, t = 0f, dur = Mathf.Max(1.2f, dur), col = col ?? Calm, range = range, checkT = 0f, visible = true });
            if (lines.Count > 40) lines.RemoveAt(0);
        }
        /// <summary>Admin: watch the town for `seconds` (at `speed` x time) and write what people did to NpcReport.log.</summary>
        public static void RunReport(float seconds, float speed) { if (inst) inst.StartCoroutine(inst.Report(seconds, speed)); }
        public static bool Reporting { get; private set; }
        System.Collections.IEnumerator Report(float seconds, float speed)
        {
            if (Reporting) yield break; Reporting = true; NpcBrain.Stats.Clear(); float old = Time.timeScale; Time.timeScale = speed;
            var sb = new System.Text.StringBuilder(); float t0 = Time.realtimeSinceStartup; int samples = 0; var states = new Dictionary<string, int>();
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                yield return new WaitForSecondsRealtime(2f); samples++;
                foreach (var n in FindObjectsByType<NpcBrain>(FindObjectsSortMode.None)) { string k = n.GetType().Name.Replace("Brain", "") + ":" + n.State; states.TryGetValue(k, out var c); states[k] = c + 1; }
            }
            Time.timeScale = old <= 0.01f ? 1f : old;
            int civs = 0, active = 0; foreach (var c in FindObjectsByType<CivilianBrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { civs++; if (c.isActiveAndEnabled) active++; }
            sb.AppendLine($"NPC report: {seconds:0} s real at x{speed:0.#} ({seconds * speed:0} s game), {samples} samples, game hour {(World.DayNightCycle.I ? World.DayNightCycle.I.timeOfDay : 0f):0.0}, civilians active {active}/{civs}");
            sb.AppendLine("--- events (state entries, barks, stuck):");
            var ev = new List<KeyValuePair<string, int>>(NpcBrain.Stats); ev.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key)); foreach (var kv in ev) sb.AppendLine($"  {kv.Key,-34} {kv.Value}");
            sb.AppendLine("--- average people in each state at any moment:");
            var st = new List<KeyValuePair<string, int>>(states); st.Sort((a, b) => b.Value.CompareTo(a.Value)); foreach (var kv in st) sb.AppendLine($"  {kv.Key,-34} {kv.Value / (float)Mathf.Max(1, samples):0.0}");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "..", "NpcReport.log"), sb.ToString()); } catch (System.Exception e) { Debug.LogWarning("NpcReport.log: " + e.Message); }
            Debug.Log(sb.ToString()); Core.GameManager.I?.Banner("NPC REPORT", "written to NpcReport.log", 4f); Reporting = false;
        }
        public static bool Talking(Transform who) { foreach (var l in lines) if (l.who == who && l.t < l.dur) return true; return false; }
        public static string Pick(params string[] options) => options[Random.Range(0, options.Length)];

        void Update()
        {
            var cam = Camera.main; float dt = Time.deltaTime;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                var l = lines[i]; l.t += dt; if (!l.who || l.t >= l.dur) { lines.RemoveAt(i); continue; }
                var b = l.who.GetComponent<Combat.CharacterBody>(); if (b && b.Dead) { lines.RemoveAt(i); continue; }
                l.checkT -= dt;
                if (l.checkT <= 0f && cam) { l.checkT = 0.4f; Vector3 h = l.who.position + Vector3.up * 1.9f; l.visible = (cam.transform.position - h).sqrMagnitude < l.range * l.range && Perception.Clear(cam.transform.position, h); }
            }
        }

        void Styles()
        {
            if (style != null) return;
            style = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = false, padding = new RectOffset(8, 8, 4, 5) };
            style.normal.textColor = new Color(0.08f, 0.08f, 0.1f);
            bg = new Texture2D(1, 1); bg.SetPixel(0, 0, Color.white); bg.Apply();
            const int n = 16; tail = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float w = (y + 0.5f) / n * 0.5f; float dx = Mathf.Abs((x + 0.5f) / n - 0.5f); tail.SetPixel(x, y, new Color(1f, 1f, 1f, dx < w ? 1f : 0f)); }
            tail.Apply();
        }

        void OnGUI()
        {
            var cam = Camera.main; if (!cam || lines.Count == 0 || GameUI.Blocking || Workshop.Active || PhonePanel.IsOpen || CrewPanel.IsOpen || CharacterCreator.IsOpen) return;
            Styles(); GUI.depth = 40;
            // nearest first, at most 8 at once
            var shown = new List<(Line l, float d)>();
            foreach (var l in lines) { if (!l.visible || !l.who) continue; float d = Vector3.Distance(cam.transform.position, l.who.position); shown.Add((l, d)); }
            shown.Sort((a, b) => b.d.CompareTo(a.d));
            int skip = Mathf.Max(0, shown.Count - 8);
            for (int i = skip; i < shown.Count; i++)
            {
                var (l, d) = shown[i]; Vector3 wp = l.who.position + Vector3.up * 2.12f; Vector3 sp = cam.WorldToScreenPoint(wp); if (sp.z <= 0.3f) continue;
                float fade = Mathf.Clamp01(l.t / 0.15f) * Mathf.Clamp01((l.dur - l.t) / 0.35f) * Mathf.Clamp01((l.range - d) / 6f);
                if (fade <= 0.01f) continue;
                float scale = Mathf.Clamp(14f / Mathf.Max(4f, d), 0.75f, 1.15f); style.fontSize = Mathf.RoundToInt(13f * scale);
                var content = new GUIContent(l.text); float w = Mathf.Min(240f * scale, style.CalcSize(content).x + 2f); float h = style.CalcHeight(content, w);
                float x = sp.x - w / 2f, y = Screen.height - sp.y - h - 8f - 6f * (1f - Mathf.Clamp01(l.t / 0.2f));
                GUI.color = new Color(l.col.r, l.col.g, l.col.b, 0.92f * fade); GUI.DrawTexture(new Rect(x, y, w, h), bg); GUI.DrawTexture(new Rect(sp.x - 6f, y + h, 12f, 8f), tail);
                GUI.color = new Color(1f, 1f, 1f, fade); GUI.Label(new Rect(x, y, w, h), content, style);
            }
            GUI.color = Color.white;
        }
    }
}
