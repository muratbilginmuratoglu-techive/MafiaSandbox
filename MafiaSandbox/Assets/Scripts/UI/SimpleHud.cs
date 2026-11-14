using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Combat;
using MafiaSandbox.Vehicles;
using MafiaSandbox.Economy;
using MafiaSandbox.Player;

namespace MafiaSandbox.UI
{
    /// <summary>IMGUI HUD: health, weapon, crosshair + hit marker, prompts, message log, banner, FPS, controls (H). The styled HUD/minimap arrives in step 5.</summary>
    public class SimpleHud : MonoBehaviour
    {
        GUIStyle box, big, banner, logStyle; float fps, fpsT; int frames; bool showHelp = true;
        VehicleInteraction vi; PlayerCombat combat; CharacterBody body; Texture2D white;

        void Update() { frames++; fpsT += Time.unscaledDeltaTime; if (fpsT >= 0.5f) { fps = frames / fpsT; frames = 0; fpsT = 0f; } if (Input.GetKeyDown(KeyCode.H)) showHelp = !showHelp; }

        void OnGUI()
        {
            if (box == null)
            {
                white = Texture2D.whiteTexture;
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleLeft, fontSize = 14, padding = new RectOffset(10, 10, 6, 6) }; box.normal.textColor = Color.white;
                big = new GUIStyle(box) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
                banner = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; banner.normal.textColor = new Color(1f, 0.3f, 0.3f);
                logStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 }; logStyle.normal.textColor = Color.white;
            }
            var gm = GameManager.I; if (!gm || !gm.player) return;
            if (!vi) vi = gm.player.GetComponent<VehicleInteraction>();
            if (!combat) combat = gm.player.GetComponent<PlayerCombat>();
            if (!body) body = gm.player.GetComponent<CharacterBody>();
            float W = Screen.width, H = Screen.height;

            // top-left: fps + help
            GUI.Box(new Rect(12, 12, 400, 26), $"FPS {fps:0}   {(Cursor.lockState == CursorLockMode.Locked ? "Esc: free cursor" : "Click to look around")}   H: help", box);
            if (showHelp)
                GUI.Box(new Rect(12, 42, 430, 88), "WASD move · Shift sprint · C crouch · Space jump · V first person · R home\n1–6 weapons · RMB aim · LMB fire / swing · E enter/exit car\nF take / buy / trade / search body · G steal · Tab backpack · M map\nCar: W/S gas, A/D steer, Space handbrake", box);

