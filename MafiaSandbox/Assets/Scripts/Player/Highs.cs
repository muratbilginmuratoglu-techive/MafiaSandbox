using System.Collections.Generic;
using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.UI;
using MafiaSandbox.World;

namespace MafiaSandbox.Player
{
    /// <summary>
    /// Using your own product. Every drink and every drug has its own head, with something it is good for and a price afterwards:
    ///   Moonshine  blind drunk: the world rolls and your aim wanders, but half the blows that land on you don't register
    ///   Whiskey    warm and steady: almost no recoil and one extra point of health while it lasts
    ///   Gin        silver tongue: people buy more often and pay more, recruits say yes more easily
    ///   Weed       mellow: wounds close on their own, the day slows down, and the munchies eat your food bar
    ///   Pills      wired: faster on your feet and you never tire, then you crash and can't run at all
    ///   Powder     razor focus: time slows while you aim and you shoot faster, the comedown costs health
    ///   Crystal    berserk: much faster, melee kills in one blow, you shrug off hits, and you start seeing things
    /// Doses add up to a toxicity meter. Fill it and you black out and wake up at home, lighter in the pocket.
    /// </summary>
    public class Highs : MonoBehaviour
    {
        public static Highs I { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (I) return; var go = new GameObject("Highs"); DontDestroyOnLoad(go); go.AddComponent<Highs>(); }

        public class Kind { public string id, name, head, upside, after; public float dur, tox, crashDur; public Color tint; public bool drink; }
        public static readonly Kind[] Kinds =
        {
            new Kind { id = "Moonshine", name = "Moonshine",    head = "BLIND DRUNK",   upside = "half the hits on you don't register",          after = "hangover: slow to catch your breath", dur = 60f, tox = 24f, crashDur = 40f, tint = new Color(0.95f, 0.7f, 0.25f), drink = true },
            new Kind { id = "Whiskey",   name = "Whiskey",      head = "WARM AND STEADY", upside = "half the recoil, +1 health",                 after = "a dull head",                         dur = 75f, tox = 18f, crashDur = 25f, tint = new Color(0.85f, 0.45f, 0.15f), drink = true },
            new Kind { id = "Gin",       name = "Gin",          head = "SILVER TONGUE", upside = "buyers and recruits say yes, +25% street price", after = "dry mouth",                           dur = 90f, tox = 16f, crashDur = 20f, tint = new Color(0.55f, 0.9f, 0.8f), drink = true },
            new Kind { id = "Weed",      name = "Weed",         head = "MELLOW",        upside = "wounds heal on their own",                     after = "the munchies",                        dur = 90f, tox = 12f, crashDur = 30f, tint = new Color(0.4f, 0.85f, 0.35f) },
            new Kind { id = "Pills",     name = "Pills",        head = "WIRED",         upside = "+20% speed, you never tire",                   after = "crash: you can't run",                dur = 60f, tox = 28f, crashDur = 25f, tint = new Color(1f, 0.95f, 0.5f) },
            new Kind { id = "Powder",    name = "White powder", head = "RAZOR FOCUS",   upside = "time slows while you aim, faster trigger",     after = "comedown: -1 health, shaky hands",    dur = 45f, tox = 34f, crashDur = 30f, tint = new Color(0.9f, 0.95f, 1f) },
            new Kind { id = "Crystal",   name = "Blue crystal", head = "BERSERK",       upside = "+35% speed, melee kills, you shrug off hits",  after = "comedown: -1 health, slow, starving", dur = 60f, tox = 45f, crashDur = 40f, tint = new Color(0.35f, 0.75f, 1f) },
        };
        public static Kind Get(string id) { foreach (var k in Kinds) if (k.id == id) return k; return null; }

        readonly float[] left = new float[Kinds.Length], crash = new float[Kinds.Length];
        public float Toxicity { get; private set; }
        float healT, jitterT, ghostT, blackout, wobble, pulse; bool slowed, extraHp; string ghost = ""; float ghostShow; int ghostStars;
        Texture2D vignette, dot; Quaternion savedRot; bool swayed; Camera swayCam;

