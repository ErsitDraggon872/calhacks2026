using NUnit.Framework;
using UnityEngine;

namespace CalHacks.Tests
{
    public class PaperTextureTests
    {
        [Test]
        public void PaperAddsBorderAroundInk()
        {
            var src = new Texture2D(20, 20, TextureFormat.RGBA32, false);
            var clear = new Color32[400];
            src.SetPixels32(clear);
            src.SetPixel(10, 10, Color.black);
            src.Apply();

            var paper = PaperTexture.MakePaper(src);
            Assert.Greater(paper.width, src.width);
            var border = (paper.width - src.width) / 2;
            // The inked pixel stays dark, a neighbour within the border becomes opaque paper, a far corner stays clear.
            Assert.Less(paper.GetPixel(10 + border, 10 + border).r, 0.2f);
            Assert.AreEqual(1f, paper.GetPixel(10 + border + 2, 10 + border).a, 0.01f);
            Assert.AreEqual(0f, paper.GetPixel(0, 0).a, 0.01f);

            Object.DestroyImmediate(src);
            Object.DestroyImmediate(paper);
        }

        [Test]
        public void BadBase64ReturnsNull()
        {
            Assert.IsNull(PaperTexture.FromBase64("not base64!!"));
            Assert.IsNull(PaperTexture.FromBase64(null));
        }
    }
}
