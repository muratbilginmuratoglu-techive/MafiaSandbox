using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Vehicles;

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

        float reactT, bodyMemo, personality = -1f;
        protected override void OnSpawned() { if (personality < 0f) { personality = Random.value; walkSpeed = Random.Range(1.5f, 2.4f); } SetState("wander"); idleClip = "Idle"; scared = 0f; patient = null; PickWanderTarget(); }

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
        public override void Honk(Vector3 carPos, Vector3 carForward) { if (State == "dead" || dummy) return; if (State == "flee" || State == "hide" || State == "tohide") base.Honk(carPos, carForward); else StartFlee(carPos); }

        void StartFlee(Vector3 from)
        {
            threat = from; scared = Mathf.Max(scared, 0.6f);
            if (dummy) return;
            var p = EscapePoint(from, 28f, false);
            if (p.HasValue && GoTo(p.Value, runSpeed)) SetState("flee"); else { SetState("hide"); Stop(); }
            idleClip = "Idle";
        }

        float chatLen = 6f;
        bool Calm => State == "wander" || State == "wait" || State == "cautious" || State == "look" || State == "chat";

        bool PlayerAimingAtMe(float range)
        {
            var pc = Player ? Player.GetComponent<PlayerCombat>() : null; if (!pc || !pc.Aiming || pc.Weapon.melee) return false;
            Vector3 to = transform.position - Player.transform.position; to.y = 0f; float d = to.magnitude; if (d > range) return false;
            return Vector3.Dot(Player.transform.forward, to / Mathf.Max(0.01f, d)) > 0.8f;
        }

        /// <summary>Everyday reactions, a few times a second: step out of a car's path, stare at / flee from corpses, react to a gun pointed at them, stop for a chat.</summary>
        void React()
        {
            if (dummy || isMedic && State.StartsWith("medic")) return;
            reactT -= 0.05f; bodyMemo -= 0.05f; if (reactT > 0f) return; reactT = Random.Range(0.25f, 0.4f);
            var pos = transform.position;
            // 1. cars: get out of the way (and glance back at reckless drivers)
            if (State != "flee" && State != "tohide" && State != "hide" && State != "dodge")
                foreach (var v in VehicleController.All)
                {
                    if (!v || Mathf.Abs(v.Speed) < 2.5f) continue; Vector3 rel = pos - v.transform.position; rel.y = 0f; float d = rel.magnitude; if (d > 11f) continue;
                    Vector3 dir = v.transform.forward * Mathf.Sign(v.Speed); if (Vector3.Dot(dir, rel) < 0f) continue;       // already behind it
                    float lat = Vector3.Dot(v.transform.right, rel); if (Mathf.Abs(lat) > 2.6f) continue;                     // not in its lane
                    Vector3 side = v.transform.right * (lat >= 0f ? 1f : -1f);
                    bool fast = Mathf.Abs(v.Speed) > 9f;
                    if (GoTo(pos + side * (fast ? 3.6f : 2.4f) + dir * 0.5f, fast ? runSpeed : walkSpeed * 1.5f)) { SetState("dodge"); scared = Mathf.Max(scared, fast ? 0.4f : 0f); if (fast && Random.value < 0.5f) { lookAt = null; } }
                    return;
                }
            // 2. dead bodies nearby
            if (bodyMemo <= 0f && Calm)
                foreach (var b in CharacterBody.All)
                {
                    if (!b || !b.Dead || b == body || (b.transform.position - pos).sqrMagnitude > 12f * 12f) continue;
                    if (!Perception.Clear(Eye, b.ChestPos)) continue;
                    bodyMemo = 25f; threat = b.transform.position;
                    if (personality < 0.55f) StartFlee(b.transform.position);
                    else { Stop(); lookAt = b.transform.position; SetState("watch"); }
                    GameManager.I?.Say($"{displayName} is shaken by the body");
                    return;
                }
            // 3. gun pointed at me
            if (Calm && Player && PlayerAimingAtMe(12f) && Perception.Clear(Eye, Player.transform.position + Vector3.up * 1.4f))
            {
                if (personality < 0.4f || scared > 0.5f) StartFlee(Player.transform.position);
                else { Stop(); lookAt = Player.transform.position; idleClip = "Idle"; SetState("handsup"); GameManager.I?.Say($"{displayName} backs away with hands up"); }
                return;
            }
            // 4. a chat between two neighbours
            if ((State == "wait" || State == "wander") && personality > 0.35f && Random.value < 0.06f)
                foreach (var o in FindNearbyCivs())
                {
                    if (!o || o == this || !(o.State == "wait" || o.State == "wander") || (o.transform.position - pos).sqrMagnitude > 3.5f * 3.5f) continue;
                    chatLen = Random.Range(5f, 11f); Stop(); lookAt = o.transform.position; SetState("chat"); o.StartChat(transform.position, chatLen); break;
                }
        }
        public void StartChat(Vector3 with, float len) { chatLen = len; Stop(); lookAt = with; SetState("chat"); }
        static CivilianBrain[] civCache; static float civCacheT;
        static CivilianBrain[] FindNearbyCivs() { if (civCache == null || Time.time - civCacheT > 2f) { civCache = FindObjectsByType<CivilianBrain>(FindObjectsSortMode.None); civCacheT = Time.time; } return civCache; }

        void EnterHide() { Stop(); SetState("hide"); idleClip = "Crouch_Idle"; lookAt = threat; }

        protected override void Think()
        {
            switch (State)
            {
                case "dodge":
                    if (Arrived(0.4f) || stateT > 1.6f) { SetState("wait"); stateT = -Random.Range(0.8f, 1.8f); Stop(); }
                    break;
                case "chat":
                    if (stateT > chatLen) { SetState("wander"); PickWanderTarget(); }
                    break;
                case "watch":   // brave ones stop and stare at trouble
                    if (stateT > 7f) { SetState("wander"); PickWanderTarget(); }
                    break;
                case "handsup":
                    if (stateT > 5f || !PlayerAimingAtMe(14f)) { idleClip = "Idle"; SetState("cautious"); GoTo(RandomPedNode(transform.position, 40f), walkSpeed * 0.7f); }
                    break;
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
            React();

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
