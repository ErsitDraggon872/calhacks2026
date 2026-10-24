using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CalHacks.Tests
{
    public class SilhouetteBoxesTests
    {
        const float Cell = 0.15f;

        static byte[] Alpha(int w, int h, System.Func<int, int, bool> solid)
        {
            var alpha = new byte[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                alpha[y * w + x] = solid(x, y) ? (byte)255 : (byte)0;
            return alpha;
        }

        static bool Covers(List<Rect> boxes, Vector2 point)
        {
            foreach (var b in boxes)
            {
                if (b.Contains(point)) return true;
            }
            return false;
        }

        static float Area(List<Rect> boxes)
        {
            var area = 0f;
            foreach (var b in boxes) area += b.width * b.height;
            return area;
        }

        [Test]
        public void FilledRectangleIsOneBoxOfFullSize()
        {
            var size = new Vector2(3f, 1.5f);
            var boxes = SilhouetteBoxes.Build(Alpha(60, 30, (x, y) => true), 60, 30, size, Cell);
            Assert.AreEqual(1, boxes.Count);
            Assert.AreEqual(-1.5f, boxes[0].xMin, 1e-4f);
            Assert.AreEqual(1.5f, boxes[0].xMax, 1e-4f);
            Assert.AreEqual(0f, boxes[0].yMin, 1e-4f);
            Assert.AreEqual(1.5f, boxes[0].yMax, 1e-4f);
        }

        [Test]
        public void HollowOutlineHasNoBoxInTheMiddle()
        {
            // 100 x 100 px drawn 3 x 3 m: a 5 px (0.15 m) frame around an empty middle.
            var alpha = Alpha(100, 100, (x, y) => x < 5 || y < 5 || x >= 95 || y >= 95);
            var boxes = SilhouetteBoxes.Build(alpha, 100, 100, new Vector2(3f, 3f), Cell);
            Assert.IsFalse(Covers(boxes, new Vector2(0f, 1.5f)), "middle must be open");
            Assert.IsTrue(Covers(boxes, new Vector2(0f, 2.95f)), "top edge is solid");
            Assert.IsTrue(Covers(boxes, new Vector2(0f, 0.05f)), "bottom edge is solid");
            Assert.IsTrue(Covers(boxes, new Vector2(-1.45f, 1.5f)), "left edge is solid");
            Assert.IsTrue(Covers(boxes, new Vector2(1.45f, 1.5f)), "right edge is solid");
            Assert.Less(Area(boxes), 3f * 3f * 0.5f);
        }

        [Test]
        public void FlatTopIsPixelExactNotRoundedUpToACell()
        {
            // 100 x 100 px drawn 3 x 3 m, solid up to row 37: the top is at 1.11 m, not the next cell edge (1.2 m).
            var alpha = Alpha(100, 100, (x, y) => y < 37);
            var boxes = SilhouetteBoxes.Build(alpha, 100, 100, new Vector2(3f, 3f), Cell);
            Assert.AreEqual(1, boxes.Count);
            Assert.AreEqual(1.11f, boxes[0].yMax, 1e-4f);
        }

        [Test]
        public void DiagonalLineGivesStairSteps()
        {
            // A thick diagonal from bottom-left to top-right, 200 px across 3 x 3 m (0.015 m a pixel).
            var alpha = Alpha(200, 200, (x, y) => Mathf.Abs(x - y) < 6);
            var boxes = SilhouetteBoxes.Build(alpha, 200, 200, new Vector2(3f, 3f), Cell);
            Assert.Greater(boxes.Count, 10);
            // No step stands above the line at its right edge (the line's top is 5 px above y = x), and the higher a
            // box is, the further right.
            boxes.Sort((a, b) => a.yMin.CompareTo(b.yMin));
            for (var i = 0; i < boxes.Count; i++)
            {
                Assert.LessOrEqual(boxes[i].yMax, boxes[i].xMax + 1.5f + 5f * 0.015f + 1e-4f);
                if (i > 0) Assert.GreaterOrEqual(boxes[i].center.x, boxes[i - 1].center.x - 1e-4f);
            }
            Assert.Less(boxes[0].center.x, 0f);
            Assert.Greater(boxes[boxes.Count - 1].center.x, 0f);
            Assert.IsFalse(Covers(boxes, new Vector2(1.2f, 0.3f)), "below the diagonal is open");
        }

        // The gap stretches every drawing to its 4.9 m span (GapSocket), whatever its aspect.
        static readonly Vector2 GapBridge = new Vector2(4.9f, 1f);

        [Test]
        public void StretchedBridgeWithABreakHasNoBoxOverTheMiddle()
        {
            // 200 x 100 px drawn 4.9 x 1 m: a blob at each end (0.49 m each) and nothing between them.
            var alpha = Alpha(200, 100, (x, y) => x < 20 || x >= 180);
            var boxes = SilhouetteBoxes.Build(alpha, 200, 100, GapBridge, Cell);
            Assert.IsTrue(Covers(boxes, new Vector2(-2.3f, 0.5f)), "near end is solid");
            Assert.IsTrue(Covers(boxes, new Vector2(2.3f, 0.5f)), "far end is solid");
            foreach (var b in boxes)
            {
                Assert.IsTrue(b.xMax < -1.5f || b.xMin > 1.5f, $"box {b} reaches over the middle");
            }
        }

        [Test]
        public void StretchedFullBarCoversTheWholeSpan()
        {
            var alpha = Alpha(200, 100, (x, y) => true);
            var boxes = SilhouetteBoxes.Build(alpha, 200, 100, GapBridge, Cell);
            for (var x = -2.4f; x <= 2.4f; x += 0.1f)
            {
                Assert.IsTrue(Covers(boxes, new Vector2(x, 0.95f)), $"top is open at x {x:0.0}");
            }
            Assert.AreEqual(GapBridge.x * GapBridge.y, Area(boxes), 1e-3f);
        }

        [Test]
        public void EmptyAlphaGivesNoBoxes()
        {
            Assert.AreEqual(0, SilhouetteBoxes.Build(new byte[64 * 64], 64, 64, new Vector2(3f, 3f), Cell).Count);
            Assert.AreEqual(0, SilhouetteBoxes.Build((byte[])null, 0, 0, new Vector2(3f, 3f), Cell).Count);
        }

        [Test]
        public void FaintAlphaIsNotSolid()
        {
            var alpha = new byte[32 * 32];
            for (var i = 0; i < alpha.Length; i++) alpha[i] = SilhouetteBoxes.SolidAlpha - 1;
            Assert.AreEqual(0, SilhouetteBoxes.Build(alpha, 32, 32, new Vector2(1f, 1f), Cell).Count);
        }

        [Test]
        public void Color32OverloadUsesAlpha()
        {
            var pixels = new Color32[20 * 20];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, 255);
            var boxes = SilhouetteBoxes.Build(pixels, 20, 20, new Vector2(1.5f, 1.5f), Cell);
            Assert.AreEqual(1, boxes.Count);
            Assert.AreEqual(1.5f * 1.5f, Area(boxes), 1e-4f);
        }
    }
}
