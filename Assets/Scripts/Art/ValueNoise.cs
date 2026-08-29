using UnityEngine;

namespace SheepGate.Art
{
    /// <summary>
    /// Deterministic hash noise. Every texture detail in this folder is derived from a seed
    /// computed from the sprite key, so the art is byte-identical on every run, machine and
    /// platform. UnityEngine.Random is never used here: it is global mutable state and its
    /// sequence is not a stable contract.
    /// </summary>
    public static class ValueNoise
    {
        /// <summary>FNV-1a over the key. Stable everywhere, unlike string.GetHashCode.</summary>
        public static int SeedFrom(string key)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (!string.IsNullOrEmpty(key))
                {
                    for (int i = 0; i < key.Length; i++)
                    {
                        hash ^= key[i];
                        hash *= 16777619u;
                    }
                }
                return (int)(hash & 0x7FFFFFFFu);
            }
        }

        /// <summary>Integer avalanche mix.</summary>
        public static uint Mix(uint value)
        {
            unchecked
            {
                value ^= 2747636419u;
                value *= 2654435769u;
                value ^= value >> 16;
                value *= 2654435769u;
                value ^= value >> 16;
                value *= 2654435769u;
                return value;
            }
        }

        /// <summary>Hash of a lattice cell, in 0..1.</summary>
        public static float Cell(int seed, int x, int y)
        {
            unchecked
            {
                uint hash = Mix((uint)seed ^ 0x9E3779B9u);
                hash = Mix(hash ^ (uint)(x * 374761393));
                hash = Mix(hash ^ (uint)(y * 668265263));
                return (hash & 0x00FFFFFFu) / 16777215f;
            }
        }

        /// <summary>One hashed value from a single index, in 0..1.</summary>
        public static float Value01(int seed, int index)
        {
            return Cell(seed, index, 0);
        }

        /// <summary>One hashed integer in [minInclusive, maxExclusive).</summary>
        public static int RangeInt(int seed, int index, int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            int span = maxExclusive - minInclusive;
            int offset = Mathf.FloorToInt(Value01(seed, index) * span);
            if (offset >= span) offset = span - 1;
            return minInclusive + offset;
        }

        /// <summary>Smoothly interpolated value noise. wrapCells > 0 makes the lattice tile.</summary>
        public static float Sample(int seed, float x, float y, int wrapCells)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);

            int x1 = x0 + 1;
            int y1 = y0 + 1;
            if (wrapCells > 0)
            {
                x0 = Wrap(x0, wrapCells);
                y0 = Wrap(y0, wrapCells);
                x1 = Wrap(x1, wrapCells);
                y1 = Wrap(y1, wrapCells);
            }

            float v00 = Cell(seed, x0, y0);
            float v10 = Cell(seed, x1, y0);
            float v01 = Cell(seed, x0, y1);
            float v11 = Cell(seed, x1, y1);

            float top = v00 + (v10 - v00) * sx;
            float bottom = v01 + (v11 - v01) * sx;
            return top + (bottom - top) * sy;
        }

        /// <summary>Two octaves of value noise, still tileable when wrapCells > 0.</summary>
        public static float Fbm(int seed, float x, float y, int octaves, int wrapCells)
        {
            float total = 0f;
            float amplitude = 1f;
            float weight = 0f;
            float frequency = 1f;
            int cells = wrapCells;
            for (int i = 0; i < octaves; i++)
            {
                total += Sample(seed + i * 7919, x * frequency, y * frequency, cells) * amplitude;
                weight += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
                if (cells > 0) cells *= 2;
            }
            return weight > 0f ? total / weight : 0f;
        }

        static int Wrap(int value, int period)
        {
            int wrapped = value % period;
            return wrapped < 0 ? wrapped + period : wrapped;
        }
    }
}
