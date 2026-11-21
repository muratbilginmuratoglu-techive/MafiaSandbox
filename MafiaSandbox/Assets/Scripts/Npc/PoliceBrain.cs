using UnityEngine;
using UnityEngine.AI;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;

namespace MafiaSandbox.Npc
{
    /// <summary>
    /// Police: walk the beat in pairs around the station (the partner walks alongside, they chat at the corners), check out
    /// gunfire, bodies and phone reports, tell the player to put his gun away, chase when he is wanted (the second officer
    /// cuts him off instead of running in his footsteps), arrest on contact, open fire if shot at or if the chase drags on
    /// (stepping sideways between shots instead of standing still), fan out to search the last known position and give up
    /// when the trail goes cold. The rules the player asked for stay: chase first, shoot only if the player shoots or after a
    /// long chase, arrest when caught, lose the player when they break line of sight for ~20 s.
    /// </summary>
    public class PoliceBrain : NpcBrain
    {
        public Vector3 stationPos;
        public float sightRange = 38f;
        public float chaseToShootSeconds = 20f;
        public float accuracy = 0.5f;
        /// <summary>The officer this one walks the beat with (the one with the lower number leads).</summary>
        public PoliceBrain partner;

        Vector3 lastSeen, lastVel, investigatePos; float lostT, warnedT, gunWarnCd, aimedAtT, strafeT, searchN, bodyCd; int searchIdx;
        static readonly System.Collections.Generic.HashSet<CharacterBody> bodiesSeen = new System.Collections.Generic.HashSet<CharacterBody>();
        bool Leads => !partner || partner.Dead || partner.uid > uid;
        protected override bool AllowGlance => IsIdle || State == "escort" || State == "lookaround";

