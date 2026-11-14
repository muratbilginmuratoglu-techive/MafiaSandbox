using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Player;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// Hold Tab: a radial menu of emotes (Wave, Dance, Talk, Give, Take, Cancel), ported from the prototype. Move the mouse
    /// toward a slice and release Tab to play it; looping emotes (Dance, Talk) run until you move or pick Cancel.
    /// </summary>
    public class EmoteWheel : MonoBehaviour
    {
        static readonly (string clip, string label)[] Emotes = { ("Wave", "Wave"), ("Dance", "Dance"), ("Talk", "Talk"), ("Give", "Give"), ("Take", "Take"), ("Idle", "Cancel") };
        public static EmoteWheel I { get; private set; }
        public bool Open { get; private set; }
        public string Emote { get; private set; }     // looping emote currently playing (null = none)
        Vector2 vec; int sel = -1; float emoteT;
        PlayerController pc; CharacterAnimator anim; PlayerCombat combat; CharacterBody body;

        void Awake() { I = this; }

        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player) return;
            if (!pc) { pc = gm.player; anim = pc.GetComponent<CharacterAnimator>(); combat = pc.GetComponent<PlayerCombat>(); body = pc.GetComponent<CharacterBody>(); }
            bool dead = body && body.Dead; var vi = pc.GetComponent<Vehicles.VehicleInteraction>(); bool driving = vi && vi.Current;
            if (Input.GetKeyDown(KeyCode.Tab) && !Open && !dead && !driving && !GameUI.Blocking) { Open = true; sel = -1; vec = Vector2.zero; }
            if (Open)
            {
                vec += new Vector2(Input.GetAxisRaw("Mouse X"), -Input.GetAxisRaw("Mouse Y")) * 14f;
                if (vec.magnitude > 120f) vec = vec.normalized * 120f;
                sel = vec.magnitude < 28f ? -1 : Mathf.FloorToInt(((Mathf.Atan2(vec.y, vec.x) * Mathf.Rad2Deg + 90f + 30f + 360f) % 360f) / 60f);
                if (Input.GetKeyUp(KeyCode.Tab) || dead || driving) { Open = false; if (sel >= 0 && !dead && !driving) Play(Emotes[sel].clip); }
            }
            // a running emote ends when the player moves, aims, shoots or dies
            if (Emote != null)
            {
                emoteT -= Time.deltaTime;
                bool oneShot = Emote == "Wave" || Emote == "Give" || Emote == "Take";
                if (pc.Moving || dead || driving || (combat && combat.Aiming) || Input.GetMouseButtonDown(0) || (oneShot && emoteT <= 0f)) Stop();
            }
        }

        public void PlayClip(string clip) { if (pc && !(body && body.Dead)) Play(clip); }

        void Play(string clip)
        {
            if (clip == "Idle") { Stop(); return; }
            Emote = clip; emoteT = anim ? anim.PlayOnce(clip) : 1f; if (emoteT <= 0f) emoteT = 1.5f;
            bool oneShot = clip == "Wave" || clip == "Give" || clip == "Take";
            if (!oneShot) { var st = anim && anim.Anim ? anim.Anim[clip] : null; if (st != null) st.wrapMode = WrapMode.Loop; emoteT = 999f; }
            if (combat) combat.Emoting = true;
            pc.LocomotionOverride = Locomotion;
        }
        string Locomotion(bool moving, bool running) => Emote;   // keep the emote clip while standing still (movement cancels it in Update)
        void Stop()
        {
            if (Emote == null) return;
            var st = anim && anim.Anim ? anim.Anim[Emote] : null; if (st != null) st.wrapMode = CharacterAnimator.IsOneShot(Emote) ? WrapMode.Once : WrapMode.Loop;
            Emote = null; if (combat) { combat.Emoting = false; pc.LocomotionOverride = combat.LocomotionFn; } else pc.LocomotionOverride = null;
            if (anim) anim.Play("Idle");
        }

        void OnGUI()
        {
            if (!Open) return;
            float W = Screen.width, H = Screen.height; var c = new Vector2(W / 2f, H / 2f); const float R = 150f, r0 = 42f;
            GUI.color = new Color(0f, 0f, 0f, 0.35f); GUI.DrawTexture(new Rect(0, 0, W, H), UiSkin.White); GUI.color = Color.white;
            // six slices drawn as rotated rounded chips around the centre
            for (int i = 0; i < Emotes.Length; i++)
            {
                float ang = (i * 60f - 90f) * Mathf.Deg2Rad; var p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (R * 0.72f);
                var rr = new Rect(p.x - 54, p.y - 20, 108, 40);
                GUI.color = i == sel ? new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.92f) : Color.white;
                GUI.Box(rr, GUIContent.none, UiSkin.Chip); GUI.color = Color.white;
                var st = new GUIStyle(UiSkin.Center) { fontSize = 15, fontStyle = i == sel ? FontStyle.Bold : FontStyle.Normal }; st.normal.textColor = i == sel ? new Color(0.1f, 0.08f, 0.02f) : UiSkin.Text;
                GUI.Label(rr, Emotes[i].label, st);
            }
            // centre: current direction + hint
            GUI.Box(new Rect(c.x - r0, c.y - r0, r0 * 2, r0 * 2), GUIContent.none, UiSkin.Chip);
            GUI.Label(new Rect(c.x - r0, c.y - r0, r0 * 2, r0 * 2), sel >= 0 ? Emotes[sel].label : "Emote", UiSkin.Center);
            var dot = c + vec * 0.5f; GUI.color = UiSkin.Accent; GUI.DrawTexture(new Rect(dot.x - 4, dot.y - 4, 8, 8), UiSkin.White); GUI.color = Color.white;
            GUI.Label(new Rect(c.x - 150, c.y + R + 10, 300, 20), "move the mouse · release Tab", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter });
        }
    }
}
