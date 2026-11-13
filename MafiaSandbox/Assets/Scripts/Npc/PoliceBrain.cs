using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;

namespace MafiaSandbox.Npc
{
    /// <summary>
    /// Police: patrol around the station, investigate gunfire, chase the player when wanted, arrest on contact,
    /// open fire if shot at or if the chase drags on, search the last known position, give up when the trail goes cold.
    /// Mirrors the prototype's rules the player asked for: chase first, shoot only if the player shoots or after a long chase,
    /// arrest when caught, lose the player when they break line of sight for ~20 s.
    /// </summary>
    public class PoliceBrain : NpcBrain
    {
        public Vector3 stationPos;
        public float sightRange = 38f;
        public float chaseToShootSeconds = 20f;
        public float accuracy = 0.5f;

        Vector3 lastSeen, investigatePos; float lostT, warnedT;

        protected override void OnSpawned() { EquipGun(WeaponKind.Pistol, false); SetState("patrol"); idleClip = "Idle"; Patrol(); }
        protected override Vector3 RespawnPoint() => stationPos + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));

        void Patrol() { var p = RandomPedNode(stationPos, 90f); if (!GoTo(p, walkSpeed)) { SetState("standby"); Stop(); } }

        protected override void OnNoise(Vector3 pos, float radius, GameObject src, bool melee)
        {
            if (State == "dead" || src == gameObject || melee) return;
            float d = Vector3.Distance(pos, transform.position);
            if (d > radius * 1.6f) return;
            bool playerShot = Player && src == Player.gameObject;
            if (playerShot && Wanted) { if (Wanted.Level == 0) Wanted.Add(1, "shots fired"); Wanted.Seen(pos); }
            if (State == "chase" || State == "engage" || State == "arrest") return;
            if (playerShot && CanSeePlayer(sightRange + 10f, 360f)) { StartChase(); return; }
            investigatePos = pos; SetState("investigate"); GoTo(pos, runSpeed * 0.8f);
        }

        protected override void OnHit(string zone, Vector3 point)
        {
            if (!Wanted) return;
            if (body && body.KilledBy != "player") { if (State != "chase" && State != "engage" && State != "arrest") { investigatePos = point; SetState("investigate"); GoTo(point, runSpeed * 0.8f); } return; }   // hit by traffic / a gangster: not the player's fault
            Wanted.PoliceHostile = true; if (Wanted.Level < 2) Wanted.Add(2, "assaulting an officer");
            Wanted.Seen(Player ? Player.transform.position : point);
            GameManager.I?.Say($"{displayName}: We warned you!");
            SetState("engage");
        }

        public bool IsIdle => State == "patrol" || State == "standby" || State == "return" || State == "lookaround";
        /// <summary>A shop's silent alarm: go and have a look at its door.</summary>
        public void Dispatch(Vector3 door, Vector3 threat) { if (!IsIdle) return; investigatePos = door; lastSeen = threat; SetState("investigate"); GoTo(door, runSpeed * 0.9f); }
        public void Chase() { if (State == "chase" || State == "engage" || State == "arrest") return; lastSeen = Player ? Player.transform.position : lastSeen; StartChase(); }

        void StartChase()
        {
            if (State != "chase" && State != "engage") GameManager.I?.Say($"{displayName}: POLICE! FREEZE!");
            SetState("chase"); lostT = 0f;
        }

        protected override void Think()
        {
            if (!Player || !Wanted) return;
            bool see = CanSeePlayer(sightRange, 130f);
            if (see) { lastSeen = Player.transform.position; Wanted.Seen(lastSeen); }
            float dp = DistToPlayer;

            // acquire
            if (Wanted.Level > 0 && see && State != "chase" && State != "engage" && State != "arrest") StartChase();

            switch (State)
            {
                case "patrol":
                    if (Arrived() || stateT > 45f) { SetState("standby"); Stop(); stateT = -Random.Range(2f, 6f); }
                    break;
                case "standby":
                    if (stateT > 0f) { SetState("patrol"); Patrol(); }
                    break;
                case "investigate":
                    if (Arrived() || stateT > 15f) { Stop(); lookAt = investigatePos; SetState("lookaround"); }
                    break;
                case "lookaround":
                    if (stateT > 6f) { SetState("patrol"); Patrol(); }
                    else if (Time.frameCount % 60 == 0) lookAt = transform.position + Random.insideUnitSphere * 5f;
                    break;
                case "chase":
                    ShowGun(Wanted.PoliceHostile);
                    if (PlayerBody && PlayerBody.Dead) { SetState("return"); Patrol(); break; }
                    if (see) lostT = 0f; else lostT += 0.2f;
                    if (lostT > 3f) { SetState("search"); GoTo(lastSeen, runSpeed); break; }
                    if (Wanted.PoliceHostile || stateT > chaseToShootSeconds) { if (!Wanted.PoliceHostile) { Wanted.PoliceHostile = true; GameManager.I?.Say($"{displayName}: I SAID STOP!"); } SetState("engage"); break; }
                    if (dp < 1.8f && !PlayerInVehicle && see) { Stop(); SetState("arrest"); lookAt = Player.transform.position; anim.PlayUpper("Take"); GameManager.I?.Arrest(this); break; }
                    if (stateT % 1f < 0.25f || Arrived(2f)) GoTo(Player.transform.position, runSpeed);
                    if (stateT - warnedT > 6f) { warnedT = stateT; GameManager.I?.Say($"{displayName}: STOP!"); }
                    break;
                case "arrest":
                    if (stateT > 3.5f) { SetState("return"); Patrol(); }
                    break;
                case "engage":
                    ShowGun(true);
                    if (PlayerBody && PlayerBody.Dead) { SetState("return"); Patrol(); break; }
                    if (see) lostT = 0f; else lostT += 0.2f;
                    if (lostT > 4f) { SetState("search"); GoTo(lastSeen, runSpeed); break; }
                    if (!see) { GoTo(lastSeen, runSpeed); break; }
                    if (dp > 16f) { GoTo(Player.transform.position, runSpeed); }
                    else { Stop(); lookAt = Player.transform.position; idleClip = "Pistol_Aim"; if (Vector3.Angle(transform.forward, Player.transform.position - transform.position) < 25f) ShootAt(PlayerBody, accuracy, "police"); }
                    break;
                case "search":
                    idleClip = "Idle";
                    if (see) { StartChase(); break; }
                    if (Arrived() || stateT > 12f)
                    {
                        if (stateT > 22f || Wanted.Level == 0) { SetState("return"); ShowGun(false); GameManager.I?.Say($"{displayName} lost the suspect"); Patrol(); }
                        else if (Arrived()) { var p = lastSeen + Random.insideUnitSphere * 10f; p.y = lastSeen.y; GoTo(p, walkSpeed * 1.4f); }
                    }
                    break;
                case "return":
                    idleClip = "Idle";
                    if (Arrived() || stateT > 40f) { SetState("patrol"); Patrol(); }
                    break;
            }
            if (State != "engage") idleClip = "Idle";
        }

        protected override void Animate(float dt)
        {
            base.Animate(dt);
            // keep aiming at the player while engaging
            if (State == "engage" && Player && !Moving) { Vector3 d = Player.transform.position - transform.position; d.y = 0f; if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 540f * dt); }
        }
    }
}
