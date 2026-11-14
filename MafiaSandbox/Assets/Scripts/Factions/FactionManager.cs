using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.UI;

namespace MafiaSandbox.Factions
{
    /// <summary>
    /// Reputation with every crew (-100 … +100), the shared alarm per faction, and the territory banner.
    /// Hurting a member costs reputation with that crew and its allies and earns a little with the rival bloc; below
    /// -15 they warn you in their territory, below -35 they attack on sight. Reputation drifts back toward neutral slowly.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class FactionManager : MonoBehaviour
    {
        public static FactionManager I { get; private set; }
        public const float WarnAt = -15f, HostileAt = -35f;

        readonly Dictionary<string, float> rep = new Dictionary<string, float>();
        readonly Dictionary<string, float> alertUntil = new Dictionary<string, float>();
        public FactionDef Territory { get; private set; }
        float bannerT, checkT; string bannerText = "";
        Texture2D dot;

        void Awake()
        {
            I = this; foreach (var f in FactionDefs.All) { rep[f.id] = 0f; alertUntil[f.id] = -1f; }
            dot = new Texture2D(1, 1); dot.SetPixel(0, 0, Color.white); dot.Apply();
        }
        void OnEnable() { WorldEvents.OnCharacterDied += OnDied; }
        void OnDisable() { WorldEvents.OnCharacterDied -= OnDied; }

        public float Rep(FactionDef f) => f != null && rep.TryGetValue(f.id, out var v) ? v : 0f;
        public bool Hostile(FactionDef f) => Rep(f) <= HostileAt || Alerted(f);
        public bool Wary(FactionDef f) => Rep(f) <= WarnAt;
        public bool Alerted(FactionDef f) => f != null && alertUntil.TryGetValue(f.id, out var t) && Time.time < t;
        public void Alert(FactionDef f, float seconds) { if (f == null) return; alertUntil[f.id] = Mathf.Max(alertUntil[f.id], Time.time + seconds); }

        public void Change(FactionDef f, float delta, bool quiet = false)
        {
            if (f == null) return; float before = rep[f.id]; rep[f.id] = Mathf.Clamp(before + delta, -100f, 100f);
            if (!quiet && Mathf.Abs(delta) >= 5f) GameManager.I?.Say($"{f.name}: {(delta < 0 ? "" : "+")}{delta:0} reputation");
        }

        /// <summary>The player attacked (hit or killed) a member: that crew and its allies turn on the player, rivals approve.</summary>
        public void PlayerAttacked(FactionDef f, bool killed)
        {
            if (f == null) return;
            Change(f, killed ? -40f : -12f); Alert(f, killed ? 60f : 35f);
            foreach (var id in f.allies) { var a = FactionDefs.Get(id); Change(a, killed ? -15f : -4f, !killed); if (killed) Alert(a, 25f); }
            foreach (var id in f.enemies) { var e = FactionDefs.Get(id); Change(e, killed ? 8f : 2f, true); }
            if (killed && Rep(f) <= HostileAt) GameManager.I?.Say($"{f.name} want you dead. Stay out of their turf.");
        }

        void OnDied(CharacterBody b, string by)
        {
            if (by != "player" || !b) return;
            var fb = b.GetComponent<FactionBrain>(); if (fb) PlayerAttacked(fb.Def, true);
        }

        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player) return;
            // slow drift back toward neutral (slower while hostile; nothing is forgotten in a minute)
            float dt = Time.deltaTime; foreach (var f in FactionDefs.All) { float v = rep[f.id]; rep[f.id] = Mathf.MoveTowards(v, 0f, dt * (v < HostileAt ? 0.02f : 0.05f)); }
            checkT -= dt; if (checkT > 0f) return; checkT = 0.4f;
            var t = FactionDefs.At(gm.player.transform.position);
            if (t != Territory)
            {
                Territory = t;
                if (t != null) { bannerText = t.name.ToUpperInvariant() + (t.kind == FactionKind.Mafia ? " TERRITORY" : " TURF") + (Hostile(t) ? "  —  HOSTILE" : Wary(t) ? "  —  unwelcome" : ""); bannerT = 4.5f; }
            }
        }

        void OnGUI()
        {
            if (GameUI.Blocking) return;
            var gm = GameManager.I; if (!gm || !gm.player || gm.player.GetComponent<CharacterBody>()?.Dead == true) return;
            float W = Screen.width;
            if (bannerT > 0f)
            {
                bannerT -= Time.deltaTime; float a = Mathf.Clamp01(bannerT / 0.6f);
                var st = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; st.normal.textColor = new Color(1f, 1f, 1f, a);
                var c = Territory != null ? Territory.color : Color.gray; GUI.color = new Color(c.r, c.g, c.b, 0.75f * a); GUI.DrawTexture(new Rect(W / 2f - 190, 98, 380, 36), dot); GUI.color = Color.white;
                GUI.Label(new Rect(W / 2f - 190, 98, 380, 36), bannerText, st);
            }
            else if (Territory != null)
            {
                var st = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter }; st.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
                var c = Territory.color; GUI.color = new Color(c.r, c.g, c.b, 0.55f); GUI.DrawTexture(new Rect(W / 2f - 70, 96, 140, 18), dot); GUI.color = Color.white;
                GUI.Label(new Rect(W / 2f - 70, 96, 140, 18), Territory.name + (Hostile(Territory) ? " ✖" : Wary(Territory) ? " !" : ""), st);
            }
        }

        public static string Standing(float v) => v <= HostileAt ? "hostile" : v <= WarnAt ? "unfriendly" : v < 15f ? "neutral" : v < 40f ? "friendly" : "respected";
    }
}
