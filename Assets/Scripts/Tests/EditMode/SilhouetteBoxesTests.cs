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
        public void DiagonalLineGivesStairSteps()
        {
            // A thick diagonal from bottom-left to top-right, 200 px across 3 x 3 m.
            var alpha = Alpha(200, 200, (x, y) => Mathf.Abs(x - y) < 6);
            var boxes = SilhouetteBoxes.Build(alpha, 200, 200, new Vector2(3f, 3f), Cell);
            Assert.Greater(boxes.Count, 10);
            // Every box is short (one or two cells tall), and the higher a box is, the further right.
            boxes.Sort((a, b) => a.yMin.CompareTo(b.yMin));
            for (var i = 0; i < boxes.Count; i++)
            {
                Assert.LessOrEqual(boxes[i].height, 2f * Cell + 1e-4f);
                if (i > 0) Assert.GreaterOrEqual(boxes[i].center.x, boxes[i - 1].center.x - 1e-4f);
            }
            Assert.Less(boxes[0].center.x, 0f);
            Assert.Greater(boxes[boxes.Count - 1].center.x, 0f);
            Assert.IsFalse(Covers(boxes, new Vector2(1.2f, 0.3f)), "below the diagonal is open");
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
