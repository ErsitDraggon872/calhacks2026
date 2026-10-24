using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Black-marker-on-white drawing pad for rapid testing without a webcam.
    /// Press P to open/close, drag the left mouse button to draw, Clear to erase, Enter or BUILD to capture.
    /// The game stores the exact white canvas pixels; the vision client produces a separate transparent cutout.
    /// Created automatically by RunManager; selectable in the Game view alongside webcam input.
    /// </summary>
    public class DigitalDrawingCanvas : MonoBehaviour
    {
        const int CanvasWidth = 960;
        const int CanvasHeight = 540;
        const int BrushRadius = 4;
        const float ReferenceHeight = 1080f;

        static readonly Color32 White = new Color32(255, 255, 255, 255);
        static readonly Color32 Black = new Color32(0, 0, 0, 255);

        Color32[] pixels;
        Texture2D canvas;
        bool visible;
        bool dragging;
        Vector2Int lastPoint;
        RunManager run;

        GUIStyle brandStyle;
        GUIStyle eyebrowStyle;
        GUIStyle titleStyle;
        GUIStyle subtitleStyle;
        GUIStyle sectionStyle;
        GUIStyle bodyStyle;
        GUIStyle mutedStyle;
        GUIStyle placeholderStyle;
        GUIStyle inputStyle;
        GUIStyle primaryButtonStyle;
        GUIStyle secondaryButtonStyle;
        GUIStyle cardStyle;
        GUIStyle sideStyle;
        GUIStyle boardStyle;
        GUIStyle badgeStyle;

        Texture2D cardTex;
        Texture2D sideTex;
        Texture2D boardFrameTex;
        Texture2D inputTex;
        Texture2D badgeTex;
        Texture2D primaryTex;
        Texture2D primaryHoverTex;
        Texture2D secondaryTex;
        Texture2D secondaryHoverTex;
        Texture2D lineTex;

        string intentText = "";
        public string IntentHint => string.Empty;
        public string IntentMode => "auto";
        public string IntentText => (intentText ?? "").Trim();

        public bool IsOpen => visible;
        public int Width => CanvasWidth;
        public int Height => CanvasHeight;

        public void Initialize(RunManager owner)
        {
            run = owner;
            EnsureCanvas();
        }

        public Color32[] CopyBoardPixels()
        {
            EnsureCanvas();
            return (Color32[])pixels.Clone();
        }

        public void Open()
        {
            EnsureCanvas();
            visible = true;
            if (run != null) run.SetDigitalDrawingOpen(true);
        }

        public void Close()
        {
            visible = false;
            dragging = false;
            if (run != null) run.SetDigitalDrawingOpen(false);
        }

        public void Toggle()
        {
            if (visible) Close();
            else Open();
        }

        public void Clear()
        {
            EnsureCanvas();
            intentText = ""; // Do not reuse the previous invention's description.
            for (var i = 0; i < pixels.Length; i++) pixels[i] = White;
            Upload();
        }

        void EnsureCanvas()
        {
            if (canvas != null) return;
            pixels = new Color32[CanvasWidth * CanvasHeight];
            canvas = new Texture2D(CanvasWidth, CanvasHeight, TextureFormat.RGBA32, false)
            {
                name = "Digital whiteboard (black ink)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Clear();
        }

        void Upload()
        {
            canvas.SetPixels32(pixels);
            canvas.Apply(false);
        }

        // Reusable nine-slice rounded surfaces; no font assets or UI prefabs required.
        static Texture2D Rounded(Color color, int radius = 13)
        {
            const int size = 48;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "ThinkInk UI surface"
            };
            float center = size * 0.5f;
            float extent = center - radius;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - center) - extent, 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - center) - extent, 0f);
                float coverage = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                t.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * coverage));
            }
            t.Apply(false, true);
            return t;
        }

        static GUIStyle Surface(Texture2D background, int radius = 13)
        {
            return new GUIStyle(GUI.skin.box)
            {
                normal = { background = background },
                border = new RectOffset(radius + 1, radius + 1, radius + 1, radius + 1),
                padding = new RectOffset(0, 0, 0, 0)
            };
        }

        static GUIStyle Label(int size, Color color, bool bold = false)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                normal = { textColor = color }
            };
        }

        void EnsureStyles()
        {
            if (titleStyle != null) return;

            cardTex = Rounded(new Color(0.060f, 0.084f, 0.125f, 1f));
            sideTex = Rounded(new Color(0.095f, 0.129f, 0.175f, 1f));
            boardFrameTex = Rounded(new Color(0.18f, 0.235f, 0.295f, 1f), 9);
            inputTex = Rounded(new Color(0.053f, 0.080f, 0.119f, 1f), 11);
            badgeTex = Rounded(new Color(0.11f, 0.235f, 0.29f, 1f), 9);
            primaryTex = Rounded(new Color(0.20f, 0.69f, 0.93f, 1f));
            primaryHoverTex = Rounded(new Color(0.31f, 0.78f, 1f, 1f));
            secondaryTex = Rounded(new Color(0.17f, 0.225f, 0.285f, 1f));
            secondaryHoverTex = Rounded(new Color(0.23f, 0.29f, 0.36f, 1f));
            lineTex = Rounded(new Color(0.34f, 0.42f, 0.53f, 0.54f), 2);

            cardStyle = Surface(cardTex);
            sideStyle = Surface(sideTex);
            boardStyle = Surface(boardFrameTex, 9);
            badgeStyle = Surface(badgeTex, 9);

            brandStyle = Label(22, new Color(0.39f, 0.86f, 0.99f, 1f), true);
            eyebrowStyle = Label(14, new Color(0.64f, 0.76f, 0.87f, 1f), true);
            titleStyle = Label(40, Color.white, true);
            subtitleStyle = Label(18, new Color(0.75f, 0.82f, 0.89f, 1f));
            sectionStyle = Label(20, Color.white, true);
            bodyStyle = Label(17, new Color(0.90f, 0.94f, 0.98f, 1f));
            mutedStyle = Label(15, new Color(0.69f, 0.78f, 0.86f, 1f));
            placeholderStyle = Label(17, new Color(0.47f, 0.59f, 0.69f, 1f));

            inputStyle = new GUIStyle(GUI.skin.textArea)
            {
                fontSize = 18,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(13, 13, 11, 11),
                border = new RectOffset(11, 11, 11, 11),
                normal = { background = inputTex, textColor = Color.white },
                focused = { background = inputTex, textColor = Color.white },
                hover = { background = inputTex, textColor = Color.white }
            };

            primaryButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(14, 14, 14, 14),
                normal = { background = primaryTex, textColor = new Color(0.02f, 0.10f, 0.16f, 1f) },
                hover = { background = primaryHoverTex, textColor = new Color(0.02f, 0.10f, 0.16f, 1f) },
                active = { background = primaryHoverTex, textColor = new Color(0.02f, 0.10f, 0.16f, 1f) },
                onNormal = { background = primaryTex, textColor = new Color(0.02f, 0.10f, 0.16f, 1f) }
            };
            secondaryButtonStyle = new GUIStyle(primaryButtonStyle)
            {
                fontSize = 16,
                normal = { background = secondaryTex, textColor = Color.white },
                hover = { background = secondaryHoverTex, textColor = Color.white },
                active = { background = secondaryHoverTex, textColor = Color.white }
            };
        }

        void DrawSurface(Rect rect, GUIStyle style)
        {
            GUI.Box(rect, GUIContent.none, style);
        }

        void OnGUI()
        {
            if (!visible) return;
            EnsureCanvas();
            EnsureStyles();

            // Scale to the Game view, even at Unity's 2x preview zoom.
            float scale = Mathf.Min(Screen.height / 1080f, Screen.width / 1500f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float vw = Screen.width / scale;
            float vh = Screen.height / scale;
            GUI.depth = -1000;

            // Opaque workspace is deliberate: gameplay UI and scenery no longer show through.
            var oldColor = GUI.color;
            GUI.color = new Color(0.025f, 0.035f, 0.055f, 0.94f);
            GUI.DrawTexture(new Rect(0, 0, vw, vh), Texture2D.whiteTexture);
            GUI.color = oldColor;

            float cardW = Mathf.Min(1510f, vw - 72f);
            float cardH = Mathf.Min(900f, vh - 70f);
            var card = new Rect((vw - cardW) * 0.5f, (vh - cardH) * 0.5f, cardW, cardH);
            DrawSurface(card, cardStyle);

            float x = card.x + 37f;
            float right = card.xMax - 37f;
            float top = card.y + 30f;
            float insideW = card.width - 74f;

            GUI.Label(new Rect(x, top, 155f, 30f), "thinkink.", brandStyle);
            DrawSurface(new Rect(x + 172f, top + 1f, 166f, 29f), badgeStyle);
            GUI.Label(new Rect(x + 186f, top + 2f, 145f, 27f), "CREATOR STUDIO", eyebrowStyle);
            GUI.Label(new Rect(x, top + 44f, insideW - 200f, 54f), "Draw a way across.", titleStyle);
            GUI.Label(new Rect(x, top + 101f, insideW - 220f, 38f),
                "Sketch an invention. Give it a purpose. Watch your teammate bring it to life.", subtitleStyle);
            GUI.Label(new Rect(right - 183f, top + 7f, 183f, 26f), "P  /  CLOSE", eyebrowStyle);
            GUI.DrawTexture(new Rect(x, top + 150f, insideW, 2f), lineTex);

            // Two columns: the drawing is the hero; everything else stays in a tidy side panel.
            float contentY = top + 167f;
            float sideW = 340f;
            float gap = 28f;
            float leftW = insideW - gap - sideW;
            float sideX = x + leftW + gap;
            GUI.Label(new Rect(x + 4f, contentY, 310f, 28f), "01    YOUR SKETCH", eyebrowStyle);
            GUI.Label(new Rect(x + leftW - 120f, contentY + 1f, 116f, 27f), "BLACK INK", eyebrowStyle);

            // Preserve the native 16:9 board aspect ratio for accurate mouse -> PNG coordinates.
            float boardPadding = 10f;
            float boardW = leftW - boardPadding * 2f;
            float boardH = boardW * CanvasHeight / CanvasWidth;
            var frame = new Rect(x, contentY + 37f, leftW, boardH + boardPadding * 2f);
            DrawSurface(frame, boardStyle);
            var area = new Rect(frame.x + boardPadding, frame.y + boardPadding, boardW, boardH);
            GUI.DrawTexture(area, canvas, ScaleMode.StretchToFill);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && area.Contains(e.mousePosition))
            {
                dragging = true;
                lastPoint = ToPixel(e.mousePosition, area);
                Stamp(lastPoint.x, lastPoint.y);
                Upload();
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && dragging && e.button == 0)
            {
                var next = ToPixel(e.mousePosition, area);
                DrawStroke(lastPoint, next);
                lastPoint = next;
                Upload();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                dragging = false;
            }
            GUI.Label(new Rect(x + 4f, frame.yMax + 12f, leftW - 8f, 36f),
                "Hold left mouse to draw  /  Black-and-white sketch  /  No art skills required", mutedStyle);

            // Sidebar: optional natural-language intent, not an ability-category picker.
            var side = new Rect(sideX, contentY + 3f, sideW, card.yMax - (contentY + 3f) - 28f);
            DrawSurface(side, sideStyle);
            float sx = side.x + 22f;
            float sw = side.width - 44f;
            float sy = side.y + 23f;
            GUI.Label(new Rect(sx, sy, sw, 30f), "02    YOUR IDEA", eyebrowStyle);
            GUI.Label(new Rect(sx, sy + 42f, sw, 33f), "What should it do?", sectionStyle);
            GUI.Label(new Rect(sx, sy + 76f, sw, 38f), "Optional, but helpful for rough drawings.", mutedStyle);

            var textRect = new Rect(sx, sy + 128f, sw, 106f);
            // GUI.TextArea is kept so the backend receives the user's full intention.
            GUI.SetNextControlName("ThinkInkIntentText");
            intentText = GUI.TextArea(textRect, intentText, 180, inputStyle);
            if (string.IsNullOrWhiteSpace(intentText) &&
                GUI.GetNameOfFocusedControl() != "ThinkInkIntentText")
            {
                GUI.Label(new Rect(textRect.x + 13f, textRect.y + 12f, textRect.width - 26f, 66f),
                    "e.g. A bird that carries me across the gap", placeholderStyle);
            }

            GUI.Label(new Rect(sx, sy + 253f, sw, 21f), "SOME IDEAS", eyebrowStyle);
            GUI.Label(new Rect(sx, sy + 279f, sw, 90f),
                "A rocket that launches up-right\nA spring that throws me across\nA giant bird that carries me", bodyStyle);

            GUI.DrawTexture(new Rect(sx, sy + 393f, sw, 2f), lineTex);
            GUI.Label(new Rect(sx, sy + 410f, sw, 25f), "03    MAKE IT REAL", eyebrowStyle);
            GUI.Label(new Rect(sx, sy + 448f, sw, 58f),
                "DeerAI interprets your sketch; Unity brings its physics to life.", mutedStyle);

            // Primary CTA remains obvious. No category buttons, no extra form fields.
            bool busy = run != null && run.State.InFlight;
            float buttonY = side.yMax - 138f;
            var buildRect = new Rect(sx, buttonY, sw, 60f);
            var clearRect = new Rect(sx, buttonY + 71f, sw, 44f);
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !busy;
            if (GUI.Button(buildRect, busy ? "BRINGING TO LIFE..." : "BRING TO LIFE", primaryButtonStyle))
            {
                if (run != null) run.Cast();
            }
            GUI.enabled = wasEnabled;
            if (GUI.Button(clearRect, "Clear drawing", secondaryButtonStyle)) Clear();

            GUI.matrix = Matrix4x4.identity;
        }

        Vector2Int ToPixel(Vector2 mouse, Rect area)
        {
            var x = Mathf.Clamp(Mathf.FloorToInt((mouse.x - area.x) * CanvasWidth / area.width), 0, CanvasWidth - 1);
            var y = Mathf.Clamp(Mathf.FloorToInt((1f - (mouse.y - area.y) / area.height) * CanvasHeight), 0, CanvasHeight - 1);
            return new Vector2Int(x, y);
        }

        void DrawStroke(Vector2Int a, Vector2Int b)
        {
            var steps = Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
            if (steps == 0) { Stamp(a.x, a.y); return; }
            for (var i = 0; i <= steps; i++)
            {
                var t = (float)i / steps;
                Stamp(Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t)),
                    Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t)));
            }
        }

        void Stamp(int x, int y)
        {
            for (var dy = -BrushRadius; dy <= BrushRadius; dy++)
            for (var dx = -BrushRadius; dx <= BrushRadius; dx++)
            {
                if (dx * dx + dy * dy > BrushRadius * BrushRadius) continue;
                var px = x + dx;
                var py = y + dy;
                if (px >= 0 && px < CanvasWidth && py >= 0 && py < CanvasHeight)
                    pixels[py * CanvasWidth + px] = Black;
            }
        }

        void OnDestroy()
        {
            foreach (var tex in new[]
                     { canvas, cardTex, sideTex, boardFrameTex, inputTex, badgeTex, primaryTex,
                       primaryHoverTex, secondaryTex, secondaryHoverTex, lineTex })
            {
                if (tex != null) Destroy(tex);
            }
            canvas = null;
        }
    }
}
