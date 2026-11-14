using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Data;

namespace MafiaSandbox.Factions
{
    public enum FactionKind { Gang, Mafia }

    /// <summary>
    /// One crew: where it lives (territory rectangle in prototype coordinates, mirrored to Unity), its headquarters and
    /// hang-out spots, who it likes. Two blocs: the Moretti family runs the west side with the Blue and Green gangs, the
    /// Kaplan family runs the east side with the Yellow and Red gangs. Gangs of the same bloc tolerate each other, the
    /// blocs hate each other.
    /// </summary>
    public class FactionDef
    {
        public string id, name, prefabPrefix; public FactionKind kind; public Color color;
        public Rect territory;                    // prototype coords (x, z)
        public Vector2 hq;                        // HQ door-front (prototype coords)
        public Vector2[] spots;                   // loiter points (prototype coords)
        public Vector2[] patrol;                  // patrol loop (prototype coords)
        public string[] allies, enemies;          // faction ids
        public int members; public string bloc;

        public Vector3 HqUnity => Coords.ToUnity(hq.x, 0f, hq.y);
        public bool Contains(Vector3 unityPos) { var p = Coords.FromUnity(unityPos); return territory.Contains(new Vector2(p.x, p.z)); }
    }

    public static class FactionDefs
    {
        public static readonly List<FactionDef> All = new List<FactionDef>();
        static readonly Dictionary<string, FactionDef> byId = new Dictionary<string, FactionDef>();

        // prototype-coordinate districts (the HTML city is x ∈ [-78, 78]; the new districts sit beyond on both sides)
        // west side: x ∈ [-160, -78];  east side: x ∈ [78, 160]
        static FactionDefs()
        {
            // block layout (DistrictBuilder): north gang block z ∈ [7,43] (bar door on the z=50 road), family block z ∈ [-43,-7]
            // (restaurant door on the z=0 road), south gang block z ∈ [-93,-57] (bar door on the z=-50 road) + scrap yard z ∈ [-121,-107]
            Side(-1, "mavi", "Mavi Akrepler", "Cete_Mavi", new Color(0.2f, 0.45f, 0.95f), "moretti", "Moretti Ailesi", "Mafya_Moretti", new Color(0.55f, 0.08f, 0.14f),
                 "yesil", "Yeşil Yılanlar", "Cete_Yesil", new Color(0.15f, 0.65f, 0.3f), "west", new[] { "kaplan", "sari", "kirmizi" });
            Side(1, "sari", "Sarı Çakallar", "Cete_Sari", new Color(0.95f, 0.78f, 0.15f), "kaplan", "Kaplan Ailesi", "Mafya_Kaplan", new Color(0.85f, 0.75f, 0.5f),
                 "kirmizi", "Kızıl Kurtlar", "Cete_Kirmizi", new Color(0.9f, 0.15f, 0.15f), "east", new[] { "moretti", "mavi", "yesil" });
        }

        static void Side(int s, string gN, string gNName, string gNPrefab, Color gNCol, string m, string mName, string mPrefab, Color mCol, string gS, string gSName, string gSPrefab, Color gSCol, string bloc, string[] enemies)
        {
            float xa = s < 0 ? -168f : 78f, xb = s < 0 ? -78f : 168f;
            Vector2 P(float x, float z) => new Vector2(s * x, z);
            // north gang: bar at (124, 38) with its door at z = 42, yard south of it, depot east
            Add(new FactionDef { id = gN, name = gNName, kind = FactionKind.Gang, color = gNCol, prefabPrefix = gNPrefab, bloc = bloc,
                territory = Rect.MinMaxRect(Mathf.Min(xa, xb), 4f, Mathf.Max(xa, xb), 60f), hq = P(124f, 45f),
                spots = new[] { P(124f, 45.5f), P(121f, 45.5f), P(126f, 25f), P(123f, 20f), P(129f, 27f), P(137f, 19f), P(120f, 29f) },
                patrol = new[] { P(116f, 44f), P(154f, 44f), P(154f, 6f), P(116f, 6f), P(104f, 6f), P(84f, 6f), P(84f, 44f), P(104f, 44f) },
                allies = new[] { m, gS }, enemies = enemies, members = 8 });
            // the family: restaurant at (124, -12.5) with its door at (123, -7.5) on the z=0 road, villas and the fountain behind
            Add(new FactionDef { id = m, name = mName, kind = FactionKind.Mafia, color = mCol, prefabPrefix = mPrefab, bloc = bloc,
                territory = Rect.MinMaxRect(Mathf.Min(xa, xb), -46f, Mathf.Max(xa, xb), 4f), hq = P(123f, -5f),
                spots = new[] { P(121f, -5.5f), P(125.5f, -5.5f), P(124f, -25f), P(120f, -29f), P(128f, -29f), P(135f, -16f), P(118f, -20f) },
                patrol = new[] { P(116f, -6f), P(154f, -6f), P(154f, -44f), P(116f, -44f), P(104f, -44f), P(84f, -44f), P(84f, -6f), P(104f, -6f) },
                allies = new[] { gN, gS }, enemies = enemies, members = 8 });
            // south gang: bar at (124, -62) with its door at z = -58, yard south, scrap yard across the z=-100 road
            Add(new FactionDef { id = gS, name = gSName, kind = FactionKind.Gang, color = gSCol, prefabPrefix = gSPrefab, bloc = bloc,
                territory = Rect.MinMaxRect(Mathf.Min(xa, xb), -134f, Mathf.Max(xa, xb), -46f), hq = P(124f, -55f),
                spots = new[] { P(124f, -54.5f), P(121f, -54.5f), P(126f, -75f), P(123f, -80f), P(129f, -73f), P(137f, -81f), P(130f, -114f), P(144f, -112f) },
                patrol = new[] { P(116f, -56f), P(154f, -56f), P(154f, -104f), P(116f, -104f), P(104f, -104f), P(84f, -104f), P(84f, -56f), P(104f, -56f) },
                allies = new[] { m, gN }, enemies = enemies, members = 8 });
        }

        static void Add(FactionDef f) { All.Add(f); byId[f.id] = f; }
        public static FactionDef Get(string id) => id != null && byId.TryGetValue(id, out var f) ? f : null;
        public static FactionDef At(Vector3 unityPos) { foreach (var f in All) if (f.Contains(unityPos)) return f; return null; }
        public static bool AreAllies(FactionDef a, FactionDef b) => a == b || (a != null && b != null && System.Array.IndexOf(a.allies, b.id) >= 0);
        public static bool AreEnemies(FactionDef a, FactionDef b) => a != null && b != null && a != b && System.Array.IndexOf(a.enemies, b.id) >= 0;
    }
}
