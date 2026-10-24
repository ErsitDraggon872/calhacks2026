using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Tutorial gap: any non-empty drawing becomes the bridge on /capture, without waiting for the label, so the
    /// first build never fails. The drawing is stretched to span the gap (Span.x), its top edge at deck level, and the
    /// hidden deck collider is what B walks on. Solving it reveals the world (the camera's Wide framing).
    /// </summary>
    public class GapSocket : PuzzleSocket
    {
        [Tooltip("Invisible side rails enabled with the deck so the explorer can't walk off the bridge's edges.")]
        [SerializeField] GameObject rails;
        [Tooltip("Dashed \"draw here\" outline shown until the gap is solved.")]
        [SerializeField] GameObject drawHint;
        [Tooltip("How far the top of the drawing sits above the deck, so drawn railings stick up.")]
        [SerializeField] float railHeight = 0.4f;
        [Tooltip("Shortest the drawing is squashed to, however wide it is.")]
        [SerializeField] float minHeight = 1f;

        public override SocketType Type => SocketType.Gap;

        protected override void Awake()
        {
            base.Awake();
            ShowSolved(false);
        }

        protected override Cutout CreateCutout(Texture2D texture, bool ownsTexture)
        {
            var aspect = texture.height > 0 ? (float)texture.width / texture.height : 1f;
            var size = new Vector2(span.x, Mathf.Clamp(span.x / aspect, minHeight, span.y));
            var bottom = anchor.position + Vector3.up * (railHeight - size.y);
            return Cutout.Create(texture, ownsTexture, cutoutMaterial, bottom, anchor.eulerAngles.y, size, transform);
        }

        protected override void OnCaptured(CaptureResponse capture)
        {
            if (!capture.empty_board) Solve();
        }

        protected override void OnSolved()
        {
            ShowSolved(true);
            var follow = FindFirstObjectByType<CameraFollow>();
            if (follow != null) follow.Reveal();
        }

        protected override void OnReset() => ShowSolved(false);

        void ShowSolved(bool solved)
        {
            if (rails != null) rails.SetActive(solved);
            if (drawHint != null) drawHint.SetActive(!solved);
        }
    }
}
