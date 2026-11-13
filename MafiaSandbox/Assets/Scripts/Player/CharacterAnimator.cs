using UnityEngine;

namespace MafiaSandbox.Player
{
    /// <summary>
    /// Thin wrapper over the Legacy <see cref="Animation"/> component that glTFast creates for our character GLBs
    /// (import setting: Animation Method = Legacy — set by MafiaSandbox ▸ Setup). Plays clips by name with cross-fades.
    /// Clip names come straight from build_crowd.py: Idle, Walk, Run, Crouch_Idle, Crouch_Walk, Jump, Pistol_Aim, … (38 clips).
    /// Layer 0 = full body locomotion. Layer 1 = upper body (spine and up): fire / attack / hit reactions blend over locomotion.
    /// </summary>
    public class CharacterAnimator : MonoBehaviour
    {
        public float fade = 0.15f;
        public string Current { get; private set; }
        public Animation Anim => anim;

        Animation anim;
        Transform spine;
        string upperClip; float upperT;
        readonly System.Collections.Generic.HashSet<string> mixed = new System.Collections.Generic.HashSet<string>();

        void Awake()
        {
            anim = GetComponentInChildren<Animation>();
            if (anim == null) { Debug.LogWarning($"{name}: no Legacy Animation component found. Run MafiaSandbox ▸ Setup and reimport the character.", this); return; }
            foreach (var t in GetComponentsInChildren<Transform>()) if (t.name.EndsWith("Spine1")) { spine = t; break; }
            foreach (AnimationState st in anim)
            {
                st.wrapMode = IsOneShot(st.name) ? WrapMode.Once : WrapMode.Loop;
                st.layer = 0;
            }
            anim.playAutomatically = false;
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            Play("Idle");
        }

        static bool IsOneShot(string clip) =>
            clip == "Jump" || clip == "Death" || clip.StartsWith("Hit_") || clip.EndsWith("_Fire") || clip.EndsWith("_Attack") || clip == "Give" || clip == "Take" || clip == "Wave";

        public bool Has(string clip) => anim != null && anim[clip] != null;

        /// <summary>Cross-fade to a looping full-body clip (no-op if already playing).</summary>
        public void Play(string clip, float speed = 1f)
        {
            if (anim == null) return;
            var st = anim[clip];
            if (st == null) { if (clip != "Idle") Play("Idle", speed); return; }
            st.speed = speed;
            if (clip == Current) return;
            st.layer = 0; st.blendMode = AnimationBlendMode.Blend;
            anim.CrossFade(clip, fade);
            Current = clip;
        }

        /// <summary>Play a one-shot clip full-body (jump, death). Returns its length.</summary>
        public float PlayOnce(string clip)
        {
            if (anim == null || anim[clip] == null) return 0f;
            var st = anim[clip]; st.speed = 1f; st.layer = 0; st.time = 0f;
            anim.CrossFade(clip, 0.08f);
            Current = clip;
            return st.length;
        }

        /// <summary>Play a one-shot clip on the upper body only (fire, melee swing, hit reaction) while legs keep their locomotion clip.</summary>
        public float PlayUpper(string clip, float speed = 1f)
        {
            if (anim == null || anim[clip] == null) return 0f;
            var st = anim[clip];
            st.layer = 1; st.speed = speed; st.time = 0f; st.wrapMode = WrapMode.Once;
            if (spine && mixed.Add(clip)) st.AddMixingTransform(spine, true);
            anim.CrossFade(clip, 0.06f, PlayMode.StopSameLayer);
            upperClip = clip; upperT = st.length / Mathf.Max(0.01f, speed);
            return st.length / Mathf.Max(0.01f, speed);
        }

        public bool UpperBusy => upperT > 0f;

        void Update()
        {
            if (upperT > 0f) { upperT -= Time.deltaTime; if (upperT <= 0f && anim != null && upperClip != null && anim[upperClip] != null) { anim.Blend(upperClip, 0f, 0.1f); upperClip = null; } }
        }

        public bool IsPlaying(string clip) => anim != null && anim.IsPlaying(clip);

        /// <summary>Freeze the current pose (used right before switching to ragdoll).</summary>
        public void Freeze() { if (anim) anim.enabled = false; enabled = false; }
        public void Unfreeze() { if (anim) anim.enabled = true; enabled = true; Current = null; Play("Idle"); }
    }
}
