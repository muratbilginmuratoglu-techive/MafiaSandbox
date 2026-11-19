using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;
using MafiaSandbox.Gang;

namespace MafiaSandbox.UI
{
    /// <summary>
    /// Admin / test mode (F10): unlimited ingredients, instant batches, every recipe unlocked, and a button per product that opens
    /// its workshop job right where you stand. Also stocks the cupboard, hands out cash and sets up the three stations at home.
    /// </summary>
    public class AdminPanel : MonoBehaviour
    {
        public static AdminPanel I { get; private set; }
        public static bool IsOpen => I && I.open;
        bool open; Vector2 scroll;
        void Awake() { I = this; }

        void Update()
        {
            var gm = GameManager.I; if (!gm || !gm.player) { open = false; return; }
            if (Input.GetKeyDown(KeyCode.F10) && !Workshop.Active && !CharacterCreator.IsOpen && !IntroCinematic.Playing && !HomeLab.Sleeping) open = !open;
            if (open && Input.GetKeyDown(KeyCode.Escape)) open = false;
        }

        static void Stock(string id, int n)
        {
            var st = Stash.I; if (!st) { EconomyManager.I?.AddItem(id, n, false); return; }
            var it = st.items.Find(x => x.id == id && !x.stolen); if (it != null) it.qty += n; else st.items.Add(new InvItem { id = id, qty = n, stolen = false }); st.SavePublic();
        }

        void OnGUI()
        {
            var gm = GameManager.I; if (!gm || !gm.player) return;
            if (Admin.On && !open && !Workshop.Active) { var chip = new Rect(Screen.width / 2f - 150, 36, 300, 22); UiSkin.DrawChip(chip, "ADMIN MODE  ·  F10 opens the panel", new Color(1f, 0.6f, 0.35f)); }
            if (!open) return; GUI.depth = -40; var lab = HomeLab.I; var eco = EconomyManager.I;
            float W = Screen.width, H = Screen.height; var r = new Rect(W / 2f - 380, 40, 760, Mathf.Min(H - 70, 640)); UiSkin.DrawPanel(r, true);
            GUILayout.BeginArea(new Rect(r.x + 16, r.y + 12, r.width - 32, r.height - 24));
            GUILayout.BeginHorizontal(); GUILayout.Label("Admin mode", UiSkin.Title); GUILayout.FlexibleSpace(); GUILayout.Label("F10 / Esc closes", UiSkin.LabelDim); GUILayout.EndHorizontal();
            GUILayout.Label("SWITCHES", UiSkin.H2); GUILayout.BeginHorizontal();
            bool on = GUILayout.Toggle(Admin.On, Admin.On ? "Admin: ON" : "Admin: OFF", UiSkin.Button, GUILayout.Height(34));
            if (on != Admin.On) { Admin.On = Admin.Unlimited = Admin.Instant = Admin.UnlockAll = on; gm.Say(on ? "Admin mode on: unlimited ingredients, instant batches, all recipes." : "Admin mode off."); }
            GUI.enabled = Admin.On;
            Admin.Unlimited = GUILayout.Toggle(Admin.Unlimited, "Unlimited ingredients", UiSkin.Button, GUILayout.Height(34)) && Admin.On;
            Admin.Instant = GUILayout.Toggle(Admin.Instant, "Instant batches", UiSkin.Button, GUILayout.Height(34)) && Admin.On;
            Admin.UnlockAll = GUILayout.Toggle(Admin.UnlockAll, "All recipes unlocked", UiSkin.Button, GUILayout.Height(34)) && Admin.On;
            GUI.enabled = true; GUILayout.EndHorizontal();
            GUILayout.Label("In the workshop, admin mode adds: N = skip the current step, and a Finish now button.", UiSkin.LabelDim);

            GUILayout.Space(6); GUILayout.Label("HOME AND SUPPLIES", UiSkin.H2); GUILayout.BeginHorizontal();
            if (GUILayout.Button("+$5,000", UiSkin.Button) && eco) eco.cash += 5000;
            if (GUILayout.Button("Cupboard: 20 of every ingredient", UiSkin.Button)) { foreach (var kv in Items.All) if (kv.Value.kind == "material") Stock(kv.Key, 20); gm.Say("The cupboard at home is stocked."); }
            if (GUILayout.Button("Backpack: 5 of every product", UiSkin.Button) && eco) { foreach (var p in CrewManager.Products) eco.AddItem(p, 5, false); gm.Say("Five of every product in your backpack."); }
            GUILayout.EndHorizontal(); GUILayout.BeginHorizontal();
            if (GUILayout.Button("Set up the three stations at home", UiSkin.Button) && lab && lab.Ready && eco && eco.homeShop != null)
            {
                var bi = eco.homeShop.info; int added = 0; Vector3 mid = Stash.I ? Stash.I.Pos : lab.BedPos + Vector3.right * 1.9f, right = mid - lab.BedPos; right.y = 0f; right.Normalize(); Vector3 toDoor = Vector3.Cross(right, Vector3.up);
                for (int k = 0; k < 3; k++) if (lab.CountStations(k) == 0) { var p = Stash.InHome(bi, mid - toDoor * 2.1f + right * ((k - 1) * 1.8f)); p.y = lab.BedPos.y; lab.AddStation(k, p, Mathf.Atan2(toDoor.x, toDoor.z) * Mathf.Rad2Deg); added++; }
                gm.Say(added > 0 ? $"{added} station(s) placed at home." : "All three stations are already at home.");
            }
            if (GUILayout.Button("Teleport home", UiSkin.Button)) { gm.player.Teleport(gm.homePosition, gm.homeYaw); if (gm.playerCamera) gm.playerCamera.SetYaw(gm.homeYaw); open = false; }
            GUILayout.EndHorizontal();

            GUILayout.Space(6); GUILayout.Label("OPEN A WORKSHOP JOB HERE (the product goes straight to your backpack)", UiSkin.H2);
            scroll = GUILayout.BeginScrollView(scroll);
            if (lab) for (int st = 0; st < 3; st++)
            {
                GUILayout.Label(Career.StationNames[st].ToUpperInvariant(), UiSkin.H2);
                foreach (int i in HomeLab.RecipesFor(st))
                {
                    var rc = HomeLab.Recipes[i]; GUILayout.BeginHorizontal();
                    string need = ""; foreach (var (id, n) in rc.inputs) need += (need.Length > 0 ? ", " : "") + n + " " + Items.Get(id).name;
                    GUILayout.Label($"{rc.name}  →  {rc.yield} × {Items.Get(rc.product).name}   ({need})", UiSkin.Label, GUILayout.Width(540));
                    if (GUILayout.Button("Open", UiSkin.ButtonOn, GUILayout.Width(150)))
                    {
                        if (!Admin.On) gm.Say("Turn admin mode on first.");
                        else { if (lab.batches[i].state != 0) lab.CollectNow(i); if (Workshop.I && Workshop.I.Begin(i, 1, true)) open = false; }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
