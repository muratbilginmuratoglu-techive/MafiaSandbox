using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Npc;

namespace MafiaSandbox.Factions
{
    /// <summary>
    /// A gang member or a made man. Roles: "loiter" (hang around a spot, chat), "patrol" (walk the crew's loop),
    /// "guard" (stand at the HQ door). Everyone watches the player: unwelcome players get a warning inside the turf,
    /// hostile ones are attacked (gangs with bats, the families with pistols), and enemy-bloc members who wander in get
    /// jumped. The whole crew shares an alarm through FactionManager, so hitting one brings the rest running.
    /// </summary>
    public class FactionBrain : NpcBrain
    {
        public string factionId; public string role = "loiter";
        public Vector3 homeSpot; public Vector3[] route; public int routeIdx;
        public float sightRange = 28f, warnRadius = 16f, giveUpRadius = 55f;
        public FactionDef Def => def ??= FactionDefs.Get(factionId);
        FactionDef def; bool armed; float swingCd, warnedAt = -99f, chatT; CharacterBody target;
        static readonly string[] WarnLines = { "{0}: Keep walking.", "{0}: You're not welcome here.", "{0}: Wrong neighbourhood, friend.", "{0}: This is {1} turf. Move along." };
        static readonly string[] ChatClips = { "Talk", "Idle", "Idle", "Wave", "Idle" };

        protected override void OnSpawned()
        {
            var d = Def; armed = d != null && d.kind == FactionKind.Mafia;
            EquipGun(armed ? WeaponKind.Pistol : WeaponKind.Bat, !armed); idleClip = armed ? "Idle" : "Bat_Idle";
            if (homeSpot == Vector3.zero) homeSpot = spawnPos;
            target = null; SetState(role == "patrol" ? "patrol" : "loiter"); if (role == "patrol" && route != null && route.Length > 0) GoTo(route[routeIdx], walkSpeed);
        }
        protected override Vector3 RespawnPoint() => homeSpot;

        protected override void OnHit(string zone, Vector3 point)
        {
            if (body.KilledBy == "player") { FactionManager.I?.PlayerAttacked(Def, false); Engage(PlayerBody); }
        }
        protected override void OnDied(string by) { if (by == "player") FactionManager.I?.Alert(Def, 60f); }
        protected override void OnNoise(Vector3 pos, float radius, GameObject src, bool melee)
        {
            if (!Player || src != Player.gameObject || melee) return;
            // gunfire in our turf puts the crew on edge; if they already dislike the player it is a fight
            if (Def != null && Def.Contains(pos) && FactionManager.I && FactionManager.I.Wary(Def)) { FactionManager.I.Alert(Def, 20f); }
        }

