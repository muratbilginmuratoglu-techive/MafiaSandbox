using UnityEngine;

namespace MafiaSandbox.Core
{
    /// <summary>
    /// Wanted level 0–3 (★). Police who hear shooting raise it to 1; a kill in front of a witness adds 2 (3 for a cop).
    /// It decays one star every 25 s the police haven't seen the player. PoliceHostile flips when the player shoots a cop
    /// or a chase drags on — then police open fire instead of trying to arrest.
    /// </summary>
    public class WantedSystem : MonoBehaviour
    {
        public static WantedSystem I { get; private set; }
        public int Level { get; private set; }
        public bool PoliceHostile { get; set; }
        public Vector3 LastKnownPlayerPos { get; private set; }
        public float SinceSeen { get; private set; } = 999f;
        public float DecaySeconds = 25f;

        /// <summary>Testing switch: with the police off nobody chases, dispatches or adds stars. Saved; default OFF for now.</summary>
        public static bool PoliceOff { get => PlayerPrefs.GetInt("ms_police_off_v1", 1) == 1; set { PlayerPrefs.SetInt("ms_police_off_v1", value ? 1 : 0); if (value && I) I.Clear(); } }
        void Awake() { I = this; }

        // ---- witness model: stars only come from police who actually SEE a crime; civilians who see one only make you a suspect for a while
        public float SuspectT { get; private set; } int suspectSev; float suspectCheck;
        public void Suspect(int severity, float seconds = 70f) { if (PoliceOff) return; suspectSev = Mathf.Max(suspectSev, severity); SuspectT = Mathf.Max(SuspectT, seconds); }

        /// <summary>A crime at `pos`: if an officer within `range` has a clear view of it, that's `stars` stars; returns whether a cop saw it.</summary>
        public bool CopWitness(Vector3 pos, float range, int stars, string why)
        {
            if (PoliceOff) return false;
            foreach (var cop in FindObjectsByType<MafiaSandbox.Npc.PoliceBrain>(FindObjectsSortMode.None))
            {
                if (cop.Dead || Vector3.Distance(cop.transform.position, pos) > range) continue;
                if (!MafiaSandbox.Npc.Perception.Clear(cop.transform.position + Vector3.up * 1.6f, pos + Vector3.up * 1.2f)) continue;
                Raise(stars, why); Seen(pos); cop.Chase(); return true;
            }
            return false;
        }

        public void Add(int stars, string why)
        {
            if (PoliceOff) return;
            int before = Level; Level = Mathf.Clamp(Level + stars, 0, 3); SinceSeen = 0f;
            if (Level > before) GameManager.I?.Say($"Wanted {new string('★', Level)} — {why}");
        }

        /// <summary>Raise to at least `level` stars.</summary>
        public void Raise(int level, string why) { if (Level < level) Add(level - Level, why); else SinceSeen = 0f; }

        public void Seen(Vector3 playerPos) { SinceSeen = 0f; LastKnownPlayerPos = playerPos; }

        public void Clear() { Level = 0; PoliceHostile = false; SinceSeen = 999f; }

        void Update()
        {
            if (PoliceOff && (Level > 0 || SuspectT > 0f)) { Clear(); SuspectT = 0f; }
            if (SuspectT > 0f)
            {
                SuspectT -= Time.deltaTime; suspectCheck -= Time.deltaTime;
                if (SuspectT <= 0f) suspectSev = 0;
                else if (suspectCheck <= 0f && Level == 0 && GameManager.I && GameManager.I.player)
                {
                    suspectCheck = 0.6f; var pp = GameManager.I.player.transform.position;
                    foreach (var cop in FindObjectsByType<MafiaSandbox.Npc.PoliceBrain>(FindObjectsSortMode.None))
                        if (!cop.Dead && Vector3.Distance(cop.transform.position, pp) < 24f && cop.SeesPlayer(24f, 140f)) { int s = suspectSev; SuspectT = 0f; suspectSev = 0; Raise(s, "recognised from a witness report"); Seen(pp); cop.Chase(); break; }
                }
            }
            if (Level <= 0) return;
            SinceSeen += Time.deltaTime;
            if (SinceSeen > DecaySeconds)
            {
                Level--; SinceSeen = 0f;
                GameManager.I?.Say(Level > 0 ? $"Wanted level dropped ({new string('★', Level)})" : "The police gave up the chase");
                if (Level == 0) PoliceHostile = false;
            }
        }
    }
}
