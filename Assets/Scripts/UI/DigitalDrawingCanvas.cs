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
        static readonly Color32 White = new Color32(255, 255, 255, 255);
        static readonly Color32 Black = new Color32(0, 0, 0, 255);

        Color32[] pixels;
        Texture2D canvas;
        bool visible;
        bool dragging;
        Vector2Int lastPoint;
        RunManager run;
        GUIStyle titleStyle;
        GUIStyle subtitleStyle;
        GUIStyle helperStyle;
        GUIStyle sectionLabelStyle;
        GUIStyle footerStyle;
        GUIStyle promptStyle;
        GUIStyle promptPlaceholderStyle;
        GUIStyle primaryButtonStyle;
        GUIStyle secondaryButtonStyle;
        Texture2D panelTexture, accentTexture, neutralButton, neutralHover, accentHover;
        string intentText = ""; // The creator's own words often disambiguate rough sketches.
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
            intentText = ""; // Do not carry one invention's intent into the next drawing.
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

        void OnGUI()
        {
            if (!visible) return;
            EnsureCanvas();
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 24,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = Color.white }
                };
                subtitleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true,
                    normal = { textColor = new Color(0.88f, 0.93f, 0.98f, 1f) }
                };
                helperStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = true,
                    normal = { textColor = new Color(0.74f, 0.82f, 0.90f, 1f) }
                };
                sectionLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                };
                footerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = new Color(0.82f, 0.88f, 0.94f, 1f) }
                };
                promptStyle = new GUIStyle(GUI.skin.textArea)
                {
                    fontSize = 14,
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(10, 10, 8, 8)
                };
                promptPlaceholderStyle = new GUIStyle(promptStyle)
                {
                    normal = { textColor = new Color(0.55f, 0.58f, 0.64f, 1f) }
                };
                secondaryButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    fixedHeight = 42
                };
                primaryButtonStyle = new GUIStyle(secondaryButtonStyle);
                panelTexture = MakePixel(new Color(.065f, .11f, .18f, .98f));
                accentTexture = MakePixel(new Color(.2f, .85f, .68f));
                accentHover = MakePixel(new Color(.35f, .96f, .78f));
                neutralButton = MakePixel(new Color(.27f, .34f, .42f));
                neutralHover = MakePixel(new Color(.36f, .45f, .55f));
                primaryButtonStyle.normal.background=accentTexture;
                primaryButtonStyle.hover.background=accentHover;
                primaryButtonStyle.active.background=accentHover;
                primaryButtonStyle.normal.textColor=new Color(.04f,.13f,.17f);
                primaryButtonStyle.hover.textColor=new Color(.04f,.13f,.17f);
                secondaryButtonStyle.normal.background=neutralButton;
                secondaryButtonStyle.hover.background=neutralHover;
                secondaryButtonStyle.active.background=neutralHover;
                secondaryButtonStyle.normal.textColor=Color.white;
                secondaryButtonStyle.hover.textColor=Color.white;
            }

            GUI.depth = -100; // In front of the gameplay HUD.
            var overlayColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = overlayColor;

            var panelWidth = Mathf.Min(Screen.width - 32f, 1060f);
            var panelX = (Screen.width - panelWidth) * 0.5f;
            var topMargin = 18f;
            var panelHeight = Mathf.Min(Screen.height - 28f, 860f);
            var panelRect = new Rect(panelX, topMargin, panelWidth, panelHeight);
            GUI.DrawTexture(panelRect, panelTexture);
            GUI.DrawTexture(new Rect(panelRect.x, panelRect.y, 5f, panelRect.height), accentTexture);

            var innerX = panelRect.x + 18f;
            var innerW = panelRect.width - 36f;
            GUI.Label(new Rect(innerX, panelRect.y + 10f, innerW, 28f), "THINKINK     /     INVENTION STATION", titleStyle);
            GUI.Label(new Rect(innerX, panelRect.y + 39f, innerW, 28f),
                "The puzzle is fixed. The solution isn't.  Draw something to get across the gap.", subtitleStyle);

            var maxWidth = Mathf.Max(220f, innerW);
            var maxHeight = Mathf.Max(150f, Screen.height - 310f);
            var scale = Mathf.Min(maxWidth / CanvasWidth, maxHeight / CanvasHeight);
            var w = CanvasWidth * scale;
            var h = CanvasHeight * scale;
            var area = new Rect(panelRect.x + (panelRect.width - w) * 0.5f, panelRect.y + 78f, w, h);

            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(area.x - 5f, area.y - 5f, area.width + 10f, area.height + 10f), neutralButton);
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

            var promptY = area.yMax + 16f;
            GUI.Label(new Rect(innerX, promptY, innerW, 22f), "WHAT SHOULD IT DO?    (OPTIONAL)", sectionLabelStyle);
            GUI.Label(new Rect(innerX, promptY + 22f, innerW, 18f),
                "Try: 'A bird that carries me'  or  'A spring that launches me up-right'.", helperStyle);

            var promptRect = new Rect(innerX, promptY + 44f, innerW, 54f);
            intentText = GUI.TextArea(promptRect, intentText, 180, promptStyle);
            if (string.IsNullOrWhiteSpace(intentText))
            {
                GUI.Label(new Rect(promptRect.x + 12f, promptRect.y + 10f, promptRect.width - 24f, promptRect.height - 20f),
                    "Describe the intended action in one short sentence...",
                    promptPlaceholderStyle);
            }

            var footerY = promptRect.yMax + 14f;
            GUI.Label(new Rect(innerX, footerY, innerW * 0.55f, 42f),
                "LEFT-DRAG TO DRAW   ·   P TO CLOSE   ·   ENTER TO BUILD",
                footerStyle);

            var buttonWidth = 158f;
            var gap = 12f;
            var buildRect = new Rect(panelRect.xMax - 18f - buttonWidth, footerY, buttonWidth, 42f);
            var clearRect = new Rect(buildRect.x - gap - 100f, footerY, 100f, 42f);

            if (GUI.Button(clearRect, "Clear", secondaryButtonStyle)) Clear();

            if (GUI.Button(buildRect, "BRING TO LIFE", primaryButtonStyle))
            {
                if (run != null) run.Cast();
            }
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

        static Texture2D MakePixel(Color color)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0,0,color);
            t.Apply();
            return t;
        }

        void OnDestroy()
        {
            foreach (var t in new[]{canvas,panelTexture,accentTexture,neutralButton,neutralHover,accentHover})
                if (t != null) Destroy(t);
            canvas = null;
        }
    }
}
