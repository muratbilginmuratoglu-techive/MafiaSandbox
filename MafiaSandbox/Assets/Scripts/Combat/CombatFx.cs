using System.Collections.Generic;
using UnityEngine;

namespace MafiaSandbox.Combat
{
    /// <summary>Tracers, muzzle flash, impact / blood particles — all built in code, no asset dependencies. One instance in the scene (GameManager creates it).</summary>
    public class CombatFx : MonoBehaviour
    {
        public static CombatFx I { get; private set; }

        ParticleSystem sparks, dust, blood, muzzleSmoke;
        Light flashLight; float flashT;
        readonly List<(LineRenderer lr, float t)> tracers = new List<(LineRenderer, float)>();
        readonly Stack<LineRenderer> tracerPool = new Stack<LineRenderer>();
        Material tracerMat;
        Transform hitMarkerDummy;

        void Awake()
        {
            I = this;
            sparks = MakePs("Sparks", new Color(1f, 0.85f, 0.4f), new Color(1f, 0.4f, 0.1f), 0.02f, 0.05f, 6f, 0.25f, 0.45f, true);
            dust = MakePs("Dust", new Color(0.75f, 0.7f, 0.6f, 0.6f), new Color(0.6f, 0.55f, 0.5f, 0f), 0.08f, 0.2f, 1.5f, 0.4f, 0.8f, false);
            blood = MakePs("Blood", new Color(0.6f, 0.02f, 0.02f), new Color(0.35f, 0f, 0f), 0.03f, 0.07f, 3.5f, 0.3f, 0.6f, true);
            muzzleSmoke = MakePs("MuzzleSmoke", new Color(0.8f, 0.8f, 0.8f, 0.5f), new Color(0.7f, 0.7f, 0.7f, 0f), 0.06f, 0.16f, 1.2f, 0.3f, 0.6f, false);
            var lg = new GameObject("MuzzleFlashLight"); lg.transform.SetParent(transform, false);
            flashLight = lg.AddComponent<Light>(); flashLight.type = LightType.Point; flashLight.range = 9f; flashLight.color = new Color(1f, 0.8f, 0.5f); flashLight.intensity = 0f; flashLight.shadows = LightShadows.None;
            tracerMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.9f, 0.6f, 0.9f) };
        }

        ParticleSystem MakePs(string name, Color c0, Color c1, float sizeMin, float sizeMax, float speed, float lifeMin, float lifeMax, bool gravity)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed); main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.gravityModifier = gravity ? 1f : 0.05f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 400;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.02f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(c0, 0f), new GradientColorKey(c1, 1f) }, new[] { new GradientAlphaKey(c0.a, 0f), new GradientAlphaKey(c1.a, 1f) });
            col.color = g;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.material = new Material(Shader.Find("Particles/Standard Unlit")); r.material.SetColor("_Color", Color.white);
            r.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        void Burst(ParticleSystem ps, Vector3 pos, Vector3 dir, int n)
        {
            ps.transform.position = pos; ps.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : Vector3.up);
            ps.Emit(n);
        }

        public void Impact(Vector3 point, Vector3 normal, bool metal) { Burst(sparks, point, normal, metal ? 10 : 5); Burst(dust, point, normal, metal ? 1 : 3); }
        public void Blood(Vector3 point, Vector3 dir, int n = 7) { Burst(blood, point, (-dir + Vector3.up * 0.4f).normalized, n); }
        public void Muzzle(Vector3 point, Vector3 dir, bool big)
        {
            flashLight.transform.position = point; flashLight.intensity = big ? 10f : 6f; flashT = 0.055f;
            Burst(muzzleSmoke, point, dir, big ? 5 : 2); Burst(sparks, point, dir, big ? 8 : 4);
        }
        public void Slash(Vector3 point, Vector3 dir) { Burst(dust, point, dir, 3); }

        public void Tracer(Vector3 a, Vector3 b)
        {
            var lr = tracerPool.Count > 0 ? tracerPool.Pop() : NewTracer();
            lr.enabled = true; lr.SetPosition(0, a); lr.SetPosition(1, b);
            tracers.Add((lr, 0.06f));
        }

        LineRenderer NewTracer()
        {
            var go = new GameObject("Tracer"); go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>(); lr.positionCount = 2; lr.startWidth = 0.02f; lr.endWidth = 0.006f; lr.material = tracerMat; lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            return lr;
        }

        void Update()
        {
            if (flashT > 0f) { flashT -= Time.deltaTime; if (flashT <= 0f) flashLight.intensity = 0f; }
            for (int i = tracers.Count - 1; i >= 0; i--)
            {
                var (lr, t) = tracers[i]; t -= Time.deltaTime;
                if (t <= 0f) { lr.enabled = false; tracerPool.Push(lr); tracers.RemoveAt(i); } else tracers[i] = (lr, t);
            }
        }
    }
}
