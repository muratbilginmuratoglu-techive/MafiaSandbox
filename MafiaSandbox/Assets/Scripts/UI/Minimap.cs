using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Npc;
using MafiaSandbox.Vehicles;
using MafiaSandbox.World;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// North-up minimap (bottom left) and the full city map (M). The base map is painted once into a texture from the road
    /// graph, building footprints and prop colliders; live dots on top: you, police, crew, pedestrians, vehicles.
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        public float worldMinX = -100f, worldMaxX = 100f, worldMinZ = -140f, worldMaxZ = 140f;
        const int TexSize = 512;
        Texture2D map, dot; Color32[] px;
        static readonly Dictionary<string, Color> KindCol = new Dictionary<string, Color>
        {
            { "market", new Color(0.47f, 0.80f, 0.42f) }, { "gun", new Color(0.85f, 0.35f, 0.30f) }, { "pawn", new Color(0.85f, 0.70f, 0.30f) }, { "cafe", new Color(0.80f, 0.55f, 0.35f) },
            { "restaurant", new Color(0.90f, 0.45f, 0.55f) }, { "pharmacy", new Color(0.40f, 0.80f, 0.80f) }, { "jeweler", new Color(0.75f, 0.55f, 0.95f) }, { "bank", new Color(0.35f, 0.55f, 0.95f) },
            { "barber", new Color(0.55f, 0.75f, 0.95f) }, { "house", new Color(0.72f, 0.72f, 0.70f) }, { "police", new Color(0.30f, 0.45f, 0.90f) }, { "home", new Color(0.95f, 0.85f, 0.40f) }
        };

        void Start() { Build(); dot = Texture2D.whiteTexture; }

        Vector2 ToPx(Vector3 w) => new Vector2((w.x - worldMinX) / (worldMaxX - worldMinX) * TexSize, (w.z - worldMinZ) / (worldMaxZ - worldMinZ) * TexSize);
        float PxPerM => TexSize / (worldMaxX - worldMinX);

        void Build()
        {
            map = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            px = new Color32[TexSize * TexSize]; var grass = new Color32(118, 150, 104, 255); for (int i = 0; i < px.Length; i++) px[i] = grass;
            var graph = FindAnyObjectByType<RoadGraph>();
            if (graph && graph.roadNodes != null)
            {
                var kerb = new Color32(176, 178, 182, 255); var road = new Color32(74, 78, 87, 255);
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < graph.roadNodes.Length; i++) foreach (int j in graph.roadNodes[i].adj ?? new int[0]) if (j > i)
                        Line(ToPx(graph.roadNodes[i].pos), ToPx(graph.roadNodes[j].pos), pass == 0 ? 14f * PxPerM : 10f * PxPerM, pass == 0 ? kerb : road);
            }
            foreach (var c in FindObjectsByType<CityCollider>(FindObjectsSortMode.None))
            {
                var bc = c.GetComponent<BoxCollider>(); if (!bc) continue; var b = bc.bounds; if (b.size.x > 3f || b.size.z > 3f) continue;
                FillRect(ToPx(b.min), ToPx(b.max), new Color32(96, 110, 90, 255));
            }
            foreach (var bi in FindObjectsByType<BuildingInfo>(FindObjectsSortMode.None))
            {
                var col = KindCol.TryGetValue(bi.kind, out var k) ? k : Color.gray; var b = bi.footprint;
                FillRect(ToPx(b.min), ToPx(b.max), col);
            }
            map.SetPixels32(px); map.Apply(); px = null;
        }

        void FillRect(Vector2 a, Vector2 b, Color col)
        {
            int x0 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(a.x, b.x)), 0, TexSize - 1), x1 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(a.x, b.x)), 0, TexSize - 1);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(a.y, b.y)), 0, TexSize - 1), y1 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(a.y, b.y)), 0, TexSize - 1);
            Color32 c = col; for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) px[y * TexSize + x] = c;
        }
        void Line(Vector2 a, Vector2 b, float width, Color32 c)
        {
            float hw = width * 0.5f; int x0 = Mathf.Clamp((int)(Mathf.Min(a.x, b.x) - hw), 0, TexSize - 1), x1 = Mathf.Clamp((int)(Mathf.Max(a.x, b.x) + hw), 0, TexSize - 1);
            int y0 = Mathf.Clamp((int)(Mathf.Min(a.y, b.y) - hw), 0, TexSize - 1), y1 = Mathf.Clamp((int)(Mathf.Max(a.y, b.y) + hw), 0, TexSize - 1);
            Vector2 d = b - a; float len2 = Mathf.Max(0.001f, d.sqrMagnitude);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x, y); float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / len2); if ((a + d * t - p).sqrMagnitude <= hw * hw) px[y * TexSize + x] = c;
            }
        }

        void OnGUI()
        {
            var gm = GameManager.I; if (!gm || !gm.player || map == null) return;
            if (GameUI.I && GameUI.I.MapOpen) DrawBig(gm); else DrawMini(gm);
        }

        void DrawMini(GameManager gm)
        {
            float H = Screen.height; const float S = 190f; float radiusM = 55f;
            var r = new Rect(12, H - 12 - S - 84, S, S);
            Vector3 p = gm.player.transform.position; Vector2 c = ToPx(p); float half = radiusM * PxPerM;
            GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(new Rect(r.x - 3, r.y - 3, S + 6, S + 6), dot); GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(r, map, new Rect((c.x - half) / TexSize, (c.y - half) / TexSize, 2f * half / TexSize, 2f * half / TexSize));
            // live dots (north up: +z = up on screen)
            float scale = S / (2f * radiusM);
            Vector2 Sp(Vector3 w) => new Vector2(r.x + S / 2f + (w.x - p.x) * scale, r.y + S / 2f - (w.z - p.z) * scale);
            void Dot(Vector3 w, Color col, float sz) { var s = Sp(w); if (s.x < r.x + 2 || s.x > r.xMax - 2 || s.y < r.y + 2 || s.y > r.yMax - 2) return; GUI.color = col; GUI.DrawTexture(new Rect(s.x - sz / 2, s.y - sz / 2, sz, sz), dot); }
            foreach (var v in FindObjectsByType<VehicleController>(FindObjectsSortMode.None)) if (!v.Controlled) Dot(v.transform.position, new Color(0.96f, 0.83f, 0.42f), 5f);
            foreach (var b in CharacterBody.All)
            {
                if (!b || b.Dead || b.gameObject == gm.player.gameObject) continue; var br = b.GetComponent<NpcBrain>();
                Color col = br is PoliceBrain ? new Color(0.31f, 0.55f, 1f) : br is GangBrain ? new Color(0.88f, 0.31f, 0.31f) : new Color(0.86f, 0.86f, 0.86f);
                Dot(b.transform.position, col, br is PoliceBrain || br is GangBrain ? 6f : 4f);
            }
            // player arrow
            var ps = new Vector2(r.x + S / 2f, r.y + S / 2f); float yaw = gm.player.transform.eulerAngles.y;
            GUIUtility.RotateAroundPivot(yaw, ps); GUI.color = Color.white; GUI.DrawTexture(new Rect(ps.x - 2, ps.y - 9, 4, 12), dot); GUI.DrawTexture(new Rect(ps.x - 6, ps.y - 3, 12, 4), dot); GUIUtility.RotateAroundPivot(-yaw, ps);
            GUI.color = Color.white;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 11 }; st.normal.textColor = new Color(1, 1, 1, 0.7f);
            GUI.Label(new Rect(r.x, r.yMax + 2, S, 16), "M map", st);
        }

        void DrawBig(GameManager gm)
        {
            float W = Screen.width, H = Screen.height; float S = Mathf.Min(H - 60f, 700f);
            var r = new Rect(W / 2f - S / 2f - 90, H / 2f - S / 2f, S, S);
            GUI.color = new Color(0, 0, 0, 0.75f); GUI.DrawTexture(new Rect(0, 0, W, H), dot); GUI.color = Color.white;
            GUI.DrawTexture(r, map);
            float sx = S / (worldMaxX - worldMinX), sz = S / (worldMaxZ - worldMinZ);
            Vector2 Sp(Vector3 w) => new Vector2(r.x + (w.x - worldMinX) * sx, r.yMax - (w.z - worldMinZ) * sz);
            void Dot(Vector3 w, Color col, float sz2) { var s = Sp(w); GUI.color = col; GUI.DrawTexture(new Rect(s.x - sz2 / 2, s.y - sz2 / 2, sz2, sz2), dot); }
            foreach (var v in FindObjectsByType<VehicleController>(FindObjectsSortMode.None)) if (!v.Controlled) Dot(v.transform.position, new Color(0.96f, 0.83f, 0.42f), 5f);
            foreach (var b in CharacterBody.All)
            {
                if (!b || b.Dead || b.gameObject == gm.player.gameObject) continue; var br = b.GetComponent<NpcBrain>();
                Dot(b.transform.position, br is PoliceBrain ? new Color(0.31f, 0.55f, 1f) : br is GangBrain ? new Color(0.88f, 0.31f, 0.31f) : new Color(0.86f, 0.86f, 0.86f), 5f);
            }
            var lab = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; lab.normal.textColor = Color.white;
            foreach (var bi in FindObjectsByType<BuildingInfo>(FindObjectsSortMode.None)) { if (bi.kind == "house") continue; var s = Sp(bi.footprint.center); GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(new Rect(s.x - 42, s.y - 9, 84, 18), dot); GUI.color = Color.white; GUI.Label(new Rect(s.x - 42, s.y - 9, 84, 18), bi.label, lab); }
            Dot(gm.player.transform.position, Color.white, 9f);
            // legend
            var box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, padding = new RectOffset(10, 10, 8, 8) }; box.normal.textColor = Color.white;
            var eco = EconomyManager.I; var ws = WantedSystem.I;
            string legend = "CITY MAP   (M / Esc closes)\n\n";
            foreach (var kv in KindCol) legend += $"■ {Items.ShopFor(kv.Key)?.title ?? kv.Key}\n";
            legend += "\n● Police   ● Crew   ● Pedestrian   ■ Vehicle\n\n" + (eco ? $"Cash: ${eco.cash}\n" : "") + (ws && ws.Level > 0 ? "Wanted: " + new string('★', ws.Level) : "Wanted: –");
            GUI.color = Color.white; GUI.Box(new Rect(r.xMax + 16, r.y, 190, 330), legend, box);
            // coloured squares for the legend rows
            int i = 0; foreach (var kv in KindCol) { GUI.color = kv.Value; GUI.DrawTexture(new Rect(r.xMax + 26, r.y + 44 + i * 17f, 10, 10), dot); i++; }
            GUI.color = Color.white;
        }
    }
}
