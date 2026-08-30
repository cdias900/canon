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

        // ================================================================ antialiased shapes
        //
        // Everything above draws with hard pixel edges, and for a 32x32 tile magnified by the
        // tilemap that is correct: a soft edge on pixel art reads as a mistake, not as polish.
        //
        // The design system's UI frames are the opposite case. They are generated at the size
        // they render at, so a 55 pixel corner is genuinely a curve rather than a staircase, and
        // the only place a curve can live in an RGBA32 texture is the alpha channel. That is also
        // where the neutral repaint puts the bevel and the border, so the routines below exist to
        // write coverage rather than colour.
        //
        // Coverage comes from a signed distance field: negative inside the shape, zero on its
        // edge, positive outside. One routine therefore draws every radius at the same quality,
        // and the shapes compose — a ring is a rounded box distance folded around its own edge,
        // an arc is a chain of capsules.

        /// <summary>
        /// Writes <paramref name="color"/> at a fractional coverage, which is how every routine
        /// below lands its pixels.
        ///
        /// Two shapes of the same colour combine by MAXIMUM coverage rather than by compositing.
        /// That matters because an icon stroke is drawn as overlapping capsules: source-over
        /// would add the two half-covered edge pixels where two segments meet and fatten the
        /// join into a visible lump. Different colours still composite normally, so a body can be
        /// laid over its own shadow.
        /// </summary>
        public void PaintCoverage(int x, int y, Color32 color, float coverage)
        {
            if (!InBounds(x, y)) return;
            if (coverage <= 0f) return;
            if (coverage > 1f) coverage = 1f;

            byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * coverage), 0, 255);
            if (alpha == 0) return;

            Color32 destination = _pixels[Index(x, y)];
            if (destination.a > 0 && destination.r == color.r && destination.g == color.g && destination.b == color.b)
            {
                if (alpha > destination.a) _pixels[Index(x, y)] = new Color32(color.r, color.g, color.b, alpha);
                return;
            }

            Blend(x, y, new Color32(color.r, color.g, color.b, alpha));
        }

        /// <summary>
        /// Signed distance from a point to a rounded rectangle: negative inside, zero on the
        /// edge, positive outside. The rectangle is described the way every other routine here
        /// describes one — top-left corner, width, height — and the point is in the same
        /// top-left pixel space, so a pixel's centre is (x + 0.5, y + 0.5).
        /// </summary>
        public static float RoundedBoxDistance(float pointX, float pointY,
            float x, float y, float width, float height, float radius)
        {
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            float centerX = x + halfWidth;
            float centerY = y + halfHeight;
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(halfWidth, halfHeight));

            float qx = Mathf.Abs(pointX - centerX) - (halfWidth - radius);
            float qy = Mathf.Abs(pointY - centerY) - (halfHeight - radius);
            float outsideX = Mathf.Max(qx, 0f);
            float outsideY = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY)
                 + Mathf.Min(Mathf.Max(qx, qy), 0f)
                 - radius;
        }

        /// <summary>Distance from a point to a line segment. The basis of every stroked shape.</summary>
        public static float SegmentDistance(float pointX, float pointY, float x0, float y0, float x1, float y1)
        {
            float axisX = x1 - x0;
            float axisY = y1 - y0;
            float lengthSquared = axisX * axisX + axisY * axisY;

            float toPointX = pointX - x0;
            float toPointY = pointY - y0;
            float t = lengthSquared <= 0f
                ? 0f
                : Mathf.Clamp01((toPointX * axisX + toPointY * axisY) / lengthSquared);

            float offsetX = toPointX - axisX * t;
            float offsetY = toPointY - axisY * t;
            return Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY);
        }

        /// <summary>Antialiased rounded rectangle, filled flat.</summary>
        public void FillRoundedRectAA(float x, float y, float width, float height, float radius, Color32 color)
        {
            FillRoundedRectAA(x, y, width, height, radius, color, 0f, 255);
        }

        /// <summary>
        /// Antialiased rounded rectangle whose outermost <paramref name="rimWidth"/> pixels fade
        /// towards <paramref name="rimAlpha"/>.
        ///
        /// The rim is the neutral repaint's substitute for a painted border. A frame drawn to be
        /// multiplied by <c>Image.color</c> cannot carry a highlight — nothing in a multiply can
        /// come out brighter than the tint — so the only edge it can carry is one that lets some
        /// of the surface behind show through. Over this game's dark background that reads as a
        /// darker rim, which is the border the design system asks for, and it costs no hue.
        /// </summary>
        public void FillRoundedRectAA(float x, float y, float width, float height, float radius,
            Color32 color, float rimWidth, byte rimAlpha)
        {
            if (width <= 0f || height <= 0f) return;
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(width, height) * 0.5f);

            int minX = Mathf.Max(0, Mathf.FloorToInt(x) - 1);
            int minY = Mathf.Max(0, Mathf.FloorToInt(y) - 1);
            int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(x + width) + 1);
            int maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(y + height) + 1);

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    float distance = RoundedBoxDistance(px + 0.5f, py + 0.5f, x, y, width, height, radius);
                    float coverage = Mathf.Clamp01(0.5f - distance);
                    if (coverage <= 0f) continue;

                    float scale = 1f;
                    if (rimWidth > 0f && rimAlpha < 255)
                    {
                        // -distance is how far inside the perimeter the pixel sits.
                        float depth = Mathf.Clamp01(-distance / rimWidth);
                        scale = Mathf.Lerp(rimAlpha / 255f, 1f, depth);
                    }

                    PaintCoverage(px, py, color, coverage * scale);
                }
            }
        }

        /// <summary>
        /// Antialiased rounded rectangle outline, drawn <paramref name="thickness"/> pixels
        /// INWARD from the given bounds so the shape never grows past the rect it was asked for.
        /// The centre is left untouched, which is what makes it usable as a nine-sliced ring.
        /// </summary>
        public void StrokeRoundedRectAA(float x, float y, float width, float height, float radius,
            float thickness, Color32 color)
        {
            if (width <= 0f || height <= 0f || thickness <= 0f) return;
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(width, height) * 0.5f);

            int minX = Mathf.Max(0, Mathf.FloorToInt(x) - 1);
            int minY = Mathf.Max(0, Mathf.FloorToInt(y) - 1);
            int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(x + width) + 1);
            int maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(y + height) + 1);
            float half = thickness * 0.5f;

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    float distance = RoundedBoxDistance(px + 0.5f, py + 0.5f, x, y, width, height, radius);
                    // Fold the field around a line half a stroke inside the edge: the result is
                    // negative only within the band [-thickness, 0], which is the stroke.
                    float ring = Mathf.Abs(distance + half) - half;
                    PaintCoverage(px, py, color, Mathf.Clamp01(0.5f - ring));
                }
            }
        }

        /// <summary>Antialiased disc.</summary>
        public void FillCircleAA(float centerX, float centerY, float radius, Color32 color)
        {
            if (radius <= 0f) return;

            int minX = Mathf.Max(0, Mathf.FloorToInt(centerX - radius) - 1);
            int minY = Mathf.Max(0, Mathf.FloorToInt(centerY - radius) - 1);
            int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(centerX + radius) + 1);
            int maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(centerY + radius) + 1);

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    float offsetX = px + 0.5f - centerX;
                    float offsetY = py + 0.5f - centerY;
                    float distance = Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY) - radius;
                    PaintCoverage(px, py, color, Mathf.Clamp01(0.5f - distance));
                }
            }
        }

        /// <summary>
        /// Antialiased thick line segment with round caps. Every icon stroke in the design
        /// system's 24 grid is one of these, which is what gives them a single consistent weight
        /// and the 2px rounded ends the iconography rule asks for.
        /// </summary>
        public void CapsuleAA(float x0, float y0, float x1, float y1, float thickness, Color32 color)
        {
            if (thickness <= 0f) return;
            float half = thickness * 0.5f;

            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x0, x1) - half) - 1);
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y0, y1) - half) - 1);
            int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(Mathf.Max(x0, x1) + half) + 1);
            int maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(Mathf.Max(y0, y1) + half) + 1);

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    float distance = SegmentDistance(px + 0.5f, py + 0.5f, x0, y0, x1, y1) - half;
                    PaintCoverage(px, py, color, Mathf.Clamp01(0.5f - distance));
                }
            }
        }

        /// <summary>
        /// Antialiased arc, as a chain of capsules. Angles are in degrees, measured
        /// counter-clockwise from the positive x axis of a normal maths diagram — the routine
        /// flips y itself, so 90 is up on screen despite this canvas growing y downward.
        /// </summary>
        public void StrokeArcAA(float centerX, float centerY, float radius, float thickness,
            float startDegrees, float sweepDegrees, Color32 color)
        {
            if (radius <= 0f || thickness <= 0f) return;

            // One segment per six degrees: at the radii this file uses, the chord's sag is well
            // under a tenth of a pixel, so the arc reads as a curve and not as a polygon.
            int steps = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(sweepDegrees) / 6f));
            float previousX = 0f;
            float previousY = 0f;

            for (int i = 0; i <= steps; i++)
            {
                float radians = (startDegrees + sweepDegrees * i / steps) * Mathf.Deg2Rad;
                float pointX = centerX + Mathf.Cos(radians) * radius;
                float pointY = centerY - Mathf.Sin(radians) * radius;
                if (i > 0) CapsuleAA(previousX, previousY, pointX, pointY, thickness, color);
                previousX = pointX;
                previousY = pointY;
            }
        }

        /// <summary>
        /// Ambient shadow OUTSIDE a rounded rectangle, fading to nothing over
        /// <paramref name="softness"/> pixels. Draw it before the body it belongs to.
        ///
        /// This is the one place the UI art is not neutral, and it cannot be: a sprite that is
        /// multiplied by its tint can never come out darker than the tint, so an elevation
        /// shadow under a near-white pergaminho card has to carry its own dark pixels. Keeping
        /// its alpha low is what stops it reading as a border.
        /// </summary>
        public void SoftShadowRoundedRect(float x, float y, float width, float height, float radius,
            float softness, Color32 color)
        {
            if (softness <= 0f || width <= 0f || height <= 0f) return;
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(width, height) * 0.5f);

            int minX = Mathf.Max(0, Mathf.FloorToInt(x - softness) - 1);
            int minY = Mathf.Max(0, Mathf.FloorToInt(y - softness) - 1);
            int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(x + width + softness) + 1);
            int maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(y + height + softness) + 1);

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    float distance = RoundedBoxDistance(px + 0.5f, py + 0.5f, x, y, width, height, radius);
                    if (distance <= 0f)
                    {
                        PaintCoverage(px, py, color, 1f);
                        continue;
                    }

                    float falloff = 1f - Mathf.Clamp01(distance / softness);
                    PaintCoverage(px, py, color, falloff * falloff);
                }
            }
        }

        /// <summary>
        /// Antialiased circular hole: scales down whatever alpha is already there instead of
        /// painting. The padlock's keyhole is a hole, and a hole punched with a background colour
        /// would stop being a hole the moment the icon is tinted.
        /// </summary>
        public void EraseCircleAA(float centerX, float centerY, float radius)
        {
            if (radius <= 0f) return;

            int minX = Mathf.Max(0, Mathf.FloorToInt(centerX - radius) - 1);
            int minY = Mathf.Max(0, Mathf.FloorToInt(centerY - radius) - 1);
            int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(centerX + radius) + 1);
            int maxY = Mathf.Min(Height - 1, Mathf.CeilToInt(centerY + radius) + 1);

            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    float offsetX = px + 0.5f - centerX;
                    float offsetY = py + 0.5f - centerY;
                    float distance = Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY) - radius;
                    float coverage = Mathf.Clamp01(0.5f - distance);
                    if (coverage <= 0f) continue;

                    int index = Index(px, py);
                    Color32 existing = _pixels[index];
                    if (existing.a == 0) continue;

                    byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(existing.a * (1f - coverage)), 0, 255);
                    _pixels[index] = new Color32(existing.r, existing.g, existing.b, alpha);
                }
            }
        }

        /// <summary>
        /// Recolours every fully transparent pixel without giving any of them alpha.
        ///
        /// An RGBA32 texture keeps an RGB value underneath a zero alpha, and bilinear filtering
        /// interpolates that hidden colour along with everything else. A shape antialiased against
        /// empty texels whose hidden colour is black therefore picks up a dark fringe wherever it
        /// is sampled off the pixel grid — which is every UI sprite that is not rendered at exactly
        /// its own size. Filling the empty texels with the shape's own colour removes the fringe
        /// and changes nothing that is visible.
        /// </summary>
        public void SetTransparentColor(Color32 color)
        {
            for (int i = 0; i < _pixels.Length; i++)
            {
                if (_pixels[i].a != 0) continue;
                _pixels[i] = new Color32(color.r, color.g, color.b, 0);
            }
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
