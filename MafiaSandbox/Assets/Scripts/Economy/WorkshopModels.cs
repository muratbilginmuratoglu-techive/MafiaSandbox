using System.Collections.Generic;
using UnityEngine;
using static MafiaSandbox.Economy.Kit;

namespace MafiaSandbox.Economy
{
    public enum WK { Place, Crank, Shake, Fill, Drops, Heat, Stretch, Grind, Timing, Clicks, Pack, Pour }

    /// <summary>One hands-on step of a job. part = tray part index, f = amount (turns / seconds / target), a / b = scene object names, v = a point on the table.</summary>
    public class WStep
    {
        public WK k; public int part; public float f = 1f; public string a, b, on, off, pre, text, hint; public Vector3 v;
        // Pour: unit = what is counted (drops, g, ml ...), per = units in one drop / grain, fill = object that rises as it goes in, with = objects carried along with the container
        public string unit, fill, with; public float fillTo = 1f, per = 1f; public Color col = Color.white; public bool grain;
    }
    /// <summary>A part lying in the tray. It is modelled in its final (assembled) position, so placing it is just "move to the origin".</summary>
    public class WPart { public string name; public System.Action<Transform> build; public bool hide; }
    public class WorkDef
    {
        public string title; public Vector3 origin; public float camDist = 1.72f; public WPart[] parts; public WStep[] steps;
        public System.Action<Transform> apparatus; public string[] hidden = new string[0];
    }

    /// <summary>
    /// The workshop's models and job scripts: firearms built from readable parts (frame, barrel, slide, grips, magazine ...),
    /// a copper pot still with jars and bottles, and a lab bench (glassware, burner, mortar, press, scale).
    /// Everything is game fiction: the steps are hand-skill mini games, not instructions.
    /// </summary>
    public static class WorkshopModels
    {
        public const float TableY = 1.0f;
        static readonly Color gm = H(0x3d4046), gmD = H(0x202226), st = H(0x8d9299), stD = H(0x5a5f66), wd = H(0x6a3f20), wdD = H(0x432612), poly = H(0x2a2a2e), brass = H(0xc9a24a),
            copper = H(0xb8683a), copperD = H(0x8a4a26), cork = H(0xb98a55), cream = H(0xe9dcc0), ink = H(0x2a2420), white = H(0xf1f1ec), stone = H(0x8f8a80), rubber = H(0x202022);

        static WPart P(string name, System.Action<Transform> build, bool hide = false) => new WPart { name = name, build = build, hide = hide };
        static WStep S(WK k, string text, string hint, int part = -1, float f = 1f, string a = null, string b = null, string on = null, string off = null, Vector3 v = default)
            => new WStep { k = k, text = text, hint = hint, part = part, f = f, a = a, b = b, on = on, off = off, v = v };
        public const string PourHint = "Drag it over the target and let go: it stays there. Scroll DOWN to tip it, drop by drop; scroll up to stop. Click to put it back when the amount is right.";
        /// <summary>Carry a container over a target and tip it with the mouse wheel: drop by drop or gram by gram.</summary>
        static WStep Pour(string text, int part, string node, float amount, string unit, Vector3 mouth, Color col, bool grain, float per = 1f, string fill = null, float fillTo = 1f)
            => new WStep { k = WK.Pour, text = text, hint = PourHint, part = part, a = node, f = amount, unit = unit, v = mouth, col = col, grain = grain, per = per, fill = fill, fillTo = fillTo };
        static Transform Rot(Transform t, float x, float y, float z) { t.localRotation = Quaternion.Euler(x, y, z) * t.localRotation; return t; }

        /// <summary>The same parts the bench jobs assemble, for the guns people actually carry (x = towards the muzzle, y = up).</summary>
        public static WPart[] GunParts(MafiaSandbox.Combat.WeaponKind k)
        {
            switch (k)
            {
                case MafiaSandbox.Combat.WeaponKind.Pistol: return Pistol();
                case MafiaSandbox.Combat.WeaponKind.Revolver: return Revolver();
                case MafiaSandbox.Combat.WeaponKind.Shotgun: return Shotgun();
                case MafiaSandbox.Combat.WeaponKind.Rifle: return Rifle();
                default: return null;
            }
        }

        public static WorkDef Get(string recipe)
        {
            switch (recipe)
            {
                case "gun": return Gun("Semi-auto pistol", Pistol(), 1.15f);
                case "revolver": return Gun("Revolver", Revolver(), 1.15f);
                case "shotgun": return Gun("Pump shotgun", Shotgun(), 1.7f);
                case "rifle": return Gun("Assault rifle", Rifle(), 1.7f);
                case "moon": return Liquor(0);
                case "whisk": return Liquor(1);
                case "gin": return Liquor(2);
                case "weed": return Weed();
                case "pills": return Pills();
                case "pow": return Cook(false);
                default: return Cook(true);
            }
        }

        // =================================================================================================== GUNSMITH BENCH
        static WorkDef Gun(string title, WPart[] parts, float jigW)
        {
            var steps = new List<WStep>();
            for (int i = 0; i < parts.Length; i++) steps.Add(S(WK.Place, i == 0 ? "Mount the " + parts[i].name.ToLower() + " on the jig" : "Fit the " + parts[i].name.ToLower(), "Drag the part from the tray onto its glowing outline. The closer the fit, the better the gun.", i));
            steps.Add(S(WK.Crank, "Press the pins home", "Grab the hand wheel and turn it in circles.", -1, 2.5f, "Crank"));
            steps.Add(S(WK.Timing, "Function check", "Rack the action: stop the marker in the green zone (SPACE)."));
            steps.Add(S(WK.Pack, "Wrap it and crate it", "Drag the finished gun into the crate."));
            return new WorkDef
            {
                title = title, parts = parts, steps = steps.ToArray(), origin = new Vector3(0.05f, TableY + 0.43f, 0.12f), camDist = jigW > 1.3f ? 2.1f : 1.72f,
                apparatus = r =>
                {
                    // the jig: a felt board with two pegs behind the work piece, and the pin press with its hand wheel
                    Box(r, 0.05f, TableY + 0.4f, 0.2f, jigW, 0.72f, 0.02f, H(0x23352f), 0.05f); Box(r, 0.05f, TableY + 0.4f, 0.215f, jigW + 0.06f, 0.78f, 0.02f, H(0x4a3524), 0.1f);
                    Box(r, 0.05f, TableY + 0.02f, 0.2f, jigW + 0.06f, 0.04f, 0.14f, H(0x4a3524), 0.1f);
                    for (int i = 0; i < 9; i++) Box(r, 0.05f - jigW * 0.5f + 0.06f + i * (jigW - 0.12f) / 8f, TableY + 0.74f, 0.188f, 0.012f, 0.012f, 0.004f, H(0xd8c88a), 0.2f, 0f, "Tick");
                    Box(r, 0.86f, TableY + 0.07f, 0.1f, 0.2f, 0.14f, 0.16f, stD, 0.4f, 0.6f); Box(r, 0.86f, TableY + 0.25f, 0.14f, 0.06f, 0.24f, 0.06f, st, 0.5f, 0.8f); Box(r, 0.86f, TableY + 0.37f, 0.1f, 0.12f, 0.04f, 0.16f, stD, 0.4f, 0.6f);
                    Cy(r, 0.86f, TableY + 0.24f, 0.05f, 0.03f, 0.2f, st, 1, 0.7f, 0.9f);
                    var crank = Node(r, "Crank", 0.86f, TableY + 0.3f, 0.01f);
                    Cy(crank, 0f, 0f, 0f, 0.2f, 0.022f, H(0xb23a2e), 2, 0.4f, 0.3f); Cy(crank, 0f, 0f, 0f, 0.05f, 0.04f, st, 2, 0.6f, 0.9f);
                    for (int i = 0; i < 3; i++) Rot(Box(crank, 0f, 0f, -0.005f, 0.19f, 0.018f, 0.016f, st, 0.6f, 0.9f), 0f, 0f, i * 60f);
                    Cy(crank, 0.08f, 0f, -0.04f, 0.03f, 0.06f, H(0x1c1c1e), 2, 0.3f);
                    // loose bench clutter: files, punch, oil
                    Rot(Box(r, 0.5f, TableY + 0.012f, -0.32f, 0.26f, 0.012f, 0.03f, st, 0.6f, 0.8f), 0f, 18f, 0f); Rot(Box(r, 0.39f, TableY + 0.016f, -0.36f, 0.1f, 0.026f, 0.034f, H(0x9a5a22), 0.2f), 0f, 18f, 0f);
                    La(r, 0.62f, TableY, 0.3f, new[] { 0f, 0f, 0.035f, 0f, 0.035f, 0.09f, 0.008f, 0.12f, 0.008f, 0.15f }, H(0xb23a2e), 0.5f, 0.2f);
                }
            };
        }

        static void TriggerGuard(Transform t, float x0, float x1, float y, Color c)
        {
            Box(t, (x0 + x1) * 0.5f, y, 0f, x1 - x0, 0.013f, 0.026f, c, 0.4f, 0.6f); Rot(Box(t, x1 + 0.005f, y + 0.04f, 0f, 0.013f, 0.095f, 0.026f, c, 0.4f, 0.6f), 0f, 0f, -12f);
        }

