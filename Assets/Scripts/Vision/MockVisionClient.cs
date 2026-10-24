using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Stands in for the Python service. Serves PNGs from Resources/MockDrawings in the same shape as
    /// POST /capture, then returns the drawing's configured label from GET /label after a delay.
    /// </summary>
    public class MockVisionClient : IVisionClient
    {
        public const int EmptyBoard = -1;

        readonly GameConfig config;
        readonly Dictionary<string, string> base64Cache = new Dictionary<string, string>();
        readonly Dictionary<string, (CaptureRequest request, string label)> pending =
            new Dictionary<string, (CaptureRequest, string)>();
        int cycleIndex;
        int? forcedNext;
        int nextId;

        public MockVisionClient(GameConfig config) => this.config = config;

        public string Name => "Mock";

        /// <summary>Picks the drawing for the next CAST: an index into mockDrawings, or EmptyBoard.</summary>
        public void ForceNext(int index) => forcedNext = index;

        public string DescribeNext()
        {
            var index = forcedNext ?? cycleIndex;
            if (index == EmptyBoard) return "(empty board)";
            var d = config.mockDrawings[index % config.mockDrawings.Length];
            return $"{d.resourceName} -> {d.label}";
        }

        public async Task<VisionResult<CaptureResponse>> CaptureAsync(CaptureRequest request, CancellationToken ct)
        {
            await Awaitable.WaitForSecondsAsync(config.mockCaptureDelay, ct);

            var index = forcedNext ?? cycleIndex;
            forcedNext = null;
            var id = "mock-" + ++nextId;
            var response = new CaptureResponse
            {
                id = id,
                socket_id = request.socket_id,
                run_id = request.run_id,
                outline = Array.Empty<float>(),
            };

            if (index == EmptyBoard || config.mockDrawings.Length == 0)
            {
                response.empty_board = true;
                return VisionResult<CaptureResponse>.Success(response);
            }

            cycleIndex = (index + 1) % config.mockDrawings.Length;
            var drawing = config.mockDrawings[index % config.mockDrawings.Length];
            var texture = Resources.Load<Texture2D>("MockDrawings/" + drawing.resourceName);
            if (texture == null)
            {
                return VisionResult<CaptureResponse>.Fail("mock drawing missing: " + drawing.resourceName);
            }
            if (!base64Cache.TryGetValue(drawing.resourceName, out var b64))
            {
                b64 = Convert.ToBase64String(texture.EncodeToPNG());
                base64Cache[drawing.resourceName] = b64;
            }

            response.png_base64 = b64;
            response.width = texture.width;
            response.height = texture.height;
            response.ink_px = texture.width * texture.height / 10;
            pending[id] = (request, drawing.label);
            return VisionResult<CaptureResponse>.Success(response);
        }

        public async Task<VisionResult<LabelResponse>> GetLabelAsync(string captureId, CancellationToken ct)
        {
            var started = Time.realtimeSinceStartup;
            await Awaitable.WaitForSecondsAsync(config.mockLabelDelay, ct);
            if (!pending.Remove(captureId, out var entry))
            {
                return VisionResult<LabelResponse>.Fail("unknown capture id " + captureId);
            }
            return VisionResult<LabelResponse>.Success(new LabelResponse
            {
                status = "done",
                socket_id = entry.request.socket_id,
                run_id = entry.request.run_id,
                label = entry.label,
                confidence = 0.9f,
                provider = "mock",
                latency_ms = (Time.realtimeSinceStartup - started) * 1000f,
            });
        }

        public Task<VisionResult<HealthResponse>> HealthAsync(CancellationToken ct) =>
            Task.FromResult(VisionResult<HealthResponse>.Success(new HealthResponse
            {
                camera = true,
                calibrated = true,
                provider = "mock",
                provider_reachable = true,
            }));

        public async Task<VisionResult<CalibrateResponse>> CalibrateAsync(CancellationToken ct)
        {
            await Awaitable.WaitForSecondsAsync(config.mockCaptureDelay, ct);
            return VisionResult<CalibrateResponse>.Success(new CalibrateResponse { markers_found = 4, saved = true });
        }
    }
}
