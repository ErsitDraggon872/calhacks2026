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
        public void LowestInkRowSkipsPaperBorder()
        {
            var src = new Texture2D(20, 20, TextureFormat.RGBA32, false);
            src.SetPixels32(new Color32[400]);
            src.SetPixel(10, 6, Color.black);
            src.SetPixel(4, 15, Color.black);
            src.Apply();

            var paper = PaperTexture.MakePaper(src);
            var border = (paper.width - src.width) / 2;
            // The paper reaches below the ink, but the lowest ink row is the inked pixel's.
            Assert.AreEqual(6 + border, PaperTexture.LowestInkRow(paper.GetPixels32(), paper.width, paper.height));
            Assert.AreEqual(-1, PaperTexture.LowestInkRow(new Color32[16], 4, 4));

            Object.DestroyImmediate(src);
            Object.DestroyImmediate(paper);
        }

        [Test]
        public void EnclosedAreasBecomePaper()
        {
            // A 1 px ring of ink, radius 30: its inside is far wider than the border, but a cutout has no holes.
            const int size = 80;
            var src = new Texture2D(size, size, TextureFormat.RGBA32, false);
            src.SetPixels32(new Color32[size * size]);
            for (var k = 0; k < 720; k++)
            {
                var angle = k * Mathf.PI / 360f;
                src.SetPixel(40 + Mathf.RoundToInt(30f * Mathf.Cos(angle)), 40 + Mathf.RoundToInt(30f * Mathf.Sin(angle)), Color.black);
            }
            src.Apply();

            var paper = PaperTexture.MakePaper(src);
            var border = (paper.width - src.width) / 2;
            var centre = paper.GetPixel(40 + border, 40 + border);
            Assert.AreEqual(1f, centre.a, 0.01f, "inside of the ring is paper");
            Assert.Greater(centre.r, 0.9f, "and it's white, not ink");
            Assert.AreEqual(0f, paper.GetPixel(0, 0).a, 0.01f, "outside stays clear");

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
