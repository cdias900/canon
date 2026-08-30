using System;
using System.Collections.Generic;
using UnityEngine;

namespace SheepGate.Audio
{
    /// <summary>
    /// Every sound in the game, synthesised the first time it is asked for and cached from then
    /// on. There are no audio files, nothing to import and nothing to wire in the inspector:
    /// <see cref="Get"/> is the whole audio pipeline, and it is deliberately the same shape as
    /// ArtLibrary.Get so that the two seams look alike and are replaced the same way.
    ///
    /// ---------------------------------------------------------------------------------
    /// KEYS
    /// ---------------------------------------------------------------------------------
    ///     amb_day       looping daytime bed, wind over open stone
    ///     amb_night     looping night bed, lower and emptier
    ///     sfx_step      one footstep on grit
    ///     sfx_stone     a block set down on the wall
    ///     sfx_confirm   a choice taken
    ///     sfx_trumpet   the shofar of NEH.4.20, and the only sound with a reference behind it
    ///
    /// ---------------------------------------------------------------------------------
    /// CONVENTIONS
    /// ---------------------------------------------------------------------------------
    /// Mono, 22050 Hz. Nothing here is music and nothing needs stereo width; half the sample
    /// rate is inaudible on these sounds and halves the memory.
    ///
    /// Deterministic: every generator draws from a hash of its own key, never from
    /// UnityEngine.Random. Two runs produce byte-identical audio, which is what makes a
    /// screenshot-and-listen check reproducible and keeps the build free of a moving target.
    ///
    /// Everything is peak-normalised and then scaled by a per-key headroom, so no sound can
    /// clip and the mix balance lives in one table rather than in each generator.
    /// </summary>
    public static class AudioLibrary
    {
        public const int SampleRate = 22050;

        static readonly Dictionary<string, AudioClip> Cache =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The clip for a key, or null when the key is not one this library makes.</summary>
        public static AudioClip Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            AudioClip cached;
            if (Cache.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            AudioClip built = Build(key);
            if (built != null)
            {
                Cache[key] = built;
            }

            return built;
        }

        /// <summary>Drops every cached clip. Called when the game is torn down in a test run.</summary>
        public static void Clear()
        {
            foreach (KeyValuePair<string, AudioClip> pair in Cache)
            {
                if (pair.Value != null)
                {
                    UnityEngine.Object.Destroy(pair.Value);
                }
            }

            Cache.Clear();
        }

        static AudioClip Build(string key)
        {
            switch (key)
            {
                case AudioKeys.AmbienceDay: return Ambience(key, 8f, 220f, 0.5f, 0.30f);
                case AudioKeys.AmbienceNight: return Ambience(key, 8f, 120f, 0.8f, 0.22f);
                case AudioKeys.Step: return Step(key);
                case AudioKeys.Stone: return Stone(key);
                case AudioKeys.Confirm: return Confirm(key);
                case AudioKeys.Trumpet: return Trumpet(key);
            }

            Debug.LogWarning("[Audio] No generator for key " + key + ".");
            return null;
        }

        // ------------------------------------------------------------------ generators

        /// <summary>
        /// A wind bed: filtered noise with a slow swell riding on it. <paramref name="cutoff"/> is
        /// what separates day from night — the same generator, darker — and the loop is seamless
        /// because the swell is built from whole cycles across the clip.
        /// </summary>
        static AudioClip Ambience(string key, float seconds, float cutoff, float swellCycles, float headroom)
        {
            int count = Mathf.RoundToInt(seconds * SampleRate);
            float[] samples = new float[count];

            var noise = new Rng(key);
            float smoothing = Mathf.Clamp01(cutoff / SampleRate * 6f);
            float filtered = 0f;

            for (int i = 0; i < count; i++)
            {
                float white = noise.NextSigned();
                filtered += (white - filtered) * smoothing;

                // Whole cycles over the clip, so the last sample meets the first.
                float swell = 0.65f + 0.35f * Mathf.Sin(i / (float)count * Mathf.PI * 2f * swellCycles);
                samples[i] = filtered * swell;
            }

            CrossfadeEnds(samples, SampleRate / 4);
            return Finish(key, samples, headroom);
        }

        /// <summary>Grit under a boot: a burst of noise with a very fast decay.</summary>
        static AudioClip Step(string key)
        {
            int count = SampleRate / 12;
            float[] samples = new float[count];
            var noise = new Rng(key);
            float filtered = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                filtered += (noise.NextSigned() - filtered) * 0.5f;
                samples[i] = filtered * Mathf.Exp(-14f * t);
            }

            return Finish(key, samples, 0.5f);
        }

