using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Npc;

namespace MafiaSandbox.Factions
{
    /// <summary>
    /// A gang member or a made man. Roles: "loiter" (hang around a spot with the others: they stand in a loose circle, take
    /// turns talking, laugh, at night some dance), "patrol" (walk the crew's loop), "guard" (stand at the HQ door, arms crossed,
    /// eyes on the street). Everyone watches the player: unwelcome players get warnings that grow sharper the closer they come,
    /// hostile ones are attacked (gangs with bats spread out and surround him, the families keep their distance, step aside
    /// between shots), and enemy-bloc members who wander in get jumped. A badly hurt member, or one who sees most of his
    /// friends drop, may run for it. The whole crew shares an alarm through FactionManager, so hitting one brings the rest running.
    /// </summary>
    public class FactionBrain : NpcBrain
    {
        public string factionId; public string role = "loiter";
        public Vector3 homeSpot; public Vector3[] route; public int routeIdx;
        public float sightRange = 28f, warnRadius = 16f, giveUpRadius = 55f;
        public FactionDef Def => def ??= FactionDefs.Get(factionId);
        FactionDef def; bool armed; float swingCd, warnedAt = -99f, chatT, strafeT, barkCd, moraleCd; int warnStage; CharacterBody target; Vector3 groupCentre; float groupT;
        static readonly string[] WarnLines = { "Keep walking.", "You're not welcome here.", "Wrong neighbourhood, friend.", "This is {0} turf. Move along." };
        static readonly string[] Banter = { "Ha! You should've seen his face.", "Business is slow this week.", "Who's got a smoke?", "The boss wants more.", "I'm telling you, it was him.", "Nah, nah, listen...", "Cops were sniffing around earlier.", "Ha ha ha!", "That's what I said!", "Next time I'm driving." };
        protected override bool AllowGlance => State == "loiter" || State == "guard" || State == "patrol";

        protected override void OnSpawned()
        {
            var d = Def; armed = d != null && d.kind == FactionKind.Mafia;
            EquipGun(armed ? WeaponKind.Pistol : WeaponKind.Bat, !armed); idleClip = armed ? "Idle" : "Bat_Idle";
            if (homeSpot == Vector3.zero) homeSpot = spawnPos;
            target = null; warnStage = 0; pose = ArmPose.None; faceWhileMoving = null; headTarget = null;
            SetState(role == "patrol" ? "patrol" : role == "guard" ? "guard" : "loiter"); if (role == "patrol" && route != null && route.Length > 0) GoTo(route[routeIdx], walkSpeed);
        }
        protected override Vector3 RespawnPoint() => homeSpot;

        protected override void OnHit(string zone, Vector3 point)
        {
            if (body.KilledBy == "player") { FactionManager.I?.PlayerAttacked(Def, false); Engage(PlayerBody); }
            if (State == "attack" && body.hp <= 1 && moraleCd <= 0f && Random.value < 0.35f) Retreat("I'm hit! I'm out!");
        }
        protected override void OnDied(string by) { if (by == "player") FactionManager.I?.Alert(Def, 60f); }
        protected override void OnNoise(Vector3 pos, float radius, GameObject src, bool melee)
        {
            if (!Player || src != Player.gameObject || melee) return;
            // gunfire in our turf puts the crew on edge; if they already dislike the player it is a fight
            if (Def != null && Def.Contains(pos) && FactionManager.I && FactionManager.I.Wary(Def)) { FactionManager.I.Alert(Def, 20f); }
            if (State == "loiter" || State == "guard" || State == "patrol") headTarget = pos + Vector3.up * 1.5f;
        }

        string Idle0 => armed ? "Idle" : "Bat_Idle";

