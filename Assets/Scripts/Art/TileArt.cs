using UnityEngine;

namespace SheepGate.Art
{
    /// <summary>
    /// Every 32x32 world texture: the four ground tiles, the five wall stages and the two props.
    ///
    /// The wall stages are the game's core visual reward, so the progression is carried by
    /// four independent signals that all move the same way, and are readable at a glance even
    /// on a phone screen:
    ///   height      5 -> 11 -> 18 -> 25 -> 32 pixels
    ///   regularity  jittered rubble courses -> staggered, even courses
    ///   value       dark and dirty -> pale and dressed
    ///   silhouette  ragged top edge -> flat walkway with crenellations
    /// Scaffold putlog holes appear while the work is in progress and are filled at stage 4.
    /// </summary>
    public static class TileArt
    {
        public const int Size = 32;

        /// <summary>Wall height in pixels for each stage, 0 through 4.</summary>
        static readonly int[] WallHeights = { 6, 11, 18, 25, 32 };

        // ------------------------------------------------------------------ tiles

        public static PixelCanvas Ground(int seed)
        {
            PixelCanvas canvas = new PixelCanvas(Size, Size);
            canvas.NoiseFill(0, 0, Size, Size, ArtPalette.GroundRamp, seed, 4f, 8);

            // Pebbles and hairline cracks, so a large field of ground is not a flat wash.
            for (int i = 0; i < 7; i++)
            {
                int x = ValueNoise.RangeInt(seed, i, 1, Size - 3);
                int y = ValueNoise.RangeInt(seed, i + 40, 1, Size - 3);
                canvas.HLine(x, y, 2, ArtPalette.StoneLight);
                canvas.Set(x, y + 1, ArtPalette.StoneDark);
                canvas.Set(x + 1, y + 1, ArtPalette.StoneDark);
            }
            for (int i = 0; i < 3; i++)
            {
                int x = ValueNoise.RangeInt(seed, i + 80, 2, Size - 6);
                int y = ValueNoise.RangeInt(seed, i + 90, 2, Size - 2);
                canvas.HLine(x, y, ValueNoise.RangeInt(seed, i + 95, 3, 6), ArtPalette.StoneDark);
            }
            return canvas;
        }

        public static PixelCanvas RubbleTile(int seed)
        {
            PixelCanvas canvas = Ground(seed + 17);
            canvas.ShadeRect(0, 0, Size, Size, ArtPalette.SoftShade);

            for (int i = 0; i < 13; i++)
            {
                int x = ValueNoise.RangeInt(seed, i, 0, Size - 5);
                int y = ValueNoise.RangeInt(seed, i + 60, 0, Size - 4);
                int width = ValueNoise.RangeInt(seed, i + 120, 3, 6);
                int height = ValueNoise.RangeInt(seed, i + 180, 2, 4);
                DrawStone(canvas, x, y, width, height, ArtPalette.StoneRamp, seed, i);
            }
            return canvas;
        }

        public static PixelCanvas Water(int seed)
        {
            PixelCanvas canvas = new PixelCanvas(Size, Size);
            Color32[] ramp = { ArtPalette.TealDeep, ArtPalette.TealDark, ArtPalette.TealDark, ArtPalette.TealMid };
            canvas.NoiseFill(0, 0, Size, Size, ramp, seed, 4f, 8);

            for (int i = 0; i < 5; i++)
            {
                int x = ValueNoise.RangeInt(seed, i, 0, Size - 6);
                int y = ValueNoise.RangeInt(seed, i + 30, 1, Size - 1);
                int length = ValueNoise.RangeInt(seed, i + 60, 3, 7);
                canvas.HLine(x, y, length, ArtPalette.TealLight);
                canvas.HLine(x + 1, y + 1, length - 1, ArtPalette.TealDeep);
            }
            return canvas;
        }

