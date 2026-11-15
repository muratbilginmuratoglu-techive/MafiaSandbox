using UnityEngine;
using MafiaSandbox.Combat;
using MafiaSandbox.Core;
using MafiaSandbox.World;
using MafiaSandbox.Player;
using MafiaSandbox.UI;

namespace MafiaSandbox.Economy
{
    /// <summary>
    /// Furnishing your home with equipment you bought (still / chemistry table / gunsmith bench).
    /// B inside the house: place a kit from the backpack (ghost follows you, Q/E or wheel rotates, F confirms, B cancels).
    /// B next to a placed station with no kit in hand: pick it up again to move it.
    /// </summary>
    public class HomePlacer : MonoBehaviour
    {
        public static HomePlacer I { get; private set; }
        public static bool Active => I && I.placing;
        bool placing; string kitId; int kind; float yaw; Vector3 pos; bool valid; string why; GameObject ghost; Material gm; PlayerController pc;
        void Awake() { I = this; }

        bool InHome(Vector3 p, out BuildingInfo bi)
        {
            bi = null; var eco = EconomyManager.I; if (!eco || eco.homeShop == null || !eco.homeShop.info) return false; bi = eco.homeShop.info; var f = bi.footprint;
            return f.size.sqrMagnitude > 1f && p.x > f.min.x && p.x < f.max.x && p.z > f.min.z && p.z < f.max.z;
        }
        string FindKit() { var eco = EconomyManager.I; if (!eco) return null; foreach (var k in Career.KitIds) if (eco.Count(k) > 0) return k; return null; }

        void Update()
        {
            var g = GameManager.I; if (!g || !g.player) return; pc = g.player; var lab = HomeLab.I; if (!lab || !lab.Ready) return;
            if (GameUI.Blocking || Prologue.Active || IntroCinematic.Playing || CharacterCreator.IsOpen) { if (placing) Cancel(); return; }
            var p = pc.transform.position;
            if (placing)
            {
                if (!InHome(p, out var bi)) { Cancel(); g.Say("Placement cancelled: you left the house."); return; }
                if (Input.GetKeyDown(KeyCode.B)) { Cancel(); return; }
                if (Input.GetKeyDown(KeyCode.Q)) yaw -= 90f; if (Input.GetKeyDown(KeyCode.E)) yaw += 90f;
                float sw = Input.mouseScrollDelta.y; if (Mathf.Abs(sw) > 0.1f) yaw += Mathf.Sign(sw) * 90f;
                UpdateGhost(bi, p);
                if (Input.GetKeyDown(KeyCode.F) && valid) Confirm();
                else if (Input.GetKeyDown(KeyCode.F)) g.Say(why);
                return;
            }
            if (Input.GetKeyDown(KeyCode.B))
            {
                if (!InHome(p, out var bi)) { var k0 = FindKit(); if (k0 != null) g.Say("Go inside your house, then press B to place it."); return; }
                var kit = FindKit();
                if (kit != null) Begin(kit, p);
                else { int i = lab.NearStation(p, 2.6f); if (i >= 0) PickUp(i); else g.Say("Nothing to place. Buy equipment first (pawn shop, pharmacy, gun shop)."); }
            }
        }

        void Begin(string kit, Vector3 p)
        {
            kitId = kit; kind = Career.KitStation(kit); yaw = Mathf.Round(pc.transform.eulerAngles.y / 90f) * 90f; placing = true;
            ghost = GameObject.CreatePrimitive(PrimitiveType.Cube); ghost.name = "PlacementGhost"; Destroy(ghost.GetComponent<Collider>());
            gm = new Material(Shader.Find("Sprites/Default")); ghost.GetComponent<Renderer>().material = gm;
            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(arrow.GetComponent<Collider>()); arrow.transform.SetParent(ghost.transform, false); arrow.transform.localPosition = new Vector3(0f, 0f, 0.62f); arrow.transform.localScale = new Vector3(0.5f, 0.3f, 0.2f); arrow.GetComponent<Renderer>().sharedMaterial = gm;
            GameManager.I.Say($"Placing: {Items.Get(kit).name}. Walk to position it, Q/E rotate, F confirm, B cancel.");
        }
        void Cancel() { placing = false; if (ghost) Destroy(ghost); ghost = null; }

