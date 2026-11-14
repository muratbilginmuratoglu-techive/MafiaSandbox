using UnityEngine;
using UnityEngine.AI;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;

namespace MafiaSandbox.World
{
    /// <summary>
    /// Procedural low-poly dogs and cats (boxes, like the prototype). They wander near their home spot (off the roads),
    /// cats run from the player, dogs sometimes tag along, everything panics at gunfire. Shootable (one hit), cars knock them over.
    /// </summary>
    public class Animal : MonoBehaviour
    {
        public enum Kind { Dog, Cat }
        public Kind kind; public Vector3 home; public float radius = 8f;
        public bool Dead { get; private set; }

        static readonly Color[][] DogColors = { new[] { C(0x8a5a2b), C(0x5a3a1a) }, new[] { C(0x2a2a2c), C(0x5a5a5c) }, new[] { C(0xe8e0d0), C(0xc9b89a) }, new[] { C(0xc78f4e), C(0x8a5a2b) }, new[] { C(0x4b4b4f), C(0xe8e0d0) } };
        static readonly Color[][] CatColors = { new[] { C(0xd98a3a), C(0xf2c48a) }, new[] { C(0x7a7a80), C(0xa9a9b0) }, new[] { C(0x1e1e22), C(0x3a3a40) }, new[] { C(0xf0ece4), C(0xd9d2c4) }, new[] { C(0xb8762e), C(0x2a2a2c) } };
        static Color C(int hex) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);
        static Material matCache;

        float len, wid, hgt, legH, headS, walk, run;
        Transform body, head, tail; Transform[] legPiv = new Transform[4]; int[] legSz = new int[4];
        NavMeshAgent agent; float phase, waitT, sitT, soundT; Vector3? target; float fleeT; Vector3 fleeFrom; bool follow; float deathT; int deathSide = 1;

        public static Animal Create(Kind kind, Vector3 pos, Vector3 home, float radius)
        {
            var go = new GameObject(kind == Kind.Dog ? "Dog" : "Cat"); go.transform.position = pos;
            var a = go.AddComponent<Animal>(); a.kind = kind; a.home = home; a.radius = radius; a.Build(); return a;
        }