        static WPart[] Pistol() => new[]
        {
            P("Frame", t =>
            {
                Box(t, 0f, 0f, 0f, 0.46f, 0.05f, 0.072f, gm, 0.45f, 0.7f);                                                         // rail / dust cover
                Pr(t, new[] { -0.215f, -0.02f, -0.07f, -0.02f, -0.118f, -0.3f, -0.258f, -0.28f }, 0.062f, gmD, 0.3f, 0.4f);        // grip frame
                Pr(t, new[] { -0.275f, 0.012f, -0.2f, -0.035f, -0.2f, 0.025f }, 0.06f, gm, 0.45f, 0.7f);                           // beavertail
                TriggerGuard(t, -0.065f, 0.065f, -0.125f, gm);
                Rot(Box(t, -0.02f, -0.07f, 0f, 0.016f, 0.07f, 0.018f, st, 0.6f, 0.9f), 0f, 0f, 14f);                                // trigger
                Cy(t, -0.03f, 0.002f, -0.038f, 0.02f, 0.008f, st, 2, 0.6f, 0.9f); Box(t, 0.03f, 0.006f, -0.038f, 0.075f, 0.012f, 0.006f, st, 0.6f, 0.9f);   // slide stop
                Cy(t, -0.082f, -0.075f, -0.033f, 0.018f, 0.01f, st, 2, 0.6f, 0.9f);                                                 // magazine release
                Box(t, 0.15f, -0.03f, 0f, 0.12f, 0.012f, 0.04f, gmD, 0.4f, 0.6f);                                                   // accessory rail
            }),
            P("Barrel", t =>
            {
                Cy(t, 0.03f, 0.058f, 0f, 0.036f, 0.38f, st, 0, 0.75f, 0.95f); Box(t, -0.125f, 0.06f, 0f, 0.09f, 0.05f, 0.05f, st, 0.7f, 0.9f); Box(t, -0.11f, 0.03f, 0f, 0.05f, 0.022f, 0.03f, stD, 0.6f, 0.9f);
                Cy(t, 0.222f, 0.058f, 0f, 0.02f, 0.004f, gmD, 0); Cy(t, 0.07f, 0.03f, 0f, 0.014f, 0.27f, stD, 0, 0.6f, 0.9f);
                for (int i = 0; i < 9; i++) Cy(t, -0.04f + i * 0.026f, 0.03f, 0f, 0.022f, 0.006f, H(0x9aa0a8), 0, 0.7f, 0.9f);     // recoil spring coils
            }),
            P("Slide", t =>
            {
                Pr(t, new[] { -0.255f, 0.028f, 0.232f, 0.028f, 0.232f, 0.094f, 0.2f, 0.108f, -0.235f, 0.108f, -0.255f, 0.09f }, 0.084f, gm, 0.5f, 0.75f);
                foreach (float z in new[] { -0.0425f, 0.0425f })
                {
                    for (int i = 0; i < 7; i++) Rot(Box(t, -0.225f + i * 0.015f, 0.07f, z, 0.006f, 0.06f, 0.004f, gmD, 0.3f, 0.5f), 0f, 0f, -8f);
                    for (int i = 0; i < 4; i++) Rot(Box(t, 0.15f + i * 0.015f, 0.07f, z, 0.006f, 0.05f, 0.004f, gmD, 0.3f, 0.5f), 0f, 0f, -8f);
                }
                Box(t, -0.05f, 0.086f, -0.0425f, 0.1f, 0.03f, 0.004f, gmD, 0.2f); Box(t, -0.05f, 0.086f, -0.0405f, 0.092f, 0.022f, 0.004f, st, 0.75f, 0.95f); Box(t, -0.05f, 0.109f, -0.012f, 0.1f, 0.004f, 0.05f, st, 0.75f, 0.95f);
                Box(t, 0.2f, 0.116f, 0f, 0.016f, 0.016f, 0.012f, gmD); Box(t, -0.22f, 0.117f, 0f, 0.02f, 0.02f, 0.06f, gmD); Box(t, -0.231f, 0.118f, -0.018f, 0.002f, 0.008f, 0.008f, white, 0.2f, 0f, "Dot", 0.6f); Box(t, -0.231f, 0.118f, 0.018f, 0.002f, 0.008f, 0.008f, white, 0.2f, 0f, "Dot", 0.6f);
                Cy(t, 0.2335f, 0.058f, 0f, 0.036f, 0.004f, gmD, 0); Cy(t, 0.235f, 0.058f, 0f, 0.026f, 0.004f, st, 0, 0.75f, 0.95f); Cy(t, 0.2365f, 0.058f, 0f, 0.016f, 0.004f, H(0x050505), 0);
            }),
            P("Grip panels", t =>
            {
                foreach (float z in new[] { -0.036f, 0.036f })
                {
                    Pr(t, new[] { -0.205f, -0.045f, -0.085f, -0.045f, -0.126f, -0.275f, -0.243f, -0.258f }, 0.014f, poly, 0.15f, 0f, z);
                    float zz = z + Mathf.Sign(z) * 0.008f;
                    for (int i = 0; i < 7; i++) Rot(Box(t, -0.152f - i * 0.0052f, -0.075f - i * 0.028f, zz, 0.105f, 0.007f, 0.003f, H(0x0c0c0d), 0.1f), 0f, 0f, 0f);
                    Cy(t, -0.15f, -0.06f, zz, 0.013f, 0.003f, st, 2, 0.6f, 0.9f); Cy(t, -0.185f, -0.25f, zz, 0.013f, 0.003f, st, 2, 0.6f, 0.9f);
                }
            }),
            P("Magazine", t =>
            {
                Pr(t, new[] { -0.19f, -0.03f, -0.1f, -0.03f, -0.145f, -0.3f, -0.232f, -0.29f }, 0.04f, stD, 0.6f, 0.9f);
                Pr(t, new[] { -0.252f, -0.292f, -0.122f, -0.305f, -0.126f, -0.332f, -0.258f, -0.318f }, 0.066f, gmD, 0.3f, 0.3f);
                Cy(t, -0.143f, -0.022f, 0f, 0.024f, 0.07f, brass, 0, 0.7f, 0.9f); Cy(t, -0.103f, -0.022f, 0f, 0.022f, 0.02f, H(0xb87333), 0, 0.6f, 0.9f);
                for (int i = 0; i < 5; i++) Cy(t, -0.152f - i * 0.0085f, -0.09f - i * 0.045f, -0.021f, 0.012f, 0.003f, H(0x101012), 2);    // witness holes
            }),
        };

        static WPart[] Revolver() => new[]
        {
            P("Frame", t =>
            {
                Box(t, 0f, 0.118f, 0f, 0.21f, 0.022f, 0.05f, st, 0.7f, 0.9f); Box(t, 0.005f, -0.022f, 0f, 0.2f, 0.03f, 0.05f, st, 0.7f, 0.9f); Box(t, 0.1f, 0.048f, 0f, 0.03f, 0.16f, 0.05f, st, 0.7f, 0.9f);
                Pr(t, new[] { -0.135f, -0.04f, -0.09f, -0.04f, -0.09f, 0.129f, -0.135f, 0.112f }, 0.092f, st, 0.7f, 0.9f);
                Pr(t, new[] { -0.2f, -0.03f, -0.1f, -0.03f, -0.17f, -0.27f, -0.255f, -0.245f }, 0.028f, stD, 0.6f, 0.9f);
                TriggerGuard(t, -0.095f, 0.03f, -0.12f, st); Box(t, -0.02f, 0.132f, 0f, 0.12f, 0.006f, 0.012f, stD, 0.5f, 0.8f);
            }),
            P("Cylinder", t =>
            {
                Cy(t, 0f, 0.048f, 0f, 0.122f, 0.172f, st, 0, 0.75f, 0.95f);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f + 0.52f; Cy(t, 0.03f, 0.048f + Mathf.Cos(a) * 0.052f, Mathf.Sin(a) * 0.052f, 0.03f, 0.115f, stD, 0, 0.6f, 0.9f);                      // flutes
                    float b = i * Mathf.PI / 3f; Cy(t, 0.0865f, 0.048f + Mathf.Cos(b) * 0.036f, Mathf.Sin(b) * 0.036f, 0.026f, 0.003f, H(0x0a0a0a), 0); Cy(t, -0.0865f, 0.048f + Mathf.Cos(b) * 0.036f, Mathf.Sin(b) * 0.036f, 0.028f, 0.004f, brass, 0, 0.7f, 0.9f);
                }
                Cy(t, 0.13f, 0.048f, 0f, 0.016f, 0.09f, stD, 0, 0.6f, 0.9f);
            }),
            P("Barrel", t =>
            {
                Cy(t, 0.255f, 0.078f, 0f, 0.05f, 0.29f, st, 0, 0.75f, 0.95f); Box(t, 0.22f, 0.04f, 0f, 0.21f, 0.03f, 0.028f, st, 0.7f, 0.9f); Box(t, 0.255f, 0.108f, 0f, 0.29f, 0.012f, 0.02f, stD, 0.5f, 0.8f);
                Pr(t, new[] { 0.35f, 0.112f, 0.398f, 0.112f, 0.39f, 0.14f }, 0.01f, gmD); Cy(t, 0.4015f, 0.078f, 0f, 0.026f, 0.004f, H(0x050505), 0);
            }),
            P("Hammer and trigger", t =>
            {
                Pr(t, new[] { -0.152f, 0.095f, -0.118f, 0.1f, -0.128f, 0.165f, -0.168f, 0.172f }, 0.02f, stD, 0.6f, 0.9f); Box(t, -0.175f, 0.172f, 0f, 0.04f, 0.012f, 0.026f, stD, 0.6f, 0.9f);
                Rot(Box(t, -0.03f, -0.065f, 0f, 0.016f, 0.075f, 0.016f, stD, 0.6f, 0.9f), 0f, 0f, 16f); Cy(t, -0.11f, 0.07f, -0.047f, 0.022f, 0.008f, stD, 2, 0.6f, 0.9f);
            }),
            P("Grips", t =>
            {
                Pr(t, new[] { -0.205f, -0.03f, -0.097f, -0.03f, -0.128f, -0.16f, -0.165f, -0.292f, -0.278f, -0.26f, -0.245f, -0.12f }, 0.06f, wd, 0.35f);
                foreach (float z in new[] { -0.031f, 0.031f }) { Cy(t, -0.17f, -0.09f, z, 0.022f, 0.004f, brass, 2, 0.7f, 0.9f); for (int i = 0; i < 5; i++) Rot(Box(t, -0.19f, -0.15f - i * 0.022f, z, 0.09f, 0.004f, 0.003f, wdD), 0f, 0f, 20f); }
            }),
        };

