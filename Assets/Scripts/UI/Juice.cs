using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CalHacks
{
    /// <summary>
    /// Sound and particles for BUILD, the puzzles and the win. Gameplay code makes one-line calls (<see cref="Play"/>,
    /// <see cref="PlayAfterCard"/>, <see cref="Appear"/>); without a Juice in the scene they do nothing, and they never
    /// throw. Visual only: particles are their own objects under this one, never the drawings or their colliders. They
    /// run on unscaled time so they play behind the paused card, and F5 clears them. The sounds are synthesized at start
    /// (<see cref="JuiceSounds"/>), so there are no audio files.
    /// </summary>
    public sealed class Juice : MonoBehaviour
    {
        const int Voices = 10;
        static readonly Color Ink = new Color(0.16f, 0.18f, 0.26f);
        static readonly Color Paper = new Color(0.98f, 0.95f, 0.86f);
        static readonly Color[] Confetti =
        {
            new Color(0.98f, 0.36f, 0.36f), new Color(1f, 0.78f, 0.25f), new Color(0.4f, 0.8f, 0.45f),
            new Color(0.35f, 0.62f, 0.98f), new Color(0.78f, 0.5f, 0.95f), Paper,
        };
        // Musical sounds keep their pitch; the rest vary a little so repeats don't sound canned.
        static readonly HashSet<JuiceSound> Tuned = new HashSet<JuiceSound>
        {
            JuiceSound.Reveal, JuiceSound.Chime, JuiceSound.Thud, JuiceSound.Ding, JuiceSound.Checkpoint, JuiceSound.Fanfare,
        };

        static Juice instance;

        [Tooltip("Master volume for every game sound.")]
        [Range(0f, 1f)] [SerializeField] float volume = 0.45f;

        readonly Dictionary<JuiceSound, AudioClip> clips = new Dictionary<JuiceSound, AudioClip>();
        AudioSource[] voices = Array.Empty<AudioSource>();
        AudioSource scribble;
        Transform effects;
        Material particleMaterial;
        float scribbleLevel;
        int nextVoice;
        bool failed;

        /// <summary>Plays <paramref name="sound"/> now (also while the card pauses the game).</summary>
        public static void Play(JuiceSound sound, float gain = 1f) => Guard(j => j.PlayNow(sound, gain));

        /// <summary>
        /// Plays <paramref name="sound"/> once the game is running again: after the drawing card if one is up or about to
        /// show, else next frame. For what happens in the world (the bridge landing, a puzzle solved), not on the card.
        /// </summary>
        public static void PlayAfterCard(JuiceSound sound, float gain = 1f) =>
            Guard(j => j.StartCoroutine(j.AfterCard(sound, gain)));

        /// <summary>A drawing just appeared: a paper thwip and a puff of ink dust around it.</summary>
        public static void Appear(Vector3 centre, Vector2 size) => Guard(j =>
        {
            j.PlayNow(JuiceSound.Thwip, 1f);
            j.InkPuff(centre, size);
        });

        static void Guard(Action<Juice> effect)
        {
            if (instance == null || instance.failed) return;
            try
            {
                effect(instance);
            }
            catch (Exception e)
            {
                // Juice must never break the game: log once and go quiet.
                instance.failed = true;
                Debug.LogWarning("[Juice] Effects off after an error: " + e);
            }
        }

        void Awake()
        {
            instance = this;
            effects = new GameObject("Juice Effects").transform;
            effects.SetParent(transform, false);
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) particleMaterial = new Material(shader);
            try
            {
                foreach (JuiceSound sound in Enum.GetValues(typeof(JuiceSound)))
                {
                    var samples = JuiceSounds.Synth(sound);
                    var clip = AudioClip.Create("Juice " + sound, samples.Length, 1, JuiceSounds.Rate, false);
                    clip.SetData(samples, 0);
                    clips[sound] = clip;
                }
                voices = new AudioSource[Voices];
                for (var i = 0; i < Voices; i++) voices[i] = NewSource(false);
                scribble = NewSource(true);
                scribble.clip = clips[JuiceSound.Scribble];
            }
            catch (Exception e)
            {
                // No sound (audio disabled, say); the particles still work.
                Debug.LogWarning("[Juice] Sounds off: " + e.Message);
                voices = Array.Empty<AudioSource>();
                scribble = null;
            }
        }

        AudioSource NewSource(bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            return source;
        }

        void OnEnable()
        {
            RunManager.RunReset += Clear;
            RunManager.Won += OnWon;
        }

        void OnDisable()
        {
            RunManager.RunReset -= Clear;
            RunManager.Won -= OnWon;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            foreach (var clip in clips.Values) if (clip != null) Destroy(clip);
            clips.Clear();
            if (particleMaterial != null) Destroy(particleMaterial);
        }

        /// <summary>The pencil scribbles while the AI reads a drawing, and stops when the card shows.</summary>
        void Update()
        {
            if (scribble == null) return;
            var run = RunManager.Instance;
            var thinking = !failed && run != null && run.State.InFlight && !run.State.Finished && !InventionCard.Visible;
            scribbleLevel = Mathf.MoveTowards(scribbleLevel, thinking ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            scribble.volume = scribbleLevel * volume;
            if (scribbleLevel > 0f && !scribble.isPlaying) scribble.Play();
            else if (scribbleLevel <= 0f && scribble.isPlaying) scribble.Stop();
        }

        void PlayNow(JuiceSound sound, float gain)
        {
            if (voices.Length == 0 || !clips.TryGetValue(sound, out var clip)) return;
            // A free voice, else the one started longest ago.
            AudioSource source = null;
            foreach (var voice in voices)
            {
                if (voice.isPlaying) continue;
                source = voice;
                break;
            }
            if (source == null) source = voices[nextVoice++ % voices.Length];
            source.Stop();
            source.clip = clip;
            source.volume = Mathf.Clamp01(gain) * volume;
            source.pitch = Tuned.Contains(sound) ? 1f : UnityEngine.Random.Range(0.95f, 1.05f);
            source.Play();
        }

        IEnumerator AfterCard(JuiceSound sound, float gain)
        {
            // The card shows later in the same frame as most solves, so look from the next frame on.
            yield return null;
            while (Time.timeScale <= 0f || InventionCard.Visible) yield return null;
            PlayNow(sound, gain);
        }

        /// <summary>F5: nothing left playing or flying.</summary>
        void Clear()
        {
            StopAllCoroutines();
            foreach (var voice in voices) voice.Stop();
            scribbleLevel = 0f;
            if (scribble != null) scribble.Stop();
            foreach (Transform effect in effects) Destroy(effect.gameObject);
        }

        void OnWon() => Guard(j =>
        {
            j.PlayNow(JuiceSound.Fanfare, 1f);
            var flag = GameObject.Find("Goal Flag");
            var player = RunManager.Instance != null ? RunManager.Instance.Player : null;
            var at = flag != null ? flag.transform.position + Vector3.up * 2.6f
                : player != null ? player.transform.position + Vector3.up * 1.5f : (Vector3?)null;
            if (at != null) j.ConfettiBurst(at.Value);
        });

        void InkPuff(Vector3 centre, Vector2 size)
        {
            var particles = NewBurst("Ink puff", centre + Vector3.back * 0.15f, 1f);
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            main.gravityModifier = 0.15f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(Mathf.Max(0.3f, size.x), Mathf.Max(0.3f, size.y * 0.8f), 0.2f);
            shape.randomDirectionAmount = 1f;
            var count = Mathf.Clamp(Mathf.RoundToInt(size.x * size.y * 6f), 16, 48);
            Emit(particles, count * 2 / 3, Ink);
            Emit(particles, count / 3, Paper);
        }

        void ConfettiBurst(Vector3 at)
        {
            var particles = NewBurst("Win confetti", at, 3f);
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.6f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.2f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // the cone opens upward
            var spin = particles.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var drag = particles.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.2f;
            foreach (var color in Confetti) Emit(particles, 16, color);
        }

        /// <summary>A one-shot particle system under <see cref="effects"/>, on unscaled time, gone when it's done.</summary>
        ParticleSystem NewBurst(string name, Vector3 at, float lifetime)
        {
            var go = new GameObject(name);
            go.transform.SetParent(effects, false);
            go.transform.position = at;
            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.2f;
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            var fade = particles.sizeOverLifetime;
            fade.enabled = true;
            fade.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (particleMaterial != null) renderer.sharedMaterial = particleMaterial;
            // Backstop on unscaled time, in case the stop action never fires.
            StartCoroutine(DestroyAfter(go, lifetime + 1f));
            return particles;
        }

        static void Emit(ParticleSystem particles, int count, Color color)
        {
            if (count <= 0) return;
            particles.Emit(new ParticleSystem.EmitParams { startColor = color, applyShapeToPosition = true }, count);
            if (!particles.isPlaying) particles.Play();
        }

        static IEnumerator DestroyAfter(GameObject go, float seconds)
        {
            for (var t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
            if (go != null) Destroy(go);
        }
    }
}
