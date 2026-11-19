using UnityEngine;
using MafiaSandbox.Core;
using MafiaSandbox.Economy;

namespace MafiaSandbox.Vehicles
{
    /// <summary>
    /// Light rides: skateboard, kick scooter and bicycle (each 15% faster than the one before). Bought at the market, carried in the
    /// backpack and set down with Use; E gets on and off like a car, but the rider stays visible. One bicycle waits outside the home.
    /// </summary>
    public class Rides : MonoBehaviour
    {
        public static Rides I { get; private set; }
        public const float BoardSpeed = 7.4f;   // running is 6 m/s
        public static float SpeedOf(string id) => id == "Bisiklet" ? BoardSpeed * 1.15f * 1.15f : id == "Skuter" ? BoardSpeed * 1.15f : BoardSpeed;
        GameObject homeBike;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (I) return; var go = new GameObject("Rides"); DontDestroyOnLoad(go); go.AddComponent<Rides>(); }
        void Awake() { I = this; }

        void Update()
        {
            if (homeBike) return; var gm = GameManager.I; var eco = EconomyManager.I; if (!gm || !gm.player || !eco || eco.homeShop == null) return;
            // parked beside the spot where you step out of the front door
            var rot = Quaternion.Euler(0f, gm.homeYaw, 0f); Vector3 p = gm.homePosition + rot * new Vector3(2.6f, 0f, 0.6f);
            var v = Spawn("Bisiklet", p, gm.homeYaw + 20f); if (v) homeBike = v.gameObject;
        }

        /// <summary>Use from the backpack: set the ride down in front of the player.</summary>
        public bool Deploy(string id)
        {
            var gm = GameManager.I; if (!gm || !gm.player) return false; var t = gm.player.transform;
            var v = Spawn(id, t.position + t.forward * 1.3f, t.eulerAngles.y); if (!v) return false;
            gm.Say($"{v.displayName} set down. Press E to ride it."); return true;
        }

