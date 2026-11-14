using UnityEngine;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// Shared look for every IMGUI panel: the prototype's dark glass panels (rgba(14,20,32,.72) with a faint 1 px border,
    /// 10 px radius), rounded bars, pill chips and flat buttons. Textures are generated once (9-sliced rounded rects).
    /// </summary>
    public static class UiSkin
    {
        public static readonly Color PanelCol = new Color(0.055f, 0.078f, 0.125f, 0.78f), PanelSolidCol = new Color(0.07f, 0.09f, 0.13f, 0.96f), Border = new Color(1f, 1f, 1f, 0.12f);
        public static readonly Color Text = new Color(0.93f, 0.94f, 0.96f), Dim = new Color(0.62f, 0.66f, 0.74f), Accent = new Color(1f, 0.78f, 0.25f), Green = new Color(0.45f, 0.86f, 0.52f), Red = new Color(0.95f, 0.33f, 0.3f), Blue = new Color(0.36f, 0.62f, 0.95f);

        public static Texture2D White => Texture2D.whiteTexture;
        static Texture2D panelTex, panelSolidTex, pillTex, barTex, barFillTex, btnTex, btnHoverTex, btnOnTex;
        static GUIStyle panel, panelSolid, chip, label, labelDim, labelBold, title, h2, button, buttonOn, small, center, big, mono;
        static Font font;

        public static GUIStyle Panel => panel ?? (panel = Box(PanelTex(), 12));
        public static GUIStyle PanelSolid => panelSolid ?? (panelSolid = Box(PanelSolidTex(), 12));
        public static GUIStyle Chip { get { if (chip == null) { chip = Box(PillTex(), 10); chip.alignment = TextAnchor.MiddleCenter; chip.fontSize = 12; chip.padding = new RectOffset(10, 10, 3, 3); chip.normal.textColor = Text; } return chip; } }
        public static GUIStyle Label => label ?? (label = Lbl(14, Text));
        public static GUIStyle LabelDim => labelDim ?? (labelDim = Lbl(13, Dim));
        public static GUIStyle LabelBold { get { if (labelBold == null) { labelBold = Lbl(14, Text); labelBold.fontStyle = FontStyle.Bold; } return labelBold; } }
        public static GUIStyle Title { get { if (title == null) { title = Lbl(21, Text); title.fontStyle = FontStyle.Bold; } return title; } }
        public static GUIStyle H2 { get { if (h2 == null) { h2 = Lbl(12, Dim); h2.fontStyle = FontStyle.Bold; } return h2; } }
        public static GUIStyle Small => small ?? (small = Lbl(11, Dim));
        public static GUIStyle Center { get { if (center == null) { center = Lbl(14, Text); center.alignment = TextAnchor.MiddleCenter; } return center; } }
        public static GUIStyle Big { get { if (big == null) { big = Lbl(20, Text); big.alignment = TextAnchor.MiddleCenter; big.fontStyle = FontStyle.Bold; } return big; } }
        public static GUIStyle Mono { get { if (mono == null) { mono = Lbl(13, Text); mono.alignment = TextAnchor.MiddleCenter; } return mono; } }
        public static GUIStyle Button
        {
            get
            {
                if (button == null)
                {
                    button = new GUIStyle(GUI.skin.button) { fontSize = 13, padding = new RectOffset(10, 10, 4, 4), border = new RectOffset(8, 8, 8, 8), alignment = TextAnchor.MiddleCenter };
                    button.normal.background = BtnTex(); button.hover.background = BtnHoverTex(); button.active.background = BtnOnTex(); button.focused.background = BtnTex();
                    button.normal.textColor = Text; button.hover.textColor = Color.white; button.active.textColor = Color.white; button.focused.textColor = Text;
                    button.onNormal.background = BtnOnTex(); button.onHover.background = BtnOnTex(); button.onActive.background = BtnOnTex(); button.onFocused.background = BtnOnTex();
                    button.onNormal.textColor = Color.white; button.onHover.textColor = Color.white; button.onFocused.textColor = Color.white; button.onActive.textColor = Color.white;
                }
                return button;
            }
        }
        public static GUIStyle ButtonOn { get { if (buttonOn == null) { buttonOn = new GUIStyle(Button); buttonOn.normal.background = BtnOnTex(); buttonOn.hover.background = BtnOnTex(); buttonOn.normal.textColor = Color.white; } return buttonOn; } }

        static GUIStyle Lbl(int size, Color c) { var s = new GUIStyle(GUI.skin.label) { fontSize = size, richText = true, wordWrap = false }; s.normal.textColor = c; s.hover.textColor = c; return s; }
        static GUIStyle Box(Texture2D tex, int border)
        {
            var s = new GUIStyle(GUI.skin.box) { border = new RectOffset(border, border, border, border), padding = new RectOffset(12, 12, 8, 8), alignment = TextAnchor.MiddleLeft, fontSize = 13, richText = true };
            s.normal.background = tex; s.normal.textColor = Text; s.hover.background = tex; s.hover.textColor = Text; return s;
        }

        public static Texture2D PanelTex() => panelTex ? panelTex : (panelTex = Rounded(40, 10, PanelCol, Border));
        public static Texture2D PanelSolidTex() => panelSolidTex ? panelSolidTex : (panelSolidTex = Rounded(40, 10, PanelSolidCol, Border));
        public static Texture2D PillTex() => pillTex ? pillTex : (pillTex = Rounded(32, 15, PanelCol, Border));
        public static Texture2D BarTex() => barTex ? barTex : (barTex = Rounded(16, 4, new Color(1f, 1f, 1f, 0.1f), Color.clear));
        public static Texture2D BarFillTex() => barFillTex ? barFillTex : (barFillTex = Rounded(16, 4, Color.white, Color.clear));
        static Texture2D BtnTex() => btnTex ? btnTex : (btnTex = Rounded(24, 6, new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.14f)));
        static Texture2D BtnHoverTex() => btnHoverTex ? btnHoverTex : (btnHoverTex = Rounded(24, 6, new Color(1f, 1f, 1f, 0.16f), new Color(1f, 1f, 1f, 0.3f)));
        static Texture2D BtnOnTex() => btnOnTex ? btnOnTex : (btnOnTex = Rounded(24, 6, new Color(1f, 0.78f, 0.25f, 0.85f), new Color(1f, 0.9f, 0.6f, 0.9f)));

        /// <summary>Square texture with rounded corners (radius r) and a 1 px border, meant for 9-slicing with border = r + 2.</summary>
        static Texture2D Rounded(int size, int r, Color fill, Color border)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, size - r), cy = Mathf.Clamp(y + 0.5f, r, size - r);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));   // distance outside the inner rect
                float a = Mathf.Clamp01(r - d + 0.5f);                  // coverage of the rounded shape
                float b = Mathf.Clamp01(r - d - 0.7f);                  // coverage of the inner (non-border) part
                Color c = Color.Lerp(border, fill, b); c.a = Mathf.Lerp(border.a, fill.a, b) * a;
                px[y * size + x] = c;
            }
            t.SetPixels(px); t.Apply(); return t;
        }

        // ------------------------------------------------------------------ drawing helpers
        public static void DrawPanel(Rect r, bool solid = false) { GUI.color = Color.white; GUI.Box(r, GUIContent.none, solid ? PanelSolid : Panel); }
        public static void DrawChip(Rect r, string text, Color? col = null) { var st = Chip; GUI.color = Color.white; GUI.Box(r, GUIContent.none, st); var prev = st.normal.textColor; if (col.HasValue) st.normal.textColor = col.Value; GUI.Label(r, text, st); st.normal.textColor = prev; }
        public static void DrawBar(Rect r, float v, Color fill)
        {
            GUI.color = Color.white; GUI.DrawTexture(r, BarTex(), ScaleMode.StretchToFill, true, 0f, Color.white, 0f, 4f);
            float w = Mathf.Max(0f, r.width * Mathf.Clamp01(v)); if (w > 1f) { GUI.color = fill; GUI.DrawTexture(new Rect(r.x, r.y, w, r.height), BarFillTex(), ScaleMode.StretchToFill, true, 0f, fill, 0f, 4f); }
            GUI.color = Color.white;
        }
        public static void Shadowed(Rect r, string text, GUIStyle st)
        {
            var c = st.normal.textColor; st.normal.textColor = new Color(0f, 0f, 0f, 0.7f * c.a); GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, st); st.normal.textColor = c; GUI.Label(r, text, st);
        }
    }
}
