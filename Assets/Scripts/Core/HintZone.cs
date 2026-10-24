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
        [Tooltip("While the explorer is inside, show the build ghost (where a free build will land). For free-build " +
                 "obstacles only: a socket has its own outline.")]
        [SerializeField] bool showBuildGhost;

        float lastShown = float.NegativeInfinity;
        Collider area;

        void Awake() => area = GetComponent<Collider>();

        void OnEnable()
        {
            if (showBuildGhost) BuildGhost.Register(this);
        }

        void OnDisable() => BuildGhost.Unregister(this);

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            if (socket != null && socket.Solved) return;
            if (Time.time - lastShown < cooldown) return;
            lastShown = Time.time;
            Subtitles.Show(text);
        }

        /// <summary>Whether <paramref name="point"/> is inside this zone.</summary>
        public bool Contains(Vector3 point) => area != null && area.bounds.Contains(point);

        public void ResetHint() => lastShown = float.NegativeInfinity;
    }
}
