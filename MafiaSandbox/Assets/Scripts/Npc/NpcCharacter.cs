using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Player;
using MafiaSandbox.World;

namespace MafiaSandbox.Npc
{
    /// <summary>
    /// Step-2 placeholder brain: pedestrians wander the pedestrian graph edge to edge, flee from gunfire and from being hit,
    /// get hurt / limp / die / respawn. Step 3 replaces this with NavMesh pathing and the full civilian / police / gang state machines;
    /// the body side (CharacterBody, CharacterAnimator) stays as is.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class NpcCharacter : MonoBehaviour
    {
        public RoadGraph graph;
        public float walkSpeed = 2.0f, runSpeed = 5.6f;
        public string displayName = "Citizen";
        public bool dummy;   // debug: stands still (target practice)

        CharacterController cc; CharacterAnimator anim; CharacterBody body;
        int node = -1; Vector3 target; float waitT, stuckT, respawnT, fleeT; Vector3 fleeFrom; Vector3 lastPos; float vy;
        enum State { Wander, Wait, Flee, Dead }
        State state;

        void Awake()
        {
            cc = GetComponent<CharacterController>(); anim = GetComponent<CharacterAnimator>(); body = GetComponent<CharacterBody>();
        }

        void OnEnable()
        {
            WorldEvents.OnNoise += OnNoise;
            if (body) { body.OnHit += OnHit; body.OnDeath += OnDeath; }
        }
        void OnDisable()
        {
            WorldEvents.OnNoise -= OnNoise;
            if (body) { body.OnHit -= OnHit; body.OnDeath -= OnDeath; }
        }

        void Start()
        {
            if (!graph) graph = FindAnyObjectByType<RoadGraph>();
            node = graph ? graph.Nearest(graph.pedNodes, transform.position) : -1;
            PickNext(); waitT = Random.Range(0f, 2f); lastPos = transform.position;
        }

        void PickNext()
        {
            if (graph == null || node < 0 || graph.pedNodes.Length == 0) { target = transform.position; return; }
            var n = graph.pedNodes[Mathf.Clamp(node, 0, graph.pedNodes.Length - 1)];
            int next = n.adj != null && n.adj.Length > 0 ? n.adj[Random.Range(0, n.adj.Length)] : -1;
            node = next >= 0 && next < graph.pedNodes.Length ? next : Random.Range(0, graph.pedNodes.Length);
            var p = graph.pedNodes[node].pos;
            target = p + new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f));
            state = State.Wander;
        }

        void OnNoise(Vector3 pos, float radius, GameObject src, bool melee)
        {
            if (state == State.Dead || src == gameObject) return;
            if ((pos - transform.position).sqrMagnitude < radius * radius) StartFlee(pos);
        }
        void OnHit(CharacterBody b, string zone, Vector3 point) { if (!b.Dead) StartFlee(point); }
        void OnDeath(CharacterBody b, string by) { state = State.Dead; respawnT = 12f; WorldEvents.OnCharacterDied?.Invoke(b, by); }

        void StartFlee(Vector3 from) { if (state == State.Dead) return; state = State.Flee; fleeFrom = from; fleeT = Random.Range(5f, 8f); }

        void Update()
        {
            float dt = Time.deltaTime;
            if (state == State.Dead)
            {
                respawnT -= dt;
                if (respawnT <= 0f && graph && graph.pedNodes.Length > 0)
                {
                    node = Random.Range(0, graph.pedNodes.Length);
                    var p = graph.pedNodes[node].pos;
                    cc.enabled = false; body.Respawn(p + Vector3.up * 0.1f, Random.Range(0f, 360f)); cc.enabled = true;
                    PickNext(); waitT = 1f;
                }
                return;
            }
            if (!cc.enabled) return;
            if (dummy) { Move(Vector3.zero, dt); anim.Play("Idle"); return; }
            if (body && body.Stun > 0f) { Move(Vector3.zero, dt); anim.Play(anim.Current != null && anim.Current.StartsWith("Hit_") ? anim.Current : "Idle"); return; }

            Vector3 dir = Vector3.zero; float speed = 0f; string clip = "Idle";
            if (state == State.Flee)
            {
                fleeT -= dt;
                Vector3 away = transform.position - fleeFrom; away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0f;
                dir = away.normalized; speed = runSpeed; clip = "Run";
                // steer around what's ahead
                if (Physics.SphereCast(transform.position + Vector3.up, 0.3f, dir, out var h, 2.5f, ~((1 << CharacterBody.HitboxLayer) | (1 << CharacterBody.CharactersLayer)), QueryTriggerInteraction.Ignore))
                    dir = Vector3.Cross(Vector3.up, h.normal).normalized * (Vector3.Dot(Vector3.Cross(Vector3.up, h.normal), dir) >= 0 ? 1f : -1f);
                if (fleeT <= 0f) { node = graph ? graph.Nearest(graph.pedNodes, transform.position) : node; state = State.Wait; waitT = Random.Range(2f, 5f); }
            }
            else if (state == State.Wait)
            {
                waitT -= dt; if (waitT <= 0f) PickNext();
            }
            else
            {
                Vector3 to = target - transform.position; to.y = 0f;
                if (to.magnitude < 0.6f) { state = State.Wait; waitT = Random.Range(0.5f, 4f); }
                else { dir = to.normalized; speed = walkSpeed; clip = "Walk"; }
                // stuck → re-pick
                if ((transform.position - lastPos).sqrMagnitude < 0.0004f && speed > 0f) { stuckT += dt; if (stuckT > 1.5f) { stuckT = 0f; node = graph ? graph.Nearest(graph.pedNodes, transform.position) : node; PickNext(); } }
                else stuckT = 0f;
            }
            if (body && body.InjuryLeg > 0 && speed > 0f) { speed *= 0.55f; }
            lastPos = transform.position;
            Move(dir * speed, dt);
            if (dir.sqrMagnitude > 0.001f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 540f * dt);
            anim.Play(clip, body && body.InjuryLeg > 0 && clip != "Idle" ? 0.7f : 1f);
        }

        void Move(Vector3 planar, float dt)
        {
            if (cc.isGrounded && vy < 0f) vy = -2f; vy += -18f * dt;
            cc.Move(new Vector3(planar.x, vy, planar.z) * dt);
        }
    }
}
