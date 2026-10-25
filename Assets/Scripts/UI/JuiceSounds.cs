using System;
using UnityEngine;

namespace CalHacks
{
    public enum JuiceSound
    {
        Thwip, Reveal, Chime, Thud, Munch, Snip, Clack, Ding, Step, Jump, Land, Crumple, Whoosh, Sputter, Boing,
        Checkpoint, Fanfare, Scribble,
    }

    /// <summary>
    /// The game's sounds, synthesized from sine tones and filtered noise: no audio files, nothing to license or
    /// download. Pure functions of the sound (fixed noise seeds), so they can be tested without an AudioClip.
    /// </summary>
    public static class JuiceSounds
    {
        public const int Rate = 22050;

        /// <summary>Notes the musical sounds use (Hz).</summary>
        const float C5 = 523.25f, E5 = 659.25f, G5 = 783.99f, C6 = 1046.5f, E6 = 1318.5f, G6 = 1568f;

        /// <summary>Mono samples in -1..1, silent at both ends. <see cref="JuiceSound.Scribble"/> loops seamlessly.</summary>
        public static float[] Synth(JuiceSound sound)
        {
            var random = new System.Random(1000 + (int)sound);
            float[] b;
            switch (sound)
            {
                case JuiceSound.Thwip: // the drawing pops out of the page
                    b = Buffer(0.24f);
                    Noise(b, random, 0f, 0.22f, 1f, 700f, 3800f, highpass: true, attack: 0.015f, decay: 0.06f);
                    Tone(b, 0f, 0.2f, 350f, 1400f, 0.25f, 0.07f);
                    return Finish(b, 0.4f);
                case JuiceSound.Reveal: // "ta-da" under the card
                    b = Buffer(0.8f);
                    Bell(b, 0f, G5, 0.6f, 0.18f);
                    Bell(b, 0.11f, C6, 0.8f, 0.35f);
                    return Finish(b, 0.45f);
                case JuiceSound.Chime: // it worked
                    b = Buffer(1.2f);
                    Bell(b, 0f, C5, 0.7f, 0.3f);
                    Bell(b, 0.08f, E5, 0.7f, 0.3f);
                    Bell(b, 0.16f, G5, 0.7f, 0.3f);
                    Bell(b, 0.26f, C6, 0.9f, 0.6f);
                    return Finish(b, 0.5f);
                case JuiceSound.Thud: // it didn't
                    b = Buffer(0.5f);
                    Tone(b, 0f, 0.45f, 150f, 60f, 1f, 0.12f);
                    Noise(b, random, 0f, 0.2f, 0.5f, 450f, 150f, attack: 0.004f, decay: 0.05f);
                    return Finish(b, 0.65f);
                case JuiceSound.Munch:
                    b = Buffer(0.56f);
                    for (var i = 0; i < 3; i++)
                        Noise(b, random, i * 0.17f, 0.14f, 1f - i * 0.15f, 2600f, 800f, attack: 0.005f, decay: 0.045f);
                    return Finish(b, 0.55f);
                case JuiceSound.Snip:
                    b = Buffer(0.3f);
                    for (var i = 0; i < 2; i++)
                    {
                        Noise(b, random, i * 0.12f, 0.05f, 1f, 2200f, 4500f, highpass: true, attack: 0.004f, decay: 0.014f);
                        Tone(b, i * 0.12f, 0.08f, 2800f, 2800f, 0.1f, 0.025f);
                    }
                    return Finish(b, 0.28f);
                case JuiceSound.Clack: // planks landing
                    b = Buffer(0.36f);
                    for (var i = 0; i < 2; i++)
                    {
                        var t = i * 0.09f;
                        Tone(b, t, 0.15f, 820f - i * 120f, 820f - i * 120f, 1f, 0.03f);
                        Tone(b, t, 0.1f, 1350f - i * 150f, 1350f - i * 150f, 0.5f, 0.02f);
                        Noise(b, random, t, 0.03f, 0.6f, 3000f, 3000f, attack: 0.001f, decay: 0.008f);
                    }
                    return Finish(b, 0.45f);
                case JuiceSound.Ding:
                    b = Buffer(1f);
                    Bell(b, 0f, E6, 1f, 0.4f);
                    return Finish(b, 0.35f);
                case JuiceSound.Step:
                    b = Buffer(0.07f);
                    Noise(b, random, 0f, 0.06f, 1f, 1800f, 900f, attack: 0.003f, decay: 0.015f);
                    return Finish(b, 0.16f);
                case JuiceSound.Jump:
                    b = Buffer(0.15f);
                    Noise(b, random, 0f, 0.14f, 1f, 1000f, 3200f, highpass: true, attack: 0.008f, decay: 0.035f);
                    Tone(b, 0f, 0.12f, 500f, 900f, 0.2f, 0.05f);
                    return Finish(b, 0.2f);
                case JuiceSound.Land:
                    b = Buffer(0.16f);
                    Noise(b, random, 0f, 0.15f, 1f, 900f, 300f, attack: 0.003f, decay: 0.035f);
                    Tone(b, 0f, 0.14f, 120f, 80f, 0.6f, 0.04f);
                    return Finish(b, 0.35f);
                case JuiceSound.Crumple:
                    b = Buffer(0.4f);
                    for (var i = 0; i < 16; i++)
                        Noise(b, random, (float)random.NextDouble() * 0.32f, 0.03f, 0.4f + 0.6f * (float)random.NextDouble(),
                            1200f, 2200f, highpass: true, attack: 0.002f, decay: 0.007f);
                    return Finish(b, 0.35f);
                case JuiceSound.Whoosh: // a rocket or a ride setting off
                    b = Buffer(1.5f);
                    Noise(b, random, 0f, 1.45f, 1f, 600f, 2200f, attack: 0.12f, decay: 0.6f);
                    Noise(b, random, 0f, 1.45f, 0.6f, 150f, 150f, attack: 0.08f, decay: 0.7f);
                    return Finish(b, 0.45f);
                case JuiceSound.Sputter: // out of fuel
                    b = Buffer(0.8f);
                    float[] puffs = { 0f, 0.12f, 0.26f, 0.42f, 0.6f };
                    for (var i = 0; i < puffs.Length; i++)
                        Noise(b, random, puffs[i], 0.1f, 1f - i * 0.18f, 1200f, 400f, attack: 0.004f, decay: 0.025f);
                    return Finish(b, 0.45f);
                case JuiceSound.Boing:
                    b = Buffer(0.45f);
                    Boing(b);
                    return Finish(b, 0.4f);
                case JuiceSound.Checkpoint:
                    b = Buffer(0.9f);
                    Bell(b, 0f, G5, 0.7f, 0.25f);
                    Bell(b, 0.1f, C6, 1f, 0.4f);
                    return Finish(b, 0.4f);
                case JuiceSound.Fanfare: // the win
                    b = Buffer(2f);
                    Brass(b, 0f, C5, 0.7f, 0.1f);
                    Brass(b, 0.12f, E5, 0.7f, 0.1f);
                    Brass(b, 0.24f, G5, 0.7f, 0.1f);
                    Brass(b, 0.4f, C6, 1f, 0.55f);
                    Brass(b, 0.4f, E6, 0.5f, 0.55f);
                    Bell(b, 0.4f, G6, 0.3f, 0.5f);
                    return Finish(b, 0.5f);
                case JuiceSound.Scribble: // pencil on paper while the AI thinks
                    b = Buffer(1.2f);
                    Scribble(b, random);
                    return Finish(b, 0.22f);
                default:
                    throw new ArgumentOutOfRangeException(nameof(sound), sound, null);
            }
        }