        /// <summary>A mud brick house front: stone footing, brick courses, doorway, window.</summary>
        public static PixelCanvas House(int seed)
        {
            PixelCanvas canvas = new PixelCanvas(Size, Size);
            canvas.FillRect(0, 0, Size, Size, ArtPalette.ClayMid);
            // Sun dried mud brick, not fired brick: dust the face down with stone so a wall
            // of these tiles does not read as a modern red facade.
            canvas.Dither(0, 0, Size, Size, ArtPalette.ClayMid, ArtPalette.ClayDark, 0.4f);
            canvas.Speckle(0, 0, Size, Size, ArtPalette.StoneDark, seed, 0.12f);

            // Brick courses, staggered every other row.
            for (int y = 3; y < 28; y += 4)
            {
                canvas.HLine(0, y, Size, ArtPalette.ClayDark);
                int stagger = ((y / 4) % 2 == 0) ? 0 : 4;
                for (int x = stagger; x < Size; x += 8) canvas.VLine(x, y + 1, 3, ArtPalette.ClayDark);
            }

            // Eave shadow along the top, stone footing along the bottom.
            canvas.FillRect(0, 0, Size, 3, ArtPalette.ClayDeep);
            canvas.HLine(0, 2, Size, ArtPalette.ClayDark);
            canvas.FillRect(0, 28, Size, 4, ArtPalette.StoneDark);
            canvas.HLine(0, 28, Size, ArtPalette.StoneMid);

            // Weathering, applied before the openings so it never lands inside them.
            canvas.Speckle(0, 3, Size, 25, ArtPalette.ClayLight, seed, 0.05f);

            // Doorway with a stone lintel.
            canvas.FillRect(12, 16, 8, 16, ArtPalette.Ink);
            canvas.FillRect(13, 17, 6, 15, ArtPalette.StoneDeep);
            canvas.FillRect(11, 14, 10, 2, ArtPalette.StoneMid);
            canvas.HLine(11, 14, 10, ArtPalette.StoneLight);

            // Small window.
            canvas.FillRect(4, 8, 5, 4, ArtPalette.StoneMid);
            canvas.FillRect(5, 9, 3, 2, ArtPalette.Ink);
            return canvas;
        }

        // ------------------------------------------------------------------ wall

        public static PixelCanvas Wall(int stage, int seed)
        {
            stage = Mathf.Clamp(stage, 0, 4);
            PixelCanvas canvas = new PixelCanvas(Size, Size);

            if (stage == 0)
            {
                DrawFooting(canvas, seed);
                return canvas;
            }

            int bottom = Size - 1;
            int top = Size - WallHeights[stage];

            if (stage == 4)
            {
                // Finished: even courses, a flat walkway cap, crenellations on a 16 pixel
                // period so neighbouring tiles line up into one continuous parapet.
                DrawCourses(canvas, 8, bottom, stage, seed);
                canvas.FillRect(0, 6, Size, 2, ArtPalette.StoneLight);
                canvas.HLine(0, 6, Size, ArtPalette.StonePale);
                DrawMerlon(canvas, 0);
                DrawMerlon(canvas, 16);
                return canvas;
            }

            DrawCourses(canvas, top, bottom, stage, seed);
            CarveTopEdge(canvas, top, seed, stage == 1 ? 3 : (stage == 2 ? 2 : 1));

            if (stage >= 2)
            {
                // Putlog holes: the sockets a working scaffold sits in. They read as "unfinished".
                DrawPutlogHole(canvas, 6, top + 6);
                DrawPutlogHole(canvas, 22, top + 6);
            }
            if (stage == 3)
            {
                // A timber tie beam left in the wall while the work goes on.
                canvas.FillRect(0, 12, Size, 2, ArtPalette.ClayDark);
                canvas.HLine(0, 12, Size, ArtPalette.ClayMid);
            }
            return canvas;
        }

        static void DrawFooting(PixelCanvas canvas, int seed)
        {
            // Packed earth trench with the first stones laid flush into it.
            canvas.FillRect(0, 28, Size, 4, ArtPalette.StoneDark);
            canvas.Speckle(0, 28, Size, 4, ArtPalette.StoneDeep, seed, 0.35f);
            for (int i = 0; i < 4; i++)
            {
                int x = i * 8;
                canvas.FillRect(x + 1, 27, 6, 3, ArtPalette.StoneMid);
                canvas.HLine(x + 1, 27, 6, ArtPalette.StoneLight);
                canvas.HLine(x + 1, 29, 6, ArtPalette.StoneDeep);
            }
            // Chalked line marking where the wall will stand.
            canvas.Dither(0, 26, Size, 1, ArtPalette.Transparent, ArtPalette.StonePale, 0.5f);
        }

