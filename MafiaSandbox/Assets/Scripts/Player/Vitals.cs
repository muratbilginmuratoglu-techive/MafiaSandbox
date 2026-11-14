using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;

namespace MafiaSandbox.Player
{
    /// <summary>
    /// Stamina (sprinting drains it, 0 = exhausted until it recovers to 25) and food (100 → 0 in ~10 minutes; below 50 no
    /// health regen, at 0 you slowly lose health). Health itself lives on the CharacterBody.
    /// </summary>
    public class Vitals : MonoBehaviour
    {
        public static Vitals I { get; private set; }
        public float stamina = 100f, hunger = 100f;
        public bool Exhausted { get; private set; }
        PlayerController pc; CharacterBody body; float starveT, regenT, nagT, stepT;

        void Awake() { I = this; pc = GetComponent<PlayerController>(); body = GetComponent<CharacterBody>(); }

        void Update()
        {
            float dt = Time.deltaTime; if (body && body.Dead) return;
            bool sprinting = pc && pc.IsRunning && pc.Moving && !pc.InputLocked;
            if (sprinting) stamina = Mathf.Max(0f, stamina - 11f * dt);
            else stamina = Mathf.Min(100f, stamina + (hunger > 30f ? 12f : 5f) * dt * (pc && pc.Moving ? 0.6f : 1f));
            if (stamina <= 0f) Exhausted = true; else if (stamina > 25f) Exhausted = false;
            if (pc) { pc.SprintAllowed = !Exhausted; pc.SpeedScale = hunger <= 0f ? 0.8f : 1f; }

            // footsteps
            if (pc && pc.Moving && !pc.InputLocked) { stepT -= dt; if (stepT <= 0f) { stepT = sprinting ? 0.3f : pc.IsCrouching ? 0.7f : 0.48f; Sfx.At("step", transform.position, sprinting ? 0.5f : 0.3f); } } else stepT = 0.1f;
            hunger = Mathf.Max(0f, hunger - dt / 6f);
            if (hunger <= 0f && body)
            {
                starveT += dt; if (starveT > 40f) { starveT = 0f; if (body.hp > 1) body.hp--; }
                nagT -= dt; if (nagT <= 0f) { nagT = 20f; GameManager.I?.Say("You are weak with hunger — eat something"); }
            }
        }

        public void Reset() { stamina = 100f; hunger = Mathf.Max(hunger, 60f); Exhausted = false; }
    }
}
