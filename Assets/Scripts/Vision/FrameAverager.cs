using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// The last few webcam frames, averaged: in a dim room each frame is grainy, and the grain eats into faint strokes.
    /// The board holds still, so averaging keeps the ink and cancels the noise. Pure so it can be unit tested.
    /// </summary>
    public class FrameAverager
    {
        readonly Color32[][] frames;
        int width, height, next;

        public FrameAverager(int capacity)
        {
            frames = new Color32[Mathf.Max(1, capacity)][];
        }

        public int Count { get; private set; }

        /// <summary>Copies <paramref name="frame"/> in, dropping the oldest. A different size starts over.</summary>
        public void Add(Color32[] frame, int w, int h)
        {
            if (w != width || h != height) Clear();
            width = w;
            height = h;
            var slot = frames[next];
            if (slot == null || slot.Length != w * h) frames[next] = slot = new Color32[w * h];
            System.Array.Copy(frame, slot, w * h);
            next = (next + 1) % frames.Length;
            Count = Mathf.Min(Count + 1, frames.Length);
        }

        public void Clear()
        {
            Count = 0;
            next = 0;
        }

        /// <summary>The per-channel mean of the buffered frames, or null when there are none.</summary>
        public Color32[] Average(out int w, out int h)
        {
            w = width;
            h = height;
            if (Count == 0) return null;
            var n = width * height;
            var result = new Color32[n];
            for (var i = 0; i < n; i++)
            {
                int r = 0, g = 0, b = 0;
                for (var f = 0; f < Count; f++)
                {
                    var c = frames[f][i];
                    r += c.r;
                    g += c.g;
                    b += c.b;
                }
                result[i] = new Color32((byte)(r / Count), (byte)(g / Count), (byte)(b / Count), 255);
            }
            return result;
        }
    }
}
