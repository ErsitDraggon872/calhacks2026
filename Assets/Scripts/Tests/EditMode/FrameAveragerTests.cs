using NUnit.Framework;
using UnityEngine;

namespace CalHacks.Tests
{
    public class FrameAveragerTests
    {
        static Color32[] Frame(byte v, int n)
        {
            var frame = new Color32[n];
            for (var i = 0; i < n; i++) frame[i] = new Color32(v, (byte)(v / 2), 0, 255);
            return frame;
        }

        [Test]
        public void AveragesEachChannel()
        {
            var averager = new FrameAverager(4);
            averager.Add(Frame(100, 6), 3, 2);
            averager.Add(Frame(200, 6), 3, 2);
            var average = averager.Average(out var w, out var h);
            Assert.AreEqual(3, w);
            Assert.AreEqual(2, h);
            Assert.AreEqual(150, average[5].r);
            Assert.AreEqual(75, average[5].g);
        }

        [Test]
        public void KeepsOnlyTheLatestFrames()
        {
            var averager = new FrameAverager(2);
            averager.Add(Frame(0, 4), 2, 2);
            averager.Add(Frame(100, 4), 2, 2);
            averager.Add(Frame(200, 4), 2, 2);
            Assert.AreEqual(2, averager.Count);
            Assert.AreEqual(150, averager.Average(out _, out _)[0].r);
        }

        [Test]
        public void ANewSizeStartsOver()
        {
            var averager = new FrameAverager(4);
            averager.Add(Frame(0, 4), 2, 2);
            averager.Add(Frame(90, 6), 3, 2);
            Assert.AreEqual(1, averager.Count);
            Assert.AreEqual(90, averager.Average(out var w, out _)[0].r);
            Assert.AreEqual(3, w);
        }
    }
}
