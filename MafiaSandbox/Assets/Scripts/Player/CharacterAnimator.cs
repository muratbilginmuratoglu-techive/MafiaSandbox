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
        string upperClip; float upperT, upperDur; Transform hips, spine1, spine2, head;
        readonly System.Collections.Generic.HashSet<string> mixed = new System.Collections.Generic.HashSet<string>();

        void Awake()
        {
            anim = GetComponentInChildren<Animation>();
            if (anim == null) { Debug.LogWarning($"{name}: no Legacy Animation component found. Run MafiaSandbox ▸ Setup and reimport the character.", this); return; }
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                if (t.name.EndsWith("Spine1")) { spine = t; spine1 = t; }
                else if (t.name.EndsWith("Spine2")) spine2 = t;
                else if (t.name.EndsWith("Hips")) hips = t;
                else if (t.name.EndsWith("Head")) head = t;
            }
            foreach (AnimationState st in anim)
            {
                st.wrapMode = IsOneShot(st.name) ? WrapMode.Once : WrapMode.Loop;
                st.layer = 0;
            }
            anim.playAutomatically = false;
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            Play("Idle");
        }

        public static bool IsOneShot(string clip) =>
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
            upperClip = clip; upperDur = upperT = st.length / Mathf.Max(0.01f, speed);
            return st.length / Mathf.Max(0.01f, speed);
        }

        public bool UpperBusy => upperT > 0f;

        static float S(float p, float a, float b) { float x = Mathf.Clamp01((p - a) / (b - a)); return x * x * (3f - 2f * x); }

        /// <summary>
        /// The authored swing clips only move the arms. On top of them the whole torso winds up, whips through and follows through
        /// (world-axis twists, so it doesn't depend on the rig's bone axes): bat = full-body sweep, knife = quick lunge with the shoulder behind it.
        /// </summary>
        void LateUpdate()
        {
            if (upperT <= 0f || upperClip == null || !spine1) return;
            bool bat = upperClip == "Bat_Attack", knife = upperClip == "Knife_Attack"; if (!bat && !knife) return;
            float p = Mathf.Clamp01(1f - upperT / Mathf.Max(0.01f, upperDur)); float yaw, lean, hipShift = 0f, headYaw;
            if (bat)
            {
                float back = S(p, 0f, 0.3f), sw = S(p, 0.3f, 0.55f), rec = S(p, 0.72f, 1f);
                yaw = Mathf.Lerp(Mathf.Lerp(0f, 30f, back), -48f, sw) * (1f - rec);
                lean = Mathf.Lerp(Mathf.Lerp(0f, -7f, back), 14f, sw) * (1f - rec);
                hipShift = 0.05f * sw * (1f - rec); headYaw = -yaw * 0.5f;
            }
            else
            {
                float pull = S(p, 0f, 0.25f) * (1f - S(p, 0.25f, 0.5f)), th = S(p, 0.25f, 0.5f) * (1f - S(p, 0.55f, 1f));
                yaw = 12f * pull - 20f * th; lean = -4f * pull + 20f * th; hipShift = 0.12f * th; headYaw = -yaw * 0.6f;
            }
            Vector3 up = transform.up, right = transform.right;
            if (hips) { hips.rotation = Quaternion.AngleAxis(yaw * 0.25f, up) * hips.rotation; hips.position += transform.forward * hipShift - up * (hipShift * 0.4f); }
            spine1.rotation = Quaternion.AngleAxis(yaw * 0.5f, up) * Quaternion.AngleAxis(lean * 0.5f, right) * spine1.rotation;
            if (spine2) spine2.rotation = Quaternion.AngleAxis(yaw * 0.4f, up) * Quaternion.AngleAxis(lean * 0.5f, right) * spine2.rotation;
            if (head) head.rotation = Quaternion.AngleAxis(headYaw * 0.5f, up) * head.rotation;
        }

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
