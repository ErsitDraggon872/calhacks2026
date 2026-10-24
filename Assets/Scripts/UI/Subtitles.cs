using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>Queued bottom-of-screen subtitles. Every line is also logged so it shows in the console.</summary>
    public class Subtitles : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;

        static Subtitles instance;

        readonly Queue<(string text, float seconds)> queue = new Queue<(string, float)>();
        string current;
        float currentUntil;
        GUIStyle style;
        GUIStyle boxStyle;
        Texture2D boxTexture;

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
            if (boxTexture != null) Destroy(boxTexture);
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
            var maxWidth = Mathf.Min(1400f, width - 80f);
            var height = style.CalcHeight(content, maxWidth - 60f) + 36f;
            var size = style.CalcSize(content);
            var boxWidth = Mathf.Min(maxWidth, size.x + 60f);
            var rect = new Rect((width - boxWidth) * 0.5f, ReferenceHeight - height - 70f, boxWidth, height);
            GUI.Box(rect, GUIContent.none, boxStyle);
            GUI.Label(new Rect(rect.x + 30f, rect.y + 18f, rect.width - 60f, rect.height - 36f), content, style);
            GUI.matrix = Matrix4x4.identity;
        }

        void EnsureStyles()
        {
            if (style != null) return;
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 44,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = Color.white },
            };
            boxTexture = new Texture2D(1, 1);
            boxTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.7f));
            boxTexture.Apply();
            boxStyle = new GUIStyle { normal = { background = boxTexture } };
        }
    }
}
