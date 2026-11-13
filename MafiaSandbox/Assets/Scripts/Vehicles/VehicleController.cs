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

        Rigidbody rb;
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

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
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

            // velocity: keep PhysX gravity on Y, drive XZ ourselves
            Vector3 v = rb.linearVelocity;
            Vector3 planar = transform.forward * speed;
            rb.linearVelocity = new Vector3(planar.x, v.y, planar.z);
        }

        void OnCollisionEnter(Collision c)
        {
            // lose speed on hard frontal/rear hits so we don't tunnel along walls
            float along = Vector3.Dot(c.relativeVelocity, transform.forward);
            if (Mathf.Abs(along) > 2f) speed *= 0.25f;
        }
    }
}
