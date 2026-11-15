using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Factions;
using MafiaSandbox.Player;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// Opening cinematic (in-engine, the living city): a camera tour past the two families' HQs, the police station and the player's
    /// derelict home, with letterbox bars, fades and story captions. Space / Enter / Esc / click skips. Hands over to the character creator.
    /// </summary>
    public class IntroCinematic : MonoBehaviour
    {
        public static IntroCinematic I { get; private set; }
        public static bool Playing => (I && I.playing) || Prologue.Cinematic;
        public bool enabledAtStart = true;
        bool playing; float t; int shot; float shotT;
        PlayerController pc; PlayerCamera cam; Camera camera;

        struct Shot { public Vector3 a, b, look; public float dur; public string text; }
        Shot[] shots;

        void Awake() { I = this; }
        void Start() { if (enabledAtStart) Invoke(nameof(Begin), 0.05f); }

        void Begin()
        {
            var gm = GameManager.I; if (!gm || !gm.player || !gm.playerCamera) return;
            pc = gm.player; cam = gm.playerCamera; camera = cam.GetComponent<Camera>();
            Vector3 home = gm.homePosition != Vector3.zero ? gm.homePosition : pc.transform.position;
            var mor = FactionDefs.Get("moretti"); var kap = FactionDefs.Get("kaplan");
            Vector3 mh = mor != null ? mor.HqUnity : home + Vector3.left * 60f, kh = kap != null ? kap.HqUnity : home + Vector3.right * 60f;
            Vector3 ps = gm.policeStationPos != Vector3.zero ? gm.policeStationPos : home + Vector3.forward * 50f;
            shots = new[]
            {
                new Shot { a = home + new Vector3(40, 55, 40), b = home + new Vector3(-25, 30, 15), look = home + Vector3.up * 2f, dur = 7f, text = "<b>PORT CITY</b>\nThree years ago, the families went to war." },
                new Shot { a = mh + new Vector3(34, 36, 34), b = mh + new Vector3(-8, 20, 26), look = mh + Vector3.up * 3f, dur = 5.5f, text = "The Morettis hold the west side." },
                new Shot { a = kh + new Vector3(-34, 36, 34), b = kh + new Vector3(8, 20, 26), look = kh + Vector3.up * 3f, dur = 5.5f, text = "The Kaplans hold the east." },
                new Shot { a = ps + new Vector3(-20, 14, 20), b = ps + new Vector3(10, 8, 12), look = ps + Vector3.up * 3f, dur = 5.5f, text = "And the police take their cut from both." },
                new Shot { a = home + new Vector3(9, 3.5f, 9), b = home + new Vector3(4, 2.2f, 5), look = home + Vector3.up * 1.3f, dur = 8f, text = "Your family was caught in the middle.\nYou went under, and nobody came home." },
            };
            if (MafiaSandbox.World.Hospital.I)
            {
                var hp = MafiaSandbox.World.Hospital.I.transform.position; var hs = new System.Collections.Generic.List<Shot>(shots);
                hs.Add(new Shot { a = hp + new Vector3(-22, 14, 26), b = hp + new Vector3(-10, 8, 20), look = hp + Vector3.up * 3f, dur = 6f, text = "Three years later, you open your eyes." });
                shots = hs.ToArray();
            }
            playing = true; shot = 0; shotT = 0f; t = 0f;
            cam.enabled = false; pc.InputLocked = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        void Update()
        {
            if (!playing) return;
            float dt = Time.unscaledDeltaTime; t += dt; shotT += dt;
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(0)) { End(); return; }
            var s = shots[shot];
            if (shotT >= s.dur) { shot++; shotT = 0f; if (shot >= shots.Length) { End(); return; } s = shots[shot]; }
            float k = Mathf.SmoothStep(0f, 1f, shotT / s.dur);
            Vector3 p = Vector3.Lerp(s.a, s.b, k); camera.transform.position = p;
            camera.transform.rotation = Quaternion.LookRotation((s.look - p).normalized, Vector3.up);
            camera.fieldOfView = 55f;
        }

        void End()
        {
            playing = false;
            if (cam) { cam.enabled = true; cam.SetYaw(pc.transform.eulerAngles.y); }
            if (camera) camera.fieldOfView = 60f;
            if (CharacterCreator.I) CharacterCreator.I.Begin(false); else if (pc) pc.InputLocked = false;
        }

        void OnGUI()
        {
            if (!playing) return;
            float W = Screen.width, H = Screen.height; var s = shots[shot];
            float bar = H * 0.12f;
            GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, W, bar), UiSkin.White); GUI.DrawTexture(new Rect(0, H - bar, W, bar), UiSkin.White);
            // fades: in at the very start, a dip at each cut
            float a = 0f; if (shot == 0) a = Mathf.Clamp01(1f - shotT / 1.2f); else a = Mathf.Clamp01(1f - shotT / 0.5f);
            float tail = s.dur - shotT; if (shot == shots.Length - 1) a = Mathf.Max(a, Mathf.Clamp01(1f - tail / 0.8f));
            if (a > 0f) { GUI.color = new Color(0, 0, 0, a); GUI.DrawTexture(new Rect(0, 0, W, H), UiSkin.White); }
            // caption
            float ca = Mathf.Clamp01(shotT / 0.8f) * Mathf.Clamp01(tail / 0.6f);
            var st = new GUIStyle(UiSkin.Big) { fontSize = Mathf.RoundToInt(H * 0.034f), alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true };
            st.normal.textColor = new Color(1f, 1f, 1f, ca);
            var rc = new Rect(W * 0.1f, H - bar - H * 0.17f, W * 0.8f, H * 0.15f);
            GUI.color = new Color(0, 0, 0, 0.55f * ca); GUI.Label(new Rect(rc.x + 2, rc.y + 2, rc.width, rc.height), s.text, st);
            GUI.color = Color.white; GUI.Label(rc, s.text, st);
            var hint = new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleRight }; GUI.Label(new Rect(W - 260, H - bar + 6, 240, 20), "Space / click to skip", hint);
        }
    }
}
