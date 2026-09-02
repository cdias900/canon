using System.Collections.Generic;
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

        /// <summary>
        /// Dry ground.
        ///
        /// The ramp is deliberately narrow and the noise cells are deliberately large. The shared
        /// GroundRamp put ClayDark - a saturated brick red - in one of five slots, so a fifth of
        /// every tile was red pixels sitting against grey ones at a four-pixel cell size. Hue
        /// contrast plus value contrast at high frequency is the recipe for camouflage, and that
        /// is exactly what a field of it read as. Clay belongs here as a few deliberate flecks,
        /// which is what it gets below.
        /// </summary>
        static readonly Color32[] DustRamp =
        {
            ArtPalette.StoneDark, ArtPalette.StoneMid, ArtPalette.StoneMid,
            ArtPalette.StoneMid, ArtPalette.StoneLight
        };

        public static PixelCanvas Ground(int seed)
        {
            PixelCanvas canvas = new PixelCanvas(Size, Size);
            canvas.NoiseFill(0, 0, Size, Size, DustRamp, seed, 7f, 8);

            // A handful of clay flecks: present, countable, and not a fifth of the tile.
            for (int i = 0; i < 5; i++)
            {
                int fx = ValueNoise.RangeInt(seed, i + 120, 1, Size - 1);
                int fy = ValueNoise.RangeInt(seed, i + 140, 1, Size - 1);
                canvas.Set(fx, fy, ArtPalette.ClayDark);
            }

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

        /// <summary>
        /// A sleeping mat, woven straw, rolled at the head end and left where its owner sleeps.
        ///
        /// Deliberately the plainest object in the village: it is the one thing in the world that
        /// ends the day, and anything more ceremonious than a mat would turn stopping into a rite.
        /// </summary>
        public static PixelCanvas PropMat(int seed)
        {
            PixelCanvas canvas = new PixelCanvas(Size, Size);

            // The mat lying flat. Narrower at the top edge so it reads as lying away from us.
            canvas.FillRect(6, 15, 21, 10, ArtPalette.ClayLight);
            canvas.HLine(7, 14, 19, ArtPalette.ClayLight);
            canvas.HLine(6, 15, 21, ArtPalette.ClayPale);
            canvas.HLine(6, 24, 21, ArtPalette.ClayDark);

            // Weave: weft across, warp down, two shades apart so it reads as woven and not as a plank.
            for (int y = 17; y < 24; y += 2)
            {
                canvas.HLine(7, y, 19, ArtPalette.ClayMid);
            }

            for (int x = 8; x < 26; x += 3)
            {
                canvas.VLine(x, 16, 8, ArtPalette.ClayPale);
            }

            // Fringe worked into the bottom edge rather than hung below it: a strand drawn one row
            // clear of the body is an island, and OutlineOpaque rings every island in ink, which
            // turned a woven edge into two specks of dirt under the mat.
            for (int x = 7; x < 26; x++)
            {
                if (ValueNoise.RangeInt(seed, x, 0, 4) != 0)
                {
                    continue;
                }

                canvas.Set(x, 24, ArtPalette.ClayDeep);
                canvas.Set(x, 16, ArtPalette.ClayPale);
            }

            // Rolled at the head end: a blanket, or the mat itself turned over on itself. Lit
            // along the top and shaded under, so the roll reads as round instead of as a crate.
            canvas.FillRoundedRect(5, 8, 22, 7, 3, ArtPalette.ClayMid);
            canvas.HLine(7, 8, 18, ArtPalette.ClayPale);
            canvas.HLine(6, 9, 20, ArtPalette.ClayLight);
            canvas.HLine(6, 13, 20, ArtPalette.ClayDark);
            canvas.HLine(7, 14, 18, ArtPalette.ClayDeep);

            // One turn of the roll showing at each end, and a crease where the cloth folds over.
            canvas.VLine(8, 10, 3, ArtPalette.ClayDark);
            canvas.VLine(23, 10, 3, ArtPalette.ClayDark);
            canvas.Set(7, 11, ArtPalette.ClayDeep);
            canvas.Set(24, 11, ArtPalette.ClayDeep);
            canvas.Speckle(6, 17, 20, 7, ArtPalette.ClayPale, seed, 0.05f);

            canvas.OutlineOpaque(ArtPalette.Ink);
            return canvas;
        }


        // ------------------------------------------------------------ fallen wall

        /// <summary>
        /// The wall lying where it fell.
        ///
        /// This is not <see cref="RubbleTile"/> with a different seed, and the difference is the
        /// whole point. Rubble inside the city is gathered material: a dark patch packed edge to
        /// edge with small stones, something a player walks onto and clears. This is masonry that
        /// came off the wall decades ago and was never picked up, so it is drawn from the wall's
        /// own vocabulary — <see cref="DrawCourse"/>'s block proportions, <see cref="ArtPalette.StoneRamp"/>,
        /// a lit top edge with a dark underside — only level, aligned and mortared has become
        /// tilted, chipped and half sunk.
        ///
        /// Three properties exist to stop a field of these reading as a 32 pixel grid, which is
        /// exactly what the first attempt at this band did:
        ///   - The base is ordinary <see cref="Ground"/>, untouched. Nothing shades the whole
        ///     tile, so there is no square edge for the eye to find; the only shading is the
        ///     contact shadow each block casts on the ground it sits on.
        ///   - Every coordinate wraps. A block placed near an edge runs off it and reappears on
        ///     the far side, so stone silhouettes straddle the seams instead of stopping short of
        ///     them, and the tile is seamless against a copy of itself.
        ///   - The stones gather around a clump centre that moves per variant, and the count runs
        ///     from two to eleven, so some tiles are nearly bare and some are a heap. A run of
        ///     ruin cells is uneven inside each cell as well as between them.
        /// </summary>
        public static PixelCanvas FallenWall(int seed)
        {
            PixelCanvas canvas = Ground(seed + 613);

            List<FallenBlock> blocks = new List<FallenBlock>();

            // Where the stone gathers on this tile, and how tightly. A small spread leaves most
            // of the tile bare; a large one scatters across the whole cell.
            int clumpX = ValueNoise.RangeInt(seed, 900, 0, Size);
            int clumpY = ValueNoise.RangeInt(seed, 901, 0, Size);
            int spread = ValueNoise.RangeInt(seed, 902, 8, 21);

            int count = ValueNoise.RangeInt(seed, 903, 2, 9);
            for (int i = 0; i < count; i++)
            {
                blocks.Add(ScatteredBlock(seed, i, clumpX, clumpY, spread));
            }

            // Two variants in five keep a piece of course together: three blocks that went over
            // as one and are still roughly in line, ends nearly touching. Nothing else on the
            // tile says "this was a wall" as plainly as a course that fell without breaking up.
            if (ValueNoise.Value01(seed, 904) < 0.4f)
            {
                AddToppledCourse(blocks, seed);
            }

            // Painter's order, lowest edge last, so a heap occludes the way a heap does.
            SortByBottomEdge(blocks);

            // Stone and its hard shadow go down block by block, so a block that came to rest on
            // another occludes it. The soft halo is collected across the whole tile and laid down
            // once at the end: blended per block it would darken twice wherever two stones lie
            // close enough for their halos to meet, and a twice-shaded pixel is a colour the
            // world palette does not have.
            bool[] occupied = new bool[Size * Size];
            bool[] halo = new bool[Size * Size];
            for (int i = 0; i < blocks.Count; i++)
            {
                DrawFallenBlock(canvas, blocks[i], seed, i, occupied, halo);
            }

            for (int i = 0; i < halo.Length; i++)
            {
                if (halo[i] && !occupied[i]) canvas.Blend(i % Size, i / Size, ArtPalette.SoftShade);
            }

            DrawSpall(canvas, blocks, seed);
            return canvas;
        }

        /// <summary>
        /// One block of the fallen wall. Coordinates are in tile space and wrap, so X and Y may
        /// sit outside 0..<see cref="Size"/> and the block simply crosses the tile edge.
        /// </summary>
        struct FallenBlock
        {
            public int X;
            public int Y;

            /// <summary>Along the long axis: the length of a course block, 6 to 12 pixels.</summary>
            public int Length;

            /// <summary>Across it: the depth of a course, 4 to 6 pixels.</summary>
            public int Thickness;

            /// <summary>True when the long axis runs down the tile: a block stood on its edge.</summary>
            public bool Upright;

            /// <summary>Pixels of run per pixel of rise. 0 is level, 3 is the steepest rest.</summary>
            public int SlopeRun;

            /// <summary>Which way the block leans, -1 or +1.</summary>
            public int SlopeDir;

            /// <summary>Index into <see cref="ArtPalette.StoneRamp"/> for the block's face.</summary>
            public int Shade;

            /// <summary>
            /// Sunk into the ground. It loses its dark underside line and its contact shadow,
            /// which is the whole cue: the dark line is what says a stone is resting on top of
            /// the ground, so a block without one reads as a block the ground has closed over.
            /// </summary>
            public bool Buried;

            /// <summary>Bit 1 knocks the corner off the head, bit 2 off the tail.</summary>
            public int Chip;

            public int Height { get { return Upright ? Length : Thickness; } }
        }

        static FallenBlock ScatteredBlock(int seed, int index, int clumpX, int clumpY, int spread)
        {
            int key = index * 7 + 300;
            FallenBlock block = new FallenBlock();

            // A few blocks came down on edge rather than flat. They stay short: a tall one reads
            // as a pillar still standing, which is the opposite of what this tile says.
            block.Upright = ValueNoise.Value01(seed, key) < 0.12f;
            block.Length = block.Upright
                ? ValueNoise.RangeInt(seed, key + 1, 5, 9)
                : ValueNoise.RangeInt(seed, key + 1, 6, 13);
            block.Thickness = ValueNoise.RangeInt(seed, key + 2, 4, 7);
            block.X = clumpX + ValueNoise.RangeInt(seed, key + 3, -spread, spread + 1);
            block.Y = clumpY + ValueNoise.RangeInt(seed, key + 4, -spread, spread + 1);

            // How far from level it came to rest. Two in five are flat, the rest lean; nothing
            // leans so hard that the staircase stops reading as one straight block.
            float tilt = ValueNoise.Value01(seed, key + 5);
            block.SlopeRun = tilt < 0.40f ? 0 : (tilt < 0.75f ? 5 : 3);
            block.SlopeDir = ValueNoise.Value01(seed, key + 6) < 0.5f ? -1 : 1;

            // Weighted toward the darker half of the ramp: stone that has been out in the
            // weather for decades, not a face that came off the saw this morning.
            float value = ValueNoise.Value01(seed, key + 7);
            block.Shade = value < 0.42f ? 1 : (value < 0.84f ? 2 : 3);
            block.Buried = ValueNoise.Value01(seed, key + 8) < 0.34f;
            if (block.Buried) block.Thickness = Mathf.Min(block.Thickness, 4);
            block.Chip = ValueNoise.RangeInt(seed, key + 9, 0, 4);
            return block;
        }

        static void AddToppledCourse(List<FallenBlock> blocks, int seed)
        {
            int x = ValueNoise.RangeInt(seed, 905, -8, Size);
            int y = ValueNoise.RangeInt(seed, 906, 0, Size);
            int direction = ValueNoise.Value01(seed, 907) < 0.5f ? -1 : 1;

            for (int i = 0; i < 3; i++)
            {
                int key = 910 + i * 6;
                FallenBlock block = new FallenBlock();
                block.Length = ValueNoise.RangeInt(seed, key, 8, 12);
                block.Thickness = 5;
                block.X = x;
                block.Y = y + ValueNoise.RangeInt(seed, key + 1, -1, 2);
                block.SlopeRun = 5;
                block.SlopeDir = direction;
                block.Shade = ValueNoise.RangeInt(seed, key + 2, 1, 4);
                block.Chip = ValueNoise.RangeInt(seed, key + 3, 0, 4);
                blocks.Add(block);

                // Ends nearly touching, the way blocks that fell together still sit.
                x += block.Length + ValueNoise.RangeInt(seed, key + 4, 1, 4);
                y += direction * 2;
            }
        }

        /// <summary>Insertion sort, because List.Sort is not stable and the art must be.</summary>
        static void SortByBottomEdge(List<FallenBlock> blocks)
        {
            for (int i = 1; i < blocks.Count; i++)
            {
                FallenBlock block = blocks[i];
                int bottom = block.Y + block.Height;
                int j = i - 1;
                while (j >= 0 && blocks[j].Y + blocks[j].Height > bottom)
                {
                    blocks[j + 1] = blocks[j];
                    j--;
                }
                blocks[j + 1] = block;
            }
        }

        /// <summary>
        /// One block: its bed of mortar shadow, its face, and the pitting on it.
        ///
        /// The block is rasterised into a scratch buffer before anything reaches the canvas,
        /// because the dark edge has to know the whole silhouette first. Drawn per column it
        /// would land inside the block wherever the tilt steps, and would double-blend the soft
        /// shadow wherever two columns share a neighbour.
        ///
        /// The dark edge is <see cref="ArtPalette.StoneDeep"/> and it is not decoration: it is the
        /// same value the standing wall packs between its blocks in <see cref="DrawCourses"/>.
        /// Without it a fallen block is a pale smear on pale ground — the two share the stone
        /// ramp — and with it the block reads as the same masonry, one course of it, on its side.
        /// </summary>
        static void DrawFallenBlock(PixelCanvas canvas, FallenBlock block, int seed, int index,
            bool[] occupied, bool[] halo)
        {
            Color32[] face = new Color32[Size * Size];
            bool[] solid = new bool[Size * Size];

            Color32[] ramp = ArtPalette.StoneRamp;
            Color32 body = ramp[block.Shade];
            Color32 lit = ramp[Mathf.Min(block.Shade + 1, ramp.Length - 1)];
            Color32 under = ramp[Mathf.Max(block.Shade - 1, 0)];

            for (int i = 0; i < block.Length; i++)
            {
                int cut = EndCut(block, i);
                int depth = block.Thickness - cut;
                if (depth < 2) continue;

                int drift = block.SlopeRun <= 0 ? 0 : (i / block.SlopeRun) * block.SlopeDir;

                for (int k = 0; k < depth; k++)
                {
                    Color32 colour = body;
                    bool farEdge = k == depth - 1;

                    if (block.Upright)
                    {
                        // Stood on its edge: we see the narrow face, lit down its near side.
                        if (k == 0) colour = lit;
                        else if (farEdge) colour = under;
                        if (i == 0) colour = lit;
                        else if (i == block.Length - 1 && !block.Buried) colour = under;
                        Mark(face, solid, block.X + cut + k + drift, block.Y + i, colour);
                    }
                    else
                    {
                        // Lying flat: the top face catches the light, the far edge falls away.
                        if (k == 0) colour = lit;
                        else if (farEdge && !block.Buried) colour = under;
                        if (i == block.Length - 1) colour = under;
                        Mark(face, solid, block.X + i, block.Y + cut + k + drift, colour);
                    }
                }
            }

            // Pitting and old mortar loss on the face. Weathering is what says "decades ago",
            // not "collapsed last night".
            int pits = block.Length * block.Thickness < 28 ? 0 : ValueNoise.RangeInt(seed, index * 31 + 11, 0, 3);
            for (int p = 0; p < pits; p++)
            {
                int px = block.X + ValueNoise.RangeInt(seed, index * 31 + 20 + p, 0, Mathf.Max(1, block.Upright ? block.Thickness : block.Length));
                int py = block.Y + ValueNoise.RangeInt(seed, index * 31 + 40 + p, 0, Mathf.Max(1, block.Height));
                int at = WrappedIndex(px, py);
                if (solid[at]) face[at] = ArtPalette.StoneDark;
            }

            DrawSeat(canvas, solid, block.Buried, occupied, halo);

            for (int i = 0; i < solid.Length; i++)
            {
                if (!solid[i]) continue;
                canvas.Set(i % Size, i / Size, face[i]);
                occupied[i] = true;
            }
        }

        /// <summary>
        /// How much of the block's depth is missing at column i: a corner knocked off in the
        /// fall. Two pixels at the very end, one beside it, and always off the upper edge, so a
        /// broken end reads as bevelled rather than sawn — which matters most where the block
        /// crosses a tile seam.
        /// </summary>
        static int EndCut(FallenBlock block, int i)
        {
            int cut = 0;
            if ((block.Chip & 1) != 0 && i <= 1) cut = 2 - i;
            if ((block.Chip & 2) != 0 && i >= block.Length - 2)
            {
                cut = Mathf.Max(cut, 2 - (block.Length - 1 - i));
            }
            return Mathf.Max(cut, 0);
        }

        /// <summary>
        /// What seats the stone on the ground: one pixel of <see cref="ArtPalette.StoneDeep"/>
        /// around the silhouette, then one pixel of soft shadow beyond it on the shaded side.
        /// This is the only shading the tile gets — nothing touches the ground the block is not
        /// standing on, which is why a field of these has no square in it.
        ///
        /// A block that is half sunk gets neither below its lower edge. The dark line is the
        /// "resting on top" cue, and its absence is what reads as "sunk in".
        /// </summary>
        static void DrawSeat(PixelCanvas canvas, bool[] solid, bool buried, bool[] occupied, bool[] halo)
        {
            bool[] rim = new bool[solid.Length];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    if (!solid[y * Size + x]) continue;

                    // Only the shaded side. A dark line all the way round turns a block into a
                    // sticker with an outline; light from the upper left means the shadow falls
                    // below and to the right, and the lit top and left edges are told apart from
                    // the ground by their own value instead.
                    MarkFree(solid, rim, x + 1, y);
                    if (buried) continue;
                    MarkFree(solid, rim, x, y + 1);
                    MarkFree(solid, rim, x + 1, y + 1);
                    MarkFree(solid, rim, x - 1, y + 1);
                }
            }

            if (!buried)
            {
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        if (!rim[y * Size + x]) continue;
                        MarkSoft(solid, rim, halo, x, y + 1);
                        MarkSoft(solid, rim, halo, x + 1, y);
                    }
                }
            }

            for (int i = 0; i < rim.Length; i++)
            {
                if (!rim[i]) continue;
                canvas.Set(i % Size, i / Size, ArtPalette.StoneDeep);
                occupied[i] = true;
            }
        }

        static void MarkFree(bool[] solid, bool[] rim, int x, int y)
        {
            int at = WrappedIndex(x, y);
            if (!solid[at]) rim[at] = true;
        }

        static void MarkSoft(bool[] solid, bool[] rim, bool[] halo, int x, int y)
        {
            int at = WrappedIndex(x, y);
            if (!solid[at] && !rim[at]) halo[at] = true;
        }

        /// <summary>
        /// Spall: the chips a block sheds where it lands, dropped beside the stone rather than
        /// anywhere on the tile. They exist so that a block cut by a tile edge never ends alone
        /// against clean ground, which is the last thing that would still let the eye find a seam.
        /// </summary>
        static void DrawSpall(PixelCanvas canvas, List<FallenBlock> blocks, int seed)
        {
            if (blocks.Count == 0) return;

            int chips = ValueNoise.RangeInt(seed, 960, 2, 8);
            for (int i = 0; i < chips; i++)
            {
                int key = 970 + i * 5;
                FallenBlock host = blocks[ValueNoise.RangeInt(seed, key, 0, blocks.Count)];
                int x = host.X + ValueNoise.RangeInt(seed, key + 1, -4, host.Length + 4);
                int y = host.Y + ValueNoise.RangeInt(seed, key + 2, -3, host.Height + 5);
                int width = ValueNoise.RangeInt(seed, key + 3, 1, 4);

                for (int k = 0; k < width; k++)
                {
                    SetWrapped(canvas, x + k, y, ArtPalette.StoneLight);
                    SetWrapped(canvas, x + k, y + 1, ArtPalette.StoneDark);
                }
            }
        }

        static void Mark(Color32[] face, bool[] solid, int x, int y, Color32 colour)
        {
            int at = WrappedIndex(x, y);
            face[at] = colour;
            solid[at] = true;
        }

        /// <summary>
        /// Index of a tile pixel, wrapped on both axes. Every coordinate in the fallen wall goes
        /// through here: that is what lets a block run off one edge and back on at the other, and
        /// so what puts stone across the seams instead of a bare gutter along them.
        /// </summary>
        static int WrappedIndex(int x, int y)
        {
            return WrapCoordinate(y) * Size + WrapCoordinate(x);
        }

        static void SetWrapped(PixelCanvas canvas, int x, int y, Color32 colour)
        {
            canvas.Set(WrapCoordinate(x), WrapCoordinate(y), colour);
        }

        static int WrapCoordinate(int value)
        {
            int wrapped = value % Size;
            return wrapped < 0 ? wrapped + Size : wrapped;
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