        protected override void Think()
        {
            var fm = FactionManager.I; var d = Def; if (fm == null || d == null || !Player || !PlayerBody) return;
            float dt = thinkDt;
            float dp = DistToPlayer; bool see = CanSeePlayer(sightRange, 200f);
            bool hostile = fm.Hostile(d), wary = fm.Wary(d); bool inTurf = d.Contains(Player.transform.position);
            swingCd -= dt; barkCd -= dt; moraleCd -= dt;

            // enemies first: a player we hate, or a rival-bloc member in sight
            if (State != "attack" && State != "retreat")
            {
                if (hostile && see && dp < giveUpRadius && !(PlayerInVehicle && dp > 12f)) { Engage(PlayerBody); return; }
                if (stateT % 1.2f < dt * 1.25f) { var rival = NearestRival(22f); if (rival) { Engage(rival); return; } }
            }

            switch (State)
            {
                case "loiter":
                    if (see && wary && inTurf && dp < warnRadius && !PlayerInVehicle) { StartWarn(); break; }
                    Loiter(dt);
                    break;
                case "guard":
                    idleClip = Idle0; pose = armed ? ArmPose.Crossed : ArmPose.None;
                    headTarget = see && dp < 20f ? Player.transform.position + Vector3.up * 1.5f : (Vector3?)null;
                    if (see && wary && inTurf && dp < warnRadius && !PlayerInVehicle) { StartWarn(); break; }
                    if (!Arrived(0.8f) && stateT > 3f) GoTo(homeSpot, walkSpeed);
                    break;
                case "patrol":
                    idleClip = Idle0; pose = ArmPose.None;
                    if (see && wary && inTurf && dp < warnRadius && !PlayerInVehicle) { StartWarn(); break; }
                    if (route == null || route.Length == 0) { SetState("loiter"); break; }
                    if (Arrived(1.2f)) { if (stateT > 2.5f) { routeIdx = (routeIdx + 1) % route.Length; GoTo(route[routeIdx], walkSpeed); stateT = 0f; } }
                    else if (stateT > 40f) { routeIdx = (routeIdx + 1) % route.Length; GoTo(route[routeIdx], walkSpeed); stateT = 0f; }   // stuck guard
                    break;
                case "warn":   // the closer he comes, the sharper it gets
                    idleClip = armed ? "Pistol_Aim" : "Bat_Idle"; lookAt = Player.transform.position; headTarget = Player.transform.position + Vector3.up * 1.5f;
                    if (warnStage == 1 && dp < 9f) { warnStage = 2; pose = ArmPose.Point; pointAt = Player.transform.position + Vector3.up; Bark(Barks.Pick("Last warning.", "I said walk away.", "One more step..."), Barks.Gang, 2.4f, true); stateT = 0f; }
                    if (pose == ArmPose.Point && stateT > 2f) pose = ArmPose.None;
                    if (armed && warnStage >= 2) ShowGun(true);
                    if (hostile || (dp < 3.5f && warnStage >= 2 && stateT > 2.5f && !PlayerInVehicle)) { if (!hostile) { fm.Change(d, -6f, true); fm.Alert(d, 25f); } Engage(PlayerBody); break; }
                    if (dp > warnRadius + 5f || !see || !inTurf) { if (armed) ShowGun(false); pose = ArmPose.None; headTarget = null; warnStage = 0; BackToRole(); if (Random.value < 0.4f) Bark(Barks.Pick("That's right, keep moving.", "Smart.", "And don't come back."), Barks.Gang, 2f); }
                    break;
                case "attack":
                    pose = ArmPose.None;
                    if (!target || target.Dead || target == PlayerBody && (dp > giveUpRadius || (PlayerInVehicle && dp > 14f) || (!hostile && !fm.Alerted(d)))) { Disengage(); break; }
                    if (moraleCd <= 0f) { moraleCd = 3f; if (Shaken()) { Retreat(Barks.Pick("Forget this!", "They're killing us!", "Run!")); break; } }
                    {
                        float tdist = Vector3.Distance(transform.position, target.transform.position); bool canSee = CanSee(target, sightRange + 10f, 360f);
                        headTarget = target.ChestPos;
                        if (barkCd <= 0f) { barkCd = Random.Range(4f, 8f); Bark(armed ? Barks.Pick("You're dead!", "Take him out!", "For the family!", "Nobody touches us!") : Barks.Pick("Get him!", "Come here!", "You're finished!", "Surround him!"), Barks.Gang, 1.8f, false, 34f); }
                        if (armed)
                        {
                            if (tdist < 24f && canSee)
                            {
                                lookAt = target.transform.position; faceWhileMoving = target.transform.position; idleClip = "Pistol_Aim"; ShowGun(true);
                                strafeT -= dt; if (strafeT <= 0f) { strafeT = Random.Range(2f, 4f); if (Random.value < 0.55f && StrafePoint(target.transform.position, out var sp)) GoTo(sp, walkSpeed * 1.1f); else Stop(); }
                                if (Vector3.Angle(transform.forward, target.transform.position - transform.position) < 25f) ShootAt(target, Moving ? 0.25f : 0.33f, factionId);
                            }
                            else { idleClip = "Idle"; faceWhileMoving = null; if (Arrived(1.5f) || stateT % 1f < dt * 1.25f) GoTo(target.transform.position, runSpeed); }
                        }
                        else
                        {
                            idleClip = "Bat_Idle"; faceWhileMoving = null;
                            if (tdist < 1.9f) { Stop(); lookAt = target.transform.position; if (swingCd <= 0f) { swingCd = Random.Range(1.1f, 1.6f); anim.PlayUpper("Bat_Attack"); Invoke(nameof(Swing), 0.42f); } }
                            else if (Arrived(1.5f) || stateT % 1f < dt * 1.25f) GoTo(SurroundPoint(target, tdist), runSpeed);
                        }
                    }
                    if (stateT > 90f) Disengage();
                    break;
                case "retreat":   // running home to lick wounds
                    faceWhileMoving = null; pose = ArmPose.None;
                    if (Arrived(1.5f) || stateT > 25f) { Stop(); idleClip = "Crouch_Idle"; if (stateT > 25f) { idleClip = Idle0; target = null; BackToRole(); } }
                    break;
            }
        }

