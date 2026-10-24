using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// The browser whiteboard's strokes, kept per capture_id and drawn onto a clean board: black ink on white, origin
    /// bottom-left, the same 16:9 canvas and pen width as tools/whiteboard/index.html. Plain C# (no SpacetimeDB types),
    /// so the board a BUILD sees can be tested without a connection.
    ///
    /// The canvas sits inside a white <see cref="Border"/>: InkExtractor drops dark blobs touching the frame edge (a
    /// webcam's board frame), and a drawing that runs to the page's edge must still count.
    /// </summary>
    public class StrokeBoard
    {
        /// <summary>The page's canvas is 960x540 with an 8 px pen.</summary>
        public const int CanvasWidth = 960;
        public const int CanvasHeight = 540;
        public const int PenRadius = 4;
        public const int Border = 16;
        public const int Width = CanvasWidth + 2 * Border;
        public const int Height = CanvasHeight + 2 * Border;

        readonly Dictionary<string, List<float[]>> captures = new Dictionary<string, List<float[]>>();

        /// <summary>The capture that most recently got a stroke, or null.</summary>
        public string Latest { get; private set; }

        public bool Has(string captureId) => captureId != null && captures.ContainsKey(captureId);

        /// <summary>Adds one stroke batch: flattened x0,y0,x1,y1,... in 0-1 board coordinates.</summary>
        public void Add(string captureId, IReadOnlyList<float> points)
        {
            if (string.IsNullOrEmpty(captureId) || points == null || points.Count < 4) return;
            if (!captures.TryGetValue(captureId, out var strokes)) captures[captureId] = strokes = new List<float[]>();
            var copy = new float[points.Count];
            for (var i = 0; i < copy.Length; i++) copy[i] = points[i];
            strokes.Add(copy);
            Latest = captureId;
        }

        public void Forget(string captureId)
        {
            if (captureId == null) return;
            captures.Remove(captureId);
            if (Latest == captureId) Latest = null;
        }

        /// <summary>Drops every capture but <paramref name="keep"/> (null keeps none).</summary>
        public void ForgetAllBut(string keep)
        {
            var kept = keep != null && captures.TryGetValue(keep, out var strokes) ? strokes : null;
            captures.Clear();
            if (kept != null) captures[keep] = kept;
            Latest = kept != null ? keep : null;
        }

        public void Clear()
        {
            captures.Clear();
            Latest = null;
        }

        /// <summary>The board for <paramref name="captureId"/>; blank (all white) if it has no strokes.</summary>
        public Color32[] Render(string captureId, out int width, out int height)
        {
            width = Width;
            height = Height;
            var raster = new StrokeRaster(Width, Height, Color.white, Color.black, PenRadius);
            try
            {
                if (captureId != null && captures.TryGetValue(captureId, out var strokes))
                    foreach (var s in strokes) raster.Draw(ToBoard(s));
                return raster.CopyPixels();
            }
            finally
            {
                raster.Dispose();
            }
        }

        /// <summary>Canvas coordinates (0-1) to board coordinates (0-1), inside the border.</summary>
        static float[] ToBoard(float[] canvas)
        {
            var board = new float[canvas.Length];
            for (var i = 0; i + 1 < canvas.Length; i += 2)
            {
                board[i] = (Border + canvas[i] * (CanvasWidth - 1)) / (Width - 1);
                board[i + 1] = (Border + canvas[i + 1] * (CanvasHeight - 1)) / (Height - 1);
            }
            return board;
        }

        /// <summary>
        /// A board with <paramref name="cutout"/> (ink on transparent, as BUILD's PNG carries it) in the middle, for a
        /// capture whose strokes never arrived. Scaled down to fit if it's bigger than the board.
        /// </summary>
        public static Color32[] RenderCutout(Texture2D cutout, out int width, out int height)
        {
            width = Width;
            height = Height;
            var board = new Color32[Width * Height];
            for (var i = 0; i < board.Length; i++) board[i] = new Color32(255, 255, 255, 255);
            if (cutout == null) return board;
            var scale = Mathf.Min(1f, Mathf.Min((float)CanvasWidth / cutout.width, (float)CanvasHeight / cutout.height));
            var w = Mathf.Max(1, Mathf.FloorToInt(cutout.width * scale));
            var h = Mathf.Max(1, Mathf.FloorToInt(cutout.height * scale));
            var x0 = (Width - w) / 2;
            var y0 = (Height - h) / 2;
            var src = cutout.GetPixels32();
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var p = src[Mathf.Min(cutout.height - 1, (int)(y / scale)) * cutout.width + Mathf.Min(cutout.width - 1, (int)(x / scale))];
                if (p.a >= 128) board[(y0 + y) * Width + x0 + x] = new Color32(0, 0, 0, 255);
            }
            return board;
        }
    }
}
