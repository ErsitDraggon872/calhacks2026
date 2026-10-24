using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace CalHacks
{
    [Serializable]
    public class MockDrawing
    {
        [Tooltip("PNG name under Assets/Resources/MockDrawings/ (no extension).")]
        public string resourceName;
        [Tooltip("Label the mock 'provider' returns for this drawing.")]
        public string label;
    }

    /// <summary>Where board images come from. Interpreting them is a separate, replaceable step (IInterpreter).</summary>
    public enum CaptureSource
    {
        Mock,    // MockVisionClient: PNGs from Resources/MockDrawings, no hardware or service.
        Service, // HttpVisionClient: the Python service owns the camera, crops and labels.
        Webcam,  // WebcamVisionClient: Unity reads the webcam itself (BoardCamera) and cuts the ink out locally.
        Replay,  // WebcamVisionClient on saved board photos (ReplayFrameSource): live pipeline, no camera rig.
        DigitalBoard, // WebcamVisionClient on the SpacetimeDB browser whiteboard's strokes (DigitalBoardFrameSource).
    }

    [CreateAssetMenu(menuName = "Game/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Header("Vision")]
        [Tooltip("Where board images come from. A build can override this with -capture=mock|service|webcam|replay|digital.")]
        [FormerlySerializedAs("visionMode")]
        public CaptureSource captureSource = CaptureSource.Mock;
        public string serviceUrl = "http://localhost:8000";
        [Tooltip("Seconds before POST /capture gives up.")]
        public int captureTimeout = 10;
        [Tooltip("Seconds before GET /label/{id} (held open by the service) gives up.")]
        public int labelTimeout = 20;
        [Tooltip("Seconds between GET /health polls while the debug overlay is open.")]
        public float healthPollInterval = 3f;

        [Header("Interpreter")]
        [Tooltip("The /interpret backend (tools/thinkink_backend.py) for Webcam and Replay captures. Empty: no AI, every " +
                 "drawing is \"unknown\". A build can override with -interpret=<url> or -interpret=none.")]
        public string interpretUrl = "http://127.0.0.1:8000";
        [Tooltip("Seconds before POST /interpret gives up. Longer than the backend's own model timeout (30 s).")]
        public int interpretTimeout = 35;
        [Tooltip("Folder of whiteboard photos (.png) for the Replay capture source. Empty: StreamingAssets/Replay. " +
                 "A build can override with -replay=<folder>.")]
        public string replayFolder = "";

        [Header("Display")]
        [Tooltip("Frame rate cap (Application.targetFrameRate). Uncapped, a maximized Game view or fullscreen build " +
                 "pins the machine and starves the webcam, which then stalls and blocks the main thread for seconds.")]
        public int targetFrameRate = 60;

        [Header("Webcam")]
        [Tooltip("WebCamTexture device name. Empty: the first USB webcam (never the integrated or a virtual camera). " +
                 "A build can override with -camera=<name>.")]
        public string cameraDevice = "";
        public int cameraWidth = 1280;
        public int cameraHeight = 720;
        [Tooltip("Part of the frame that is the whiteboard, normalized (0-1, origin bottom-left).")]
        public Rect boardCrop = new Rect(0f, 0f, 1f, 1f);
        [Tooltip("A pixel is ink when its brightness is below the local board brightness times this.")]
        [Range(0.1f, 0.95f)] public float inkThreshold = 0.6f;
        [Tooltip("Fewer ink pixels than this counts as an empty board.")]
        public int minInkPx = 400;

        [Header("Gameplay")]
        [Tooltip("Seconds after a build before BUILD is ready again.")]
        public float buildCooldown = 3f;
        [Tooltip("CAST binds to the nearest unsolved socket within this horizontal distance of the explorer.")]
        public float castRange = 5f;
        [Tooltip("Falling below this height respawns the explorer at the last checkpoint.")]
        public float killY = -5f;
        [Tooltip("Metres the explorer rises on Space.")]
        public float jumpHeight = 1.2f;

        [Header("Inventions")]
        [Tooltip("Box (width, height in metres) every drawing is fitted into, keeping its aspect. " +
                 "How big it was drawn on the board doesn't matter.")]
        public Vector2 inventionSize = new Vector2(3f, 2.5f);
        [Tooltip("How far an invention's collider reaches along the view (Z), centred on the drawing.")]
        public float inventionDepth = 2f;
        [Tooltip("Width in metres of the columns an invention's silhouette is rebuilt from as box colliders. " +
                 "Their tops and bottoms are exact to the pixel.")]
        public float inventionCell = 0.15f;

        [Header("Mock vision")]
        public float mockCaptureDelay = 0.1f;
        public float mockLabelDelay = 1f;
        [Tooltip("Cycled in order on each CAST. Keys 1-5 pick the next one; 0 makes the next CAST an empty board.")]
        public MockDrawing[] mockDrawings =
        {
            new MockDrawing { resourceName = "bridge", label = "bridge" },
            new MockDrawing { resourceName = "sun", label = "light" },
            new MockDrawing { resourceName = "fish", label = "fish" },
            new MockDrawing { resourceName = "cat", label = "cat" },
            new MockDrawing { resourceName = "scribble", label = "unknown" },
        };

        /// <summary>The capture source, honouring -capture=... (and the older -vision=mock|http).</summary>
        public CaptureSource ResolveCaptureSource()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                var a = arg.ToLowerInvariant();
                if (a == "-capture=mock" || a == "-vision=mock") return CaptureSource.Mock;
                if (a == "-capture=service" || a == "-vision=http") return CaptureSource.Service;
                if (a == "-capture=webcam") return CaptureSource.Webcam;
                if (a == "-capture=replay") return CaptureSource.Replay;
                if (a == "-capture=digital") return CaptureSource.DigitalBoard;
            }
            return captureSource;
        }

        public string ResolveCameraDevice() => Argument("-camera=") ?? cameraDevice;

        /// <summary>The /interpret base URL, honouring -interpret=...; empty means no interpreter.</summary>
        public string ResolveInterpretUrl()
        {
            var url = Argument("-interpret=") ?? interpretUrl;
            return string.Equals(url, "none", StringComparison.OrdinalIgnoreCase) ? "" : url?.Trim() ?? "";
        }

        public string ResolveReplayFolder()
        {
            var folder = Argument("-replay=") ?? replayFolder;
            return string.IsNullOrWhiteSpace(folder) ? System.IO.Path.Combine(Application.streamingAssetsPath, "Replay") : folder;
        }

        static string Argument(string prefix)
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return arg.Substring(prefix.Length);
            }
            return null;
        }
    }
}
