using SpacetimeDB.Types;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// The room's whiteboard as a small texture: synced strokes (digital canvas) drawn as they arrive, or the latest
    /// board frame (physical whiteboard). <see cref="CoopHud"/> shows it; anything else can read <see cref="Texture"/>.
    /// </summary>
    public class WhiteboardPreview : MonoBehaviour
    {
        public static WhiteboardPreview Instance { get; private set; }

        [SerializeField] int width = 384;
        [Tooltip("Height / width for stroke drawings. Physical-whiteboard frames bring their own aspect.")]
        [SerializeField] float strokeAspect = 9f / 16f;

        StrokeRaster raster;
        ThinkInkSession session;
        Texture2D decoded;

        public Texture2D Texture => raster?.Apply();
        public bool HasInk { get; private set; }

        void Awake()
        {
            Instance = this;
            raster = new StrokeRaster(width, Mathf.RoundToInt(width * strokeAspect), Color.white, Color.black, 2);
        }

        void Update()
        {
            if (session != null || ThinkInkSession.Instance == null) return;
            session = ThinkInkSession.Instance;
            session.StrokeAdded += OnStroke;
            session.BoardFrameChanged += OnFrame;
            session.DrawingCleared += Rebuild;
            Rebuild();
        }

        void OnDestroy()
        {
            if (session != null)
            {
                session.StrokeAdded -= OnStroke;
                session.BoardFrameChanged -= OnFrame;
                session.DrawingCleared -= Rebuild;
            }
            raster?.Dispose();
            if (decoded != null) Destroy(decoded);
            if (Instance == this) Instance = null;
        }

        void OnStroke(DrawingStroke stroke)
        {
            raster.Draw(stroke.Points);
            HasInk = true;
        }

        void OnFrame(BoardFrame frame)
        {
            if (decoded == null) decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!decoded.LoadImage(System.Convert.FromBase64String(frame.PngBase64))) return;
            raster.Show(decoded);
            HasInk = true;
        }

        /// <summary>Redraws from the client cache: after a clear, a reset, or joining a room.</summary>
        void Rebuild()
        {
            raster.Resize(width, Mathf.RoundToInt(width * strokeAspect));
            raster.Clear();
            HasInk = false;
            if (session == null) return;
            var frame = session.BoardFrame();
            if (frame != null) OnFrame(frame);
            foreach (var s in session.Strokes()) OnStroke(s);
        }
    }
}
