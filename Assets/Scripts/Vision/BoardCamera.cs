using System.Linq;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Owns the whiteboard webcam when the capture source is Webcam. Opens it once and keeps it running so a BUILD
    /// grabs the current frame instantly. Everything downstream (preview, capture, ink cut-out) uses the upright frame
    /// from <see cref="Grab"/>, so the preview always shows exactly the pixels a capture will use.
    /// </summary>
    public class BoardCamera : MonoBehaviour
    {
        /// <summary>A frame older than this means the camera has stalled.</summary>
        const float FreshFrameSeconds = 1f;

        public static BoardCamera Instance { get; private set; }

        [SerializeField] GameConfig config;

        WebCamTexture webcam;
        Color32[] raw;
        Texture2D preview;
        float nextDeviceCheck;
        float lastFrameTime = float.NegativeInfinity;
        bool disconnected;
        bool wanted;

        public string DeviceName { get; private set; }
        public string Status { get; private set; } = "starting";
        /// <summary>Set by the preview overlay while it is open; the upright preview texture is only built then.</summary>
        public bool PreviewWanted { get; set; }
        /// <summary>The latest upright frame, while <see cref="PreviewWanted"/>. Null until one arrives.</summary>
        public Texture2D Preview => preview;

        /// <summary>The camera is delivering frames right now, not merely opened.</summary>
        public bool HasFreshFrame => webcam != null && !disconnected && webcam.isPlaying &&
                                     Time.unscaledTime - lastFrameTime < FreshFrameSeconds;

        void Awake()
        {
            Instance = this;
            if (config == null) config = Resources.Load<GameConfig>("GameConfig");
            if (config == null) config = ScriptableObject.CreateInstance<GameConfig>();
        }

        void Start()
        {
            // RunManager can select either source at runtime. Avoid opening the device in Digital mode.
            if (RunManager.Instance != null)
            {
                SetCaptureEnabled(RunManager.Instance.ActiveCaptureSource == CaptureSource.Webcam);
                return;
            }
            SetCaptureEnabled(config.ResolveCaptureSource() == CaptureSource.Webcam);
        }

        /// <summary>Start/stop the USB camera when the player switches input source in the game.</summary>
        public void SetCaptureEnabled(bool enabled)
        {
            if (!enabled)
            {
                wanted = false;
                PreviewWanted = false;
                if (webcam != null)
                {
                    webcam.Stop();
                    Destroy(webcam);
                    webcam = null;
                }
                if (preview != null)
                {
                    Destroy(preview);
                    preview = null;
                }
                disconnected = false;
                lastFrameTime = float.NegativeInfinity;
                Status = "disabled (digital drawing mode)";
                return;
            }

            wanted = true;
            if (webcam == null)
            {
                nextDeviceCheck = Time.unscaledTime + 2f;
                Open();
            }
        }

        void OnDestroy()
        {
            if (webcam != null)
            {
                webcam.Stop();
                Destroy(webcam);
            }
            if (preview != null) Destroy(preview);
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!wanted) return;
            if (webcam == null)
            {
                // Not plugged in yet: keep looking, so plugging the webcam in mid-game just works.
                if (wanted && Time.unscaledTime >= nextDeviceCheck)
                {
                    nextDeviceCheck = Time.unscaledTime + 2f;
                    Open();
                }
                return;
            }
            if (Time.unscaledTime >= nextDeviceCheck) CheckConnected();
            if (disconnected) return;

            if (webcam.didUpdateThisFrame && webcam.width > 16)
            {
                lastFrameTime = Time.unscaledTime;
                if (PreviewWanted) RefreshPreview();
            }

            Status = HasFreshFrame ? $"{DeviceName}  {webcam.width}x{webcam.height}"
                : float.IsNegativeInfinity(lastFrameTime) ? $"{DeviceName}  (waiting for the first frame)"
                : $"{DeviceName}  (no new frame for {Time.unscaledTime - lastFrameTime:0}s)";
        }

        public void Open()
        {
            if (!wanted || (webcam != null && !disconnected)) return;
            var devices = WebCamTexture.devices;
            var named = config.ResolveCameraDevice();
            // Never fall back to the laptop's own camera: pointing at the player instead of the board is worse than
            // saying the board camera is missing.
            var device = string.IsNullOrEmpty(named)
                ? devices.FirstOrDefault(IsExternal)
                : devices.FirstOrDefault(d => d.name == named);
            if (string.IsNullOrEmpty(device.name))
            {
                var status = (string.IsNullOrEmpty(named) ? "no USB webcam found" : $"camera '{named}' not found") +
                             $" (devices: {(devices.Length == 0 ? "none" : string.Join(", ", devices.Select(d => d.name)))})";
                if (status != Status) Debug.LogWarning("[Camera] " + status);
                Status = status;
                return;
            }

            DeviceName = device.name;
            lastFrameTime = float.NegativeInfinity;
            webcam = new WebCamTexture(device.name, config.cameraWidth, config.cameraHeight, 30);
            webcam.Play();
            Debug.Log($"[Camera] Opened '{device.name}'. Devices: {string.Join(", ", devices.Select(d => d.name))}");
        }

        /// <summary>
        /// Unplugging a USB webcam doesn't stop the WebCamTexture: it keeps "playing" and on Windows can start serving
        /// another device's frames (e.g. the OBS placeholder). Watch the device list instead and reopen on replug.
        /// </summary>
        void CheckConnected()
        {
            nextDeviceCheck = Time.unscaledTime + 1f;
            var present = WebCamTexture.devices.Any(d => d.name == DeviceName);
            if (!present && !disconnected)
            {
                disconnected = true;
                webcam.Stop();
                Status = $"{DeviceName} disconnected: plug it back in";
                Debug.LogWarning("[Camera] " + Status);
            }
            else if (present && disconnected)
            {
                disconnected = false;
                Destroy(webcam);
                webcam = null;
                Debug.Log($"[Camera] {DeviceName} is back; reopening.");
                Open();
            }
        }

        /// <summary>The whiteboard camera is a USB webcam: skip the laptop's own camera and virtual ones (OBS etc.).</summary>
        static bool IsExternal(WebCamDevice d)
        {
            var name = d.name.ToLowerInvariant();
            return !name.Contains("integrated") && !name.Contains("virtual") && !name.Contains("built-in");
        }

        void RefreshPreview()
        {
            var pixels = Grab(out var w, out var h);
            if (pixels == null) return;
            if (preview == null || preview.width != w || preview.height != h)
            {
                if (preview != null) Destroy(preview);
                preview = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "BoardPreview", wrapMode = TextureWrapMode.Clamp };
            }
            preview.SetPixels32(pixels);
            preview.Apply(false);
        }

        /// <summary>
        /// Copies the current frame upright (rotation and mirroring undone): board space, origin bottom-left.
        /// Null unless a fresh frame is available.
        /// </summary>
        public Color32[] Grab(out int width, out int height)
        {
            width = height = 0;
            if (!HasFreshFrame) return null;
            var w = webcam.width;
            var h = webcam.height;
            if (raw == null || raw.Length != w * h) raw = new Color32[w * h];
            webcam.GetPixels32(raw);
            return Upright(raw, w, h, webcam.videoRotationAngle, webcam.videoVerticallyMirrored, out width, out height);
        }

        static Color32[] Upright(Color32[] src, int w, int h, int rotation, bool mirrored, out int outW, out int outH)
        {
            var quarterTurns = ((rotation % 360 + 360) % 360) / 90;
            outW = quarterTurns % 2 == 0 ? w : h;
            outH = quarterTurns % 2 == 0 ? h : w;
            var dst = new Color32[src.Length];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var sy = mirrored ? h - 1 - y : y;
                int dx, dy;
                switch (quarterTurns)
                {
                    case 1: dx = y; dy = w - 1 - x; break;
                    case 2: dx = w - 1 - x; dy = h - 1 - y; break;
                    case 3: dx = h - 1 - y; dy = x; break;
                    default: dx = x; dy = y; break;
                }
                dst[dy * outW + dx] = src[sy * w + x];
            }
            return dst;
        }
    }
}
