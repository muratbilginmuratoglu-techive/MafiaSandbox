using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Combat;
using MafiaSandbox.Vehicles;
using MafiaSandbox.Economy;
using MafiaSandbox.Player;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// The HUD, laid out like the prototype: vitals + weapon strip bottom-left (next to the minimap), cash and wanted stars
    /// top-right, clock / weather top-centre, prompts bottom-centre, message log bottom-right, crosshair, banner, help (H).
    /// </summary>
    public class SimpleHud : MonoBehaviour
    {
        float fps, fpsT; int frames; bool showHelp = true;
        VehicleInteraction vi; PlayerCombat combat; CharacterBody body; float dmgFlash; int lastHp = -1;

        void Update() { frames++; fpsT += Time.unscaledDeltaTime; if (fpsT >= 0.5f) { fps = frames / fpsT; frames = 0; fpsT = 0f; } if (Input.GetKeyDown(KeyCode.H)) showHelp = !showHelp; }

        void OnGUI()
        {
            var gm = GameManager.I; if (!gm || !gm.player) return;
            if (!vi) vi = gm.player.GetComponent<VehicleInteraction>();
            if (!combat) combat = gm.player.GetComponent<PlayerCombat>();
            if (!body) body = gm.player.GetComponent<CharacterBody>();
            float W = Screen.width, H = Screen.height; var white = UiSkin.White;
            bool driving = vi && vi.Current;

            // damage vignette
            if (body) { if (lastHp >= 0 && body.hp < lastHp) dmgFlash = 1f; lastHp = body.hp; }
            if (dmgFlash > 0f) { dmgFlash -= Time.deltaTime * 1.6f; GUI.color = new Color(0.8f, 0f, 0f, 0.35f * dmgFlash); GUI.DrawTexture(new Rect(0, 0, W, H), white); GUI.color = Color.white; }

            // top-left: help chip + help panel
            UiSkin.DrawChip(new Rect(12, 12, 300, 24), $"FPS {fps:0}   ·   {(Cursor.lockState == CursorLockMode.Locked ? "Esc frees the cursor" : "click to look around")}   ·   H help   ·   P debug", UiSkin.Dim);
            if (showHelp)
            {
                var r = new Rect(12, 42, 470, 108); UiSkin.DrawPanel(r);
                GUI.Label(new Rect(r.x + 12, r.y + 6, r.width - 24, r.height - 12),
                    "<b>Move</b> WASD   <b>Sprint</b> Shift   <b>Crouch</b> C   <b>Jump</b> Space   <b>View</b> V   <b>Home</b> R\n" +
                    "<b>Weapons</b> 1–6   <b>Aim</b> RMB   <b>Fire / swing</b> LMB   <b>Emote</b> hold Tab   <b>Car</b> E\n" +
                    "<b>Take / buy / trade / search</b> F   <b>Steal</b> G   <b>Backpack</b> I   <b>Map</b> M\n" +
                    "<b>In a car</b> W/S gas · A/D steer · Space handbrake", UiSkin.LabelDim);
            }

            // bottom-left: vitals panel to the right of the minimap
            float hp = body ? Mathf.Clamp01(body.hp / (float)body.maxHp) : 1f; var vit = Vitals.I; var eco = EconomyManager.I;
            float px = 214f, py = H - 112f; var panel = new Rect(px, py, 286, 100); UiSkin.DrawPanel(panel);
            void Bar(float y, string name, float v, Color c, string tail = null)
            {
                GUI.Label(new Rect(px + 12, y - 3, 72, 18), name, UiSkin.H2);
                UiSkin.DrawBar(new Rect(px + 84, y + 1, 180, 9), v, c);
                if (tail != null) GUI.Label(new Rect(px + 268, y - 4, 40, 18), tail, UiSkin.Small);
            }
            Bar(py + 12, "HEALTH", hp, Color.Lerp(UiSkin.Red, UiSkin.Green, hp));
            Bar(py + 30, "STAMINA", vit ? vit.stamina / 100f : 1f, vit && vit.Exhausted ? new Color(0.9f, 0.4f, 0.2f) : UiSkin.Blue);
            Bar(py + 48, "FOOD", vit ? vit.hunger / 100f : 1f, vit && vit.hunger < 30f ? UiSkin.Red : new Color(0.91f, 0.6f, 0.29f));
            // weapon strip (1–6)
            if (combat)
            {
                float sx = px + 12, sy = py + 70, sw = (panel.width - 24 - 10) / 6f;
                for (int i = 0; i < WeaponDef.Order.Length; i++)
                {
                    var k = WeaponDef.Order[i]; bool cur = combat.Weapon.kind == k; bool own = combat.owned[i];
                    var rr = new Rect(sx + i * (sw + 2), sy, sw, 20);
                    GUI.color = cur ? new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.9f) : new Color(1f, 1f, 1f, own ? 0.08f : 0.03f); GUI.DrawTexture(rr, UiSkin.BarFillTex(), ScaleMode.StretchToFill, true, 0f, GUI.color, 0f, 4f); GUI.color = Color.white;
                    var st = new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 10 }; st.normal.textColor = cur ? new Color(0.1f, 0.08f, 0.02f) : own ? UiSkin.Text : new Color(1f, 1f, 1f, 0.3f);
                    GUI.Label(rr, $"{i + 1} {Short(k)}", st);
                }
            }

            // top-right: cash + wanted stars
            var ws = WantedSystem.I; int lvl = ws ? ws.Level : 0;
            if (eco) { var cr = new Rect(W - 172, 12, 160, 40); UiSkin.DrawPanel(cr); var cs = new GUIStyle(UiSkin.Big) { alignment = TextAnchor.MiddleRight, fontSize = 22 }; cs.normal.textColor = UiSkin.Green; GUI.Label(new Rect(cr.x, cr.y, cr.width - 12, cr.height), $"${eco.cash:n0}", cs); }
            {
                var st = new GUIStyle(UiSkin.Big) { fontSize = 22, alignment = TextAnchor.MiddleRight };
                st.normal.textColor = lvl == 0 ? new Color(1f, 1f, 1f, 0.25f) : ws.PoliceHostile ? UiSkin.Red : UiSkin.Accent;
                UiSkin.Shadowed(new Rect(W - 172, 54, 148, 28), new string('★', lvl) + new string('☆', 3 - lvl), st);
            }
            // top-centre: clock + weather
            var dn = MafiaSandbox.World.DayNightCycle.I; var atmo = MafiaSandbox.World.Atmosphere.I;
            if (dn)
            {
                int hh = Mathf.FloorToInt(dn.timeOfDay), mm = Mathf.FloorToInt((dn.timeOfDay - hh) * 60f);
                string wx = atmo ? (atmo.weather == MafiaSandbox.World.Atmosphere.WeatherKind.Rain ? "  ☂ rain" : atmo.weather == MafiaSandbox.World.Atmosphere.WeatherKind.Snow ? "  ❄ snow" : "  ☀") : "";
                UiSkin.DrawChip(new Rect(W / 2f - 70, 12, 140, 26), $"<b>{hh:00}:{mm:00}</b>{wx}");
            }

            // interaction prompt
            var inter = gm.player.GetComponent<PlayerInteraction>();
            if (inter && !string.IsNullOrEmpty(inter.Prompt) && !driving && !GameUI.Blocking) Prompt(W / 2f, H - 150, inter.Prompt);

            // crosshair / hit marker
            bool aim = combat && (combat.Aiming || combat.Weapon.melee) && !driving && Cursor.lockState == CursorLockMode.Locked && !GameUI.Blocking;
            if (aim)
            {
                float cx = W / 2f, cy = H / 2f, g = combat.Weapon.melee ? 10f : 6f + combat.Weapon.spreadDeg * 2f;
                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                foreach (var o in new[] { new Vector2(1, 1) }) { GUI.DrawTexture(new Rect(cx - 1 + o.x, cy - g - 8 + o.y, 2, 8), white); GUI.DrawTexture(new Rect(cx - 1 + o.x, cy + g + o.y, 2, 8), white); GUI.DrawTexture(new Rect(cx - g - 8 + o.x, cy - 1 + o.y, 8, 2), white); GUI.DrawTexture(new Rect(cx + g + o.x, cy - 1 + o.y, 8, 2), white); }
                GUI.color = new Color(1f, 1f, 1f, 0.95f);
                GUI.DrawTexture(new Rect(cx - 1, cy - g - 8, 2, 8), white); GUI.DrawTexture(new Rect(cx - 1, cy + g, 2, 8), white);
                GUI.DrawTexture(new Rect(cx - g - 8, cy - 1, 8, 2), white); GUI.DrawTexture(new Rect(cx + g, cy - 1, 8, 2), white);
                GUI.DrawTexture(new Rect(cx - 1, cy - 1, 2, 2), white);
                if (combat.HitMarkerT > 0f)
                {
                    GUI.color = combat.HitMarkerHead ? new Color(1f, 0.35f, 0.2f) : Color.white;
                    GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
                    GUI.DrawTexture(new Rect(cx - 12, cy - 1, 24, 2), white); GUI.DrawTexture(new Rect(cx - 1, cy - 12, 2, 24), white);
                    GUIUtility.RotateAroundPivot(-45f, new Vector2(cx, cy));
                }
                GUI.color = Color.white;
            }

            // driving: speed + exit prompt / enter prompt
            if (driving)
            {
                var sr = new Rect(W / 2f - 70, H - 92, 140, 44); UiSkin.DrawPanel(sr);
                var ss = new GUIStyle(UiSkin.Big) { fontSize = 24 }; GUI.Label(new Rect(sr.x, sr.y + 2, sr.width, 28), $"{Mathf.Abs(vi.Current.Speed) * 3.6f:0}", ss);
                GUI.Label(new Rect(sr.x, sr.y + 26, sr.width, 14), "km/h", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
                Prompt(W / 2f, H - 36, $"<b>E</b>  get out of the {vi.Current.displayName}");
            }
            else if (vi && vi.Nearby && !GameUI.Blocking) Prompt(W / 2f, H - 112, $"<b>E</b>  get in — {vi.Nearby.displayName}");

            // log (bottom right)
            float y = H - 16;
            for (int i = gm.Log.Count - 1; i >= 0; i--)
            {
                var e = gm.Log[i]; float a = Mathf.Clamp01(e.t);
                var st = new GUIStyle(UiSkin.Label) { fontSize = 13, alignment = TextAnchor.MiddleRight }; st.normal.textColor = new Color(1f, 1f, 1f, a);
                UiSkin.Shadowed(new Rect(W - 440, y - 20, 428, 20), e.text, st); y -= 20;
            }
            GUI.color = Color.white;

            // banner
            if (gm.BannerT > 0f)
            {
                GUI.color = new Color(0.02f, 0.03f, 0.06f, 0.72f); GUI.DrawTexture(new Rect(0, H / 2f - 72, W, 144), white); GUI.color = Color.white;
                var bt = new GUIStyle(UiSkin.Big) { fontSize = 46 }; bt.normal.textColor = UiSkin.Red; UiSkin.Shadowed(new Rect(0, H / 2f - 60, W, 70), gm.BannerTitle, bt);
                GUI.Label(new Rect(0, H / 2f + 12, W, 40), gm.BannerText, UiSkin.Big);
            }
        }

        static string Short(WeaponKind k) => k == WeaponKind.Pistol ? "Pistol" : k == WeaponKind.Rifle ? "Rifle" : k == WeaponKind.Shotgun ? "Shotgun" : k == WeaponKind.Knife ? "Knife" : k == WeaponKind.Bat ? "Bat" : "Revolver";

        static void Prompt(float cx, float y, string text)
        {
            var st = UiSkin.Center; var sz = st.CalcSize(new GUIContent(text)); float w = sz.x + 28;
            var r = new Rect(cx - w / 2f, y, w, 30); UiSkin.DrawPanel(r); GUI.Label(r, text, st);
        }
    }
}
