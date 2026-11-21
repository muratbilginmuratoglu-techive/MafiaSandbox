using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Npc;
using MafiaSandbox.UI;

namespace MafiaSandbox.Player
{
    /// <summary>
    /// Talking to people on the street. Y: say hello to the person you are facing (a wave and a greeting). U: throw them a jibe.
    /// What comes back depends on who it is and how they feel: civilians wave back, stop for a word, get offended, back off or
    /// fire one back; gang members nod or square up (heckle a crew twice and it is a fight); the police tell you to watch your
    /// mouth and, if you keep at it, take you in. Your line appears in a speech bubble over your own head.
    /// </summary>
    public class PlayerSpeech : MonoBehaviour
    {
        public static PlayerSpeech I { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (I) return; var go = new GameObject("PlayerSpeech"); DontDestroyOnLoad(go); I = go.AddComponent<PlayerSpeech>(); }

        public const KeyCode GreetKey = KeyCode.Y, HeckleKey = KeyCode.U;
        float cd; NpcBrain target; float scanT; GUIStyle hint;

        static float Hour => World.DayNightCycle.I ? World.DayNightCycle.I.timeOfDay : 12f;

        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player) return; var pc = gm.player;
            cd -= Time.deltaTime; scanT -= Time.deltaTime;
            var body = pc.GetComponent<CharacterBody>(); var vi = pc.GetComponent<Vehicles.VehicleInteraction>();
            bool can = !(body && body.Dead) && !GameUI.Blocking && !Workshop.Active && !(vi && vi.Current && !vi.Current.openRide) && !pc.InputLocked && !AdminPanel.IsOpen;
            if (scanT <= 0f) { scanT = 0.15f; target = can ? FindTarget(pc) : null; }
            if (!can || cd > 0f) return;
            bool greet = Input.GetKeyDown(GreetKey), rude = Input.GetKeyDown(HeckleKey);
            if (!greet && !rude) return;
            cd = 1.3f;
            string line = rude ? Jibe(target) : Hello(target);
            Barks.Say(pc.transform, line, 2.4f, new Color(1f, 0.95f, 0.75f), 30f);
            var anim = pc.GetComponent<CharacterAnimator>(); if (anim && greet && !anim.UpperBusy) anim.PlayUpper("Wave");
            if (target) target.HearPlayer(rude);
        }

        /// <summary>The person in front of you, within talking distance, in plain view.</summary>
        static NpcBrain FindTarget(PlayerController pc)
        {
            Vector3 p = pc.transform.position, f = pc.transform.forward; var cam = GameManager.I.playerCamera; if (cam) f = Quaternion.Euler(0f, cam.Yaw, 0f) * Vector3.forward;
            NpcBrain best = null; float bs = 1e9f;
            foreach (var b in CharacterBody.All)
            {
                if (!b || b.Dead || b.isPlayer) continue; Vector3 to = b.transform.position - p; to.y = 0f; float d = to.magnitude; if (d > 7f || d < 0.2f) continue;
                float dot = Vector3.Dot(f, to / d); if (dot < 0.55f) continue;
                var nb = b.GetComponent<NpcBrain>(); if (!nb) continue;
                float score = d * (1.6f - dot); if (score >= bs) continue;
                if (!Perception.Clear(p + Vector3.up * 1.5f, b.ChestPos)) continue;
                bs = score; best = nb;
            }
            return best;
        }

        static string Hello(NpcBrain t)
        {
            float h = Hour;
            if (t is PoliceBrain) return Barks.Pick("Officer.", "Evening, officer.", "Keeping us safe?");
            if (t is Factions.FactionBrain || t is GangBrain) return Barks.Pick("Hey, fellas.", "How's business?", "Easy, boys.", "Nice day for it.");
            if (t is Gang.CrewBrain) return Barks.Pick("How we doing?", "Stay sharp.", "Good work today.");
            if (t is KeeperBrain) return Barks.Pick("Hi there.", "How's business?", "Morning!");
            return h > 5f && h < 11f ? Barks.Pick("Good morning!", "Morning!", "Lovely morning.") : h > 18f || h < 5f ? Barks.Pick("Evening!", "Good evening.", "Nice night.") : Barks.Pick("Hey there!", "Hi!", "How's it going?", "Nice day, huh?", "Hello, friend.", "What's new?", "Looking good today!", "Hey, neighbour.", "All good?");
        }
        static string Jibe(NpcBrain t)
        {
            if (!t) return Barks.Pick("Hey! Anybody?", "This town's dead.", "Woo!");
            if (t is PoliceBrain) return Barks.Pick("Donut break, officer?", "Shouldn't you be catching criminals?", "Nice hat, officer.", "Writing tickets again?");
            if (t is Factions.FactionBrain || t is GangBrain) return Barks.Pick("Your crew's a joke.", "Nice turf. Shame if something happened to it.", "Who dressed you, your mother?", "You lot don't scare me.", "This is my street now.", "Tell your boss I said hi.", "You call that a gang?");
            if (t is Gang.CrewBrain) return Barks.Pick("Look alive!", "Are you sleeping on the job?", "My grandma fights better.");
            if (t is KeeperBrain) return Barks.Pick("Your prices are a robbery.", "Is this place still open?", "Smells funny in here.");
            return Barks.Pick("Nice shoes, grandpa!", "Hey, look at this guy!", "What are you looking at?", "Move it, slowpoke!", "You lost?", "Nice hat!", "Did you get dressed in the dark?", "Walk much?", "Nice haircut. Did you lose a bet?", "Out of my way, nobody.", "Smile, it might help.", "Is that your face or a mask?", "Go home, loser.", "Hey, ugly!");
        }

        void OnGUI()
        {
            if (!target || GameUI.Blocking || Workshop.Active || PhonePanel.IsOpen || CrewPanel.IsOpen || AdminPanel.IsOpen) return;
            var cam = Camera.main; if (!cam) return; Vector3 sp = cam.WorldToScreenPoint(target.transform.position + Vector3.up * 2.35f); if (sp.z <= 0f) return;
            if (hint == null) { hint = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, richText = true }; hint.normal.textColor = new Color(1f, 1f, 1f, 0.85f); }
            var r = new Rect(sp.x - 90f, Screen.height - sp.y - 9f, 180f, 18f);
            GUI.color = new Color(0f, 0f, 0f, 0.45f); GUI.DrawTexture(new Rect(r.x + 28f, r.y, r.width - 56f, r.height), Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(r, "<b>Y</b> greet   ·   <b>U</b> heckle", hint);
        }
    }
}