        static WPart[] Shotgun() => new[]
        {
            P("Receiver", t =>
            {
                Box(t, -0.08f, 0f, 0f, 0.26f, 0.086f, 0.05f, gm, 0.45f, 0.7f); Box(t, -0.06f, 0.012f, -0.0255f, 0.105f, 0.03f, 0.004f, H(0x070708));
                TriggerGuard(t, -0.17f, -0.07f, -0.07f, gm); Rot(Box(t, -0.125f, -0.04f, 0f, 0.014f, 0.05f, 0.014f, st, 0.6f, 0.9f), 0f, 0f, 14f);
                Box(t, -0.08f, -0.046f, 0f, 0.2f, 0.012f, 0.04f, gmD, 0.4f, 0.6f); Cy(t, -0.185f, 0.03f, -0.026f, 0.014f, 0.006f, st, 2, 0.6f, 0.9f);
            }),
            P("Bolt", t =>
            {
                Box(t, -0.06f, 0.012f, -0.012f, 0.1f, 0.026f, 0.03f, st, 0.8f, 0.95f); Cy(t, -0.02f, 0.012f, -0.012f, 0.02f, 0.03f, H(0xb23a2e), 0, 0.4f); Cy(t, 0.0f, 0.012f, -0.012f, 0.021f, 0.012f, brass, 0, 0.7f, 0.9f);
            }),
            P("Barrel and magazine tube", t =>
            {
                Cy(t, 0.28f, 0.022f, 0f, 0.036f, 0.47f, gm, 0, 0.6f, 0.85f); Cy(t, 0.225f, -0.022f, 0f, 0.032f, 0.36f, gm, 0, 0.6f, 0.85f); Cy(t, 0.408f, -0.022f, 0f, 0.036f, 0.012f, stD, 0, 0.6f, 0.9f);
                Box(t, 0.385f, 0f, 0f, 0.014f, 0.078f, 0.042f, gmD, 0.4f, 0.6f); Cy(t, 0.5f, 0.046f, 0f, 0.009f, 0.01f, brass, 1, 0.7f, 0.9f); Cy(t, 0.5165f, 0.022f, 0f, 0.026f, 0.004f, H(0x050505), 0);
                Box(t, 0.28f, 0.043f, 0f, 0.44f, 0.005f, 0.012f, gmD, 0.4f, 0.6f);
            }),
            P("Pump forend", t =>
            {
                Cy(t, 0.2f, -0.022f, 0f, 0.056f, 0.175f, wd, 0, 0.35f); for (int i = 0; i < 7; i++) Cy(t, 0.132f + i * 0.0225f, -0.022f, 0f, 0.06f, 0.007f, wdD, 0, 0.3f);
                Box(t, 0.09f, -0.022f, -0.03f, 0.1f, 0.01f, 0.006f, stD, 0.6f, 0.9f);
            }),
            P("Stock", t =>
            {
                Pr(t, new[] { -0.21f, 0.04f, -0.21f, -0.04f, -0.3f, -0.065f, -0.5f, -0.115f, -0.5f, 0.012f, -0.32f, 0.032f }, 0.044f, wd, 0.35f);
                Box(t, -0.507f, -0.052f, 0f, 0.016f, 0.132f, 0.046f, rubber, 0.1f); for (int i = 0; i < 5; i++) Rot(Box(t, -0.27f, -0.005f - i * 0.012f, -0.0225f, 0.07f, 0.004f, 0.003f, wdD), 0f, 0f, 12f);
            }),
        };

        static WPart[] Rifle() => new[]
        {
            P("Receiver", t =>
            {
                Box(t, -0.1f, 0f, 0f, 0.3f, 0.07f, 0.046f, gm, 0.4f, 0.7f); Pr(t, new[] { -0.2f, -0.035f, -0.14f, -0.035f, -0.172f, -0.165f, -0.238f, -0.152f }, 0.032f, wd, 0.3f);
                TriggerGuard(t, -0.14f, -0.05f, -0.075f, gm); Rot(Box(t, -0.105f, -0.045f, 0f, 0.012f, 0.045f, 0.012f, st, 0.6f, 0.9f), 0f, 0f, 14f);
                Box(t, 0.065f, 0.045f, 0f, 0.05f, 0.022f, 0.03f, gmD, 0.4f, 0.6f); Box(t, -0.1f, -0.012f, -0.0235f, 0.06f, 0.01f, 0.004f, stD, 0.6f, 0.9f);
                for (int i = 0; i < 3; i++) Cy(t, -0.2f + i * 0.1f, 0.01f, -0.0235f, 0.012f, 0.004f, stD, 2, 0.6f, 0.9f);
            }),
            P("Bolt carrier", t =>
            {
                Box(t, -0.09f, 0.045f, 0f, 0.24f, 0.024f, 0.03f, st, 0.8f, 0.95f); Cy(t, -0.0f, 0.045f, -0.03f, 0.014f, 0.04f, st, 2, 0.8f, 0.95f); Cy(t, 0.07f, 0.045f, 0f, 0.016f, 0.1f, stD, 0, 0.7f, 0.9f);
                Pr(t, new[] { -0.25f, 0.035f, 0.05f, 0.035f, 0.04f, 0.066f, -0.24f, 0.066f }, 0.044f, gmD, 0.35f, 0.6f, 0.004f); for (int i = 0; i < 4; i++) Box(t, -0.2f + i * 0.05f, 0.068f, 0f, 0.008f, 0.004f, 0.04f, gm, 0.4f, 0.7f);
            }),
            P("Barrel and gas tube", t =>
            {
                Cy(t, 0.31f, 0f, 0f, 0.024f, 0.52f, gm, 0, 0.6f, 0.85f); Cy(t, 0.2f, 0.034f, 0f, 0.02f, 0.24f, gm, 0, 0.6f, 0.85f); Box(t, 0.325f, 0.018f, 0f, 0.03f, 0.055f, 0.03f, gmD, 0.4f, 0.6f);
                Box(t, 0.5f, 0.032f, 0f, 0.022f, 0.075f, 0.02f, gmD, 0.4f, 0.6f); Box(t, 0.5f, 0.075f, 0f, 0.03f, 0.012f, 0.03f, gmD, 0.4f, 0.6f); Cy(t, 0.575f, 0f, 0f, 0.032f, 0.045f, gmD, 0, 0.4f, 0.6f); Cy(t, 0.599f, 0f, 0f, 0.016f, 0.004f, H(0x050505), 0);
            }),
            P("Handguard", t =>
            {
                Box(t, 0.175f, -0.012f, 0f, 0.2f, 0.046f, 0.05f, wd, 0.35f); Box(t, 0.175f, 0.036f, 0f, 0.19f, 0.03f, 0.042f, wd, 0.35f);
                foreach (float z in new[] { -0.0255f, 0.0255f }) for (int i = 0; i < 3; i++) Box(t, 0.12f + i * 0.055f, -0.012f, z, 0.035f, 0.01f, 0.003f, wdD);
                Box(t, 0.08f, 0.012f, 0f, 0.012f, 0.085f, 0.052f, gmD, 0.4f, 0.6f); Box(t, 0.272f, 0.012f, 0f, 0.012f, 0.085f, 0.052f, gmD, 0.4f, 0.6f);
            }),
            P("Stock", t =>
            {
                Pr(t, new[] { -0.25f, 0.032f, -0.25f, -0.032f, -0.52f, -0.1f, -0.52f, 0.0f }, 0.042f, wd, 0.35f); Box(t, -0.526f, -0.05f, 0f, 0.014f, 0.105f, 0.044f, stD, 0.5f, 0.8f);
                Cy(t, -0.44f, -0.04f, -0.0215f, 0.03f, 0.003f, wdD, 2);
            }),
            P("Magazine", t =>
            {
                Rot(Box(t, -0.02f, -0.075f, 0f, 0.062f, 0.085f, 0.03f, gmD, 0.45f, 0.7f), 0f, 0f, 8f); Rot(Box(t, -0.004f, -0.15f, 0f, 0.062f, 0.085f, 0.03f, gmD, 0.45f, 0.7f), 0f, 0f, 18f); Rot(Box(t, 0.026f, -0.222f, 0f, 0.062f, 0.085f, 0.03f, gmD, 0.45f, 0.7f), 0f, 0f, 28f);
                for (int i = 0; i < 3; i++) Rot(Box(t, -0.012f + i * 0.02f, -0.11f - i * 0.045f, -0.0155f, 0.05f, 0.006f, 0.003f, gm, 0.4f, 0.7f), 0f, 0f, 13f + i * 6f);
            }),
        };

