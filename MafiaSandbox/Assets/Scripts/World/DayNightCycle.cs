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
        /// <summary>True from 05:00 on: the clock stands still until the player sleeps.</summary>
        public bool Frozen { get; private set; }
        float exhaustT;
        public float TotalHours => (Day - 1) * 24f + timeOfDay;
        /// <summary>Jump forward to the given clock hour (next day if it is already past).</summary>
        public void SkipTo(float hour) { if (hour <= timeOfDay) Day++; timeOfDay = hour; Frozen = false; }

        void Reset() { BuildDefaults(); }
        void Awake()
        {
            I = this; sun = GetComponent<Light>();
            if (MafiaSandbox.Core.SaveGame.Has) { Day = MafiaSandbox.Core.SaveGame.SavedDay; timeOfDay = 6.5f; }
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
                // the day (06:00-20:00, 14 h) lasts 10 real minutes, the night (20:00-05:00, 9 h) 8 minutes; at 05:00 the clock stops until you sleep
                float rate = (IsNight ? 9f / 480f : 14f / 600f) * (hoursPerRealSecond * 60f);
                if (!Frozen) { timeOfDay += rate * Time.deltaTime; if (timeOfDay >= 24f) { timeOfDay -= 24f; Day++; } }
                if (!Frozen && timeOfDay >= 5f && timeOfDay < 6f)
                {
                    timeOfDay = 5f; Frozen = true;
                    var gmF = MafiaSandbox.Core.GameManager.I; if (gmF) { gmF.Banner("05:00", "Dawn is close and you are dead on your feet. Go home and sleep (F at your bed) to save and start the next day.", 6f); gmF.Say("05:00 - time has stopped. Sleep to start a new day."); }
                }
                if (Frozen)
                {
                    // exhausted: stamina stays low, and a reminder every half minute
                    var vt = MafiaSandbox.Player.Vitals.I; if (vt) vt.stamina = Mathf.Min(vt.stamina, 30f);
                    exhaustT -= Time.deltaTime; if (exhaustT <= 0f) { exhaustT = 30f; var g2 = MafiaSandbox.Core.GameManager.I; if (g2) g2.Say("You can barely stand. Go home and sleep."); }
                }
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
