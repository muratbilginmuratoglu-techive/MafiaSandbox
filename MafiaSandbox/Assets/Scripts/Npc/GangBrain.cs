using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;

namespace MafiaSandbox.Npc
{
    /// <summary>
    /// The crew at the southern construction site: loiter around their spot, warn the player who comes close,
    /// attack with bats when the player gets too close or hurts one of them; all members alert together; they give up
    /// once the player is far away and drift back.
    /// </summary>
    public class GangBrain : NpcBrain
    {
        public Vector3 homeSpot; public float warnRadius = 14f, attackRadius = 7.5f, giveUpRadius = 45f;
        public static float alertUntil;          // shared alarm (Time.time)
        float swingCd, warnedAt = -99f;

        protected override void OnSpawned() { EquipGun(WeaponKind.Bat, true); SetState("loiter"); idleClip = "Bat_Idle"; homeSpot = spawnPos; }
        protected override Vector3 RespawnPoint() => homeSpot;

        protected override void OnHit(string zone, Vector3 point) { alertUntil = Time.time + 30f; }
        protected override void OnDied(string by) { if (by == "player") alertUntil = Time.time + 40f; }
        protected override void OnNoise(Vector3 pos, float radius, GameObject src, bool melee)
        {
            if (Player && src == Player.gameObject && Vector3.Distance(pos, homeSpot) < 30f) alertUntil = Time.time + 25f;
        }

        protected override void Think()
        {
            if (!Player || !PlayerBody) return;
            float dp = DistToPlayer; bool see = CanSeePlayer(30f, 360f);
            bool alert = Time.time < alertUntil;
            switch (State)
            {
                case "loiter":
                    idleClip = "Bat_Idle";
                    if ((alert && dp < giveUpRadius) || (see && dp < attackRadius && !PlayerInVehicle)) { Attack(); break; }
                    if (see && dp < warnRadius && !PlayerInVehicle) { SetState("warn"); Stop(); lookAt = Player.transform.position; if (Time.time - warnedAt > 12f) { warnedAt = Time.time; GameManager.I?.Say($"{displayName}: Hey! Private property, get lost."); } break; }
                    if (stateT > Random.Range(6f, 14f)) { var p = homeSpot + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f)); GoTo(p, walkSpeed * 0.8f); stateT = 0f; }
                    break;
                case "warn":
                    idleClip = "Bat_Idle"; lookAt = Player.transform.position;
                    if ((alert && dp < giveUpRadius) || (dp < attackRadius && !PlayerInVehicle)) { Attack(); break; }
                    if (dp > warnRadius + 4f || !see) { SetState("loiter"); }
                    break;
                case "attack":
                    idleClip = "Bat_Idle";
                    if (PlayerBody.Dead || dp > giveUpRadius || (PlayerInVehicle && dp > 10f)) { SetState("loiter"); GoTo(homeSpot, walkSpeed); break; }
                    if (dp < 1.9f && !PlayerInVehicle)
                    {
                        Stop(); lookAt = Player.transform.position;
                        if (swingCd <= 0f) { swingCd = 1.2f; anim.PlayUpper("Bat_Attack"); Invoke(nameof(Swing), 0.42f); }
                    }
                    else if (Arrived(1.5f) || stateT % 1f < 0.25f) GoTo(Player.transform.position, runSpeed);
                    break;
            }
            swingCd -= 0.2f;
        }

        void Attack() { if (State != "attack") { SetState("attack"); alertUntil = Mathf.Max(alertUntil, Time.time + 20f); GoTo(Player.transform.position, runSpeed); } }

        void Swing()
        {
            if (!PlayerBody || PlayerBody.Dead || body.Dead || DistToPlayer > 2.4f) { Sfx.At("swing", transform.position, 0.5f); return; }
            Vector3 dir = (Player.transform.position - transform.position).normalized;
            Vector3 point = PlayerBody.ChestPos;
            CombatFx.I?.Blood(point, dir, 4); Sfx.At("thud", point);
            bool killed = PlayerBody.Hit("torso", point, dir, 1, false, 1.5f, "gang", false);
            GameManager.I?.PlayerHurt();
        }
    }
}