        // =================================================================================================== COPPER STILL
        static readonly Vector3 Pot = new Vector3(-0.28f, TableY, 0.2f), Spot = new Vector3(0.36f, TableY, -0.1f);

        public static void Still(Transform r)
        {
            float x = Pot.x, y = Pot.y, z = Pot.z;
            // fire box with the burner flame, then the riveted copper boiler with its onion head
            Box(r, x, y + 0.045f, z, 0.36f, 0.09f, 0.36f, H(0x5b3a2c), 0.05f); Box(r, x, y + 0.05f, z - 0.181f, 0.16f, 0.06f, 0.004f, H(0x120c0a));
            Ball(Node(r, "Flame", x, y + 0.03f, z - 0.17f), 0f, 0.03f, 0f, 0.1f, 0.09f, 0.05f, H(0xff9a2e), 0.1f, 0f, "FlameCore", 2.2f);
            La(r, x, y + 0.09f, z, new[] { 0f, 0f, 0.15f, 0f, 0.175f, 0.05f, 0.175f, 0.2f, 0.13f, 0.27f, 0.06f, 0.3f, 0.045f, 0.34f, 0.085f, 0.4f, 0.09f, 0.45f, 0.05f, 0.52f, 0.028f, 0.56f }, copper, 0.7f, 0.9f, "Boiler");
            foreach (float yy in new[] { 0.15f, 0.28f }) Cy(r, x, y + yy, z, 0.356f, 0.012f, copperD, 1, 0.6f, 0.9f);
            for (int i = 0; i < 12; i++) { float a = i * Mathf.PI / 6f; Ball(r, x + Mathf.Cos(a) * 0.177f, y + 0.22f, z + Mathf.Sin(a) * 0.177f, 0.014f, 0.014f, 0.014f, brass, 0.7f, 0.9f); }
            // thermometer dial facing the player
            Cy(r, x, y + 0.2f, z - 0.178f, 0.09f, 0.012f, brass, 2, 0.7f, 0.9f); Cy(r, x, y + 0.2f, z - 0.185f, 0.076f, 0.004f, white, 2);
            Box(Node(r, "Needle", x, y + 0.2f, z - 0.189f), 0f, 0.016f, 0f, 0.005f, 0.036f, 0.002f, H(0xb21f1f));
            // swan neck over to the condenser (a barrel of cold water with the worm inside) and the spout
            Rot(Cy(r, x + 0.13f, y + 0.69f, z, 0.03f, 0.3f, copper, 0, 0.7f, 0.9f), 0f, 0f, -14f); Cy(r, x + 0.28f, y + 0.62f, z, 0.03f, 0.1f, copper, 1, 0.7f, 0.9f); Ball(r, x, y + 0.66f, z, 0.05f, 0.05f, 0.05f, copper, 0.7f, 0.9f); Ball(r, x + 0.28f, y + 0.66f, z, 0.04f, 0.04f, 0.04f, copper, 0.7f, 0.9f);
            float cx = Spot.x, cz = z;
            Rot(Cy(r, (x + 0.28f + cx) * 0.5f, y + 0.565f, z, 0.03f, cx - x - 0.26f, copper, 0, 0.7f, 0.9f), 0f, 0f, 4f);
            La(r, cx, y, cz, new[] { 0f, 0f, 0.13f, 0f, 0.145f, 0.2f, 0.145f, 0.32f, 0.13f, 0.52f, 0.115f, 0.52f, 0.115f, 0.5f }, H(0x6b4a2c), 0.2f, 0f, "Tub");
            foreach (float yy in new[] { 0.08f, 0.26f, 0.44f }) Cy(r, cx, y + yy, cz, 0.3f, 0.02f, stD, 1, 0.5f, 0.8f);
            Cy(r, cx, y + 0.5f, cz, 0.228f, 0.004f, H(0x6fa6c4, 0.75f), 1, 0.9f);
            for (int i = 0; i < 4; i++) Cy(r, cx, y + 0.515f + i * 0.0f, cz, 0.12f - i * 0.02f, 0.012f + i * 0.001f, copper, 1, 0.7f, 0.9f);
            Cy(r, cx, y + 0.44f, cz - 0.2f, 0.026f, 0.14f, copper, 2, 0.7f, 0.9f); Cy(r, cx, y + 0.415f, cz - 0.265f, 0.026f, 0.06f, copper, 1, 0.7f, 0.9f); Box(r, cx, y + 0.475f, cz - 0.22f, 0.07f, 0.012f, 0.02f, H(0xb23a2e), 0.4f);
            // slate mat where the jar stands
            Cy(r, Spot.x, y + 0.006f, Spot.z, 0.26f, 0.012f, H(0x2f3a3a), 1, 0.1f);
        }

        static void Sack(Transform t, float dx, Color c, Color band)
        {
            float x = Pot.x + dx, y = Pot.y + 0.68f, z = Pot.z;
            Box(t, x, y + 0.08f, z, 0.13f, 0.16f, 0.07f, c, 0.05f); Box(t, x, y + 0.17f, z, 0.1f, 0.03f, 0.05f, new Color(c.r * 0.85f, c.g * 0.85f, c.b * 0.85f), 0.05f); Box(t, x, y + 0.085f, z - 0.036f, 0.09f, 0.06f, 0.003f, band);
        }
        static void Label(Transform t, float r, float y, float w, float h, Color paper, Color band, bool xxx)
        {
            float x = Spot.x, z = Spot.z - r - 0.002f;
            Box(t, x, y, z, w, h, 0.004f, paper, 0.1f); Box(t, x, y + h * 0.3f, z - 0.003f, w * 0.86f, h * 0.14f, 0.002f, band); Box(t, x, y - h * 0.32f, z - 0.003f, w * 0.6f, h * 0.06f, 0.002f, ink);
            if (xxx) for (int i = -1; i <= 1; i++) { Rot(Box(t, x + i * w * 0.24f, y - h * 0.04f, z - 0.003f, h * 0.3f, h * 0.06f, 0.002f, ink), 0f, 0f, 45f); Rot(Box(t, x + i * w * 0.24f, y - h * 0.04f, z - 0.003f, h * 0.3f, h * 0.06f, 0.002f, ink), 0f, 0f, -45f); }
            else Cy(t, x, y - h * 0.05f, z - 0.003f, h * 0.36f, 0.002f, band, 2);
        }
        static void Vessel(Transform t, float[] glass, Color glassCol, float[] liquid, Color liqCol, float level = 0.001f)
        {
            La(t, Spot.x, Spot.y + 0.012f, Spot.z, glass, glassCol, 0.95f, 0f, "Glass");
            var liq = Node(t, "Liquid", Spot.x, Spot.y + 0.02f, Spot.z); liq.localScale = new Vector3(1f, level, 1f); La(liq, 0f, 0f, 0f, liquid, liqCol, 0.8f, 0f, "LiquidMesh");
        }

