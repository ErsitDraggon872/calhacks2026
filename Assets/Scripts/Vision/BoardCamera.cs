using System.Linq;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Owns the whiteboard webcam when the capture source is Webcam. Opens it once and keeps it running so a BUILD
    /// grabs the current frame instantly. Everything downstream (preview, capture, ink cut-out) uses the same upright
    /// frame, so the preview shows the pixels a capture will use (a capture averages the last few to cancel grain).
    /// </summary>
    public class BoardCamera : MonoBehaviour, IBoardFrameSource
    {
        /// <summary>A frame older than this means the camera has stalled.</summary>
        const float FreshFrameSeconds = 1f;
        /// <summary>A capture averages this many of the latest frames (about 0.13 s at 30 fps) to cancel low-light grain.</summary>
        const int AveragedFrames = 4;

        public static BoardCamera Instance { get; private set; }

        [SerializeField] GameConfig config;

        WebCamTexture webcam;
        Color32[] raw;
        Color32[] latest;
        readonly FrameAverager recent = new FrameAverager(AveragedFrames);
        Texture2D preview;
        float nextDeviceCheck;
        float lastFrameTime = float.NegativeInfinity;
        bool disconnected;
        bool wanted;
        bool onlyInput;

        /// <summary>Captures come from the webcam right now: it's the only input, or M switched to it.</summary>
        bool ActiveInput => onlyInput || (RunManager.Instance != null && RunManager.Instance.UsingWebcam);

        public string DeviceName { get; private set; }
        public string Status { get; private set; } = "starting";
        /// <summary>Set by the preview overlay while it is open; the upright preview texture is only built then.</summary>
        public bool PreviewWanted { get; set; }
        /// <summary>The latest upright frame, while <see cref="PreviewWanted"/>. Null until one arrives.</summary>
        public Texture2D Preview => preview;
        /// <summary>The whiteboard inside each frame (GameConfig.boardCrop), normalized, origin bottom-left.</summary>
        public Rect Crop => config.boardCrop;

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
            // The Webcam source opens it at once; DigitalBoard opens it on the first M (RunManager.SwitchInput). With
            // Service the Python process owns the webcam (Windows usually lets only one process open it); with Mock
            // there is no hardware at all.
            var source = config.ResolveCaptureSource();
            onlyInput = source == CaptureSource.Webcam;
            if (source != CaptureSource.Webcam)
            {
                Status = $"off (capture source: {source})";
                return;
            }
            Activate();
        }

        /// <summary>Opens the webcam (and keeps looking for it until it's plugged in). It stays open from then on.</summary>
        public void Activate()
        {
            if (wanted) return;
            wanted = true;
            Open();
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
                // Copying a frame out costs a few ms, so only while a capture could use it: the webcam is the
                // input (its preview already pays for the copy). On the drawing pad, frames from before the switch
                // would be stale anyway.
                if (!ActiveInput) recent.Clear();
                if (ActiveInput || PreviewWanted)
                {
                    latest = Copy(latest, out var w, out var h);
                    if (ActiveInput) recent.Add(latest, w, h);
                    if (PreviewWanted) RefreshPreview(latest, w, h);
                }
            }

            Status = HasFreshFrame ? $"{DeviceName}  {webcam.width}x{webcam.height}"
                : float.IsNegativeInfinity(lastFrameTime) ? $"{DeviceName}  (waiting for the first frame)"
                : $"{DeviceName}  (no new frame for {Time.unscaledTime - lastFrameTime:0}s)";
        }

        public void Open()
        {
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
            recent.Clear();
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

        void RefreshPreview(Color32[] pixels, int w, int h)
        {
            if (preview == null || preview.width != w || preview.height != h)
            {
                if (preview != null) Destroy(preview);
                preview = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "BoardPreview", wrapMode = TextureWrapMode.Clamp };
            }
            preview.SetPixels32(pixels);
            preview.Apply(false);
        }

        /// <summary>
        /// The board for a capture: the last few frames averaged, upright (rotation and mirroring undone), board space,
        /// origin bottom-left. Null unless a fresh frame is available.
        /// </summary>
        public Color32[] Grab(out int width, out int height)
        {
            width = height = 0;
            if (!HasFreshFrame) return null;
            return recent.Count >= 2 ? recent.Average(out width, out height) : GrabLatest(out width, out height);
        }

        /// <summary>Just the current frame, upright: for the live previews, which want it sharp and cheap.</summary>
        public Color32[] GrabLatest(out int width, out int height)
        {
            width = height = 0;
            return HasFreshFrame ? Copy(null, out width, out height) : null;
        }

        /// <summary>The webcam's current frame, upright, into <paramref name="into"/> when it's the right size.</summary>
        Color32[] Copy(Color32[] into, out int width, out int height)
        {
            var w = webcam.width;
            var h = webcam.height;
            if (raw == null || raw.Length != w * h) raw = new Color32[w * h];
            webcam.GetPixels32(raw);
            if (into == null || into.Length != w * h) into = new Color32[w * h];
            Upright(raw, into, w, h, webcam.videoRotationAngle, webcam.videoVerticallyMirrored, out width, out height);
            return into;
        }

        static void Upright(Color32[] src, Color32[] dst, int w, int h, int rotation, bool mirrored, out int outW, out int outH)
        {
            var quarterTurns = ((rotation % 360 + 360) % 360) / 90;
            outW = quarterTurns % 2 == 0 ? w : h;
            outH = quarterTurns % 2 == 0 ? h : w;
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
        }
    }
}
