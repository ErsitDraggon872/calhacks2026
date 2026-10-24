using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CalHacks.EditorTools
{
    /// <summary>
    /// Draws the mock whiteboard drawings (bridge, sun, fish, cat, scribble) as black marker strokes on a transparent
    /// background, cropped to the ink like the vision service's crop. Replace the PNGs with photos of real drawings
    /// whenever you like; the file names are what GameConfig.mockDrawings refers to.
    /// </summary>
    public static class MockDrawingGenerator
    {
        const string Folder = "Assets/Resources/MockDrawings";

        [MenuItem("Game/Generate Mock Drawings")]
        public static void Generate()
        {
            Directory.CreateDirectory(Folder);
            Save("bridge", Bridge);
            Save("sun", Sun);
            Save("fish", Fish);
            Save("cat", Cat);
            Save("scribble", Scribble);
            AssetDatabase.Refresh();
        }

        public static void GenerateIfMissing()
        {
            if (!File.Exists(Path.Combine(Folder, "bridge.png"))) Generate();
        }

        static void Save(string name, Action<Ink> draw)
        {
            var ink = new Ink(512, 512, name.GetHashCode());
            draw(ink);
            var tex = ink.ToCroppedTexture(12);
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static void Bridge(Ink k)
        {
            k.Line(30, 250, 482, 250);
            k.Line(30, 228, 482, 228);
            k.Arc(256, 70, 205, 150, 0, 180);
            for (var x = 86; x <= 426; x += 48)
            {
                var t = (x - 256f) / 205f;
                var archY = 70f + 150f * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
                k.Line(x, 228, x, archY);
            }
            k.Line(30, 320, 482, 320);
            for (var x = 30; x <= 482; x += 56) k.Line(x, 250, x, 320);
        }

        static void Sun(Ink k)
        {
            k.Arc(256, 256, 105, 105, 0, 360);
            for (var i = 0; i < 12; i++)
            {
                var a = i * Mathf.PI / 6f;
                k.Line(256 + Mathf.Cos(a) * 140, 256 + Mathf.Sin(a) * 140, 256 + Mathf.Cos(a) * 215, 256 + Mathf.Sin(a) * 215);
            }
            k.Dot(220, 285, 13);
            k.Dot(292, 285, 13);
            k.Arc(256, 250, 50, 40, 200, 340);
        }

        static void Fish(Ink k)
        {
            k.Arc(225, 256, 160, 85, 0, 360);
            k.Poly(370, 256, 475, 335, 460, 256, 475, 177, 370, 256);
            k.Dot(130, 280, 13);
            k.Arc(150, 256, 40, 60, -60, 60);
            k.Poly(200, 338, 250, 400, 290, 335);
            k.Arc(110, 232, 25, 12, 200, 330);
        }

        static void Cat(Ink k)
        {
            k.Arc(256, 220, 125, 110, 0, 360);
            k.Poly(160, 295, 165, 430, 245, 325);
            k.Poly(267, 325, 347, 430, 352, 295);
            k.Dot(210, 245, 14);
            k.Dot(302, 245, 14);
            k.Poly(244, 205, 268, 205, 256, 190, 244, 205);
            k.Arc(238, 190, 18, 22, 200, 340);
            k.Arc(274, 190, 18, 22, 200, 340);
            k.Line(200, 205, 90, 225);
            k.Line(200, 195, 92, 180);
            k.Line(312, 205, 422, 225);
            k.Line(312, 195, 420, 180);
        }

        static void Scribble(Ink k)
        {
            var rng = new System.Random(7);
            var x = 120f;
            var y = 200f;
            for (var i = 0; i < 26; i++)
            {
                var nx = 80 + (float)rng.NextDouble() * 352;
                var ny = 120 + (float)rng.NextDouble() * 272;
                k.Line(x, y, nx, ny);
                x = nx;
                y = ny;
            }
        }

        /// <summary>A transparent canvas you draw wobbly marker strokes on.</summary>
        class Ink
        {
            const float Radius = 8.5f;
            readonly int w, h;
            readonly byte[] alpha;
            readonly float seed;

            public Ink(int w, int h, int seed)
            {
                this.w = w;
                this.h = h;
                alpha = new byte[w * h];
                this.seed = Mathf.Abs(seed % 1000) * 0.37f;
            }

            public void Dot(float cx, float cy, float r)
            {
                var minX = Mathf.Max(0, (int)(cx - r - 2));
                var maxX = Mathf.Min(w - 1, (int)(cx + r + 2));
                var minY = Mathf.Max(0, (int)(cy - r - 2));
                var maxY = Mathf.Min(h - 1, (int)(cy + r + 2));
                for (var y = minY; y <= maxY; y++)
                for (var x = minX; x <= maxX; x++)
                {
                    var d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    var a = (byte)(Mathf.Clamp01(r - d + 0.5f) * 255f);
                    var i = y * w + x;
                    if (a > alpha[i]) alpha[i] = a;
                }
            }

            public void Line(float x0, float y0, float x1, float y1)
            {
                var length = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                var steps = Mathf.Max(1, (int)(length / (Radius * 0.35f)));
                var nx = -(y1 - y0) / Mathf.Max(length, 0.001f);
                var ny = (x1 - x0) / Mathf.Max(length, 0.001f);
                for (var s = 0; s <= steps; s++)
                {
                    var t = (float)s / steps;
                    var wobble = (Mathf.PerlinNoise(seed + x0 * 0.01f + t * length * 0.012f, seed + y0 * 0.01f) - 0.5f) * 7f;
                    var px = Mathf.Lerp(x0, x1, t) + nx * wobble;
                    var py = Mathf.Lerp(y0, y1, t) + ny * wobble;
                    Dot(px, py, Radius * (0.9f + 0.2f * Mathf.PerlinNoise(seed + t * 3f, 1.7f)));
                }
            }

            public void Poly(params float[] xy)
            {
                for (var i = 0; i + 3 < xy.Length; i += 2) Line(xy[i], xy[i + 1], xy[i + 2], xy[i + 3]);
            }

            public void Arc(float cx, float cy, float rx, float ry, float fromDeg, float toDeg)
            {
                const int segments = 48;
                var prevX = cx + Mathf.Cos(fromDeg * Mathf.Deg2Rad) * rx;
                var prevY = cy + Mathf.Sin(fromDeg * Mathf.Deg2Rad) * ry;
                for (var s = 1; s <= segments; s++)
                {
                    var a = Mathf.Lerp(fromDeg, toDeg, (float)s / segments) * Mathf.Deg2Rad;
                    var x = cx + Mathf.Cos(a) * rx;
                    var y = cy + Mathf.Sin(a) * ry;
                    Line(prevX, prevY, x, y);
                    prevX = x;
                    prevY = y;
                }
            }

            public Texture2D ToCroppedTexture(int pad)
            {
                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    if (alpha[y * w + x] < 8) continue;
                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
                minX = Mathf.Max(0, minX - pad);
                minY = Mathf.Max(0, minY - pad);
                maxX = Mathf.Min(w - 1, maxX + pad);
                maxY = Mathf.Min(h - 1, maxY + pad);
                var cw = maxX - minX + 1;
                var ch = maxY - minY + 1;
                var pixels = new Color32[cw * ch];
                for (var y = 0; y < ch; y++)
                for (var x = 0; x < cw; x++)
                    pixels[y * cw + x] = new Color32(22, 22, 28, alpha[(y + minY) * w + x + minX]);
                var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                tex.SetPixels32(pixels);
                tex.Apply();
                return tex;
            }
        }
    }
}