            // bottom-left: vitals (health / stamina / food) + weapon + cash
            float hp = body ? Mathf.Clamp01(body.hp / (float)body.maxHp) : 1f; var vit = Vitals.I; var eco = EconomyManager.I;
            float panelX = 212f;   // right of the minimap
            GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(new Rect(panelX, H - 96, 270, 84), white);
            void Bar(float y, string name, float v, Color c)
            {
                GUI.color = Color.white; GUI.Label(new Rect(panelX + 10, y - 3, 70, 18), name, logStyle);
                GUI.color = new Color(0.2f, 0.2f, 0.2f); GUI.DrawTexture(new Rect(panelX + 78, y, 180, 10), white);
                GUI.color = c; GUI.DrawTexture(new Rect(panelX + 78, y, 180 * Mathf.Clamp01(v), 10), white);
            }
            Bar(H - 88, "HEALTH", hp, Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.3f, 0.85f, 0.4f), hp));
            Bar(H - 72, "STAMINA", vit ? vit.stamina / 100f : 1f, vit && vit.Exhausted ? new Color(0.9f, 0.4f, 0.2f) : new Color(0.36f, 0.62f, 0.95f));
            Bar(H - 56, "FOOD", vit ? vit.hunger / 100f : 1f, vit && vit.hunger < 30f ? new Color(0.9f, 0.35f, 0.35f) : new Color(0.91f, 0.6f, 0.29f));
            GUI.color = Color.white;
            GUI.Label(new Rect(panelX + 10, H - 36, 260, 22), combat ? $"{combat.Weapon.name}   ·   kills {gm.Kills}" : "", logStyle);
            if (eco) { var cs = new GUIStyle(big) { fontSize = 20, alignment = TextAnchor.MiddleRight }; cs.normal.textColor = new Color(0.56f, 0.9f, 0.6f); GUI.Box(new Rect(W - 150, 12, 138, 36), $"${eco.cash}", cs); }
            // interaction prompt
            var inter = gm.player.GetComponent<PlayerInteraction>();
            if (inter && !string.IsNullOrEmpty(inter.Prompt) && !(vi && vi.Current) && !UI.GameUI.Blocking) GUI.Box(new Rect(W / 2f - 190, H - 100, 380, 28), inter.Prompt, box);
            // clock + weather (top centre, under the stars)
            var dn = MafiaSandbox.World.DayNightCycle.I; var atmo = MafiaSandbox.World.Atmosphere.I;
            if (dn)
            {
                int hh = Mathf.FloorToInt(dn.timeOfDay), mm = Mathf.FloorToInt((dn.timeOfDay - hh) * 60f);
                string wx = atmo ? (atmo.weather == MafiaSandbox.World.Atmosphere.WeatherKind.Rain ? "  ·  rain" : atmo.weather == MafiaSandbox.World.Atmosphere.WeatherKind.Snow ? "  ·  snow" : "") : "";
                var cs = new GUIStyle(box) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
                GUI.Box(new Rect(W / 2f - 70, 50, 140, 22), $"{hh:00}:{mm:00}{wx}", cs);
            }
            // wanted stars (top centre)
            var ws = WantedSystem.I; int lvl = ws ? ws.Level : 0;
            if (lvl > 0)
            {
                var st = new GUIStyle(big) { fontSize = 26 }; st.normal.textColor = ws.PoliceHostile ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.85f, 0.3f);
                GUI.Box(new Rect(W / 2f - 80, 12, 160, 40), new string('★', lvl) + new string('☆', 3 - lvl), st);
            }

            // crosshair / hit marker
            bool aim = combat && (combat.Aiming || combat.Weapon.melee) && !(vi && vi.Current) && Cursor.lockState == CursorLockMode.Locked;
            if (aim)
            {
                float cx = W / 2f, cy = H / 2f, g = combat.Weapon.melee ? 10f : 6f + (combat.Weapon.spreadDeg * 2f);
                GUI.color = new Color(1f, 1f, 1f, 0.9f);
                GUI.DrawTexture(new Rect(cx - 1, cy - g - 7, 2, 7), white); GUI.DrawTexture(new Rect(cx - 1, cy + g, 2, 7), white);
                GUI.DrawTexture(new Rect(cx - g - 7, cy - 1, 7, 2), white); GUI.DrawTexture(new Rect(cx + g, cy - 1, 7, 2), white);
                if (combat.HitMarkerT > 0f)
                {
                    GUI.color = combat.HitMarkerHead ? new Color(1f, 0.35f, 0.2f) : new Color(1f, 1f, 1f);
                    GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
                    GUI.DrawTexture(new Rect(cx - 12, cy - 1, 24, 2), white); GUI.DrawTexture(new Rect(cx - 1, cy - 12, 2, 24), white);
                    GUIUtility.RotateAroundPivot(-45f, new Vector2(cx, cy));
                }
                GUI.color = Color.white;
            }

            // prompts
            if (vi && vi.Current)
            {
                GUI.Box(new Rect(W / 2f - 90, H - 70, 180, 32), $"{vi.Current.Speed * 3.6f:0} km/h", big);
                GUI.Box(new Rect(W / 2f - 130, H - 34, 260, 26), $"E  get out of the {vi.Current.displayName}", box);
            }
            else if (vi && vi.Nearby) GUI.Box(new Rect(W / 2f - 130, H - 60, 260, 28), $"E  get in — {vi.Nearby.displayName}", box);

            // log (bottom right)
            float y = H - 20;
            for (int i = gm.Log.Count - 1; i >= 0; i--) { var e = gm.Log[i]; GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(e.t)); GUI.Label(new Rect(W - 420, y - 20, 410, 20), e.text, logStyle); y -= 20; }
            GUI.color = Color.white;

            // banner
            if (gm.BannerT > 0f)
            {
                GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(new Rect(0, H / 2f - 70, W, 140), white); GUI.color = Color.white;
                GUI.Label(new Rect(0, H / 2f - 60, W, 70), gm.BannerTitle, banner);
                GUI.Label(new Rect(0, H / 2f + 10, W, 40), gm.BannerText, big);
            }
        }
    }
}
