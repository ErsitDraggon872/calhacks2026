using System;
using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Hand-drawn IMGUI bits to match the paper cutouts: paper boxes with a wobbly marker outline, masking tape, tilted
    /// drawing and a handwriting font. Box textures are drawn once per size and shared by every HUD.
    /// </summary>
    public static class Sketch
    {
        public static readonly Color Paper = new Color(0.98f, 0.96f, 0.92f);
        public static readonly Color Ink = new Color(0.12f, 0.11f, 0.09f);
        public static readonly Color Muted = new Color(0.54f, 0.50f, 0.45f);
        public static readonly Color Marker = new Color(1f, 0.82f, 0.25f);
        public static readonly Color MarkerHover = new Color(1f, 0.66f, 0.2f);
        public static readonly Color Spent = new Color(0.82f, 0.80f, 0.76f);
        static readonly Color TapeColor = new Color(0.91f, 0.85f, 0.66f, 0.8f);
        static readonly Color Shadow = new Color(0f, 0f, 0f, 0.18f);

        /// <summary>Outline overshoot room around a box: the texture is this much bigger than the rect on every side.</summary>
        const int Margin = 7;
        const int MaxCached = 48;

        static readonly Dictionary<(int, int, Color, int), Texture2D> boxes = new Dictionary<(int, int, Color, int), Texture2D>();
        static Font hand;
        static bool handLoaded;

        /// <summary>Ink Free (Windows 10/11), else Segoe Print or Comic Sans; null (the default font) if none is installed.</summary>
        public static Font Hand
        {
            get
            {
                if (handLoaded) return hand;
                handLoaded = true;
                var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
                foreach (var name in new[] { "Ink Free", "Segoe Print", "Comic Sans MS" })
                {
                    if (!installed.Contains(name)) continue;
                    hand = Font.CreateDynamicFontFromOSFont(name, 32);
                    break;
                }
                return hand;
            }
        }

        public static GUIStyle Text(int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft, bool bold = false)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                font = Hand, fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, alignment = alignment,
                richText = true, wordWrap = false, clipping = TextClipping.Overflow,
            };
            Tint(style, color);
            return style;
        }

        /// <summary>
        /// Gives every state the normal text colour. A style copied from GUI.skin.label only sets <c>normal</c>, so under
        /// the mouse a label falls back to the skin's pale grey hover colour.
        /// </summary>
        public static GUIStyle HoldColor(GUIStyle style)
        {
            Tint(style, style.normal.textColor);
            return style;
        }

        static void Tint(GUIStyle style, Color color)
        {
            foreach (var state in new[] { style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused })
                state.textColor = color;
        }

        /// <summary>
        /// GUI.Label, but rendered at the on-screen size. The HUDs lay out at 1080p under a scaled GUI.matrix, which
        /// rasterises glyphs at the 1080p size and then resamples them, blurring text at any other resolution. This takes
        /// the scale back out of the matrix (keeping any tilt) and scales the rect and font size instead.
        /// </summary>
        public static void Label(Rect rect, string text, GUIStyle style) => Crisp(rect, style, r => GUI.Label(r, text, style));

        /// <summary>
        /// Text's size as <see cref="Label"/> draws it, in the current matrix's units: measured at the on-screen font size,
        /// which runs a little wider than the 1080p size, so a box fitted to a 1080p measure can wrap its last word.
        /// Height is wrapped to <paramref name="wrapWidth"/> when given.
        /// </summary>
        public static Vector2 Measure(GUIStyle style, GUIContent content, float wrapWidth = 0f)
        {
            var scale = GUI.matrix.GetColumn(0).magnitude;
            var size = style.fontSize;
            style.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * scale));
            try
            {
                var natural = style.CalcSize(content);
                var height = wrapWidth > 0f ? style.CalcHeight(content, wrapWidth * scale) : natural.y;
                return new Vector2(natural.x, height) / scale;
            }
            finally { style.fontSize = size; }
        }

        /// <summary>Runs <paramref name="draw"/> with the matrix scale moved into the rect and the style's font size.</summary>
        public static void Crisp(Rect rect, GUIStyle style, Action<Rect> draw)
        {
            var old = GUI.matrix;
            var scale = old.GetColumn(0).magnitude;
            if (Mathf.Approximately(scale, 1f))
            {
                draw(rect);
                return;
            }
            var size = style.fontSize;
            GUI.matrix = old * Matrix4x4.Scale(new Vector3(1f / scale, 1f / scale, 1f));
            style.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * scale));
            try { draw(new Rect(rect.x * scale, rect.y * scale, rect.width * scale, rect.height * scale)); }
            finally
            {
                style.fontSize = size;
                GUI.matrix = old;
            }
        }

        /// <summary>A paper box whose marker outline runs along <paramref name="rect"/>'s edge.</summary>
        public static void Box(Rect rect, Color fill, int seed, bool shadow = false)
        {
            var texture = BoxTexture(Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height), fill, seed);
            var outer = new Rect(rect.x - Margin, rect.y - Margin, rect.width + Margin * 2, rect.height + Margin * 2);
            var old = GUI.color;
            if (shadow)
            {
                GUI.color = Shadow;
                GUI.DrawTexture(new Rect(outer.x + 5f, outer.y + 6f, outer.width, outer.height), texture);
            }
            GUI.color = old;
            GUI.DrawTexture(outer, texture);
        }

        /// <summary>
        /// A paper button with a drop shadow. Under the mouse it turns marker yellow (a marker-yellow button turns orange)
        /// so it reads as clickable. False while GUI.enabled is off.
        /// </summary>
        public static bool Button(Rect rect, string label, Color fill, int seed, GUIStyle style)
        {
            var hover = GUI.enabled && rect.Contains(Event.current.mousePosition);
            Box(rect, hover ? (fill == Marker ? MarkerHover : Marker) : fill, seed, shadow: true);
            Label(rect, label, style);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        /// <summary>An underlined text link; under the mouse it goes ink-dark on a highlighter swipe.</summary>
        public static bool Link(Rect rect, string label, GUIStyle style)
        {
            var hover = GUI.enabled && rect.Contains(Event.current.mousePosition);
            var color = style.normal.textColor;
            var width = style.CalcSize(new GUIContent(label)).x;
            var x = style.alignment == TextAnchor.MiddleRight || style.alignment == TextAnchor.UpperRight ? rect.xMax - width : rect.x;
            var old = GUI.color;
            if (hover)
            {
                GUI.color = Marker;
                GUI.DrawTexture(new Rect(x - 5f, rect.center.y - style.fontSize * 0.6f, width + 10f, style.fontSize * 1.3f), Texture2D.whiteTexture);
                Tint(style, Ink);
            }
            Label(rect, label, style);
            Tint(style, color);
            GUI.color = hover ? Ink : color;
            GUI.DrawTexture(new Rect(x, rect.center.y + style.fontSize * 0.55f, width, 2f), Texture2D.whiteTexture);
            GUI.color = old;
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        /// <summary>A strip of masking tape centred on <paramref name="centre"/>.</summary>
        public static void Tape(Vector2 centre, float degrees, float width = 64f, float height = 20f)
        {
            Tilted(centre, degrees, () =>
            {
                var old = GUI.color;
                GUI.color = TapeColor;
                GUI.DrawTexture(new Rect(centre.x - width * 0.5f, centre.y - height * 0.5f, width, height), Texture2D.whiteTexture);
                GUI.color = old;
            });
        }

        /// <summary>Runs <paramref name="draw"/> rotated about <paramref name="pivot"/> (GUI space, under any scale).</summary>
        public static void Tilted(Vector2 pivot, float degrees, Action draw)
        {
            var old = GUI.matrix;
            var p = new Vector3(pivot.x, pivot.y, 0f);
            GUI.matrix = old * Matrix4x4.Translate(p) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, degrees)) * Matrix4x4.Translate(-p);
            try { draw(); }
            finally { GUI.matrix = old; }
        }

        static Texture2D BoxTexture(int w, int h, Color fill, int seed)
        {
            var key = (w, h, fill, seed);
            if (boxes.TryGetValue(key, out var cached) && cached != null) return cached;
            if (boxes.Count >= MaxCached) Flush();

            int tw = w + Margin * 2, th = h + Margin * 2;
            var random = new System.Random(seed);
            float Offset() => (float)random.NextDouble() * 100f;
            // Each edge wobbles ±2 px along its length, its line weight breathes, and it overshoots the corners a little.
            var bottom = Edge(tw, Margin, Offset());
            var top = Edge(tw, th - 1 - Margin, Offset());
            var left = Edge(th, Margin, Offset());
            var right = Edge(th, tw - 1 - Margin, Offset());
            var weight = Edge(Mathf.Max(tw, th), 0f, Offset(), 0.6f);
            float Over() => 2f + (float)random.NextDouble() * 4f;
            float bl = Over(), br = Over(), tl = Over(), tr = Over(), lb = Over(), lt = Over(), rb = Over(), rt = Over();

            var pixels = new Color32[tw * th];
            var clear = new Color(fill.r, fill.g, fill.b, 0f);
            for (var y = 0; y < th; y++)
            for (var x = 0; x < tw; x++)
            {
                var inside = y > bottom[x] && y < top[x] && x > left[y] && x < right[y];
                var d = float.MaxValue;
                if (x > left[y] - bl && x < right[y] + br) d = Mathf.Min(d, Mathf.Abs(y - bottom[x]));
                if (x > left[y] - tl && x < right[y] + tr) d = Mathf.Min(d, Mathf.Abs(y - top[x]));
                if (y > bottom[x] - lb && y < top[x] + lt) d = Mathf.Min(d, Mathf.Abs(x - left[y]));
                if (y > bottom[x] - rb && y < top[x] + rt) d = Mathf.Min(d, Mathf.Abs(x - right[y]));
                var half = 1.5f + weight[Mathf.Max(x, y)];
                var ink = Mathf.Clamp01(half + 0.5f - d);
                var c = Color.Lerp(inside ? fill : clear, Ink, ink);
                c.a = Mathf.Max(inside ? fill.a : 0f, ink);
                pixels[y * tw + x] = c;
            }

            var texture = new Texture2D(tw, th, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Sketch box",
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            boxes[key] = texture;
            return texture;
        }

        static float[] Edge(int length, float at, float offset, float amplitude = 2f)
        {
            var edge = new float[length];
            for (var i = 0; i < length; i++)
                edge[i] = at + (Mathf.PerlinNoise(i * 0.013f + offset, offset) - 0.5f) * amplitude * 2f;
            return edge;
        }

        static void Flush()
        {
            foreach (var texture in boxes.Values)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
            boxes.Clear();
        }
    }
}
