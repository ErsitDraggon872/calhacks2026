using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Saved whiteboard photos instead of the webcam, for repeatable trials without the camera rig. Each grab returns the
    /// next PNG in the folder (sorted by name, then repeating), so the same ink cut-out and interpreter run as live.
    /// Transparent pixels are laid on white, so a cut-out PNG works as a board too.
    /// </summary>
    public class ReplayFrameSource : IBoardFrameSource
    {
        readonly string[] files;
        int next;

        public ReplayFrameSource(string folder)
        {
            Folder = folder;
            files = Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.png").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
        }

        public string Folder { get; }
        public string DeviceName => "replay";
        public string Status => files.Length == 0 ? $"no .png board photos in {Folder}" : $"replay {next % files.Length + 1}/{files.Length}: {NextName}";
        public bool HasFreshFrame => files.Length > 0;
        /// <summary>The photo the next grab returns.</summary>
        public string NextName => files.Length == 0 ? "-" : Path.GetFileName(files[next % files.Length]);

        public Color32[] Grab(out int width, out int height)
        {
            width = height = 0;
            if (files.Length == 0) return null;
            var path = files[next++ % files.Length];
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path))) return null;
                width = texture.width;
                height = texture.height;
                Debug.Log($"[Replay] {Path.GetFileName(path)} ({width}x{height})");
                return OnWhite(texture.GetPixels32());
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        static Color32[] OnWhite(Color32[] pixels)
        {
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                if (p.a == 255) continue;
                var k = p.a / 255f;
                pixels[i] = new Color32((byte)(255 - (255 - p.r) * k), (byte)(255 - (255 - p.g) * k), (byte)(255 - (255 - p.b) * k), 255);
            }
            return pixels;
        }
    }
}