        /// <summary>Hanging out at the spot: drift around a little, face the group, take turns talking, laugh, at night maybe dance.</summary>
        void Loiter(float dt)
        {
            chatT -= dt; groupT -= dt;
            if (groupT <= 0f)
            {
                groupT = 3f; Vector3 sum = Vector3.zero; int n = 0;
                foreach (var b in CharacterBody.All) { if (!b || b.Dead || b == body) continue; var fb = b.GetComponent<FactionBrain>(); if (!fb || fb.factionId != factionId || fb.State != "loiter") continue; Vector3 dd = b.transform.position - transform.position; if (dd.sqrMagnitude > 7f * 7f) continue; sum += b.transform.position; n++; }
                groupCentre = n > 0 ? sum / n : transform.position + transform.forward;
            }
            if (Arrived(0.8f))
            {
                if (groupCentre != transform.position) lookAt = groupCentre;
                if (chatT <= 0f)
                {
                    chatT = Random.Range(3f, 7f); float r = Random.value; bool night = World.DayNightCycle.I && World.DayNightCycle.I.IsNight;
                    if (night && r < 0.12f && !armed) { idleClip = "Dance"; nod = 0f; pose = ArmPose.None; }
                    else if (r < 0.45f) { idleClip = "Talk"; nod = 0f; pose = ArmPose.None; if (CamDist(transform.position) < 22f && Random.value < 0.5f) Bark(Banter[Random.Range(0, Banter.Length)], Barks.Gang, 2.4f); }
                    else { idleClip = Idle0; nod = Random.value < 0.6f ? 0.6f : 0f; pose = armed && Random.value < 0.35f ? ArmPose.Crossed : ArmPose.None; }
                    headTarget = null;
                }
                if (stateT > Random.Range(12f, 22f)) { idleClip = Idle0; pose = ArmPose.None; nod = 0f; GoTo(homeSpot + new Vector3(Random.Range(-3.5f, 3.5f), 0f, Random.Range(-3.5f, 3.5f)), walkSpeed * 0.7f); stateT = 0f; }
            }
            else { idleClip = Idle0; pose = ArmPose.None; nod = 0f; }
        }

