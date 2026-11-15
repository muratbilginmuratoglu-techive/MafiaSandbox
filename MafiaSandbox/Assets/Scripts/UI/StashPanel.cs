using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;

namespace MafiaSandbox.UI
{
    /// <summary>Two-column transfer window for the home stash: backpack on the left, chest on the right. Click = 1, Shift-click = all.</summary>
    public class StashPanel : MonoBehaviour
    {
        public static StashPanel I { get; private set; }
        public static bool IsOpen => I && I.open;
        bool open; Vector2 sa, sb;
        void Awake() { I = this; }
        public void Open() { open = true; }

        void Update()
        {
            var gm = GameManager.I; if (!open || !gm || !gm.player) return;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F) || Stash.I == null || (gm.player.transform.position - Stash.I.Pos).sqrMagnitude > 5f * 5f) open = false;
        }

        void OnGUI()
        {
            if (!open) return; var eco = EconomyManager.I; var st = Stash.I; if (!eco || !st) return;
            float W = Screen.width, H = Screen.height; var r = new Rect(W / 2f - 420, H / 2f - 260, 840, 520); UiSkin.DrawPanel(r, true);
            GUILayout.BeginArea(new Rect(r.x + 16, r.y + 12, r.width - 32, r.height - 24));
            GUILayout.BeginHorizontal(); GUILayout.Label("Home stash", UiSkin.Title); GUILayout.FlexibleSpace(); GUILayout.Label($"{st.Units}/{Stash.Capacity} items  ·  worth ${st.Worth}  ·  Esc / F closes  ·  click = 1, Shift = all", UiSkin.LabelDim); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            bool all = Event.current.shift;
            GUILayout.BeginVertical(GUILayout.Width(400));
            GUILayout.Label("BACKPACK", UiSkin.H2); sa = GUILayout.BeginScrollView(sa, GUILayout.Height(420));
            InvItem mv = null; foreach (var it in eco.inv) { var d = Items.Get(it.id); GUILayout.BeginHorizontal(); GUILayout.Label($"{d.name} x{it.qty}{(it.stolen ? " (stolen)" : "")}", it.stolen ? UiSkin.LabelDim : UiSkin.Label, GUILayout.Width(280)); if (GUILayout.Button("→", UiSkin.Button, GUILayout.Width(40))) mv = it; GUILayout.EndHorizontal(); }
            if (eco.inv.Count == 0) GUILayout.Label("Empty.", UiSkin.LabelDim);
            GUILayout.EndScrollView(); GUILayout.EndVertical();
            if (mv != null) st.Put(mv, all ? mv.qty : 1);
            GUILayout.Space(16);
            GUILayout.BeginVertical();
            GUILayout.Label("CHEST", UiSkin.H2); sb = GUILayout.BeginScrollView(sb, GUILayout.Height(420));
            InvItem tk = null; foreach (var it in st.items) { var d = Items.Get(it.id); GUILayout.BeginHorizontal(); if (GUILayout.Button("←", UiSkin.Button, GUILayout.Width(40))) tk = it; GUILayout.Label($"{d.name} x{it.qty}{(it.stolen ? " (stolen)" : "")}", it.stolen ? UiSkin.LabelDim : UiSkin.Label); GUILayout.EndHorizontal(); }
            if (st.items.Count == 0) GUILayout.Label("Nothing stored. Items here are safe from arrests and survive death.", UiSkin.LabelDim);
            GUILayout.EndScrollView(); GUILayout.EndVertical();
            if (tk != null) st.Take(tk, all ? tk.qty : 1);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
