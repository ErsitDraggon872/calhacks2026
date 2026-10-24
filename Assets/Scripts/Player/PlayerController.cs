using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CalHacks
{
    /// <summary>
    /// Paper explorer (Player B) for the side view. WASD moves relative to the camera's yaw (with the side camera:
    /// A/D along X, W/S in depth), Space jumps. Grounded by a 3D downward raycast. The body never rotates: the
    /// <see cref="visual"/> child's X scale flips to face left or right. The transform's pivot is at the feet.
    /// An invention is a flat drawing with a deep collider, so while B stands on one the visual slides to the drawing's
    /// depth: seen from the pitched camera, B then stands on the drawn edge wherever B is in the collider's depth.
    /// An invention's <see cref="InventionSurface"/> changes how B moves on it: slippery slides, bouncy throws B up,
    /// sticky slows B down.
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
        [Tooltip("How fast (m/s) the visual slides to an invention's drawing plane and back.")]
        [SerializeField] float planeSnapSpeed = 8f;

        CharacterController controller;
        float verticalVelocity;
        // The last fall speed in the air: the ground ray snaps verticalVelocity to -2 just before touchdown.
        float airVelocity;
        float jumpPressedAt = float.NegativeInfinity;
        Vector3 visualScale = Vector3.one;
        Vector3 visualPosition;
        float planeOffset;
        Invention standingOn;
        // What B last landed on, for its surface; null on level geometry.
        Invention ground;
        Vector3 walkVelocity;
        float horizontalImpulse, forcedAirborneUntil;
        InventionPhysics riding;
        Vector3 lastPlatformPosition;
        float rideContactTime;
        Action<InputAction.CallbackContext> onJump;

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
            if (visual != null)
            {
                visualScale = visual.localScale;
                visualPosition = visual.localPosition;
            }
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
            if (riding != null)
            {
                var platform = riding;
                var position = platform.transform.position;
                controller.Move(position - lastPlatformPosition);
                // A collision callback can launch/disembark the player during Move.
                if (riding == platform)
                {
                    lastPlatformPosition = position;
                    if (Time.time - rideContactTime > .15f) riding = null;
                }
            }
            var input = InputLocked ? Vector2.zero : GameInput.Move.ReadValue<Vector2>();
            var move = CameraRelative(input);

            const float rayStart = 0.3f;
            IsGrounded = Time.time >= forcedAirborneUntil && (controller.isGrounded || riding != null ||
                Physics.Raycast(transform.position + Vector3.up * rayStart, Vector3.down,
                rayStart + groundProbe, groundMask, QueryTriggerInteraction.Ignore));

            var surface = IsGrounded && ground != null ? ground.Surface : InventionSurface.Normal;
            var wantsJump = !InputLocked && Time.time - jumpPressedAt <= jumpBuffer;
            if (IsGrounded && verticalVelocity <= 0f)
            {
                verticalVelocity = -2f;
                if (wantsJump)
                {
                    riding = null;
                    forcedAirborneUntil = Time.time + .2f;
                    verticalVelocity = Mathf.Sqrt(2f * gravity * JumpHeight);
                    jumpPressedAt = float.NegativeInfinity;
                }
                else if (surface.Bounce > 0f && (ground == null || ground.Abilities == null))
                {
                    verticalVelocity = surface.Bounce;
                }
            }
            else
            {
                verticalVelocity -= gravity * Time.deltaTime;
                airVelocity = verticalVelocity;
            }

            // In the air, and on normal ground, walking speed changes instantly; on a slippery build it slides.
            var targetWalk = move * moveSpeed * surface.SpeedScale;
            walkVelocity = IsGrounded ? Vector3.MoveTowards(walkVelocity, targetWalk, surface.Grip * Time.deltaTime) : targetWalk;
            horizontalImpulse = Mathf.MoveTowards(horizontalImpulse, 0f, 4f * Time.deltaTime);
            var velocity = walkVelocity + new Vector3(horizontalImpulse, verticalVelocity, 0f);
            standingOn = null;
            var flags = controller.Move(velocity * Time.deltaTime);
            // Bumped a ceiling: start falling instead of sticking to it.
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
            var landed = (flags & CollisionFlags.Below) != 0;
            if (landed)
            {
                ground = standingOn;
                airVelocity = 0f;
            }
            FollowDrawingPlane(landed);

            if (move.x > 0.1f) SetFacing(1f);
            else if (move.x < -0.1f) SetFacing(-1f);
        }

        // Called during controller.Move for everything B touches. Anything below B counts, edges included (a box edge
        // under the capsule gives a tilted normal); walls don't.
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Andy's stomp: a real downward landing squashes a small living cutout into paper confetti.
            // Stepping onto a freshly built one doesn't.
            var motion = hit.collider.GetComponentInParent<InventionMotion>();
            if (motion != null && motion.TrySquash(Mathf.Min(verticalVelocity, airVelocity), hit))
            {
                Launch(new Vector3(0f, 2.5f, 0f));
                return;
            }
            var ability = hit.collider.GetComponentInParent<InventionPhysics>();
            if (ability != null)
            {
                ability.PlayerContact(this, hit);
                if (ability.MovingPlatform && hit.normal.y > .45f && verticalVelocity <= 0f && Time.time >= forcedAirborneUntil)
                {
                    if (riding != ability) lastPlatformPosition = ability.transform.position;
                    riding = ability;
                    rideContactTime = Time.time;
                }
            }
            if (hit.normal.y < 0.1f) return;
            var invention = hit.collider.GetComponentInParent<Invention>();
            if (invention != null) standingOn = invention;
        }

        public void Launch(Vector3 velocity)
        {
            riding = null;
            verticalVelocity = Mathf.Max(0f, velocity.y);
            horizontalImpulse = velocity.x;
            forcedAirborneUntil = Time.time + .2f;
        }

        public bool IsRiding(InventionPhysics platform) => riding == platform;

        public void Disembark(InventionPhysics platform)
        {
            if (riding != platform) return;
            riding = null;
            forcedAirborneUntil = Time.time + .15f;
            verticalVelocity = -1f;
        }

        public void BoardInvention(InventionPhysics platform, Invention invention)
        {
            var boxes = invention.GetComponentsInChildren<BoxCollider>();
            if (boxes.Length == 0) return;
            var bounds = boxes[0].bounds;
            for (var i = 1; i < boxes.Length; i++) bounds.Encapsulate(boxes[i].bounds);
            var x = bounds.size.x >= Radius * 2f
                ? Mathf.Clamp(transform.position.x, bounds.min.x + Radius, bounds.max.x - Radius) : bounds.center.x;
            Teleport(new Vector3(x, bounds.max.y + .07f, platform.transform.position.z),
                Quaternion.LookRotation(Vector3.right * FacingSign));
            riding = platform;
            lastPlatformPosition = platform.transform.position;
            rideContactTime = Time.time;
        }

        /// <summary>
        /// On an invention, slides the visual to its drawing's depth; on anything else, back to B's own. In the air it
        /// keeps the last plane, so jumping off a build doesn't make B bob.
        /// </summary>
        void FollowDrawingPlane(bool grounded)
        {
            if (visual == null) return;
            if (grounded)
            {
                var target = standingOn != null ? standingOn.transform.position.z - transform.position.z : 0f;
                planeOffset = Mathf.MoveTowards(planeOffset, target, planeSnapSpeed * Time.deltaTime);
            }
            visual.localPosition = visualPosition + Vector3.forward * planeOffset;
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
            walkVelocity = Vector3.zero;
            horizontalImpulse = 0f;
            forcedAirborneUntil = 0f;
            riding = null;
            ground = null;
            jumpPressedAt = float.NegativeInfinity;
            FacingSign = (rotation * Vector3.forward).x < -0.01f ? -1f : 1f;
            ApplyFacing();
            planeOffset = 0f;
            if (visual != null) visual.localPosition = visualPosition;
        }
    }
}
