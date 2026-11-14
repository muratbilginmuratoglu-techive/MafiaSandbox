using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Core;

namespace MafiaSandbox.World
{
    /// <summary>
    /// Weather (clear / rain / snow, drifting every few minutes or forced with F12), the street lamps that come on at dusk
    /// (a small pool of point lights follows the player), the barrel fires in the camp (flame particles, flicker, crackle),
    /// and the ambient / rain loops. Works with DayNightCycle, which owns the sun.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class Atmosphere : MonoBehaviour
    {
        public static Atmosphere I { get; private set; }
        public enum WeatherKind { Clear, Rain, Snow }
        public WeatherKind weather = WeatherKind.Clear;
        public float weatherChangeEvery = 240f;
        float weatherT; ParticleSystem rain, snow; AudioSource rainLoop, ambientLoop;
        readonly List<Transform> lamps = new List<Transform>(); Light[] lampLights = new Light[10];
        class Fire { public Vector3 pos; public Light light; public ParticleSystem ps; public float ph; }
        readonly List<Fire> fires = new List<Fire>();
        readonly List<Material> grassMats = new List<Material>(); readonly List<Color> grassBases = new List<Color>(); bool grassFound;

        void Awake() { I = this; }

        void Start()
        {
            QualitySettings.pixelLightCount = Mathf.Max(QualitySettings.pixelLightCount, 8);   // lamps + fires + muzzle flashes
            weatherT = weatherChangeEvery * (0.5f + Random.value);
            // props by name: lamps and fire barrels
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name.StartsWith("Sokak_Lambasi")) lamps.Add(t);
                else if (t.name.StartsWith("Ates_Varili")) fires.Add(MakeFire(t.position));
            }
            for (int i = 0; i < lampLights.Length; i++)
            {
                var go = new GameObject("LampLight"); go.transform.SetParent(transform, false); var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 16f; l.intensity = 0f; l.color = new Color(1f, 0.85f, 0.6f); l.shadows = LightShadows.None; lampLights[i] = l;
            }
            rain = MakePrecip("Rain", new Color(0.67f, 0.77f, 0.88f, 0.55f), 0.03f, 0.9f, 22f, true);
            snow = MakePrecip("Snow", new Color(1f, 1f, 1f, 0.9f), 0.12f, 0.12f, 1.6f, false);
            if (Sfx.I) { rainLoop = Sfx.I.Loop("rainloop", 0f, false); ambientLoop = Sfx.I.Loop("ambient", 0.18f, false); }
            // the grass tint lives on the static mesh: find a material that looks like grass (greenest)
            // every grass-coloured ground material in the scene (the prototype's plane and the districts' quads) gets the same snow tint
            var world = GameObject.Find("World"); if (world)
            {
                var seen = new HashSet<Material>();
                foreach (var r in world.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!r.sharedMaterial || !r.sharedMaterial.HasProperty("_Color")) continue;
                    var c = r.sharedMaterial.color; float g = c.g - (c.r + c.b) * 0.5f; if (g < 0.08f || c.g < 0.45f) continue;   // greenish only
                    foreach (var m in r.materials) if (seen.Add(m)) { grassMats.Add(m); grassBases.Add(m.color); }
                }
                grassFound = grassMats.Count > 0;
            }
            Apply();
        }

        ParticleSystem MakePrecip(string name, Color c, float w, float h, float speed, bool stretch)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>(); var main = ps.main; main.loop = true; main.playOnAwake = false; main.startLifetime = stretch ? 2.2f : 10f; main.startSpeed = speed; main.startSize = new ParticleSystem.MinMaxCurve(w, w * 1.6f); main.startSize3D = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 3000; main.gravityModifier = stretch ? 0.4f : 0.02f; main.startColor = c;
            var em = ps.emission; em.rateOverTime = stretch ? 1200f : 700f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(50f, 16f, 50f); sh.rotation = Vector3.zero;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = stretch ? new ParticleSystem.MinMaxCurve(3f, 3f) : new ParticleSystem.MinMaxCurve(-0.8f, 0.8f); vel.y = new ParticleSystem.MinMaxCurve(-speed, -speed); vel.z = stretch ? new ParticleSystem.MinMaxCurve(0f, 0f) : new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.material = new Material(Shader.Find("Particles/Standard Unlit")); r.material.SetColor("_Color", Color.white);
            if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 0f; r.velocityScale = 0.04f; } else r.renderMode = ParticleSystemRenderMode.Billboard;
            go.transform.localPosition = Vector3.zero; return ps;
        }

        Fire MakeFire(Vector3 barrel)
        {
            var f = new Fire { pos = barrel + Vector3.up * 0.95f, ph = Random.value * 10f };
            var lgo = new GameObject("FireLight"); lgo.transform.position = barrel + Vector3.up * 1.6f; f.light = lgo.AddComponent<Light>(); f.light.type = LightType.Point; f.light.color = new Color(1f, 0.54f, 0.24f); f.light.range = 11f; f.light.intensity = 1.5f; f.light.shadows = LightShadows.None;
            var go = new GameObject("Flames"); go.transform.position = f.pos; var ps = go.AddComponent<ParticleSystem>(); var main = ps.main; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.8f); main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 200;
            var em = ps.emission; em.rateOverTime = 60f; var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.17f;
            var col = ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.4f), new GradientColorKey(new Color(0.3f, 0.3f, 0.3f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) }); col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.material = new Material(Shader.Find("Particles/Standard Unlit")); r.material.SetColor("_Color", Color.white); r.renderMode = ParticleSystemRenderMode.Billboard;
            f.ps = ps; return f;
        }

        public void SetWeather(WeatherKind w) { weather = w; Apply(); GameManager.I?.Say(w == WeatherKind.Clear ? "The sky clears up" : w == WeatherKind.Rain ? "It starts to rain" : "It starts to snow"); }
        public void CycleWeather() => SetWeather((WeatherKind)(((int)weather + 1) % 3));

        void Apply()
        {
            if (rain) { if (weather == WeatherKind.Rain) rain.Play(); else rain.Stop(); }
            if (snow) { if (weather == WeatherKind.Snow) snow.Play(); else snow.Stop(); }
            if (grassFound) for (int i = 0; i < grassMats.Count; i++) if (grassMats[i]) grassMats[i].color = weather == WeatherKind.Snow ? new Color(0.91f, 0.93f, 0.95f) : grassBases[i];
        }

        void Update()
        {
            float dt = Time.deltaTime; var cam = Camera.main; if (!cam) return;
            weatherT -= dt; if (weatherT <= 0f) { weatherT = weatherChangeEvery * (0.6f + Random.value * 0.8f); float r = Random.value; SetWeather(r < 0.55f ? WeatherKind.Clear : r < 0.85f ? WeatherKind.Rain : WeatherKind.Snow); }
            // precipitation follows the camera
            Vector3 top = cam.transform.position + Vector3.up * 7f + cam.transform.forward * 8f; if (rain) rain.transform.position = top; if (snow) snow.transform.position = top;
            if (rainLoop) rainLoop.volume = Mathf.MoveTowards(rainLoop.volume, weather == WeatherKind.Rain ? 0.25f : 0f, dt * 0.3f);
            // fog and sun mood
            var dn = DayNightCycle.I; float night = dn ? (dn.IsNight ? 1f : 0f) : 0f;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = weather == WeatherKind.Clear ? 70f : 25f; RenderSettings.fogEndDistance = weather == WeatherKind.Clear ? 200f + 60f * night : 110f;
            if (weather != WeatherKind.Clear) { RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, new Color(0.55f, 0.58f, 0.63f), 0.6f); RenderSettings.ambientLight *= 0.85f; var sun = dn ? dn.GetComponent<Light>() : null; if (sun) sun.intensity *= 0.5f; }
            if (cam) cam.backgroundColor = RenderSettings.fogColor;
            // street lamps: the nearest ten glow at night / in bad weather
            bool lampsOn = dn && (dn.timeOfDay < 6.5f || dn.timeOfDay > 18.5f || weather != WeatherKind.Clear);
            if (lampsOn && lamps.Count > 0)
            {
                Vector3 pp = cam.transform.position; lamps.Sort((a, b) => (a.position - pp).sqrMagnitude.CompareTo((b.position - pp).sqrMagnitude));
                for (int i = 0; i < lampLights.Length; i++) { if (i < lamps.Count) { lampLights[i].transform.position = lamps[i].position + Vector3.up * 4.2f; lampLights[i].intensity = Mathf.MoveTowards(lampLights[i].intensity, 3.2f, dt * 2f); } }
            }
            else foreach (var l in lampLights) l.intensity = Mathf.MoveTowards(l.intensity, 0f, dt * 2f);
            // fires
            foreach (var f in fires)
            {
                f.ph += dt * (6f + Random.value * 4f); float flick = 0.75f + 0.25f * Mathf.Sin(f.ph) * Mathf.Sin(f.ph * 0.37f + 1.3f);
                f.light.intensity = (night > 0.5f ? 2.6f : 1.2f) * flick;
                if ((f.pos - cam.transform.position).sqrMagnitude < 400f && Random.value < dt * 2.5f) Sfx.At("crackle", f.pos, 0.5f);
            }
        }
    }
}
