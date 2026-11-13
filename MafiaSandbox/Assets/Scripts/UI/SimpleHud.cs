using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Vehicles;

namespace MafiaSandbox.UI
{
    /// <summary>Placeholder IMGUI HUD for step 1: prompt, speed, FPS, controls. Replaced by the real HUD (health/stamina/food, minimap, cash) in step 5.</summary>
    public class SimpleHud : MonoBehaviour
    {
        GUIStyle box, big; float fps, fpsT; int frames;
        VehicleInteraction vi;

        void Update() { frames++; fpsT += Time.unscaledDeltaTime; if (fpsT >= 0.5f) { fps = frames / fpsT; frames = 0; fpsT = 0f; } }

        void OnGUI()
        {
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleLeft, fontSize = 14, padding = new RectOffset(10, 10, 6, 6) };
                box.normal.textColor = Color.white;
                big = new GUIStyle(box) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            }
            var gm = GameManager.I; if (!gm || !gm.player) return;
            if (!vi) vi = gm.player.GetComponent<VehicleInteraction>();

            GUI.Box(new Rect(12, 12, 360, 26), $"FPS {fps:0}   {(Cursor.lockState == CursorLockMode.Locked ? "Esc: free cursor" : "Click to look around")}", box);
            GUI.Box(new Rect(12, 42, 360, 48), "WASD move · Shift sprint · C crouch · Space jump · V first person\nE enter/exit car (W/S gas, A/D steer, Space handbrake) · R respawn at home", box);

            if (vi && vi.Current)
            {
                GUI.Box(new Rect(Screen.width / 2f - 90, Screen.height - 70, 180, 32), $"{vi.Current.Speed * 3.6f:0} km/h", big);
                GUI.Box(new Rect(Screen.width / 2f - 130, Screen.height - 34, 260, 26), $"E  get out of the {vi.Current.displayName}", box);
            }
            else if (vi && vi.Nearby)
            {
                GUI.Box(new Rect(Screen.width / 2f - 130, Screen.height - 60, 260, 28), $"E  get in — {vi.Nearby.displayName}", box);
            }
        }
    }
}