        static void DrawCourses(PixelCanvas canvas, int top, int bottom, int stage, int seed)
        {
            Color32[] ramp = RampForStage(stage);
            Color32 mortar = stage >= 3 ? ArtPalette.StoneDark : ArtPalette.StoneDeep;
            const int courseHeight = 4;
            int courseIndex = 0;

            for (int rowBottom = bottom; rowBottom >= top; rowBottom -= courseHeight)
            {
                int rowTop = Mathf.Max(top, rowBottom - courseHeight + 1);
                canvas.FillRect(0, rowTop, Size, rowBottom - rowTop + 1, mortar);
                DrawCourse(canvas, rowTop, rowBottom, courseIndex, stage, ramp, seed);
                courseIndex++;
            }
        }

        static void DrawCourse(PixelCanvas canvas, int rowTop, int rowBottom, int courseIndex, int stage, Color32[] ramp, int seed)
        {
            bool dressed = stage >= 3;
            int stagger = (courseIndex % 2 == 0) ? 0 : 4;
            int x = -stagger;
            int blockIndex = 0;

            while (x < Size)
            {
                int blockWidth = dressed ? 8 : ValueNoise.RangeInt(seed, courseIndex * 31 + blockIndex, 6, 10);
                int jitter = dressed ? 0 : (ValueNoise.Value01(seed, courseIndex * 53 + blockIndex) < 0.35f ? 1 : 0);
                int blockTop = rowTop + 1 + jitter;
                int blockHeight = rowBottom - blockTop + 1;
                int fillX = x + 1;
                int fillWidth = blockWidth - 1;

                if (blockHeight > 0 && fillWidth > 0)
                {
                    int shade = ValueNoise.RangeInt(seed, courseIndex * 17 + blockIndex + 5, 1, ramp.Length);
                    canvas.FillRect(fillX, blockTop, fillWidth, blockHeight, ramp[shade]);
                    canvas.HLine(fillX, blockTop, fillWidth, ramp[Mathf.Min(shade + 1, ramp.Length - 1)]);
                    canvas.HLine(fillX, rowBottom, fillWidth, ramp[Mathf.Max(shade - 1, 0)]);
                }

                x += blockWidth;
                blockIndex++;
            }
        }

        static void CarveTopEdge(PixelCanvas canvas, int top, int seed, int notches)
        {
            for (int i = 0; i < notches; i++)
            {
                int x = ValueNoise.RangeInt(seed, 100 + i, 0, Size - 4);
                int width = ValueNoise.RangeInt(seed, 200 + i, 2, 6);
                int height = ValueNoise.RangeInt(seed, 300 + i, 1, 3);
                canvas.FillRect(x, top, width, height, ArtPalette.Transparent);
            }
        }

        static void DrawPutlogHole(PixelCanvas canvas, int x, int y)
        {
            canvas.FillRect(x, y, 3, 2, ArtPalette.Ink);
            canvas.HLine(x, y + 2, 3, ArtPalette.StoneDeep);
        }

        static void DrawMerlon(PixelCanvas canvas, int x)
        {
            canvas.FillRect(x, 0, 8, 6, ArtPalette.StoneMid);
            canvas.HLine(x, 0, 8, ArtPalette.StonePale);
            canvas.VLine(x, 0, 6, ArtPalette.StoneLight);
            canvas.VLine(x + 7, 0, 6, ArtPalette.StoneDark);
            canvas.HLine(x, 5, 8, ArtPalette.StoneDark);
        }

