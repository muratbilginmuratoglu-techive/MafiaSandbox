using UnityEngine;

namespace MafiaSandbox.World
{
    /// <summary>Sun + ambient over a 24 h cycle. Like the prototype: 1 real minute = 1 game hour. Put it on the Directional Light.</summary>
    public class DayNightCycle : MonoBehaviour
    {
        public static DayNightCycle I { get; private set; }
        [Range(0f, 24f)] public float timeOfDay = 13f;
        public float hoursPerRealSecond = 1f / 60f;
        public bool paused;
        public Gradient sunColor;
        public AnimationCurve sunIntensity = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        Light sun;
        public int Day = 1;
        public float TotalHours => (Day - 1) * 24f + timeOfDay;
        /// <summary>Jump forward to the given clock hour (next day if it is already past).</summary>
        public void SkipTo(float hour) { if (hour <= timeOfDay) Day++; timeOfDay = hour; }

        void Reset() { BuildDefaults(); }
        void Awake()
        {
            I = this; sun = GetComponent<Light>();
            if (sunColor == null || sunColor.colorKeys.Length == 0) BuildDefaults();
        }

        void BuildDefaults()
        {
            sunColor = new Gradient();
            sunColor.SetKeys(new[] {
                new GradientColorKey(new Color(0.25f, 0.3f, 0.5f), 0.0f), new GradientColorKey(new Color(1f, 0.55f, 0.3f), 0.26f),
                new GradientColorKey(new Color(1f, 0.96f, 0.9f), 0.5f), new GradientColorKey(new Color(1f, 0.5f, 0.25f), 0.78f), new GradientColorKey(new Color(0.25f, 0.3f, 0.5f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        }

        void Update()
        {
            if (!paused)
            {
                // daylight (06-20, 14 h) lasts 10 real minutes and so does the night (20-06, 10 h)
                float rate = (IsNight ? 10f : 14f) / 600f * (hoursPerRealSecond * 60f);
                timeOfDay += rate * Time.deltaTime; if (timeOfDay >= 24f) { timeOfDay -= 24f; Day++; }
            }
            float t = timeOfDay / 24f;
            // sun elevation: -90 at midnight, +90 at noon-ish
            float elev = Mathf.Sin((t - 0.25f) * Mathf.PI * 2f) * 75f;
            transform.rotation = Quaternion.Euler(elev, 35f + t * 60f, 0f);
            float day = Mathf.Clamp01(Mathf.InverseLerp(-8f, 12f, elev));
            if (sun) { sun.color = sunColor.Evaluate(t); sun.intensity = Mathf.Lerp(0.02f, 1.0f, day); sun.shadows = day > 0.05f ? LightShadows.Soft : LightShadows.None; }
            RenderSettings.ambientLight = Color.Lerp(new Color(0.08f, 0.09f, 0.14f), new Color(0.36f, 0.39f, 0.46f), day);   // sun + ambient used to add up past white on light pavement
            RenderSettings.fogColor = Color.Lerp(new Color(0.05f, 0.06f, 0.1f), new Color(0.72f, 0.8f, 0.9f), day);
        }

        public bool IsNight => timeOfDay < 6f || timeOfDay > 20f;
    }
}