        bool On(int i) => left[i] > 0f;
        bool Crashing(int i) => crash[i] > 0f;
        public bool Any { get { for (int i = 0; i < left.Length; i++) if (left[i] > 0f || crash[i] > 0f) return true; return false; } }
        public bool Active(string id) { for (int i = 0; i < Kinds.Length; i++) if (Kinds[i].id == id) return left[i] > 0f; return false; }

        // ---- what the rest of the game asks
        public static float Speed { get { if (!I) return 1f; float s = 1f; if (I.On(0)) s *= 0.88f; if (I.On(3)) s *= 0.92f; if (I.On(4)) s *= 1.2f; if (I.On(6)) s *= 1.35f; if (I.Crashing(6)) s *= 0.75f; return s; } }
        public static bool NoTire => I && (I.On(4) || I.On(6));
        public static bool CantRun => I && I.Crashing(4);
        public static float StaminaRegen => I && (I.Crashing(0) || I.Crashing(1)) ? 0.5f : 1f;
        public static float HungerDrain => I && (I.On(3) || I.Crashing(3)) ? 4f : I && I.Crashing(6) ? 3f : 1f;
        public static float Recoil { get { if (!I) return 1f; float r = 1f; if (I.On(1)) r *= 0.5f; if (I.On(0)) r *= 1.5f; if (I.Crashing(5)) r *= 1.6f; return r; } }
        public static float FireRate => I && I.On(5) ? 0.7f : 1f;                    // multiplies the time between shots
        public static float Numb => !I ? 0f : I.On(6) ? 0.5f : I.On(0) ? 0.5f : 0f;   // chance a hit on you does nothing
        public static bool Berserk => I && I.On(6);
        public static float DealChance => I && I.On(2) ? 0.2f : 0f;
        public static float DealPrice => I && I.On(2) ? 1.25f : 1f;
        public static float FovAdd { get { if (!I) return 0f; float f = 0f; if (I.On(3)) f += 8f; if (I.On(4)) f += 6f; if (I.On(6)) f += 10f + Mathf.Sin(Time.time * 9f) * 2f; return f; } }