        /// <summary>
        /// A block going down: a low thud with grit on top. The two layers are the point — the
        /// thud alone reads as a drum, the grit alone as a footstep.
        /// </summary>
        static AudioClip Stone(string key)
        {
            int count = SampleRate / 4;
            float[] samples = new float[count];
            var noise = new Rng(key);
            float filtered = 0f;
            float phase = 0f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;

                // The thud drops in pitch as it decays, which is what a heavy thing does.
                float frequency = Mathf.Lerp(120f, 62f, t);
                phase += frequency / SampleRate * Mathf.PI * 2f;
                float thud = Mathf.Sin(phase) * Mathf.Exp(-9f * t);

                filtered += (noise.NextSigned() - filtered) * 0.42f;
                float grit = filtered * Mathf.Exp(-26f * t) * 0.7f;

                samples[i] = thud + grit;
            }

            return Finish(key, samples, 0.7f);
        }

        /// <summary>Two short tones, the second above the first. Answered, not announced.</summary>
        static AudioClip Confirm(string key)
        {
            int count = SampleRate / 5;
            float[] samples = new float[count];
            int half = count / 2;

            for (int i = 0; i < count; i++)
            {
                bool second = i >= half;
                int local = second ? i - half : i;
                float t = local / (float)half;
                float frequency = second ? 587.33f : 440f;

                float envelope = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                samples[i] = Mathf.Sin(local * frequency / SampleRate * Mathf.PI * 2f) * envelope;
            }

            return Finish(key, samples, 0.45f);
        }

        /// <summary>
        /// The trumpet of NEH.4.20 — the one sound in the game that exists because a verse asks
        /// for it. A shofar is a horn, not a brass instrument: odd harmonics, a slow swell into
        /// the note, and a small waver that keeps it from sounding synthesised.
        /// </summary>
        static AudioClip Trumpet(string key)
        {
            int count = Mathf.RoundToInt(1.4f * SampleRate);
            float[] samples = new float[count];
            const float Fundamental = 233.08f;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                float seconds = i / (float)SampleRate;

                float waver = 1f + 0.008f * Mathf.Sin(seconds * Mathf.PI * 2f * 5.5f);
                float f = Fundamental * waver;

                float tone =
                    Mathf.Sin(seconds * f * Mathf.PI * 2f) +
                    0.55f * Mathf.Sin(seconds * f * 3f * Mathf.PI * 2f) +
                    0.28f * Mathf.Sin(seconds * f * 5f * Mathf.PI * 2f) +
                    0.12f * Mathf.Sin(seconds * f * 7f * Mathf.PI * 2f);

                float attack = Mathf.Clamp01(t / 0.08f);
                float release = Mathf.Clamp01((1f - t) / 0.30f);
                samples[i] = tone * attack * release;
            }

            return Finish(key, samples, 0.6f);
        }

        // ------------------------------------------------------------------ shaping

        /// <summary>
        /// Wraps the tail of a loop over its head so the seam is inaudible. A bed that clicks once
        /// every eight seconds is worse than no bed at all.
        /// </summary>
        static void CrossfadeEnds(float[] samples, int fade)
        {
            if (samples == null || fade <= 0 || fade * 2 >= samples.Length)
            {
                return;
            }

            for (int i = 0; i < fade; i++)
            {
                float mix = i / (float)fade;
                int tail = samples.Length - fade + i;
                samples[i] = Mathf.Lerp(samples[tail], samples[i], mix);
            }
        }

        /// <summary>Peak-normalises, applies the key's headroom and hands back a named clip.</summary>
        static AudioClip Finish(string key, float[] samples, float headroom)
        {
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float magnitude = Mathf.Abs(samples[i]);
                if (magnitude > peak)
                {
                    peak = magnitude;
                }
            }

            float scale = peak > 0.0001f ? headroom / peak : 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] *= scale;
            }

            AudioClip clip = AudioClip.Create(key, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// A tiny deterministic generator seeded from the key's own characters. The point is not
        /// statistical quality — it is that the same key produces the same noise on every machine
        /// and every run, which UnityEngine.Random cannot promise.
        /// </summary>
        struct Rng
        {
            uint _state;

            public Rng(string seed)
            {
                unchecked
                {
                    uint hash = 2166136261u;
                    for (int i = 0; i < seed.Length; i++)
                    {
                        hash ^= seed[i];
                        hash *= 16777619u;
                    }

                    _state = hash != 0u ? hash : 1u;
                }
            }

            public float NextSigned()
            {
                unchecked
                {
                    _state ^= _state << 13;
                    _state ^= _state >> 17;
                    _state ^= _state << 5;
                    return _state / (float)uint.MaxValue * 2f - 1f;
                }
            }
        }
    }

    /// <summary>The key strings, so no caller spells one by hand.</summary>
    public static class AudioKeys
    {
        public const string AmbienceDay = "amb_day";
        public const string AmbienceNight = "amb_night";
        public const string Step = "sfx_step";
        public const string Stone = "sfx_stone";
        public const string Confirm = "sfx_confirm";
        public const string Trumpet = "sfx_trumpet";
    }
}
