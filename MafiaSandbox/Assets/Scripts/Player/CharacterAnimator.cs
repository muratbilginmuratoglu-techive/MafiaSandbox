using UnityEngine;

namespace MafiaSandbox.Player
{
    /// <summary>
    /// Thin wrapper over the Legacy <see cref="Animation"/> component that glTFast creates for our character GLBs
    /// (import setting: Animation Method = Legacy — set by MafiaSandbox ▸ Setup). Plays clips by name with cross-fades.
    /// Clip names come straight from build_crowd.py: Idle, Walk, Run, Crouch_Idle, Crouch_Walk, Jump, Pistol_Aim, … (38 clips).
    /// </summary>
    public class CharacterAnimator : MonoBehaviour
    {
        public float fade = 0.15f;
        public string Current { get; private set; }

        Animation anim;

        void Awake()
        {
            anim = GetComponentInChildren<Animation>();
            if (anim == null) { Debug.LogWarning($"{name}: no Legacy Animation component found. Run MafiaSandbox ▸ Setup and reimport the character.", this); return; }
            foreach (AnimationState st in anim)
            {
                st.wrapMode = IsOneShot(st.name) ? WrapMode.Once : WrapMode.Loop;
            }
            anim.playAutomatically = false;
            Play("Idle");
        }

        static bool IsOneShot(string clip) =>
            clip == "Jump" || clip == "Death" || clip.StartsWith("Hit_") || clip.EndsWith("_Fire") || clip.EndsWith("_Attack") || clip == "Give" || clip == "Take" || clip == "Wave";

        public bool Has(string clip) => anim != null && anim[clip] != null;

        /// <summary>Cross-fade to a looping locomotion clip (no-op if already playing).</summary>
        public void Play(string clip, float speed = 1f)
        {
            if (anim == null || clip == Current) { if (anim != null && anim[clip] != null) anim[clip].speed = speed; return; }
            if (anim[clip] == null) { if (clip != "Idle") Play("Idle", speed); return; }
            anim[clip].speed = speed;
            anim.CrossFade(clip, fade);
            Current = clip;
        }

        /// <summary>Play a one-shot clip on top; returns its length in seconds (0 if missing).</summary>
        public float PlayOnce(string clip)
        {
            if (anim == null || anim[clip] == null) return 0f;
            anim[clip].speed = 1f;
            anim.CrossFade(clip, 0.08f);
            Current = clip;
            return anim[clip].length;
        }

        public bool IsPlaying(string clip) => anim != null && anim.IsPlaying(clip);
    }
}
