using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CalHacks
{
    /// <summary>
    /// Paper explorer (Player B) for the side view. WASD moves relative to the camera's yaw (with the side camera:
    /// A/D along X, W/S in depth), Space jumps. Grounded by a 3D downward raycast. The body never rotates: the
    /// <see cref="visual"/> child's X scale flips to face left or right. The transform's pivot is at the feet.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] float moveSpeed = 5f;
        [SerializeField] float gravity = 30f;
        [Tooltip("Used when no GameConfig is found; GameConfig.jumpHeight wins otherwise.")]
        [SerializeField] float jumpHeight = 1.2f;
        [Tooltip("A Space press this many seconds before landing still jumps.")]
        [SerializeField] float jumpBuffer = 0.12f;
        [Tooltip("How far below the feet the ground ray reaches.")]
        [SerializeField] float groundProbe = 0.25f;
        [SerializeField] LayerMask groundMask = ~0;
        [SerializeField] Transform cameraTransform;
        [Tooltip("Child holding the paper sprite. Its X scale is flipped to face left or right.")]
        [SerializeField] Transform visual;

        CharacterController controller;
        float verticalVelocity;
        float jumpPressedAt = float.NegativeInfinity;
        Vector3 visualScale = Vector3.one;
        Action<InputAction.CallbackContext> onJump;
        float horizontalImpulse;
        float forcedAirborneUntil;
        InventionPhysics riding;
        Vector3 lastPlatformPosition;
        float rideContactTime;
        float rideLockedUntil;

        public bool IsGrounded { get; private set; }
        public bool InputLocked { get; set; }

        /// <summary>+1 facing right (+X), -1 facing left.</summary>
        public float FacingSign { get; private set; } = 1f;

        public float Radius => controller != null ? controller.radius : 0.4f;

        float JumpHeight => config != null ? config.jumpHeight : jumpHeight;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (config == null) config = Resources.Load<GameConfig>("GameConfig");
            if (visual != null) visualScale = visual.localScale;
            ApplyFacing();
        }

        // The press is latched from the `performed` callback (like RunManager.Bind) so it's never lost between updates.
        void OnEnable()
        {
            onJump = _ => jumpPressedAt = Time.time;
            GameInput.Jump.performed += onJump;
        }

        void OnDisable()
        {
            if (onJump != null) GameInput.Jump.performed -= onJump;
            onJump = null;
        }

        void Update()
        {
            // CharacterController doesn't automatically inherit a moving Rigidbody platform's delta.
            // Apply the ride motion before standard WASD/Space movement.
            if (riding != null && (Time.time < rideLockedUntil || Time.time - rideContactTime <= 0.22f))
            {
                var movement = riding.transform.position - lastPlatformPosition;
                if (movement.sqrMagnitude > 0f && movement.sqrMagnitude < 16f)
                    controller.Move(movement);
                lastPlatformPosition = riding.transform.position;
            }
            else riding = null;

            var input = InputLocked ? Vector2.zero : GameInput.Move.ReadValue<Vector2>();
            var move = CameraRelative(input);

            const float rayStart = 0.3f;
            IsGrounded = controller.isGrounded || Physics.Raycast(transform.position + Vector3.up * rayStart, Vector3.down,
                rayStart + groundProbe, groundMask, QueryTriggerInteraction.Ignore);

            var wantsJump = !InputLocked && Time.time - jumpPressedAt <= jumpBuffer;
            if (riding != null && Time.time < rideLockedUntil)
            {
                // CharacterController has its own gravity and does not inherit
                // Rigidbody velocity. While riding a rocket, only carry the
                // platform delta; Space detaches so the player can jump away.
                if (wantsJump)
                {
                    rideLockedUntil = 0f;
                    riding = null;
                    verticalVelocity = Mathf.Sqrt(2f * gravity * JumpHeight);
                    forcedAirborneUntil = Time.time + 0.2f;
                    jumpPressedAt = float.NegativeInfinity;
                }
                else
                {
                    verticalVelocity = 0f;
                    horizontalImpulse = 0f;
                    return;
                }
            }
            if (IsGrounded && Time.time >= forcedAirborneUntil && verticalVelocity <= 0f)
            {
                verticalVelocity = -2f;
                if (wantsJump)
                {
                    verticalVelocity = Mathf.Sqrt(2f * gravity * JumpHeight);
                    jumpPressedAt = float.NegativeInfinity;
                }
            }
            else
            {
                verticalVelocity -= gravity * Time.deltaTime;
            }

            horizontalImpulse = Mathf.MoveTowards(horizontalImpulse, 0f, 4f * Time.deltaTime);
            var velocity = move * moveSpeed + new Vector3(horizontalImpulse, verticalVelocity, 0f);
            var flags = controller.Move(velocity * Time.deltaTime);
            // Bumped a ceiling: start falling instead of sticking to it.
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;

            if (move.x > 0.1f) SetFacing(1f);
            else if (move.x < -0.1f) SetFacing(-1f);
        }

        public void Launch(Vector3 velocity)
        {
            verticalVelocity = Mathf.Max(0f, velocity.y);
            horizontalImpulse = velocity.x;
            forcedAirborneUntil = Time.time + 0.20f;
            riding = null;
            rideLockedUntil = 0f;
        }

        public bool IsRiding(InventionPhysics invention) => riding == invention;

        public void Disembark(InventionPhysics invention)
        {
            if (riding != invention) return;
            riding = null;
            rideLockedUntil = 0f;
            forcedAirborneUntil = Time.time + 0.15f;
            verticalVelocity = -1f;
        }

        public void BoardInvention(InventionPhysics platform, Invention invention, float duration)
        {
            if (platform == null || invention == null) return;
            var boxes = invention.GetComponentsInChildren<BoxCollider>();
            if (boxes.Length == 0) return;
            var bounds = boxes[0].bounds;
            for (int i = 1; i < boxes.Length; i++) bounds.Encapsulate(boxes[i].bounds);
            // Let the character board via E from the side; do not require
            // climbing to the top of a tall, vertically drawn rocket first.
            float x = bounds.size.x >= Radius * 2f ? Mathf.Clamp(transform.position.x, bounds.min.x + Radius, bounds.max.x - Radius) : bounds.center.x;
            var pos = new Vector3(x, bounds.max.y + 0.07f, platform.transform.position.z);
            controller.enabled = false;
            transform.position = pos;
            controller.enabled = true;
            verticalVelocity = 0f;
            horizontalImpulse = 0f;
            jumpPressedAt = float.NegativeInfinity;
            riding = platform;
            lastPlatformPosition = platform.transform.position;
            rideLockedUntil = Time.time + duration + 0.2f;
            rideContactTime = Time.time;
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // A genuine downward landing squashes small living cutouts into a
            // harmless paper-confetti poof. Merely stepping onto a newly
            // created drawing does not squash it.
            var motion = hit.collider.GetComponentInParent<InventionMotion>();
            if (motion != null && motion.TrySquash(verticalVelocity, hit))
            {
                Launch(new Vector3(0f, 2.5f, 0f)); // Little arcade stomp bounce.
                return;
            }
            var behavior = hit.collider.GetComponentInParent<InventionPhysics>();
            if (behavior == null) return;
            behavior.PlayerContact(this, hit);
            if (behavior.MovingPlatform && hit.normal.y > 0.45f && verticalVelocity <= 0f)
            {
                if (riding != behavior) lastPlatformPosition = behavior.transform.position;
                riding = behavior;
                rideContactTime = Time.time;
            }
        }

        Vector3 CameraRelative(Vector2 input)
        {
            var cam = cameraTransform != null ? cameraTransform : Camera.main != null ? Camera.main.transform : null;
            var yaw = cam != null ? cam.eulerAngles.y : 0f;
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var move = rotation * new Vector3(input.x, 0f, input.y);
            return Vector3.ClampMagnitude(move, 1f);
        }

        void SetFacing(float sign)
        {
            if (Mathf.Approximately(sign, FacingSign)) return;
            FacingSign = sign;
            ApplyFacing();
        }

        void ApplyFacing()
        {
            if (visual == null) return;
            visual.localScale = new Vector3(Mathf.Abs(visualScale.x) * FacingSign, visualScale.y, visualScale.z);
        }

        /// <summary>Moves to <paramref name="position"/>. The body stays unrotated; <paramref name="rotation"/>'s forward only sets the facing.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.identity);
            controller.enabled = true;
            verticalVelocity = 0f;
            horizontalImpulse = 0f;
            riding = null;
            rideLockedUntil = 0f;
            jumpPressedAt = float.NegativeInfinity;
            FacingSign = (rotation * Vector3.forward).x < -0.01f ? -1f : 1f;
            ApplyFacing();
        }
    }
}
