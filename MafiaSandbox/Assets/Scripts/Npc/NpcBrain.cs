using UnityEngine;
using UnityEngine.AI;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Player;
using MafiaSandbox.World;

namespace MafiaSandbox.Npc
{
    /// <summary>
    /// Common NPC machinery: NavMeshAgent locomotion + animation, perception helpers, noise events, stun/injury handling,
    /// death → ragdoll → respawn, and an optional sidearm (police). Subclasses implement Think() as a state machine
    /// that runs 5× per second.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public abstract class NpcBrain : MonoBehaviour
    {
        public string displayName = "Citizen";
        public float walkSpeed = 2.0f, runSpeed = 5.6f;
        public RoadGraph graph;
        public bool dummy;                       // debug: stand still
        public string State { get; protected set; } = "init";

        float offMeshT;
        protected NavMeshAgent agent; protected CharacterBody body; protected CharacterAnimator anim; protected CapsuleCollider capsule;
        protected float stateT, thinkT, respawnT;
        protected Vector3 spawnPos; protected float spawnYaw;
        protected Vector3? lookAt;              // face this point while standing
        protected GameObject gun; protected Transform gunMuzzle; protected float fireCd;
        protected string idleClip = "Idle";

        protected static PlayerController Player => GameManager.I ? GameManager.I.player : null;
        protected static CharacterBody PlayerBody => GameManager.I && GameManager.I.player ? GameManager.I.player.GetComponent<CharacterBody>() : null;
        protected static WantedSystem Wanted => WantedSystem.I;
        public bool Dead => body && body.Dead;
        public CharacterBody Body => body;
        protected Vector3 Eye => transform.position + Vector3.up * 1.6f;
        protected float DistToPlayer => Player ? Vector3.Distance(transform.position, Player.transform.position) : 999f;
        protected bool PlayerInVehicle { get { if (!Player) return false; var vi = Player.GetComponent<Vehicles.VehicleInteraction>(); return vi != null && vi.Current != null; } }

        protected virtual void Awake()
        {
            agent = GetComponent<NavMeshAgent>(); body = GetComponent<CharacterBody>(); anim = GetComponent<CharacterAnimator>(); capsule = GetComponent<CapsuleCollider>();
            agent.updateRotation = false; agent.radius = 0.35f; agent.height = 1.8f; agent.angularSpeed = 720f; agent.acceleration = 14f; agent.stoppingDistance = 0.4f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance; agent.autoBraking = true; agent.autoRepath = true;
            spawnPos = transform.position; spawnYaw = transform.eulerAngles.y;
        }

        protected virtual void OnEnable()
        {
            WorldEvents.OnNoise += OnNoise;
            if (body) { body.OnHit += OnHitInternal; body.OnDeath += OnDeathInternal; }
        }
        protected virtual void OnDisable()
        {
            WorldEvents.OnNoise -= OnNoise;
            if (body) { body.OnHit -= OnHitInternal; body.OnDeath -= OnDeathInternal; }
        }

        protected virtual void Start()
        {
            if (!graph) graph = FindAnyObjectByType<RoadGraph>();
            if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var h, 4f, NavMesh.AllAreas)) agent.Warp(h.position);
            thinkT = Random.Range(0f, 0.2f);
            OnSpawned();
        }

        protected abstract void OnSpawned();
        protected abstract void Think();
        protected virtual void OnNoise(Vector3 pos, float radius, GameObject src, bool melee) { }
        protected virtual void OnHit(string zone, Vector3 point) { }
        /// <summary>A car is honking at us from `carPos`: step off the road. Default: hop 4 m to the side away from the car's path.</summary>
        public virtual void Honk(Vector3 carPos, Vector3 carForward)
        {
            if (dummy || State == "dead" || !agent || !agent.enabled) return;
            Vector3 side = Vector3.Cross(Vector3.up, carForward).normalized; Vector3 rel = transform.position - carPos;
            if (Vector3.Dot(rel, side) < 0f) side = -side;
            GoTo(transform.position + side * 4f + carForward * 1.5f, runSpeed);
        }
        protected virtual void OnDied(string by) { }
        protected virtual Vector3 RespawnPoint() => spawnPos;

        void OnHitInternal(CharacterBody b, string zone, Vector3 point) { if (!b.Dead) OnHit(zone, point); }
        void OnDeathInternal(CharacterBody b, string by)
        {
            State = "dead"; respawnT = 14f;
            if (agent.enabled) { agent.isStopped = true; agent.enabled = false; }
            if (capsule) capsule.enabled = false;
            ShowGun(false);
            WorldEvents.OnCharacterDied?.Invoke(b, by);
            OnDied(by);
        }

        protected void SetState(string s) { if (State != s) { State = s; stateT = 0f; } }

        protected virtual void Update()
        {
            float dt = Time.deltaTime; stateT += dt; fireCd -= dt;
            if (body && body.Dead)
            {
                respawnT -= dt;
                if (respawnT <= 0f) Respawn();
                return;
            }
            if (agent.enabled && !agent.isOnNavMesh)
            {
                // a carving car (or a ragdoll pile) ate the navmesh under us: hop to the nearest walkable spot instead of freezing
                offMeshT += dt;
                if (offMeshT > 0.3f && NavMesh.SamplePosition(transform.position, out var nh, 3f, NavMesh.AllAreas)) { agent.Warp(nh.position); offMeshT = 0f; }
            }
            else offMeshT = 0f;
            if (!agent.enabled || !agent.isOnNavMesh) return;
            if (dummy) { agent.isStopped = true; anim.Play(idleClip); return; }
            if (body && body.Stun > 0f) { agent.isStopped = true; return; }
            thinkT -= dt; if (thinkT <= 0f) { thinkT = 0.2f; Think(); }
            Animate(dt);
        }

        protected virtual void Animate(float dt)
        {
            Vector3 v = agent.velocity; v.y = 0f; float speed = v.magnitude;
            float limp = body && body.InjuryLeg > 0 ? 0.7f : 1f;
            if (speed > 0.2f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(v), 720f * dt);
                anim.Play(speed > 3.3f ? "Run" : "Walk", limp * (speed > 3.3f ? Mathf.Clamp(speed / 5.6f, 0.8f, 1.3f) : Mathf.Clamp(speed / 2.0f, 0.7f, 1.4f)));
            }
            else
            {
                if (lookAt.HasValue) { Vector3 d = lookAt.Value - transform.position; d.y = 0f; if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 360f * dt); }
                anim.Play(idleClip);
            }
        }

        // ------------------------------------------------------------ movement helpers
        protected bool GoTo(Vector3 p, float speed, float sampleRadius = 4f)
        {
            if (!agent.enabled || !agent.isOnNavMesh) return false;
            if (!NavMesh.SamplePosition(p, out var h, sampleRadius, NavMesh.AllAreas)) return false;
            agent.speed = speed * (body && body.InjuryLeg > 0 ? 0.55f : 1f); agent.isStopped = false; lookAt = null;
            return agent.SetDestination(h.position);
        }
        protected void Stop() { if (agent.enabled && agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); } }
        protected bool Arrived(float d = 0.7f) => !agent.pathPending && (!agent.hasPath || agent.remainingDistance <= Mathf.Max(d, agent.stoppingDistance + 0.05f));
        protected bool Moving => agent.enabled && agent.velocity.sqrMagnitude > 0.04f;

        protected Vector3 RandomPedNode(Vector3 near, float maxDist)
        {
            if (!graph || graph.pedNodes.Length == 0) return near + Random.insideUnitSphere * 10f;
            for (int i = 0; i < 12; i++) { var n = graph.pedNodes[Random.Range(0, graph.pedNodes.Length)]; if ((n.pos - near).sqrMagnitude < maxDist * maxDist) return n.pos; }
            return graph.pedNodes[Random.Range(0, graph.pedNodes.Length)].pos;
        }

        /// <summary>A reachable point roughly `dist` metres away from `threat`, preferring directions away from it.</summary>
        protected Vector3? EscapePoint(Vector3 threat, float dist, bool outOfSight)
        {
            Vector3 away = transform.position - threat; away.y = 0f; if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.Normalize();
            Vector3? best = null; float bestScore = -1f;
            for (int i = 0; i < 10; i++)
            {
                float ang = Random.Range(-100f, 100f); Vector3 dir = Quaternion.Euler(0f, ang, 0f) * away;
                Vector3 cand = transform.position + dir * dist * Random.Range(0.7f, 1.2f);
                if (!NavMesh.SamplePosition(cand, out var h, 5f, NavMesh.AllAreas)) continue;
                float score = Vector3.Distance(h.position, threat) + (outOfSight && !Perception.Clear(threat + Vector3.up * 1.6f, h.position + Vector3.up * 1.2f) ? 25f : 0f);
                if (score > bestScore) { bestScore = score; best = h.position; }
            }
            return best;
        }

        // ------------------------------------------------------------ perception helpers
        public bool SeesPlayer(float range, float fov = 120f) => CanSeePlayer(range, fov);
        protected bool CanSeePlayer(float range, float fov = 120f) => PlayerBody && !PlayerBody.Dead && Perception.CanSee(Eye, transform.forward, PlayerBody, range, fov);
        protected bool CanSee(CharacterBody b, float range, float fov = 120f) => b && Perception.CanSee(Eye, transform.forward, b, range, fov);

        // ------------------------------------------------------------ respawn
        protected virtual void Respawn()
        {
            Vector3 p = RespawnPoint();
            if (NavMesh.SamplePosition(p, out var h, 6f, NavMesh.AllAreas)) p = h.position;
            body.Respawn(p + Vector3.up * 0.05f, spawnYaw);
            if (capsule) capsule.enabled = true;
            agent.enabled = true; agent.Warp(p); lookAt = null;
            OnSpawned();
        }

        // ------------------------------------------------------------ weapons (police sidearm, gang bats)
        protected void EquipGun(WeaponKind k, bool alwaysVisible)
        {
            if (gun) Destroy(gun);
            var hand = body ? body.RightHand : null;
            gun = WeaponModels.Build(k); gun.transform.SetParent(hand ? hand : transform, false); gun.transform.localPosition = new Vector3(0f, 0.06f, 0f) + gun.transform.localPosition;
            gunMuzzle = gun.transform.Find("Muzzle"); gun.SetActive(alwaysVisible);
        }
        protected void ShowGun(bool on) { if (gun) gun.SetActive(on); }

        /// <summary>Fire the sidearm at a body: hit chance scales with accuracy and distance; misses spark near the target.</summary>
        protected void ShootAt(CharacterBody target, float accuracy, string tag)
        {
            if (!target || fireCd > 0f) return;
            fireCd = 0.8f + Random.Range(0f, 0.3f);
            anim.PlayUpper("Pistol_Fire");
            Vector3 mz = gunMuzzle ? gunMuzzle.position : Eye;
            Vector3 aim = target.ChestPos + Random.insideUnitSphere * 0.25f; Vector3 dir = (aim - mz).normalized;
            CombatFx.I?.Muzzle(mz, dir, false); Sfx.At("pistol", mz, 0.9f);
            float dist = Vector3.Distance(mz, aim);
            bool hit = Random.value < accuracy * Mathf.Clamp01(1.25f - dist / 40f) && Perception.Clear(mz, aim);
            if (hit)
            {
                string zone = Random.value < 0.12f ? "Hit_Head" : Random.value < 0.6f ? "torso" : (Random.value < 0.5f ? "Hit_LegLeft" : "Hit_ArmRight");
                CombatFx.I?.Tracer(mz, aim); CombatFx.I?.Blood(aim, dir, 5);
                target.Hit(zone, aim, dir, 1, false, 1f, tag, false);
            }
            else
            {
                Vector3 end = aim + Random.insideUnitSphere * 1.2f + dir * 3f;
                if (Physics.Raycast(mz, (end - mz).normalized, out var h, 60f, ~((1 << CharacterBody.HitboxLayer) | (1 << CharacterBody.CharactersLayer)), QueryTriggerInteraction.Ignore)) { end = h.point; CombatFx.I?.Impact(h.point, h.normal, false); }
                CombatFx.I?.Tracer(mz, end);
            }
            WorldEvents.Shot(transform.position, 28f, gameObject);
        }
    }
}
