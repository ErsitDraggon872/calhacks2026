using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Uses the Unity digital whiteboard rather than a webcam. Keeps the same capture/cutout contract as
    /// WebcamVisionClient, so an HttpInterpreter can later be plugged in without changing the drawing UI.
    /// Currently the RunManager supplies NoInterpreter, and inventions are solid cutouts only.
    /// </summary>
    public class DigitalVisionClient : IVisionClient
    {
        readonly DigitalDrawingCanvas pad;
        readonly IInterpreter interpreter;
        readonly Dictionary<string, BoardCapture> pending = new Dictionary<string, BoardCapture>();
        int nextId;

        public DigitalVisionClient(DigitalDrawingCanvas pad, IInterpreter interpreter)
        {
            this.pad = pad;
            this.interpreter = interpreter;
        }

        public string Name => "Digital drawing + " + interpreter.Name;

        public Task<VisionResult<CaptureResponse>> CaptureAsync(CaptureRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (pad == null)
                return Task.FromResult(VisionResult<CaptureResponse>.Fail("digital drawing canvas missing"));

            var board = pad.CopyBoardPixels();
            var w = pad.Width;
            var h = pad.Height;
            var ink = DigitalInkExtractor.Extract(board, w, h);
            var response = new CaptureResponse
            {
                id = "digital-" + ++nextId,
                socket_id = request.socket_id,
                run_id = request.run_id,
                empty_board = ink.Empty,
                ink_px = ink.InkPx,
                outline = Array.Empty<float>()
            };
            if (!ink.Empty)
            {
                var texture = new Texture2D(ink.Width, ink.Height, TextureFormat.RGBA32, false);
                texture.SetPixels32(ink.Pixels);
                texture.Apply(false);
                response.png_base64 = Convert.ToBase64String(texture.EncodeToPNG());
                response.width = ink.Width;
                response.height = ink.Height;
                response.cutout_x = ink.X;
                response.cutout_y = ink.Y;
                response.cutout_width = ink.Width;
                response.cutout_height = ink.Height;
                response.board_width = w;
                response.board_height = h;
                // Interpret the original white canvas, not the alpha-only sprite.
                var rawBoard = new Texture2D(w, h, TextureFormat.RGBA32, false);
                rawBoard.SetPixels32(board);
                rawBoard.Apply(false);
                response.board_png_base64 = Convert.ToBase64String(rawBoard.EncodeToPNG());
                UnityEngine.Object.Destroy(rawBoard);
                UnityEngine.Object.Destroy(texture);

                // Free builds do not call GetLabelAsync in this version of RunManager.
                if (request.socket_id != RunManager.FreeBuildId)
                {
                    pending[response.id] = new BoardCapture
                    {
                        Request = request,
                        Cutout = response,
                        Board = board,
                        BoardWidth = w,
                        BoardHeight = h,
                        CutoutRect = new RectInt(ink.X, ink.Y, ink.Width, ink.Height)
                    };
                }
            }
            return Task.FromResult(VisionResult<CaptureResponse>.Success(response));
        }

        public Task<VisionResult<LabelResponse>> GetLabelAsync(string captureId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!pending.TryGetValue(captureId, out var capture))
                return Task.FromResult(VisionResult<LabelResponse>.Fail("unknown capture id " + captureId));
            pending.Remove(captureId);
            return interpreter.InterpretAsync(capture, ct);
        }

        public Task<VisionResult<HealthResponse>> HealthAsync(CancellationToken ct) =>
            Task.FromResult(VisionResult<HealthResponse>.Success(new HealthResponse
            {
                camera = false,
                calibrated = true,
                provider = interpreter.Name,
                provider_reachable = !(interpreter is NoInterpreter)
            }));

        public Task<VisionResult<CalibrateResponse>> CalibrateAsync(CancellationToken ct) =>
            Task.FromResult(VisionResult<CalibrateResponse>.Fail("digital whiteboard needs no calibration"));
    }
}
