using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Factions;
using MafiaSandbox.Npc;
using MafiaSandbox.Player;
using MafiaSandbox.UI;
using MafiaSandbox.Vehicles;

namespace MafiaSandbox.Gang
{
    /// <summary>Sal's side jobs (phone ▸ Jobs): smuggling runs, stolen cars to the chop shop (the pawn shop garage) and contract hits. One job at a time.</summary>
    public class JobManager : MonoBehaviour
    {
        public static JobManager I { get; private set; }
        public class Job { public string type, title, desc; public int reward, xp; public float limit; }
        public readonly List<Job> Offers = new List<Job>();
        public Job Active { get; private set; }
        int stage; float deadline; Vector3 pickup, drop; CharacterBody hitTarget; string hitName; VehicleController chopCar; GameObject beacon; float offerT = -999f; PlayerController pc;

        void Awake() { I = this; }

        List<Shop> Venues() { var l = new List<Shop>(); if (EconomyManager.I) foreach (var s in EconomyManager.I.shops) if (CrewManager.Eligible(s) || s.kind == "bank") l.Add(s); return l; }
        Shop Pawn() { if (EconomyManager.I) foreach (var s in EconomyManager.I.shops) if (s.kind == "pawn") return s; return null; }

        void Refresh()
        {
            offerT = Time.time; Offers.Clear();
            Offers.Add(new Job { type = "courier", title = "Smuggling run", desc = "Pick up a package from one shop's back door and deliver it across town within 4 minutes. No questions.", reward = Random.Range(250, 450), xp = 25, limit = 240f });
            Offers.Add(new Job { type = "chop", title = "Chop shop order", desc = "Steal any car and drive it to the pawn shop garage. Park it at the door.", reward = Random.Range(350, 600), xp = 35 });
            Offers.Add(new Job { type = "hit", title = "Contract", desc = "Sal marks a target. Make it quiet — a cop who sees it makes it your problem.", reward = Random.Range(600, 900), xp = 60 });
        }

        public bool Accept(Job j)
        {
            var gm = GameManager.I; if (Active != null) { gm.Say("Finish or drop your current job first."); return false; }
            if (j.type == "hit" && Respect.Rank < 1) { gm.Say("Sal: I don't hand contracts to Street Rats."); return false; }
            var venues = Venues();
            if (j.type == "courier")
            {
                if (venues.Count < 2) return false; var a = venues[Random.Range(0, venues.Count)]; Shop b = null; float bd = 0f; foreach (var s in venues) { float d = Vector3.Distance(s.info.door, a.info.door); if (d > bd && Random.value < 0.6f) { bd = d; b = s; } }
                if (!b || b == a) b = venues[Random.Range(0, venues.Count)]; pickup = a.info.door; drop = b.info.door; stage = 0; j.desc = $"Pick up the package at the {a.label}, deliver to the {b.label}.";
            }
            else if (j.type == "chop") { var p = Pawn(); if (p == null) return false; drop = p.info.door; stage = 0; chopCar = null; }
            else if (j.type == "hit")
            {
                CharacterBody best = null; float bd = 1e9f; var pp = gm.player.transform.position;
                foreach (var b in CharacterBody.All) { if (!b || b.Dead || b.isPlayer) continue; var nb = b.GetComponent<NpcBrain>(); if (!(nb is CivilianBrain || nb is FactionBrain) || (nb is CivilianBrain cb && cb.isMedic)) continue; float d = Vector3.Distance(b.transform.position, pp); if (d > 30f && d < bd && Random.value < 0.4f) { bd = d; best = b; } }
                if (!best) { gm.Say("Sal: Nobody to hit right now. Check back."); return false; }
                hitTarget = best; hitName = best.GetComponent<NpcBrain>().displayName; stage = 0; j.desc = $"Target: {hitName}. Follow the marker.";
            }
            Active = j; Offers.Remove(j); deadline = j.limit > 0f ? Time.time + j.limit : 0f;
            gm.Say($"Job accepted: {j.title}"); PhonePanel.Post("sal", j.type == "courier" ? "Package is waiting. Don't open it." : j.type == "chop" ? "Any car will do. Bring it to the pawn shop garage." : $"The target is {hitName}. I'll be waiting for the news.");
            return true;
        }
        public void Abandon() { if (Active != null) { GameManager.I?.Say("Job dropped."); Active = null; SetBeacon(null); } }

