using System;
using UnityEngine;

namespace CalHacks
{
    /// <summary>Turns a marker-on-transparent crop into a paper cutout: ink on a white shape with a border.</summary>
    public static class PaperTexture
    {
        static readonly Color32 Paper = new Color32(250, 246, 236, 255);

        public static Texture2D FromBase64(string pngBase64)
        {
            if (string.IsNullOrEmpty(pngBase64)) return null;
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(pngBase64);
            }
            catch (FormatException)
            {
                return null;
            }
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!source.LoadImage(bytes))
            {
                DestroySafe(source);
                return null;
            }
            var paper = MakePaper(source);
            DestroySafe(source);
            return paper;
        }

        static void DestroySafe(UnityEngine.Object obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        public static Texture2D MakePaper(Texture2D source)
        {
            var w = source.width;
            var h = source.height;
            var border = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(w, h) * 0.035f), 3, 24);
            var outW = w + border * 2;
            var outH = h + border * 2;

            var src = source.GetPixels32();
            var ink = new byte[outW * outH];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                ink[(y + border) * outW + x + border] = src[y * w + x].a;

            // Paper shape: ink alpha dilated by `border` pixels (separable box dilation).
            var horiz = Dilate(ink, outW, outH, border, true);
            var shape = Dilate(horiz, outW, outH, border, false);

            var pixels = new Color32[outW * outH];
            for (var y = 0; y < outH; y++)
            for (var x = 0; x < outW; x++)
            {
                var i = y * outW + x;
                var sx = x - border;
                var sy = y - border;
                Color32 inkColor = default;
                if (sx >= 0 && sy >= 0 && sx < w && sy < h) inkColor = src[sy * w + sx];
                var a = inkColor.a / 255f;
                var paperAlpha = shape[i] > 40 ? (byte)255 : (byte)0;
                pixels[i] = new Color32(
                    (byte)Mathf.Lerp(Paper.r, inkColor.r, a),
                    (byte)Mathf.Lerp(Paper.g, inkColor.g, a),
                    (byte)Mathf.Lerp(Paper.b, inkColor.b, a),
                    paperAlpha);
            }

            var result = new Texture2D(outW, outH, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "Cutout",
            };
            result.SetPixels32(pixels);
            result.Apply(true, false);
            return result;
        }

        static byte[] Dilate(byte[] input, int w, int h, int radius, bool horizontal)
        {
            var output = new byte[input.Length];
            var lines = horizontal ? h : w;
            var length = horizontal ? w : h;
            for (var line = 0; line < lines; line++)
            {
                // Distance to the nearest inked cell along the line, forward then backward.
                var lastInk = int.MinValue / 2;
                for (var p = 0; p < length; p++)
                {
                    var i = horizontal ? line * w + p : p * w + line;
                    if (input[i] > 40) lastInk = p;
                    if (p - lastInk <= radius) output[i] = 255;
                }
                lastInk = int.MaxValue / 2;
                for (var p = length - 1; p >= 0; p--)
                {
                    var i = horizontal ? line * w + p : p * w + line;
                    if (input[i] > 40) lastInk = p;
                    if (lastInk - p <= radius) output[i] = 255;
                }
            }
            return output;
        }
    }
}
