using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Npc;
using MafiaSandbox.Vehicles;
using MafiaSandbox.World;

using MafiaSandbox.Factions;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// North-up minimap (bottom left) and the full city map (M). The base map is painted once into a texture from the road
    /// graph, building footprints and prop colliders; live dots on top: you, police, crew, pedestrians, vehicles.
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        public float worldMinX = -100f, worldMaxX = 100f, worldMinZ = -140f, worldMaxZ = 140f;
        const int TexSize = 1024;
        /// <summary>Right edge of the mini map on screen, so the HUD can sit next to it.</summary>
        public static float MiniRight = 214f;
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
            // faction territories: a light tint of the crew's colour
            foreach (var f in FactionDefs.All)
            {
                var a = ToPx(Data.Coords.ToUnity(f.territory.xMin, 0f, f.territory.yMin)); var b = ToPx(Data.Coords.ToUnity(f.territory.xMax, 0f, f.territory.yMax));
                var tint = Color.Lerp(grass, f.color, 0.28f); FillRect(a, b, tint);
            }
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
            var gm = GameManager.I; if (!gm || !gm.player || map == null || CharacterCreator.IsOpen || IntroCinematic.Playing) return;
            if (GameUI.I && GameUI.I.MapOpen) DrawBig(gm); else DrawMini(gm);
        }

        static void GLine(Vector2 a, Vector2 b, float w, Color c)
        {
            var d = b - a; float len = d.magnitude; if (len < 0.5f) return; float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var old = GUI.matrix; GUIUtility.RotateAroundPivot(ang, a); GUI.color = c; GUI.DrawTexture(new Rect(a.x, a.y - w / 2f, len, w), Texture2D.whiteTexture); GUI.matrix = old; GUI.color = Color.white;
        }
        static void Diamond(Vector2 c, float sz, Color col)
        {
            var old = GUI.matrix; GUIUtility.RotateAroundPivot(45f, c); GUI.color = new Color(0, 0, 0, 0.8f); GUI.DrawTexture(new Rect(c.x - sz / 2 - 2, c.y - sz / 2 - 2, sz + 4, sz + 4), Texture2D.whiteTexture);
            GUI.color = col; GUI.DrawTexture(new Rect(c.x - sz / 2, c.y - sz / 2, sz, sz), Texture2D.whiteTexture); GUI.matrix = old; GUI.color = Color.white;
        }
        static readonly Color QuestCol = new Color(1f, 0.82f, 0.15f), PinCol = new Color(0.25f, 0.9f, 1f);
        void DrawRoute(Vector3[] pts, Color col, System.Func<Vector3, Vector2> sp, float w)
        {
            for (int i = 0; i + 1 < pts.Length; i++) GLine(sp(pts[i]), sp(pts[i + 1]), w, new Color(col.r, col.g, col.b, 0.85f));
        }
        /// <summary>Marker for a waypoint; when it is off the mini map it sticks to the edge pointing the way.</summary>
        static void Marker(Vector2 s, Rect clip, bool clamp, Color col, float sz)
        {
            if (clip.Contains(s)) { Diamond(s, sz, col); return; }
            if (!clamp) return; var c = clip.center; var d = s - c; float k = Mathf.Min((clip.width / 2f - 8f) / Mathf.Max(0.01f, Mathf.Abs(d.x)), (clip.height / 2f - 8f) / Mathf.Max(0.01f, Mathf.Abs(d.y))); Diamond(c + d * k, sz * 0.8f, col);
        }

        void DrawMini(GameManager gm)
        {
            float H = Screen.height; float S = Mathf.Clamp(H * 0.4f, 190f, 380f); float radiusM = 55f;
            var r = new Rect(14, H - 14 - S - 22, S, S); MiniRight = r.xMax + 14f;
            Vector3 p = gm.player.transform.position; Vector2 c = ToPx(p); float half = radiusM * PxPerM;
            // no panel behind the turning map: a fixed frame and a soft shadow instead
            GUI.color = new Color(0f, 0f, 0f, 0.35f); GUI.DrawTexture(new Rect(r.x - 3, r.y - 1, S + 6, S + 6), dot); GUI.color = Color.white;
            // heading-up: the map turns with the camera, so "up" on the minimap is always where you are looking
            var cam = Camera.main; float camYaw = cam ? cam.transform.eulerAngles.y : gm.player.transform.eulerAngles.y; float A = -camYaw * Mathf.Deg2Rad, ca = Mathf.Cos(A), sa = Mathf.Sin(A);
            float scale = S / (2f * radiusM); var mid = new Vector2(S / 2f, S / 2f);
            Vector2 Sp(Vector3 w)   // group-local position: rotate the north-up offset (x right, z up) by -camera yaw
            {
                float x = (w.x - p.x) * scale, y = -(w.z - p.z) * scale;
                return mid + new Vector2(x * ca - y * sa, x * sa + y * ca);
            }
            GUI.BeginGroup(r);
            {
                float E = S * 0.75f; float hE = half * (E / (S / 2f)); var old = GUI.matrix; GUIUtility.RotateAroundPivot(-camYaw, mid);
                GUI.DrawTextureWithTexCoords(new Rect(mid.x - E, mid.y - E, 2f * E, 2f * E), map, new Rect((c.x - hE) / TexSize, (c.y - hE) / TexSize, 2f * hE / TexSize, 2f * hE / TexSize));
                GUI.matrix = old;
                var local = new Rect(0, 0, S, S);
                void Dot(Vector3 w, Color col, float sz) { var s2 = Sp(w); if (!local.Contains(s2)) return; GUI.color = col; GUI.DrawTexture(new Rect(s2.x - sz / 2, s2.y - sz / 2, sz, sz), dot); }
                foreach (var v in FindObjectsByType<VehicleController>(FindObjectsSortMode.None)) if (!v.Controlled) Dot(v.transform.position, new Color(0.96f, 0.83f, 0.42f), 5f);
                foreach (var b2 in CharacterBody.All)
                {
                    if (!b2 || b2.Dead || b2.gameObject == gm.player.gameObject) continue; var br = b2.GetComponent<NpcBrain>();
                    Color col = br is PoliceBrain ? new Color(0.31f, 0.55f, 1f) : br is GangBrain ? new Color(0.88f, 0.31f, 0.31f) : br is FactionBrain fb && fb.Def != null ? fb.Def.color : new Color(0.86f, 0.86f, 0.86f);
                    Dot(b2.transform.position, col, br is PoliceBrain || br is GangBrain || br is FactionBrain ? 6f : 4f);
                }
                var wp = Waypoints.I;
                if (wp != null)
                {
                    DrawRoute(wp.QuestRoute, QuestCol, Sp, 3f); DrawRoute(wp.PinRoute, PinCol, Sp, 3f);
                    if (wp.QuestTarget.HasValue) Marker(Sp(wp.QuestTarget.Value), local, true, QuestCol, 9f);
                    foreach (var pin in wp.Pins) Marker(Sp(pin), local, true, PinCol, 8f);
                }
                // player arrow: its angle relative to the camera
                float rel = gm.player.transform.eulerAngles.y - camYaw; GUIUtility.RotateAroundPivot(rel, mid); GUI.color = Color.white;
                GUI.DrawTexture(new Rect(mid.x - 2, mid.y - 9, 4, 12), dot); GUI.DrawTexture(new Rect(mid.x - 6, mid.y - 3, 12, 4), dot); GUI.matrix = old;
                // north marker on the rim
                var nl = new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }; nl.normal.textColor = new Color(1f, 0.85f, 0.4f);
                var np = mid + new Vector2(0f * ca - (-(S / 2f - 10f)) * sa, 0f * sa + (-(S / 2f - 10f)) * ca); GUI.Label(new Rect(np.x - 8, np.y - 8, 16, 16), "N", nl);
            }
            GUI.EndGroup();
            // frame
            GUI.color = new Color(1f, 0.78f, 0.25f, 0.9f); float fw = 2f;
            GUI.DrawTexture(new Rect(r.x - fw, r.y - fw, S + 2 * fw, fw), dot); GUI.DrawTexture(new Rect(r.x - fw, r.yMax, S + 2 * fw, fw), dot);
            GUI.DrawTexture(new Rect(r.x - fw, r.y, fw, S), dot); GUI.DrawTexture(new Rect(r.xMax, r.y, fw, S), dot);
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x, r.yMax + 6, S, 16), "M map   ·   I backpack", UiSkin.Small);
        }

        void DrawBig(GameManager gm)
        {
            float W = Screen.width, H = Screen.height; float S = Mathf.Max(300f, Mathf.Min(H - 56f, W - 290f));
            var r = new Rect((W - (S + 232f)) / 2f, H / 2f - S / 2f, S, S);
            GUI.color = new Color(0, 0, 0, 0.75f); GUI.DrawTexture(new Rect(0, 0, W, H), dot); GUI.color = Color.white;
            UiSkin.DrawPanel(new Rect(r.x - 8, r.y - 8, S + 16, S + 16), true); GUI.DrawTexture(r, map);
            // faint 50 m grid and a compass tag
            GUI.color = new Color(1f, 1f, 1f, 0.07f);
            for (float gx = Mathf.Ceil(worldMinX / 50f) * 50f; gx < worldMaxX; gx += 50f) GUI.DrawTexture(new Rect(r.x + (gx - worldMinX) / (worldMaxX - worldMinX) * S - 0.5f, r.y, 1f, S), dot);
            for (float gz = Mathf.Ceil(worldMinZ / 50f) * 50f; gz < worldMaxZ; gz += 50f) GUI.DrawTexture(new Rect(r.x, r.yMax - (gz - worldMinZ) / (worldMaxZ - worldMinZ) * S - 0.5f, S, 1f), dot);
            GUI.color = Color.white; var nst = new GUIStyle(UiSkin.H2) { alignment = TextAnchor.MiddleCenter, fontSize = 13 }; nst.normal.textColor = UiSkin.Accent;
            UiSkin.DrawChip(new Rect(r.x + 8, r.y + 8, 56, 22), "▲ N", UiSkin.Accent);
            float sx = S / (worldMaxX - worldMinX), sz = S / (worldMaxZ - worldMinZ);
            Vector2 Sp(Vector3 w) => new Vector2(r.x + (w.x - worldMinX) * sx, r.yMax - (w.z - worldMinZ) * sz);
            void Dot(Vector3 w, Color col, float sz2) { var s = Sp(w); GUI.color = col; GUI.DrawTexture(new Rect(s.x - sz2 / 2, s.y - sz2 / 2, sz2, sz2), dot); }
            foreach (var v in FindObjectsByType<VehicleController>(FindObjectsSortMode.None)) if (!v.Controlled) Dot(v.transform.position, new Color(0.96f, 0.83f, 0.42f), 5f);
            foreach (var b in CharacterBody.All)
            {
                if (!b || b.Dead || b.gameObject == gm.player.gameObject) continue; var br = b.GetComponent<NpcBrain>();
                Dot(b.transform.position, br is PoliceBrain ? new Color(0.31f, 0.55f, 1f) : br is GangBrain ? new Color(0.88f, 0.31f, 0.31f) : br is FactionBrain fb && fb.Def != null ? fb.Def.color : new Color(0.86f, 0.86f, 0.86f), 5f);
            }
            var lab = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; lab.normal.textColor = Color.white;
            foreach (var bi in FindObjectsByType<BuildingInfo>(FindObjectsSortMode.None)) { if (bi.kind == "house") continue; var s = Sp(bi.footprint.center); GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(new Rect(s.x - 42, s.y - 9, 84, 18), dot); GUI.color = Color.white; GUI.Label(new Rect(s.x - 42, s.y - 9, 84, 18), bi.label, lab); }
            foreach (var f in FactionDefs.All) { var c = Data.Coords.ToUnity(f.territory.center.x, 0f, f.territory.center.y + 18f); var s = Sp(c); var ts = new GUIStyle(lab) { fontSize = 13 }; ts.normal.textColor = Color.Lerp(f.color, Color.white, 0.35f); GUI.Label(new Rect(s.x - 60, s.y - 9, 120, 18), f.name, ts); }
            var wp = Waypoints.I; var ev = Event.current;
            if (wp != null)
            {
                DrawRoute(wp.QuestRoute, QuestCol, Sp, 4f); DrawRoute(wp.PinRoute, PinCol, Sp, 4f);
                if (wp.QuestTarget.HasValue) Diamond(Sp(wp.QuestTarget.Value), 13f, QuestCol);
                var pst = new GUIStyle(lab) { fontSize = 11 }; pst.normal.textColor = Color.black;
                for (int pi = 0; pi < wp.Pins.Count; pi++) { var ps = Sp(wp.Pins[pi]); Diamond(ps, 13f, PinCol); GUI.Label(new Rect(ps.x - 10, ps.y - 9, 20, 18), (pi + 1).ToString(), pst); }
                if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition))
                {
                    if (ev.button == 1) { wp.ClearPins(); ev.Use(); }
                    else if (ev.button == 0)
                    {
                        int hit = -1; for (int pi = 0; pi < wp.Pins.Count; pi++) if ((Sp(wp.Pins[pi]) - ev.mousePosition).magnitude < 12f) hit = pi;
                        if (hit >= 0) wp.RemovePin(hit);
                        else { var w = new Vector3(worldMinX + (ev.mousePosition.x - r.x) / sx, gm.player.transform.position.y, worldMinZ + (r.yMax - ev.mousePosition.y) / sz); wp.AddPin(w); }
                        ev.Use();
                    }
                }
                if (r.Contains(ev.mousePosition)) { GUI.color = new Color(0.25f, 0.9f, 1f, 0.5f); GUI.DrawTexture(new Rect(ev.mousePosition.x - 4, ev.mousePosition.y - 4, 8, 8), dot); GUI.color = Color.white; }
            }
            Dot(gm.player.transform.position, Color.white, 9f);
            // legend
            var box = new GUIStyle(UiSkin.PanelSolid) { alignment = TextAnchor.UpperLeft, fontSize = 13, padding = new RectOffset(12, 12, 10, 10) };
            var eco = EconomyManager.I; var ws = WantedSystem.I;
            string legend = "CITY MAP   (M / Esc closes)\n\n";
            var present = new HashSet<string>(); foreach (var pbi in FindObjectsByType<BuildingInfo>(FindObjectsSortMode.None)) present.Add(pbi.kind);   // only the kinds that exist on the trimmed map
            foreach (var kv in KindCol) if (present.Contains(kv.Key)) legend += $"■ {Items.ShopFor(kv.Key)?.title ?? kv.Key}\n";
            legend += "\n● Police   ● Crew   ● Pedestrian   ■ Vehicle\n";
            legend += "\n◆ Mission target (gold route)\n◆ Your pins (cyan route)\nLeft-click: drop / remove a pin\nRight-click: clear all pins\n\n";
            var fm = FactionManager.I; if (fm) foreach (var f in FactionDefs.All) legend += $"■ {f.name}: {FactionManager.Standing(fm.Rep(f))}\n";
            legend += "\n" + (eco ? $"Cash: ${eco.cash}\n" : "") + (ws && ws.Level > 0 ? "Wanted: " + new string('★', ws.Level) : "Wanted: –");
            GUI.color = Color.white; GUI.Box(new Rect(r.xMax + 16, r.y, 210, 500f), legend, box);
            // coloured squares for the legend rows
            int i = 0; foreach (var kv in KindCol) { if (!present.Contains(kv.Key)) continue; GUI.color = kv.Value; GUI.DrawTexture(new Rect(r.xMax + 28, r.y + 46 + i * 17f, 10, 10), dot); i++; }
            GUI.color = Color.white;
        }
    }
}
