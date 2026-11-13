using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.World;

namespace MafiaSandbox.Npc
{
    /// <summary>
    /// Shopkeepers stand behind their counter. A gun pointed at them from close range makes them raise their hands
    /// (the robbery itself — register cash, silent alarm — comes with the economy in step 5). If shot, they flee the shop.
    /// </summary>
    public class KeeperBrain : NpcBrain
    {
        public BuildingInfo building;
        float heldUpT;

        protected override void OnSpawned() { SetState("keeper"); idleClip = "Idle"; Stop(); lookAt = building ? building.counter + (building.door - building.counter).normalized * 3f : (Vector3?)null; }
        protected override Vector3 RespawnPoint() => spawnPos;

        protected override void OnHit(string zone, Vector3 point) { Flee(point); }
        protected override void OnNoise(Vector3 pos, float radius, GameObject src, bool melee)
        {
            if (State == "dead" || melee) return;
            if (building && building.footprint.Contains(pos)) Flee(pos);   // shots inside the shop
        }

        void Flee(Vector3 from)
        {
            var p = EscapePoint(from, 25f, true);
            if (p.HasValue && GoTo(p.Value, runSpeed)) { SetState("flee"); GameManager.I?.Say($"{displayName}: Don't shoot! Take whatever you want!"); }
        }

        protected override void Think()
        {
            var pc = Player ? Player.GetComponent<PlayerCombat>() : null;
            bool gunOnMe = pc && pc.Aiming && !pc.Weapon.melee && DistToPlayer < 7f && CanSeePlayer(8f, 160f)
                           && Vector3.Angle(Player.transform.forward, transform.position - Player.transform.position) < 30f;
            switch (State)
            {
                case "keeper":
                    idleClip = "Idle";
                    if (gunOnMe) { SetState("handsup"); heldUpT = 0f; GameManager.I?.Say($"{displayName}: Don't shoot! Take whatever you want!"); GameManager.I?.OnKeeperHeldUp(this); }
                    else if (DistToPlayer < 4f && CanSeePlayer(6f, 120f) && stateT > 10f) { stateT = 0f; GameManager.I?.Say($"{displayName}: Welcome, how can I help?"); }
                    break;
                case "handsup":
                    idleClip = "Handsup"; lookAt = Player ? Player.transform.position : lookAt;
                    heldUpT = gunOnMe ? 0f : heldUpT + 0.2f;
                    if (heldUpT > 6f) { SetState("keeper"); idleClip = "Idle"; }
                    break;
                case "flee":
                    idleClip = "Idle";
                    if (Arrived() || stateT > 10f) { Stop(); SetState("hidden"); idleClip = "Crouch_Idle"; }
                    break;
                case "hidden":
                    if (stateT > 25f) { SetState("return"); GoTo(spawnPos, walkSpeed); }
                    break;
                case "return":
                    idleClip = "Idle";
                    if (Arrived(0.5f) || stateT > 40f) { agent.Warp(spawnPos); transform.rotation = Quaternion.Euler(0f, spawnYaw, 0f); OnSpawned(); }
                    break;
            }
        }
    }
}