        void Awake()
        {
            I = this; dot = new Texture2D(1, 1); dot.SetPixel(0, 0, Color.white); dot.Apply();
            const int n = 64; vignette = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f; float d = Mathf.Sqrt(dx * dx + dy * dy); vignette.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((d - 0.5f) / 0.7f))); }
            vignette.Apply(); Camera.onPreCull += PreCull; Camera.onPostRender += PostRender;
        }
        void OnDestroy() { Camera.onPreCull -= PreCull; Camera.onPostRender -= PostRender; }

        /// <summary>Drink it / smoke it / take it. Returns true if it was used up.</summary>
        public bool Take(string id)
        {
            var gm = GameManager.I; int i = System.Array.FindIndex(Kinds, k => k.id == id); if (i < 0 || !gm || !gm.player) return false;
            if (blackout > 0f) return false; var kind = Kinds[i];
            var vi = gm.player.GetComponent<MafiaSandbox.Vehicles.VehicleInteraction>();
            left[i] = Mathf.Min(kind.dur * 2f, Mathf.Max(left[i], 0f) + kind.dur); crash[i] = 0f; Toxicity += kind.tox * (left[i] > kind.dur ? 1.25f : 1f);
            var body = gm.player.GetComponent<CharacterBody>();
            if (i == 1 && body && !extraHp) { extraHp = true; body.maxHp += 1; body.hp = Mathf.Min(body.maxHp, body.hp + 1); }
            if (kind.drink && Vitals.I) Vitals.I.hunger = Mathf.Min(100f, Vitals.I.hunger + 4f);
            gm.Banner(kind.head, $"{kind.name}: {kind.upside}", 3.5f); gm.Say($"{(kind.drink ? "You drink the" : i == 3 ? "You smoke the" : "You take the")} {kind.name.ToLower()}. Afterwards: {kind.after}.");
            Sfx.At("click", gm.player.transform.position); pulse = 1f;
            if (vi && vi.Current && !vi.Current.light && WantedSystem.I) WantedSystem.I.Suspect(1, 40f);   // behind the wheel: somebody may notice
            if (Toxicity >= 100f) Blackout();
            else if (Toxicity >= 70f) gm.Say("Your heart is hammering. One more of anything could put you on the floor.");
            return true;
        }

        void Blackout()
        {
            var gm = GameManager.I; blackout = 5f; for (int i = 0; i < left.Length; i++) { left[i] = 0f; crash[i] = 0f; }
            gm.player.InputLocked = true; gm.Say("Too much. Everything goes dark.");
        }
        void WakeUp()
        {
            var gm = GameManager.I; var eco = EconomyManager.I; var dn = DayNightCycle.I; if (!gm || !gm.player) return;
            var vi = gm.player.GetComponent<MafiaSandbox.Vehicles.VehicleInteraction>(); if (vi && vi.Current) vi.Exit();
            gm.player.Teleport(gm.homePosition, gm.homeYaw); gm.player.InputLocked = false; Toxicity = 35f; DropExtraHp();
            int lost = eco ? Mathf.Min(eco.cash / 10, 800) : 0; if (eco) eco.cash -= lost;
            if (dn) { dn.timeOfDay += 6f; if (dn.timeOfDay >= 24f) { dn.timeOfDay -= 24f; dn.Day++; } }
            var body = gm.player.GetComponent<CharacterBody>(); if (body && !body.Dead) body.hp = 1; if (Vitals.I) { Vitals.I.hunger = Mathf.Min(Vitals.I.hunger, 25f); Vitals.I.stamina = 10f; }
            gm.Banner("YOU BLACKED OUT", $"Six hours gone. Somebody dragged you home" + (lost > 0 ? $" and helped themselves to ${lost}." : "."), 6f);
            PhonePanel.Post("jeremy", "Found you face down two streets away. You're home. Don't do that again.");
        }
        void DropExtraHp() { var gm = GameManager.I; var body = gm && gm.player ? gm.player.GetComponent<CharacterBody>() : null; if (extraHp && body) { extraHp = false; body.maxHp = Mathf.Max(1, body.maxHp - 1); body.hp = Mathf.Min(body.hp, body.maxHp); } }

        void EndOf(int i)
        {
            var gm = GameManager.I; var k = Kinds[i]; crash[i] = k.crashDur; var body = gm.player.GetComponent<CharacterBody>(); var v = Vitals.I;
            gm.Say($"The {k.name.ToLower()} wears off: {k.after}.");
            if (i == 1) DropExtraHp();
            if (i == 4 && v) { v.stamina = 0f; v.hunger = Mathf.Max(0f, v.hunger - 15f); }
            if ((i == 5 || i == 6) && body && !body.Dead && body.hp > 1) body.hp--;
            if (i == 6 && v) v.hunger = Mathf.Max(0f, v.hunger - 30f);
        }

        static readonly string[] Ghosts = { "Was that a siren?", "Somebody is following you.", "The man by the corner is a cop. Isn't he?", "Your phone is ringing. No, it isn't.", "They know. They all know." };
        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player) return; float dt = Time.deltaTime, udt = Time.unscaledDeltaTime;
            var body = gm.player.GetComponent<CharacterBody>(); var combat = gm.player.GetComponent<PlayerCombat>(); var cam = gm.playerCamera;
            if (blackout > 0f) { blackout -= udt; if (blackout <= 0f) WakeUp(); return; }
            if (body && body.Dead) { for (int i = 0; i < left.Length; i++) { left[i] = 0f; crash[i] = 0f; } DropExtraHp(); Toxicity = 0f; }
            Toxicity = Mathf.Max(0f, Toxicity - dt * 0.45f); if (pulse > 0f) pulse -= udt * 0.8f;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] > 0f) { left[i] -= dt; if (left[i] <= 0f) { left[i] = 0f; EndOf(i); } }
                else if (crash[i] > 0f) crash[i] -= dt;
            }
            // weed: wounds close slowly
            if (On(3) && body && !body.Dead && body.hp < body.maxHp) { healT += dt; if (healT > 12f) { healT = 0f; body.Treat(1); gm.Say("The ache fades a little."); } } else if (!On(3)) healT = 0f;
            // moonshine: your feet (and the camera) wander
            wobble += dt;
            if (On(0) && cam && !gm.player.InputLocked) cam.SetYaw(cam.Yaw + Mathf.Sin(wobble * 0.9f) * 16f * dt + Mathf.Sin(wobble * 2.3f) * 7f * dt, cam.Pitch);
            // pills / crystal: the jitters
            if ((On(4) || On(6) || Crashing(5)) && cam) { jitterT -= dt; if (jitterT <= 0f) { jitterT = On(6) ? Random.Range(0.25f, 0.9f) : Random.Range(0.8f, 2.2f); cam.AddKick(On(6) ? 0.5f : 0.22f); } }
            // crystal: things that aren't there
            if (On(6)) { ghostT -= dt; if (ghostT <= 0f) { ghostT = Random.Range(7f, 14f); ghost = Ghosts[Random.Range(0, Ghosts.Length)]; ghostShow = 3.5f; ghostStars = Random.Range(1, 4); Sfx.At("click", gm.player.transform.position + Random.onUnitSphere * 4f); } }
            if (ghostShow > 0f) ghostShow -= dt;
            // powder: the world slows down while you aim
            bool wantSlow = On(5) && combat && combat.Aiming && !GameUI.Blocking && !Workshop.Active;
            if (Time.timeScale > 0.01f)
            {
                if (wantSlow) { Time.timeScale = Mathf.Lerp(Time.timeScale, 0.55f, 1f - Mathf.Exp(-10f * udt)); slowed = true; }
                else if (slowed) { Time.timeScale = Mathf.Lerp(Time.timeScale, 1f, 1f - Mathf.Exp(-10f * udt)); if (Time.timeScale > 0.985f) { Time.timeScale = 1f; slowed = false; } }
            }
        }

        // ---- the camera rolls with the drink (applied just for the render, so aiming and movement stay honest)
        void PreCull(Camera c)
        {
            swayed = false; if (c != Camera.main || blackout > 0f || Workshop.Active) return;
            float roll = 0f, pitch = 0f, yaw = 0f, t = Time.unscaledTime;
            if (On(0)) { roll += Mathf.Sin(t * 1.1f) * 6.5f; pitch += Mathf.Sin(t * 0.8f + 1f) * 2.2f; yaw += Mathf.Sin(t * 0.6f) * 1.5f; }
            if (On(1)) roll += Mathf.Sin(t * 0.9f) * 1.8f;
            if (On(2)) roll += Mathf.Sin(t * 1.3f) * 1.4f;
            if (On(3)) { roll += Mathf.Sin(t * 0.45f) * 2.2f; pitch += Mathf.Sin(t * 0.35f) * 1f; }
            if (On(6)) { roll += Mathf.Sin(t * 7f) * 1.2f + Mathf.Sin(t * 0.7f) * 3f; }
            if (Crashing(0) || Crashing(1)) roll += Mathf.Sin(t * 0.5f) * 1.2f;
            if (roll == 0f && pitch == 0f && yaw == 0f) return;
            swayCam = c; savedRot = c.transform.rotation; c.transform.rotation = savedRot * Quaternion.Euler(pitch, yaw, roll); swayed = true;
        }
        void PostRender(Camera c) { if (swayed && c == swayCam) { c.transform.rotation = savedRot; swayed = false; } }

        void Tint(Color col, float a, bool full)
        {
            if (a <= 0.003f) return; GUI.color = new Color(col.r, col.g, col.b, a);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), full ? dot : vignette, ScaleMode.StretchToFill); GUI.color = Color.white;
        }
        void OnGUI()
        {
            var gm = GameManager.I; if (!gm || !gm.player || Workshop.Active || CharacterCreator.IsOpen || IntroCinematic.Playing) return;
            GUI.depth = 60; float W = Screen.width, H = Screen.height, t = Time.unscaledTime;
            if (blackout > 0f) { Tint(Color.black, Mathf.Clamp01((5f - blackout) / 1.2f), true); return; }
            // the colour of each head
            for (int i = 0; i < Kinds.Length; i++)
            {
                if (left[i] <= 0f) continue; var k = Kinds[i]; float fade = Mathf.Clamp01(left[i] / 6f);
                float a = i == 0 ? 0.36f + 0.1f * Mathf.Sin(t * 1.4f) : i == 5 ? 0.24f + 0.22f * Mathf.Max(0f, Mathf.Sin(t * 5.2f)) : i == 6 ? 0.4f : i == 3 ? 0.34f : 0.24f;
                Color c = i == 6 ? Color.HSVToRGB(Mathf.Repeat(t * 0.25f, 1f), 0.7f, 1f) : k.tint;
                Tint(c, a * fade, false); if (i == 0 || i == 3 || i == 6) Tint(c, (i == 6 ? 0.07f : 0.04f) * fade, true);
            }
            for (int i = 0; i < Kinds.Length; i++) if (crash[i] > 0f) Tint(Color.black, 0.35f * Mathf.Clamp01(crash[i] / 6f), false);
            if (pulse > 0f) Tint(Color.white, 0.25f * pulse, true);
            if (GameUI.Blocking || PhonePanel.IsOpen || CrewPanel.IsOpen) return;
            // crystal: stars and voices that are not there
            if (ghostShow > 0f && On(6))
            {
                var gs = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter }; gs.normal.textColor = new Color(0.8f, 0.9f, 1f, Mathf.Clamp01(ghostShow));
                GUI.Label(new Rect(W / 2f - 300, H * 0.3f + Mathf.Sin(t * 3f) * 6f, 600, 30), ghost, gs);
                var ss = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleRight }; ss.normal.textColor = new Color(1f, 0.85f, 0.2f, 0.75f * Mathf.Clamp01(ghostShow) * (0.5f + 0.5f * Mathf.Sin(t * 12f)));
                GUI.Label(new Rect(W - 200, 84, 188, 28), new string('★', ghostStars), ss);
            }
            // what you are on, and how close to the floor
            if (!Any && Toxicity < 1f) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, richText = true, wordWrap = false, clipping = TextClipping.Clip }; st.normal.textColor = Color.white;
            float y = H - 84f, x = W - 392f;
            void Row(string text, Color c, float v)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(new Rect(x, y, 380, 22), dot); GUI.color = new Color(c.r, c.g, c.b, 0.85f); GUI.DrawTexture(new Rect(x, y + 19, 380f * Mathf.Clamp01(v), 3), dot); GUI.color = Color.white;
                GUI.Label(new Rect(x + 8, y, 372, 20), text, st); y -= 25f;
            }
            if (Toxicity >= 1f) Row($"TOXICITY {Toxicity:0}%" + (Toxicity >= 70f ? "   <color=#ff7060>DANGER</color>" : ""), Toxicity >= 70f ? new Color(1f, 0.35f, 0.3f) : new Color(0.8f, 0.8f, 0.8f), Toxicity / 100f);
            for (int i = 0; i < Kinds.Length; i++)
            {
                var k = Kinds[i];
                if (left[i] > 0f) Row($"{k.head}  {left[i]:0}s   <size=11>{k.upside}</size>", k.tint, left[i] / k.dur);
                else if (crash[i] > 0f) Row($"<color=#b0b0b0>{k.name}: {k.after}  {crash[i]:0}s</color>", new Color(0.5f, 0.5f, 0.5f), crash[i] / k.crashDur);
            }
        }
    }
}
