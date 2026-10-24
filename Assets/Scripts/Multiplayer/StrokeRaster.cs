using System;
using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// A small CPU canvas for synced strokes: thick polylines in normalized 0-1 coordinates (origin bottom-left) on a
    /// <see cref="Texture2D"/>. Used by the Explorer's live preview and by the stroke-only BUILD fallback.
    /// </summary>
    public class StrokeRaster : IDisposable
    {
        readonly Color32 background;
        readonly Color32 ink;
        readonly int radius;
        Color32[] pixels;
        bool dirty;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public Texture2D Texture { get; private set; }

        public StrokeRaster(int width, int height, Color background, Color ink, int radius)
        {
            this.background = background;
            this.ink = ink;
            this.radius = Mathf.Max(1, radius);
            Resize(width, height);
        }

        public void Resize(int width, int height)
        {
            width = Mathf.Max(8, width);
            height = Mathf.Max(8, height);
            if (Texture != null && width == Width && height == Height) return;
            if (Texture != null) PaperTexture.DestroySafe(Texture);
            Width = width;
            Height = height;
            pixels = new Color32[width * height];
            Texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "StrokeRaster",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            Clear();
        }

        public void Clear()
        {
            for (var i = 0; i < pixels.Length; i++) pixels[i] = background;
            dirty = true;
        }

        /// <summary>Draws a flattened polyline x0,y0,x1,y1,...</summary>
        public void Draw(IReadOnlyList<float> points)
        {
            for (var i = 0; i + 3 < points.Count; i += 2)
                Line(points[i] * (Width - 1), points[i + 1] * (Height - 1), points[i + 2] * (Width - 1), points[i + 3] * (Height - 1));
            dirty = true;
        }

        /// <summary>Replaces the canvas with a decoded image, scaled to fit (used for physical-whiteboard frames).</summary>
        public void Show(Texture2D image)
        {
            Resize(Width, Mathf.RoundToInt(Width * (float)image.height / image.width));
            var src = image.GetPixels32();
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                pixels[y * Width + x] = src[(y * image.height / Height) * image.width + x * image.width / Width];
            dirty = true;
        }

        /// <summary>A copy of the canvas pixels, bottom row first (Texture2D order).</summary>
        public Color32[] CopyPixels() => (Color32[])pixels.Clone();

        /// <summary>Uploads pending changes and returns the texture.</summary>
        public Texture2D Apply()
        {
            if (dirty)
            {
                Texture.SetPixels32(pixels);
                Texture.Apply(false);
                dirty = false;
            }
            return Texture;
        }

        /// <summary>PNG of just the inked area plus <paramref name="margin"/> pixels, or null if nothing is drawn.</summary>
        public byte[] CroppedPng(int margin)
        {
            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var p = pixels[y * Width + x];
                if (p.r != ink.r || p.g != ink.g || p.b != ink.b || p.a != ink.a) continue;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
            if (maxX < 0) return null;
            minX = Math.Max(0, minX - margin);
            minY = Math.Max(0, minY - margin);
            var w = Math.Min(Width, maxX + margin + 1) - minX;
            var h = Math.Min(Height, maxY + margin + 1) - minY;
            var crop = new Color32[w * h];
            for (var y = 0; y < h; y++) Array.Copy(pixels, (y + minY) * Width + minX, crop, y * w, w);
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.SetPixels32(crop);
            texture.Apply(false);
            var png = texture.EncodeToPNG();
            UnityEngine.Object.Destroy(texture);
            return png;
        }

        void Line(float x0, float y0, float x1, float y1)
        {
            var steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0))));
            for (var s = 0; s <= steps; s++)
            {
                var t = (float)s / steps;
                Dot(Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, t)));
            }
        }

        void Dot(int cx, int cy)
        {
            var r2 = radius * radius;
            for (var dy = -radius; dy <= radius; dy++)
            {
                var y = cy + dy;
                if (y < 0 || y >= Height) continue;
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var x = cx + dx;
                    if (x < 0 || x >= Width || dx * dx + dy * dy > r2) continue;
                    pixels[y * Width + x] = ink;
                }
            }
        }

        public void Dispose()
        {
            if (Texture != null) PaperTexture.DestroySafe(Texture);
            Texture = null;
        }
    }
}
