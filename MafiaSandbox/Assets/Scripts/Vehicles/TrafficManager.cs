using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.World;

namespace MafiaSandbox.Vehicles
{
    /// <summary>
    /// Shared traffic state: the road graph, junction reservations (one vehicle crosses a junction at a time, with a stall
    /// breaker so nobody waits forever) and the list of AI drivers. Mirrors the prototype's grant system.
    /// </summary>
    public class TrafficManager : MonoBehaviour
    {
        public static TrafficManager I { get; private set; }
        public RoadGraph graph;
        public float laneOffset = 1.9f;        // right-hand traffic, metres from the centre line
        public int maxCars = 12;               // the little grid jams up with more
        public readonly List<TrafficDriver> drivers = new List<TrafficDriver>();

        readonly Dictionary<int, TrafficDriver> tokens = new Dictionary<int, TrafficDriver>();
        readonly Dictionary<int, float> tokenSince = new Dictionary<int, float>();

        void Awake() { I = this; if (!graph) graph = GetComponent<RoadGraph>() ?? FindAnyObjectByType<RoadGraph>(); }

        /// <summary>A random spot on a random edge's right-hand lane, away from `avoid`, with no other vehicle within 7 m. Null if unlucky.</summary>
        public bool RandomLaneSpot(Vector3 avoid, float minDist, out int a, out int b, out Vector3 pos, out Quaternion rot)
        {
            var nodes = Nodes; a = b = -1; pos = Vector3.zero; rot = Quaternion.identity; if (nodes == null) return false;
            int vmask = LayerMask.GetMask("Vehicles");
            for (int tries = 0; tries < 24; tries++)
            {
                int i = Random.Range(0, nodes.Length); if (nodes[i].adj == null || nodes[i].adj.Length == 0) continue;
                int j = nodes[i].adj[Random.Range(0, nodes[i].adj.Length)]; if (IsDeadEnd(j) || IsDeadEnd(i)) continue;
                Vector3 pa = nodes[i].pos, pb = nodes[j].pos; Vector3 d = pb - pa; d.y = 0f; float len = d.magnitude; if (len < 20f) continue; d /= len;
                float t = Random.Range(0.2f, 0.8f); Vector3 p = Vector3.Lerp(pa, pb, t) + Vector3.Cross(Vector3.up, d) * laneOffset;
                if ((p - avoid).sqrMagnitude < minDist * minDist) continue;
                if (Physics.CheckSphere(p + Vector3.up, 7f, vmask, QueryTriggerInteraction.Ignore)) continue;
                a = i; b = j; pos = p; rot = Quaternion.LookRotation(d, Vector3.up); return true;
            }
            return false;
        }

        /// <summary>A driver found a wall across this edge (e.g. the tunnel portal): drop it from the graph for everyone.</summary>
        public void BlockEdge(int a, int b)
        {
            var nodes = Nodes; if (nodes == null || a < 0 || b < 0 || a >= nodes.Length || b >= nodes.Length) return;
            if (System.Array.IndexOf(nodes[a].adj, b) < 0) return;
            nodes[a].adj = System.Array.FindAll(nodes[a].adj, x => x != b); nodes[b].adj = System.Array.FindAll(nodes[b].adj, x => x != a);
            Debug.Log($"[Traffic] road edge {a}<->{b} is walled off, cars won't use it any more");
        }

        public RoadGraph.Node[] Nodes => graph ? graph.roadNodes : null;
        public bool IsJunction(int i) => Nodes != null && i >= 0 && i < Nodes.Length && Nodes[i].adj != null && Nodes[i].adj.Length >= 3;
        public bool IsDeadEnd(int i) => Nodes != null && i >= 0 && i < Nodes.Length && (Nodes[i].adj == null || Nodes[i].adj.Length <= 1);

        public int Nearest(Vector3 p) => graph ? graph.Nearest(graph.roadNodes, p) : -1;

        /// <summary>Pick where to go after `cur`, coming from `prev`: prefer going straight, avoid U-turns unless it's a dead end.</summary>
        public int NextNode(int cur, int prev)
        {
            var n = Nodes[cur]; if (n.adj == null || n.adj.Length == 0) return prev >= 0 ? prev : cur;
            if (n.adj.Length == 1) return n.adj[0];
            Vector3 inDir = prev >= 0 ? (n.pos - Nodes[prev].pos).normalized : Vector3.zero;
            float total = 0f; var w = new float[n.adj.Length];
            for (int i = 0; i < n.adj.Length; i++)
            {
                int a = n.adj[i]; if (a == prev) { w[i] = 0f; continue; }
                Vector3 d = (Nodes[a].pos - n.pos).normalized;
                float straight = prev >= 0 ? Mathf.Clamp01((Vector3.Dot(inDir, d) + 1f) * 0.5f) : 1f;   // 1 = straight on, 0 = back
                w[i] = 0.25f + straight * straight * 1.5f;
                if (IsDeadEnd(a)) w[i] *= 0.08f;                 // stubs and dead ends: rarely worth the U-turn
                total += w[i];
            }
            if (total <= 0f) return prev;
            float r = Random.value * total;
            for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0f && w[i] > 0f) return n.adj[i]; }
            for (int i = 0; i < w.Length; i++) if (w[i] > 0f) return n.adj[i];
            return prev;
        }

        /// <summary>Ask to enter a junction. Granted if free, already ours, or the holder has been sitting on it for too long.</summary>
        public bool Request(int junction, TrafficDriver who)
        {
            if (!tokens.TryGetValue(junction, out var holder) || holder == null || holder == who || !holder.isActiveAndEnabled || Time.time - tokenSince[junction] > 9f)
            {
                tokens[junction] = who; if (holder != who) tokenSince[junction] = Time.time; return true;
            }
            return false;
        }

        public void Release(int junction, TrafficDriver who) { if (tokens.TryGetValue(junction, out var h) && h == who) tokens.Remove(junction); }
    }
}
