using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Npc;
using MafiaSandbox.Player;
using MafiaSandbox.Vehicles;
using MafiaSandbox.World;

namespace MafiaSandbox.Core
{
    /// <summary>Scene-level glue: references, layer collision rules, player death / respawn / arrest, crime witnessing, message log.</summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }
        public PlayerController player;
        public PlayerCamera playerCamera;
        public VehicleController playerCar;
        public Vector3 homePosition;
        public float homeYaw;
        public Vector3 policeStationPos;

        public string BannerTitle { get; private set; }
        public string BannerText { get; private set; }
        public float BannerT { get; private set; }
        public readonly List<(string text, float t)> Log = new List<(string, float)>();
        public int Kills { get; private set; }
        public bool Arrested { get; private set; }

        CharacterBody playerBody; float lastHurtT = -99f, regenT;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = -1;
            if (!player) player = FindAnyObjectByType<PlayerController>();
            if (!playerCamera) playerCamera = FindAnyObjectByType<PlayerCamera>();
            int hb = LayerMask.NameToLayer("Hitbox"), ch = LayerMask.NameToLayer("Characters");
            if (hb >= 0 && ch >= 0) { Physics.IgnoreLayerCollision(hb, ch, true); Physics.IgnoreLayerCollision(ch, ch, true); }
            if (!FindAnyObjectByType<CombatFx>()) new GameObject("CombatFx").AddComponent<CombatFx>();
            if (!FindAnyObjectByType<Sfx>()) new GameObject("Sfx").AddComponent<Sfx>();
            if (!FindAnyObjectByType<WantedSystem>()) gameObject.AddComponent<WantedSystem>();
            if (policeStationPos == Vector3.zero) { foreach (var b in FindObjectsByType<BuildingInfo>(FindObjectsSortMode.None)) if (b.kind == "police") policeStationPos = b.door; }
        }

        void Start()
        {
            if (player) { playerBody = player.GetComponent<CharacterBody>(); if (playerBody) { playerBody.OnDeath += OnPlayerDeath; playerBody.OnHit += (b, z, p) => PlayerHurt(); } }
            WorldEvents.OnCharacterDied += OnSomeoneDied;
            Say("Ready. Home sweet derelict home. Press H for controls.");
        }

        static string NameOf(CharacterBody b) { var n = b.GetComponent<NpcBrain>(); return n ? n.displayName : b.name; }

        public void Say(string text) { Log.Add((text, 6f)); if (Log.Count > 7) Log.RemoveAt(0); }
        public void Banner(string title, string text, float seconds) { BannerTitle = title; BannerText = text; BannerT = seconds; }
        public void PlayerHurt() { lastHurtT = Time.time; }

        void OnSomeoneDied(CharacterBody b, string by)
        {
            bool cop = b.GetComponent<PoliceBrain>() != null;
            if (by == "player")
            {
                Kills++; Say($"{NameOf(b)} went down");
                // witnesses: a cop that can see the scene, or a civilian close by with line of sight
                Vector3 scene = b.transform.position; bool witnessed = false; bool copSaw = false;
                foreach (var o in CharacterBody.All)
                {
                    if (o == b || o == playerBody || o.Dead) continue;
                    float d = Vector3.Distance(o.transform.position, scene);
                    bool isCop = o.GetComponent<PoliceBrain>() != null;
                    if (d < (isCop ? 60f : 28f) && Perception.Clear(o.transform.position + Vector3.up * 1.6f, player.transform.position + Vector3.up * 1.2f)) { witnessed = true; if (isCop) copSaw = true; }
                }
                if (cop) { WantedSystem.I.PoliceHostile = true; WantedSystem.I.Add(3, "officer down"); }
                else if (witnessed) WantedSystem.I.Add(copSaw ? 2 : 1, copSaw ? "murder in front of the police" : "a witness saw the killing");
                if (WantedSystem.I.Level > 0) WantedSystem.I.Seen(player.transform.position);
            }
            else if (by == "vehicle") Say($"{NameOf(b)} was run over");
        }

        bool topDown; int shopCycle = -1, factionCycle = -1;

        void Update()
        {
            float dt = Time.deltaTime;
            if (BannerT > 0f) BannerT -= dt;
            for (int i = Log.Count - 1; i >= 0; i--) { var e = Log[i]; e.t -= dt; if (e.t <= 0f) Log.RemoveAt(i); else Log[i] = e; }
            // slow health regen when not hurt for 8 s
            if (playerBody && !playerBody.Dead && playerBody.hp < playerBody.maxHp && Time.time - lastHurtT > 8f && (!MafiaSandbox.Player.Vitals.I || MafiaSandbox.Player.Vitals.I.hunger > 50f)) { regenT += dt; if (regenT > 5f) { regenT = 0f; playerBody.hp++; } }
            if (Input.GetKeyDown(KeyCode.R) && player && !player.InputLocked && !(playerBody && playerBody.Dead)) Respawn();
            if (Input.GetKeyDown(KeyCode.F11) && playerBody && !playerBody.Dead) playerBody.Hit("torso", playerBody.ChestPos, player.transform.forward, 99, true, 1f, "test");   // F11: test death
            if (Input.GetKeyDown(KeyCode.F10) && player && playerCamera)   // F10: look at the nearest living pedestrian (debug)
            {
                CharacterBody best = null; float bd = float.MaxValue;
                foreach (var b in CharacterBody.All) { if (b == playerBody || b.Dead) continue; float d = (b.transform.position - player.transform.position).sqrMagnitude; if (d < bd) { bd = d; best = b; } }
                if (best) playerCamera.LookAt(best.ChestPos);
            }
            if (Input.GetKeyDown(KeyCode.F9) && player)   // F9: spawn a standing test pedestrian 5 m ahead (debug)
            {
                var sp = FindAnyObjectByType<NpcSpawner>();
                if (sp && sp.prefabs != null && sp.prefabs.Length > 0) { var p = player.transform.position + player.transform.forward * 5f + Vector3.up * 0.1f; var n = sp.Spawn<CivilianBrain>(sp.prefabs[Random.Range(0, sp.prefabs.Length)], p, player.transform.eulerAngles.y + 180f); n.dummy = true; }
            }
            if (Input.GetKeyDown(KeyCode.F5)) ToggleTopDown();   // F5: top-down debug view over the player
            if (topDown && playerCamera && player) { playerCamera.transform.position = player.transform.position + Vector3.up * 80f; playerCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f); }
            if (Input.GetKeyDown(KeyCode.F4)) { var dn = FindAnyObjectByType<MafiaSandbox.World.DayNightCycle>(); if (dn) { dn.timeOfDay = Mathf.Abs(dn.timeOfDay - 13f) < 0.01f && dn.paused ? 22f : 13f; dn.paused = true; Say(dn.timeOfDay > 20f ? "Debug: night, clock paused" : "Debug: noon, clock paused"); } }   // F4: noon / pause clock
            if (Input.GetKeyDown(KeyCode.F1) && MafiaSandbox.Economy.EconomyManager.I) { MafiaSandbox.Economy.EconomyManager.I.cash += 500; Say("Debug: +$500"); }   // F1: cash (debug)
            if (Input.GetKeyDown(KeyCode.F2) && player && !player.InputLocked)   // F2: teleport into the next shop (debug)
            {
                var eco = MafiaSandbox.Economy.EconomyManager.I;
                if (eco && eco.shops.Count > 0)
                {
                    var sh = eco.shops[(++shopCycle) % eco.shops.Count]; var bi = sh.info;
                    bool hasCounter = bi.kind != "house" && bi.kind != "home" && bi.counter != Vector3.zero;
                    Vector3 anchor = hasCounter ? bi.counter : bi.footprint.center;
                    Vector3 toDoor = bi.door - anchor; toDoor.y = 0f; toDoor.Normalize();
                    Vector3 p = hasCounter ? anchor + toDoor * 1.4f : bi.door - toDoor * 1.5f; p.y = bi.footprint.min.y + 0.1f;
                    float yaw = Mathf.Atan2(-toDoor.x, -toDoor.z) * Mathf.Rad2Deg;
                    player.Teleport(p, yaw); if (playerCamera) playerCamera.SetYaw(yaw); Say($"Debug: {bi.label}");
                }
            }
            if (Input.GetKeyDown(KeyCode.F3)) { var sb = new System.Text.StringBuilder("[Traffic]\n"); if (player) sb.Append("PLAYER @").Append(player.transform.position.ToString("F2")).Append(" home=").Append(homePosition.ToString("F2")).Append(" station=").Append(policeStationPos.ToString("F1")).Append('\n'); foreach (var d in FindObjectsByType<MafiaSandbox.Vehicles.TrafficDriver>(FindObjectsSortMode.None)) sb.Append(d.name).Append(" @").Append(d.transform.position.ToString("F1")).Append(" yaw").Append(d.transform.eulerAngles.y.ToString("F0")).Append("  ").Append(d.State).Append("  rbv").Append(d.GetComponent<MafiaSandbox.Vehicles.VehicleController>().RbVelocity.magnitude.ToString("F1")).Append(" contact(").Append((Time.time - d.GetComponent<MafiaSandbox.Vehicles.VehicleController>().LastContactT).ToString("F0")).Append("s ago) ").Append(d.GetComponent<MafiaSandbox.Vehicles.VehicleController>().LastContact).Append('\n'); foreach (var pv in FindObjectsByType<MafiaSandbox.Vehicles.VehicleController>(FindObjectsSortMode.None)) if (pv.isPlayerCar) sb.Append("PLAYER CAR ").Append(pv.name).Append(" @").Append(pv.transform.position.ToString("F2")).Append(" yaw").Append(pv.transform.eulerAngles.y.ToString("F0")).Append(" speed").Append(pv.Speed.ToString("F1")).Append(" rbv").Append(pv.RbVelocity.ToString("F2")).Append(" controlled=").Append(pv.Controlled).Append(" contact(").Append((Time.time - pv.LastContactT).ToString("F0")).Append("s ago) ").Append(pv.LastContact).Append(" ground=").Append(pv.DebugGround).Append('\n');
                System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "..", "traffic_dump.txt"), sb.ToString()); Debug.Log(sb.ToString()); }   // F3: dump traffic state to the console
            if (Input.GetKeyDown(KeyCode.F12) && MafiaSandbox.World.Atmosphere.I) MafiaSandbox.World.Atmosphere.I.CycleWeather();   // F12: weather
            if (Input.GetKeyDown(KeyCode.F6)) MafiaSandbox.Vehicles.TrafficDriver.ShowDebug = !MafiaSandbox.Vehicles.TrafficDriver.ShowDebug;   // F6: traffic debug labels
            if (Input.GetKeyDown(KeyCode.F8) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))   // Shift+F8: pick a fight with the crew whose turf you stand in (debug)
            { var fm = Factions.FactionManager.I; if (fm && fm.Territory != null) { fm.PlayerAttacked(fm.Territory, true); } }
            else if (Input.GetKeyDown(KeyCode.F8) && WantedSystem.I) WantedSystem.I.Add(1, "debug");   // F8: +1 star (debug)
            if (Input.GetKeyDown(KeyCode.F7) && player && !player.InputLocked && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))   // Shift+F7: cycle the gang / family HQs (debug)
            {
                var all = Factions.FactionDefs.All; factionCycle = (factionCycle + 1) % all.Count; var f = all[factionCycle];
                Vector3 p = f.HqUnity + Vector3.forward * 2f;   // the HQ doors face +z (prototype) roads, so 4 m out on the road side
                if (UnityEngine.AI.NavMesh.SamplePosition(p, out var h, 8f, UnityEngine.AI.NavMesh.AllAreas)) p = h.position;
                player.Teleport(p + Vector3.up * 0.1f, 0f); playerCamera?.SetYaw(180f); Say($"→ {f.name} HQ");
            }
            else if (Input.GetKeyDown(KeyCode.F7) && player && !player.InputLocked)   // F7: teleport in front of the police station (debug)
            {
                Vector3 p = policeStationPos + (homePosition - policeStationPos).normalized * 0f + Vector3.right * 6f;
                if (UnityEngine.AI.NavMesh.SamplePosition(p, out var h, 8f, UnityEngine.AI.NavMesh.AllAreas)) p = h.position;
                player.Teleport(p + Vector3.up * 0.1f, 0f);
            }
        }

        void OnPlayerDeath(CharacterBody b, string by)
        {
            Banner("YOU DIED", by == "vehicle" ? "Run over." : by == "police" ? "You were shot by the police." : by == "gang" ? "The crew at the construction site beat you to a pulp." : by == "test" ? "Test death." : "You were killed.", 4f);
            if (playerCamera) playerCamera.SetFirstPerson(false);
            player.InputLocked = true;
            WantedSystem.I?.Clear();
            StartCoroutine(RespawnAfter(4f));
        }

        IEnumerator RespawnAfter(float s) { yield return new WaitForSeconds(s); Respawn(); }

        public void ToggleTopDown()
        {
            if (!playerCamera || !player) return;
            topDown = !topDown; playerCamera.enabled = !topDown;
            var cam = playerCamera.GetComponent<Camera>(); cam.orthographic = topDown; if (topDown) cam.orthographicSize = 45f;
        }

        public void Respawn()
        {
            if (playerBody && playerBody.Dead) playerBody.Respawn(homePosition, homeYaw);
            else player.Teleport(homePosition, homeYaw);
            player.InputLocked = false; Arrested = false; MafiaSandbox.Player.Vitals.I?.Reset();
            Say("You wake up back home.");
        }

        /// <summary>Called by a police officer who caught the player on foot.</summary>
        public void Arrest(PoliceBrain cop)
        {
            if (Arrested || (playerBody && playerBody.Dead)) return;
            Arrested = true;
            Banner("ARRESTED", "You didn't get away. A night at the station…", 3.5f);
            Say($"{cop.displayName}: Hands behind your back! You're under arrest.");
            player.InputLocked = true;
            var vi = player.GetComponent<VehicleInteraction>(); if (vi && vi.Current) vi.Exit();
            MafiaSandbox.Economy.EconomyManager.I?.OnArrested();
            StartCoroutine(ReleaseAfter(3.5f));
        }

        IEnumerator ReleaseAfter(float s)
        {
            yield return new WaitForSeconds(s);
            Vector3 p = policeStationPos != Vector3.zero ? policeStationPos + Vector3.forward * 0f : homePosition;
            if (UnityEngine.AI.NavMesh.SamplePosition(p, out var h, 6f, UnityEngine.AI.NavMesh.AllAreas)) p = h.position;
            player.Teleport(p + Vector3.up * 0.1f, player.transform.eulerAngles.y);
            WantedSystem.I?.Clear();
            player.InputLocked = false; Arrested = false;
            Say("Released from the police station.");
        }

        public void OnKeeperHeldUp(KeeperBrain k) { }
    }
}
