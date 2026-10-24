using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Unity reads the whiteboard itself: grabs a BoardCamera frame, cuts the ink out locally, and hands the capture
    /// to an <see cref="IInterpreter"/> for the label.
    /// </summary>
    public class WebcamVisionClient : IVisionClient
    {
        readonly GameConfig config;
        readonly BoardCamera camera;
        readonly IInterpreter interpreter;
        readonly Dictionary<string, BoardCapture> pending = new Dictionary<string, BoardCapture>();
        int nextId;

        public WebcamVisionClient(GameConfig config, BoardCamera camera, IInterpreter interpreter)
        {
            this.config = config;
            this.camera = camera;
            this.interpreter = interpreter;
        }

        public string Name => $"Webcam {(camera != null ? camera.DeviceName ?? "-" : "(missing)")} + interpreter {interpreter.Name}";

        public Task<VisionResult<CaptureResponse>> CaptureAsync(CaptureRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (camera == null) return Fail("camera: BoardCamera missing from the scene");
            var frame = camera.Grab(out var w, out var h);
            if (frame == null) return Fail("camera: " + camera.Status);

            var ink = InkExtractor.Extract(frame, w, h, config.boardCrop, config.inkThreshold, config.minInkPx);
            var response = new CaptureResponse
            {
                id = "cam-" + ++nextId,
                socket_id = request.socket_id,
                run_id = request.run_id,
                empty_board = ink.Empty,
                ink_px = ink.InkPx,
                outline = Array.Empty<float>(),
            };
            if (!ink.Empty)
            {
                var texture = new Texture2D(ink.Width, ink.Height, TextureFormat.RGBA32, false);
                texture.SetPixels32(ink.Pixels);
                texture.Apply();
                response.png_base64 = Convert.ToBase64String(texture.EncodeToPNG());
                response.width = ink.Width;
                response.height = ink.Height;
                UnityEngine.Object.Destroy(texture);

                var board = InkExtractor.BoardRect(w, h, config.boardCrop);
                pending[response.id] = new BoardCapture
                {
                    Request = request,
                    Cutout = response,
                    Board = Crop(frame, w, board),
                    BoardWidth = board.width,
                    BoardHeight = board.height,
                    CutoutRect = new RectInt(ink.X, ink.Y, ink.Width, ink.Height),
                };
            }
            Debug.Log($"[Webcam] {response.id}: {w}x{h} frame, ink_px={ink.InkPx}" +
                      (ink.Empty ? " (empty)" : $", cutout {ink.Width}x{ink.Height} at ({ink.X},{ink.Y})"));
            return Task.FromResult(VisionResult<CaptureResponse>.Success(response));
        }

        public Task<VisionResult<LabelResponse>> GetLabelAsync(string captureId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!pending.Remove(captureId, out var capture))
            {
                return Task.FromResult(VisionResult<LabelResponse>.Fail("unknown capture id " + captureId));
            }
            return interpreter.InterpretAsync(capture, ct);
        }

        public Task<VisionResult<HealthResponse>> HealthAsync(CancellationToken ct) =>
            Task.FromResult(VisionResult<HealthResponse>.Success(new HealthResponse
            {
                camera = camera != null && camera.HasFreshFrame,
                calibrated = true,
                provider = interpreter.Name,
                provider_reachable = !(interpreter is NoInterpreter),
            }));

        public Task<VisionResult<CalibrateResponse>> CalibrateAsync(CancellationToken ct) =>
            Task.FromResult(VisionResult<CalibrateResponse>.Fail("not used with the Unity webcam (set boardCrop instead)"));

        static Color32[] Crop(Color32[] frame, int frameW, RectInt rect)
        {
            var result = new Color32[rect.width * rect.height];
            for (var y = 0; y < rect.height; y++)
                Array.Copy(frame, (rect.y + y) * frameW + rect.x, result, y * rect.width, rect.width);
            return result;
        }

        static Task<VisionResult<CaptureResponse>> Fail(string error) =>
            Task.FromResult(VisionResult<CaptureResponse>.Fail(error));
    }
}
