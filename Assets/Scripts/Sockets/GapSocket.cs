using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Tutorial gap: any non-empty drawing becomes the bridge on /capture, without waiting for the label.
    /// The drawing is stretched to span the gap (Span.x), its top edge at deck level, on both sides of the walkway.
    /// </summary>
    public class GapSocket : PuzzleSocket
    {
        [Tooltip("Invisible side rails enabled with the deck so the explorer can't walk off the bridge's edges.")]
        [SerializeField] GameObject rails;
        [Tooltip("Paper walkway shown with the deck.")]
        [SerializeField] GameObject deckVisual;
        [Tooltip("How far the top of the drawing sits above the deck, so drawn railings stick up.")]
        [SerializeField] float railHeight = 0.4f;
        [Tooltip("Shortest the drawing is squashed to, however wide it is.")]
        [SerializeField] float minHeight = 1f;
        [Tooltip("Distance from the far face (at the anchor) to the near face of the bridge.")]
        [SerializeField] float bridgeWidth = 3.2f;

        public override SocketType Type => SocketType.Gap;

        protected override void Awake()
        {
            base.Awake();
            if (rails != null) rails.SetActive(false);
            if (deckVisual != null) deckVisual.SetActive(false);
        }

        protected override Cutout CreateCutout(Texture2D texture, bool ownsTexture)
        {
            var aspect = texture.height > 0 ? (float)texture.width / texture.height : 1f;
            var size = new Vector2(span.x, Mathf.Clamp(span.x / aspect, minHeight, span.y));
            var bottom = anchor.position + Vector3.up * (railHeight - size.y);
            var cutout = Cutout.Create(texture, ownsTexture, cutoutMaterial, bottom, anchor.eulerAngles.y, size, transform);
            cutout.AddParallelFace(-bridgeWidth);
            return cutout;
        }

        protected override void OnCaptured(CaptureResponse capture)
        {
            if (!capture.empty_board) Solve();
        }

        protected override void OnSolved()
        {
            if (rails != null) rails.SetActive(true);
            if (deckVisual != null) deckVisual.SetActive(true);
        }

        protected override void OnReset()
        {
            if (rails != null) rails.SetActive(false);
            if (deckVisual != null) deckVisual.SetActive(false);
        }
    }
}
