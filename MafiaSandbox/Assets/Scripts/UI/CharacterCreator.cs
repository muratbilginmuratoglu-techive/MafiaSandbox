using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Player;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// Character-creation screen shown when the game starts (and again from the phone's Settings app): name, body (weight / height),
    /// face (skin, hair, facial hair, glasses, hats) and clothes (top, bottom, colours). The world freezes behind it, the camera
    /// frames the character; drag with the mouse to turn it. "Start" saves the look and begins the game.
    /// </summary>
    public class CharacterCreator : MonoBehaviour
    {
        public static CharacterCreator I { get; private set; }
        public static bool IsOpen => I && I.Open;
        public bool Open { get; private set; }
        public bool showAtStart = true;

        Look look; PlayerAppearance app; PlayerController pc; PlayerCamera cam; Look backup; bool midGame;
        int tab; float spin, zoomT = 0f, zoomGoal = 0f; Vector2 scroll;
        float oldTimeScale = 1f; bool camWasEnabled = true; float oldFov; Camera camera;
        GUIStyle nameField;
        static readonly string[] Tabs = { "Body", "Face", "Clothes" };

        void Awake() { I = this; }

        void Start() { if (showAtStart && !FindAnyObjectByType<IntroCinematic>()) StartCoroutineNextFrame(); }
        void StartCoroutineNextFrame() { Invoke(nameof(OpenAtStart), 0.05f); }
        void OpenAtStart() { Begin(false); }

        public void Begin(bool mid)
        {
            var gm = GameManager.I; if (!gm || !gm.player) return;
            pc = gm.player; cam = gm.playerCamera; app = pc.GetComponent<PlayerAppearance>();
            if (!app) return;
            midGame = mid; look = mid && app.Current != null ? Clone(app.Current) : Look.Load();
            backup = Clone(look); tab = 0; spin = 0f; zoomT = zoomGoal = 0f;
            app.Apply(look);
            oldTimeScale = Time.timeScale; Time.timeScale = 0f;
            camera = cam ? cam.GetComponent<Camera>() : Camera.main;
            if (cam) { camWasEnabled = cam.enabled; cam.enabled = false; }
            if (camera) oldFov = camera.fieldOfView;
            Open = true; pc.InputLocked = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            var vi = pc.GetComponent<MafiaSandbox.Vehicles.VehicleInteraction>(); if (vi && vi.Current) { Open = false; Time.timeScale = oldTimeScale; if (cam) cam.enabled = camWasEnabled; }
        }

        static Look Clone(Look l) => JsonUtility.FromJson<Look>(JsonUtility.ToJson(l));

        void Finish(bool save)
        {
            if (!save && midGame) { look = backup; app.Apply(look); }
            if (save) { if (string.IsNullOrWhiteSpace(look.name)) look.name = "Vito"; look.name = look.name.Trim(); look.Save(); app.Apply(look); }
            Open = false; Time.timeScale = oldTimeScale <= 0f ? 1f : oldTimeScale;
            var m = pc.transform.Find("Model"); if (m) m.localRotation = Quaternion.identity;
            if (cam) { cam.enabled = camWasEnabled; cam.SetYaw(pc.transform.eulerAngles.y); }
            if (camera) camera.fieldOfView = oldFov;
            pc.InputLocked = false;   // GameUI.ApplyLock re-locks the cursor when Blocking ends
            if (save) GameManager.I?.Say(midGame ? $"Looking good, {look.name}." : $"Welcome to town, {look.name}. Press H for controls, T for your phone.");
        }

        void Update()
        {
            if (!Open || !pc || !camera) return;
            float dt = Time.unscaledDeltaTime;
            zoomT = Mathf.MoveTowards(zoomT, zoomGoal, dt * 2.5f); float k = Mathf.SmoothStep(0f, 1f, zoomT);
            // frame the character: full body or face close-up, shifted so the character sits right of the panel
            var feet = pc.transform.position; Vector3 fwd = pc.transform.forward;
            float h = look != null ? 1f + 0.08f * look.height : 1f;
            Vector3 aim = feet + Vector3.up * Mathf.Lerp(0.95f * h, 1.68f * h, k);
            float dist = Mathf.Lerp(3.6f, 1.6f, k);
            camera.transform.position = aim + fwd * dist + Vector3.up * Mathf.Lerp(0.25f, 0.05f, k);
            camera.transform.rotation = Quaternion.LookRotation(aim - camera.transform.position, Vector3.up);
            camera.fieldOfView = Mathf.Lerp(34f, 28f, k);
            camera.transform.position -= camera.transform.right * Mathf.Lerp(0.95f, 0.30f, k);
            var m = pc.transform.Find("Model"); if (m) m.localRotation = Quaternion.Euler(0f, spin, 0f);
        }

        void OnGUI()
        {
            if (!Open || look == null) return;
            var ev = Event.current;
            float W = Screen.width, H = Screen.height;
            float pw = Mathf.Min(420f, W * 0.42f);
            var r = new Rect(24, 24, pw, H - 48);
            // drag to turn / wheel to zoom anywhere right of the panel
            if (ev.type == EventType.MouseDrag && ev.mousePosition.x > r.xMax) { spin -= ev.delta.x * 0.6f; ev.Use(); }
            if (ev.type == EventType.ScrollWheel && ev.mousePosition.x > r.xMax) { zoomGoal = Mathf.Clamp01(zoomGoal + (ev.delta.y < 0 ? 0.35f : -0.35f)); ev.Use(); }
            UiSkin.DrawPanel(r, true);
            GUILayout.BeginArea(new Rect(r.x + 16, r.y + 12, r.width - 32, r.height - 24));
            GUILayout.Label(midGame ? "Edit character" : "Create your character", UiSkin.Title);
            GUILayout.Space(6);
            GUILayout.Label("NAME", UiSkin.H2);
            if (nameField == null) { nameField = new GUIStyle(GUI.skin.textField) { fontSize = 18, padding = new RectOffset(10, 10, 6, 6) }; nameField.normal.textColor = Color.white; nameField.focused.textColor = Color.white; }
            GUI.SetNextControlName("namefield");
            look.name = GUILayout.TextField(look.name ?? "", 16, nameField, GUILayout.Height(34));
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Tabs.Length; i++) { if (GUILayout.Button(Tabs[i], tab == i ? UiSkin.ButtonOn : UiSkin.Button, GUILayout.Height(30))) { tab = i; zoomGoal = i == 1 ? 1f : 0f; } }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(r.height - 255));
            bool changed = false; var before = JsonUtility.ToJson(look);
            if (tab == 0) TabBody(); else if (tab == 1) TabFace(); else TabClothes();
            GUILayout.EndScrollView();
            if (JsonUtility.ToJson(look) != before) changed = true;
            if (changed) app.Apply(look);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", UiSkin.Button, GUILayout.Width(40), GUILayout.Height(28))) spin += 40f;
            if (GUILayout.Button(">", UiSkin.Button, GUILayout.Width(40), GUILayout.Height(28))) spin -= 40f;
            if (GUILayout.Button("Randomize", UiSkin.Button, GUILayout.Height(28))) { var n = look.name; look = Look.Random(); look.name = n; app.Apply(look); }
            if (GUILayout.Button("Reset", UiSkin.Button, GUILayout.Height(28))) { var n = look.name; look = new Look { name = n }; app.Apply(look); }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (midGame && GUILayout.Button("Cancel", UiSkin.Button, GUILayout.Height(42))) Finish(false);
            var big = new GUIStyle(UiSkin.ButtonOn) { fontSize = 17, fontStyle = FontStyle.Bold };
            bool go = GUILayout.Button(midGame ? "Save" : "START GAME", big, GUILayout.Height(42));
            GUILayout.EndHorizontal();
            GUILayout.Label("Drag with the mouse to turn · wheel zooms", UiSkin.LabelDim);
            GUILayout.EndArea();
            if (go) Finish(true);
        }

        void TabBody()
        {
            GUILayout.Label("WEIGHT", UiSkin.H2);
            look.weight = Slider(look.weight, -1f, 1f, WeightLabel(look.weight));
            GUILayout.BeginHorizontal();
            Preset("Skeletal", -1f); Preset("Thin", -0.5f); Preset("Average", 0f); Preset("Heavy", 0.6f); Preset("Obese", 1f);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label("HEIGHT", UiSkin.H2);
            look.height = Slider(look.height, -1f, 1f, look.height < -0.6f ? "Short" : look.height > 0.6f ? "Tall" : "Medium");
            GUILayout.Space(10);
            GUILayout.Label("SKIN TONE", UiSkin.H2);
            look.skin = Swatches(look.skin, CharacterPalettes.Skin, 6);
        }

        void Preset(string n, float v) { if (GUILayout.Button(n, Mathf.Abs(look.weight - v) < 0.05f ? UiSkin.ButtonOn : UiSkin.Button, GUILayout.Height(26))) look.weight = v; }
        static string WeightLabel(float w) => w < -0.7f ? "Skeletal" : w < -0.25f ? "Thin" : w < 0.25f ? "Average" : w < 0.7f ? "Heavy" : "Obese";

        void TabFace()
        {
            GUILayout.Label("HAIR STYLE", UiSkin.H2);
            Grid(Look.HairNames, ref look.hair, 3, true);
            GUILayout.Label("HAIR COLOUR", UiSkin.H2);
            look.hairCol = Swatches(look.hairCol, CharacterPalettes.Hair, 10);
            GUILayout.Space(8);
            GUILayout.Label("FACE", UiSkin.H2);
            GUILayout.BeginHorizontal();
            look.beard = Toggle("Beard", look.beard); look.mustache = Toggle("Moustache", look.mustache); look.glasses = Toggle("Glasses", look.glasses);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("HAT", UiSkin.H2);
            Grid(Look.HatNames, ref look.hat, 3, true);
            if (look.hat >= 0) { GUILayout.Label("HAT COLOUR", UiSkin.H2); look.hatCol = Swatches(look.hatCol, CharacterPalettes.Clothes, 8); }
            GUILayout.Space(8);
            GUILayout.Label("SKIN TONE", UiSkin.H2);
            look.skin = Swatches(look.skin, CharacterPalettes.Skin, 6);
        }

        void TabClothes()
        {
            GUILayout.Label("TOP", UiSkin.H2);
            Grid(Look.TopNames, ref look.top, 3, false);
            look.topCol = Swatches(look.topCol, CharacterPalettes.Clothes, 8);
            GUILayout.Space(8);
            GUILayout.Label("BOTTOM", UiSkin.H2);
            Grid(Look.BottomNames, ref look.bottom, 3, false);
            look.pantsCol = Swatches(look.pantsCol, CharacterPalettes.Clothes, 8);
            GUILayout.Space(8);
            GUILayout.Label("SHOES", UiSkin.H2);
            look.shoesCol = Swatches(look.shoesCol, CharacterPalettes.Clothes, 8);
        }

        static float Slider(float v, float a, float b, string label)
        {
            GUILayout.BeginHorizontal(); v = GUILayout.HorizontalSlider(v, a, b, GUILayout.Height(22)); GUILayout.Label(label, UiSkin.LabelBold, GUILayout.Width(80)); GUILayout.EndHorizontal(); return v;
        }

        static bool Toggle(string n, bool v) => GUILayout.Button(n, v ? UiSkin.ButtonOn : UiSkin.Button, GUILayout.Height(28)) ? !v : v;

        static void Grid(string[] names, ref int sel, int cols, bool allowNone)
        {
            int total = names.Length + (allowNone ? 1 : 0);
            for (int i = 0; i < total; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + cols, total); j++)
                {
                    int idx = allowNone ? j - 1 : j; string n = idx < 0 ? "None" : names[idx];
                    if (GUILayout.Button(n, sel == idx ? UiSkin.ButtonOn : UiSkin.Button, GUILayout.Height(28))) sel = idx;
                }
                GUILayout.EndHorizontal();
            }
        }

        static Color Swatches(Color cur, Color[] pal, int perRow)
        {
            for (int i = 0; i < pal.Length; i += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + perRow, pal.Length); j++)
                {
                    var rc = GUILayoutUtility.GetRect(30, 26, GUILayout.Width(30), GUILayout.Height(26));
                    bool sel = Near(cur, pal[j]);
                    GUI.color = sel ? UiSkin.Accent : new Color(1f, 1f, 1f, 0.25f); GUI.DrawTexture(rc, UiSkin.White);
                    GUI.color = pal[j]; GUI.DrawTexture(new Rect(rc.x + 2, rc.y + 2, rc.width - 4, rc.height - 4), UiSkin.White); GUI.color = Color.white;
                    if (GUI.Button(rc, GUIContent.none, GUIStyle.none)) cur = pal[j];
                    GUILayout.Space(3);
                }
                GUILayout.EndHorizontal(); GUILayout.Space(3);
            }
            return cur;
        }
        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f;
    }
}
