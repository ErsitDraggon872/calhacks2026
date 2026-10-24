using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CalHacks
{
    /// <summary>Talks to Andy's Python service. All payloads are flat JSON parsed with JsonUtility.</summary>
    public class HttpVisionClient : IVisionClient
    {
        readonly string baseUrl;
        readonly GameConfig config;

        public HttpVisionClient(GameConfig config)
        {
            this.config = config;
            baseUrl = config.serviceUrl.TrimEnd('/');
        }

        public string Name => "HTTP " + baseUrl;

        public Task<VisionResult<CaptureResponse>> CaptureAsync(CaptureRequest request, CancellationToken ct) =>
            Send<CaptureResponse>("POST", "/capture", JsonUtility.ToJson(request), config.captureTimeout, ct);

        public Task<VisionResult<LabelResponse>> GetLabelAsync(string captureId, CancellationToken ct) =>
            Send<LabelResponse>("GET", "/label/" + UnityWebRequest.EscapeURL(captureId), null, config.labelTimeout, ct);

        public Task<VisionResult<HealthResponse>> HealthAsync(CancellationToken ct) =>
            Send<HealthResponse>("GET", "/health", null, 3, ct);

        public Task<VisionResult<CalibrateResponse>> CalibrateAsync(CancellationToken ct) =>
            Send<CalibrateResponse>("POST", "/calibrate", "{}", config.captureTimeout, ct);

        async Task<VisionResult<T>> Send<T>(string method, string path, string jsonBody, int timeoutSeconds, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var request = new UnityWebRequest(baseUrl + path, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            if (jsonBody != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.timeout = Mathf.Max(1, timeoutSeconds);

            using (ct.Register(request.Abort))
            {
                await request.SendWebRequest();
            }
            ct.ThrowIfCancellationRequested();

            var body = request.downloadHandler?.text;
            if (request.result != UnityWebRequest.Result.Success)
            {
                return VisionResult<T>.Fail(DescribeError(request, body));
            }
            try
            {
                var data = JsonUtility.FromJson<T>(body);
                return data == null ? VisionResult<T>.Fail("empty response") : VisionResult<T>.Success(data);
            }
            catch (Exception e)
            {
                return VisionResult<T>.Fail("bad response from " + path + ": " + e.Message);
            }
        }

        static string DescribeError(UnityWebRequest request, string body)
        {
            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                return "unreachable: " + request.error;
            }
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var err = JsonUtility.FromJson<ErrorResponse>(body);
                    if (!string.IsNullOrEmpty(err?.error)) return err.error;
                    if (!string.IsNullOrEmpty(err?.detail)) return err.detail;
                }
                catch (Exception)
                {
                    // Not a flat error body; fall through to the raw text.
                }
                return $"HTTP {request.responseCode}: {body}";
            }
            return $"HTTP {request.responseCode}: {request.error}";
        }
    }
}
