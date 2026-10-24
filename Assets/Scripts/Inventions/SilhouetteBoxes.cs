using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Rebuilds a cutout's silhouette as a few boxes ("flat to look at, thick to stand on"). The alpha is cut into
    /// cells of about <c>cell</c> metres; a cell is solid if any pixel in it has alpha of at least <see cref="SolidAlpha"/>.
    /// Each row's solid cells merge into horizontal runs, and identical runs in neighbouring rows merge into one box.
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
            var rows = Mathf.Max(1, Mathf.RoundToInt(size.y / cell));
            var cellW = size.x / cols;
            var cellH = size.y / rows;
            var solid = Downsample(alpha, width, height, cols, rows);

            // Runs still growing upward, keyed by (first column, last column).
            var open = new Dictionary<(int, int), Rect>();
            for (var row = 0; row < rows; row++)
            {
                var next = new Dictionary<(int, int), Rect>();
                var col = 0;
                while (col < cols)
                {
                    if (!solid[row * cols + col])
                    {
                        col++;
                        continue;
                    }
                    var first = col;
                    while (col < cols && solid[row * cols + col]) col++;
                    var key = (first, col - 1);
                    if (open.TryGetValue(key, out var below))
                    {
                        below.height += cellH;
                        next[key] = below;
                        open.Remove(key);
                    }
                    else
                    {
                        next[key] = new Rect(-size.x * 0.5f + first * cellW, row * cellH, (col - first) * cellW, cellH);
                    }
                }
                boxes.AddRange(open.Values);
                open = next;
            }
            boxes.AddRange(open.Values);
            return boxes;
        }

        static bool[] Downsample(byte[] alpha, int width, int height, int cols, int rows)
        {
            var solid = new bool[cols * rows];
            for (var row = 0; row < rows; row++)
            {
                var y0 = row * height / rows;
                var y1 = Mathf.Max(y0 + 1, (row + 1) * height / rows);
                for (var col = 0; col < cols; col++)
                {
                    var x0 = col * width / cols;
                    var x1 = Mathf.Max(x0 + 1, (col + 1) * width / cols);
                    solid[row * cols + col] = AnySolid(alpha, width, height, x0, x1, y0, y1);
                }
            }
            return solid;
        }

        static bool AnySolid(byte[] alpha, int width, int height, int x0, int x1, int y0, int y1)
        {
            for (var y = y0; y < y1 && y < height; y++)
            for (var x = x0; x < x1 && x < width; x++)
            {
                if (alpha[y * width + x] >= SolidAlpha) return true;
            }
            return false;
        }
    }
}
