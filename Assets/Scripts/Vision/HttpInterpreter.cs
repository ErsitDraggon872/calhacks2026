using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CalHacks
{
    /// <summary>The POST /interpret request body (docs/interpret-contract.md). Field names are the JSON keys.</summary>
    [Serializable]
    public class InterpretRequest
    {
        public string capture_id;
        public int run_id;
        public string socket_id;
        public string goal;
        public string board_png_base64;
        public int board_width;
        public int board_height;
        public string cutout_png_base64;
        public int cutout_x;
        public int cutout_y;
        public int cutout_width;
        public int cutout_height;
        public int ink_px;
    }

    /// <summary>
    /// Asks the backend what a drawing is (tools/thinkink_backend.py): POST /interpret with the board crop and the
    /// cut-out. The reply's attributes ride along as <see cref="LabelResponse.interpretation"/>. A backend error, a
    /// timeout or an unreachable server is a failed result, never an "unknown" drawing.
    /// </summary>
    public class HttpInterpreter : IInterpreter
    {
        readonly string baseUrl;
        readonly int timeoutSeconds;

        public HttpInterpreter(string baseUrl, int timeoutSeconds)
        {
            this.baseUrl = baseUrl.TrimEnd('/');
            this.timeoutSeconds = Mathf.Max(1, timeoutSeconds);
        }

        public string Name => "HTTP " + baseUrl;

        public async Task<VisionResult<LabelResponse>> InterpretAsync(BoardCapture capture, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var body = JsonUtility.ToJson(BuildRequest(capture, EncodeBoard(capture)));
            using var request = new UnityWebRequest(baseUrl + "/interpret", "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = timeoutSeconds;

            using (ct.Register(request.Abort))
            {
                await request.SendWebRequest();
            }
            ct.ThrowIfCancellationRequested();

            var text = request.downloadHandler?.text;
            if (request.result != UnityWebRequest.Result.Success)
            {
                return VisionResult<LabelResponse>.Fail("interpreter " + HttpVisionClient.DescribeError(request, text));
            }
            InterpretResponse reply;
            try
            {
                reply = JsonUtility.FromJson<InterpretResponse>(text);
            }
            catch (Exception e)
            {
                return VisionResult<LabelResponse>.Fail("interpreter: bad response: " + e.Message);
            }
            return ToLabel(reply, capture.Request);
        }

        /// <summary>The request for one capture. Pure, apart from the board PNG passed in.</summary>
        public static InterpretRequest BuildRequest(BoardCapture capture, string boardPngBase64) => new InterpretRequest
        {
            capture_id = capture.Cutout.id,
            run_id = capture.Request.run_id,
            socket_id = capture.Request.socket_id,
            goal = capture.Request.goal ?? "",
            board_png_base64 = boardPngBase64,
            board_width = capture.BoardWidth,
            board_height = capture.BoardHeight,
            cutout_png_base64 = capture.Cutout.png_base64,
            cutout_x = capture.CutoutRect.x,
            cutout_y = capture.CutoutRect.y,
            cutout_width = capture.CutoutRect.width,
            cutout_height = capture.CutoutRect.height,
            ink_px = capture.Cutout.ink_px,
        };

        /// <summary>
        /// A reply as the label result the run expects: <c>ok</c> and <c>rejected</c> are answers (a rejection is
        /// still something the AI said), <c>error</c> or no reply at all is a failure.
        /// </summary>
        public static VisionResult<LabelResponse> ToLabel(InterpretResponse reply, CaptureRequest request)
        {
            if (reply == null || string.IsNullOrWhiteSpace(reply.status))
            {
                return VisionResult<LabelResponse>.Fail("interpreter: empty response");
            }
            if (reply.status.Trim().ToLowerInvariant() == "error")
            {
                return VisionResult<LabelResponse>.Fail("interpreter: " + (string.IsNullOrWhiteSpace(reply.message) ? "error" : reply.message));
            }
            return VisionResult<LabelResponse>.Success(new LabelResponse
            {
                status = "done",
                socket_id = request.socket_id,
                run_id = reply.run_id,
                label = string.IsNullOrWhiteSpace(reply.name) ? SocketLogic.Unknown : reply.name,
                provider = reply.provider,
                latency_ms = reply.latency_ms,
                interpretation = reply,
            });
        }

        static string EncodeBoard(BoardCapture capture)
        {
            var texture = new Texture2D(capture.BoardWidth, capture.BoardHeight, TextureFormat.RGB24, false);
            try
            {
                texture.SetPixels32(capture.Board);
                texture.Apply();
                return Convert.ToBase64String(texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
