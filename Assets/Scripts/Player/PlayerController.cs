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
        float jumpPressedAt = float.NegativeInfinity;
        Vector3 visualScale = Vector3.one;
        Vector3 visualPosition;
        float planeOffset;
        Invention standingOn;
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
            var input = InputLocked ? Vector2.zero : GameInput.Move.ReadValue<Vector2>();
            var move = CameraRelative(input);

            const float rayStart = 0.3f;
            IsGrounded = controller.isGrounded || Physics.Raycast(transform.position + Vector3.up * rayStart, Vector3.down,
                rayStart + groundProbe, groundMask, QueryTriggerInteraction.Ignore);

            var wantsJump = !InputLocked && Time.time - jumpPressedAt <= jumpBuffer;
            if (IsGrounded && verticalVelocity <= 0f)
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

            var velocity = move * moveSpeed + Vector3.up * verticalVelocity;
            standingOn = null;
            var flags = controller.Move(velocity * Time.deltaTime);
            // Bumped a ceiling: start falling instead of sticking to it.
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
            FollowDrawingPlane((flags & CollisionFlags.Below) != 0);

            if (move.x > 0.1f) SetFacing(1f);
            else if (move.x < -0.1f) SetFacing(-1f);
        }

        // Called during controller.Move for everything B touches. Anything below B counts, edges included (a box edge
        // under the capsule gives a tilted normal); walls don't.
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y < 0.1f) return;
            var invention = hit.collider.GetComponentInParent<Invention>();
            if (invention != null) standingOn = invention;
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
            jumpPressedAt = float.NegativeInfinity;
            FacingSign = (rotation * Vector3.forward).x < -0.01f ? -1f : 1f;
            ApplyFacing();
            planeOffset = 0f;
            if (visual != null) visual.localPosition = visualPosition;
        }
    }
}
