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

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            if (socket != null && socket.Solved) return;
            if (Time.time - lastShown < cooldown) return;
            lastShown = Time.time;
            Subtitles.Show(text);
        }

        public void ResetHint() => lastShown = float.NegativeInfinity;
    }
}
