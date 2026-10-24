using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Top-down explorer, no jump. Moves relative to the isometric camera and is grounded by a 3D downward raycast.
    /// The transform's pivot is at the feet.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] float moveSpeed = 5f;
        [SerializeField] float turnSpeed = 720f;
        [SerializeField] float gravity = 30f;
        [Tooltip("How far below the feet the ground ray reaches.")]
        [SerializeField] float groundProbe = 0.25f;
        [SerializeField] LayerMask groundMask = ~0;
        [SerializeField] Transform cameraTransform;

        CharacterController controller;
        float verticalVelocity;

        public bool IsGrounded { get; private set; }
        public bool InputLocked { get; set; }

        void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        void Update()
        {
            var input = InputLocked ? Vector2.zero : GameInput.Move.ReadValue<Vector2>();
            var move = CameraRelative(input);

            const float rayStart = 0.3f;
            IsGrounded = Physics.Raycast(transform.position + Vector3.up * rayStart, Vector3.down,
                rayStart + groundProbe, groundMask, QueryTriggerInteraction.Ignore);

            if (IsGrounded && verticalVelocity <= 0f) verticalVelocity = -2f;
            else verticalVelocity -= gravity * Time.deltaTime;

            var velocity = move * moveSpeed + Vector3.up * verticalVelocity;
            controller.Move(velocity * Time.deltaTime);

            if (move.sqrMagnitude > 0.001f)
            {
                var target = Quaternion.LookRotation(move, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
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

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
            verticalVelocity = 0f;
        }
    }
}