        void Finish(bool ok, string why = null)
        {
            var gm = GameManager.I; var eco = EconomyManager.I; var j = Active; Active = null; SetBeacon(null); if (j == null) return;
            if (ok)
            {
                eco.cash += j.reward; var cm = CrewManager.I; if (cm) { cm.JobsDone++; if (j.type == "chop") cm.Chopped++; if (j.type == "hit") cm.Hits++; }
                Respect.Add(j.xp); gm.Banner("JOB DONE", $"{j.title}   +${j.reward}", 3f); gm.Say($"Job complete: {j.title} (+${j.reward})"); PhonePanel.Post("sal", "Clean work. Payment sent.");
            }
            else { gm.Say(why ?? "Job failed."); PhonePanel.Post("sal", "You blew it. I'll find someone more reliable."); }
        }

        Vector3? Target()
        {
            if (Active == null) return null;
            switch (Active.type)
            {
                case "courier": return stage == 0 ? pickup : drop;
                case "chop": return stage == 1 ? drop : (Vector3?)null;
                case "hit": return hitTarget ? hitTarget.transform.position : (Vector3?)null;
            }
            return null;
        }

        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player) return; pc = gm.player;
            if (CharacterCreator.IsOpen || IntroCinematic.Playing) return;
            if (Time.time - offerT > 300f || Offers.Count == 0) Refresh();
            if (Active == null) { SetBeacon(null); return; }
            var p = pc.transform.position; var vi = pc.GetComponent<VehicleInteraction>();
            if (deadline > 0f && Time.time > deadline) { Finish(false, "Time's up."); return; }
            switch (Active.type)
            {
                case "courier":
                    if (stage == 0 && Vector3.Distance(p, pickup) < 5f) { stage = 1; gm.Say("Got the package. Deliver it."); }
                    else if (stage == 1 && Vector3.Distance(p, drop) < 5f) Finish(true);
                    break;
                case "chop":
                    if (stage == 0 && vi && vi.Current) { stage = 1; chopCar = vi.Current; gm.Say("Nice ride. Take it to the pawn shop garage."); }
                    else if (stage == 1)
                    {
                        if (!vi || !vi.Current) { stage = 0; break; }
                        if (Vector3.Distance(p, drop) < 9f && vi.Current.Speed < 4f) { var car = vi.Current; vi.Exit(); Finish(true); StartCoroutine(Scrap(car)); }
                    }
                    break;
                case "hit":
                    if (!hitTarget) { Finish(false, "The target is gone."); break; }
                    if (hitTarget.Dead) { if (hitTarget.KilledBy == "player" || hitTarget.KilledBy == "crew") Finish(true); else Finish(false, "Somebody else got to the target first."); }
                    break;
            }
            SetBeacon(Target(), Active.type == "hit" ? new Color(0.95f, 0.2f, 0.2f, 0.35f) : new Color(0.25f, 0.8f, 0.95f, 0.35f));
        }

        System.Collections.IEnumerator Scrap(VehicleController car) { yield return new WaitForSeconds(2.5f); if (car) Destroy(car.gameObject); GameManager.I?.Say("The garage stripped the car for parts."); }

        void SetBeacon(Vector3? t, Color? c = null)
        {
            if (!t.HasValue) { if (beacon) beacon.SetActive(false); return; }
            if (!beacon) { beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder); beacon.name = "JobBeacon"; Destroy(beacon.GetComponent<Collider>()); beacon.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default")); beacon.transform.localScale = new Vector3(0.8f, 28f, 0.8f); }
            beacon.GetComponent<Renderer>().material.color = c ?? Color.white; beacon.SetActive(true); beacon.transform.position = t.Value + Vector3.up * 28f;
        }

        void OnGUI()
        {
            if (Active == null || CharacterCreator.IsOpen || IntroCinematic.Playing || !pc) return;
            float W = Screen.width; var r = new Rect(W - 396, UI.QuestManager.PanelBottom + 8f, 384, 70); UiSkin.DrawPanel(r);
            GUI.Label(new Rect(r.x + 12, r.y + 6, r.width - 24, 20), "JOB  ·  $" + Active.reward, UiSkin.H2);
            string line = Active.type == "courier" ? (stage == 0 ? "Pick up the package" : "Deliver the package") : Active.type == "chop" ? (stage == 0 ? "Steal a car" : "Park it at the pawn shop garage") : "Kill " + hitName;
            GUI.Label(new Rect(r.x + 12, r.y + 24, r.width - 24, 22), "<b>" + Active.title + "</b>: " + line, UiSkin.Label);
            var t = Target(); string extra = t.HasValue ? $"{Vector3.Distance(pc.transform.position, t.Value):0} m" : "";
            if (deadline > 0f) extra += $"   {Mathf.Max(0, Mathf.CeilToInt(deadline - Time.time))} s left";
            GUI.Label(new Rect(r.x + 12, r.y + 46, r.width - 24, 20), extra, UiSkin.LabelDim);
        }
    }
}
