using UnityEngine;

namespace CalHacks
{
    /// <summary>How much of the world the side camera shows.</summary>
    public enum CameraFraming
    {
        Tight, // Opening shot: close on B at the gap.
        Wide,  // After the first build: the whole hub opens up.
    }

    /// <summary>
    /// Paper Mario style side camera: perspective, yaw 0 (looking along +Z), tilted down a little.
    /// Smoothly follows the target on X and Y; on depth (Z) it follows only partly, so walking toward or away from
    /// the camera doesn't swing the shot. <see cref="SetFraming"/> blends between a tight and a wide shot.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] float fieldOfView = 35f;
        [Tooltip("Downward tilt in degrees.")]
        [SerializeField] float pitch = 17f;

        [Header("Framings")]
        [Tooltip("Metres from the focus point in the Tight framing.")]
        [SerializeField] float tightDistance = 14f;
        [Tooltip("Point looked at in the Tight framing, relative to the target's feet.")]
        [SerializeField] Vector3 tightOffset = new Vector3(0f, 1f, 0f);
        [Tooltip("Metres from the focus point in the Wide framing.")]
        [SerializeField] float wideDistance = 24f;
        [Tooltip("Point looked at in the Wide framing, relative to the target's feet. Looks ahead (+X) and up.")]
        [SerializeField] Vector3 wideOffset = new Vector3(4f, 2f, 0f);
        [Tooltip("Seconds to blend from one framing to the other.")]
        [SerializeField] float blendTime = 1f;
        [Tooltip("Seconds a Reveal holds the Wide shot before blending back to Tight.")]
        [SerializeField] float revealHold = 2.5f;

        [Header("Follow")]
        [Tooltip("Seconds to catch up with the target on X.")]
        [SerializeField] float smoothTime = 0.2f;
        [Tooltip("Seconds to catch up on Y; slower, so jumps don't bob the shot.")]
        [SerializeField] float verticalSmoothTime = 0.45f;
        [Tooltip("Share of the target's depth (Z) movement the camera follows: 0 never, 1 fully.")]
        [Range(0f, 1f)] [SerializeField] float depthFollow = 0.3f;
        [Tooltip("Depth (Z) the camera centres on when the target stands there.")]
        [SerializeField] float depthCentre;

        Vector3 focus;
        Vector3 focusVelocity;
        float blend;
        float blendGoal;
        float revertAt = -1f; // Time.time at which a Reveal blends back to Tight; < 0 when none is pending

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        public CameraFraming Framing { get; private set; } = CameraFraming.Tight;

        void Awake()
        {
            var cam = GetComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView = fieldOfView;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            Snap();
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (revertAt >= 0f && Time.time >= revertAt) SetFraming(CameraFraming.Tight);
            blend =Mathf.MoveTowards(blend, blendGoal, blendTime > 0f ? Time.deltaTime / blendTime : 1f);
            var desired = DesiredFocus;
            focus.x = Mathf.SmoothDamp(focus.x, desired.x, ref focusVelocity.x, smoothTime);
            focus.y = Mathf.SmoothDamp(focus.y, desired.y, ref focusVelocity.y, verticalSmoothTime);
            focus.z = Mathf.SmoothDamp(focus.z, desired.z, ref focusVelocity.z, smoothTime);
            Apply();
        }

        /// <summary>Blends to <paramref name="framing"/> over the blend time, or jumps there when <paramref name="instant"/>.</summary>
        public void SetFraming(CameraFraming framing, bool instant = false)
        {
            Framing = framing;
            blendGoal = framing == CameraFraming.Wide ? 1f : 0f;
            if (instant) blend = blendGoal;
            revertAt = -1f;
        }

        /// <summary>Blends out to the Wide shot, holds it for the reveal hold, then blends back to Tight.</summary>
        public void Reveal()
        {
            SetFraming(CameraFraming.Wide);
            revertAt = Time.time + blendTime + revealHold;
        }

        /// <summary>Jumps to the target with no smoothing (after a teleport or reset).</summary>
        public void Snap()
        {
            focusVelocity = Vector3.zero;
            if (target != null) focus = DesiredFocus;
            Apply();
        }

        float Eased => Mathf.SmoothStep(0f, 1f, blend);

        Quaternion Rotation => Quaternion.Euler(pitch, 0f, 0f);

        Vector3 DesiredFocus
        {
            get
            {
                var p = target.position;
                var z = depthCentre + (p.z - depthCentre) * depthFollow;
                return new Vector3(p.x, p.y, z) + Vector3.Lerp(tightOffset, wideOffset, Eased);
            }
        }

        void Apply()
        {
            var rotation = Rotation;
            transform.rotation = rotation;
            transform.position = focus - rotation * Vector3.forward * Mathf.Lerp(tightDistance, wideDistance, Eased);
        }
    }
}