        static WorkDef Liquor(int kind)
        {
            Vector3 spout = new Vector3(Spot.x, TableY + 0.385f, Spot.z + 0.035f), mouth;
            var parts = new List<WPart>(); var steps = new List<WStep>(); string title;
            Color corn = H(0xd9b24a), sugar = H(0xeeeeea), yeast = H(0xb98a55);
            const string drag = "Drag it from the tray onto the glowing outline.";
            if (kind == 0)
            {
                title = "Moonshine"; mouth = new Vector3(Spot.x, TableY + 0.3f, Spot.z);
                parts.Add(P("Corn mash", t => Sack(t, 0f, corn, H(0x6e8b3d)), true)); parts.Add(P("Sugar", t => Sack(t, 0f, sugar, H(0x3a6fc4)), true)); parts.Add(P("Yeast", t => Sack(t, 0f, yeast, H(0x8a2f22)), true));
                parts.Add(P("Mason jar", t => Vessel(t, new[] { 0f, 0f, 0.085f, 0f, 0.09f, 0.02f, 0.09f, 0.19f, 0.07f, 0.225f, 0.07f, 0.262f, 0.074f, 0.262f, 0.074f, 0.27f }, H(0xbfe3e6, 0.42f), new[] { 0f, 0f, 0.08f, 0f, 0.084f, 0.02f, 0.084f, 0.215f }, H(0xcfe6ea, 0.45f))));
                parts.Add(P("Lid", t => { Cy(t, Spot.x, TableY + 0.292f, Spot.z, 0.158f, 0.028f, H(0xc9b25a), 1, 0.7f, 0.9f); Cy(t, Spot.x, TableY + 0.308f, Spot.z, 0.13f, 0.006f, H(0xa8923f), 1, 0.7f, 0.9f); }));
                parts.Add(P("Label", t => Label(t, 0.09f, TableY + 0.115f, 0.12f, 0.1f, cream, H(0x8a2f22), true)));
            }
            else if (kind == 1)
            {
                title = "Barrel whiskey"; mouth = new Vector3(Spot.x, TableY + 0.395f, Spot.z);
                parts.Add(P("Corn mash", t => Sack(t, 0f, corn, H(0x6e8b3d)), true)); parts.Add(P("Sugar", t => Sack(t, 0f, sugar, H(0x3a6fc4)), true)); parts.Add(P("Yeast", t => Sack(t, 0f, yeast, H(0x8a2f22)), true));
                parts.Add(P("Bottle", t => Vessel(t, new[] { 0f, 0f, 0.066f, 0f, 0.07f, 0.015f, 0.07f, 0.21f, 0.05f, 0.265f, 0.026f, 0.29f, 0.026f, 0.365f, 0.031f, 0.365f, 0.031f, 0.382f }, H(0x8a5a2a, 0.5f), new[] { 0f, 0f, 0.062f, 0f, 0.065f, 0.015f, 0.065f, 0.22f }, H(0xc06a14, 0.9f))));
                parts.Add(P("Charred oak chips", t => { for (int i = 0; i < 6; i++) Rot(Box(t, Spot.x + (i % 3 - 1) * 0.022f, TableY + 0.43f + (i / 3) * 0.022f, Spot.z, 0.02f, 0.012f, 0.03f, i % 2 == 0 ? H(0x3a2416) : H(0x1c120c), 0.05f), i * 20f, i * 33f, 0f); }, true));
                parts.Add(P("Cork", t => { Cy(t, Spot.x, TableY + 0.4f, Spot.z, 0.05f, 0.04f, cork, 1, 0.1f); Cy(t, Spot.x, TableY + 0.425f, Spot.z, 0.062f, 0.014f, H(0x3a2416), 1, 0.2f); }));
                parts.Add(P("Label", t => Label(t, 0.07f, TableY + 0.125f, 0.105f, 0.12f, cream, H(0x2a2420), false)));
            }
            else
            {
                title = "Bathtub gin"; mouth = new Vector3(Spot.x, TableY + 0.415f, Spot.z);
                parts.Add(P("Sugar", t => Sack(t, 0f, sugar, H(0x3a6fc4)), true)); parts.Add(P("Yeast", t => Sack(t, 0f, yeast, H(0x8a2f22)), true));
                parts.Add(P("Botanicals", t => { Box(t, Pot.x, Pot.y + 0.71f, Pot.z, 0.13f, 0.06f, 0.09f, H(0x7a5a36), 0.05f); for (int i = 0; i < 9; i++) Ball(t, Pot.x + (i % 3 - 1) * 0.034f, Pot.y + 0.75f, Pot.z + (i / 3 - 1) * 0.024f, 0.026f, 0.026f, 0.026f, i % 4 == 0 ? H(0x3f7a3a) : H(0x2b2f6a), 0.3f); }, true));
                parts.Add(P("Bottle", t => Vessel(t, new[] { 0f, 0f, 0.055f, 0f, 0.06f, 0.012f, 0.06f, 0.24f, 0.045f, 0.29f, 0.024f, 0.32f, 0.024f, 0.385f, 0.028f, 0.385f, 0.028f, 0.4f }, H(0x8fd0b4, 0.45f), new[] { 0f, 0f, 0.052f, 0f, 0.055f, 0.012f, 0.055f, 0.25f }, H(0xd6f0ea, 0.75f))));
                parts.Add(P("Screw cap", t => { Cy(t, Spot.x, TableY + 0.418f, Spot.z, 0.064f, 0.04f, H(0xc8ccd2), 1, 0.8f, 0.95f); for (int i = 0; i < 10; i++) { float a = i * Mathf.PI / 5f; Box(t, Spot.x + Mathf.Cos(a) * 0.032f, TableY + 0.418f, Spot.z + Mathf.Sin(a) * 0.032f, 0.004f, 0.034f, 0.004f, H(0x8d9299), 0.7f, 0.9f); } }));
                parts.Add(P("Label", t => Label(t, 0.06f, TableY + 0.135f, 0.095f, 0.13f, white, H(0x1f6b63), false)));
            }
            int n = 0;
            Vector3 neck = new Vector3(Pot.x, Pot.y + 0.7f, Pot.z);
            for (; n < 3; n++) { string pn = parts[n].name; bool y2 = pn == "Yeast", bot = pn == "Botanicals"; steps.Add(Pour("Load the still: " + pn.ToLower(), n, null, bot ? 12f : y2 ? 20f : pn == "Sugar" ? 300f : 500f, bot ? "berries" : "g", neck, bot ? H(0x2b2f6a) : pn == "Sugar" ? sugar : y2 ? yeast : corn, true, bot ? 1f : y2 ? 2f : pn == "Sugar" ? 20f : 25f)); }
            steps.Add(S(WK.Heat, "Run the still", "Hold SPACE (or the mouse button) to feed the fire. Keep the needle inside the green band.", -1, kind == 0 ? 9f : 11f, "Flame"));
            steps.Add(S(WK.Place, "Stand the " + parts[3].name.ToLower() + " under the spout", drag, 3));
            steps.Add(S(WK.Fill, "Fill it to the line", "Scroll DOWN to open the tap, scroll up to close it. Stop at the mark, close the tap and press ENTER.", -1, 0.86f, "Liquid", null, null, null, spout));
            if (kind == 1)
            {
                steps.Add(Pour("Drop in the charred oak", 4, null, 6f, "chips", mouth, H(0x2a1a10), true));
                steps.Add(S(WK.Place, "Cork it", drag, 5));
                steps.Add(S(WK.Shake, "Shake the oak through", "Grab the bottle and flick it the way the arrows show.", 3, 6f));
                steps.Add(S(WK.Place, "Hang the label", drag, 6));
            }
            else if (kind == 2)
            {
                steps.Add(Pour("Add the essence: 6 drops", -1, "Essence", 6f, "drops", mouth, H(0x55d06a), false));
                steps.Add(S(WK.Place, "Screw the cap on", drag, 4));
                steps.Add(S(WK.Place, "Hang the label", drag, 5));
            }
            else
            {
                steps.Add(S(WK.Place, "Close the lid", drag, 4));
                steps.Add(S(WK.Place, "Hang the label", drag, 5));
            }
            steps.Add(S(WK.Stretch, "Smooth the label", "Pull both ends out evenly until it sits flat."));
            if (kind == 1) steps.Add(S(WK.Crank, "Press the wax seal", "Grab the hand wheel and turn it in circles.", -1, 2f, "Crank"));
            steps.Add(S(WK.Pack, "Crate it", "Drag the finished bottle into the crate."));
            return new WorkDef
            {
                title = title, parts = parts.ToArray(), steps = steps.ToArray(), origin = Vector3.zero,
                apparatus = r =>
                {
                    Still(r);
                    if (kind == 2)
                    {
                        var e = Node(r, "Essence", 0.62f, TableY, -0.34f); e.localScale = Vector3.one * 1.5f;
                        La(e, 0f, 0f, 0f, new[] { 0f, 0f, 0.028f, 0f, 0.03f, 0.008f, 0.03f, 0.07f, 0.012f, 0.085f, 0.012f, 0.105f }, H(0x4f8f5a, 0.7f), 0.9f); Cy(e, 0f, 0.113f, 0f, 0.018f, 0.016f, H(0x1c1c1e)); Box(e, 0f, 0.04f, -0.031f, 0.036f, 0.03f, 0.002f, white);
                    }
                    if (kind == 1)
                    {
                        Box(r, 0.8f, TableY + 0.05f, 0.24f, 0.2f, 0.1f, 0.16f, stD, 0.4f, 0.6f); Box(r, 0.8f, TableY + 0.2f, 0.28f, 0.05f, 0.2f, 0.05f, st, 0.5f, 0.8f);
                        var crank = Node(r, "Crank", 0.8f, TableY + 0.26f, 0.15f); Cy(crank, 0f, 0f, 0f, 0.18f, 0.02f, H(0x8a2f22), 2, 0.4f, 0.3f); Cy(crank, 0f, 0f, 0f, 0.045f, 0.04f, st, 2, 0.6f, 0.9f);
                        for (int i = 0; i < 3; i++) Rot(Box(crank, 0f, 0f, -0.004f, 0.17f, 0.016f, 0.014f, st, 0.6f, 0.9f), 0f, 0f, i * 60f); Cy(crank, 0.07f, 0f, -0.035f, 0.028f, 0.05f, H(0x1c1c1e), 2, 0.3f);
                    }
                }
            };
        }

        // =================================================================================================== LAB BENCH
        static readonly Vector3 Burn = new Vector3(0.05f, TableY, 0.16f), Mort = new Vector3(0.5f, TableY, -0.12f), Scale = new Vector3(-0.3f, TableY, -0.22f), TrayPos = new Vector3(0.1f, TableY, -0.28f);

