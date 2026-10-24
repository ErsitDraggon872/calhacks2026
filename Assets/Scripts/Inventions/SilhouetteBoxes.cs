using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Rebuilds a cutout's silhouette as a few boxes ("flat to look at, thick to stand on"). The alpha is cut into
    /// columns about <c>cell</c> metres wide. In each column, every run of pixel rows holding a pixel with alpha of at
    /// least <see cref="SolidAlpha"/> becomes a box, so tops and bottoms are exact to the pixel and B stands on the
    /// drawn edge, not above it. Identical runs in neighbouring columns merge into one box.
    /// Pure: no Unity objects, so EditMode tests cover it.
    /// </summary>
    public static class SilhouetteBoxes
    {
        public const byte SolidAlpha = 128;

        /// <summary>Boxes from a texture's pixels (row-major, bottom row first, as <c>Texture2D.GetPixels32</c> returns them).</summary>
        public static List<Rect> Build(Color32[] pixels, int width, int height, Vector2 size, float cell)
        {
            if (pixels == null) return new List<Rect>();
            var alpha = new byte[pixels.Length];
            for (var i = 0; i < pixels.Length; i++) alpha[i] = pixels[i].a;
            return Build(alpha, width, height, size, cell);
        }

        /// <summary>
        /// Boxes as local rects in metres for a cutout of <paramref name="size"/>: origin at the cutout's bottom-centre,
        /// x from -size.x/2 to +size.x/2, y from 0 to size.y. <paramref name="alpha"/> is row-major, bottom row first.
        /// </summary>
        public static List<Rect> Build(byte[] alpha, int width, int height, Vector2 size, float cell)
        {
            var boxes = new List<Rect>();
            if (alpha == null || width <= 0 || height <= 0 || alpha.Length < width * height) return boxes;
            if (size.x <= 0f || size.y <= 0f || cell <= 0f) return boxes;

            var cols = Mathf.Max(1, Mathf.RoundToInt(size.x / cell));
            var colW = size.x / cols;
            var rowH = size.y / height;

            // Runs still growing sideways, keyed by (first row, last row).
            var open = new Dictionary<(int, int), Rect>();
            for (var col = 0; col < cols; col++)
            {
                var x0 = col * width / cols;
                var x1 = Mathf.Max(x0 + 1, (col + 1) * width / cols);
                var next = new Dictionary<(int, int), Rect>();
                var row = 0;
                while (row < height)
                {
                    if (!AnySolid(alpha, width, x0, x1, row))
                    {
                        row++;
                        continue;
                    }
                    var first = row;
                    while (row < height && AnySolid(alpha, width, x0, x1, row)) row++;
                    var key = (first, row - 1);
                    if (open.TryGetValue(key, out var left))
                    {
                        left.width += colW;
                        next[key] = left;
                        open.Remove(key);
                    }
                    else
                    {
                        next[key] = new Rect(-size.x * 0.5f + col * colW, first * rowH, colW, (row - first) * rowH);
                    }
                }
                boxes.AddRange(open.Values);
                open = next;
            }
            boxes.AddRange(open.Values);
            return boxes;
        }

        static bool AnySolid(byte[] alpha, int width, int x0, int x1, int row)
        {
            for (var x = x0; x < x1 && x < width; x++)
            {
                if (alpha[row * width + x] >= SolidAlpha) return true;
            }
            return false;
        }
    }
}