        public VehicleController Spawn(string id, Vector3 pos, float yaw)
        {
            if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var h, 12f, 1 << 0, QueryTriggerInteraction.Ignore)) pos.y = h.point.y;
            var go = new GameObject("Ride_" + id); go.SetActive(false); go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var body = new GameObject("Model").transform; body.SetParent(go.transform, false); var model = new GameObject("Lean").transform; model.SetParent(body, false);
            float len, wid; string name, pose; Vector3 seat; float seatYaw = 0f;
            if (id == "Bisiklet") { Bicycle(model); len = 1.75f; wid = 0.55f; name = "Bicycle"; pose = "Crouch_Idle"; seat = new Vector3(0f, 0.42f, -0.28f); }
            else if (id == "Skuter") { Scooter(model); len = 1.0f; wid = 0.5f; name = "Kick scooter"; pose = "Idle"; seat = new Vector3(0f, 0.15f, -0.1f); }
            else { Board(model); len = 1.0f; wid = 0.5f; name = "Skateboard"; pose = "Idle"; seat = new Vector3(0f, 0.13f, 0f); seatYaw = 65f; }
            go.AddComponent<Rigidbody>(); var box = go.AddComponent<BoxCollider>(); box.center = new Vector3(0f, 0.5f, 0f); box.size = new Vector3(wid, 1.2f, len);
            var vc = go.AddComponent<VehicleController>(); vc.displayName = name; vc.length = len; vc.width = wid; vc.maxSpeed = SpeedOf(id); vc.maxReverse = 1.5f; vc.acceleration = id == "Bisiklet" ? 5f : 4f; vc.brakeDecel = 11f; vc.coastDecel = id == "Bisiklet" ? 1.2f : 1.8f;
            vc.maxSteerDeg = 34f; vc.wheelBase = id == "Bisiklet" ? 1.05f : 0.8f; vc.light = true; vc.openRide = true; vc.seat = seat; vc.seatYaw = seatYaw; vc.ridePose = pose;
            if (id == "Bisiklet") { vc.hasGrips = true; vc.gripL = new Vector3(-0.22f, 1.0f, 0.34f); vc.gripR = new Vector3(0.22f, 1.0f, 0.34f); }
            else if (id == "Skuter") { vc.hasGrips = true; vc.gripL = new Vector3(-0.19f, 1.02f, 0.3f); vc.gripR = new Vector3(0.19f, 1.02f, 0.3f); }
            var vis = go.AddComponent<RideVisual>(); vis.lean = model; vis.maxLean = id == "Bisiklet" ? 22f : id == "Skuter" ? 12f : 16f;
            int vl = LayerMask.NameToLayer("Vehicles"); if (vl >= 0) go.layer = vl;
            go.SetActive(true); return vc;
        }

        static readonly Color tyre = Kit.H(0x141416), steel = Kit.H(0xb9bec6), red = Kit.H(0xc8382c), dark = Kit.H(0x26282c);
        static Transform Tube(Transform p, Vector3 a, Vector3 b, float dia, Color c)
        {
            var t = Kit.Mk(p, "Tube", Kit.Cylinder, (a + b) * 0.5f, new Vector3(dia, Vector3.Distance(a, b) * 0.5f, dia), Quaternion.FromToRotation(Vector3.up, (b - a).normalized), Kit.Mat(c, 0.5f, 0.5f)); return t;
        }
        static void Wheel(Transform parent, float y, float z, float dia, float w)
        {
            var p = Kit.Node(parent, "Wheel", 0f, y, z); y = 0f; z = 0f;   // spun by RideVisual
            Kit.Cy(p, 0f, y, z, dia, w, tyre, 0, 0.2f); Kit.Cy(p, 0f, y, z, dia * 0.86f, w + 0.004f, steel, 0, 0.7f, 0.9f); Kit.Cy(p, 0f, y, z, dia * 0.8f, w + 0.008f, Kit.H(0x3a3d42), 0, 0.3f); Kit.Cy(p, 0f, y, z, dia * 0.14f, w + 0.03f, steel, 0, 0.7f, 0.9f);
            for (int i = 0; i < 4; i++) { var s = Kit.Box(p, 0f, y, z, w + 0.01f, dia * 0.8f, 0.012f, steel, 0.7f, 0.9f); s.localRotation = Quaternion.Euler(i * 45f, 0f, 0f); }
        }
        static void Bicycle(Transform m)
        {
            float r = 0.34f; Wheel(m, r, 0.54f, r * 2f, 0.045f); Wheel(m, r, -0.5f, r * 2f, 0.045f);
            Vector3 bb = new Vector3(0f, 0.3f, -0.08f), seatTop = new Vector3(0f, 0.88f, -0.26f), head = new Vector3(0f, 0.86f, 0.36f), headLow = new Vector3(0f, 0.7f, 0.4f), rear = new Vector3(0f, r, -0.5f), front = new Vector3(0f, r, 0.54f);
            Tube(m, bb, seatTop, 0.04f, red); Tube(m, seatTop + new Vector3(0f, -0.1f, 0.03f), head, 0.04f, red); Tube(m, bb, headLow, 0.045f, red); Tube(m, head, headLow, 0.05f, red);
            foreach (float x in new[] { -0.04f, 0.04f }) { Tube(m, bb + Vector3.right * x, rear + Vector3.right * x, 0.025f, red); Tube(m, seatTop + new Vector3(x, -0.12f, 0.02f), rear + Vector3.right * x, 0.022f, red); Tube(m, headLow + Vector3.right * x, front + Vector3.right * x, 0.028f, steel); }
            Kit.Box(m, 0f, 0.93f, -0.28f, 0.14f, 0.05f, 0.27f, dark, 0.2f); Tube(m, head, head + new Vector3(0f, 0.14f, -0.02f), 0.03f, steel);
            Kit.Cy(m, 0f, 1.0f, 0.34f, 0.026f, 0.52f, steel, 0, 0.7f, 0.9f); foreach (float x in new[] { -0.22f, 0.22f }) Kit.Cy(m, x, 1.0f, 0.34f, 0.036f, 0.1f, dark, 0, 0.2f);
            Kit.Cy(m, 0.05f, 0.3f, -0.08f, 0.2f, 0.01f, steel, 0, 0.7f, 0.9f); Kit.Box(m, 0.1f, 0.2f, -0.02f, 0.02f, 0.02f, 0.18f, steel, 0.7f, 0.9f).localRotation = Quaternion.Euler(35f, 0f, 0f);
            Kit.Box(m, 0.16f, 0.14f, 0.04f, 0.1f, 0.02f, 0.07f, dark); Kit.Box(m, -0.16f, 0.44f, -0.2f, 0.1f, 0.02f, 0.07f, dark);
        }
        static void Scooter(Transform m)
        {
            Wheel(m, 0.09f, 0.36f, 0.18f, 0.035f); Wheel(m, 0.09f, -0.36f, 0.18f, 0.035f);
            Kit.Box(m, 0f, 0.11f, -0.04f, 0.13f, 0.03f, 0.56f, Kit.H(0x2d7dd2), 0.4f, 0.4f); Kit.Box(m, 0f, 0.127f, -0.04f, 0.11f, 0.004f, 0.5f, dark, 0.05f);
            Tube(m, new Vector3(0f, 0.12f, 0.24f), new Vector3(0f, 0.3f, 0.34f), 0.04f, steel); Tube(m, new Vector3(0f, 0.1f, 0.36f), new Vector3(0f, 1.02f, 0.3f), 0.035f, steel);
            Kit.Cy(m, 0f, 1.02f, 0.3f, 0.028f, 0.46f, steel, 0, 0.7f, 0.9f); foreach (float x in new[] { -0.19f, 0.19f }) Kit.Cy(m, x, 1.02f, 0.3f, 0.038f, 0.09f, dark, 0, 0.2f);
            Kit.Box(m, 0f, 0.17f, -0.37f, 0.06f, 0.012f, 0.16f, steel, 0.6f, 0.8f).localRotation = Quaternion.Euler(-20f, 0f, 0f);
        }
        static void Board(Transform m)
        {
            Kit.Box(m, 0f, 0.1f, 0f, 0.21f, 0.014f, 0.6f, Kit.H(0x8a5a34), 0.3f); Kit.Box(m, 0f, 0.108f, 0f, 0.2f, 0.004f, 0.58f, dark, 0.05f); Kit.Box(m, 0f, 0.092f, 0f, 0.15f, 0.004f, 0.3f, red, 0.3f);
            Kit.Box(m, 0f, 0.118f, 0.36f, 0.2f, 0.014f, 0.14f, Kit.H(0x8a5a34), 0.3f).localRotation = Quaternion.Euler(-18f, 0f, 0f); Kit.Box(m, 0f, 0.118f, -0.36f, 0.2f, 0.014f, 0.14f, Kit.H(0x8a5a34), 0.3f).localRotation = Quaternion.Euler(18f, 0f, 0f);
            foreach (float z in new[] { -0.22f, 0.22f }) { Kit.Box(m, 0f, 0.075f, z, 0.07f, 0.03f, 0.05f, steel, 0.7f, 0.9f); Kit.Cy(m, 0f, 0.05f, z, 0.012f, 0.2f, steel, 0, 0.7f, 0.9f); foreach (float x in new[] { -0.095f, 0.095f }) { var w = Kit.Node(m, "Wheel", x, 0.03f, z); Kit.Cy(w, 0f, 0f, 0f, 0.058f, 0.035f, Kit.H(0xe9dcc0), 0, 0.4f); Kit.Box(w, 0f, 0f, 0f, 0.037f, 0.05f, 0.008f, Kit.H(0xb23a2e)); } }
        }
    }

    /// <summary>Spins every "Wheel" node with the ride's speed and leans the frame into corners.</summary>
    public class RideVisual : MonoBehaviour
    {
        public Transform lean; public float maxLean = 18f;
        VehicleController vc; Transform[] wheels; float[] radius; float roll;
        void Start()
        {
            vc = GetComponent<VehicleController>(); var list = new System.Collections.Generic.List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>()) if (t.name == "Wheel") list.Add(t);
            wheels = list.ToArray(); radius = new float[wheels.Length];
            for (int i = 0; i < wheels.Length; i++) radius[i] = Mathf.Max(0.02f, Kit.BoundsOf(wheels[i]).size.y * 0.5f);
        }
        void Update()
        {
            if (!vc) return; float v = vc.Speed, dt = Time.deltaTime;
            for (int i = 0; i < wheels.Length; i++) wheels[i].Rotate(v / radius[i] * Mathf.Rad2Deg * dt, 0f, 0f, Space.Self);
            float want = vc.Controlled ? -vc.SteerInput * maxLean * Mathf.Clamp01(Mathf.Abs(v) / Mathf.Max(1f, vc.maxSpeed)) : 0f;
            roll = Mathf.Lerp(roll, want, 1f - Mathf.Exp(-6f * dt)); if (lean) lean.localRotation = Quaternion.Euler(0f, 0f, roll);
        }
    }
}
