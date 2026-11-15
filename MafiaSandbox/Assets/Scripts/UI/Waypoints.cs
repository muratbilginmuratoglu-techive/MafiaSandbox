using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MafiaSandbox.Core;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// Where to go: the current quest target (gold) and the pins the player drops on the map (cyan, the newest is the active one).
    /// Routes are walked over the NavMesh, drawn as a line on the ground and on both maps.
    /// </summary>
    public class Waypoints : MonoBehaviour
    {
        public static Waypoints I { get; private set; }
        public Vector3? QuestTarget;
        public readonly List<Vector3> Pins = new List<Vector3>();
        public Vector3[] QuestRoute = new Vector3[0], PinRoute = new Vector3[0];
        public const int MaxPins = 8; const string Key = "ms_pins_v1";
        float routeT; Vector3? lastQ, lastP; Vector3 lastFrom;

        void Awake() { I = this; Load(); }
        public Vector3? ActivePin => Pins.Count > 0 ? Pins[Pins.Count - 1] : (Vector3?)null;
        public void AddPin(Vector3 p) { if (Pins.Count >= MaxPins) Pins.RemoveAt(0); Pins.Add(p); Save(); routeT = 0f; GameManager.I?.Say($"Pin dropped ({Pins.Count}). Follow the cyan line."); }
        public void RemovePin(int i) { if (i < 0 || i >= Pins.Count) return; Pins.RemoveAt(i); Save(); routeT = 0f; }
        public void ClearPins() { Pins.Clear(); Save(); routeT = 0f; }
        void Save() { var sb = new System.Text.StringBuilder(); var ci = System.Globalization.CultureInfo.InvariantCulture; foreach (var p in Pins) sb.Append(p.x.ToString(ci)).Append(',').Append(p.y.ToString(ci)).Append(',').Append(p.z.ToString(ci)).Append(';'); PlayerPrefs.SetString(Key, sb.ToString()); PlayerPrefs.Save(); }
        void Load()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture; var ns = System.Globalization.NumberStyles.Float;
            foreach (var part in PlayerPrefs.GetString(Key, "").Split(';')) { var q = part.Split(','); if (q.Length == 3 && float.TryParse(q[0], ns, ci, out float x) && float.TryParse(q[1], ns, ci, out float y) && float.TryParse(q[2], ns, ci, out float z)) Pins.Add(new Vector3(x, y, z)); }
        }

        /// <summary>Route along the streets: snap both ends onto the nearest road edge, Dijkstra over the road graph, then the short hops off the road.</summary>
        static Vector3[] Path(Vector3 a, Vector3 b)
        {
            var g = Object.FindAnyObjectByType<MafiaSandbox.World.RoadGraph>(); var n = g != null ? g.roadNodes : null;
            if (n == null || n.Length < 2) return new[] { a, b };
            int N = n.Length, S = N, T = N + 1; var pos = new Vector3[N + 2]; for (int i = 0; i < N; i++) pos[i] = n[i].pos;
            var extra = new List<(int u, int v)>[2] { new List<(int, int)>(), new List<(int, int)>() };
            Vector3 Snap(Vector3 p, int slot, out int ea, out int eb)
            {
                float best = float.MaxValue; Vector3 bp = p; ea = eb = -1;
                for (int i = 0; i < N; i++) if (n[i].adj != null) foreach (int j in n[i].adj) if (j > i)
                {
                    Vector3 pa = n[i].pos, pb = n[j].pos; pa.y = pb.y = p.y; var ab = pb - pa; float t = Mathf.Clamp01(Vector3.Dot(p - pa, ab) / Mathf.Max(0.001f, ab.sqrMagnitude)); var q = pa + ab * t; float d = (q - p).sqrMagnitude;
                    if (d < best) { best = d; bp = q; ea = i; eb = j; }
                }
                return bp;
            }
            pos[S] = Snap(a, 0, out int sa, out int sb); pos[T] = Snap(b, 1, out int ta, out int tb);
            var dist = new float[N + 2]; var prev = new int[N + 2]; var done = new bool[N + 2]; for (int i = 0; i < dist.Length; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            dist[S] = 0f;
            for (int it = 0; it < N + 2; it++)
            {
                int u = -1; float bd = float.MaxValue; for (int i = 0; i < N + 2; i++) if (!done[i] && dist[i] < bd) { bd = dist[i]; u = i; }
                if (u < 0 || u == T) break; done[u] = true;
                void Relax(int w) { float d = dist[u] + Vector3.Distance(pos[u], pos[w]); if (d < dist[w]) { dist[w] = d; prev[w] = u; } }
                if (u < N) { if (n[u].adj != null) foreach (int w in n[u].adj) { if (u == sa && w == sb || u == sb && w == sa) { } Relax(w); } if (u == ta || u == tb) Relax(T); if (u == sa || u == sb) { } }
                else if (u == S) { Relax(sa); Relax(sb); if (sa == ta && sb == tb) Relax(T); }
            }
            if (prev[T] < 0 && dist[T] == float.MaxValue) return new[] { a, b };
            var chain = new List<Vector3> { b }; for (int c = T; c >= 0; c = prev[c]) chain.Add(pos[c]); chain.Add(a); chain.Reverse();
            for (int i = 0; i < chain.Count; i++) { var q = chain[i]; if (NavMesh.SamplePosition(q, out var h, 3f, NavMesh.AllAreas)) q.y = h.position.y; chain[i] = q; }
            return chain.ToArray();
        }

        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player) return; var from = gm.player.transform.position;
            Vector3? pin = ActivePin; routeT -= Time.deltaTime;
            bool changed = !SameT(QuestTarget, lastQ) || !SameT(pin, lastP);
            if (changed || routeT <= 0f && (from - lastFrom).sqrMagnitude > 4f || routeT < -4f)
            {
                routeT = 1.2f; lastFrom = from; lastQ = QuestTarget; lastP = pin;
                QuestRoute = QuestTarget.HasValue ? Path(from, QuestTarget.Value) : new Vector3[0];
                PinRoute = pin.HasValue ? Path(from, pin.Value) : new Vector3[0];
            }
            if (pin.HasValue)
            {
                if (new Vector2(from.x - pin.Value.x, from.z - pin.Value.z).magnitude < 3f) { RemovePin(Pins.Count - 1); gm.Say("You reached your pin."); }
            }
        }
        static bool SameT(Vector3? a, Vector3? b) => a.HasValue == b.HasValue && (!a.HasValue || (a.Value - b.Value).sqrMagnitude < 0.25f);
    }
}
