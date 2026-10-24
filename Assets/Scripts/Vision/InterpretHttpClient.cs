using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CalHacks
{
    /// <summary>Unity talks only to local Python; the Bitdeer secret never leaves the backend.</summary>
    public sealed class InterpretHttpClient
    {
        readonly string endpoint;
        readonly int timeout;
        public InterpretHttpClient(GameConfig config)
        {
            endpoint = config.serviceUrl.TrimEnd('/') + "/interpret";
            timeout = Mathf.Max(2, config.interpretTimeout);
        }

        public async Task<VisionResult<InterpretResponse>> InterpretAsync(CaptureResponse capture, int runId,
            string socketId, string goal, CancellationToken ct, string intent = "", string intentMode = "auto")
        {
            if (capture == null || string.IsNullOrEmpty(capture.board_png_base64))
                return VisionResult<InterpretResponse>.Fail("No original drawing PNG available for AI");

            var payload = new InterpretRequest
            {
                capture_id = capture.id,
                run_id = runId,
                socket_id = socketId,
                goal = goal,
                intent = intent,
                intent_mode = intentMode,
                board_png_base64 = capture.board_png_base64,
                cutout_png_base64 = capture.png_base64,
                board_width = capture.board_width,
                board_height = capture.board_height,
                cutout_x = capture.cutout_x,
                cutout_y = capture.cutout_y,
                cutout_width = capture.cutout_width,
                cutout_height = capture.cutout_height,
                ink_px = capture.ink_px
            };
            using var web = new UnityWebRequest(endpoint, "POST");
            web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
            web.downloadHandler = new DownloadHandlerBuffer();
            web.SetRequestHeader("Content-Type", "application/json");
            web.timeout = timeout;
            using (ct.Register(web.Abort))
                await web.SendWebRequest();
            ct.ThrowIfCancellationRequested();
            if (web.result != UnityWebRequest.Result.Success)
                return VisionResult<InterpretResponse>.Fail("Backend: " + web.error + " " + (web.downloadHandler?.text ?? ""));
            try
            {
                var result = JsonUtility.FromJson<InterpretResponse>(web.downloadHandler.text);
                if (result == null) return VisionResult<InterpretResponse>.Fail("Empty /interpret response");
                if (result.capture_id != capture.id || result.run_id != runId)
                    return VisionResult<InterpretResponse>.Fail("Stale or mismatched AI reply");
                return VisionResult<InterpretResponse>.Success(result);
            }
            catch (Exception e)
            {
                return VisionResult<InterpretResponse>.Fail("Invalid /interpret JSON: " + e.Message);
            }
        }
    }
}
