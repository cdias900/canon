using UnityEngine;

namespace SheepGate.Art
{
    /// <summary>
    /// A small mutable pixel buffer with a TOP-LEFT origin: y grows downward, the way pixel
    /// art is actually read. The single flip into Unity's bottom-up texture memory happens
    /// inside Index(), so no drawing routine in this folder ever has to think about it.
    ///
    /// All coordinates are clipped, never wrapped: drawing off the edge is silently ignored.
    /// </summary>
    public sealed class PixelCanvas
    {
        /// <summary>Ordered 4x4 Bayer matrix, used by Dither.</summary>
        static readonly int[] Bayer4 =
        {
             0,  8,  2, 10,
            12,  4, 14,  6,
             3, 11,  1,  9,
            15,  7, 13,  5
        };

        readonly Color32[] _pixels;

        public int Width { get; }
        public int Height { get; }

        public PixelCanvas(int width, int height)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            _pixels = new Color32[Width * Height];
            // A default Color32 is (0, 0, 0, 0): the canvas starts fully transparent.
        }

        int Index(int x, int y)
        {
            return (Height - 1 - y) * Width + x;
        }

        public bool InBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public Color32 Get(int x, int y)
        {
            if (!InBounds(x, y)) return ArtPalette.Transparent;
            return _pixels[Index(x, y)];
        }

        /// <summary>Overwrites the pixel, alpha included.</summary>
        public void Set(int x, int y, Color32 color)
        {
            if (!InBounds(x, y)) return;
            _pixels[Index(x, y)] = color;
        }

