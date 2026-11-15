using UnityEngine;

namespace MafiaSandbox.Npc
{
    /// <summary>Jeremy, the only friend who stayed: a scripted NPC driven by the Prologue (walks where told, talks, ignores everything else).</summary>
    public class JeremyBrain : NpcBrain
    {
        protected override void OnSpawned() { SetState("scripted"); walkSpeed = 2.3f; Stop(); }
        protected override void Think() { if (hasGoal && !Done && agent.enabled && agent.isOnNavMesh && !agent.pathPending && !agent.hasPath) GoTo(goal, 2.4f, 8f); }
        protected override void OnHit(string zone, Vector3 point) { }
        protected override void OnNoise(Vector3 pos, float radius, GameObject src, bool melee) { }
        public override void Honk(Vector3 carPos, Vector3 carForward) { }
        Vector3 goal; bool hasGoal;
        public void WalkTo(Vector3 p, float speed = 2.4f) { idleClip = "Idle"; goal = p; hasGoal = true; GoTo(p, speed, 8f); }
        public bool Done { get { if (!hasGoal) return true; Vector3 d = transform.position - goal; d.y = 0f; return d.magnitude < 1.1f; } }
        public void Face(Vector3? p) { lookAt = p; }
        public void Talk(bool on) { idleClip = on ? "Talk" : "Idle"; }
        public void Halt() { hasGoal = false; Stop(); }
    }
}
