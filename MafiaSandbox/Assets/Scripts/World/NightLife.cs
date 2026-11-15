using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Npc;
using MafiaSandbox.Vehicles;

namespace MafiaSandbox.World
{
    /// <summary>The city empties out at night: far-away pedestrians and traffic are switched off in the small hours and come back at dawn.</summary>
    public class NightLife : MonoBehaviour
    {
        readonly List<GameObject> civs = new List<GameObject>(), cars = new List<GameObject>(); float t, refresh;
        public static float Fraction(float h)
        {
            if (h >= 7f && h < 18f) return 1f; if (h >= 18f && h < 20f) return Mathf.Lerp(1f, 0.6f, (h - 18f) / 2f); if (h >= 20f && h < 23f) return Mathf.Lerp(0.6f, 0.2f, (h - 20f) / 3f);
            if (h >= 23f || h < 5f) return 0.12f; return Mathf.Lerp(0.12f, 1f, (h - 5f) / 2f);
        }
        static float Rank(GameObject g) { uint id = (uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(g); return ((id * 2654435761u) % 1000u) / 1000f; }

        void Update()
        {
            var dn = DayNightCycle.I; var gm = GameManager.I; if (!dn || !gm || !gm.player) return; t -= Time.deltaTime; if (t > 0f) return; t = 2.5f;
            refresh -= 2.5f; if (refresh <= 0f || civs.Count == 0)
            {
                refresh = 30f; civs.Clear(); cars.Clear();
                foreach (var c in FindObjectsByType<CivilianBrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)) civs.Add(c.gameObject);
                foreach (var d in FindObjectsByType<TrafficDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None)) cars.Add(d.gameObject);
            }
            float f = Fraction(dn.timeOfDay); Vector3 p = gm.player.transform.position;
            foreach (var g in civs) { if (!g) continue; var cb = g.GetComponent<CivilianBrain>(); if (!cb || cb.isMedic || (cb.GetComponent<CharacterBody>()?.Dead ?? false)) continue; Toggle(g, Rank(g) < f, p, cb.State != "wander" && cb.State != "wait" && cb.State != "look" && cb.State != "cautious" && cb.State != "chat"); }
            float cf = Mathf.Max(0.15f, f * 0.7f);
            foreach (var g in cars) { if (!g) continue; var vc = g.GetComponent<VehicleController>(); if (vc && (vc.Controlled || vc.isPlayerCar)) continue; Toggle(g, Rank(g) < cf, p, false); }
        }
        static void Toggle(GameObject g, bool want, Vector3 player, bool busy)
        {
            float d = (g.transform.position - player).magnitude;
            if (g.activeSelf && !want && d > 45f && !busy) g.SetActive(false);
            else if (!g.activeSelf && want && d > 28f) g.SetActive(true);
        }
    }
}
