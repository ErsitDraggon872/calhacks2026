using System;

namespace CalHacks
{
    // Flat payloads for the Python vision service (localhost:8000). Field names match the JSON keys exactly,
    // because JsonUtility maps by name. Don't rename without telling Andy.

    [Serializable]
    public class CaptureRequest
    {
        public string socket_id;
        public int run_id;
        public string[] accepted_labels;
    }

    [Serializable]
    public class CaptureResponse
    {
        public string id;
        public string socket_id;
        public int run_id;
        public bool empty_board;
        public int ink_px;
        public string png_base64;
        // Unity's raw board image and crop coordinates for POST /interpret.
        public string board_png_base64;
        public int board_width;
        public int board_height;
        public int cutout_x;
        public int cutout_y;
        public int cutout_width;
        public int cutout_height;
        public int width;
        public int height;
        public float angle;
        public float[] outline;
    }

    [Serializable]
    public class LabelResponse
    {
        public string status; // "done" or "timeout"
        public string socket_id;
        public int run_id;
        public string label;
        public float confidence;
        public string provider;
        public float latency_ms;
    }

    [Serializable]
    public class HealthResponse
    {
        public bool camera;
        public bool calibrated;
        public string provider;
        public bool provider_reachable;
    }

    [Serializable]
    public class CalibrateResponse
    {
        public int markers_found;
        public bool saved;
    }

    /// <summary>Error body. Accepts {"error": "..."} or FastAPI's {"detail": "..."}.</summary>
    [Serializable]
    public class ErrorResponse
    {
        public string error;
        public string detail;
    }

    public readonly struct VisionResult<T>
    {
        public readonly T Data;
        public readonly string Error;

        VisionResult(T data, string error)
        {
            Data = data;
            Error = error;
        }

        public bool Ok => Error == null;
        public static VisionResult<T> Success(T data) => new VisionResult<T>(data, null);
        public static VisionResult<T> Fail(string error) => new VisionResult<T>(default, error ?? "unknown error");
    }
}
