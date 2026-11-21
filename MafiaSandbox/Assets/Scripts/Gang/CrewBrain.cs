using UnityEngine;
using UnityEngine.AI;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Factions;
using MafiaSandbox.Npc;

namespace MafiaSandbox.Gang
{
    /// <summary>
    /// One of the player's own crew. Following: each member keeps his own place in a loose formation behind the boss, walks
    /// when he walks and runs when he runs, and when the boss stops they keep an eye on the street instead of staring at him
    /// (now and then two of them chat). Guards stand at their post with arms crossed watching passers-by, dealers call out to
    /// people walking past. They fight hostile gang members (stepping aside between shots), and fire whatever they were armed with.
    /// </summary>
    public class CrewBrain : NpcBrain
    {
        public enum Role { Follow, Guard, Dealer }
        public Role role = Role.Follow; public WeaponKind? weapon; public Vector3 post; public int wage = 40; public int sales;
        CharacterBody target; float swingCd, chatT, strafeT, callCd, spotCd;
        static readonly System.Collections.Generic.List<CrewBrain> followers = new System.Collections.Generic.List<CrewBrain>();

        public void SetWeapon(WeaponKind? k) { weapon = k; if (k.HasValue) { EquipGun(k.Value, false); } else { if (gun) Destroy(gun); gun = null; gunKind = null; } }
        public bool Armed => weapon.HasValue;
        bool Ranged => weapon.HasValue && weapon != WeaponKind.Bat && weapon != WeaponKind.Knife;
        public void SetRole(Role r) { role = r; if (r != Role.Follow) post = transform.position; }
        protected override bool AllowGlance => target == null;

        protected override void OnSpawned() { SetState("idle"); idleClip = "Idle"; if (weapon.HasValue) EquipGun(weapon.Value, false); }
        protected override void OnDied(string by) { followers.Remove(this); CrewManager.I?.OnMemberDied(this); Destroy(this, 2f); }
        protected override void OnDisable() { base.OnDisable(); followers.Remove(this); }
        protected override void OnHit(string zone, Vector3 point) { if (Random.value < 0.5f) Bark(Barks.Pick("Agh! Boss, I'm hit!", "They got me!", "Argh!"), Barks.Alarm, 1.8f); }
        protected override Vector3 RespawnPoint() => transform.position;
        public override void HearPlayer(bool rude)
        {
            if (Dead || !Player) return; headTarget = Player.transform.position + Vector3.up * 1.5f; Invoke(nameof(ClearHead), 2.5f);
            ReplyLater(rude ? Barks.Pick("Easy, boss.", "Ouch.", "Yes, boss... sorry, boss.", "Whatever you say.") : Barks.Pick("Boss.", "Yes, boss?", "All good here.", "Ready when you are."), 0.6f, Barks.Gang, !rude && Random.value < 0.4f);
        }

        /// <summary>This member's place behind the boss: two by two, a step apart.</summary>
        Vector3 Slot()
        {
            if (role == Role.Follow) { if (!followers.Contains(this)) followers.Add(this); } else followers.Remove(this);
            followers.RemoveAll(f => !f || f.Dead);
            int i = Mathf.Max(0, followers.IndexOf(this)); var p = Player.transform;
            float side = (i % 2 == 0 ? -1f : 1f) * (1.3f + (i / 2) * 0.4f), back = 1.8f + (i / 2) * 1.5f;
            Vector3 fwd = p.forward; fwd.y = 0f; fwd.Normalize(); Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 s = p.position - fwd * back + right * side;
            return NavMesh.SamplePosition(s, out var h, 2f, NavMesh.AllAreas) ? h.position : p.position - fwd * 2f;
        }

        protected override void Think()
        {
            var fm = FactionManager.I; if (!Player) return;
            float dt = thinkDt; float dp = DistToPlayer; swingCd -= dt; callCd -= dt; spotCd -= dt;
            // enemies: members of crews that are hostile to the player, in sight
            if (target == null || target.Dead || Vector3.Distance(target.transform.position, transform.position) > 34f) { if (target != null && target.Dead && Random.value < 0.5f) Bark(Barks.Pick("Got him.", "He's down.", "One less."), Barks.Gang, 1.8f); target = null; faceWhileMoving = null; }
            if (target == null && fm != null && Random.value < 0.35f)
            {
                float bd = 22f * 22f;
                foreach (var b in CharacterBody.All)
                {
                    if (!b || b.Dead || b == body || b.isPlayer) continue; var fb = b.GetComponent<FactionBrain>(); if (!fb || fb.Def == null || !fm.Hostile(fb.Def)) continue;
                    float d = (b.transform.position - transform.position).sqrMagnitude; if (d < bd && CanSee(b, 22f, 200f)) { bd = d; target = b; }
                }
                if (target != null && spotCd <= 0f) { spotCd = 15f; Bark(Barks.Pick("Boss, trouble!", "Heads up!", "We've got company!", "Over there!"), Barks.Gang, 2f, false, 34f); }
            }
            if (target != null) { pose = ArmPose.None; nod = 0f; Fight(dt); return; }
            if (gun) ShowGun(false);
            switch (role)
            {
                case Role.Follow:
                    {
                        Vector3 slot = Slot(); float ds = Vector3.Distance(transform.position, slot);
                        bool bossRuns = Player.IsRunning && Player.Moving, bossMoves = Player.Moving;
                        if (dp > 70f && NavMeshWarp()) { }
                        else if (dp > 14f || ds > 6f) { GoTo(slot, runSpeed); SetState("move"); pose = ArmPose.None; }
                        else if (ds > 0.9f && (bossMoves || ds > 2.2f)) { GoTo(slot, bossRuns || ds > 4f ? runSpeed * 0.95f : Mathf.Max(walkSpeed, Player.Velocity.magnitude * 1.1f)); SetState("move"); pose = ArmPose.None; nod = 0f; idleClip = "Idle"; }
                        else if (State != "idle") { Stop(); SetState("idle"); chatT = Random.Range(1f, 3f); }
                        if (State == "idle") IdleAround(dt);
                    }
                    break;
                case Role.Guard:
                case Role.Dealer:
                    if (Vector3.Distance(transform.position, post) > 2.5f) { GoTo(post, walkSpeed); SetState("move"); pose = ArmPose.None; }
                    else
                    {
                        if (State != "idle") { Stop(); SetState("idle"); }
                        chatT -= dt;
                        if (chatT <= 0f)
                        {
                            chatT = Random.Range(4f, 9f); float r = Random.value;
                            if (role == Role.Guard) { pose = r < 0.6f ? ArmPose.Crossed : ArmPose.None; idleClip = "Idle"; }
                            else { pose = r < 0.3f ? ArmPose.Hips : r < 0.45f ? ArmPose.PhoneText : ArmPose.None; headDown = pose == ArmPose.PhoneText; idleClip = r > 0.8f ? "Talk" : "Idle"; }
                        }
                        // dealers call out to people walking past
                        if (role == Role.Dealer && callCd <= 0f)
                            foreach (var b in CharacterBody.All)
                            {
                                if (!b || b.Dead || b == body || b.isPlayer || (b.transform.position - transform.position).sqrMagnitude > 4.5f * 4.5f) continue; if (!b.GetComponent<CivilianBrain>()) continue;
                                callCd = Random.Range(10f, 20f); headTarget = b.transform.position + Vector3.up * 1.5f; Invoke(nameof(ClearHead), 2f);
                                Bark(Barks.Pick("Psst. Need something?", "Hey, friend. Good stuff here.", "Looking to party?", "Best prices in town."), null, 2.2f); break;
                            }
                    }
                    break;
            }
        }
        void ClearHead() { headTarget = null; }

