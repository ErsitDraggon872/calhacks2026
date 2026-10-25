using System;
using NUnit.Framework;

namespace CalHacks.Tests
{
    public class JuiceSoundsTests
    {
        [Test]
        public void EverySoundIsShortQuietAndClickFree()
        {
            foreach (JuiceSound sound in Enum.GetValues(typeof(JuiceSound)))
            {
                var samples = JuiceSounds.Synth(sound);
                Assert.Greater(samples.Length, 0, sound.ToString());
                Assert.LessOrEqual(samples.Length, JuiceSounds.Rate * 2, sound + " is over 2 s");
                var peak = 0f;
                foreach (var s in samples)
                {
                    Assert.IsFalse(float.IsNaN(s) || float.IsInfinity(s), sound.ToString());
                    peak = Math.Max(peak, Math.Abs(s));
                }
                // Nothing near full scale: the game's sounds sit well under the speakers' limit.
                Assert.Greater(peak, 0.05f, sound + " is silent");
                Assert.LessOrEqual(peak, 0.66f, sound + " is too loud");
                // Silent at both ends, so starting, stopping and looping never click.
                Assert.AreEqual(0f, samples[0], 1e-4f, sound.ToString());
                Assert.AreEqual(0f, samples[samples.Length - 1], 1e-4f, sound.ToString());
            }
        }

        [Test]
        public void SoundsAreTheSameEveryTime()
        {
            CollectionAssert.AreEqual(JuiceSounds.Synth(JuiceSound.Munch), JuiceSounds.Synth(JuiceSound.Munch));
        }
    }
}
