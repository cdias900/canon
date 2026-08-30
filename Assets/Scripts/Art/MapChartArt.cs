using UnityEngine;

namespace SheepGate.Art
{
    /// <summary>The three states a stop on the journey map may show.</summary>
    public enum JourneyNodeState
    {
        Complete,
        Current,
        Locked
    }

    /// <summary>
    /// Image sprites and stable overlay anchors for the journey map.
    ///
    /// The generated PNGs are authored imagery, but they still enter through one code-owned seam:
    /// callers ask this class for a Sprite and never depend on importer slicing, inspector fields
    /// or scene GUIDs. The background is one image and every marker/reward is an isolated image with
    /// alpha; labels remain live localized UI so words are never baked into art.
    ///
    /// The procedural parchment chart below remains as a deliberate fallback. A missing image is
    /// loud in the log and still leaves a usable map rather than a blank modal.
    ///
    /// Overlay positions are normalised against the source image. That keeps the road stops and
    /// their labels together on every phone, because the sheet itself preserves this aspect ratio.
    /// </summary>
    public static class MapChartArt
    {
        public const int Width = 480;
        public const int Height = 1040;
        public const int PlaceCount = 4;

        const int CentreX = Width / 2;
        const int CentreY = Height / 2;

        // The island, in pixels. Wider than it is tall relative to the sheet, which is why the
        // title sits in the water above it and the key in the water below.
        const int IslandRadiusX = 186;
        const int IslandRadiusY = 396;

        const int BayDepth = 46;
        const int BayWidth = 78;
        const int BayNorthY = 150;
        const int BaySouthY = -205;

        const int CityRadius = 60;
        const int BorderInset = 9;

        const string MapResource = "Art/Map/map_background";
        const string CompleteNodeResource = "Art/Map/node_complete";
        const string CurrentNodeResource = "Art/Map/node_current";
        const string LockedNodeResource = "Art/Map/node_locked";
        const string ToolBagResource = "Art/Map/item_tool_bag";
        const string HeadscarfResource = "Art/Map/item_headscarf";
        const string ValleyMantleResource = "Art/Map/item_valley_mantle";

        /// <summary>The generated map is a wide 3:2 valley explored through a portrait viewport.</summary>
        public const float BackgroundAspect = 3f / 2f;

        /// <summary>Logical size of the pannable map content in canvas units.</summary>
        public static readonly Vector2 ContentSize = new Vector2(1536f, 1024f);

        /// <summary>
        /// The three clearings painted into the road, from the first stop at the lower left to the
        /// repaired gate in the north. UI coordinates use a bottom-left origin.
        /// </summary>
        static readonly Vector2[] JourneyAnchors =
        {
            new Vector2(0.19f, 0.25f),
            new Vector2(0.52f, 0.50f),
            new Vector2(0.88f, 0.83f)
        };

        /// <summary>
        /// Where the viewport centres for each day. A focus includes the node's card as well as its
        /// marker, which is why day two looks to the right and day three back to the left.
        /// </summary>
        static readonly Vector2[] JourneyFocusAnchors =
        {
            new Vector2(0.24f, 0.39f),
            new Vector2(0.70f, 0.53f),
            new Vector2(0.72f, 0.82f)
        };

        /// <summary>
        /// The complete opaque vocabulary of world art. Generated images are suggestions of shape;
        /// every shipped pixel is remapped onto these existing colours before a Sprite is created.
        /// </summary>
        static readonly Color32[] ExactPalette =
        {
            ArtPalette.Ink, ArtPalette.Shadow, ArtPalette.Neutral, ArtPalette.Light, ArtPalette.Paper,
            ArtPalette.StoneDeep, ArtPalette.StoneDark, ArtPalette.StoneMid,
            ArtPalette.StoneLight, ArtPalette.StonePale,
            ArtPalette.ClayDeep, ArtPalette.ClayDark, ArtPalette.ClayMid,
            ArtPalette.ClayLight, ArtPalette.ClayPale,
            ArtPalette.TealDeep, ArtPalette.TealDark, ArtPalette.TealMid,
            ArtPalette.TealLight, ArtPalette.TealPale
        };

        // Where the four shut cities sit. Same arrangement as the region the opening shows, moved
        // in from the coast so the drawn coastline still has room to run behind them.
        static readonly Vector2Int[] Places =
        {
            new Vector2Int(-70, 300),
            new Vector2Int(82, 236),
            new Vector2Int(-86, -228),
            new Vector2Int(76, -292)
        };

