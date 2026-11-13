using UnityEngine;

namespace MafiaSandbox.Player
{
    /// <summary>
    /// Third-person character movement on a CharacterController (camera-relative WASD, Shift sprint, C crouch, Space jump).
    /// Same tuning as the HTML prototype: walk 2.6 m/s, run 6 m/s, crouch 1.3 m/s.
    /// Stamina/hunger, weapons and aiming arrive in step 2.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Speeds (m/s)")]
        public float walkSpeed = 2.6f;
        public float runSpeed = 6f;
        public float crouchSpeed = 1.3f;
        public float turnSpeed = 720f;      // deg/s
        public float jumpSpeed = 5.2f;
        public float gravity = -18f;

        [Header("Refs")]
        public Transform cameraTransform;   // set by CityBuilder; falls back to Camera.main
        public CharacterAnimator animator;

        public bool IsCrouching { get; private set; }
        public bool IsRunning { get; private set; }
        public bool InputLocked { get; set; }  // true while driving / in menus
        public Vector3 Velocity => velocity;

        CharacterController cc;
        Vector3 velocity;
        float jumpAnimTimer;
        float standHeight, standCenterY;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (!animator) animator = GetComponent<CharacterAnimator>();
            standHeight = cc.height; standCenterY = cc.center.y;
        }

        void Update()
        {
            if (!cc.enabled) return;   // riding in a vehicle (VehicleInteraction disables the controller)
            if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
            float dt = Time.deltaTime;

            Vector2 input = InputLocked ? Vector2.zero : new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (!InputLocked && Input.GetKeyDown(KeyCode.C)) SetCrouch(!IsCrouching);
            IsRunning = !InputLocked && Input.GetKey(KeyCode.LeftShift) && !IsCrouching && input.sqrMagnitude > 0.01f;

            // camera-relative move direction
            Vector3 fwd = cameraTransform ? cameraTransform.forward : Vector3.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = cameraTransform ? cameraTransform.right : Vector3.right; right.y = 0f; right.Normalize();
            Vector3 move = (fwd * input.y + right * input.x);
            if (move.sqrMagnitude > 1f) move.Normalize();

            float speed = IsCrouching ? crouchSpeed : IsRunning ? runSpeed : walkSpeed;
            Vector3 horizontal = move * speed;

            // gravity & jump
            bool grounded = cc.isGrounded;
            if (grounded && velocity.y < 0f) velocity.y = -2f;
            if (grounded && !InputLocked && !IsCrouching && Input.GetKeyDown(KeyCode.Space))
            {
                velocity.y = jumpSpeed;
                jumpAnimTimer = animator ? animator.PlayOnce("Jump") : 0.6f;
            }
            velocity.y += gravity * dt;

            cc.Move(new Vector3(horizontal.x, velocity.y, horizontal.z) * dt);

            // face movement direction
            if (move.sqrMagnitude > 0.001f)
            {
                var target = Quaternion.LookRotation(move, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);
            }

            // animation
            if (animator)
            {
                if (jumpAnimTimer > 0f) { jumpAnimTimer -= dt; }
                else
                {
                    bool moving = move.sqrMagnitude > 0.001f;
                    string clip = IsCrouching ? (moving ? "Crouch_Walk" : "Crouch_Idle") : moving ? (IsRunning ? "Run" : "Walk") : "Idle";
                    animator.Play(clip);
                }
            }
        }

        public void SetCrouch(bool on)
        {
            IsCrouching = on;
            cc.height = on ? standHeight * 0.65f : standHeight;
            cc.center = new Vector3(0f, on ? standCenterY * 0.65f : standCenterY, 0f);
        }

        /// <summary>Teleport safely (CharacterController ignores direct transform writes while enabled).</summary>
        public void Teleport(Vector3 pos, float yawDeg)
        {
            cc.enabled = false;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDeg, 0f));
            velocity = Vector3.zero;
            cc.enabled = true;
        }
    }
}
