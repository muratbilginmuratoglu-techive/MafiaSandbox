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

        void Awake() { I = this; }

        public void Add(int stars, string why)
        {
            int before = Level; Level = Mathf.Clamp(Level + stars, 0, 3); SinceSeen = 0f;
            if (Level > before) GameManager.I?.Say($"Wanted {new string('★', Level)} — {why}");
        }

        public void Seen(Vector3 playerPos) { SinceSeen = 0f; LastKnownPlayerPos = playerPos; }

        public void Clear() { Level = 0; PoliceHostile = false; SinceSeen = 999f; }

        void Update()
        {
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
