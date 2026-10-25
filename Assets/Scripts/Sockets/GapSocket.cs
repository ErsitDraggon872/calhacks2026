using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Tutorial gap: a drawing waits for its label. Only a bridge or a mystery shape (<see cref="SocketLogic.IsBridge"/>)
    /// goes in the gap; anything else is built as itself on B's ledge (<see cref="LaunchSpot"/>), so a rocket can carry B
    /// across. The bridge is stretched to span the gap (Span.x), its top edge <see cref="railHeight"/> above the
    /// ledges, and built as an <see cref="Invention"/>: B walks on the ink, so a drawing with a break, or one that
    /// doesn't reach a ledge, drops B into the gap. The gap stays unsolved, so the next BUILD in range replaces the
    /// bridge, until B gets past the far ledge (the gap is crossed along +X).
    /// </summary>
    public class GapSocket : PuzzleSocket
    {
        [Tooltip("Invisible side rails, on while a bridge stands, so the explorer can't walk off the bridge's edges.")]
        [SerializeField] GameObject rails;
        [Tooltip("Dashed \"draw here\" outline, shown until a bridge stands.")]
        [SerializeField] GameObject drawHint;
        [Tooltip("How far the top of the drawing sits above the ledges.")]
        [SerializeField] float railHeight = 0.4f;
        [Tooltip("Shortest the drawing is squashed to, however wide it is.")]
        [SerializeField] float minHeight = 1f;
        [Tooltip("How deep (Z) the bridge's colliders are, centred on the anchor. Matches the space between the rails.")]
        [SerializeField] float depth = 3.2f;

        Invention bridge;
        PlayerController player;

        public override SocketType Type => SocketType.Gap;
        public override string Goal => "get B across the gap";
        public override string Banner => "draw a way across";
        // Shape solves the gap, not meaning: whatever it is, the lines are the bridge.
        public override string Verdict(InventionSpec spec, LabelOutcome outcome) => "YOUR LINES → A BRIDGE. WALK ACROSS!";

        float FarLedgeX => anchor.position.x + span.x * 0.5f;

        protected override void Awake()
        {
            base.Awake();
            Refresh();
        }

        void Start() => player = FindFirstObjectByType<PlayerController>();

        void Update()
        {
            // However B got across (bridge, rocket, bird), the gap is done.
            if (Solved || player == null) return;
            if (player.transform.position.x > FarLedgeX) Solve();
        }

        protected override Cutout CreateCutout(Texture2D texture, bool ownsTexture)
        {
            RemoveBridge();
            var aspect = texture.height > 0 ? (float)texture.width / texture.height : 1f;
            var size = new Vector2(span.x, Mathf.Clamp(span.x / aspect, minHeight, span.y));
            var bottom = anchor.position + Vector3.up * (railHeight - size.y);
            // The invention reads the pixels and owns the texture it's given.
            var paper = ownsTexture ? texture : Instantiate(texture);
            bridge = Invention.Create(paper, bottom, size, depth, cutoutMaterial, transform);
            // The planks clack into place as play resumes after the card.
            Juice.PlayAfterCard(JuiceSound.Clack);
            return bridge.Cutout;
        }

        protected override void OnCaptured(CaptureResponse capture) => Refresh();

        /// <summary>
        /// Where a drawing that isn't a bridge stands: on B's ledge against the gap, at the bridge's depth (the path's
        /// middle), <paramref name="width"/> wide.
        /// <paramref name="away"/> is the way from the gap onto that ledge.
        /// </summary>
        public Vector3 LaunchSpot(Vector3 feet, float width, out float away) =>
            InventionPlacement.LedgeBottomCentre(feet, anchor.position, span.x, width, out away);

        // The bridge stands before its attributes are known; they arrive with the label.
        protected override void OnSpec(InventionSpec spec)
        {
            if (bridge != null) bridge.Apply(spec, enableAbilities: false);
        }

        /// <summary>Manual accept (F1): always rebuilds the bridge from the fallback drawing (a full plank), then solves.</summary>
        public override void ForceSolve()
        {
            if (Solved) return;
            if (fallbackDrawing != null) ShowCutout(PaperTexture.MakePaper(fallbackDrawing), true);
            Solve();
        }

        protected override void OnSolved() => Refresh();

        protected override void OnReset()
        {
            RemoveBridge();
            Refresh();
        }

        void RemoveBridge()
        {
            if (bridge != null) Destroy(bridge.gameObject);
            bridge = null;
        }

        void Refresh()
        {
            if (rails != null) rails.SetActive(bridge != null);
            // The hint sits in the bridge's plane, so it hides once a bridge stands.
            if (drawHint != null) drawHint.SetActive(bridge == null && !Solved);
        }
    }
}
