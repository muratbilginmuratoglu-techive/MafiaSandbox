using System.Collections.Generic;
using UnityEngine;

namespace MafiaSandbox.Core
{
    /// <summary>
    /// Procedural sound effects synthesised at startup (no audio assets yet): gunshots, swings, hits, body falls, UI clicks.
    /// Mirrors the prototype's WebAudio SFX module. Replace clips with real samples later by assigning them in the inspector.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public static Sfx I { get; private set; }
        const int SR = 22050;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        AudioSource[] voices; int voiceIdx;
        System.Random rng = new System.Random(7);

        void Awake()
        {
            I = this;
            voices = new AudioSource[12];
            for (int i = 0; i < voices.Length; i++) { var go = new GameObject("voice" + i); go.transform.SetParent(transform, false); voices[i] = go.AddComponent<AudioSource>(); voices[i].spatialBlend = 1f; voices[i].rolloffMode = AudioRolloffMode.Linear; voices[i].minDistance = 3f; voices[i].maxDistance = 70f; voices[i].dopplerLevel = 0f; }
            clips["pistol"] = Gun(0.22f, 1.0f, 900f, 0.0025f);
            clips["revolver"] = Gun(0.32f, 1.1f, 600f, 0.004f);
            clips["rifle"] = Gun(0.16f, 0.9f, 1200f, 0.002f);
            clips["shotgun"] = Gun(0.45f, 1.2f, 400f, 0.006f);
            clips["swing"] = Noise(0.18f, 0.5f, 0.02f, 0.14f, true);
            clips["stab"] = Noise(0.12f, 0.7f, 0.004f, 0.05f, false);
            clips["thud"] = Thud(0.2f, 90f);
            clips["impact"] = Noise(0.08f, 0.6f, 0.002f, 0.03f, false);
            clips["ricochet"] = Tone(0.25f, 2400f, 0.5f);
            clips["bodyfall"] = Thud(0.35f, 55f);
            clips["hurt"] = Tone(0.12f, 220f, 0.4f);
            clips["click"] = Noise(0.03f, 0.3f, 0.001f, 0.01f, false);
            clips["pump"] = Noise(0.1f, 0.4f, 0.002f, 0.04f, false);
        }

        float Rnd() => (float)rng.NextDouble() * 2f - 1f;

        AudioClip Make(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, SR, false); c.SetData(data, 0); return c;
        }

        AudioClip Gun(float dur, float vol, float lpHz, float attack)
        {
            int n = (int)(dur * SR); var d = new float[n]; float lp = 0f; float a = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * lpHz / SR));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR; float env = t < attack ? t / attack : Mathf.Exp(-(t - attack) * (6f / dur));
                float x = Rnd(); lp += a * (x - lp);
                float body = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 18f) * 0.6f;   // low thump
                d[i] = Mathf.Clamp((lp * 1.6f + body) * env * vol, -1f, 1f);
            }
            return Make("gun", d);
        }

        AudioClip Noise(float dur, float vol, float attack, float decay, bool whoosh)
        {
            int n = (int)(dur * SR); var d = new float[n]; float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR; float env = t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);
                float cutoff = whoosh ? Mathf.Lerp(300f, 2500f, Mathf.Sin(Mathf.PI * t / dur)) : 3000f;
                float a = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * cutoff / SR));
                lp += a * (Rnd() - lp); d[i] = lp * env * vol * 2f;
            }
            return Make("noise", d);
        }

        AudioClip Thud(float dur, float hz)
        {
            int n = (int)(dur * SR); var d = new float[n]; float lp = 0f;
            for (int i = 0; i < n; i++) { float t = i / (float)SR; float env = Mathf.Exp(-t * 14f); lp += 0.08f * (Rnd() - lp); d[i] = (Mathf.Sin(2f * Mathf.PI * hz * t * (1f - t * 0.8f)) * 0.8f + lp * 1.5f) * env; }
            return Make("thud", d);
        }

        AudioClip Tone(float dur, float hz, float vol)
        {
            int n = (int)(dur * SR); var d = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)SR; d[i] = Mathf.Sin(2f * Mathf.PI * hz * t * (1f - t * 1.5f)) * Mathf.Exp(-t * 12f) * vol; }
            return Make("tone", d);
        }

        public void Play(string name, Vector3 pos, float vol = 1f, float pitchJitter = 0.08f)
        {
            if (!clips.TryGetValue(name, out var c)) return;
            var v = voices[voiceIdx = (voiceIdx + 1) % voices.Length];
            v.transform.position = pos; v.pitch = 1f + (Rnd()) * pitchJitter; v.volume = vol; v.clip = c; v.Play();
        }

        public static void At(string name, Vector3 pos, float vol = 1f) { if (I) I.Play(name, pos, vol); }
    }
}
