using UnityEngine;

namespace CalHacks
{
    /// <summary>P overlay: the live whiteboard webcam with the board crop outlined, for setting up the camera.</summary>
    public class CameraPreview : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;

        [SerializeField] GameConfig config;

        GUIStyle style;
        Texture2D panel, line;

        void Awake()
        {
            if (config == null) config = Resources.Load<GameConfig>("GameConfig");
            if (config == null) config = ScriptableObject.CreateInstance<GameConfig>();
        }

        void OnGUI()
        {
            var run = RunManager.Instance;
            if (run == null || !run.PreviewVisible)
            {
                // The co-op panel shows the webcam in place of the browser whiteboard while M has switched to it.
                if (BoardCamera.Instance != null) BoardCamera.Instance.PreviewWanted = run != null && run.UsingWebcam;
                return;
            }
            EnsureStyles();
            var scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var width = Screen.width / scale;

            var cam = BoardCamera.Instance;
            if (cam != null) cam.PreviewWanted = true;
            var frame = cam != null && cam.HasFreshFrame ? cam.Preview : null;
            var feedW = 1100f;
            var feedH = frame != null ? feedW * frame.height / frame.width : feedW * 9f / 16f;
            var outer = new Rect((width - feedW) * 0.5f - 20f, 100f, feedW + 40f, feedH + 90f);
            GUI.DrawTexture(outer, panel);
            var feed = new Rect(outer.x + 20f, outer.y + 20f, feedW, feedH);

            if (frame != null)
            {
                // The exact upright pixels a capture uses (board space, origin bottom-left), so the crop outline matches.
                GUI.DrawTexture(feed, frame);
                var c = config.boardCrop;
                Outline(new Rect(feed.x + c.x * feed.width, feed.y + (1f - c.yMax) * feed.height, c.width * feed.width, c.height * feed.height), 3f);
            }
            else
            {
                GUI.Label(feed, "No camera feed", style);
            }

            var status = cam != null ? cam.Status : "BoardCamera missing from the scene";
            GUI.Label(new Rect(feed.x, feed.yMax + 10f, feed.width, 50f), $"Camera: {status}   ·   P to close", style);
            GUI.matrix = Matrix4x4.identity;
        }

        void Outline(Rect r, float t)
        {
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), line);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), line);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), line);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), line);
        }

        void EnsureStyles()
        {
            if (style != null) return;
            style = Sketch.HoldColor(new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
            panel = new Texture2D(1, 1);
            panel.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.85f));
            panel.Apply();
            line = new Texture2D(1, 1);
            line.SetPixel(0, 0, new Color(1f, 0.6f, 0.1f, 0.9f));
            line.Apply();
        }

        void OnDestroy()
        {
            if (panel != null) Destroy(panel);
            if (line != null) Destroy(line);
        }
    }
}
