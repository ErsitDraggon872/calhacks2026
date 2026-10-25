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

        public static GUIStyle Text(int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft, bool bold = false) =>
            new GUIStyle(GUI.skin.label)
            {
                font = Hand, fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, alignment = alignment,
                richText = true, wordWrap = false, clipping = TextClipping.Overflow, normal = { textColor = color },
            };

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

        /// <summary>A paper button with a drop shadow; lighter on hover. False while GUI.enabled is off.</summary>
        public static bool Button(Rect rect, string label, Color fill, int seed, GUIStyle style)
        {
            var hover = GUI.enabled && rect.Contains(Event.current.mousePosition);
            Box(rect, hover ? Color.Lerp(fill, Color.white, 0.35f) : fill, seed, shadow: true);
            GUI.Label(rect, label, style);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        /// <summary>An underlined text link.</summary>
        public static bool Link(Rect rect, string label, GUIStyle style)
        {
            GUI.Label(rect, label, style);
            var width = style.CalcSize(new GUIContent(label)).x;
            var x = style.alignment == TextAnchor.MiddleRight || style.alignment == TextAnchor.UpperRight ? rect.xMax - width : rect.x;
            var old = GUI.color;
            GUI.color = style.normal.textColor;
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