        void StartWarn()
        {
            SetState("warn"); Stop(); lookAt = Player.transform.position; nod = 0f; pose = ArmPose.None; idleClip = Idle0;
            if (warnStage == 0) warnStage = 1;
            if (Time.time - warnedAt < 14f) return; warnedAt = Time.time;
            Bark(string.Format(WarnLines[Random.Range(0, WarnLines.Length)], Def.name), Barks.Gang, 2.6f, true);
            anim.PlayUpper("Wave");
        }

        void BackToRole()
        {
            SetState(role == "patrol" ? "patrol" : role == "guard" ? "guard" : "loiter");
            if (role == "patrol" && route != null && route.Length > 0) GoTo(route[routeIdx], walkSpeed); else GoTo(homeSpot, walkSpeed);
        }

        void Engage(CharacterBody t)
        {
            if (!t || t.Dead || State == "retreat") return; target = t; if (State != "attack") { SetState("attack"); nod = 0f; pose = ArmPose.None; barkCd = 0f; }
            if (t == PlayerBody) FactionManager.I?.Alert(Def, 15f);
            GoTo(t.transform.position, runSpeed);
        }
        void Disengage()
        {
            target = null; lookAt = null; faceWhileMoving = null; headTarget = null; warnStage = 0; if (armed) ShowGun(false);
            BackToRole();
        }
        void Retreat(string line)
        {
            moraleCd = 30f; target = null; faceWhileMoving = null; if (armed) ShowGun(false);
            Bark(line, Barks.Gang, 2f, false, 34f);
            Vector3 home = Def != null ? Def.HqUnity : homeSpot; if (!GoTo(home, runSpeed)) { var p = EscapePoint(Player ? Player.transform.position : transform.position, 25f, true); if (p.HasValue) GoTo(p.Value, runSpeed); }
            SetState("retreat");
        }
        /// <summary>Half the friends around us are down, or we are alone, hurt and outnumbered.</summary>
        bool Shaken()
        {
            int alive = 0, down = 0;
            foreach (var b in CharacterBody.All)
            {
                if (!b || b == body) continue; var fb = b.GetComponent<FactionBrain>(); if (!fb || fb.factionId != factionId) continue;
                if ((b.transform.position - transform.position).sqrMagnitude > 25f * 25f) continue; if (b.Dead) down++; else alive++;
            }
            if (down >= 2 && down > alive + 1) return Random.value < 0.5f;
            return body.hp <= 1 && alive == 0 && Random.value < 0.3f;
        }
        /// <summary>Bat crews don't queue up behind each other: everyone takes his own side of the target.</summary>
        Vector3 SurroundPoint(CharacterBody t, float dist)
        {
            if (dist > 7f) return t.transform.position;
            float ang = (uid % 6) * 60f;
            Vector3 p = t.transform.position + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * 1.4f;
            return NavMesh.SamplePosition(p, out var h, 1.2f, NavMesh.AllAreas) ? h.position : t.transform.position;
        }

        CharacterBody NearestRival(float range)
        {
            CharacterBody best = null; float bd = range * range;
            foreach (var b in CharacterBody.All)
            {
                if (!b || b.Dead || b == body) continue; var fb = b.GetComponent<FactionBrain>(); if (!fb || !FactionDefs.AreEnemies(Def, fb.Def)) continue;
                float dd = (b.transform.position - transform.position).sqrMagnitude; if (dd < bd && CanSee(b, range, 160f)) { bd = dd; best = b; }
            }
            return best;
        }

        void Swing()
        {
            if (!target || target.Dead || body.Dead || Vector3.Distance(transform.position, target.transform.position) > 2.4f) { Sfx.At("swing", transform.position, 0.5f); return; }
            Vector3 dir = (target.transform.position - transform.position).normalized; Vector3 point = target.ChestPos;
            CombatFx.I?.Blood(point, dir, 4); Sfx.At("thud", point);
            target.Hit("torso", point, dir, 1, false, 1.5f, factionId, false);
            if (target == PlayerBody) GameManager.I?.PlayerHurt();
        }
    }
}
