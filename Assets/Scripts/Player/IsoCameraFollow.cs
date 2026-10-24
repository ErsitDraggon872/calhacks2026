using UnityEngine;

namespace CalHacks
{
    /// <summary>Orthographic isometric camera (about 30° pitch, 45° yaw) that smoothly follows a target.</summary>
    [RequireComponent(typeof(Camera))]
    public class IsoCameraFollow : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] float pitch = 30f;
        [SerializeField] float yaw = 45f;
        [SerializeField] float distance = 30f;
        [SerializeField] float orthographicSize = 7f;
        [SerializeField] float smoothTime = 0.2f;
        [Tooltip("Point the camera looks at, relative to the target's feet.")]
        [SerializeField] Vector3 focusOffset = new Vector3(0f, 1f, 0f);

        Vector3 velocity;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        void Awake()
        {
            var cam = GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = orthographicSize;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = distance * 3f;
            Snap();
        }

        void LateUpdate()
        {
            if (target == null) return;
            transform.rotation = Rotation;
            transform.position = Vector3.SmoothDamp(transform.position, DesiredPosition, ref velocity, smoothTime);
        }

        public void Snap()
        {
            transform.rotation = Rotation;
            if (target != null) transform.position = DesiredPosition;
            velocity = Vector3.zero;
        }

        Quaternion Rotation => Quaternion.Euler(pitch, yaw, 0f);

        Vector3 DesiredPosition => target.position + focusOffset - Rotation * Vector3.forward * distance;
    }
}
