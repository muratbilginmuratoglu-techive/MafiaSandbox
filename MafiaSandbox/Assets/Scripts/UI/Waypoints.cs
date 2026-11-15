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
        LineRenderer qLine, pLine; GameObject pinBeacon; float routeT; Vector3? lastQ, lastP; Vector3 lastFrom;

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

        static Vector3[] Path(Vector3 a, Vector3 b)
        {
            Vector3 sa = a, sb = b;
            if (NavMesh.SamplePosition(a, out var ha, 8f, NavMesh.AllAreas)) sa = ha.position;
            if (NavMesh.SamplePosition(b, out var hb, 8f, NavMesh.AllAreas)) sb = hb.position;
            var path = new NavMeshPath();
            if (NavMesh.CalculatePath(sa, sb, NavMesh.AllAreas, path) && path.corners.Length >= 2) { var l = new List<Vector3>(path.corners); l.Insert(0, a); l.Add(b); return l.ToArray(); }
            return new[] { a, b };
        }
        static LineRenderer MakeLine(string n, Color c)
        {
            var g = new GameObject(n); g.transform.rotation = Quaternion.Euler(90f, 0f, 0f); var lr = g.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default")); lr.startColor = lr.endColor = c; lr.startWidth = lr.endWidth = 0.35f; lr.alignment = LineAlignment.TransformZ; lr.numCornerVertices = 3; lr.useWorldSpace = true; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }
        static void Fill(LineRenderer lr, Vector3[] pts) { if (!lr) return; lr.positionCount = pts.Length; for (int i = 0; i < pts.Length; i++) lr.SetPosition(i, pts[i] + Vector3.up * 0.3f); }

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
                if (QuestRoute.Length > 1 && !qLine) qLine = MakeLine("QuestRouteLine", new Color(1f, 0.82f, 0.15f, 0.8f));
                if (PinRoute.Length > 1 && !pLine) pLine = MakeLine("PinRouteLine", new Color(0.25f, 0.9f, 1f, 0.8f));
                if (qLine) { qLine.gameObject.SetActive(QuestRoute.Length > 1); Fill(qLine, QuestRoute); }
                if (pLine) { pLine.gameObject.SetActive(PinRoute.Length > 1); Fill(pLine, PinRoute); }
            }
            if (pin.HasValue)
            {
                if (!pinBeacon) { pinBeacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder); pinBeacon.name = "PinBeacon"; Destroy(pinBeacon.GetComponent<Collider>()); var m = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.25f, 0.9f, 1f, 0.35f) }; pinBeacon.GetComponent<Renderer>().material = m; pinBeacon.transform.localScale = new Vector3(0.6f, 30f, 0.6f); }
                pinBeacon.SetActive(true); pinBeacon.transform.position = pin.Value + Vector3.up * 30f;
                if (new Vector2(from.x - pin.Value.x, from.z - pin.Value.z).magnitude < 3f) { RemovePin(Pins.Count - 1); gm.Say("You reached your pin."); }
            }
            else if (pinBeacon) pinBeacon.SetActive(false);
        }
        static bool SameT(Vector3? a, Vector3? b) => a.HasValue == b.HasValue && (!a.HasValue || (a.Value - b.Value).sqrMagnitude < 0.25f);
    }
}
