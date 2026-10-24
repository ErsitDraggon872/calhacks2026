using NUnit.Framework;
using UnityEngine;

namespace CalHacks.Tests
{
    public class DigitalInkExtractorTests
    {
        static Color32[] Board(int w, int h)
        {
            var board = new Color32[w * h];
            for (var i = 0; i < board.Length; i++) board[i] = new Color32(255, 255, 255, 255);
            return board;
        }

        [Test]
        public void EmptyDigitalCanvasReturnsEmpty()
        {
            var result = DigitalInkExtractor.Extract(Board(20, 10), 20, 10);
            Assert.IsTrue(result.Empty);
            Assert.AreEqual(0, result.InkPx);
        }

        [Test]
        public void OneBlackPixelCropsAndPreservesBottomLeftCoordinates()
        {
            var board = Board(20, 12);
            board[3 * 20 + 8] = new Color32(0, 0, 0, 255);
            var result = DigitalInkExtractor.Extract(board, 20, 12, 1, 2);
            Assert.IsFalse(result.Empty);
            Assert.AreEqual(6, result.X);
            Assert.AreEqual(1, result.Y);
            Assert.AreEqual(5, result.Width);
            Assert.AreEqual(5, result.Height);
            Assert.AreEqual(255, result.Pixels[2 * result.Width + 2].a);
            Assert.AreEqual(0, result.Pixels[0].a);
        }

        [Test]
        public void AllBackgroundIsTransparentAndAllInkIsBlack()
        {
            var board = Board(8, 8);
            board[1 * 8 + 1] = new Color32(20, 20, 20, 255);
            board[6 * 8 + 6] = new Color32(0, 0, 0, 255);
            var result = DigitalInkExtractor.Extract(board, 8, 8, 1, 0);
            Assert.AreEqual(6, result.Width);
            Assert.AreEqual(6, result.Height);
            Assert.AreEqual(2, result.InkPx);
            Assert.AreEqual(0, result.Pixels[1].a);
            Assert.AreEqual(0, result.Pixels[0].r);
        }
    }
}
