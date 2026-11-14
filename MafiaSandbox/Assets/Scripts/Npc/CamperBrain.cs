using UnityEngine;
using MafiaSandbox.Core;

namespace MafiaSandbox.Npc
{
    /// <summary>
    /// The people living under the bridge: each has a home spot, a favourite pose (crouch, talk, dance…), maybe a fire to face
    /// or a partner to chat with. Gunfire nearby makes them look up; a shot at them sends them running like anyone else.
    /// </summary>
    public class CamperBrain : NpcBrain
    {
        public Vector3 homeSpot; public string pose = "Idle"; public Vector3? fire; public CamperBrain partner;
        float poseT; string cur;

        protected override void OnSpawned() { SetState("camp"); Stop(); idleClip = pose; cur = pose; poseT = Random.Range(2f, 6f); lookAt = fire ?? (Vector3?)(homeSpot + Vector3.forward); }
        protected override Vector3 RespawnPoint() => homeSpot;
        protected override void OnHit(string zone, Vector3 point) { var p = EscapePoint(point, 25f, true); if (p.HasValue && GoTo(p.Value, runSpeed)) SetState("flee"); }
        protected override void OnNoise(Vector3 pos, float radius, GameObject src, bool melee)
        {
            if (State == "dead" || melee) return; float d = Vector3.Distance(pos, transform.position);
            if (d < 10f) { var p = EscapePoint(pos, 20f, true); if (p.HasValue && GoTo(p.Value, runSpeed)) SetState("flee"); }
            else if (d < 40f && State == "camp") { SetState("curious"); lookAt = pos; idleClip = "Idle"; }
        }

        protected override void Think()
        {
            float dt = 0.2f;
            switch (State)
            {
                case "camp":
                    if (Vector3.Distance(transform.position, homeSpot) > 1f) { if (!agent.hasPath) GoTo(homeSpot, walkSpeed); idleClip = "Idle"; break; }
                    if (partner) { lookAt = partner.transform.position; idleClip = partner.Dead || partner.State != "camp" ? "Idle" : "Talk"; break; }
                    if (fire.HasValue) lookAt = fire;
                    poseT -= dt; if (poseT <= 0f) { poseT = 4f + Random.value * 6f; cur = Random.value < 0.7f ? pose : (Random.value < 0.5f ? "Idle" : "Wave"); }
                    idleClip = cur; break;
                case "curious":
                    if (stateT > 6f) { SetState("camp"); lookAt = fire; } break;
                case "flee":
                    if (Arrived() || stateT > 10f) { Stop(); SetState("hidden"); idleClip = "Crouch_Idle"; } break;
                case "hidden":
                    if (stateT > 20f) { SetState("return"); GoTo(homeSpot, walkSpeed); } break;
                case "return":
                    idleClip = "Idle"; if (Arrived(0.5f) || stateT > 40f) { agent.Warp(homeSpot); OnSpawned(); } break;
            }
        }
    }
}
