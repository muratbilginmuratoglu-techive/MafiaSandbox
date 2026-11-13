using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Combat;
using MafiaSandbox.Vehicles;

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
                GUI.Box(new Rect(12, 42, 400, 70), "WASD move · Shift sprint · C crouch · Space jump · V first person · R home\n1–6 weapons · RMB aim · LMB fire / swing · E enter/exit car\nCar: W/S gas, A/D steer, Space handbrake", box);

            // bottom-left: health + weapon
            float hp = body ? Mathf.Clamp01(body.hp / (float)body.maxHp) : 1f;
            GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(new Rect(12, H - 74, 240, 62), white);
            GUI.color = new Color(0.2f, 0.2f, 0.2f); GUI.DrawTexture(new Rect(22, H - 64, 220, 12), white);
            GUI.color = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.3f, 0.85f, 0.4f), hp); GUI.DrawTexture(new Rect(22, H - 64, 220 * hp, 12), white);
            GUI.color = Color.white;
            GUI.Label(new Rect(22, H - 48, 230, 24), combat ? $"{combat.Weapon.name}   ·   kills {gm.Kills}" : "", logStyle);
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
