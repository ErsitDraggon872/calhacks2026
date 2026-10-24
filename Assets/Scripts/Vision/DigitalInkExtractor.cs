using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Crops black pixels from a white digital canvas. Unlike webcam extraction this is deterministic:
    /// no lighting model, board calibration, edge rejection or blob filtering is needed.
    /// Pixel order is bottom-to-top, matching Texture2D.GetPixels32 and the /interpret contract.
    /// </summary>
    public static class DigitalInkExtractor
    {
        public static InkResult Extract(Color32[] board, int width, int height, int minInkPx = 1, int margin = 8)
        {
            if (board == null || width <= 0 || height <= 0 || board.Length < (long)width * height)
                return new InkResult { Empty = true };

            var x0 = width;
            var y0 = height;
            var x1 = -1;
            var y1 = -1;
            var count = 0;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var p = board[y * width + x];
                if (p.r >= 128 || p.g >= 128 || p.b >= 128) continue;
                count++;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
            }

            if (count < Mathf.Max(1, minInkPx)) return new InkResult { Empty = true, InkPx = count };
            margin = Mathf.Max(0, margin);
            x0 = Mathf.Max(0, x0 - margin);
            y0 = Mathf.Max(0, y0 - margin);
            x1 = Mathf.Min(width - 1, x1 + margin);
            y1 = Mathf.Min(height - 1, y1 + margin);
            var outW = x1 - x0 + 1;
            var outH = y1 - y0 + 1;
            var pixels = new Color32[outW * outH];
            for (var y = 0; y < outH; y++)
            for (var x = 0; x < outW; x++)
            {
                var p = board[(y + y0) * width + x + x0];
                if (p.r < 128 && p.g < 128 && p.b < 128)
                    pixels[y * outW + x] = new Color32(0, 0, 0, 255);
                // Background remains transparent.
            }
            return new InkResult
            {
                Pixels = pixels, X = x0, Y = y0, Width = outW, Height = outH, InkPx = count
            };
        }
    }
}
