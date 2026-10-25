using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// A speech bubble over a character's head. Says <see cref="idleLine"/> while B is within <see cref="range"/> (until
    /// <see cref="socket"/> is solved); <see cref="Say"/> shows a line for a while wherever B is.
    /// </summary>
    public class SpeechBubble : MonoBehaviour
    {
        const float ReferenceHeight = 1080f;
        const float MaxWidth = 520f;

        [TextArea] [SerializeField] string idleLine = "";
        [Tooltip("How close (m, on the ground plane) B must be to hear the idle line.")]
        [SerializeField] float range = 4.5f;
        [Tooltip("Height (m) above this transform the bubble's tail points at.")]
        [SerializeField] float headHeight = 2.4f;
        [Tooltip("The idle line stops once this socket is solved. Optional.")]
        [SerializeField] PuzzleSocket socket;

        string line;
        float lineUntil;
        Transform player;
        GUIStyle style;
        Texture2D tailTexture;

        public void Say(string text, float seconds = 3.5f)
        {
            if (string.IsNullOrEmpty(text)) return;
            Debug.Log("[Bubble] " + text);
            line = text;
            lineUntil = Time.unscaledTime + seconds;
        }

        public void Clear() => line = null;

        string Current()
        {
            if (line != null && Time.unscaledTime < lineUntil) return line;
            if (string.IsNullOrEmpty(idleLine) || (socket != null && socket.Solved)) return null;
            if (player == null)
            {
                var go = GameObject.FindWithTag("Player");
                if (go == null) return null;
                player = go.transform;
            }
            var offset = player.position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= range * range ? idleLine : null;
        }

        void OnGUI()
        {
            var text = Current();
            var cam = Camera.main;
            if (text == null || cam == null) return;
            var screen = cam.WorldToScreenPoint(transform.position + Vector3.up * headHeight);
            if (screen.z <= 0f) return;
            EnsureStyles();
            // Behind the HUD (the co-op panel draws over it rather than the bubble dodging around the panel).
            GUI.depth = 10;
            var scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var width = Screen.width / scale;
            // IMGUI's y runs down from the top; WorldToScreenPoint's runs up from the bottom.
            var tip = new Vector2(screen.x / scale, ReferenceHeight - screen.y / scale);

            var content = new GUIContent(text);
            // Measured as drawn, plus 2 px for rounding, so a line that fits on one row stays on one row.
            var boxWidth = Mathf.Min(MaxWidth, Mathf.Ceil(Sketch.Measure(style, content).x) + 42f);
            var boxHeight = Sketch.Measure(style, content, boxWidth - 40f).y + 28f;
            const float tail = 26f;
            var rect = new Rect(tip.x - boxWidth * 0.5f, tip.y - tail - boxHeight, boxWidth, boxHeight);
            rect.x = Mathf.Clamp(rect.x, 10f, width - boxWidth - 10f);
            rect.y = Mathf.Max(rect.y, 90f);

            // Paper note like the top sign; the tail is drawn over the note's bottom outline so the two read as one shape.
            Sketch.Box(rect, Sketch.Paper, 21, shadow: true);
            GUI.DrawTexture(new Rect(tip.x - tail * 0.5f, rect.yMax - 4f, tail, tail + 4f), tailTexture);
            Sketch.Label(new Rect(rect.x + 20f, rect.y + 14f, rect.width - 40f, rect.height - 28f), text, style);
            GUI.matrix = Matrix4x4.identity;
        }

        void EnsureStyles()
        {
            if (style != null) return;
            style = Sketch.Text(28, Sketch.Ink, TextAnchor.MiddleCenter);
            style.wordWrap = true;
            tailTexture = Tail(26, 30);
        }

        /// <summary>The bubble's tail: a paper wedge pointing down, inked down both sides and open at the top.</summary>
        static Texture2D Tail(int width, int height)
        {
            var texture = new Texture2D(width, height) { wrapMode = TextureWrapMode.Clamp };
            var half = width * 0.5f;
            // The slanted sides' unit normal, for the pixel distance to each side.
            var length = Mathf.Sqrt(half * half + height * height);
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                // Texture rows go bottom-up: the point is at row 0, the full width at the top.
                var px = Mathf.Abs(x + 0.5f - half);
                var py = y + 0.5f;
                var side = (px * height - py * half) / length;
                var paper = Mathf.Clamp01(0.5f - side);
                var ink = Mathf.Clamp01(2f - Mathf.Abs(side)) * (y < height - 4 ? 1f : 0f);
                var c = Color.Lerp(Sketch.Paper, Sketch.Ink, ink);
                c.a = Mathf.Max(paper, ink);
                texture.SetPixel(x, y, c);
            }
            texture.Apply();
            return texture;
        }

        void OnDestroy()
        {
            if (tailTexture != null) Destroy(tailTexture);
        }
    }
}
