using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Where the Creator's drawing comes from at BUILD. The digital canvas and the physical whiteboard (webcam) are
    /// both one of these, so the co-op flow doesn't care which is plugged in.
    /// </summary>
    public interface IDrawingCapture
    {
        string Name { get; }

        /// <summary>
        /// The finished drawing as a PNG: ink on transparent, cropped to the ink (what the Explorer's cutout shows).
        /// Null with <paramref name="error"/> set if there's nothing to build.
        /// </summary>
        byte[] Capture(out string error);
    }

    /// <summary>
    /// Turns a BUILD into physics JSON (docs/interpret-contract.md response, verbatim). Wrap the existing /interpret
    /// client in one of these; the JSON goes to SpacetimeDB untouched and the Explorer's physics reads it.
    /// </summary>
    public interface IPhysicsInterpreter
    {
        string Name { get; }
        Task<string> InterpretAsync(byte[] cutoutPng, string captureId, CancellationToken ct);
    }

    /// <summary>Stand-in until the real /interpret client is plugged in: every drawing is a solid platform.</summary>
    public class SupportOnlyInterpreter : IPhysicsInterpreter
    {
        public string Name => "support-only";

        public Task<string> InterpretAsync(byte[] cutoutPng, string captureId, CancellationToken ct) =>
            Task.FromResult(
                "{\"status\":\"ok\",\"capture_id\":\"" + captureId + "\",\"name\":\"drawing\"," +
                "\"message\":\"Your partner's drawing. Stand on it!\",\"width_m\":2,\"ink_cost\":10," +
                "\"components\":[{\"type\":\"support\",\"activation\":\"always\",\"anchor_x\":0.5,\"anchor_y\":0.5," +
                "\"launch_speed\":0,\"angle_deg\":0,\"acceleration\":0,\"duration_s\":0}]," +
                "\"provider\":\"none\",\"latency_ms\":0}");
    }

    /// <summary>The physical whiteboard: the same BoardCamera + InkExtractor cut-out the single-player CAST uses.</summary>
    public class WebcamDrawingCapture : IDrawingCapture
    {
        readonly GameConfig config;
        readonly BoardCamera camera;

        public WebcamDrawingCapture(GameConfig config, BoardCamera camera)
        {
            this.config = config;
            this.camera = camera;
        }

        public string Name => "webcam " + (camera != null ? camera.DeviceName ?? "-" : "(missing)");

        public byte[] Capture(out string error)
        {
            error = null;
            if (camera == null)
            {
                error = "No BoardCamera in the scene.";
                return null;
            }
            var frame = camera.Grab(out var w, out var h);
            if (frame == null)
            {
                error = "Camera: " + camera.Status;
                return null;
            }
            var ink = InkExtractor.Extract(frame, w, h, config.boardCrop, config.inkThreshold, config.minInkPx, config.autoInkThreshold);
            if (ink.Empty)
            {
                error = "The board looks empty. Draw something first!";
                return null;
            }
            var texture = new Texture2D(ink.Width, ink.Height, TextureFormat.RGBA32, false);
            texture.SetPixels32(ink.Pixels);
            texture.Apply();
            var png = texture.EncodeToPNG();
            UnityEngine.Object.Destroy(texture);
            return png;
        }
    }

    /// <summary>
    /// Fallback digital capture: renders this drawing's synced strokes. Lets BUILD work from strokes alone; a real
    /// canvas should implement <see cref="IDrawingCapture"/> itself and hand over its own pixels.
    /// </summary>
    public class StrokeDrawingCapture : IDrawingCapture
    {
        readonly int width;
        readonly int height;

        public StrokeDrawingCapture(int width, int height)
        {
            this.width = width;
            this.height = height;
        }

        public string Name => "synced strokes";

        public byte[] Capture(out string error)
        {
            error = null;
            var session = ThinkInkSession.Instance;
            var build = CreatorBuild.Instance;
            var raster = new StrokeRaster(width, height, Color.clear, Color.black, Mathf.Max(2, width / 120));
            var any = false;
            if (session != null && build != null)
            {
                foreach (var s in session.Strokes())
                {
                    if (s.CaptureId != build.CaptureId) continue;
                    raster.Draw(s.Points);
                    any = true;
                }
            }
            if (!any)
            {
                error = "Nothing drawn yet.";
                return null;
            }
            var png = raster.CroppedPng(8);
            raster.Dispose();
            return png;
        }
    }
}