        void Build()
        {
            bool dog = kind == Kind.Dog;
            len = dog ? 0.58f : 0.42f; wid = dog ? 0.22f : 0.15f; hgt = dog ? 0.26f : 0.17f; legH = dog ? 0.3f : 0.2f; headS = dog ? 0.2f : 0.15f; walk = dog ? 1.6f : 1.2f; run = dog ? 5.5f : 5f;
            var cols = (dog ? DogColors : CatColors)[Random.Range(0, 5)]; var m1 = Mat(cols[0]); var m2 = Mat(cols[1]); var dark = Mat(C(0x1a1a1c));
            Transform Box(Transform parent, float w, float h, float d, float x, float y, float z, Material m)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(q.GetComponent<Collider>()); q.transform.SetParent(parent, false);
                q.transform.localPosition = new Vector3(x, y, z); q.transform.localScale = new Vector3(w, h, d); q.GetComponent<Renderer>().sharedMaterial = m; return q.transform;
            }
            body = new GameObject("Body").transform; body.SetParent(transform, false); body.localPosition = new Vector3(0f, legH + hgt / 2f, 0f);
            Box(body, wid, hgt, len, 0f, 0f, 0f, m1); Box(body, wid * 0.7f, hgt * 0.5f, len * 0.6f, 0f, -hgt * 0.3f, 0.02f, m2);
            head = new GameObject("Head").transform; head.SetParent(body, false); head.localPosition = new Vector3(0f, hgt * 0.45f, len / 2f + 0.02f);
            Box(head, headS, headS * 0.9f, headS, 0f, 0f, headS * 0.3f, m1); Box(head, headS * 0.55f, headS * 0.45f, headS * 0.55f, 0f, -headS * 0.15f, headS * 0.8f, m2); Box(head, headS * 0.25f, headS * 0.18f, headS * 0.15f, 0f, -headS * 0.05f, headS * 1.1f, dark);
            foreach (int sx in new[] { -1, 1 })
            {
                Box(head, 0.03f, 0.03f, 0.01f, sx * headS * 0.25f, headS * 0.12f, headS * 0.8f, dark);
                if (dog) Box(head, headS * 0.25f, headS * 0.6f, headS * 0.12f, sx * headS * 0.55f, -headS * 0.05f, headS * 0.1f, m2);
                else { var e = Box(head, headS * 0.3f, headS * 0.4f, headS * 0.1f, sx * headS * 0.32f, headS * 0.6f, headS * 0.1f, m1); e.localRotation = Quaternion.Euler(0f, 0f, -sx * 20f); }
            }
            float lw = wid * 0.3f; int i = 0;
            foreach (var (sx, sz) in new[] { (-1, 1), (1, 1), (-1, -1), (1, -1) })
            {
                var piv = new GameObject("Leg").transform; piv.SetParent(body, false); piv.localPosition = new Vector3(sx * (wid / 2f - lw / 2f), -hgt / 2f + 0.02f, sz * (len / 2f - lw * 0.9f));
                Box(piv, lw, legH + 0.02f, lw, 0f, -legH / 2f, 0f, sz > 0 ? m1 : m2); legPiv[i] = piv; legSz[i] = sz * sx; i++;
            }
            tail = new GameObject("Tail").transform; tail.SetParent(body, false); tail.localPosition = new Vector3(0f, hgt * 0.35f, -len / 2f); tail.localRotation = Quaternion.Euler(dog ? -52f : -23f, 0f, 0f);
            Box(tail, lw * 0.7f, lw * 0.7f, len * 0.45f, 0f, 0f, -len * 0.2f, m2);
            // hit / bump volume
            int hb = LayerMask.NameToLayer("Hitbox"); if (hb >= 0) gameObject.layer = hb;
            var col = gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(0f, (legH + hgt) / 2f, 0f); col.size = new Vector3(wid * 1.6f, legH + hgt, len * 1.1f);
            var rb = gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true;
            agent = gameObject.AddComponent<NavMeshAgent>(); agent.radius = 0.25f; agent.height = legH + hgt; agent.speed = walk; agent.acceleration = 12f; agent.angularSpeed = 520f; agent.stoppingDistance = 0.2f; agent.autoBraking = true; agent.avoidancePriority = 70;
            if (NavMesh.SamplePosition(transform.position, out var nh, 3f, NavMesh.AllAreas)) agent.Warp(nh.position);
            phase = Random.value * 6f; waitT = Random.value * 3f;
            WorldEvents.OnNoise += OnNoise;
        }
        void OnDestroy() { WorldEvents.OnNoise -= OnNoise; }

        static Material Mat(Color c)
        {
            if (!matCache) { var sh = Shader.Find("Standard"); if (!sh) sh = Shader.Find("Legacy Shaders/Diffuse"); if (!sh) sh = Shader.Find("Sprites/Default"); matCache = new Material(sh); if (matCache.HasProperty("_Glossiness")) matCache.SetFloat("_Glossiness", 0.1f); }
            var m = new Material(matCache); m.color = c; return m;
        }

        void OnNoise(Vector3 pos, float radius2, GameObject src, bool melee)
        {
            if (Dead || melee) return; if ((pos - transform.position).sqrMagnitude < 30f * 30f) Panic(pos, 5f + Random.value * 3f);
        }
        public void Panic(Vector3 from, float dur) { if (Dead) return; fleeFrom = from; fleeT = dur; target = null; follow = false; sitT = 0f; }

        /// <summary>Shot or run over.</summary>
        public void Hit(Vector3 point, Vector3 dir)
        {
            if (Dead) return; Dead = true; deathT = 0f; deathSide = Vector3.Dot(dir, transform.right) > 0f ? -1 : 1;
            if (agent && agent.enabled) agent.enabled = false;
            CombatFx.I?.Blood(point, dir, 5); Sfx.At(kind == Kind.Dog ? "whine" : "meow", transform.position, 0.8f);
            GameManager.I?.Say($"{(kind == Kind.Dog ? "A dog" : "A cat")} was killed");
            foreach (var l in legPiv) l.localRotation = Quaternion.Euler(30f, 0f, 0f); head.localRotation = Quaternion.Euler(17f, 0f, 0f);
        }

        bool OnRoad(Vector3 p)
        {
            var tm = Vehicles.TrafficManager.I; if (!tm || tm.Nodes == null) return false;
            var nodes = tm.Nodes;
            for (int i = 0; i < nodes.Length; i++) foreach (int j in nodes[i].adj ?? new int[0]) if (j > i)
            {
                Vector3 a = nodes[i].pos, b = nodes[j].pos; Vector3 ab = b - a; ab.y = 0f; Vector3 ap = p - a; ap.y = 0f;
                float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(0.01f, ab.sqrMagnitude)); if ((ap - ab * t).sqrMagnitude < 7.4f * 7.4f) return true;
            }
            return false;
        }

        Vector3? PickTarget()
        {
            for (int k = 0; k < 10; k++)
            {
                float a = Random.value * Mathf.PI * 2f, r = Random.value * radius; var p = home + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (OnRoad(p)) continue;
                if (NavMesh.SamplePosition(p, out var nh, 1.5f, NavMesh.AllAreas)) return nh.position;
            }
            return null;
        }

        void Update()
        {
            float dt = Time.deltaTime; var gm = GameManager.I; var player = gm && gm.player ? gm.player.transform : null;
            if (Dead)
            {
                if (deathT < 1f) { deathT += dt * 3f; float t = Mathf.Min(1f, deathT); body.localRotation = Quaternion.Euler(0f, 0f, deathSide * t * 90f); body.localPosition = new Vector3(0f, Mathf.Lerp(legH + hgt / 2f, wid / 2f, t), 0f); }
                return;
            }
            if (!agent || !agent.enabled || !agent.isOnNavMesh) return;
            bool driving = gm && gm.player && gm.player.GetComponent<Vehicles.VehicleInteraction>()?.Current;
            float dPlayer = player ? Vector3.Distance(player.position, transform.position) : 999f;
            bool moving = false; float speed = walk;
            if (fleeT > 0f)
            {
                fleeT -= dt; Vector3 away = transform.position - fleeFrom; away.y = 0f; if (away.sqrMagnitude < 0.01f) away = Vector3.right; away.Normalize();
                away = Quaternion.Euler(0f, Mathf.Sin(fleeT * 4f + transform.position.x) * 15f, 0f) * away;
                if (!agent.hasPath || agent.remainingDistance < 1f) { if (NavMesh.SamplePosition(transform.position + away * 6f, out var nh, 3f, NavMesh.AllAreas)) agent.SetDestination(nh.position); }
                speed = run; moving = true;
            }
            else if (kind == Kind.Cat && player && !driving && dPlayer < 2.2f)
            {
                Vector3 away = transform.position - player.position; away.y = 0f; away.Normalize();
                if (NavMesh.SamplePosition(transform.position + away * 4f, out var nh, 2f, NavMesh.AllAreas)) agent.SetDestination(nh.position); speed = run * 0.8f; moving = true;
            }
            else if (kind == Kind.Dog && follow && player && !driving)
            {
                if (dPlayer > 14f) follow = false;
                else if (dPlayer > 2.4f) { agent.SetDestination(player.position); speed = dPlayer > 6f ? run * 0.7f : walk * 1.5f; moving = true; }
                else { agent.ResetPath(); }
            }
            else if (waitT > 0f) { waitT -= dt; if (sitT > 0f) sitT -= dt; }
            else
            {
                if (target == null) { target = PickTarget(); if (target == null) waitT = 1f; else agent.SetDestination(target.Value); }
                if (target != null) { if (!agent.pathPending && agent.remainingDistance < 0.35f) { target = null; waitT = 1f + Random.value * 5f; if (Random.value < 0.3f) sitT = waitT; } else moving = true; }
            }
            if (kind == Kind.Dog && !follow && player && !driving && dPlayer < 5f && Random.value < dt * 0.05f) follow = true;
            agent.speed = speed; if (!moving) agent.ResetPath();
            moving = agent.velocity.sqrMagnitude > 0.05f;
            soundT -= dt;
            if (soundT <= 0f && Camera.main && (Camera.main.transform.position - transform.position).sqrMagnitude < 900f && Random.value < (fleeT > 0f ? 0.5f : 0.08f)) { soundT = 2f; Sfx.At(kind == Kind.Dog ? "bark" : "meow", transform.position, 0.7f); }
            else if (soundT <= 0f) soundT = 1f;
            // procedural gait
            float v = agent.velocity.magnitude; bool fast = v > 3f;
            phase += dt * (moving ? (fast ? 16f : 9f) : 2f); float amp = moving ? (fast ? 52f : 32f) : 0f;
            for (int i = 0; i < 4; i++) legPiv[i].localRotation = Quaternion.Euler(Mathf.Sin(phase + (legSz[i] > 0 ? 0f : Mathf.PI)) * amp, 0f, 0f);
            if (sitT > 0f) { legPiv[2].localRotation = Quaternion.Euler(-75f, 0f, 0f); legPiv[3].localRotation = Quaternion.Euler(-75f, 0f, 0f); }
            body.localPosition = new Vector3(0f, legH + hgt / 2f + (moving ? Mathf.Abs(Mathf.Sin(phase)) * 0.03f * (fast ? 2f : 1f) : 0f), 0f);
            body.localRotation = Quaternion.Euler(sitT > 0f ? -20f : (moving && fast ? -4.5f : 0f), 0f, 0f);
            tail.localRotation = Quaternion.Euler(kind == Kind.Dog ? -52f : -23f, Mathf.Sin(phase * (kind == Kind.Dog ? 1.6f : 0.5f)) * (kind == Kind.Dog ? 34f : 17f), 0f);
            head.localRotation = Quaternion.Euler(moving ? 0f : Mathf.Sin(phase * 0.4f) * 11f + (waitT > 0f && sitT <= 0f ? 14f : 0f), Mathf.Sin(phase * 0.23f) * 14f, 0f);
        }
    }
}
