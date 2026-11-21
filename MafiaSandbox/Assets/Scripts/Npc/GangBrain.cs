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

        protected override bool AllowGlance => State == "loiter";
        protected override void OnSpawned() { EquipGun(WeaponKind.Bat, true); SetState("loiter"); idleClip = "Bat_Idle"; homeSpot = spawnPos; }
        protected override Vector3 RespawnPoint() => homeSpot;

        protected override void OnHit(string zone, Vector3 point) { alertUntil = Time.time + 30f; }
        public override void HearPlayer(bool rude)
        {
            if (Dead || !Player || State == "attack") return; headTarget = Player.transform.position + Vector3.up * 1.5f;
            if (!rude) { ReplyLater(Barks.Pick("This is a construction site, pal.", "Yeah, hi. Now beat it.", "What do you want?"), 0.7f, Barks.Gang); return; }
            _ = HeckleCount; heckled++; heckleAt = Time.time;
            if (heckled >= 2) { ReplyLater("Oh, you want trouble?", 0.6f, Barks.Gang); alertUntil = Time.time + 20f; return; }
            ReplyLater(Barks.Pick("Say that again.", "You lost, smart guy?", "Careful."), 0.7f, Barks.Gang);
        }
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
                    idleClip = "Bat_Idle"; headTarget = null;
                    if ((alert && dp < giveUpRadius) || (see && dp < attackRadius && !PlayerInVehicle)) { Attack(); break; }
                    if (see && dp < warnRadius && !PlayerInVehicle) { SetState("warn"); Stop(); lookAt = Player.transform.position; if (Time.time - warnedAt > 12f) { warnedAt = Time.time; Bark(Barks.Pick("Hey! Private property, get lost.", "Construction site. Beat it.", "You lost, pal?"), Barks.Gang, 2.6f, true); } break; }
                    if (stateT > Random.Range(6f, 14f)) { var p = homeSpot + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f)); GoTo(p, walkSpeed * 0.8f); stateT = 0f; }
                    break;
                case "warn":
                    idleClip = "Bat_Idle"; lookAt = Player.transform.position; headTarget = Player.transform.position + Vector3.up * 1.5f;
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
                    else if (Arrived(1.5f) || stateT % 1f < 0.25f) { Vector3 tp = Player.transform.position; if (dp < 7f) { var ring = tp + Quaternion.Euler(0f, (uid % 6) * 60f, 0f) * Vector3.forward * 1.4f; if (UnityEngine.AI.NavMesh.SamplePosition(ring, out var rh, 1.2f, UnityEngine.AI.NavMesh.AllAreas)) tp = rh.position; } GoTo(tp, runSpeed); }
                    headTarget = PlayerBody.ChestPos;
                    break;
            }
            swingCd -= 0.2f;
        }

        void Attack() { if (State != "attack") { SetState("attack"); Bark(Barks.Pick("Get him!", "That's it!", "You asked for it!"), Barks.Gang, 1.8f); alertUntil = Mathf.Max(alertUntil, Time.time + 20f); GoTo(Player.transform.position, runSpeed); } }

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
