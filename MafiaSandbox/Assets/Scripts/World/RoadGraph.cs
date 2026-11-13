using UnityEngine;

namespace MafiaSandbox.World
{
    /// <summary>Vehicle and pedestrian waypoint graphs from the prototype (Unity space). Traffic (step 4) and NPC wander (step 3) read these.</summary>
    public class RoadGraph : MonoBehaviour
    {
        [System.Serializable] public class Node { public Vector3 pos; public int[] adj; }
        public Node[] roadNodes;
        public Node[] pedNodes;

        public int Nearest(Node[] nodes, Vector3 p)
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++) { float d = (nodes[i].pos - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        void OnDrawGizmosSelected()
        {
            Draw(roadNodes, Color.yellow); Draw(pedNodes, Color.cyan);
        }
        void Draw(Node[] nodes, Color c)
        {
            if (nodes == null) return; Gizmos.color = c;
            foreach (var n in nodes) { Gizmos.DrawSphere(n.pos + Vector3.up * 0.3f, 0.4f); if (n.adj != null) foreach (var a in n.adj) if (a >= 0 && a < nodes.Length) Gizmos.DrawLine(n.pos + Vector3.up * 0.3f, nodes[a].pos + Vector3.up * 0.3f); }
        }
    }
}
