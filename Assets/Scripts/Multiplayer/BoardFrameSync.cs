using System;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Physical whiteboard, live: a few times a second the Creator's webcam board crop is shrunk, its ink extracted
    /// (the same InkExtractor the BUILD uses) and sent as a small black-on-white PNG, but only when the ink changed.
    /// It's the whiteboard's counterpart to <see cref="StrokeSync"/>; the Explorer's preview shows whichever arrives.
    /// Runs only for the Creator, and only when <see cref="CreatorBuild.UsesWebcam"/>.
    /// </summary>
    public class BoardFrameSync : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] float interval = 0.35f;
        [Tooltip("Width of the preview sent to the Explorer, in pixels. The full-resolution cut-out is only sent at BUILD.")]
        [SerializeField] int previewWidth = 240;

        BoardCamera boardCamera;
        float nextSend;
        ulong lastHash;
        Texture2D encoder;

        void Awake()
        {
            if (config == null) config = Resources.Load<GameConfig>("GameConfig");
            if (config == null) config = ScriptableObject.CreateInstance<GameConfig>();
        }

        void Update()
        {
            var session = ThinkInkSession.Instance;
            var build = CreatorBuild.Instance;
            if (session == null || build == null || session.Role != CoopRole.Creator || !session.RoomReady || !build.UsesWebcam) return;
            if (Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + interval;

            if (boardCamera == null) boardCamera = BoardCamera.Instance != null ? BoardCamera.Instance : FindFirstObjectByType<BoardCamera>();
            if (boardCamera == null) return;
            var frame = boardCamera.Grab(out var w, out var h);
            if (frame == null) return;

            var board = InkExtractor.BoardRect(w, h, config.boardCrop);
            var sw = Mathf.Min(previewWidth, board.width);
            var sh = Mathf.Max(1, Mathf.RoundToInt(sw * (float)board.height / board.width));
            var small = Shrink(frame, w, board, sw, sh);
            var minInk = Mathf.Max(4, Mathf.RoundToInt(config.minInkPx * (float)(sw * sh) / (board.width * board.height)));
            var ink = InkExtractor.Extract(small, sw, sh, new Rect(0f, 0f, 1f, 1f), config.inkThreshold, minInk);

            // Black ink on white, in place on the board (the extractor returns the ink's bounding box).
            var preview = new Color32[sw * sh];
            for (var i = 0; i < preview.Length; i++) preview[i] = new Color32(255, 255, 255, 255);
            var hash = 1469598103934665603UL;
            if (!ink.Empty)
            {
                for (var y = 0; y < ink.Height; y++)
                for (var x = 0; x < ink.Width; x++)
                {
                    var a = ink.Pixels[y * ink.Width + x].a;
                    if (a < 96) continue;
                    var i = (y + ink.Y) * sw + x + ink.X;
                    preview[i] = new Color32(0, 0, 0, 255);
                    hash = (hash ^ (ulong)i) * 1099511628211UL;
                }
            }
            if (hash == lastHash) return;
            lastHash = hash;

            if (encoder == null || encoder.width != sw || encoder.height != sh)
            {
                if (encoder != null) Destroy(encoder);
                encoder = new Texture2D(sw, sh, TextureFormat.RGBA32, false);
            }
            encoder.SetPixels32(preview);
            encoder.Apply(false);
            session.PublishBoardFrame(build.CaptureId, Convert.ToBase64String(encoder.EncodeToPNG()), sw, sh);
        }

        /// <summary>Box-averages the board crop of <paramref name="frame"/> down to <paramref name="sw"/> x <paramref name="sh"/>.</summary>
        static Color32[] Shrink(Color32[] frame, int frameW, RectInt board, int sw, int sh)
        {
            var result = new Color32[sw * sh];
            for (var y = 0; y < sh; y++)
            {
                var y0 = board.y + y * board.height / sh;
                var y1 = Mathf.Max(y0 + 1, board.y + (y + 1) * board.height / sh);
                for (var x = 0; x < sw; x++)
                {
                    var x0 = board.x + x * board.width / sw;
                    var x1 = Mathf.Max(x0 + 1, board.x + (x + 1) * board.width / sw);
                    int r = 0, g = 0, b = 0, n = 0;
                    for (var yy = y0; yy < y1; yy++)
                    for (var xx = x0; xx < x1; xx++)
                    {
                        var c = frame[yy * frameW + xx];
                        r += c.r;
                        g += c.g;
                        b += c.b;
                        n++;
                    }
                    result[y * sw + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                }
            }
            return result;
        }

        void OnDestroy()
        {
            if (encoder != null) Destroy(encoder);
        }
    }
}
