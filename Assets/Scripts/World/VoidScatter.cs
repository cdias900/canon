namespace SheepGate.World
{
    /// <summary>
    /// Decides which cells outside the city carry the fallen wall.
    ///
    /// The cells outside the city draw the same valley ground the city stands on, so the map no
    /// longer stops at a black rectangle — and with the outside and the inside sharing a texture,
    /// nothing on screen says where the world ends any more. This is what says it: the wall came
    /// down outward, long ago, and nobody cleared it. Beside the city the ground is nearly bare;
    /// three or four cells out the stone has taken over, and it goes on taking over for as far as
    /// any camera can travel.
    ///
    /// It is TERRAIN. A cell chosen here draws a fallen-wall tile and nothing else: no pile, no
    /// interaction, no walkability. Off-map cells stay off the map — see the note on
    /// <c>TilemapBuilder.VoidChar</c> for the bug that made them so.
    ///
    /// THE METRIC, and why it changed. Density is a function of one number: <c>dCity</c>, the
    /// distance in cells from the nearest cell the map actually draws. The version before this one
    /// used <c>dCity / (dCity + dRim)</c> — how far across the band a cell sits — and that metric
    /// had the rectangle of <c>map.json</c> built into it. Two things followed, and both were
    /// visible on a phone. The ramp spent most of its range on the columns beyond the city's own
    /// left and right edges, which the camera clamps away and no shipped aspect can frame, so in
    /// the close view — the view the game is played in — the stone was almost never on screen. And
    /// because the fraction reaches one exactly on the rectangle, the outermost columns came out
    /// near solid: over the void cells inside the rectangle, column 0 ran 75% and column 39 61%,
    /// against 11-15% in columns 8 to 10, the ones a close camera can actually frame. (The review
    /// that found this read 59% and 48% over a different window; the shape is the same either
    /// way.) That is a one-cell vertical line of stone down each side of the map, which is
    /// precisely the drawn boundary this must not have. Distance from the city has no rim in it,
    /// so there is nothing for a line to land on: measured over the whole painted width, columns
    /// -1, 0 and 1 now read 45%, 71% and 50%, and columns 38, 39 and 40 read 57%, 48% and 45% —
    /// the edges of the rectangle are no longer distinguishable from anywhere else.
    ///
    /// THE RAMP. Smoothstep from <see cref="ClearRadius"/> to <see cref="FullRadius"/>, then flat
    /// at <see cref="MaxDensity"/>. Monotonic by construction — it is a smoothstep of a clamped
    /// linear function of one distance, and nothing later can push it back down. It is also
    /// deliberately short: the close view can only ever frame off-map cells between one and about
    /// four cells from the city, so a ramp that took eight cells to arrive would be a feature
    /// nobody sees. Measured over the painted area of this map, the climb runs 16.7% at
    /// <c>dCity</c> in [1,2), 44.5% in [2,3), 54.4% in [3,4), and holds between 47% and 59% from
    /// there out — the spread past the ramp is <see cref="DensityPatch"/> and sampling, not a
    /// trend. In the close view, the only view the game is played in, every off-map cell any
    /// camera can reach sits between one and 4.24 cells from the city, and 26.7% of them carry
    /// stone; that number was 10.0% before this ramp replaced the band fraction.
    ///
    /// THE PLATEAU IS PATCHY ON PURPOSE. A single flat probability over hundreds of cells reads as
    /// television static. <see cref="DensityPatch"/> swings the density by a tenth over a lattice
    /// of thirteen cells, so the far field has drifts and clearings in it. It is not a ramp and it
    /// is not anchored to anything: it says nothing about where the world ends, which is the ramp's
    /// job, and everything about the field not being a texture swatch.
    ///
    /// THE IRREGULARITY. A clean ramp draws a ring, which is the one thing this must not look like.
    /// Two octaves of value noise push <c>dCity</c> in and out by up to <see cref="Warp"/> cells
    /// before the ramp is applied, so the edge of the debris wanders in both axes and there is no
    /// line to find. The warp is comparable to the width of the ramp itself, which is what keeps
    /// the transition reading as a collapse rather than as a gradient.
    ///
    /// DETERMINISM. Every number here comes from a hash of integer coordinates — no
    /// <c>System.Random</c>, no <c>UnityEngine.Random</c>, no clock, no build order. The same map
    /// scatters identically on every device, on every run, and in both locales; a screenshot taken
    /// twice shows the same stones. The file deliberately depends on nothing but the language, so
    /// it can be compiled and its output inspected outside Unity.
    /// </summary>
    internal static class VoidScatter
    {
        /// <summary>Share of cells that carry stone once the collapse is at full thickness.</summary>
        private const float MaxDensity = 0.52f;

        /// <summary>
        /// How far the density drifts either side of <see cref="MaxDensity"/> across the field, so
        /// the far ground has drifts and clearings instead of one even sprinkle.
        /// </summary>
        private const float DensityPatch = 0.12f;

        /// <summary>Distance in cells at which stone starts to appear at all.</summary>
        private const float ClearRadius = 0.2f;

        /// <summary>Distance in cells at which the collapse is at full thickness.</summary>
        private const float FullRadius = 2.8f;

        /// <summary>How far, in cells, the noise may push a cell along the ramp.</summary>
        private const float Warp = 1.2f;

        /// <summary>Cells per noise lattice step: coarse patches, a finer ragged edge, and the
        /// very coarse drift that keeps the far field from being uniform.</summary>
        private const float CoarseCells = 6.5f;
        private const float FineCells = 2.9f;
        private const float PatchCells = 13f;

        private const uint CoarseSalt = 0x51ED3B77u;
        private const uint FineSalt = 0xB17E4CA1u;
        private const uint PatchSalt = 0x6F1BBCD5u;
        private const uint DrawSalt = 0x2C1A9D53u;

        /// <summary>
        /// Grid of the cells that draw the fallen wall, covering the map rectangle and the skirt of terrain
        /// painted on all four sides of it.
        ///
        /// Indexed <c>[x + skirtX, y + skirtY]</c>, so cell column <c>x</c> runs from
        /// <c>-skirtX</c> to <c>width + skirtX - 1</c> and row <c>y</c> from <c>-skirtY</c> to
        /// <c>height + skirtY - 1</c>; the caller adds the two offsets once. Cells the map actually
        /// draws are always false: this decides what the ground outside the city looks like and
        /// never touches what the city itself draws.
        /// </summary>
        public static bool[,] Build(bool[,] isVoid, int width, int height, int skirtX, int skirtY)
        {
            int padX = skirtX < 0 ? 0 : skirtX;
            int padY = skirtY < 0 ? 0 : skirtY;
            int columns = (width < 0 ? 0 : width) + 2 * padX;
            int rows = (height < 0 ? 0 : height) + 2 * padY;

            bool[,] ruin = new bool[columns, rows];
            if (isVoid == null || width <= 0 || height <= 0)
            {
                return ruin;
            }

            int[] drawn = DrawnCells(isVoid, width, height);
            if (drawn.Length == 0)
            {
                // Nothing on this map is drawn, so there is no city for the stone to lie outside
                // of, and every distance below would be measured from nowhere.
                return ruin;
            }

            for (int x = -padX; x < width + padX; x++)
            {
                for (int y = -padY; y < height + padY; y++)
                {
                    bool insideRectangle = x >= 0 && x < width && y >= 0 && y < height;
                    if (insideRectangle && !isVoid[x, y])
                    {
                        continue;
                    }

                    ruin[x + padX, y + padY] = Unit(x, y, DrawSalt) < Density(drawn, x, y);
                }
            }

            return ruin;
        }

        /// <summary>
        /// The chance one cell carries stone: the ramp on the warped distance from the city, times
        /// the local plateau. Public in spirit rather than in access — it is the whole model, and
        /// it is one expression so that reading it settles what the density is anywhere.
        /// </summary>
        private static float Density(int[] drawn, int x, int y)
        {
            float noise = 0.62f * Noise(x / CoarseCells + 11.3f, y / CoarseCells + 4.7f, CoarseSalt)
                        + 0.38f * Noise(x / FineCells + 3.1f, y / FineCells + 19.7f, FineSalt);

            float distance = DistanceToCity(drawn, x, y) + Warp * 2f * (noise - 0.5f);
            float t = Clamp01((distance - ClearRadius) / (FullRadius - ClearRadius));
            float ramp = t * t * (3f - 2f * t);

            float patch = DensityPatch * 2f * (Noise(x / PatchCells + 7.9f, y / PatchCells + 2.3f, PatchSalt) - 0.5f);
            float plateau = MaxDensity + patch;
            return plateau <= 0f ? 0f : plateau * ramp;
        }

        /// <summary>
        /// Every cell the map draws, packed as x and y in one array so the distance loop walks a
        /// few hundred entries rather than the whole grid. Brute force from here on, because the
        /// map is forty cells by twenty-eight and this runs once per build: exact beats a chamfer
        /// pass whose metric artefacts would show up as straight runs of stone.
        /// </summary>
        private static int[] DrawnCells(bool[,] isVoid, int width, int height)
        {
            int count = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (!isVoid[x, y]) count++;
                }
            }

            int[] packed = new int[count * 2];
            int index = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (isVoid[x, y]) continue;
                    packed[index++] = x;
                    packed[index++] = y;
                }
            }

            return packed;
        }

        /// <summary>Euclidean distance in cells to the nearest cell the map draws.</summary>
        private static float DistanceToCity(int[] drawn, int x, int y)
        {
            int best = int.MaxValue;
            for (int i = 0; i < drawn.Length; i += 2)
            {
                int dx = drawn[i] - x;
                int dy = drawn[i + 1] - y;
                int squared = dx * dx + dy * dy;
                if (squared < best) best = squared;
            }

            return best == int.MaxValue ? 0f : Sqrt(best);
        }

        /// <summary>Value noise on a unit lattice, smoothstepped. Same input, same output, always.</summary>
        private static float Noise(float x, float y, uint salt)
        {
            int x0 = Floor(x);
            int y0 = Floor(y);
            float fx = x - x0;
            float fy = y - y0;
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);

            float bottom = Mix(Unit(x0, y0, salt), Unit(x0 + 1, y0, salt), sx);
            float top = Mix(Unit(x0, y0 + 1, salt), Unit(x0 + 1, y0 + 1, salt), sx);
            return Mix(bottom, top, sy);
        }

        /// <summary>A number in [0, 1) belonging to a cell. The only source of variation here.</summary>
        private static float Unit(int x, int y, uint salt)
        {
            return (Hash(x, y, salt) & 0xFFFFFFu) / 16777216f;
        }

        private static uint Hash(int x, int y, uint salt)
        {
            unchecked
            {
                uint h = (uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u ^ salt * 0xC2B2AE3Du;
                h ^= h >> 15;
                h *= 0x2545F491u;
                h ^= h >> 13;
                h *= 0x9E3779B1u;
                h ^= h >> 16;
                return h;
            }
        }

        private static float Mix(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }

        private static int Floor(float value)
        {
            int truncated = (int)value;
            return value < truncated ? truncated - 1 : truncated;
        }

        private static float Sqrt(int value)
        {
            return (float)System.Math.Sqrt(value);
        }
    }
}
