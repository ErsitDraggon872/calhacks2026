using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    public struct InkResult
    {
        /// <summary>Ink on transparent, row-major from the bottom-left (Texture2D order). Null when empty.</summary>
        public Color32[] Pixels;
        /// <summary>Bottom-left of the cutout inside the board crop, in pixels.</summary>
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int InkPx;
        public bool Empty;
    }

    /// <summary>
    /// Pulls the marker drawing out of a whiteboard photo. Ink is a thin dark line: clearly darker than the brightest
    /// board within about a stroke's width, so wide soft shadows (the drawer's hand) don't count. Dark blobs along the
    /// frame edge and small smudges are dropped, and dark areas enclosed by ink (filled-in shapes) are filled back in.
    /// Pure so it can be unit tested.
    /// </summary>
    public static class InkExtractor
    {
        /// <summary>Background search radius as a fraction of the crop's shorter side: wider than a marker stroke, narrower than a hand shadow.</summary>
        const float StrokeRadius = 1f / 40f;
        const int Margin = 8;
        const int MinBlobPx = 20;
        const float MinBlobFraction = 0.02f;
        /// <summary>Pixels on the crop's edge, as a fraction of its shorter side, that make a blob the board's frame.</summary>
        const float FrameContact = 0.25f;
        /// <summary>A blob this much of the crop's width or height across is the frame or the room, not a drawing.</summary>
        const float FrameSpan = 0.75f;
        /// <summary>Auto threshold: ink is this much darker than the typical board pixel (relative to the board around it).</summary>
        const float InkBelowBoard = 0.22f;
        /// <summary>Auto threshold: never looser than this. Past about 0.75, paper creases and the shadow of a sheet's
        /// edge start counting as ink in real C270 frames.</summary>
        const float MaxThreshold = 0.7f;
        /// <summary>Auto threshold: ink this far below the cut draws fully opaque.</summary>
        const float InkRamp = 0.25f;

        /// <summary>
        /// The board crop in frame pixels. Board space everywhere (preview outline, capture, /interpret) is the upright
        /// frame from BoardCamera.Grab, normalized 0-1 with the origin at the bottom-left, as Texture2D stores it.
        /// </summary>
        public static RectInt BoardRect(int frameW, int frameH, Rect crop)
        {
            var x0 = Mathf.Clamp(Mathf.FloorToInt(crop.xMin * frameW), 0, frameW - 1);
            var y0 = Mathf.Clamp(Mathf.FloorToInt(crop.yMin * frameH), 0, frameH - 1);
            var x1 = Mathf.Clamp(Mathf.CeilToInt(crop.xMax * frameW), x0 + 1, frameW);
            var y1 = Mathf.Clamp(Mathf.CeilToInt(crop.yMax * frameH), y0 + 1, frameH);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        /// <param name="threshold">Ink is darker than this fraction of the board around it. With
        /// <paramref name="autoThreshold"/>, the strictest the cut gets.</param>
        /// <param name="autoThreshold">Loosen the cut (up to <see cref="MaxThreshold"/>) as far as this frame's own
        /// board grain allows, so faint strokes come through.</param>
        public static InkResult Extract(Color32[] frame, int frameW, int frameH, Rect crop, float threshold, int minInkPx, bool autoThreshold = false)
        {
            var board = BoardRect(frameW, frameH, crop);
            var x0 = board.x;
            var y0 = board.y;
            var w = board.width;
            var h = board.height;

            var lum = new byte[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var c = frame[(y + y0) * frameW + x + x0];
                lum[y * w + x] = (byte)((c.r * 54 + c.g * 183 + c.b * 19) >> 8);
            }

            var background = Background(lum, w, h);
            var inkRatio = threshold * 0.5f;
            if (autoThreshold) threshold = AutoThreshold(lum, background, threshold, out inkRatio);

            // Ink strength 0-255: 0 at the threshold, full at the ink's own brightness.
            var alpha = new byte[w * h];
            for (var i = 0; i < lum.Length; i++)
            {
                var ratio = lum[i] / background[i];
                if (ratio < threshold) alpha[i] = (byte)(Mathf.Clamp01((threshold - ratio) / (threshold - inkRatio)) * 255f);
            }

            // Solid ink: pixels with enough inked neighbours, so camera noise doesn't count.
            var solid = new bool[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                solid[y * w + x] = alpha[y * w + x] > 0 && Neighbours(alpha, w, h, x, y) >= 4;
            BridgeGaps(solid, alpha, w, h, Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(w, h) / 360f)));

            var keep = KeepDrawing(solid, w, h, out var inkPx, out var minX, out var minY, out var maxX, out var maxY);
            if (inkPx > 0) inkPx += FillDarkHoles(keep, alpha, lum, background, w, h, threshold, inkRatio);
            if (inkPx < minInkPx) return new InkResult { Empty = true, InkPx = inkPx };

            var cx0 = Mathf.Max(0, minX - Margin);
            var cy0 = Mathf.Max(0, minY - Margin);
            var outW = Mathf.Min(w, maxX + Margin + 1) - cx0;
            var outH = Mathf.Min(h, maxY + Margin + 1) - cy0;
            var pixels = new Color32[outW * outH];
            for (var y = 0; y < outH; y++)
            for (var x = 0; x < outW; x++)
            {
                var sx = x + cx0;
                var sy = y + cy0;
                var a = alpha[sy * w + sx];
                // Soft stroke edges are kept only next to a kept blob, so dropped smudges leave nothing behind.
                if (a == 0 || !NearKept(keep, w, h, sx, sy)) continue;
                var c = frame[(sy + y0) * frameW + sx + x0];
                pixels[y * outW + x] = new Color32(c.r, c.g, c.b, a);
            }

            return new InkResult { Pixels = pixels, X = cx0, Y = cy0, Width = outW, Height = outH, InkPx = inkPx };
        }

        /// <summary>
        /// Splits solid ink into 8-connected blobs and keeps the drawing: blobs that run along the edge of the crop or
        /// span it are the board's frame or the room behind it, a blob touching the edge away from the rest of the
        /// drawing is too, and blobs under 2% of the biggest one are smudges.
        /// </summary>
        static bool[] KeepDrawing(bool[] solid, int w, int h, out int inkPx, out int minX, out int minY, out int maxX, out int maxY)
        {
            var label = new int[w * h];
            var sizes = new List<int> { 0 };
            var edge = new List<bool> { false };
            var frame = new List<bool> { false };
            var bounds = new List<RectInt> { default };
            var stack = new Stack<int>();
            for (var start = 0; start < solid.Length; start++)
            {
                if (!solid[start] || label[start] != 0) continue;
                var id = sizes.Count;
                var size = 0;
                var contact = 0;
                int bx0 = w, by0 = h, bx1 = -1, by1 = -1;
                label[start] = id;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var i = stack.Pop();
                    size++;
                    var x = i % w;
                    var y = i / w;
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) contact++;
                    if (x < bx0) bx0 = x;
                    if (x > bx1) bx1 = x;
                    if (y < by0) by0 = y;
                    if (y > by1) by1 = y;
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        var n = ny * w + nx;
                        if (!solid[n] || label[n] != 0) continue;
                        label[n] = id;
                        stack.Push(n);
                    }
                }
                sizes.Add(size);
                edge.Add(contact > 0);
                bounds.Add(new RectInt(bx0, by0, bx1 - bx0 + 1, by1 - by0 + 1));
                // The board's frame or the room behind it runs along the edge, or stretches across the whole crop.
                frame.Add(contact >= Mathf.Min(w, h) * FrameContact || bx1 - bx0 + 1 >= w * FrameSpan || by1 - by0 + 1 >= h * FrameSpan);
            }

            var largest = 0;
            for (var id = 1; id < sizes.Count; id++)
                if (!frame[id]) largest = Mathf.Max(largest, sizes[id]);
            var minBlob = Mathf.Max(MinBlobPx, Mathf.CeilToInt(largest * MinBlobFraction));
            var kept = new bool[sizes.Count];
            var inside = false;
            var drawing = default(RectInt);
            for (var id = 1; id < sizes.Count; id++)
            {
                if (edge[id] || sizes[id] < minBlob) continue;
                kept[id] = true;
                drawing = inside ? Union(drawing, bounds[id]) : bounds[id];
                inside = true;
            }
            // A blob that only pokes the edge is part of the drawing when it overlaps the rest of it (an outline running
            // off the frame around its eyes), or when it is the whole drawing. Off on its own, it's the frame or the room.
            // Each one joining grows the drawing, so eyes that run off the frame inside that outline come too.
            if (!inside)
            {
                var seed = 0;
                for (var id = 1; id < sizes.Count; id++)
                    if (edge[id] && !frame[id] && sizes[id] >= minBlob && sizes[id] > sizes[seed]) seed = id;
                if (seed != 0)
                {
                    kept[seed] = true;
                    drawing = bounds[seed];
                    inside = true;
                }
            }
            for (var grew = true; grew;)
            {
                grew = false;
                for (var id = 1; id < sizes.Count; id++)
                {
                    if (kept[id] || !edge[id] || frame[id] || sizes[id] < minBlob || !bounds[id].Overlaps(drawing)) continue;
                    kept[id] = true;
                    drawing = Union(drawing, bounds[id]);
                    grew = true;
                }
            }

            var keep = new bool[w * h];
            inkPx = 0;
            minX = w;
            minY = h;
            maxX = -1;
            maxY = -1;
            for (var i = 0; i < keep.Length; i++)
            {
                if (!kept[label[i]]) continue;
                keep[i] = true;
                inkPx++;
                var x = i % w;
                var y = i / w;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            return keep;
        }

        /// <summary>
        /// The ink cut for this frame: a fixed step below the typical (median) board pixel, each relative to the board
        /// around it. A dim, grainy frame sits lower, so its faint strokes still fall under the cut; it's never stricter
        /// than <paramref name="fallback"/>. <paramref name="inkRatio"/> is where ink draws fully opaque.
        /// </summary>
        static float AutoThreshold(byte[] lum, float[] background, float fallback, out float inkRatio)
        {
            const int Buckets = 256;
            var histogram = new int[Buckets];
            for (var i = 0; i < lum.Length; i++) histogram[Mathf.Min(Buckets - 1, (int)(lum[i] / background[i] * Buckets))]++;
            var median = 0;
            for (var seen = 0; median < Buckets - 1; median++)
            {
                seen += histogram[median];
                if (seen * 2 >= lum.Length) break;
            }
            var cut = Mathf.Clamp((median + 0.5f) / Buckets - InkBelowBoard, fallback, MaxThreshold);

            // Full strength at the faint ink just under the cut, not at the darkest thing in the frame.
            double sum = 0;
            var count = 0;
            for (var i = 0; i < lum.Length; i++)
            {
                var ratio = lum[i] / background[i];
                if (ratio >= cut || ratio < cut - InkRamp) continue;
                sum += ratio;
                count++;
            }
            inkRatio = Mathf.Min(count > 0 ? (float)(sum / count) : cut - InkRamp, cut - 0.02f);
            return cut;
        }

        /// <summary>
        /// A faint pen skips: a non-ink pixel with ink on both sides within <paramref name="gap"/> pixels, along a row,
        /// column or diagonal, is ink too, as strong as the ink it bridges. A stroke's own corners and edges have ink
        /// on one side only, so they stay as they are.
        /// </summary>
        static void BridgeGaps(bool[] solid, byte[] alpha, int w, int h, int gap)
        {
            var bridged = new List<int>();
            var strength = new List<byte>();
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (solid[y * w + x]) continue;
                for (var axis = 0; axis < 4; axis++)
                {
                    var dx = axis == 1 ? 0 : 1;
                    var dy = axis == 0 ? 0 : axis == 3 ? -1 : 1;
                    var a = Reach(x, y, dx, dy);
                    if (a < 0) continue;
                    var b = Reach(x, y, -dx, -dy);
                    if (b < 0) continue;
                    bridged.Add(y * w + x);
                    strength.Add((byte)((alpha[a] + alpha[b]) / 2));
                    break;
                }
            }
            for (var i = 0; i < bridged.Count; i++)
            {
                solid[bridged[i]] = true;
                alpha[bridged[i]] = (byte)Mathf.Max(alpha[bridged[i]], strength[i]);
            }

            int Reach(int x, int y, int dx, int dy)
            {
                for (var step = 1; step <= gap; step++)
                {
                    var nx = x + dx * step;
                    var ny = y + dy * step;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) return -1;
                    if (solid[ny * w + nx]) return ny * w + nx;
                }
                return -1;
            }
        }

        static RectInt Union(RectInt a, RectInt b)
        {
            var x0 = Mathf.Min(a.xMin, b.xMin);
            var y0 = Mathf.Min(a.yMin, b.yMin);
            return new RectInt(x0, y0, Mathf.Max(a.xMax, b.xMax) - x0, Mathf.Max(a.yMax, b.yMax) - y0);
        }

        static bool NearKept(bool[] keep, int w, int h, int x, int y)
        {
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < h && keep[ny * w + nx]) return true;
            }
            return false;
        }

        static int Neighbours(byte[] alpha, int w, int h, int x, int y)
        {
            var count = 0;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = x + dx;
                var ny = y + dy;
                if ((dx != 0 || dy != 0) && nx >= 0 && ny >= 0 && nx < w && ny < h && alpha[ny * w + nx] > 0) count++;
            }
            return count;
        }

        /// <summary>
        /// A region the ink completely encloses (not touching the crop edge) is part of the drawing when it is itself
        /// dark, closer to the ink than to the board: a filled-in shape. The inside of an outline stays empty.
        /// Returns how many pixels were added.
        /// </summary>
        static int FillDarkHoles(bool[] keep, byte[] alpha, byte[] lum, float[] background, int w, int h, float threshold, float inkRatio)
        {
            double inkSum = 0, boardSum = 0;
            var inkCount = 0;
            for (var i = 0; i < keep.Length; i++)
            {
                if (!keep[i]) continue;
                inkSum += lum[i];
                boardSum += background[i];
                inkCount++;
            }
            var cut = (float)(inkSum + boardSum) / (2f * inkCount);
            var board = (float)(boardSum / inkCount);

            var seen = new bool[keep.Length];
            var region = new List<int>();
            var stack = new Stack<int>();
            var added = 0;
            for (var start = 0; start < keep.Length; start++)
            {
                if (keep[start] || seen[start]) continue;
                region.Clear();
                var edge = false;
                long sum = 0;
                seen[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var i = stack.Pop();
                    region.Add(i);
                    sum += lum[i];
                    var x = i % w;
                    var y = i / w;
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) edge = true;
                    if (x > 0) Visit(i - 1);
                    if (x < w - 1) Visit(i + 1);
                    if (y > 0) Visit(i - w);
                    if (y < h - 1) Visit(i + w);
                }
                if (edge || (float)sum / region.Count >= cut) continue;
                foreach (var i in region)
                {
                    keep[i] = true;
                    alpha[i] = (byte)(Mathf.Clamp01((threshold - lum[i] / board) / (threshold - inkRatio)) * 255f);
                }
                added += region.Count;
            }
            return added;

            void Visit(int n)
            {
                if (keep[n] || seen[n]) return;
                seen[n] = true;
                stack.Push(n);
            }
        }

        /// <summary>
        /// Local board brightness per pixel: the brightest pixel within the stroke radius (a separable max filter).
        /// Inside a stroke that reaches the clean board beside it; inside a wide shadow it stays shadow-dark.
        /// </summary>
        static float[] Background(byte[] lum, int w, int h)
        {
            var r = Mathf.Max(4, Mathf.RoundToInt(Mathf.Min(w, h) * StrokeRadius));
            var horizontal = new byte[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                byte max = 0;
                for (var k = Mathf.Max(0, x - r); k <= Mathf.Min(w - 1, x + r); k++)
                    if (lum[y * w + k] > max) max = lum[y * w + k];
                horizontal[y * w + x] = max;
            }
            var result = new float[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                byte max = 1;
                for (var k = Mathf.Max(0, y - r); k <= Mathf.Min(h - 1, y + r); k++)
                    if (horizontal[k * w + x] > max) max = horizontal[k * w + x];
                result[y * w + x] = max;
            }
            return result;
        }
    }
}
