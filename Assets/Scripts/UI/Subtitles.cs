using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>Queued bottom-of-screen subtitles. Every line is also logged so it shows in the console.</summary>
    public class Subtitles : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;
        /// <summary>Hud's build bar: 420 px in from the right edge and 104 px up from the bottom.</summary>
        const float BuildBarInset = 420f, BuildBarTop = 104f;

        static Subtitles instance;

        readonly Queue<(string text, float seconds)> queue = new Queue<(string, float)>();
        string current;
        float currentUntil;
        GUIStyle style;

        public static string Current => instance != null ? instance.current : null;

        public static void Show(string text, float seconds = 3f)
        {
            if (string.IsNullOrEmpty(text)) return;
            Debug.Log("[Subtitle] " + text);
            if (instance == null) return;
            if (text == instance.current && Time.unscaledTime < instance.currentUntil) return;
            instance.queue.Enqueue((text, seconds));
        }

        /// <summary>Replaces whatever is showing or queued.</summary>
        public static void ShowNow(string text, float seconds = 3f)
        {
            Clear();
            Show(text, seconds);
        }

        public static void Clear()
        {
            if (instance == null) return;
            instance.queue.Clear();
            instance.current = null;
        }

        void Awake() => instance = this;

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void Update()
        {
            if (current != null && Time.unscaledTime < currentUntil) return;
            current = null;
            if (queue.Count == 0) return;
            var (text, seconds) = queue.Dequeue();
            // Catch up when lines pile up, so reactions never lag far behind the action.
            if (queue.Count > 1) seconds *= 0.5f;
            current = text;
            currentUntil = Time.unscaledTime + seconds;
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(current)) return;
            EnsureStyles();
            var scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var width = Screen.width / scale;
            var content = new GUIContent(current);
            // Stay clear of the build bar at the bottom right: narrow enough to sit beside it, or, on a narrow screen
            // where that leaves too little room, full width and lifted above it.
            var maxWidth = Mathf.Min(1100f, width - 2f * (BuildBarInset + 20f));
            var bottom = 70f;
            if (maxWidth < 560f)
            {
                maxWidth = Mathf.Min(1100f, width - 80f);
                bottom = BuildBarTop + 24f;
            }
            // The note fits its words in 20 px steps, so changing lines reuse Sketch's cached box textures. The extra 20 px
            // is slack: Sketch.Label re-rasterises at the screen's font size, which can run a little wider than measured.
            var boxWidth = Mathf.Min(maxWidth, Mathf.Ceil((style.CalcSize(content).x + 80f) / 20f) * 20f);
            var height = Mathf.Ceil((style.CalcHeight(content, boxWidth - 80f) + 32f) / 20f) * 20f;
            var rect = new Rect((width - boxWidth) * 0.5f, ReferenceHeight - height - bottom, boxWidth, height);
            Sketch.Tilted(rect.center, 0.6f, () =>
            {
                Sketch.Box(rect, Sketch.Paper, 41, shadow: true);
                Sketch.Tape(new Vector2(rect.center.x, rect.y + 1f), -3f, 90f, 22f);
                Sketch.Label(new Rect(rect.x + 20f, rect.y + 16f, rect.width - 40f, rect.height - 32f), current, style);
            });
            GUI.matrix = Matrix4x4.identity;
        }

        void EnsureStyles()
        {
            if (style != null) return;
            style = Sketch.Text(34, Sketch.Ink, TextAnchor.MiddleCenter);
            style.wordWrap = true;
        }
    }
}