        static Sprite _sprite;
        static readonly System.Collections.Generic.Dictionary<string, Sprite> ImageSprites =
            new System.Collections.Generic.Dictionary<string, Sprite>(System.StringComparer.Ordinal);

        /// <summary>The chart. Drawn on the first call and kept.</summary>
        public static Sprite Get()
        {
            if (_sprite != null)
            {
                return _sprite;
            }

            _sprite = LoadImageSprite(MapResource, "map_progress_background");
            if (_sprite != null)
            {
                return _sprite;
            }

            var canvas = new PixelCanvas(Width, Height);
            Draw(canvas);

            Texture2D texture = canvas.ToTexture("map_chart");
            _sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, Width, Height),
                new Vector2(0.5f, 0.5f),
                1f,
                0u,
                SpriteMeshType.FullRect);
            _sprite.name = "map_chart";
            return _sprite;
        }

        /// <summary>How many real POC days the journey map contains.</summary>
        public static int JourneyCount
        {
            get { return JourneyAnchors.Length; }
        }

        /// <summary>Where one day marker sits on the generated road.</summary>
        public static Vector2 JourneyAnchor(int index)
        {
            return JourneyAnchors[Mathf.Clamp(index, 0, JourneyAnchors.Length - 1)];
        }

        /// <summary>Where the clipped viewport should open for one day.</summary>
        public static Vector2 JourneyFocusAnchor(int index)
        {
            return JourneyFocusAnchors[Mathf.Clamp(index, 0, JourneyFocusAnchors.Length - 1)];
        }

        /// <summary>The image sprite for one progression state.</summary>
        public static Sprite NodeSprite(JourneyNodeState state)
        {
            switch (state)
            {
                case JourneyNodeState.Complete:
                    return LoadImageSprite(CompleteNodeResource, "map_node_complete");
                case JourneyNodeState.Current:
                    return LoadImageSprite(CurrentNodeResource, "map_node_current");
                default:
                    return LoadImageSprite(LockedNodeResource, "map_node_locked");
            }
        }

        /// <summary>
        /// The featured item image beside each day. These ids are character_catalog.json ids, never
        /// player-facing copy; the display name comes from the loaded locale catalogue.
        /// </summary>
        public static Sprite RewardSprite(string itemId)
        {
            switch (itemId)
            {
                case "acc_tool_bag":
                    return LoadImageSprite(ToolBagResource, "map_reward_tool_bag");
                case "hair_headscarf":
                    return LoadImageSprite(HeadscarfResource, "map_reward_headscarf");
                case "outfit_valley_mantle":
                    return LoadImageSprite(ValleyMantleResource, "map_reward_valley_mantle");
                default:
                    Debug.LogWarning("[MapArt] No generated reward sprite is mapped for catalog item '" + itemId + "'.");
                    return null;
            }
        }

        static Sprite LoadImageSprite(string resourcePath, string spriteName)
        {
            Sprite cached;
            if (ImageSprites.TryGetValue(resourcePath, out cached) && cached != null)
            {
                return cached;
            }

            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                Debug.LogError("[MapArt] Missing generated image at Resources/" + resourcePath + ".png.");
                return null;
            }

            Texture2D exactTexture = RemapToExactPalette(texture, spriteName);

            Sprite sprite = Sprite.Create(
                exactTexture,
                new Rect(0f, 0f, exactTexture.width, exactTexture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect);
            sprite.name = spriteName;
            ImageSprites[resourcePath] = sprite;
            return sprite;
        }

        static Texture2D RemapToExactPalette(Texture2D source, string name)
        {
            Color32[] pixels;
            try
            {
                pixels = source.GetPixels32();
            }
            catch (UnityException exception)
            {
                Debug.LogError("[MapArt] Generated texture '" + source.name +
                    "' is not readable, so its palette cannot be enforced: " + exception.Message);
                return source;
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 128)
                {
                    pixels[i] = ArtPalette.Transparent;
                    continue;
                }

                pixels[i] = NearestPaletteColour(pixels[i]);
            }

            var exact = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            exact.name = "palette_" + name;
            exact.filterMode = FilterMode.Point;
            exact.wrapMode = TextureWrapMode.Clamp;
            exact.SetPixels32(pixels);
            exact.Apply(false, false);
            return exact;
        }

        static Color32 NearestPaletteColour(Color32 source)
        {
            int bestDistance = int.MaxValue;
            Color32 best = ExactPalette[0];
            for (int i = 0; i < ExactPalette.Length; i++)
            {
                int red = source.r - ExactPalette[i].r;
                int green = source.g - ExactPalette[i].g;
                int blue = source.b - ExactPalette[i].b;
                int distance = red * red + green * green + blue * blue;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                best = ExactPalette[i];
            }

            best.a = 255;
            return best;
        }

        /// <summary>Used by the built-player run to prove no generated colour escaped the remap.</summary>
        public static bool UsesExactPalette(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null || !sprite.texture.isReadable)
            {
                return false;
            }

            Color32[] pixels = sprite.texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a == 0)
                {
                    continue;
                }

                if (pixels[i].a != 255 || !PaletteContains(pixels[i]))
                {
                    return false;
                }
            }

            return true;
        }

        static bool PaletteContains(Color32 colour)
        {
            for (int i = 0; i < ExactPalette.Length; i++)
            {
                if (colour.r == ExactPalette[i].r && colour.g == ExactPalette[i].g &&
                    colour.b == ExactPalette[i].b)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Where the player's own city sits, as a fraction of the sheet.</summary>
        public static Vector2 HomeAnchor
        {
            get { return new Vector2(0.5f, 0.5f); }
        }

        /// <summary>Where one of the shut cities sits, as a fraction of the sheet.</summary>
        public static Vector2 PlaceAnchor(int index)
        {
            Vector2Int place = Places[Mathf.Clamp(index, 0, Places.Length - 1)];

            // PixelCanvas has a top-left origin and a UI anchor has a bottom-left one, so the
            // vertical fraction is inverted on the way out. Without this every plate lands on the
            // city diagonally opposite the one it names.
            return new Vector2(
                (CentreX + place.x) / (float)Width,
                1f - (CentreY + place.y) / (float)Height);
        }

        /// <summary>How far below a city marker its name plate should hang, as a fraction.</summary>
        public static float PlateDrop
        {
            get { return 34f / Height; }
        }

        // ------------------------------------------------------------------ the drawing

        static void Draw(PixelCanvas canvas)
        {
            canvas.Clear(ArtPalette.Paper);

            DrawSea(canvas);
            DrawCoast(canvas);
            DrawUplands(canvas);
            DrawLowlands(canvas);
            DrawWadi(canvas);
            DrawRuins(canvas);

            for (int i = 0; i < Places.Length; i++)
            {
                DrawRoad(canvas, Places[i], i);
            }

            for (int i = 0; i < Places.Length; i++)
            {
                DrawShutCity(canvas, Places[i]);
            }

            DrawHomeCity(canvas);
            DrawBorder(canvas);
        }

        /// <summary>
        /// The water: a light wash, three bands of dashes following the coast, and stipple. Charts
        /// band their shorelines like this because it says "this edge is water" without colouring
        /// half the sheet in, which would drown the linework the rest of the map is made of.
        /// </summary>
        static void DrawSea(PixelCanvas canvas)
        {
            for (int y = 0; y < Height; y++)
            {
                float world = y - CentreY;
                int left = CentreX - CoastOffset(world, -1);
                int right = CentreX + CoastOffset(world, 1);

                for (int x = 0; x < Width; x++)
                {
                    if (x > left && x < right)
                    {
                        continue;
                    }

                    // A faint wash, then a sparse stipple on top of it, both deterministic.
                    canvas.Blend(x, y, SeaWash);
                    if (ValueNoise.Value01(SeaSeed, y * Width + x) > 0.94f)
                    {
                        canvas.Blend(x, y, SeaStipple);
                    }
                }
            }

            DrawSwell(canvas, 13);
            DrawSwell(canvas, 26);
            DrawSwell(canvas, 41);
        }

        /// <summary>One band of dashes running parallel to the coast, a fixed distance out.</summary>
        static void DrawSwell(PixelCanvas canvas, int distance)
        {
            for (int y = 0; y < Height; y++)
            {
                float world = y - CentreY;

                // Dashes, not a solid line: the gaps are what keep three parallel bands from
                // reading as a second coastline.
                if (((y / 7) + distance) % 3 == 0)
                {
                    continue;
                }

                int left = CentreX - CoastOffset(world, -1) - distance;
                int right = CentreX + CoastOffset(world, 1) + distance;

                if (CoastOffset(world, -1) > 0)
                {
                    canvas.Blend(left, y, SwellInk);
                }

                if (CoastOffset(world, 1) > 0)
                {
                    canvas.Blend(right, y, SwellInk);
                }
            }
        }

        /// <summary>The coastline itself: two pixels of ink, and nothing else on it.</summary>
        static void DrawCoast(PixelCanvas canvas)
        {
            for (int y = 0; y < Height; y++)
            {
                float world = y - CentreY;
                int leftReach = CoastOffset(world, -1);
                int rightReach = CoastOffset(world, 1);
                if (leftReach <= 0 && rightReach <= 0)
                {
                    continue;
                }

                canvas.Set(CentreX - leftReach, y, ArtPalette.Ink);
                canvas.Set(CentreX - leftReach + 1, y, ArtPalette.Ink);
                canvas.Set(CentreX + rightReach, y, ArtPalette.Ink);
                canvas.Set(CentreX + rightReach - 1, y, ArtPalette.Ink);
            }
        }

        /// <summary>
        /// High ground in the north, drawn the way a surveyor draws it: rows of small carets. No
        /// shading, because shading on parchment turns into a stain at this size.
        ///
        /// North is negative y here: this canvas has a top-left origin, so the half of the sheet a
        /// reader calls "up" is the half with the smaller numbers.
        /// </summary>
        static void DrawUplands(PixelCanvas canvas)
        {
            for (int i = 0; i < 78; i++)
            {
                int x = CentreX + ValueNoise.RangeInt(UplandSeed, i * 2, -150, 150);
                int y = CentreY + ValueNoise.RangeInt(UplandSeed, i * 2 + 1, -350, -60);
                if (!OnLand(x, y, 26))
                {
                    continue;
                }

                int size = 4 + ValueNoise.RangeInt(UplandSeed, i + 900, 0, 4);
                canvas.Line(x - size, y, x, y + size, ArtPalette.Ink);
                canvas.Line(x, y + size, x + size, y, ArtPalette.Ink);
            }
        }

        /// <summary>
        /// Broken ground in the south: short horizontal ticks in loose rows. The same mark a chart
        /// uses for scrub, and the reason the two halves of the island read as different country.
        /// </summary>
        static void DrawLowlands(PixelCanvas canvas)
        {
            for (int i = 0; i < 108; i++)
            {
                int x = CentreX + ValueNoise.RangeInt(LowlandSeed, i * 2, -155, 155);
                int y = CentreY + ValueNoise.RangeInt(LowlandSeed, i * 2 + 1, 60, 350);
                if (!OnLand(x, y, 22))
                {
                    continue;
                }

                int length = 3 + ValueNoise.RangeInt(LowlandSeed, i + 700, 0, 4);
                canvas.HLine(x, y, length, ArtPalette.Shadow);
                canvas.HLine(x + 2, y + 2, length - 1, ArtPalette.Shadow);
            }
        }

        /// <summary>
        /// The dry watercourse: two thin lines running from the high ground, round the city and
        /// out to the sea.
        ///
        /// It is the one feature on the sheet that crosses the whole island, and that is its job.
        /// A map needs at least one line that ties the far ends together, or the eye reads the
        /// north and the south as two unrelated drawings that happen to share a coast.
        /// </summary>
        static void DrawWadi(PixelCanvas canvas)
        {
            var start = new Vector2(-96f, -352f);
            var control = new Vector2(-150f, -40f);
            var end = new Vector2(-58f, 372f);

            const int steps = 190;
            Vector2 previous = start;

            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 point = Quadratic(start, control, end, t);

                int ax = CentreX + Mathf.RoundToInt(previous.x);
                int ay = CentreY + Mathf.RoundToInt(previous.y);
                int bx = CentreX + Mathf.RoundToInt(point.x);
                int by = CentreY + Mathf.RoundToInt(point.y);

                // Two banks, a few pixels apart: one line would read as another road.
                canvas.Line(ax, ay, bx, by, WadiInk);
                canvas.Line(ax + 4, ay + 1, bx + 4, by + 1, WadiInk);

                previous = point;
            }
        }

        /// <summary>
        /// Ruins: a broken rectangle each, scattered where nothing else is drawn. This is a map of
        /// a country whose cities came down, and the empty ground should say so somewhere other
        /// than in the caption.
        /// </summary>
        static void DrawRuins(PixelCanvas canvas)
        {
            for (int i = 0; i < 11; i++)
            {
                int x = CentreX + ValueNoise.RangeInt(RuinSeed, i * 2, -140, 140);
                int y = CentreY + ValueNoise.RangeInt(RuinSeed, i * 2 + 1, -330, 330);
                if (!OnLand(x, y, 30))
                {
                    continue;
                }

                canvas.HLine(x, y, 11, ArtPalette.Shadow);
                canvas.VLine(x, y, 8, ArtPalette.Shadow);
                canvas.VLine(x + 10, y + 3, 5, ArtPalette.Shadow);
                canvas.HLine(x + 3, y + 8, 4, ArtPalette.Shadow);
            }
        }

        /// <summary>A dashed road, bowed to one side, from the city wall out to a neighbour.</summary>
        static void DrawRoad(PixelCanvas canvas, Vector2Int place, int index)
        {
            var target = new Vector2(place.x, place.y);
            float length = target.magnitude;
            if (length < 1f)
            {
                return;
            }

            Vector2 heading = target / length;
            Vector2 start = heading * (CityRadius + 4);
            Vector2 end = target - heading * 26f;
            Vector2 perpendicular = new Vector2(-heading.y, heading.x);
            Vector2 control = (start + end) * 0.5f + perpendicular * (index % 2 == 0 ? 46f : -46f);

            const int steps = 150;
            Vector2 previous = start;

            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 point = Quadratic(start, control, end, t);

                // Every third step is left out, which is what makes it a track rather than a wall.
                if (i % 3 != 0)
                {
                    canvas.Line(
                        CentreX + Mathf.RoundToInt(previous.x), CentreY + Mathf.RoundToInt(previous.y),
                        CentreX + Mathf.RoundToInt(point.x), CentreY + Mathf.RoundToInt(point.y),
                        RoadInk);
                }

                previous = point;
            }
        }

        /// <summary>
        /// A shut city: a walled square with its gate barred. Small, and identical for all four,
        /// because the map is not allowed to claim anything about places nobody has built yet.
        /// </summary>
        static void DrawShutCity(PixelCanvas canvas, Vector2Int place)
        {
            int x = CentreX + place.x;
            int y = CentreY + place.y;

            canvas.FillRect(x - 17, y - 17, 34, 34, ArtPalette.Paper);
            canvas.Outline(x - 17, y - 17, 34, 34, ArtPalette.Ink);
            canvas.Outline(x - 14, y - 14, 28, 28, ArtPalette.Neutral);

            // Towers on the corners, then the barred gate.
            canvas.FillRect(x - 21, y - 21, 8, 8, ArtPalette.Ink);
            canvas.FillRect(x + 13, y - 21, 8, 8, ArtPalette.Ink);
            canvas.FillRect(x - 21, y + 13, 8, 8, ArtPalette.Ink);
            canvas.FillRect(x + 13, y + 13, 8, 8, ArtPalette.Ink);

            canvas.FillRect(x - 5, y - 17, 10, 11, ArtPalette.Ink);
            canvas.HLine(x - 10, y - 11, 20, ArtPalette.Ink);
        }

        /// <summary>
        /// The player's city: a ring of wall with the breaches left open, and the gate marked.
        ///
        /// The gaps are the whole point. This is a map of a city whose wall is down, and a closed
        /// ring would be the one thing on the sheet that lied about the state of the place.
        /// </summary>
        static void DrawHomeCity(PixelCanvas canvas)
        {
            canvas.FillCircle(CentreX, CentreY, CityRadius - 4, ArtPalette.Paper);

            // The wall, drawn as an arc of short strokes with four gaps left in it.
            for (int degrees = 0; degrees < 360; degrees += 3)
            {
                if (degrees % 90 < 16)
                {
                    continue;   // a breach
                }

                float radians = degrees * Mathf.Deg2Rad;
                int x = CentreX + Mathf.RoundToInt(Mathf.Cos(radians) * CityRadius);
                int y = CentreY + Mathf.RoundToInt(Mathf.Sin(radians) * CityRadius);

                canvas.Set(x, y, ArtPalette.Ink);
                canvas.Set(x + 1, y, ArtPalette.Ink);
                canvas.Set(x, y + 1, ArtPalette.Ink);
            }

            // Rubble against the inside of the wall, and a few roofs standing.
            for (int i = 0; i < 26; i++)
            {
                int x = CentreX + ValueNoise.RangeInt(CitySeed, i * 2, -34, 34);
                int y = CentreY + ValueNoise.RangeInt(CitySeed, i * 2 + 1, -34, 34);
                if ((x - CentreX) * (x - CentreX) + (y - CentreY) * (y - CentreY) > 1100)
                {
                    continue;
                }

                canvas.FillRect(x, y, 5, 4, ArtPalette.Shadow);
                canvas.Set(x, y + 4, ArtPalette.Ink);
            }

            // The sheep gate itself, on the north side: two posts and a lintel.
            canvas.FillRect(CentreX - 7, CentreY - CityRadius - 6, 3, 9, ArtPalette.ClayDark);
            canvas.FillRect(CentreX + 4, CentreY - CityRadius - 6, 3, 9, ArtPalette.ClayDark);
            canvas.HLine(CentreX - 7, CentreY - CityRadius - 7, 14, ArtPalette.ClayDark);
        }

        /// <summary>A double rule around the sheet, the way a drawn chart is bounded.</summary>
        static void DrawBorder(PixelCanvas canvas)
        {
            canvas.Outline(BorderInset, BorderInset, Width - BorderInset * 2, Height - BorderInset * 2, ArtPalette.Ink);
            canvas.Outline(BorderInset + 4, BorderInset + 4, Width - (BorderInset + 4) * 2, Height - (BorderInset + 4) * 2, ArtPalette.Neutral);
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>
        /// How far the coast lies from the centre line at one height, on one side.
        ///
        /// The sides are computed separately and swell on different phases, and each carries a bay
        /// at its own height. A symmetrical outline reads as a drawn oval however much is put on
        /// top of it; an uneven one reads as a coast, and the two bays give the eye something
        /// fixed to navigate by.
        /// </summary>
        static int CoastOffset(float y, int side)
        {
            float t = Mathf.Abs(y) / IslandRadiusY;
            if (t >= 1f)
            {
                return 0;
            }

            float basis = IslandRadiusX * Mathf.Sqrt(1f - t * t);
            float phase = side > 0 ? 0f : 2.1f;
            // Three frequencies, not one. A single sine gives a smooth potato; the short one is
            // what puts headlands and coves on the outline at the size the eye reads them.
            float swell = Mathf.Sin(y * 0.031f + phase) * 13f
                        + Mathf.Sin(y * 0.011f + 1.7f + phase) * 8f
                        + Mathf.Sin(y * 0.097f + 0.6f + phase) * 5f;
            float bay = side > 0 ? Bay(y, BayNorthY) : Bay(y, BaySouthY);

            return Mathf.Max(0, Mathf.RoundToInt(basis + swell - bay));
        }

        static float Bay(float y, float at)
        {
            float distance = (y - at) / BayWidth;
            return BayDepth * Mathf.Exp(-distance * distance);
        }

        /// <summary>True when a point is on land with the given clearance from the water.</summary>
        static bool OnLand(int x, int y, int clearance)
        {
            float world = y - CentreY;
            int offset = x >= CentreX ? CoastOffset(world, 1) : CoastOffset(world, -1);
            if (offset <= 0)
            {
                return false;
            }

            if (Mathf.Abs(x - CentreX) > offset - clearance)
            {
                return false;
            }

            // Nothing is drawn over the city or over the ring of ground around it.
            int dx = x - CentreX;
            int dy = y - CentreY;
            return dx * dx + dy * dy > (CityRadius + 22) * (CityRadius + 22);
        }

        static Vector2 Quadratic(Vector2 a, Vector2 control, Vector2 b, float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * a + 2f * inverse * t * control + t * t * b;
        }

        // ------------------------------------------------------------------ ink

        static readonly Color32 SeaWash = new Color32(76, 133, 124, 46);
        static readonly Color32 SeaStipple = new Color32(51, 96, 90, 120);
        static readonly Color32 SwellInk = new Color32(51, 96, 90, 190);
        static readonly Color32 RoadInk = new Color32(58, 54, 47, 210);
        static readonly Color32 WadiInk = new Color32(76, 133, 124, 150);

        static readonly int SeaSeed = ValueNoise.SeedFrom("chart_sea");
        static readonly int UplandSeed = ValueNoise.SeedFrom("chart_upland");
        static readonly int LowlandSeed = ValueNoise.SeedFrom("chart_lowland");
        static readonly int CitySeed = ValueNoise.SeedFrom("chart_city");
        static readonly int RuinSeed = ValueNoise.SeedFrom("chart_ruin");
    }
}