        protected override void OnSpawned() { EquipGun(WeaponKind.Pistol, false); SetState("patrol"); idleClip = "Idle"; pose = ArmPose.None; Patrol(); }
        protected override Vector3 RespawnPoint() => stationPos + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));

        void Patrol()
        {
            pose = ArmPose.None; faceWhileMoving = null; headTarget = null;
            if (!Leads && partner.IsIdle) { SetState("escort"); return; }
            var p = RandomPedNode(stationPos, 90f); if (!GoTo(p, walkSpeed)) { SetState("standby"); Stop(); } else SetState("patrol");
        }

        protected override void OnNoise(Vector3 pos, float radius, GameObject src, bool melee)
        {
            if (State == "dead" || src == gameObject || melee) return;
            float d = Vector3.Distance(pos, transform.position);
            if (d > radius * 1.6f) return;
            bool playerShot = Player && src == Player.gameObject;
            if (playerShot && Wanted && Wanted.Level > 0) Wanted.Seen(pos);
            if (State == "chase" || State == "engage" || State == "arrest") return;
            if (playerShot && CanSeePlayer(sightRange + 10f, 360f)) { Wanted.Raise(1, "shots fired"); StartChase(); return; }
            if (playerShot && Wanted) Wanted.Suspect(1, 45f);
            investigatePos = pos; SetState("investigate"); GoTo(pos, runSpeed * 0.8f); headTarget = pos + Vector3.up * 1.5f;
            Bark(Barks.Pick("Shots fired!", "Gunfire, over there!", "Dispatch, shots fired, checking it out."), Barks.Police, 2.4f, false, 34f);
        }

        protected override void OnHit(string zone, Vector3 point)
        {
            if (!Wanted) return;
            if (body && body.KilledBy != "player") { if (State != "chase" && State != "engage" && State != "arrest") { investigatePos = point; SetState("investigate"); GoTo(point, runSpeed * 0.8f); } return; }   // hit by traffic / a gangster: not the player's fault
            Wanted.PoliceHostile = true; if (Wanted.Level < 2) Wanted.Add(2, "assaulting an officer");
            Wanted.Seen(Player ? Player.transform.position : point);
            Bark("We warned you!", Barks.Police, 2.4f, true, 40f);
            SetState("engage");
        }

        public bool IsIdle => State == "patrol" || State == "standby" || State == "return" || State == "lookaround" || State == "escort";
        /// <summary>A shop's silent alarm or a phone call: go and have a look.</summary>
        public void Dispatch(Vector3 door, Vector3 threat)
        {
            if (!IsIdle || WantedSystem.PoliceOff) return; investigatePos = door; lastSeen = threat; SetState("investigate"); GoTo(door, runSpeed * 0.9f);
            pose = ArmPose.PhoneCall; Invoke(nameof(DropRadio), 2.2f); Bark(Barks.Pick("Copy that, on my way.", "Ten-four, responding.", "Heading there now."), Barks.Police, 2.4f, false, 30f);
        }
        void DropRadio() { if (pose == ArmPose.PhoneCall) pose = ArmPose.None; }
        public void Chase() { if (WantedSystem.PoliceOff) return; if (State == "chase" || State == "engage" || State == "arrest") return; lastSeen = Player ? Player.transform.position : lastSeen; StartChase(); }

        /// <summary>A civilian phoned it in: the nearest free officer goes to have a look, and a crime by the player makes him a suspect.</summary>
        public static void Report(Vector3 pos, bool byPlayer)
        {
            if (WantedSystem.PoliceOff) return;
            if (byPlayer && WantedSystem.I && WantedSystem.I.Level == 0) WantedSystem.I.Suspect(1, 60f);
            PoliceBrain best = null; float bd = 300f * 300f;
            foreach (var c in FindObjectsByType<PoliceBrain>(FindObjectsSortMode.None)) { if (!c || c.Dead || !c.IsIdle) continue; float d = (c.transform.position - pos).sqrMagnitude; if (d < bd) { bd = d; best = c; } }
            if (best) { best.Dispatch(pos, pos); if (best.partner && !best.partner.Dead) best.partner.Dispatch(pos + Random.insideUnitSphere * 2f, pos); }
        }

        void StartChase()
        {
            if (WantedSystem.PoliceOff) return;
            if (State != "chase" && State != "engage") Bark(Barks.Pick("POLICE! FREEZE!", "Stop right there!", "Police! Don't move!"), Barks.Police, 2.4f, true, 40f);
            SetState("chase"); lostT = 0f; pose = ArmPose.None; headTarget = null;
        }

        protected override void Think()
        {
            if (!Player || !Wanted) return;
            float dt = thinkDt;
            bool see = CanSeePlayer(sightRange, 130f);
            if (see) { Vector3 np = Player.transform.position; lastVel = Vector3.Lerp(lastVel, (np - lastSeen) / Mathf.Max(0.05f, dt), 0.5f); lastVel.y = 0f; if (lastVel.magnitude > 9f) lastVel = lastVel.normalized * 9f; lastSeen = np; Wanted.Seen(lastSeen); }
            float dp = DistToPlayer; gunWarnCd -= dt; bodyCd -= dt;

            // acquire
            if (Wanted.Level > 0 && see && State != "chase" && State != "engage" && State != "arrest") StartChase();
            // an idle officer and a man waving a gun about: tell him to put it away; aim it at us and it's a crime
            if (IsIdle || State == "gunwarn")
            {
                bool gunUp = PlayerGunUp && see && dp < 14f && !PlayerInVehicle;
                if (gunUp && State != "gunwarn" && gunWarnCd <= 0f && !WantedSystem.PoliceOff) { gunWarnCd = 12f; Stop(); SetState("gunwarn"); aimedAtT = 0f; ShowGun(true); Bark(Barks.Pick("Hey! Put that gun away!", "Drop the weapon!", "Lower it. Now."), Barks.Police, 2.6f, true, 30f); }
            }
            if (IsIdle && bodyCd <= 0f) LookForBodies();

            switch (State)
            {
                case "patrol":
                    if (Arrived() || stateT > 45f) { SetState("standby"); Stop(); stateT = -Random.Range(3f, 8f); pose = Random.value < 0.5f ? ArmPose.Hips : ArmPose.None; if (partner && !partner.Dead && partner.State == "escort") { lookAt = partner.transform.position; idleClip = "Talk"; } }
                    break;
                case "standby":
                    if (partner && !partner.Dead && partner.State == "escort") { lookAt = partner.transform.position; headTarget = partner.transform.position + Vector3.up * 1.5f; idleClip = stateT % 7f < 3.5f ? "Talk" : "Idle"; if (stateT % 7f < dt && Random.value < 0.5f && CamDist(transform.position) < 20f) Bark(Barks.Pick("Quiet day.", "Coffee after this?", "Sarge wants the reports by six.", "You hear about the robbery?", "My feet are killing me."), Barks.Police, 2.4f); }
                    if (stateT > 0f) { idleClip = "Idle"; headTarget = null; Patrol(); }
                    break;
                case "escort":   // walking the beat beside the leading officer
                    if (Leads || !partner.IsIdle) { Patrol(); break; }
                    {
                        Vector3 lp = partner.transform.position; float d = Vector3.Distance(transform.position, lp);
                        if (partner.State == "standby" || !partner.agent.hasPath) { if (d > 2.2f) GoTo(lp + partner.transform.right * 1.1f, walkSpeed); else { Stop(); lookAt = lp; headTarget = lp + Vector3.up * 1.5f; idleClip = partner.idleClip == "Talk" ? "Idle" : "Talk"; nod = idleClip == "Idle" ? 0.5f : 0f; } }
                        else { nod = 0f; idleClip = "Idle"; headTarget = null; GoTo(lp + partner.transform.right * 1.0f, Mathf.Max(walkSpeed, partner.agent.speed * (d > 2.5f ? 1.3f : 1f)), 2f); }
                    }
                    break;
                case "gunwarn":
                    idleClip = "Pistol_Aim"; lookAt = Player.transform.position; headTarget = Player.transform.position + Vector3.up * 1.5f;
                    if (PlayerAimingAtMe(16f)) { aimedAtT += dt; if (aimedAtT > 1.2f && !WantedSystem.PoliceOff) { Wanted.Raise(1, "threatening an officer"); Wanted.Seen(Player.transform.position); StartChase(); break; } }
                    if (!PlayerGunUp && stateT > 1.5f) { Bark(Barks.Pick("That's better.", "Keep it holstered.", "Move along."), Barks.Police, 2f); ShowGun(false); idleClip = "Idle"; headTarget = null; Patrol(); break; }
                    if (stateT > 8f || dp > 22f) { ShowGun(false); idleClip = "Idle"; headTarget = null; Patrol(); }
                    break;
                case "investigate":
                    if (Arrived() || stateT > 15f) { Stop(); lookAt = investigatePos; headTarget = null; SetState("lookaround"); pose = ArmPose.None; }
                    break;
                case "lookaround":
                    if (stateT > 7f) { pose = ArmPose.None; headTarget = null; Patrol(); }
                    else if (stateT % 2f < dt) headTarget = transform.position + Quaternion.Euler(0f, Random.Range(-80f, 80f), 0f) * transform.forward * 6f + Vector3.up * 1.4f;
                    break;
                case "body":   // a dead body: walk over, call it in on the radio, look around
                    if (Arrived(1.5f) || stateT > 15f)
                    {
                        Stop(); lookAt = investigatePos; headTarget = investigatePos + Vector3.up * 0.3f; pose = ArmPose.PhoneCall; idleClip = "Talk";
                        Bark(Barks.Pick("Dispatch, we've got a body.", "Send a coroner to my location.", "Victim's down, no pulse."), Barks.Police, 2.8f);
                        Invoke(nameof(DropRadio), 4f); SetState("lookaround"); stateT = -4f;
                    }
                    break;
                case "chase":
                    ShowGun(Wanted.PoliceHostile); idleClip = "Idle"; pose = ArmPose.None;
                    if (PlayerBody && PlayerBody.Dead) { SetState("return"); Patrol(); break; }
                    if (see) lostT = 0f; else lostT += dt;
                    if (lostT > 3f) { StartSearch(); break; }
                    if (Wanted.PoliceHostile || stateT > chaseToShootSeconds) { if (!Wanted.PoliceHostile) { Wanted.PoliceHostile = true; Bark("I SAID STOP!", Barks.Police, 2.4f, true, 40f); } SetState("engage"); break; }
                    if (dp < 1.8f && !PlayerInVehicle && see) { Stop(); SetState("arrest"); lookAt = Player.transform.position; anim.PlayUpper("Take"); GameManager.I?.Arrest(this); break; }
                    if (stateT % 1f < dt * 1.25f || Arrived(2f)) GoTo(ChaseTarget(dp), runSpeed);
                    if (stateT - warnedT > 6f) { warnedT = stateT; Bark(Barks.Pick("STOP!", "Stop, police!", "You can't run forever!", "Get on the ground!"), Barks.Police, 2f, false, 40f); }
                    break;
                case "arrest":
                    if (stateT > 3.5f) { SetState("return"); Patrol(); }
                    break;
                case "engage":
                    ShowGun(true); pose = ArmPose.None;
                    if (PlayerBody && PlayerBody.Dead) { faceWhileMoving = null; SetState("return"); Patrol(); break; }
                    if (see) lostT = 0f; else lostT += dt;
                    if (lostT > 4f) { faceWhileMoving = null; StartSearch(); break; }
                    if (!see) { faceWhileMoving = null; GoTo(lastSeen, runSpeed); break; }
                    lookAt = Player.transform.position; faceWhileMoving = Player.transform.position; idleClip = "Pistol_Aim";
                    if (dp > 16f) { GoTo(Player.transform.position, runSpeed); faceWhileMoving = null; }
                    else
                    {
                        // between shots, a step or two to the side; never stand still in a gunfight
                        strafeT -= dt;
                        if (strafeT <= 0f) { strafeT = Random.Range(2f, 3.8f); if (Random.value < 0.6f && StrafePoint(Player.transform.position, out var sp)) GoTo(sp, walkSpeed * 1.1f); else Stop(); if (Random.value < 0.25f) Bark(Barks.Pick("Drop it!", "Suspect is armed!", "Officer needs backup!", "Put your hands up!"), Barks.Police, 1.8f, false, 40f); }
                        if (Vector3.Angle(transform.forward, Player.transform.position - transform.position) < 25f) ShootAt(PlayerBody, accuracy * (Moving ? 0.75f : 1f), "police");
                    }
                    break;
                case "search":
                    idleClip = "Idle"; faceWhileMoving = null;
                    if (see) { headTarget = null; StartChase(); break; }
                    if (stateT % 1.6f < dt) headTarget = transform.position + Quaternion.Euler(0f, Random.Range(-90f, 90f), 0f) * transform.forward * 8f + Vector3.up * 1.4f;   // scanning
                    if (Arrived() || stateT > 12f)
                    {
                        if (stateT > 24f || Wanted.Level == 0) { SetState("return"); ShowGun(false); headTarget = null; Bark(Barks.Pick("Lost him.", "He's gone.", "Dispatch, suspect lost."), Barks.Police, 2.4f, true, 30f); Patrol(); }
                        else if (Arrived()) GoTo(SearchPoint(), walkSpeed * 1.5f);
                    }
                    break;
                case "return":
                    idleClip = "Idle"; ShowGun(false); faceWhileMoving = null;
                    if (Arrived() || stateT > 40f) Patrol();
                    break;
            }
            if (State != "engage" && State != "gunwarn") idleClip = idleClip == "Pistol_Aim" ? "Idle" : idleClip;
        }

        /// <summary>Where to run: straight at him if close; otherwise where he's heading. The second officer of a pair swings wide to cut him off.</summary>
        Vector3 ChaseTarget(float dp)
        {
            Vector3 pp = Player.transform.position; if (dp < 6f) return pp;
            Vector3 lead = pp + lastVel * Mathf.Clamp(dp / runSpeed, 0.3f, 1.6f);
            if (partner && !partner.Dead && partner.State == "chase" && !Leads)
            {
                Vector3 side = Vector3.Cross(Vector3.up, lastVel.sqrMagnitude > 0.5f ? lastVel.normalized : (pp - transform.position).normalized);
                if (Vector3.Dot(side, transform.position - pp) < 0f) side = -side;
                lead += side * Mathf.Min(6f, dp * 0.3f) + lastVel.normalized * 3f;
            }
            return lead;
        }
        void StartSearch()
        {
            SetState("search"); searchIdx = 0; searchN = 0f;
            GoTo(lastSeen + lastVel.normalized * Mathf.Min(8f, lastVel.magnitude * 2f), runSpeed);
            Bark(Barks.Pick("Where'd he go?", "Spread out!", "Check the alleys!", "He went this way!"), Barks.Police, 2.2f, false, 34f);
        }
        /// <summary>Somewhere near the last sighting that we can't see from here (round a corner), each officer picking different directions.</summary>
        Vector3 SearchPoint()
        {
            searchIdx++; Vector3 best = lastSeen; float bs = -1f;
            for (int i = 0; i < 6; i++)
            {
                float ang = (uid % 7) * 51f + searchIdx * 97f + i * 60f + Random.Range(-20f, 20f);
                Vector3 c = lastSeen + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * Random.Range(6f, 16f);
                if (!NavMesh.SamplePosition(c, out var h, 3f, NavMesh.AllAreas)) continue;
                float s = (Perception.Clear(Eye, h.position + Vector3.up * 1.2f) ? 0f : 10f) + Random.value * 4f;
                if (s > bs) { bs = s; best = h.position; }
            }
            return best;
        }

        void LookForBodies()
        {
            bodyCd = 1.5f;
            foreach (var b in CharacterBody.All)
            {
                if (!b || !b.Dead || b.isPlayer || bodiesSeen.Contains(b)) continue; if ((b.transform.position - transform.position).sqrMagnitude > 20f * 20f) continue;
                if (!CanSee(b, 20f, 160f)) continue;
                bodiesSeen.Add(b); investigatePos = b.transform.position; SetState("body"); GoTo(b.transform.position, walkSpeed * 1.4f);
                Bark(Barks.Pick("Hey, is he...?", "Man down!", "Oh no."), Barks.Police, 2f); return;
            }
            bodiesSeen.RemoveWhere(x => !x || !x.Dead);
        }
    }
}