        static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];

        /// <summary>A sine gliding from <paramref name="from"/> to <paramref name="to"/> Hz, decaying with time constant <paramref name="decay"/>.</summary>
        static void Tone(float[] b, float start, float duration, float from, float to, float amp, float decay)
        {
            var first = (int)(start * Rate);
            var count = Mathf.Min((int)(duration * Rate), b.Length - first);
            var phase = 0.0;
            for (var i = 0; i < count; i++)
            {
                var t = (float)i / Rate;
                var freq = Mathf.Lerp(from, to, t / duration);
                phase += 2.0 * Math.PI * freq / Rate;
                b[first + i] += amp * Envelope(t, 0.003f, decay) * (float)Math.Sin(phase);
            }
        }

        /// <summary>A struck bell: the fundamental and two inharmonic partials that die away sooner.</summary>
        static void Bell(float[] b, float start, float freq, float amp, float decay)
        {
            var duration = Mathf.Min(decay * 6f, (float)b.Length / Rate - start);
            Tone(b, start, duration, freq, freq, amp, decay);
            Tone(b, start, duration, freq * 2.76f, freq * 2.76f, amp * 0.3f, decay * 0.4f);
            Tone(b, start, duration, freq * 5.4f, freq * 5.4f, amp * 0.04f, decay * 0.2f);
        }

