using UnityEngine;

namespace CalHacks
{
    /// <summary>Shows a subtitle stating an obstacle's need when the explorer walks in. States the need, never the answer.</summary>
    [RequireComponent(typeof(Collider))]
    public class HintZone : MonoBehaviour
    {
        [TextArea] [SerializeField] string text = "Something is in the way.";
        [Tooltip("Hint stops showing once this socket is solved. Optional.")]
        [SerializeField] PuzzleSocket socket;
        [SerializeField] float cooldown = 8f;

        float lastShown = float.NegativeInfinity;
        // Set once B gets over the top or out the far side: the obstacle's beaten, so the hint stays quiet
        // (standing on a drawn step or riding a rocket up through the zone would otherwise fire it again).
        bool passed;
        Collider zone;

        void Awake() => zone = GetComponent<Collider>();

        void Update()
        {
            if (passed) return;
            var player = RunManager.Instance != null ? RunManager.Instance.Player : null;
            if (player == null) return;
            var bounds = zone.bounds;
            var at = player.transform.position;
            if (at.y > bounds.max.y || at.x > bounds.max.x) passed = true;
        }

        void OnTriggerEnter(Collider other)
        {
            var player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;
            if (passed || player.IsRidingAnything) return;
            if (socket != null && socket.Solved) return;
            if (Time.time - lastShown < cooldown) return;
            lastShown = Time.time;
            Subtitles.Show(text);
        }

        public void ResetHint()
        {
            lastShown = float.NegativeInfinity;
            passed = false;
        }
    }
}
