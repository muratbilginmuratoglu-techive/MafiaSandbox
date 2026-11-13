using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;

namespace MafiaSandbox.Npc
{
    /// <summary>
    /// Civilians: wander the pedestrian graph, stop and chat, get curious about distant noise, flee gunfire, hide out of sight,
    /// creep back cautiously, limp when shot in the leg. Medics (Doktor_*) walk to the wounded and treat them.
    /// </summary>
    public class CivilianBrain : NpcBrain
    {
        public bool isMedic;
        [Range(0f, 1f)] public float bravery = 0.3f;

        Vector3 threat; float scared; CharacterBody patient; Vector3 noisePoint;

        protected override void OnSpawned() { SetState("wander"); idleClip = "Idle"; scared = 0f; patient = null; PickWanderTarget(); }

        void PickWanderTarget()
        {
            var p = RandomPedNode(transform.position, 70f) + new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f));
            if (!GoTo(p, walkSpeed)) SetState("wait");
        }

        protected override void OnNoise(Vector3 pos, float radius, GameObject src, bool melee)
        {
            if (src == gameObject || State == "dead") return;
            float d = Vector3.Distance(pos, transform.position);
            if (melee && d > 10f) return;
            if (d < radius) { StartFlee(pos); }
            else if (!melee && d < radius * 1.8f && State == "wander" && Random.value < bravery) { noisePoint = pos; SetState("curious"); GoTo(Vector3.Lerp(transform.position, pos, 0.6f), walkSpeed); }
        }

        protected override void OnHit(string zone, Vector3 point) { StartFlee(point); scared = 1f; }

        void StartFlee(Vector3 from)
        {
            threat = from; scared = Mathf.Max(scared, 0.6f);
            if (dummy) return;
            var p = EscapePoint(from, 28f, false);
            if (p.HasValue && GoTo(p.Value, runSpeed)) SetState("flee"); else { SetState("hide"); Stop(); }
            idleClip = "Idle";
        }

        void EnterHide() { Stop(); SetState("hide"); idleClip = "Crouch_Idle"; lookAt = threat; }

        protected override void Think()
        {
            switch (State)
            {
                case "wander":
                    if (Arrived()) { SetState("wait"); stateT = -Random.Range(0.5f, 4f); Stop(); if (Random.value < 0.3f) { lookAt = Player ? Player.transform.position : (Vector3?)null; } }
                    else if (stateT > 40f) PickWanderTarget();
                    break;
                case "wait":
                    if (stateT > 0f) { SetState("wander"); PickWanderTarget(); }
                    break;
                case "curious":
                    if (Arrived() || stateT > 12f) { Stop(); lookAt = noisePoint; SetState("look"); }
                    break;
                case "look":
                    if (stateT > 5f) { SetState("wander"); PickWanderTarget(); }
                    break;
                case "flee":
                    if (Arrived() || stateT > 9f)
                    {
                        var hp = EscapePoint(threat, 14f, true);
                        if (hp.HasValue && stateT < 9f && Vector3.Distance(hp.Value, threat) > 18f && GoTo(hp.Value, runSpeed)) SetState("tohide");
                        else EnterHide();
                    }
                    break;
                case "tohide":
                    if (Arrived() || stateT > 6f) EnterHide();
                    break;
                case "hide":
                    idleClip = "Crouch_Idle"; lookAt = threat;
                    if (stateT > Random.Range(10f, 18f) || (stateT > 4f && Wanted && Wanted.Level == 0 && scared < 0.7f)) { idleClip = "Idle"; SetState("cautious"); GoTo(RandomPedNode(transform.position, 40f), walkSpeed * 0.7f); }
                    break;
                case "cautious":
                    if (Arrived() || stateT > 12f) { SetState("wander"); PickWanderTarget(); }
                    break;
                case "medic_go":
                    if (!patient || (patient.Dead && stateT > 20f)) { SetState("wander"); PickWanderTarget(); break; }
                    if (Vector3.Distance(transform.position, patient.transform.position) < 1.8f) { Stop(); lookAt = patient.transform.position; SetState("medic_treat"); anim.PlayUpper("Give"); }
                    else if (stateT > 25f) { SetState("wander"); PickWanderTarget(); }
                    else if (Arrived()) GoTo(patient.transform.position, walkSpeed * 1.3f);
                    break;
                case "medic_treat":
                    if (stateT > 3f)
                    {
                        if (patient && !patient.Dead) { patient.Treat(); GameManager.I?.Say($"{displayName} treated a wounded pedestrian"); }
                        else GameManager.I?.Say($"{displayName} could do nothing more for the victim");
                        patient = null; SetState("wander"); PickWanderTarget();
                    }
                    break;
            }
            scared = Mathf.Max(0f, scared - 0.2f * 0.05f);

            // medics look for work
            if (isMedic && (State == "wander" || State == "wait") && Time.frameCount % 5 == 0)
            {
                CharacterBody best = null; float bd = 40f * 40f;
                foreach (var b in CharacterBody.All)
                {
                    if (b == body || b.isPlayer || (!b.Dead && b.InjuryLeg == 0 && b.InjuryArm == 0)) continue;
                    float d = (b.transform.position - transform.position).sqrMagnitude; if (d < bd) { bd = d; best = b; }
                }
                if (best && (!best.Dead || best.KilledBy != null)) { patient = best; SetState("medic_go"); GoTo(best.transform.position, walkSpeed * 1.3f); }
            }
        }
    }
}