        protected override void Think()
        {
            var fm = FactionManager.I; var d = Def; if (fm == null || d == null || !Player || !PlayerBody) return;
            float dp = DistToPlayer; bool see = CanSeePlayer(sightRange, 200f);
            bool hostile = fm.Hostile(d), wary = fm.Wary(d); bool inTurf = d.Contains(Player.transform.position);
            swingCd -= 0.2f;

            // enemies first: a player we hate, or a rival-bloc member in sight
            if (State != "attack")
            {
                if (hostile && see && dp < giveUpRadius && !(PlayerInVehicle && dp > 12f)) { Engage(PlayerBody); return; }
                if (stateT % 1.2f < 0.25f) { var rival = NearestRival(22f); if (rival) { Engage(rival); return; } }
            }

            switch (State)
            {
                case "loiter":
                    if (see && wary && inTurf && dp < warnRadius && !PlayerInVehicle) { SetState("warn"); Stop(); lookAt = Player.transform.position; Warn(); break; }
                    chatT -= 0.2f;
                    if (Arrived(0.8f))
                    {
                        if (chatT <= 0f) { chatT = Random.Range(4f, 9f); idleClip = ChatClips[Random.Range(0, ChatClips.Length)]; if (!armed && idleClip == "Idle") idleClip = "Bat_Idle"; }
                        if (stateT > Random.Range(9f, 18f)) { GoTo(homeSpot + new Vector3(Random.Range(-3.5f, 3.5f), 0f, Random.Range(-3.5f, 3.5f)), walkSpeed * 0.7f); stateT = 0f; }
                    }
                    else idleClip = armed ? "Idle" : "Bat_Idle";
                    break;
                case "guard":
                    idleClip = armed ? "Idle" : "Bat_Idle"; lookAt = see && dp < 20f ? Player.transform.position : (Vector3?)null;
                    if (see && wary && inTurf && dp < warnRadius && !PlayerInVehicle) { SetState("warn"); Stop(); Warn(); break; }
                    if (!Arrived(0.8f) && stateT > 3f) GoTo(homeSpot, walkSpeed);
                    break;
                case "patrol":
                    idleClip = armed ? "Idle" : "Bat_Idle";
                    if (see && wary && inTurf && dp < warnRadius && !PlayerInVehicle) { SetState("warn"); Stop(); lookAt = Player.transform.position; Warn(); break; }
                    if (route == null || route.Length == 0) { SetState("loiter"); break; }
                    if (Arrived(1.2f)) { if (stateT > 2.5f) { routeIdx = (routeIdx + 1) % route.Length; GoTo(route[routeIdx], walkSpeed); stateT = 0f; } }
                    else if (stateT > 40f) { routeIdx = (routeIdx + 1) % route.Length; GoTo(route[routeIdx], walkSpeed); stateT = 0f; }   // stuck guard
                    break;
                case "warn":
                    idleClip = armed ? "Pistol_Aim" : "Bat_Idle"; lookAt = Player.transform.position;
                    if (hostile || (dp < 3.5f && stateT > 4f && !PlayerInVehicle)) { if (!hostile) { fm.Change(d, -6f, true); fm.Alert(d, 25f); } Engage(PlayerBody); break; }
                    if (dp > warnRadius + 5f || !see || !inTurf) { SetState(role == "patrol" ? "patrol" : role == "guard" ? "guard" : "loiter"); lookAt = null; if (role == "patrol" && route != null && route.Length > 0) GoTo(route[routeIdx], walkSpeed); }
                    break;
                case "attack":
                    if (!target || target.Dead || target == PlayerBody && (dp > giveUpRadius || (PlayerInVehicle && dp > 14f) || (!hostile && !fm.Alerted(d)))) { Disengage(); break; }
                    float dt = Vector3.Distance(transform.position, target.transform.position); bool canSee = CanSee(target, sightRange + 10f, 360f);
                    if (armed)
                    {
                        if (dt < 24f && canSee) { Stop(); lookAt = target.transform.position; idleClip = "Pistol_Aim"; ShowGun(true); if (Vector3.Angle(transform.forward, target.transform.position - transform.position) < 25f) ShootAt(target, 0.33f, factionId); }
                        else { idleClip = "Idle"; if (Arrived(1.5f) || stateT % 1f < 0.25f) GoTo(target.transform.position, runSpeed); }
                    }
                    else
                    {
                        idleClip = "Bat_Idle";
                        if (dt < 1.9f) { Stop(); lookAt = target.transform.position; if (swingCd <= 0f) { swingCd = 1.25f; anim.PlayUpper("Bat_Attack"); Invoke(nameof(Swing), 0.42f); } }
                        else if (Arrived(1.5f) || stateT % 1f < 0.25f) GoTo(target.transform.position, runSpeed);
                    }
                    if (stateT > 90f) Disengage();
                    break;
            }
        }

        void Warn()
        {
            if (Time.time - warnedAt < 14f) return; warnedAt = Time.time;
            GameManager.I?.Say(string.Format(WarnLines[Random.Range(0, WarnLines.Length)], displayName, Def.name));
            anim.PlayUpper("Wave");
        }

        void Engage(CharacterBody t)
        {
            if (!t || t.Dead) return; target = t; if (State != "attack") { SetState("attack"); }
            if (t == PlayerBody) FactionManager.I?.Alert(Def, 15f);
            GoTo(t.transform.position, runSpeed);
        }
        void Disengage()
        {
            target = null; lookAt = null; if (armed) ShowGun(false);
            SetState(role == "patrol" ? "patrol" : role == "guard" ? "guard" : "loiter");
            if (role == "patrol" && route != null && route.Length > 0) GoTo(route[routeIdx], walkSpeed); else GoTo(homeSpot, walkSpeed);
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