        static readonly Vector3 BagMouth = new Vector3(Scale.x, Scale.y + 0.145f, Scale.z + 0.015f), MortMouth = new Vector3(Mort.x, Mort.y + 0.11f, Mort.z);
        static WStep Pre(WStep s, string pre) { s.pre = pre; return s; }
        static WStep On(WStep s, string on) { s.on = on; return s; }
        static WStep Off(WStep s, string off) { s.off = off; return s; }
        static void Burner(Transform r)
        {
            float x = Burn.x, y = Burn.y, z = Burn.z;
            Cy(r, x, y + 0.01f, z, 0.12f, 0.02f, H(0x2a2c30), 1, 0.5f, 0.7f); Cy(r, x, y + 0.07f, z, 0.03f, 0.1f, st, 1, 0.7f, 0.9f); Cy(r, x, y + 0.035f, z, 0.045f, 0.02f, brass, 1, 0.7f, 0.9f);
            Rot(Cy(r, x + 0.14f, y + 0.02f, z + 0.02f, 0.016f, 0.2f, H(0xc65a2a), 0, 0.3f), 0f, 12f, 0f);
            var f = Node(r, "Flame", x, y + 0.12f, z); Ball(f, 0f, 0.035f, 0f, 0.035f, 0.08f, 0.035f, H(0x3fa8ff), 0.1f, 0f, "FlameCore", 2.5f); Ball(f, 0f, 0.02f, 0f, 0.02f, 0.04f, 0.02f, H(0xcfeaff), 0.1f, 0f, "FlameTip", 3f);
            Cy(r, x, y + 0.2f, z, 0.2f, 0.008f, stD, 1, 0.5f, 0.8f); Cy(r, x, y + 0.203f, z, 0.15f, 0.004f, H(0xd8d4c8), 1, 0.1f);
            for (int i = 0; i < 3; i++) { float a = i * Mathf.PI * 2f / 3f + 0.5f; Rot(Box(r, x + Mathf.Cos(a) * 0.1f, y + 0.1f, z + Mathf.Sin(a) * 0.1f, 0.008f, 0.2f, 0.008f, stD, 0.5f, 0.8f), 0f, 0f, 0f); }
        }
        static void Mortar(Transform r, Color content)
        {
            float x = Mort.x, y = Mort.y, z = Mort.z; var m = Node(r, "Mortar", x, y + 0.06f, z);
            La(m, 0f, -0.06f, 0f, new[] { 0f, 0f, 0.06f, 0f, 0.07f, 0.015f, 0.105f, 0.1f, 0.09f, 0.1f, 0.055f, 0.03f, 0f, 0.03f }, stone, 0.15f, 0f, "Bowl");
            var c = Node(r, "MortarContent", x, y + 0.035f, z); for (int i = 0; i < 9; i++) { float a = i * 2.4f, rad = 0.012f + 0.03f * (i % 3) / 2f; Box(c, Mathf.Cos(a) * rad, 0.006f + (i % 2) * 0.006f, Mathf.Sin(a) * rad, 0.02f, 0.012f, 0.016f, content, 0.1f); }
            var p = Node(r, "Pestle", x + 0.03f, y + 0.045f, z); Rot(Cy(p, 0.02f, 0.07f, 0f, 0.026f, 0.15f, H(0xb9b3a6), 1, 0.2f), 0f, 0f, -16f); Ball(p, 0f, 0.005f, 0f, 0.04f, 0.035f, 0.04f, H(0xb9b3a6), 0.2f);
        }
        static void ScaleUnit(Transform r)
        {
            float x = Scale.x, y = Scale.y, z = Scale.z;
            Box(r, x, y + 0.015f, z, 0.22f, 0.03f, 0.17f, H(0x25272b), 0.4f, 0.3f); Cy(r, x, y + 0.036f, z + 0.015f, 0.15f, 0.008f, H(0xc8ccd2), 1, 0.8f, 0.9f);
            Box(r, x, y + 0.02f, z - 0.086f, 0.09f, 0.018f, 0.002f, H(0x7dff8a), 0.3f, 0f, "Display", 1.6f);
        }
        static void Bag(Transform t, Color fill)
        {
            float x = Scale.x, y = Scale.y + 0.042f, z = Scale.z + 0.015f;
            Box(t, x, y + 0.045f, z, 0.1f, 0.09f, 0.035f, H(0xdfe8ee, 0.28f), 0.9f); Box(t, x, y + 0.094f, z, 0.104f, 0.008f, 0.012f, H(0x3a6fc4), 0.4f);
            var f = Node(t, "BagFill", x, y + 0.003f, z); f.localScale = new Vector3(1f, 0.001f, 1f); Box(f, 0f, 0.04f, 0f, 0.09f, 0.08f, 0.027f, fill, 0.15f);
        }
        static void Flask(Transform t, Color liquid)
        {
            float x = Burn.x, y = Burn.y + 0.206f, z = Burn.z;
            La(t, x, y, z, new[] { 0f, 0f, 0.085f, 0f, 0.09f, 0.012f, 0.03f, 0.16f, 0.03f, 0.235f, 0.036f, 0.24f, 0.036f, 0.248f }, H(0xcfe8ee, 0.4f), 0.95f, 0f, "Glass");
            var l = Node(t, "Liquid", x, y + 0.006f, z); l.localScale = new Vector3(1f, 0.001f, 1f); La(l, 0f, 0f, 0f, new[] { 0f, 0f, 0.082f, 0f, 0.084f, 0.01f, 0.05f, 0.1f }, liquid, 0.8f, 0f, "LiquidMesh");
            for (int i = 0; i < 4; i++) Box(t, x - 0.02f - i * 0.011f, y + 0.03f + i * 0.026f, z - 0.086f + i * 0.0145f, 0.02f, 0.003f, 0.002f, white);   // graduation marks
        }
        static void Jar(Transform t, float x, float y, float z, Color content, Color lid)
        {
            La(t, x, y, z, new[] { 0f, 0f, 0.045f, 0f, 0.045f, 0.09f, 0.036f, 0.1f, 0.036f, 0.112f }, H(0xe6eef0, 0.3f), 0.9f); Cy(t, x, y + 0.04f, z, 0.08f, 0.07f, content, 1, 0.1f); Cy(t, x, y + 0.122f, z, 0.082f, 0.02f, lid, 1, 0.4f);
        }
        static void Lamp(Transform r)
        {
            Cy(r, 0.86f, TableY + 0.012f, 0.3f, 0.16f, 0.024f, H(0x25272b), 1, 0.4f, 0.5f); Rot(Cy(r, 0.82f, TableY + 0.25f, 0.3f, 0.02f, 0.5f, H(0x25272b), 1, 0.4f, 0.5f), 0f, 0f, 10f);
            La(r, 0.72f, TableY + 0.47f, 0.3f, new[] { 0.09f, 0f, 0.03f, 0.08f, 0f, 0.08f }, H(0x2f6f5f), 0.5f, 0.3f); Cy(r, 0.72f, TableY + 0.468f, 0.3f, 0.16f, 0.004f, H(0xfff2c8), 1, 0.2f, 0f, "Bulb", 1.5f);
        }