        /// <summary>The boss stopped: look about, shift weight, and now and then exchange a word with another member.</summary>
        void IdleAround(float dt)
        {
            chatT -= dt;
            if (chatT > 0f) return;
            chatT = Random.Range(3f, 7f); float r = Random.value; nod = 0f; idleClip = "Idle"; pose = ArmPose.None; lookAt = null; headTarget = null;
            CrewBrain mate = null; foreach (var f in followers) if (f && f != this && !f.Dead && f.State == "idle" && (f.transform.position - transform.position).sqrMagnitude < 4f * 4f) { mate = f; break; }
            if (mate && r < 0.3f) { lookAt = mate.transform.position; headTarget = mate.transform.position + Vector3.up * 1.5f; idleClip = "Talk"; if (CamDist(transform.position) < 18f && Random.value < 0.5f) Bark(Barks.Pick("Quiet tonight.", "What's the boss planning?", "I could eat.", "You see that guy?", "Stay sharp."), Barks.Gang, 2.2f); }
            else if (r < 0.5f) { Vector3 away = transform.position - Player.transform.position; away.y = 0f; if (away.sqrMagnitude > 0.01f) lookAt = transform.position + Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * away.normalized * 5f; }
            else if (r < 0.65f && !Armed) pose = ArmPose.Crossed;
            else if (r < 0.75f) lookAt = Player.transform.position;
        }

        bool NavMeshWarp()
        {
            Vector3 p = Player.transform.position - Player.transform.forward * 3f;
            if (NavMesh.SamplePosition(p, out var h, 6f, NavMesh.AllAreas)) { agent.Warp(h.position); return true; }
            return false;
        }

        void Fight(float dt)
        {
            float tdist = Vector3.Distance(transform.position, target.transform.position); bool see = CanSee(target, 30f, 360f); headTarget = target.ChestPos;
            if (Ranged)
            {
                ShowGun(true);
                if (tdist < 24f && see)
                {
                    lookAt = target.transform.position; faceWhileMoving = target.transform.position; idleClip = "Pistol_Aim";
                    strafeT -= dt; if (strafeT <= 0f) { strafeT = Random.Range(2f, 4f); if (Random.value < 0.5f && StrafePoint(target.transform.position, out var sp)) GoTo(sp, walkSpeed * 1.1f); else Stop(); }
                    if (Vector3.Angle(transform.forward, target.transform.position - transform.position) < 25f) ShootAt(target, (weapon == WeaponKind.Rifle ? 0.55f : weapon == WeaponKind.Shotgun ? 0.5f : 0.4f) * (Moving ? 0.8f : 1f), "crew");
                }
                else { faceWhileMoving = null; idleClip = "Idle"; GoTo(target.transform.position, runSpeed); }
            }
            else
            {
                faceWhileMoving = null; if (weapon == WeaponKind.Bat || weapon == WeaponKind.Knife) ShowGun(true);
                if (tdist < 1.9f) { Stop(); lookAt = target.transform.position; if (swingCd <= 0f) { swingCd = 1.2f; anim.PlayUpper(weapon == WeaponKind.Knife ? "Knife_Attack" : "Bat_Attack"); Invoke(nameof(Swing), 0.42f); } }
                else GoTo(target.transform.position, runSpeed);
            }
        }

        void Swing()
        {
            if (!target || target.Dead || body.Dead || Vector3.Distance(target.transform.position, transform.position) > 2.4f) return;
            Vector3 dir = (target.transform.position - transform.position).normalized; var pt = target.ChestPos;
            CombatFx.I?.Blood(pt, dir, 4); Sfx.At("thud", pt); target.Hit("torso", pt, dir, 1, false, 1.5f, "crew", false);
        }
    }
}
