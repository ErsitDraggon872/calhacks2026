using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CalHacks
{
    public enum CreatorInput { Auto, Webcam, Strokes }

    /// <summary>
    /// The Creator's BUILD: capture the drawing, publish it as "pending" so the Explorer sees A is building, run the
    /// interpreter, then publish the physics JSON. Each drawing has a capture_id; publishing it again (a retry or a
    /// reconnect) never makes a second invention.
    ///
    /// Plug-in points: set <see cref="Capture"/> to the drawing source (the digital canvas, or the webcam by default
    /// when GameConfig/-capture=webcam says so) and <see cref="Interpreter"/> to the /interpret client.
    /// </summary>
    public class CreatorBuild : MonoBehaviour
    {
        public static CreatorBuild Instance { get; private set; }

        [SerializeField] GameConfig config;
        [Tooltip("Auto: the webcam when the capture source is Webcam, otherwise the synced strokes.")]
        [SerializeField] CreatorInput input = CreatorInput.Auto;
        [SerializeField] float interpretTimeout = 30f;
        [Tooltip("Start a fresh drawing once a BUILD is delivered (clears the strokes and board frame on both screens).")]
        [SerializeField] bool clearAfterBuild = true;

        readonly string sessionTag = Guid.NewGuid().ToString("N").Substring(0, 6);
        int drawingNumber = 1;
        string builtCaptureId;
        CancellationTokenSource buildCts;

        public IDrawingCapture Capture { get; set; }
        public IPhysicsInterpreter Interpreter { get; set; }

        /// <summary>The drawing in progress. Strokes, board frames and the BUILD all carry it.</summary>
        public string CaptureId => $"{sessionTag}-{drawingNumber}";
        public bool Building { get; private set; }
        public string LocalError { get; private set; }

        void Awake()
        {
            Instance = this;
            if (config == null) config = Resources.Load<GameConfig>("GameConfig");
            if (config == null) config = ScriptableObject.CreateInstance<GameConfig>();
        }

        void Start()
        {
            if (Capture == null) Capture = UsesWebcam ? new WebcamDrawingCapture(config, FindFirstObjectByType<BoardCamera>()) : new StrokeDrawingCapture(1024, 576);
            if (Interpreter == null) Interpreter = new SupportOnlyInterpreter();
            Debug.Log($"[Coop] Creator BUILD: capture={Capture.Name}, interpreter={Interpreter.Name}");
        }

        public bool UsesWebcam => input == CreatorInput.Webcam ||
                                  (input == CreatorInput.Auto && config.ResolveCaptureSource() == CaptureSource.Webcam);

        void OnEnable() => GameInput.Cast.performed += OnCast;

        void OnDisable() => GameInput.Cast.performed -= OnCast;

        void OnDestroy()
        {
            buildCts?.Cancel();
            if (Instance == this) Instance = null;
        }

        void OnCast(InputAction.CallbackContext _) => Build();

        /// <summary>BUILD status for the Creator's screen: interpreting, ready, spawned, activated, or an error.</summary>
        public string Status
        {
            get
            {
                if (LocalError != null) return "error: " + LocalError;
                if (Building) return "interpreting...";
                if (builtCaptureId == null) return "draw, then BUILD";
                var row = ThinkInkSession.Instance?.FindInvention(builtCaptureId);
                if (row == null) return "sending...";
                return row.Status == "failed" ? "failed: " + row.Message : row.Status + (row.Name != "" ? $" ({row.Name})" : "");
            }
        }

        public async void Build()
        {
            var session = ThinkInkSession.Instance;
            if (session == null || session.Role != CoopRole.Creator || Building) return;
            LocalError = null;

            var png = Capture.Capture(out var error);
            if (png == null)
            {
                LocalError = error;
                Subtitles.ShowNow(error);
                return;
            }
            var captureId = CaptureId;
            var image = Convert.ToBase64String(png);
            builtCaptureId = captureId;
            Building = true;
            session.PublishInvention(captureId, image, "", Vector2.zero);
            Debug.Log($"[Coop] BUILD {captureId}: {png.Length / 1024} KB, interpreting with {Interpreter.Name}");

            buildCts?.Cancel();
            buildCts = new CancellationTokenSource(TimeSpan.FromSeconds(interpretTimeout));
            string json;
            try
            {
                json = await Interpreter.InterpretAsync(png, captureId, buildCts.Token);
            }
            catch (Exception e)
            {
                if (!(e is OperationCanceledException)) Debug.LogException(e);
                var reason = e is OperationCanceledException ? "The interpreter took too long." : "The interpreter failed.";
                json = "{\"status\":\"error\",\"capture_id\":\"" + captureId + "\",\"message\":\"" + reason + "\",\"components\":[]}";
            }
            if (this == null) return;
            Building = false;
            if (string.IsNullOrEmpty(json))
            {
                LocalError = "The interpreter returned nothing.";
                return;
            }
            session.PublishInvention(captureId, image, json, Vector2.zero);
            if (clearAfterBuild) ClearDrawing();
            else drawingNumber++;
        }

        /// <summary>CLEAR: a new capture_id, and the strokes and board frame are cleared on both screens.</summary>
        public void ClearDrawing()
        {
            drawingNumber++;
            LocalError = null;
            ThinkInkSession.Instance?.ClearDrawing();
        }
    }
}
