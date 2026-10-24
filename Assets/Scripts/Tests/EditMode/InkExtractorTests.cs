using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace CalHacks.Tests
{
    public class InkExtractorTests
    {
        const int W = 320;
        const int H = 240;
        static readonly Rect Full = new Rect(0f, 0f, 1f, 1f);

        static Color32[] Board(System.Func<int, int, byte> brightness)
        {
            var frame = new Color32[W * H];
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var v = brightness(x, y);
                frame[y * W + x] = new Color32(v, v, v, 255);
            }
            return frame;
        }

        static void Stroke(Color32[] frame, int x0, int y0, int x1, int y1)
        {
            for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                frame[y * W + x] = new Color32(20, 20, 25, 255);
        }

        [Test]
        public void BlankBoardIsEmpty()
        {
            var result = InkExtractor.Extract(Board((x, y) => 230), W, H, Full, 0.6f, 50);
            Assert.IsTrue(result.Empty);
            Assert.AreEqual(0, result.InkPx);
            Assert.IsNull(result.Pixels);
        }

        [Test]
        public void StrokeIsFoundAndCroppedTightly()
        {
            var frame = Board((x, y) => 230);
            Stroke(frame, 100, 80, 160, 86); // 60 x 6
            var result = InkExtractor.Extract(frame, W, H, Full, 0.6f, 50);
            Assert.IsFalse(result.Empty);
            // Corner pixels have only 3 inked neighbours and are dropped by the speck filter.
            Assert.AreEqual(60 * 6 - 4, result.InkPx);
            Assert.AreEqual(60 + 16, result.Width);
            Assert.AreEqual(6 + 16, result.Height);
            Assert.AreEqual(255, result.Pixels[8 * result.Width + 8 + 30].a);
            Assert.AreEqual(0, result.Pixels[0].a);
        }

        [Test]
        public void LightingGradientIsNotInk()
        {
            // Bright on the left, half as bright on the right: a whole-frame threshold would call the right side ink.
            var frame = Board((x, y) => (byte)(240 - 120 * x / (W - 1)));
            Stroke(frame, 250, 100, 290, 106);
            var result = InkExtractor.Extract(frame, W, H, Full, 0.6f, 50);
            Assert.IsFalse(result.Empty);
            Assert.AreEqual(40 * 6 - 4, result.InkPx);
        }

        [Test]
        public void RealWhiteboardWithHandShadowKeepsTheWholeFace()
        {
            // Hand-held C270 frame: a face (box, two eyes, mouth) with the drawer's hand shadow over the mouth,
            // the board's wooden frame on the left. The face spans about x 260-635, y 80-395 (from the top).
            var png = File.ReadAllBytes(Path.Combine(Application.dataPath, "Scripts/Tests/EditMode/Fixtures/whiteboard_face_hand_shadow.png"));
            var texture = new Texture2D(2, 2);
            Assert.IsTrue(texture.LoadImage(png));
            var result = InkExtractor.Extract(texture.GetPixels32(), texture.width, texture.height, Full, 0.6f, 400);
            Object.DestroyImmediate(texture);

            Assert.IsFalse(result.Empty);
            Assert.That(result.Width, Is.InRange(360, 410), "crop width: the whole face, not the frame");
            Assert.That(result.Height, Is.InRange(300, 350), "crop height: the whole face, not the shadow");
        }

        [Test]
        public void FilledShapeKeepsItsInterior()
        {
            // Wider than a stroke, so only its rim reads as ink at first; the dark enclosed middle is filled back in.
            var frame = Board((x, y) => 230);
            Stroke(frame, 100, 60, 180, 140);
            var result = InkExtractor.Extract(frame, W, H, Full, 0.6f, 50);
            Assert.AreEqual(80 + 16, result.Width);
            Assert.AreEqual(80 * 80 - 4, result.InkPx);
            Assert.AreEqual(255, result.Pixels[(8 + 40) * result.Width + 8 + 40].a);
        }

        [Test]
        public void OutlineInteriorStaysEmpty()
        {
            var frame = Board((x, y) => 230);
            Stroke(frame, 100, 60, 180, 66);
            Stroke(frame, 100, 134, 180, 140);
            Stroke(frame, 100, 60, 106, 140);
            Stroke(frame, 174, 60, 180, 140);
            var result = InkExtractor.Extract(frame, W, H, Full, 0.6f, 50);
            Assert.AreEqual(0, result.Pixels[(8 + 40) * result.Width + 8 + 40].a);
        }

        [Test]
        public void IsolatedSpecksAreIgnored()
        {
            var frame = Board((x, y) => 230);
            Stroke(frame, 100, 80, 160, 86);
            Stroke(frame, 5, 5, 6, 6);
            Stroke(frame, 300, 220, 301, 221);
            var result = InkExtractor.Extract(frame, W, H, Full, 0.6f, 50);
            Assert.AreEqual(60 + 16, result.Width);
            Assert.AreEqual(6 + 16, result.Height);
        }

        [Test]
        public void DarkStuffAtTheFrameEdgeIsNotPartOfTheDrawing()
        {
            // The board's frame / the wall behind it along the left edge, plus a few smudges.
            var frame = Board((x, y) => x < 25 ? (byte)30 : (byte)230);
            Stroke(frame, 120, 80, 200, 86);   // the drawing: a box outline...
            Stroke(frame, 120, 150, 200, 156);
            Stroke(frame, 120, 80, 126, 156);
            Stroke(frame, 194, 80, 200, 156);
            Stroke(frame, 150, 110, 160, 120); // ...with an eye inside it
            Stroke(frame, 280, 30, 283, 33);   // smudge far away
            var result = InkExtractor.Extract(frame, W, H, Full, 0.6f, 50);
            Assert.IsFalse(result.Empty);
            Assert.AreEqual(80 + 16, result.Width);
            Assert.AreEqual(76 + 16, result.Height);
            // The eye is a separate blob but well above the smudge size, so it stays.
            Assert.AreEqual(255, result.Pixels[(8 + 35) * result.Width + 8 + 35].a);
        }

        [Test]
        public void CropLimitsTheSearch()
        {
            var frame = Board((x, y) => 230);
            Stroke(frame, 10, 10, 60, 16);
            var result = InkExtractor.Extract(frame, W, H, new Rect(0.5f, 0f, 0.5f, 1f), 0.6f, 50);
            Assert.IsTrue(result.Empty);
        }
    }
}
