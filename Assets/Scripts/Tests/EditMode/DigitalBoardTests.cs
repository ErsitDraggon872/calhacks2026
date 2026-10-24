using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace CalHacks.Tests
{
    public class DigitalBoardTests
    {
        static readonly Rect Full = new Rect(0f, 0f, 1f, 1f);
        const float Threshold = 0.6f;
        const int MinInkPx = 400;

        static bool IsInk(Color32 c) => c.r < 128;

        /// <summary>Board pixel of a canvas point (0-1, origin bottom-left).</summary>
        static Vector2Int Pixel(float x, float y) => new Vector2Int(
            Mathf.RoundToInt(StrokeBoard.Border + x * (StrokeBoard.CanvasWidth - 1)),
            Mathf.RoundToInt(StrokeBoard.Border + y * (StrokeBoard.CanvasHeight - 1)));

        [Test]
        public void StrokeIsDrawnUprightFromTheBottomLeft()
        {
            var board = new StrokeBoard();
            board.Add("c1", new[] { 0.1f, 0.1f, 0.2f, 0.1f });
            var frame = board.Render("c1", out var w, out var h);
            Assert.AreEqual(StrokeBoard.Width, w);
            Assert.AreEqual(StrokeBoard.Height, h);
            var p = Pixel(0.15f, 0.1f);
            Assert.IsTrue(IsInk(frame[p.y * w + p.x]), "ink near the bottom");
            Assert.IsFalse(IsInk(frame[(h - 1 - p.y) * w + p.x]), "not mirrored to the top");
            Assert.IsFalse(IsInk(frame[0]), "the border stays white");
        }

        [Test]
        public void OnlyTheAskedCaptureIsDrawn()
        {
            var board = new StrokeBoard();
            board.Add("old", new[] { 0.2f, 0.5f, 0.8f, 0.5f });
            var frame = board.Render("new", out _, out _);
            foreach (var c in frame) Assert.IsFalse(IsInk(c));
        }

        [Test]
        public void ClearingKeepsTheBuildStillWaiting()
        {
            var board = new StrokeBoard();
            board.Add("pinned", new[] { 0.2f, 0.5f, 0.8f, 0.5f });
            board.Add("other", new[] { 0.2f, 0.3f, 0.8f, 0.3f });
            Assert.AreEqual("other", board.Latest);
            board.ForgetAllBut("pinned");
            Assert.IsTrue(board.Has("pinned"));
            Assert.IsFalse(board.Has("other"));
            Assert.AreEqual("pinned", board.Latest);
            board.ForgetAllBut(null);
            Assert.IsFalse(board.Has("pinned"));
            Assert.IsNull(board.Latest);
        }

        [Test]
        public void BlankBoardExtractsAsEmpty()
        {
            var frame = new StrokeBoard().Render(null, out var w, out var h);
            Assert.IsTrue(InkExtractor.Extract(frame, w, h, Full, Threshold, MinInkPx).Empty);
        }

        [Test]
        public void DrawnBoardExtractsTheDrawingWhereItWasDrawn()
        {
            var board = new StrokeBoard();
            board.Add("c1", new[] { 0.25f, 0.5f, 0.75f, 0.5f });
            var frame = board.Render("c1", out var w, out var h);
            var ink = InkExtractor.Extract(frame, w, h, Full, Threshold, MinInkPx);
            Assert.IsFalse(ink.Empty);
            var left = Pixel(0.25f, 0.5f);
            var right = Pixel(0.75f, 0.5f);
            // The cutout is the stroke (pen radius each side) plus InkExtractor's 8 px margin.
            Assert.AreEqual(left.x - StrokeBoard.PenRadius - 8, ink.X, 2);
            Assert.AreEqual(right.x - left.x + 2 * StrokeBoard.PenRadius + 16, ink.Width, 3);
            Assert.AreEqual(left.y - StrokeBoard.PenRadius - 8, ink.Y, 2);
        }

        [Test]
        public void DrawingThatRunsToThePageEdgeIsKept()
        {
            // InkExtractor drops blobs touching the frame edge; the board's white border keeps this one inside.
            var board = new StrokeBoard();
            board.Add("c1", new[] { 0f, 0.5f, 0.4f, 0.5f, 0.4f, 0f });
            var frame = board.Render("c1", out var w, out var h);
            Assert.IsFalse(InkExtractor.Extract(frame, w, h, Full, Threshold, MinInkPx).Empty);
        }

        [Test]
        public void CutoutFallbackSitsInTheMiddleOfAWhiteBoard()
        {
            var cutout = new Texture2D(40, 20, TextureFormat.RGBA32, false);
            var px = new Color32[40 * 20];
            for (var i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 255);
            cutout.SetPixels32(px);
            cutout.Apply();
            var frame = StrokeBoard.RenderCutout(cutout, out var w, out var h);
            Object.DestroyImmediate(cutout);
            Assert.IsTrue(IsInk(frame[(h / 2) * w + w / 2]));
            Assert.IsFalse(IsInk(frame[0]));
        }

        class FakeBoard : IBoardFrameSource
        {
            public Color32[] Frame;
            public int W, H;
            public string DeviceName => "fake";
            public string Status => "ready";
            public bool HasFreshFrame => true;

            public Color32[] Grab(out int width, out int height)
            {
                width = W;
                height = H;
                return Frame;
            }
        }

        [Test]
        public void VisionClientReadsTheWholeDigitalBoardWhateverTheWebcamCrop()
        {
            var config = ScriptableObject.CreateInstance<GameConfig>();
            // A webcam crop that would miss the drawing: the digital board passes the whole frame instead.
            config.boardCrop = new Rect(0f, 0f, 0.1f, 0.1f);
            var board = new StrokeBoard();
            board.Add("c1", new[] { 0.25f, 0.5f, 0.75f, 0.5f });
            var source = new FakeBoard();
            source.Frame = board.Render("c1", out source.W, out source.H);
            var client = new WebcamVisionClient(config, source, new NoInterpreter(), Full);
            try
            {
                var capture = client.CaptureAsync(new CaptureRequest { socket_id = "gap", run_id = 1 }, CancellationToken.None).Result;
                Assert.IsTrue(capture.Ok, capture.Error);
                Assert.IsFalse(capture.Data.empty_board);
                Assert.IsFalse(string.IsNullOrEmpty(capture.Data.png_base64));
                var label = client.GetLabelAsync(capture.Data.id, CancellationToken.None).Result;
                Assert.IsTrue(label.Ok, label.Error);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
