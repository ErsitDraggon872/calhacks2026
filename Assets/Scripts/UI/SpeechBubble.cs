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
        GUIStyle style, boxStyle;
        Texture2D boxTexture, tailTexture;

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
            var scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var width = Screen.width / scale;
            // IMGUI's y runs down from the top; WorldToScreenPoint's runs up from the bottom.
            var tip = new Vector2(screen.x / scale, ReferenceHeight - screen.y / scale);

            var content = new GUIContent(text);
            var boxWidth = Mathf.Min(MaxWidth, style.CalcSize(content).x + 40f);
            var boxHeight = style.CalcHeight(content, boxWidth - 40f) + 28f;
            const float tail = 22f;
            var rect = new Rect(tip.x - boxWidth * 0.5f, tip.y - tail - boxHeight, boxWidth, boxHeight);
            rect.x = Mathf.Clamp(rect.x, 10f, width - boxWidth - 10f);
            rect.y = Mathf.Max(rect.y, 90f);

            GUI.Box(rect, GUIContent.none, boxStyle);
            GUI.DrawTexture(new Rect(tip.x - tail * 0.5f, rect.yMax, tail, tail), tailTexture);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 14f, rect.width - 40f, rect.height - 28f), content, style);
            GUI.matrix = Matrix4x4.identity;
        }

        void EnsureStyles()
        {
            if (style != null) return;
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = new Color(0.08f, 0.08f, 0.08f) },
            };
            boxTexture = new Texture2D(1, 1);
            boxTexture.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.95f));
            boxTexture.Apply();
            boxStyle = new GUIStyle { normal = { background = boxTexture } };
            tailTexture = Tail(32);
        }

        /// <summary>A white downward-pointing triangle, the bubble's tail.</summary>
        static Texture2D Tail(int size)
        {
            var texture = new Texture2D(size, size) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < size; y++)
            {
                // Texture rows go bottom-up: the point is at row 0, the full width at the top.
                var half = (y + 0.5f) / size * size * 0.5f;
                for (var x = 0; x < size; x++)
                {
                    var inside = Mathf.Abs(x + 0.5f - size * 0.5f) <= half;
                    texture.SetPixel(x, y, inside ? new Color(1f, 1f, 1f, 0.95f) : Color.clear);
                }
            }
            texture.Apply();
            return texture;
        }

        void OnDestroy()
        {
            if (boxTexture != null) Destroy(boxTexture);
            if (tailTexture != null) Destroy(tailTexture);
        }
    }
}