        /// <summary>Source-over composite, for translucent shading passes.</summary>
        public void Blend(int x, int y, Color32 color)
        {
            if (!InBounds(x, y)) return;
            if (color.a == 0) return;
            if (color.a == 255)
            {
                _pixels[Index(x, y)] = color;
                return;
            }

            int index = Index(x, y);
            Color32 destination = _pixels[index];
            float sourceAlpha = color.a / 255f;
            float destinationAlpha = destination.a / 255f;
            float outAlpha = sourceAlpha + destinationAlpha * (1f - sourceAlpha);
            if (outAlpha <= 0f)
            {
                _pixels[index] = ArtPalette.Transparent;
                return;
            }

            float keep = destinationAlpha * (1f - sourceAlpha);
            byte r = (byte)Mathf.Clamp(Mathf.RoundToInt((color.r * sourceAlpha + destination.r * keep) / outAlpha), 0, 255);
            byte g = (byte)Mathf.Clamp(Mathf.RoundToInt((color.g * sourceAlpha + destination.g * keep) / outAlpha), 0, 255);
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt((color.b * sourceAlpha + destination.b * keep) / outAlpha), 0, 255);
            byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(outAlpha * 255f), 0, 255);
            _pixels[index] = new Color32(r, g, b, a);
        }

        public void Clear(Color32 color)
        {
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = color;
        }

        public void FillRect(int x, int y, int width, int height, Color32 color)
        {
            for (int py = y; py < y + height; py++)
            {
                for (int px = x; px < x + width; px++) Set(px, py, color);
            }
        }

        /// <summary>Like FillRect but composites, so a translucent color shades what is there.</summary>
        public void ShadeRect(int x, int y, int width, int height, Color32 color)
        {
            for (int py = y; py < y + height; py++)
            {
                for (int px = x; px < x + width; px++) Blend(px, py, color);
            }
        }

        /// <summary>One pixel wide rectangle stroke, drawn inside the given bounds.</summary>
        public void Outline(int x, int y, int width, int height, Color32 color)
        {
            if (width <= 0 || height <= 0) return;
            HLine(x, y, width, color);
            HLine(x, y + height - 1, width, color);
            VLine(x, y, height, color);
            VLine(x + width - 1, y, height, color);
        }

        public void HLine(int x, int y, int length, Color32 color)
        {
            for (int i = 0; i < length; i++) Set(x + i, y, color);
        }

        public void VLine(int x, int y, int length, Color32 color)
        {
            for (int i = 0; i < length; i++) Set(x, y + i, color);
        }

        /// <summary>Bresenham line.</summary>
        public void Line(int x0, int y0, int x1, int y1, Color32 color)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = -Mathf.Abs(y1 - y0);
            int stepX = x0 < x1 ? 1 : -1;
            int stepY = y0 < y1 ? 1 : -1;
            int error = dx + dy;

            while (true)
            {
                Set(x0, y0, color);
                if (x0 == x1 && y0 == y1) break;
                int doubled = error * 2;
                if (doubled >= dy)
                {
                    error += dy;
                    x0 += stepX;
                }
                if (doubled <= dx)
                {
                    error += dx;
                    y0 += stepY;
                }
            }
        }

        public void FillEllipse(int centerX, int centerY, int radiusX, int radiusY, Color32 color)
        {
            if (radiusX <= 0 || radiusY <= 0) return;
            for (int py = centerY - radiusY; py <= centerY + radiusY; py++)
            {
                for (int px = centerX - radiusX; px <= centerX + radiusX; px++)
                {
                    float nx = (px + 0.5f - (centerX + 0.5f)) / radiusX;
                    float ny = (py + 0.5f - (centerY + 0.5f)) / radiusY;
                    if (nx * nx + ny * ny <= 1f) Set(px, py, color);
                }
            }
        }

        public void FillCircle(int centerX, int centerY, int radius, Color32 color)
        {
            FillEllipse(centerX, centerY, radius, radius, color);
        }

        /// <summary>Rounded rectangle fill. Stack two of them to get a one pixel outline.</summary>
        public void FillRoundedRect(int x, int y, int width, int height, int radius, Color32 color)
        {
            if (width <= 0 || height <= 0) return;
            int limit = Mathf.Min(width, height) / 2;
            radius = Mathf.Clamp(radius, 0, limit);

            int innerLeft = x + radius;
            int innerRight = x + width - 1 - radius;
            int innerTop = y + radius;
            int innerBottom = y + height - 1 - radius;

            for (int py = y; py < y + height; py++)
            {
                for (int px = x; px < x + width; px++)
                {
                    int dx = 0;
                    if (px < innerLeft) dx = innerLeft - px;
                    else if (px > innerRight) dx = px - innerRight;

                    int dy = 0;
                    if (py < innerTop) dy = innerTop - py;
                    else if (py > innerBottom) dy = py - innerBottom;

                    if (dx * dx + dy * dy <= radius * radius) Set(px, py, color);
                }
            }
        }

        /// <summary>
        /// Ordered dither between two colors. amount 0 paints only colorA, amount 1 only colorB.
        /// </summary>
        public void Dither(int x, int y, int width, int height, Color32 colorA, Color32 colorB, float amount)
        {
            for (int py = y; py < y + height; py++)
            {
                for (int px = x; px < x + width; px++)
                {
                    float threshold = (Bayer4[(py & 3) * 4 + (px & 3)] + 0.5f) / 16f;
                    Set(px, py, amount > threshold ? colorB : colorA);
                }
            }
        }

        /// <summary>
        /// Fills a rectangle by picking a ramp entry per pixel from tileable value noise.
        /// wrapCells is the lattice period; pass width / cellSize to make the result tile.
        /// </summary>
        public void NoiseFill(int x, int y, int width, int height, Color32[] ramp, int seed, float cellSize, int wrapCells)
        {
            if (ramp == null || ramp.Length == 0) return;
            float scale = cellSize <= 0f ? 1f : 1f / cellSize;
            for (int py = y; py < y + height; py++)
            {
                for (int px = x; px < x + width; px++)
                {
                    float value = ValueNoise.Fbm(seed, (px - x) * scale, (py - y) * scale, 2, wrapCells);
                    int index = Mathf.Clamp(Mathf.FloorToInt(value * ramp.Length), 0, ramp.Length - 1);
                    Set(px, py, ramp[index]);
                }
            }
        }

        /// <summary>Sparse single pixel speckles, deterministic for a given seed.</summary>
        public void Speckle(int x, int y, int width, int height, Color32 color, int seed, float density)
        {
            for (int py = y; py < y + height; py++)
            {
                for (int px = x; px < x + width; px++)
                {
                    if (ValueNoise.Cell(seed, px, py) < density) Set(px, py, color);
                }
            }
        }

        /// <summary>Paints a one pixel border around every opaque region.</summary>
        public void OutlineOpaque(Color32 color)
        {
            bool[] solid = new bool[_pixels.Length];
            for (int i = 0; i < _pixels.Length; i++) solid[i] = _pixels[i].a > 0;

            for (int py = 0; py < Height; py++)
            {
                for (int px = 0; px < Width; px++)
                {
                    if (solid[Index(px, py)]) continue;
                    if (HasSolidNeighbour(solid, px, py)) Set(px, py, color);
                }
            }
        }

        bool HasSolidNeighbour(bool[] solid, int x, int y)
        {
            if (x > 0 && solid[Index(x - 1, y)]) return true;
            if (x < Width - 1 && solid[Index(x + 1, y)]) return true;
            if (y > 0 && solid[Index(x, y - 1)]) return true;
            if (y < Height - 1 && solid[Index(x, y + 1)]) return true;
            return false;
        }

        /// <summary>Mirrors the canvas around its vertical center. Used to derive left from right.</summary>
        public void MirrorHorizontal()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width / 2; x++)
                {
                    int left = Index(x, y);
                    int right = Index(Width - 1 - x, y);
                    Color32 swap = _pixels[left];
                    _pixels[left] = _pixels[right];
                    _pixels[right] = swap;
                }
            }
        }

        /// <summary>Copy of the buffer in Unity texture order, kept for the tint cache.</summary>
        public Color32[] ToArray()
        {
            return (Color32[])_pixels.Clone();
        }

        public Texture2D ToTexture(string name)
        {
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(_pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
