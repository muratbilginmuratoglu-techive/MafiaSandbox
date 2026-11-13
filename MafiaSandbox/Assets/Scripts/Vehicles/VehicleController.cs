using UnityEngine;

namespace MafiaSandbox.Vehicles
{
    /// <summary>
    /// Arcade car on a Rigidbody: W/S throttle & reverse, A/D steer, Space handbrake. Forward is the root's +Z
    /// (CityBuilder parents the GLB model with a 90° yaw so its glTF +X nose points down +Z).
    /// Speeds match the prototype: ~17 m/s top speed (≈60 km/h), vans/buses a bit slower.
    /// Traffic AI drives these through <see cref="SetInput"/> in step 4.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        public string displayName = "Car";
        public bool isPlayerCar;
        public float maxSpeed = 17f;
        public float maxReverse = 6f;
        public float acceleration = 7f;
        public float brakeDecel = 16f;
        public float coastDecel = 2.5f;
        public float maxSteerDeg = 32f;
        public float wheelBase = 2.7f;
        public float length = 4.4f, width = 1.8f;

        public bool Controlled { get; private set; }
        public float Speed => speed;             // signed, m/s along forward

        Rigidbody rb; bool lifted; int groundMask; float fallV;
        float speed, throttle, steer; bool handbrake;
        Transform[] wheels;                      // optional: nodes named Wheel_* spin for show

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.mass = 1200f; rb.linearDamping = 0.05f; rb.angularDamping = 2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.centerOfMass = new Vector3(0f, -0.3f, 0f);
            // navmesh: carve a hole so pedestrians walk around (parked or driving)
            var obs = GetComponent<UnityEngine.AI.NavMeshObstacle>(); if (!obs) obs = gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obs.shape = UnityEngine.AI.NavMeshObstacleShape.Box; obs.size = new Vector3(width + 0.3f, 1.5f, length + 0.3f); obs.center = new Vector3(0f, 0.75f, 0f); obs.carving = true; obs.carveOnlyStationary = false; obs.carvingMoveThreshold = 0.3f;
            int vl = LayerMask.NameToLayer("Vehicles"); if (vl >= 0) gameObject.layer = vl;
            // The city's road decals (crosswalks, lane paint) are thin one-sided planes a few cm above the asphalt, and a box
            // collider resting on the asphalt catches their edges like a wall. So cars don't touch the ground at all: the
            // collider floats 35 cm up and the car rides on a downward raycast (see FixedUpdate).
            var box = GetComponent<BoxCollider>(); if (box && !lifted) { box.center += Vector3.up * 0.3f; lifted = true; }
            rb.useGravity = false; rb.maxDepenetrationVelocity = 2f;
            groundMask = 1 << 0;   // Default: roads, terrain, ramps
            var list = new System.Collections.Generic.List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>()) if (t.name.StartsWith("Wheel_")) list.Add(t);
            wheels = list.ToArray();
        }

        public void SetControlled(bool on) { Controlled = on; if (!on) { throttle = 0f; steer = 0f; handbrake = true; } }
        public void SetInput(float throttle01, float steer01, bool brake) { throttle = Mathf.Clamp(throttle01, -1f, 1f); steer = Mathf.Clamp(steer01, -1f, 1f); handbrake = brake; }

        void Update()
        {
            if (Controlled)
            {
                throttle = Input.GetAxisRaw("Vertical");
                steer = Input.GetAxisRaw("Horizontal");
                handbrake = Input.GetKey(KeyCode.Space);
            }
            // cosmetic wheel spin
            if (wheels != null && Mathf.Abs(speed) > 0.05f)
            {
                float deg = speed / 0.34f * Mathf.Rad2Deg * Time.deltaTime;
                foreach (var w in wheels) w.Rotate(Vector3.right, deg, Space.Self);
            }
        }

        /// <summary>Seconds we've been pushing into something solid without moving (AI uses this to back out).</summary>
        public float WallT { get; private set; }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            { Vector3 was = rb.linearVelocity; was.y = 0f; if (Mathf.Abs(speed) > 1f && was.magnitude < 0.35f && Time.time - LastContactT < 0.25f) WallT += dt; else WallT = 0f; }
            // longitudinal
            if (handbrake) speed = Mathf.MoveTowards(speed, 0f, brakeDecel * dt);
            else if (Mathf.Abs(throttle) > 0.01f)
            {
                bool braking = Mathf.Sign(throttle) != Mathf.Sign(speed) && Mathf.Abs(speed) > 0.3f;
                if (braking) speed = Mathf.MoveTowards(speed, 0f, brakeDecel * dt);
                else speed = Mathf.MoveTowards(speed, throttle > 0f ? maxSpeed : -maxReverse, acceleration * dt);
            }
            else speed = Mathf.MoveTowards(speed, 0f, coastDecel * dt);

            // steering (bicycle-ish): yaw rate from steer angle and speed
            float steerAngle = steer * maxSteerDeg * Mathf.Lerp(1f, 0.45f, Mathf.Abs(speed) / maxSpeed);
            float yawRate = speed / wheelBase * Mathf.Tan(steerAngle * Mathf.Deg2Rad) * Mathf.Rad2Deg; // deg/s
            if (Mathf.Abs(speed) > 0.05f) rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, yawRate * dt, 0f));

            // ground follow: ride on a raycast instead of resting on the mesh (see Awake)
            float vy; float gy = float.MinValue; Vector3 fwd = transform.forward * (length * 0.5f - 0.4f);
            foreach (var off in new[] { Vector3.zero, fwd, -fwd })   // centre, nose and tail: ride the highest so the box clears ramps
                if (Physics.Raycast(rb.position + off + Vector3.up * 1.6f, Vector3.down, out var gh, 8f, groundMask, QueryTriggerInteraction.Ignore) && gh.point.y > gy) { gy = gh.point.y; DebugGround = gh.collider.name + " y" + gy.ToString("F2"); }
            if (gy > float.MinValue)
            {
                float ny = Mathf.MoveTowards(rb.position.y, gy, (Mathf.Abs(gy - rb.position.y) > 1.5f ? 12f : 6f) * dt);
                vy = (ny - rb.position.y) / dt; fallV = 0f;
            }
            else { fallV -= 9.81f * dt; vy = fallV; }
            Vector3 planar = transform.forward * speed;
            rb.linearVelocity = new Vector3(planar.x, vy, planar.z);
        }

        public string LastContact = "-"; public float LastContactT;
        public Vector3 RbVelocity => rb ? rb.linearVelocity : Vector3.zero;
        public string DebugGround = "-";
        void OnCollisionStay(Collision c)
        {
            var n = c.GetContact(0).normal; LastContact = c.collider.name + "/" + LayerMask.LayerToName(c.collider.gameObject.layer) + " n" + n.ToString("F2"); LastContactT = Time.time;
        }

        void OnCollisionEnter(Collision c)
        {
            // pedestrians: knock them down (ragdoll) when fast, nudge them when slow
            var body = c.collider.GetComponentInParent<MafiaSandbox.Combat.CharacterBody>();
            if (body != null && !body.Dead)
            {
                float v = Mathf.Abs(speed);
                Vector3 dir = (body.transform.position - transform.position); dir.y = 0f; dir = (dir.normalized * 0.8f + transform.forward * Mathf.Sign(speed) * 0.6f).normalized;
                var contact = c.GetContact(0).point;
                if (v > 4f) { body.Hit(body.ZoneNear(contact), contact, dir, v > 9f ? 99 : 1, v > 9f, Mathf.Clamp(v * 0.5f, 1f, 8f), Controlled ? "player" : "vehicle"); MafiaSandbox.Core.Sfx.At("thud", contact); speed *= 0.6f; }
                else { var cc = body.GetComponent<CharacterController>(); if (cc && cc.enabled) cc.Move(dir * 0.4f); speed *= 0.5f; }
                return;
            }
            // lose speed on hard frontal/rear hits so we don't tunnel along walls
            float along = Vector3.Dot(c.relativeVelocity, transform.forward);
            if (Mathf.Abs(along) > 2f) speed *= 0.25f;
        }
    }
}
