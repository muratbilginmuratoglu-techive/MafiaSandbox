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

        void Update()
        {
            float dt = Time.deltaTime;
            if (BannerT > 0f) BannerT -= dt;
            for (int i = Log.Count - 1; i >= 0; i--) { var e = Log[i]; e.t -= dt; if (e.t <= 0f) Log.RemoveAt(i); else Log[i] = e; }
            // slow health regen when not hurt for 8 s
            if (playerBody && !playerBody.Dead && playerBody.hp < playerBody.maxHp && Time.time - lastHurtT > 8f) { regenT += dt; if (regenT > 5f) { regenT = 0f; playerBody.hp++; } }
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
            if (Input.GetKeyDown(KeyCode.F8) && WantedSystem.I) WantedSystem.I.Add(1, "debug");   // F8: +1 star (debug)
            if (Input.GetKeyDown(KeyCode.F7) && player && !player.InputLocked)   // F7: teleport in front of the police station (debug)
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

        public void Respawn()
        {
            if (playerBody && playerBody.Dead) playerBody.Respawn(homePosition, homeYaw);
            else player.Teleport(homePosition, homeYaw);
            player.InputLocked = false; Arrested = false;
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

        public void OnKeeperHeldUp(KeeperBrain k) { /* economy (step 5): hand over the register, silent alarm */ }
    }
}
