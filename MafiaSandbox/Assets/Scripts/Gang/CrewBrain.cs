using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Factions;
using MafiaSandbox.Npc;

namespace MafiaSandbox.Gang
{
    /// <summary>One of the player's own crew: follows, guards a venue or deals on a corner; fights hostile gang members; fires whatever it was armed with.</summary>
    public class CrewBrain : NpcBrain
    {
        public enum Role { Follow, Guard, Dealer }
        public Role role = Role.Follow; public WeaponKind? weapon; public Vector3 post; public int wage = 40; public int sales;
        CharacterBody target; float swingCd; string[] chat = { "Talk", "Idle", "Idle", "Wave" }; float chatT;

        public void SetWeapon(WeaponKind? k) { weapon = k; if (k.HasValue) { EquipGun(k.Value, false); } else { if (gun) Destroy(gun); gun = null; } }
        public bool Armed => weapon.HasValue;
        bool Ranged => weapon.HasValue && weapon != WeaponKind.Bat && weapon != WeaponKind.Knife;
        public void SetRole(Role r) { role = r; if (r != Role.Follow) post = transform.position; }

        protected override void OnSpawned() { SetState("idle"); idleClip = "Idle"; if (weapon.HasValue) EquipGun(weapon.Value, false); }
        protected override void OnDied(string by) { CrewManager.I?.OnMemberDied(this); Destroy(this, 2f); }
        protected override void OnHit(string zone, Vector3 point) { }
        protected override Vector3 RespawnPoint() => transform.position;

        protected override void Think()
        {
            var fm = FactionManager.I; if (!Player) return;
            float dp = DistToPlayer;
            // enemies: members of crews that are hostile to the player, in sight
            if (target == null || target.Dead || Vector3.Distance(target.transform.position, transform.position) > 34f) target = null;
            if (target == null && fm != null && (Time.frameCount % 3 == 0))
            {
                float bd = 22f * 22f;
                foreach (var b in CharacterBody.All)
                {
                    if (!b || b.Dead || b == body || b.isPlayer) continue; var fb = b.GetComponent<FactionBrain>(); if (!fb || fb.Def == null || !fm.Hostile(fb.Def)) continue;
                    float d = (b.transform.position - transform.position).sqrMagnitude; if (d < bd && CanSee(b, 22f, 200f)) { bd = d; target = b; }
                }
            }
            if (target != null) { Fight(); return; }
            if (gun) ShowGun(false);
            idleClip = "Idle";
            switch (role)
            {
                case Role.Follow:
                    if (dp > 14f) { GoTo(Player.transform.position, runSpeed); SetState("move"); }
                    else if (dp > 4.5f) { GoTo(Player.transform.position - Player.transform.forward * 2f, dp > 8f ? runSpeed * 0.8f : walkSpeed); SetState("move"); }
                    else { Stop(); lookAt = Player.transform.position; SetState("idle"); }
                    if (dp > 70f && NavMeshWarp()) { }
                    break;
                case Role.Guard:
                case Role.Dealer:
                    if (Vector3.Distance(transform.position, post) > 2.5f) { GoTo(post, walkSpeed); SetState("move"); }
                    else { Stop(); SetState("idle"); chatT -= 0.2f; if (chatT <= 0f) { chatT = Random.Range(4f, 9f); idleClip = chat[Random.Range(0, chat.Length)]; } lookAt = null; }
                    break;
            }
        }

        bool NavMeshWarp()
        {
            Vector3 p = Player.transform.position - Player.transform.forward * 3f;
            if (UnityEngine.AI.NavMesh.SamplePosition(p, out var h, 6f, UnityEngine.AI.NavMesh.AllAreas)) { agent.Warp(h.position); return true; }
            return false;
        }

        void Fight()
        {
            float dt = Vector3.Distance(transform.position, target.transform.position); bool see = CanSee(target, 30f, 360f); swingCd -= 0.2f;
            if (Ranged)
            {
                ShowGun(true);
                if (dt < 24f && see) { Stop(); lookAt = target.transform.position; idleClip = "Pistol_Aim"; if (Vector3.Angle(transform.forward, target.transform.position - transform.position) < 25f) ShootAt(target, weapon == WeaponKind.Rifle ? 0.55f : weapon == WeaponKind.Shotgun ? 0.5f : 0.4f, "crew"); }
                else GoTo(target.transform.position, runSpeed);
            }
            else
            {
                if (weapon == WeaponKind.Bat) ShowGun(true);
                if (dt < 1.9f) { Stop(); lookAt = target.transform.position; if (swingCd <= 0f) { swingCd = 1.2f; anim.PlayUpper(weapon == WeaponKind.Knife ? "Knife_Attack" : "Bat_Attack"); Invoke(nameof(Swing), 0.42f); } }
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
