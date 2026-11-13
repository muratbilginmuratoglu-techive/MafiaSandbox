using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Player;
using MafiaSandbox.UI;
using MafiaSandbox.Vehicles;

namespace MafiaSandbox.Economy
{
    /// <summary>F / G interactions on foot: take or buy items (G = steal), trade at a counter, search a body. Also builds the prompt text.</summary>
    public class PlayerInteraction : MonoBehaviour
    {
        public enum Kind { None, Item, Counter, Body }
        public Kind TargetKind { get; private set; }
        public Pickable TargetItem { get; private set; }
        public Shop TargetShop { get; private set; }
        public CharacterBody TargetBody { get; private set; }
        public string Prompt { get; private set; } = "";
        public float LootT { get; private set; }

        PlayerController pc; VehicleInteraction vi; CharacterAnimator anim; CharacterBody body;

        void Awake() { pc = GetComponent<PlayerController>(); vi = GetComponent<VehicleInteraction>(); anim = GetComponent<CharacterAnimator>(); body = GetComponent<CharacterBody>(); }

        void Update()
        {
            var eco = EconomyManager.I; var gm = GameManager.I; if (!eco || !gm) return;
            LootT -= Time.deltaTime;
            Find(eco);
            if (GameUI.Blocking || pc.InputLocked || (body && body.Dead) || (vi && vi.Current) || gm.Arrested) return;
            if (Input.GetKeyDown(KeyCode.F)) Do(eco, false);
            else if (Input.GetKeyDown(KeyCode.G)) Do(eco, true);
        }

        void Find(EconomyManager eco)
        {
            TargetKind = Kind.None; TargetItem = null; TargetShop = null; TargetBody = null; Prompt = "";
            if ((body && body.Dead) || (vi && vi.Current) || GameManager.I.Arrested) return;
            Vector3 pp = transform.position; Vector3 f = transform.forward; float best = 9f;
            void Consider(Kind k, Vector3 pos, float range, Pickable pk, Shop sh, CharacterBody cb)
            {
                float d = Vector3.Distance(pos, pp); if (d > range) return;
                Vector3 to = pos - pp; to.y = 0f; to.Normalize(); float score = d - Vector3.Dot(to, f) * 0.6f;
                if (score < best) { best = score; TargetKind = k; TargetItem = pk; TargetShop = sh; TargetBody = cb; }
            }
            foreach (var pk in eco.pickables) if (pk && !pk.taken && (pk.transform.position - pp).sqrMagnitude < 9f) Consider(Kind.Item, pk.transform.position, 2.1f, pk, pk.shop, null);
            foreach (var s in eco.shops) if (s.kind != "house" && s.kind != "home" && s.kind != "police" && (s.info.counter - pp).sqrMagnitude < 9f) Consider(Kind.Counter, s.info.counter, 2.3f, null, s, null);
            foreach (var cb in CharacterBody.All) if (cb && cb.Dead && cb.Ragdolled && !cb.Looted && cb != body && (cb.transform.position - pp).sqrMagnitude < 16f) Consider(Kind.Body, cb.ChestPos, 2.2f, null, null, cb);

            switch (TargetKind)
            {
                case Kind.Item:
                    {
                        var pk = TargetItem; var b = pk.shop;
                        if (pk.id == "Para") Prompt = "F  Take: cash bundle";
                        else if (b && b.kind == "home") Prompt = $"F  Take: {pk.def.name} (yours)";
                        else if (eco.FreeTake(pk)) Prompt = $"F  Take: {pk.def.name} ({(b ? "counts as theft" : "from the ground")})";
                        else Prompt = $"F  Buy: {pk.def.name} ${b.BuyPrice(pk.id)}    G  Steal";
                        break;
                    }
                case Kind.Counter: Prompt = TargetShop.kind == "bank" ? "F  Bank: talk to the teller" : $"F  {TargetShop.label}: trade"; break;
                case Kind.Body: Prompt = $"F  Search body: {NameOf(TargetBody)}"; break;
            }
        }

        static string NameOf(CharacterBody b) { var br = b.GetComponent<Npc.NpcBrain>(); return br ? br.displayName : b.name; }

        void Do(EconomyManager eco, bool steal)
        {
            switch (TargetKind)
            {
                case Kind.Item: eco.TakeItem(TargetItem, steal); if (anim) anim.PlayUpper("Take"); break;
                case Kind.Counter: if (!steal) { if (TargetShop.kind == "bank") GameManager.I.Say("Teller: We don't deal with your kind here. The vault is at the back… not that you'd dare."); else GameUI.I?.OpenShop(TargetShop); } break;
                case Kind.Body: if (!steal && LootT <= 0f) { LootT = 0.9f; if (anim) anim.PlayUpper("Take"); var cb = TargetBody; Invoke(nameof(FinishLoot), 0.7f); pendingLoot = cb; } break;
            }
        }
        CharacterBody pendingLoot;
        void FinishLoot() { if (pendingLoot && !pendingLoot.Looted) EconomyManager.I?.LootBody(pendingLoot); pendingLoot = null; }
    }
}