        void UpdateGhost(BuildingInfo bi, Vector3 p)
        {
            var f = bi.footprint; var sz = HomeLab.StationSize; var fwd = pc.transform.forward; fwd.y = 0f; fwd.Normalize();
            var t = p + fwd * 2.0f; t.x = Mathf.Round(t.x * 4f) / 4f; t.z = Mathf.Round(t.z * 4f) / 4f; t.y = p.y;
            bool rot90 = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.5f; float hx = (rot90 ? sz.z : sz.x) / 2f, hz = (rot90 ? sz.x : sz.z) / 2f;
            t.x = Mathf.Clamp(t.x, f.min.x + hx + 0.15f, f.max.x - hx - 0.15f); t.z = Mathf.Clamp(t.z, f.min.z + hz + 0.15f, f.max.z - hz - 0.15f);
            pos = t; valid = true; why = "";
            var lab = HomeLab.I; var rot = Quaternion.Euler(0f, yaw, 0f);
            if ((new Vector3(bi.door.x, 0, bi.door.z) - new Vector3(t.x, 0, t.z)).magnitude < 1.8f) { valid = false; why = "Too close to the door."; }
            if (valid && (new Vector3(lab.BedPos.x, 0, lab.BedPos.z) - new Vector3(t.x, 0, t.z)).magnitude < 1.9f) { valid = false; why = "Too close to the bed."; }
            if (valid && Stash.I && (new Vector3(Stash.I.Pos.x, 0, Stash.I.Pos.z) - new Vector3(t.x, 0, t.z)).magnitude < 1.5f) { valid = false; why = "Too close to the stash chest."; }
            if (valid) foreach (var st in lab.stations) if ((new Vector3(st.pos.x, 0, st.pos.z) - new Vector3(t.x, 0, t.z)).magnitude < 1.6f) { valid = false; why = "Too close to another station."; break; }
            if (valid)
            {
                var c = new Vector3(t.x, t.y + 0.25f + (sz.y - 0.25f) / 2f, t.z);
                foreach (var h in Physics.OverlapBox(c, new Vector3(sz.x / 2f - 0.04f, (sz.y - 0.25f) / 2f, sz.z / 2f - 0.04f), rot, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.transform.IsChildOf(pc.transform) || h is CharacterController || h.GetComponentInParent<CharacterBody>()) continue;
                    valid = false; why = "Something is in the way."; break;
                }
            }
            ghost.transform.SetPositionAndRotation(new Vector3(t.x, t.y + sz.y / 2f, t.z), rot); ghost.transform.localScale = sz;
            gm.color = valid ? new Color(0.3f, 1f, 0.4f, 0.45f) : new Color(1f, 0.25f, 0.2f, 0.45f);
        }

        void Confirm()
        {
            var eco = EconomyManager.I; var lab = HomeLab.I; var it = eco.inv.Find(x => x.id == kitId); if (it == null) { Cancel(); return; }
            it.qty--; if (it.qty <= 0) eco.inv.Remove(it);
            lab.AddStation(kind, pos, yaw); Cancel(); GameManager.I.Say($"{Career.StationNames[kind]} placed. Press F next to it to work.");
            Sfx.At("click", pos);
        }
        void PickUp(int i)
        {
            var lab = HomeLab.I; var st = lab.stations[i];
            for (int r = 0; r < HomeLab.Recipes.Length; r++) { foreach (var ri in HomeLab.RecipesFor(st.kind)) if (ri == r && lab.batches[r].state != 0) { GameManager.I.Say("Finish or collect the batch here before moving it."); return; } }
            EconomyManager.I.AddItem(Career.KitIds[st.kind], 1, false); lab.RemoveStation(i); GameManager.I.Say("Picked it up. Press B again to place it somewhere else.");
        }

        void OnGUI()
        {
            if (!placing) return;
            var st = new GUIStyle(UiSkin.Title) { alignment = TextAnchor.MiddleCenter, fontSize = 22 }; st.normal.textColor = valid ? new Color(0.5f, 1f, 0.55f) : new Color(1f, 0.45f, 0.4f);
            float W = Screen.width, H = Screen.height; GUI.Label(new Rect(0, H - 150, W, 34), valid ? $"PLACE {Items.Get(kitId).name.ToUpper()}" : why, st);
            var sm = new GUIStyle(UiSkin.LabelDim) { alignment = TextAnchor.MiddleCenter }; GUI.Label(new Rect(0, H - 118, W, 22), "Q / E or wheel: rotate    F: confirm    B: cancel", sm);
        }
    }
}
