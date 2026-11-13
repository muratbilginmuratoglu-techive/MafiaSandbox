using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.World;

namespace MafiaSandbox.Npc
{
    /// <summary>
    /// Shopkeepers stand behind their counter (role "keeper"); residents stand in their house (role "owner").
    /// A gun pointed at a keeper from close range makes them raise their hands; keep it on them for a couple of seconds and
    /// they hand over the register (silent alarm → police). Stop aiming and they calm down (and still call the police).
    /// A resident who sees the player inside the house raises the alarm and flees. If shot, keepers flee the shop.
    /// </summary>
    public class KeeperBrain : NpcBrain
    {
        public BuildingInfo building;
        public Shop shop;
        public string role = "keeper";
        float heldUpT, robT, calmT, waveT; bool gaveCash, greeted;

        protected override void OnSpawned() { SetState("keeper"); idleClip = "Idle"; Stop(); gaveCash = false; lookAt = building ? building.counter + (building.door - building.counter).normalized * 3f : (Vector3?)null; }
        protected override Vector3 RespawnPoint() => spawnPos;

        protected override void OnHit(string zone, Vector3 point) { Flee(point); }
        protected override void OnNoise(Vector3 pos, float radius, GameObject src, bool melee)
        {
            if (State == "dead" || melee) return;
            if (building && building.footprint.Contains(pos)) Flee(pos);   // shots inside the shop
        }

        /// <summary>Saw the player pocket something: shout and wave (the alarm itself is raised by the economy).</summary>
        public void Startled() { waveT = 3f; }

        void Flee(Vector3 from)
        {
            var p = EscapePoint(from, 25f, true);
            if (p.HasValue && GoTo(p.Value, runSpeed)) { SetState("flee"); if (role == "keeper") GameManager.I?.Say($"{displayName}: Don't shoot! Take whatever you want!"); }
            else { SetState("hidden"); Stop(); idleClip = "Crouch_Idle"; }
        }

        bool GunOnMe()
        {
            var pc = Player ? Player.GetComponent<PlayerCombat>() : null;
            return pc && pc.Aiming && !pc.Weapon.melee && DistToPlayer < 8f && CanSeePlayer(9f, 170f)
                   && Vector3.Angle(Player.transform.forward, transform.position - Player.transform.position) < Mathf.Max(12f, Mathf.Atan2(0.9f, Mathf.Max(0.5f, DistToPlayer)) * Mathf.Rad2Deg);
        }

        protected override void Think()
        {
            bool gunOnMe = GunOnMe(); float dt = 0.2f;
            waveT -= dt;
            switch (State)
            {
                case "keeper":
                    idleClip = waveT > 0f ? "Wave" : "Idle";
                    if (role == "keeper" && gunOnMe) { robT += dt; if (robT > 1.2f) { robT = 0f; SetState("handsup"); heldUpT = 0f; calmT = 0f; gaveCash = false; GameManager.I?.Say($"{displayName}: Don't shoot! Take whatever you want!"); } }
                    else robT = 0f;
                    if (role == "keeper")
                    {
                        if (DistToPlayer < 3f && CanSeePlayer(6f, 120f)) { if (!greeted) { greeted = true; waveT = 1.5f; GameManager.I?.Say($"{displayName}: Welcome, how can I help?"); } }
                        else if (DistToPlayer > 8f) greeted = false;
                    }
                    else if (role == "owner" && shop && Player && shop.Contains(Player.transform.position) && CanSeePlayer(14f, 170f) && !(PlayerBody && PlayerBody.Dead))
                    {
                        GameManager.I?.Say($"{displayName}: THIEF! What are you doing in my house?!");
                        EconomyManager.I?.WitnessCrime(shop, Player.transform.position, 1); Flee(Player.transform.position);
                    }
                    break;
                case "handsup":
                    idleClip = "Handsup"; lookAt = Player ? Player.transform.position : lookAt;
                    if (gunOnMe) { calmT = 0f; heldUpT += dt; }
                    else { calmT += dt; if (calmT > 3f) { calmT = 0f; SetState("keeper"); idleClip = "Idle"; if (shop) EconomyManager.I?.WitnessCrime(shop, Player ? Player.transform.position : transform.position, 1); break; } }
                    if (gunOnMe && heldUpT > 2.5f && !gaveCash && shop) { gaveCash = true; EconomyManager.I?.Rob(shop, this); Invoke(nameof(FleeFromPlayer), 1.5f + Random.value * 2f); }
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

        void FleeFromPlayer() { if (!Dead && State == "handsup") Flee(Player ? Player.transform.position : transform.position); }
    }
}
