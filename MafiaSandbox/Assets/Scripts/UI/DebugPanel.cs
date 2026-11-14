using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Data;
using MafiaSandbox.Economy;
using MafiaSandbox.Factions;
using MafiaSandbox.Player;
using MafiaSandbox.World;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// The prototype's top panel, as an overlay toggled with P: teleport to a location, set the time of day and weather,
    /// pick a weapon, play a showcase animation, plus a few cheats. Frees the cursor while open.
    /// </summary>
    public class DebugPanel : MonoBehaviour
    {
        public static DebugPanel I { get; private set; }
        public bool Open { get; private set; }

        // prototype coordinates (x, z, yaw radians) — same list as the HTML version, plus the crews' HQs
        static readonly (string name, float x, float z, float yaw, bool low)[] Locations =
        {
            ("Home (NW plateau)", -60f, 112f, Mathf.PI / 2f, false), ("Town square", 25f, 32f, Mathf.PI, false), ("Downtown (market, cafe…)", -26f, -4f, 0f, false), ("Bank", -25f, 4f, 0f, false),
            ("Police station", -25f, 46f, Mathf.PI, false), ("Construction site (S)", 25f, -104f, 0f, false), ("Warehouses (SW)", -25f, -104f, 0f, false), ("Scrapyard (SE)", 64f, -104f, 0f, false),
            ("Shanty camp (bridge)", 44f, 1f, Mathf.PI / 2f, false), ("Bridge deck", 50f, -30f, 0f, false), ("Overpass", -14f, -25f, -Mathf.PI / 2f, false), ("Tunnel entrance", 0f, 78f, 0f, true), ("Tunnel end", 0f, 112f, Mathf.PI, true),
            ("Plateau lookout", 22f, 92f, Mathf.PI, false), ("Plateau road", -30f, 100f, Mathf.PI / 2f, false), ("Behind the camp (E)", 72f, 2f, -Mathf.PI / 2f, false), ("Car park (SE)", 62f, -58f, Mathf.PI, false), ("Ramp (W)", -50f, 60f, 0f, false),
        };
        static readonly (string label, float t)[] Times = { ("Flow", -1f), ("Morning", 8f), ("Noon", 13f), ("Evening", 18.5f), ("Night", 23f) };
        string[] clips; Vector2 scrollLoc, scrollClip; string showcase;

        void Awake() { I = this; }

        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player) return;
            if (Input.GetKeyDown(KeyCode.P) && !(GameUI.I && (GameUI.I.MapOpen))) Open = !Open;
            if (Input.GetKeyDown(KeyCode.Escape) && Open) Open = false;
        }

        void OnGUI()
        {
            if (!Open) return; var gm = GameManager.I; if (!gm || !gm.player) return;
            float W = Screen.width, H = Screen.height; var r = new Rect(W / 2f - 420, 46, 840, Mathf.Min(H - 80, 560)); UiSkin.DrawPanel(r, true);
            GUILayout.BeginArea(new Rect(r.x + 14, r.y + 10, r.width - 28, r.height - 20));
            GUILayout.BeginHorizontal(); GUILayout.Label("Debug panel", UiSkin.Title); GUILayout.FlexibleSpace(); GUILayout.Label("P / Esc closes", UiSkin.LabelDim); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            // ---- left column: locations
            GUILayout.BeginVertical(GUILayout.Width(300));
            GUILayout.Label("LOCATION", UiSkin.H2);
            scrollLoc = GUILayout.BeginScrollView(scrollLoc, GUILayout.Height(r.height - 80));
            foreach (var l in Locations) if (GUILayout.Button(l.name, UiSkin.Button)) Teleport(Coords.ToUnity(l.x, 0f, l.z), Coords.YawToUnity(l.yaw), l.low);
            GUILayout.Space(6); GUILayout.Label("CREWS", UiSkin.H2);
            foreach (var f in FactionDefs.All) if (GUILayout.Button(f.name + " HQ", UiSkin.Button)) Teleport(f.HqUnity + Vector3.forward * 2f, 180f, false);
            GUILayout.Space(6); GUILayout.Label("SHOPS", UiSkin.H2);
            var eco = EconomyManager.I;
            if (eco) foreach (var sh in eco.shops) { var bi = sh.info; if (bi.kind == "house" || bi.kind == "home") continue; if (GUILayout.Button(bi.label, UiSkin.Button)) { Vector3 toDoor = bi.door - bi.counter; toDoor.y = 0f; toDoor.Normalize(); Teleport(bi.counter + toDoor * 1.4f, Mathf.Atan2(-toDoor.x, -toDoor.z) * Mathf.Rad2Deg, false, bi.footprint.min.y); } }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.Space(16);
            // ---- right column: time, weather, weapon, cheats, showcase
            GUILayout.BeginVertical();
            var dn = DayNightCycle.I;
            GUILayout.Label("TIME", UiSkin.H2); GUILayout.BeginHorizontal();
            foreach (var t in Times) { bool on = dn && (t.t < 0f ? !dn.paused : dn.paused && Mathf.Abs(dn.timeOfDay - t.t) < 0.01f); if (GUILayout.Toggle(on, t.label, UiSkin.Button) && !on && dn) { if (t.t < 0f) dn.paused = false; else { dn.timeOfDay = t.t; dn.paused = true; } } }
            GUILayout.EndHorizontal();
            var atmo = Atmosphere.I;
            GUILayout.Label("WEATHER", UiSkin.H2); GUILayout.BeginHorizontal();
            foreach (Atmosphere.WeatherKind w in System.Enum.GetValues(typeof(Atmosphere.WeatherKind))) { bool on = atmo && atmo.weather == w; if (GUILayout.Toggle(on, w.ToString(), UiSkin.Button) && !on && atmo) atmo.SetWeather(w); }
            GUILayout.EndHorizontal();
            var combat = gm.player.GetComponent<PlayerCombat>();
            GUILayout.Label("WEAPON", UiSkin.H2); GUILayout.BeginHorizontal();
            if (combat) for (int i = 0; i < WeaponDef.Order.Length; i++) { var k = WeaponDef.Order[i]; bool on = combat.Weapon.kind == k; if (GUILayout.Toggle(on, $"{i + 1} {WeaponDef.All[k].name}", UiSkin.Button) && !on) { combat.owned[i] = true; combat.Equip(k); } }
            GUILayout.EndHorizontal();
            GUILayout.Label("CHEATS", UiSkin.H2); GUILayout.BeginHorizontal();
            if (GUILayout.Button("+$500", UiSkin.Button) && eco) eco.cash += 500;
            if (GUILayout.Button("Heal", UiSkin.Button)) { var b = gm.player.GetComponent<CharacterBody>(); if (b) b.Treat(99); if (Vitals.I) { Vitals.I.stamina = 100f; Vitals.I.hunger = 100f; } }
            if (GUILayout.Button("+1 ★", UiSkin.Button) && WantedSystem.I) WantedSystem.I.Add(1, "debug");
            if (GUILayout.Button("Clear ★", UiSkin.Button) && WantedSystem.I) WantedSystem.I.Clear();
            if (GUILayout.Button("Anger crew here", UiSkin.Button) && FactionManager.I && FactionManager.I.Territory != null) FactionManager.I.PlayerAttacked(FactionManager.I.Territory, true);
            if (GUILayout.Button("Top-down cam", UiSkin.Button)) gm.ToggleTopDown();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Car to me", UiSkin.Button) && gm.playerCar) { var p = gm.player.transform.position + gm.player.transform.forward * 4f + Vector3.up * 0.3f; var rb = gm.playerCar.GetComponent<Rigidbody>(); gm.playerCar.transform.SetPositionAndRotation(p, Quaternion.LookRotation(gm.player.transform.forward, Vector3.up)); if (rb) { rb.position = p; rb.linearVelocity = Vector3.zero; } }
            if (GUILayout.Button("Traffic labels", UiSkin.Button)) Vehicles.TrafficDriver.ShowDebug = !Vehicles.TrafficDriver.ShowDebug;
            if (GUILayout.Button("Spawn dummy", UiSkin.Button)) { var sp = FindAnyObjectByType<Npc.NpcSpawner>(); if (sp && sp.prefabs.Length > 0) { var p = gm.player.transform.position + gm.player.transform.forward * 4f; var n = sp.Spawn<Npc.CivilianBrain>(sp.prefabs[Random.Range(0, sp.prefabs.Length)], p, gm.player.transform.eulerAngles.y + 180f); n.dummy = true; } }
            if (GUILayout.Button("Respawn", UiSkin.Button)) gm.Respawn();
            GUILayout.EndHorizontal();
            GUILayout.Label("SHOWCASE ANIMATION  (plays on you until you move)", UiSkin.H2);
            if (clips == null) { var a = gm.player.GetComponentInChildren<Animation>(); var l = new List<string>(); if (a) foreach (AnimationState st in a) l.Add(st.name); l.Sort(); clips = l.ToArray(); }
            scrollClip = GUILayout.BeginScrollView(scrollClip);
            int col = 0; GUILayout.BeginHorizontal();
            foreach (var c in clips) { bool on = showcase == c; if (GUILayout.Toggle(on, c, UiSkin.Button, GUILayout.Width(118)) && !on) { showcase = c; EmoteWheel.I?.PlayClip(c); } if (++col % 4 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); } }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            if (EmoteWheel.I && EmoteWheel.I.Emote == null) showcase = null;
        }

        void Teleport(Vector3 p, float yaw, bool low, float? knownY = null)
        {
            var gm = GameManager.I; if (!gm || !gm.player) return;
            var vi = gm.player.GetComponent<Vehicles.VehicleInteraction>(); if (vi && vi.Current) vi.Exit();
            float y = knownY ?? 0f;
            if (!knownY.HasValue)
            {
                float from = low ? 3.5f : 60f;   // the tunnel runs under the plateau: look for the floor from just above head height
                if (Physics.Raycast(new Vector3(p.x, from, p.z), Vector3.down, out var h, 80f, 1 << 0, QueryTriggerInteraction.Ignore)) y = h.point.y;
            }
            p.y = y + 0.15f;
            gm.player.Teleport(p, yaw); if (gm.playerCamera) gm.playerCamera.SetYaw(yaw);
            Open = false;
        }
    }
}
