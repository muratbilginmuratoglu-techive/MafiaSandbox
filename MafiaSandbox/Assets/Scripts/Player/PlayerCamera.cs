using UnityEngine;

namespace MafiaSandbox.Player
{
    /// <summary>
    /// Orbit third-person camera with wall avoidance, first-person toggle (V) and a vehicle follow mode.
    /// Click the game view to lock the cursor; Esc releases it.
    /// </summary>
    public class PlayerCamera : MonoBehaviour
    {
        public Transform target;                 // player root
        public Vector3 pivotOffset = new Vector3(0f, 1.6f, 0f);
        public float distance = 4.5f;
        public float shoulder = 0.45f;
        public float sensitivity = 2.2f;
        public float minPitch = -35f, maxPitch = 70f;
        public LayerMask collisionMask = ~0;

        [Header("Vehicle mode")]
        public Transform vehicleTarget;          // set while driving
        public float vehicleDistance = 7.5f;
        public float vehicleHeight = 2.6f;

        public bool FirstPerson { get; private set; }
        public float Yaw => yaw;

        float yaw, pitch = 12f;
        Renderer[] targetRenderers;

        void Start()
        {
            if (target) { yaw = target.eulerAngles.y; targetRenderers = target.GetComponentsInChildren<Renderer>(); }
        }

        void Update()
        {
            if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (Input.GetKeyDown(KeyCode.V) && !vehicleTarget) SetFirstPerson(!FirstPerson);

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                yaw += Input.GetAxis("Mouse X") * sensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * sensitivity, minPitch, maxPitch);
            }
        }

        void LateUpdate()
        {
            if (vehicleTarget) { FollowVehicle(); return; }
            if (!target) return;

            var rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot = target.position + pivotOffset;

            if (FirstPerson)
            {
                transform.SetPositionAndRotation(pivot + rot * new Vector3(0f, 0.05f, 0.15f), rot);
                // keep the body facing the view direction so the gun (step 2) lines up
                target.rotation = Quaternion.Euler(0f, yaw, 0f);
                return;
            }

            Vector3 desired = pivot + rot * new Vector3(shoulder, 0f, -distance);
            Vector3 dir = desired - pivot; float len = dir.magnitude;
            if (Physics.SphereCast(pivot, 0.25f, dir / len, out var hit, len, collisionMask, QueryTriggerInteraction.Ignore))
                desired = pivot + dir / len * Mathf.Max(0.4f, hit.distance - 0.1f);
            transform.SetPositionAndRotation(desired, rot);
        }

        void FollowVehicle()
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot = vehicleTarget.position + Vector3.up * 1.2f;
            Vector3 desired = pivot + rot * new Vector3(0f, vehicleHeight * 0.35f, -vehicleDistance) + Vector3.up * (vehicleHeight * 0.4f);
            Vector3 dir = desired - pivot; float len = dir.magnitude;
            if (Physics.SphereCast(pivot, 0.3f, dir / len, out var hit, len, collisionMask, QueryTriggerInteraction.Ignore))
                desired = pivot + dir / len * Mathf.Max(1f, hit.distance - 0.1f);
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-10f * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
        }

        public void SetFirstPerson(bool on)
        {
            FirstPerson = on;
            if (targetRenderers != null)
                foreach (var r in targetRenderers)
                    r.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
        }

        /// <summary>Called by VehicleInteraction when entering/leaving a car.</summary>
        public void SetVehicle(Transform vehicle)
        {
            vehicleTarget = vehicle;
            if (vehicle) { yaw = vehicle.eulerAngles.y; pitch = 14f; if (FirstPerson) SetFirstPerson(false); }
            else if (target) yaw = target.eulerAngles.y;
        }
    }
}
