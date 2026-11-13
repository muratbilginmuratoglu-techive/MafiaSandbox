using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Player;
using MafiaSandbox.Vehicles;

namespace MafiaSandbox.Core
{
    /// <summary>Scene-level glue: references, layer collision rules, player death / respawn, message log. Economy and wanted level plug in later.</summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }
        public PlayerController player;
        public PlayerCamera playerCamera;
        public VehicleController playerCar;
        public Vector3 homePosition;
        public float homeYaw;

        public string BannerTitle { get; private set; }
        public string BannerText { get; private set; }
        public float BannerT { get; private set; }
        public readonly List<(string text, float t)> Log = new List<(string, float)>();
        public int Kills { get; private set; }

        CharacterBody playerBody;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = -1;
            if (!player) player = FindAnyObjectByType<PlayerController>();
            if (!playerCamera) playerCamera = FindAnyObjectByType<PlayerCamera>();
            int hb = LayerMask.NameToLayer("Hitbox"), ch = LayerMask.NameToLayer("Characters");
            if (hb >= 0 && ch >= 0) { Physics.IgnoreLayerCollision(hb, ch, true); }
            if (!FindAnyObjectByType<CombatFx>()) new GameObject("CombatFx").AddComponent<CombatFx>();
            if (!FindAnyObjectByType<Sfx>()) new GameObject("Sfx").AddComponent<Sfx>();
        }

        void Start()
        {
            if (player) { playerBody = player.GetComponent<CharacterBody>(); if (playerBody) playerBody.OnDeath += OnPlayerDeath; }
            WorldEvents.OnCharacterDied += (b, by) => { if (by == "player") { Kills++; Say($"{NameOf(b)} went down"); } else if (by == "vehicle") Say($"{NameOf(b)} was run over"); };
            Say("Ready. Home sweet derelict home. Press H for controls.");
        }

        static string NameOf(CharacterBody b) { var n = b.GetComponent<Npc.NpcCharacter>(); return n ? n.displayName : b.name; }

        public void Say(string text) { Log.Add((text, 6f)); if (Log.Count > 6) Log.RemoveAt(0); }
        public void Banner(string title, string text, float seconds) { BannerTitle = title; BannerText = text; BannerT = seconds; }

        void Update()
        {
            float dt = Time.deltaTime;
            if (BannerT > 0f) BannerT -= dt;
            for (int i = Log.Count - 1; i >= 0; i--) { var e = Log[i]; e.t -= dt; if (e.t <= 0f) Log.RemoveAt(i); else Log[i] = e; }
            if (Input.GetKeyDown(KeyCode.R) && player && !player.InputLocked && !(playerBody && playerBody.Dead)) Respawn();
            if (Input.GetKeyDown(KeyCode.F11) && playerBody && !playerBody.Dead) playerBody.Hit("torso", playerBody.ChestPos, player.transform.forward, 99, true, 1f, "test");   // Ctrl+K: test death
            if (Input.GetKeyDown(KeyCode.F10) && player && playerCamera)   // F10: look at the nearest living pedestrian (debug)
            {
                CharacterBody best = null; float bd = float.MaxValue;
                foreach (var b in CharacterBody.All) { if (b == playerBody || b.Dead) continue; float d = (b.transform.position - player.transform.position).sqrMagnitude; if (d < bd) { bd = d; best = b; } }
                if (best) playerCamera.LookAt(best.ChestPos);
            }
            if (Input.GetKeyDown(KeyCode.F9) && player)   // F9: spawn a test pedestrian 6 m ahead (debug)
            {
                var sp = FindAnyObjectByType<Npc.NpcSpawner>();
                if (sp && sp.prefabs != null && sp.prefabs.Length > 0) { var p = player.transform.position + player.transform.forward * 5f + Vector3.up * 0.1f; var n = sp.Spawn(sp.prefabs[Random.Range(0, sp.prefabs.Length)], p, player.transform.eulerAngles.y + 180f, 900 + Random.Range(0, 99)); n.dummy = true; }
            }
        }

        void OnPlayerDeath(CharacterBody b, string by)
        {
            Banner("YOU DIED", by == "vehicle" ? "Run over." : by == "test" ? "Test death." : "You were killed.", 4f);
            if (playerCamera) playerCamera.SetFirstPerson(false);
            player.InputLocked = true;
            StartCoroutine(RespawnAfter(4f));
        }

        IEnumerator RespawnAfter(float s) { yield return new WaitForSeconds(s); Respawn(); }

        public void Respawn()
        {
            if (playerBody && playerBody.Dead) playerBody.Respawn(homePosition, homeYaw);
            else player.Teleport(homePosition, homeYaw);
            player.InputLocked = false;
            Say("You wake up back home.");
        }
    }
}