        static Color32[] RampForStage(int stage)
        {
            switch (stage)
            {
                case 1: return new[] { ArtPalette.StoneDeep, ArtPalette.StoneDeep, ArtPalette.StoneDark, ArtPalette.StoneMid };
                case 2: return new[] { ArtPalette.StoneDeep, ArtPalette.StoneDark, ArtPalette.StoneMid, ArtPalette.StoneLight };
                case 3: return new[] { ArtPalette.StoneDark, ArtPalette.StoneMid, ArtPalette.StoneLight, ArtPalette.StonePale };
                case 4: return new[] { ArtPalette.StoneMid, ArtPalette.StoneLight, ArtPalette.StoneLight, ArtPalette.StonePale };
                default: return new[] { ArtPalette.StoneDeep, ArtPalette.StoneDark, ArtPalette.StoneMid, ArtPalette.StoneMid };
            }
        }

        // ------------------------------------------------------------------ props

        public static PixelCanvas PropRubble(int seed)
        {
            PixelCanvas canvas = new PixelCanvas(Size, Size);
            int[] rowY = { 26, 22, 18, 14 };
            int[] rowLeft = { 4, 7, 10, 13 };
            int[] rowRight = { 28, 25, 22, 19 };

            int index = 0;
            for (int row = 0; row < rowY.Length; row++)
            {
                int x = rowLeft[row];
                while (x < rowRight[row])
                {
                    int width = ValueNoise.RangeInt(seed, index, 4, 7);
                    if (x + width > rowRight[row]) width = rowRight[row] - x;
                    if (width < 2) break;
                    DrawStone(canvas, x, rowY[row], width, 4, ArtPalette.StoneRamp, seed, index);
                    x += width;
                    index++;
                }
            }

            canvas.OutlineOpaque(ArtPalette.Ink);
            return canvas;
        }

        /// <summary>
        /// A dug well: a stone ring, dark water, a clay bucket resting on the rim.
        /// No frame, no beam, no rope: nothing here may resolve into a symbol.
        /// </summary>
        public static PixelCanvas PropWell(int seed)
        {
            PixelCanvas canvas = new PixelCanvas(Size, Size);

            canvas.FillEllipse(16, 18, 12, 8, ArtPalette.StoneDark);
            canvas.FillEllipse(16, 17, 12, 8, ArtPalette.StoneMid);
            canvas.FillEllipse(16, 17, 9, 6, ArtPalette.StoneDeep);
            canvas.FillEllipse(16, 18, 8, 5, ArtPalette.TealDeep);
            canvas.HLine(11, 14, 10, ArtPalette.TealDark);
            canvas.HLine(13, 15, 6, ArtPalette.TealMid);

            // Facet highlights on the ring stones.
            for (int i = 0; i < 6; i++)
            {
                int x = 5 + i * 4;
                canvas.HLine(x, 10 + (i % 2), 3, ArtPalette.StoneLight);
                canvas.HLine(x, 24 - (i % 2), 3, ArtPalette.StoneDeep);
            }

            // Bucket on the rim.
            canvas.FillRect(20, 4, 7, 9, ArtPalette.ClayMid);
            canvas.HLine(20, 4, 7, ArtPalette.ClayLight);
            canvas.HLine(20, 6, 7, ArtPalette.ClayDeep);
            canvas.HLine(20, 12, 7, ArtPalette.ClayDeep);
            canvas.VLine(26, 5, 8, ArtPalette.ClayDark);
            canvas.Speckle(4, 10, 24, 16, ArtPalette.StoneLight, seed, 0.04f);

            canvas.OutlineOpaque(ArtPalette.Ink);
            return canvas;
        }

        static void DrawStone(PixelCanvas canvas, int x, int y, int width, int height, Color32[] ramp, int seed, int index)
        {
            int shade = ValueNoise.RangeInt(seed, index * 13 + 3, 1, ramp.Length - 1);
            canvas.FillRect(x, y, width, height, ramp[shade]);
            canvas.HLine(x, y, width, ramp[Mathf.Min(shade + 1, ramp.Length - 1)]);
            canvas.HLine(x, y + height - 1, width, ramp[Mathf.Max(shade - 1, 0)]);
            canvas.Set(x, y, ramp[Mathf.Max(shade - 1, 0)]);
            canvas.Set(x + width - 1, y, ramp[Mathf.Max(shade - 1, 0)]);
        }
    }
}