        static WorkDef Weed()
        {
            float px = 0.08f, pz = 0.05f, y = TableY;
            var parts = new[]
            {
                P("Potted plant", t =>
                {
                    La(t, px, y, pz, new[] { 0f, 0f, 0.075f, 0f, 0.1f, 0.14f, 0.108f, 0.14f, 0.108f, 0.16f }, H(0xa55a34), 0.15f); Cy(t, px, y + 0.15f, pz, 0.2f, 0.012f, H(0x3a2a1c), 1, 0.05f);
                    Cy(t, px, y + 0.36f, pz, 0.018f, 0.42f, H(0x5f8a3a), 1, 0.2f);
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i * 2.4f, h = 0.26f + i * 0.045f, rad = 0.075f - i * 0.006f; var bud = Node(t, "Bud" + i, px + Mathf.Cos(a) * rad, y + h, pz + Mathf.Sin(a) * rad * 0.6f);
                        Ball(bud, 0f, 0f, 0f, 0.055f, 0.085f, 0.055f, H(0x6fa63f), 0.15f); Ball(bud, 0.012f, 0.03f, 0f, 0.035f, 0.05f, 0.035f, H(0x8fc24f), 0.15f); Ball(bud, -0.01f, -0.01f, -0.02f, 0.02f, 0.02f, 0.02f, H(0xd98a3a), 0.15f);
                    }
                    var top = Node(t, "Bud7", px, y + 0.6f, pz); Ball(top, 0f, 0f, 0f, 0.07f, 0.13f, 0.07f, H(0x6fa63f), 0.15f); Ball(top, 0.01f, 0.05f, -0.01f, 0.04f, 0.07f, 0.04f, H(0x8fc24f), 0.15f);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f + 0.3f, h = 0.24f + (i % 4) * 0.085f; var leaf = Node(t, "Leaf" + i, px + Mathf.Cos(a) * 0.15f, y + h, pz - 0.03f + Mathf.Sin(a) * 0.05f);
                        leaf.localRotation = Quaternion.Euler(0f, 0f, Mathf.Cos(a) > 0f ? -20f : 20f);
                        for (int k = -2; k <= 2; k++) Rot(Box(leaf, k * 0.012f, -Mathf.Abs(k) * 0.008f, 0f, 0.016f, 0.1f - Mathf.Abs(k) * 0.022f, 0.004f, H(0x2f6b2a), 0.2f), 0f, 0f, -k * 24f);
                    }
                }),
                P("Zip bag", t => Bag(t, H(0x6fa63f))),
            };
            var steps = new[]
            {
                S(WK.Place, "Set the plant on the bench", "Drag the pot from the tray onto the glowing ring.", 0),
                S(WK.Clicks, "Trim the fan leaves", "Move the scissors over each dark fan leaf and click to snip it. Don't cut the buds.", -1, 16f, "Leaf", "Bud"),
                S(WK.Timing, "Dry and cure", "Pull it off the line at the right moment: stop the marker in the green zone (SPACE)."),
                S(WK.Place, "Put a zip bag on the scale", "Drag it onto the scale.", 1),
                Pre(Pour("Weigh out the bag: 28 g", -1, "BudBowl", 28f, "g", BagMouth, H(0x6fa63f), true, 2f, "BagFill", 0.8f), "BudBowl,-Bud0,-Bud1,-Bud2,-Bud3,-Bud4,-Bud5,-Bud6,-Bud7"),
                S(WK.Pack, "Box it", "Drag the bag from the scale into the crate on the right.", 1),
            };
            return new WorkDef { title = "Weed", parts = parts, steps = steps, origin = Vector3.zero, hidden = new[] { "BudBowl" }, apparatus = r => { ScaleUnit(r); Lamp(r);
                var bb = Node(r, "BudBowl", 0.47f, TableY, 0.02f); La(bb, 0f, 0f, 0f, new[] { 0f, 0f, 0.05f, 0f, 0.085f, 0.05f, 0.09f, 0.055f, 0.078f, 0.05f, 0.045f, 0.012f, 0f, 0.012f }, st, 0.7f, 0.9f);
                for (int i = 0; i < 7; i++) { float a = i * 0.9f; Ball(bb, Mathf.Cos(a) * (i == 0 ? 0f : 0.04f), 0.045f + (i % 2) * 0.012f, Mathf.Sin(a) * (i == 0 ? 0f : 0.04f), 0.04f, 0.036f, 0.04f, i % 2 == 0 ? H(0x6fa63f) : H(0x8fc24f), 0.15f); } Box(r, 0.55f, TableY + 0.008f, -0.22f, 0.16f, 0.016f, 0.05f, st, 0.7f, 0.9f); Cy(r, 0.47f, TableY + 0.01f, -0.22f, 0.05f, 0.012f, H(0xb23a2e), 1, 0.3f); } };
        }

        static WorkDef Pills()
        {
            float px = 0.05f, pz = 0.15f, y = TableY;
            var parts = new[]
            {
                P("Cold tablets", t => { float x = Mort.x, yy = Mort.y + 0.13f, z = Mort.z; Box(t, x, yy, z, 0.11f, 0.008f, 0.07f, H(0xc8ccd2), 0.8f, 0.9f); for (int i = 0; i < 8; i++) Cy(t, x - 0.036f + (i % 4) * 0.024f, yy + 0.007f, z - 0.014f + (i / 4) * 0.028f, 0.018f, 0.008f, white, 1, 0.3f); }, true),
                P("Binder", t => { float x = Mort.x, yy = Mort.y + 0.13f, z = Mort.z; La(t, x, yy, z, new[] { 0f, 0f, 0.03f, 0f, 0.03f, 0.08f, 0.012f, 0.095f, 0.012f, 0.115f }, H(0x9a6a2a, 0.6f), 0.9f); Cy(t, x, yy + 0.122f, z, 0.03f, 0.016f, H(0x1c1c1e)); Box(t, x, yy + 0.04f, z - 0.031f, 0.04f, 0.035f, 0.002f, white); }, true),
                P("Pill bottle", t =>
                {
                    float x = Scale.x, yy = Scale.y + 0.042f, z = Scale.z + 0.015f;
                    La(t, x, yy, z, new[] { 0f, 0f, 0.04f, 0f, 0.04f, 0.09f, 0.034f, 0.095f, 0.034f, 0.108f }, H(0xe07a1c, 0.45f), 0.9f, 0f, "Glass"); Box(t, x, yy + 0.045f, z - 0.041f, 0.06f, 0.05f, 0.002f, white);
                    var f = Node(t, "BottleFill", x, yy + 0.004f, z); f.localScale = new Vector3(1f, 0.001f, 1f); Cy(f, 0f, 0.04f, 0f, 0.07f, 0.08f, H(0xf4f2ec), 1, 0.2f);
                }),
                P("Cap", t => { float x = Scale.x, yy = Scale.y + 0.042f, z = Scale.z + 0.015f; Cy(t, x, yy + 0.118f, z, 0.078f, 0.026f, white, 1, 0.4f); for (int i = 0; i < 12; i++) { float a = i * Mathf.PI / 6f; Box(t, x + Mathf.Cos(a) * 0.039f, yy + 0.118f, z + Mathf.Sin(a) * 0.039f, 0.004f, 0.022f, 0.004f, H(0xd0d0cc)); } }),
            };
            var steps = new[]
            {
                On(Pour("Tip the tablets into the mortar", 0, null, 8f, "tablets", MortMouth, white, true), "MortarContent"),
                S(WK.Grind, "Grind to a fine powder", "Grab the pestle and stir in steady circles. Too fast and powder flies out.", -1, 3f, "Mortar", "Pestle"),
                Pour("Add the binder: 5 drops", 1, null, 5f, "drops", MortMouth, H(0xc98a2a), false),
                S(WK.Crank, "Stamp the tablets", "Grab the press wheel and turn it in circles.", -1, 2.5f, "Crank", null, "Pressed", "MortarContent"),
                S(WK.Place, "Put a pill bottle on the scale", "Drag it onto the scale.", 2),
                Off(Pour("Count them in: 30 pills", -1, "Pressed", 30f, "pills", new Vector3(Scale.x, Scale.y + 0.155f, Scale.z + 0.015f), white, true, 2f, "BottleFill", 0.75f), "Pressed"),
                S(WK.Place, "Snap the cap on", "Drag it onto the bottle.", 3),
                S(WK.Pack, "Box it", "Drag the bottle from the scale into the crate on the right.", 2),
            };
            return new WorkDef
            {
                title = "Pills", parts = parts, steps = steps, origin = Vector3.zero, hidden = new[] { "Pressed", "MortarContent" },
                apparatus = r =>
                {
                    Mortar(r, white); ScaleUnit(r); Lamp(r);
                    // single-punch hand press: base, two columns, die plate and the wheel
                    Box(r, px, y + 0.02f, pz, 0.3f, 0.04f, 0.22f, H(0x2f5a7a), 0.4f, 0.4f); foreach (float dx in new[] { -0.1f, 0.1f }) Cy(r, px + dx, y + 0.2f, pz, 0.026f, 0.34f, st, 1, 0.7f, 0.9f);
                    Box(r, px, y + 0.37f, pz, 0.28f, 0.04f, 0.1f, H(0x2f5a7a), 0.4f, 0.4f); Cy(r, px, y + 0.27f, pz, 0.04f, 0.18f, st, 1, 0.7f, 0.9f); Cy(r, px, y + 0.06f, pz, 0.12f, 0.04f, stD, 1, 0.6f, 0.9f);
                    var crank = Node(r, "Crank", px, y + 0.44f, pz - 0.06f); Cy(crank, 0f, 0f, 0f, 0.2f, 0.02f, H(0x1c1c1e), 2, 0.4f, 0.3f); for (int i = 0; i < 3; i++) Rot(Box(crank, 0f, 0f, -0.004f, 0.19f, 0.016f, 0.014f, st, 0.6f, 0.9f), 0f, 0f, i * 60f); Cy(crank, 0.08f, 0f, -0.035f, 0.028f, 0.05f, H(0xb23a2e), 2, 0.3f);
                    var pr = Node(r, "Pressed", px, y + 0.004f, pz - 0.2f); Box(pr, 0f, 0.004f, 0f, 0.2f, 0.008f, 0.1f, H(0x3a3d42), 0.5f, 0.6f); for (int i = 0; i < 15; i++) Cy(pr, -0.08f + (i % 5) * 0.04f, 0.014f, -0.03f + (i / 5) * 0.03f, 0.022f, 0.01f, white, 1, 0.3f);
                }
            };
        }

        static WorkDef Cook(bool crystal)
        {
            Color liq = crystal ? H(0x66c8f0, 0.8f) : H(0xe8e2c8, 0.85f), prod = crystal ? H(0x58c4f2) : H(0xf4f4f0);
            float y = TableY; float fx = Burn.x, fy = Burn.y + 0.47f, fz = Burn.z;
            var parts = new List<WPart>
            {
                P("Flask", t => Flask(t, liq)),
                P("Precursor", t => Jar(t, fx, fy, fz, H(0xd9d2c0), H(0x8a2f22)), true),
            };
            if (crystal) parts.Add(P("Cold tablets", t => { Box(t, fx, fy + 0.02f, fz, 0.11f, 0.008f, 0.07f, H(0xc8ccd2), 0.8f, 0.9f); for (int i = 0; i < 8; i++) Cy(t, fx - 0.036f + (i % 4) * 0.024f, fy + 0.027f, fz - 0.014f + (i / 4) * 0.028f, 0.018f, 0.008f, H(0xe06a5a), 1, 0.3f); }, true));
            int trayI = parts.Count;
            parts.Add(P("Glass tray", t =>
            {
                float x = TrayPos.x, z = TrayPos.z; Box(t, x, y + 0.006f, z, 0.34f, 0.012f, 0.22f, H(0xd6e6ea, 0.35f), 0.95f);
                Box(t, x, y + 0.02f, z - 0.108f, 0.34f, 0.03f, 0.006f, H(0xd6e6ea, 0.35f), 0.95f); Box(t, x, y + 0.02f, z + 0.108f, 0.34f, 0.03f, 0.006f, H(0xd6e6ea, 0.35f), 0.95f); Box(t, x - 0.168f, y + 0.02f, z, 0.006f, 0.03f, 0.22f, H(0xd6e6ea, 0.35f), 0.95f); Box(t, x + 0.168f, y + 0.02f, z, 0.006f, 0.03f, 0.22f, H(0xd6e6ea, 0.35f), 0.95f);
                Box(t, x, y + 0.017f, z, 0.32f, 0.01f, 0.2f, crystal ? H(0x66c8f0, 0.7f) : H(0xefeee6), 0.6f, 0f, "Slab");
                var sh = Node(t, "Shards", x, y + 0.014f, z);
                for (int i = 0; i < 8; i++) { var s = Node(sh, "Shard" + i, -0.12f + (i % 4) * 0.08f, 0f, -0.045f + (i / 4) * 0.09f); Rot(Box(s, 0f, 0.012f, 0f, 0.06f, 0.022f, 0.07f, crystal ? H(0x58c4f2, 0.8f) : H(0xf4f4f0), 0.9f), 0f, i * 37f, 0f); Rot(Box(s, 0.01f, 0.02f, 0.008f, 0.03f, 0.03f, 0.024f, crystal ? H(0x9fe0ff, 0.8f) : H(0xffffff), 0.9f), 20f, i * 53f, 15f); }
            }));
            int bagI = parts.Count; parts.Add(P("Zip bag", t => Bag(t, prod)));
            var steps = new List<WStep>
            {
                S(WK.Place, "Stand the flask on the burner", "Drag the flask from the tray onto its glowing outline.", 0),
                Pour("Tip in the precursor: 40 g", 1, null, 40f, "g", new Vector3(fx, Burn.y + 0.46f, fz), H(0xd9d2c0), true, 2f),
            };
            if (crystal) steps.Add(Pour("Add the cold tablets", 2, null, 8f, "tablets", new Vector3(fx, Burn.y + 0.46f, fz), H(0xe06a5a), true));
            steps.Add(Pour("Top up with solvent: 120 ml", -1, "Solvent", 120f, "ml", new Vector3(fx, Burn.y + 0.46f, fz), H(0xbfe6f2), false, 5f, "Liquid", crystal ? 0.7f : 0.6f));
            steps.Add(S(WK.Heat, "Cook it down", "Hold SPACE (or the mouse button) to open the gas. Keep the needle inside the green band.", -1, crystal ? 12f : 10f, "Flame"));
            steps.Add(S(WK.Place, "Set out the glass tray", "Drag the tray onto its glowing outline.", trayI));
            steps.Add(S(WK.Timing, crystal ? "Pour and let it set" : "Pour and let it dry", "Timing is everything: stop the marker in the green zone (SPACE).", -1, 1f, null, null, crystal ? "Shards" : "Slab", "Liquid"));
            if (crystal) steps.Add(S(WK.Clicks, "Break up the sheet", "Click each shard to crack it loose.", -1, 12f, "Shard"));
            else { var g = S(WK.Grind, "Crush it to powder", "Grab the pestle and stir in steady circles. Too fast and powder flies out.", -1, 3f, "Mortar", "Pestle"); g.pre = "MortarContent,-Slab"; steps.Add(g); }
            steps.Add(S(WK.Place, "Put a zip bag on the scale", "Drag it onto the scale.", bagI));
            { var w = Pour(crystal ? "Weigh out the bag: 10 g" : "Weigh out the bag: 10 g", -1, crystal ? "Shards" : "Mortar", 10f, "g", BagMouth, prod, true, 0.5f, "BagFill", 0.8f); w.off = crystal ? "Shards" : "MortarContent"; if (!crystal) { w.with = "MortarContent"; w.pre = "-Pestle"; } steps.Add(w); }
            steps.Add(S(WK.Pack, "Box it", "Drag the bag from the scale into the crate on the right.", bagI));
            return new WorkDef
            {
                title = crystal ? "Blue crystal" : "White powder", parts = parts.ToArray(), steps = steps.ToArray(), origin = Vector3.zero,
                hidden = crystal ? new[] { "Slab", "Shards" } : new[] { "Slab", "Shards", "MortarContent" },
                apparatus = r =>
                {
                    Burner(r); ScaleUnit(r); Lamp(r); if (!crystal) Mortar(r, prod);
                    var sv = Node(r, "Solvent", 0.42f, y, -0.08f); La(sv, 0f, 0f, 0f, new[] { 0f, 0f, 0.04f, 0f, 0.042f, 0.01f, 0.042f, 0.11f, 0.016f, 0.135f, 0.016f, 0.165f }, H(0x7a4a1c, 0.6f), 0.9f); Cy(sv, 0f, 0.172f, 0f, 0.04f, 0.016f, H(0x1c1c1e)); Box(sv, 0f, 0.06f, -0.043f, 0.055f, 0.05f, 0.002f, white); Box(sv, 0f, 0.07f, -0.045f, 0.04f, 0.008f, 0.002f, H(0xb21f1f));
                    // glassware and a retort stand in the back for the look of the thing
                    Cy(r, -0.32f, y + 0.008f, 0.3f, 0.16f, 0.016f, H(0x25272b), 1, 0.4f, 0.5f); Cy(r, -0.32f, y + 0.3f, 0.3f, 0.014f, 0.6f, st, 1, 0.7f, 0.9f); Box(r, -0.26f, y + 0.42f, 0.3f, 0.12f, 0.014f, 0.014f, st, 0.7f, 0.9f);
                    La(r, -0.2f, y + 0.25f, 0.3f, new[] { 0f, 0f, 0.012f, 0f, 0.012f, 0.22f, 0.02f, 0.23f }, H(0xcfe8ee, 0.3f), 0.95f);
                    La(r, 0.42f, y, 0.3f, new[] { 0f, 0f, 0.055f, 0f, 0.055f, 0.13f, 0.06f, 0.135f }, H(0xcfe8ee, 0.4f), 0.95f); Cy(r, 0.42f, y + 0.035f, 0.3f, 0.1f, 0.06f, H(0x9ad06a, 0.7f), 1, 0.8f);
                    La(r, 0.58f, y, 0.26f, new[] { 0f, 0f, 0.04f, 0f, 0.04f, 0.1f, 0.044f, 0.104f }, H(0xcfe8ee, 0.4f), 0.95f); Cy(r, 0.58f, y + 0.025f, 0.26f, 0.07f, 0.04f, H(0xe0a04a, 0.7f), 1, 0.8f);
                }
            };
        }

        /// <summary>Dressing for the stations standing in the player's home (same models as the workshop, just smaller).</summary>
        public static void StationDecor(int kind, Transform top)
        {
            var r = Node(top, "Decor", 0f, 0.94f, 0f); r.localScale = Vector3.one * 0.72f; var o = Node(r, "Offset", 0f, -TableY, 0f);
            if (kind == 0) { o.localPosition = new Vector3(-0.05f, -TableY, -0.05f); Still(o); }
            else if (kind == 1) { o.localPosition = new Vector3(-0.1f, -TableY, -0.1f); Burner(o); var f = Node(o, "F"); Flask(f, H(0x66c8f0, 0.8f)); Kit.Find(f, "Liquid").localScale = new Vector3(1f, 0.7f, 1f); Mortar(o, white); ScaleUnit(o); }
            else
            {
                var g = Node(o, "Gun", 0f, TableY + 0.2f, 0f); g.localRotation = Quaternion.Euler(90f, 0f, 0f); g.localPosition = new Vector3(-0.1f, TableY + 0.05f, 0f); foreach (var p in Pistol()) p.build(g);
                Box(o, 0.5f, TableY + 0.07f, 0.1f, 0.2f, 0.14f, 0.16f, stD, 0.4f, 0.6f); Cy(o, 0.5f, TableY + 0.24f, 0.05f, 0.03f, 0.2f, st, 1, 0.7f, 0.9f); Cy(o, 0.5f, TableY + 0.3f, 0.0f, 0.2f, 0.022f, H(0xb23a2e), 2, 0.4f, 0.3f);
                Box(o, -0.55f, TableY + 0.03f, 0.12f, 0.22f, 0.06f, 0.14f, H(0x3f5a3a), 0.2f); for (int i = 0; i < 6; i++) Cy(o, -0.62f + (i % 3) * 0.07f, TableY + 0.075f, 0.09f + (i / 3) * 0.06f, 0.022f, 0.05f, brass, 1, 0.7f, 0.9f);
            }
        }
    }
}