        /// <summary>A soft brass note: a few harmonics.</summary>
        static void Brass(float[] b, float start, float freq, float amp, float decay)
        {
            var duration = Mathf.Min(decay * 6f, (float)b.Length / Rate - start);
            Tone(b, start, duration, freq, freq, amp, decay);
            Tone(b, start, duration, freq * 2f, freq * 2f, amp * 0.4f, decay);
            Tone(b, start, duration, freq * 3f, freq * 3f, amp * 0.2f, decay * 0.7f);
        }

        /// <summary>
        /// White noise through a one-pole filter whose cutoff sweeps from <paramref name="from"/> to <paramref name="to"/>
        /// Hz: a low-pass, or with <paramref name="highpass"/> what the low-pass removes.
        /// </summary>
        static void Noise(float[] b, System.Random random, float start, float duration, float amp, float from, float to,
            bool highpass = false, float attack = 0.005f, float decay = 0.05f)
        {
            var first = (int)(start * Rate);
            var count = Mathf.Min((int)(duration * Rate), b.Length - first);
            var low = 0f;
            for (var i = 0; i < count; i++)
            {
                var t = (float)i / Rate;
                var cutoff = Mathf.Lerp(from, to, t / duration);
                var k = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
                var white = (float)(random.NextDouble() * 2.0 - 1.0);
                low += k * (white - low);
                b[first + i] += amp * Envelope(t, attack, decay) * (highpass ? white - low : low);
            }
        }

        static void Boing(float[] b)
        {
            var phase = 0.0;
            for (var i = 0; i < b.Length; i++)
            {
                var t = (float)i / Rate;
                var freq = 150f + 250f * (1f - Mathf.Exp(-t / 0.05f));
                freq *= 1f + 0.08f * Mathf.Exp(-t / 0.2f) * Mathf.Sin(2f * Mathf.PI * 18f * t);
                phase += 2.0 * Math.PI * freq / Rate;
                b[i] += Envelope(t, 0.004f, 0.15f) * (float)Math.Sin(phase);
            }
        }

        /// <summary>
        /// Pencil strokes: band-passed noise in short swells with pauses between them. Every stroke ends inside the
        /// buffer, so the loop starts and ends silent.
        /// </summary>
        static void Scribble(float[] b, System.Random random)
        {
            float slow = 0f, fast = 0f;
            var t = 0.04f;
            var strokes = new System.Collections.Generic.List<(float start, float length, float amp)>();
            var total = (float)b.Length / Rate;
            while (true)
            {
                var length = 0.1f + 0.1f * (float)random.NextDouble();
                if (t + length > total - 0.04f) break;
                strokes.Add((t, length, 0.5f + 0.5f * (float)random.NextDouble()));
                t += length + 0.03f + 0.08f * (float)random.NextDouble();
            }
            for (var i = 0; i < b.Length; i++)
            {
                var white = (float)(random.NextDouble() * 2.0 - 1.0);
                slow += 0.22f * (white - slow); // ~900 Hz
                fast += 0.63f * (white - fast); // ~3.5 kHz
                var now = (float)i / Rate;
                var level = 0f;
                foreach (var (start, length, amp) in strokes)
                {
                    if (now < start || now > start + length) continue;
                    var s = Mathf.Sin((now - start) / length * Mathf.PI);
                    level = amp * s * s;
                }
                b[i] = (fast - slow) * level;
            }
        }

        static float Envelope(float t, float attack, float decay) =>
            t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);

        /// <summary>
        /// Softens the top end (a gentle low-pass at ~4.5 kHz: nothing shrill on a projector's speakers), scales to a peak
        /// of <paramref name="peak"/> and fades the first and last 4 ms, so nothing clicks.
        /// </summary>
        static float[] Finish(float[] b, float peak)
        {
            var soft = 1f - Mathf.Exp(-2f * Mathf.PI * 4500f / Rate);
            var low = 0f;
            for (var i = 0; i < b.Length; i++) b[i] = low += soft * (b[i] - low);
            var max = 0f;
            foreach (var s in b) max = Mathf.Max(max, Mathf.Abs(s));
            var gain = max > 0f ? peak / max : 0f;
            var fade = Mathf.Min(b.Length / 2, (int)(0.004f * Rate));
            for (var i = 0; i < b.Length; i++)
            {
                var edge = Mathf.Min(i, b.Length - 1 - i);
                b[i] *= gain * (edge < fade ? (float)edge / fade : 1f);
            }
            return b;
        }
    }
}
