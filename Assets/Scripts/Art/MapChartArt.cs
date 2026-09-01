using SheepGate.Core;
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
    /// The procedural parchment chart below remains as a deliberate fallback. A missing background
    /// or marker image is loud in the log and still leaves a usable map rather than a blank modal.
    /// A missing featured-item image is the one quiet miss, and deliberately so: item art arrives
    /// stage by stage, so an absent file there is a schedule, not a fault, and it gets a drawn
    /// stand-in instead of a log line repeated on every selection.
    ///
    /// Overlay positions are normalised against the source image. That keeps the road stops and
    /// their labels together on every phone, because the sheet itself preserves this aspect ratio.
    ///
    /// This class reads <see cref="GameData.Stages"/>, which is the one place the art layer looks
    /// outward. It is deliberate: how many stops the road has is a property of the season, not of
    /// the drawing, and the alternative — an anchor count kept in this file and a stage count kept
    /// in stages.json — is precisely the pair of disagreeing sources that used to draw three nodes
    /// and log nothing. Where each stop SITS stays a property of the drawing, and is answered here.
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

        /// <summary>Where a featured item's drawn image lives, before its catalogue name.</summary>
        const string ItemResourcePrefix = "Art/Map/item_";

        /// <summary>Slot prefixes a catalogue id may carry, longest first so hair_ cannot eat h.</summary>
        static readonly string[] SlotPrefixes = { "outfit_", "hair_", "acc_" };

        /// <summary>Side of the stand-in reward image. The drawn items are 160 square.</summary>
        const int RewardIconPixels = 128;

        /// <summary>The generated map is a wide 3:2 valley explored through a portrait viewport.</summary>
        public const float BackgroundAspect = 3f / 2f;

        /// <summary>Logical size of the pannable map content in canvas units.</summary>
        public static readonly Vector2 ContentSize = new Vector2(1536f, 1024f);

        /// <summary>
        /// The road painted into map_background.png, traced, as a normalised polyline running from
        /// the village clearing at the lower left to the repaired gate in the north. UI coordinates
        /// use a bottom-left origin.
        ///
        /// This is a measurement of the shipped image, not a design: the pale track was isolated
        /// from the artwork by colour, its centreline followed from clearing to clearing, and the
        /// result resampled to twenty-five evenly spaced vertices. Every vertex but one lies on
        /// painted sand; the exception is the wooden bridge over the inlet, which is road.
        ///
        /// It replaces the three fixed anchors this file used to carry, and it exists because a
        /// season is no longer three stages long. Three points could name the three clearings the
        /// artist painted; they could not answer where a fourth, fifth or ninth stop goes, and the
        /// straight line between them leaves the track and crosses open scrub and water. A road the
        /// nodes are spaced ALONG answers that for any stage count, and keeps every marker on
        /// something the player can see is a road.
        ///
        /// The three painted clearings sit at 0.00, 0.50 and 1.00 of this road's length, which is
        /// why a nine-stage season lands its first, fifth and last stops in them and the six others
        /// on open track. That is a real cosmetic weakness and it is accepted knowingly: fewer
        /// waypoint markings than stops reads as a longer journey, not as a broken one. The two
        /// honest fixes — re-authoring the PNG with nine clearings, or returning to the procedural
        /// chart below and drawing the stops — are art work with no artist on the team.
        ///
        /// IF THE BACKGROUND IMAGE IS EVER REPLACED, THIS ARRAY IS WRONG AND MUST BE RE-TRACED.
        /// Nothing at runtime can tell: the nodes would simply sit on whatever the new picture
        /// happens to have painted where the old road ran.
        /// </summary>
        static readonly Vector2[] RoadPath =
        {
            new Vector2(0.152f, 0.251f), new Vector2(0.187f, 0.263f), new Vector2(0.221f, 0.272f),
            new Vector2(0.252f, 0.297f), new Vector2(0.279f, 0.332f), new Vector2(0.308f, 0.362f),
            new Vector2(0.335f, 0.395f), new Vector2(0.364f, 0.427f), new Vector2(0.394f, 0.453f),
            new Vector2(0.429f, 0.456f), new Vector2(0.463f, 0.466f), new Vector2(0.488f, 0.504f),
            new Vector2(0.512f, 0.542f), new Vector2(0.546f, 0.545f), new Vector2(0.577f, 0.519f),
            new Vector2(0.612f, 0.511f), new Vector2(0.645f, 0.524f), new Vector2(0.675f, 0.553f),
            new Vector2(0.709f, 0.565f), new Vector2(0.742f, 0.584f), new Vector2(0.772f, 0.612f),
            new Vector2(0.802f, 0.639f), new Vector2(0.830f, 0.671f), new Vector2(0.838f, 0.722f),
            new Vector2(0.846f, 0.774f)
        };

        /// <summary>
        /// How many stops the road shows when the stage table has not loaded. One per painted
        /// clearing, which is what the map drew before there was a stage table to ask.
        /// </summary>
        const int FallbackJourneyCount = 3;

        /// <summary>
        /// How far a stage's declared map_anchor may sit from the road before it is reported, in
        /// canvas units of <see cref="ContentSize"/>. Under a third of the gap between two stops on
        /// a nine-stage road, so a declared anchor inside the tolerance still names the same stop.
        /// </summary>
        const float DeclaredAnchorTolerance = 48f;

        /// <summary>
        /// Where the viewport centres for each day when the stage table has not loaded. A focus
        /// includes the node's card as well as its marker, which is why day two looks to the right
        /// and day three back to the left.
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

        /// <summary>Resource paths already looked for and not found, so each miss costs one lookup.</summary>
        static readonly System.Collections.Generic.HashSet<string> MissingImages =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

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

        /// <summary>
        /// How many stops the journey map contains: one per stage the season declares.
        ///
        /// Read from the stage table rather than from an array here, so the road and the calendar
        /// cannot disagree about how long the season is. A stage table that failed to load falls
        /// back to the three painted clearings — GameData has already logged that failure, and a
        /// map with three stops on it is a better thing to hand a player than an empty sheet.
        /// </summary>
        public static int JourneyCount
        {
            get
            {
                StageDef[] stages = GameData.Stages;
                return stages != null && stages.Length > 0 ? stages.Length : FallbackJourneyCount;
            }
        }

        /// <summary>
        /// Where one stage's marker sits: spaced evenly along the painted road by arc length, so
        /// nine stops read as one journey with equal strides rather than as nine points that happen
        /// to be near a track.
        ///
        /// Deliberately NOT the stage's declared map_anchor. Those were authored against the
        /// straight line between the three clearings, and measured against the shipped picture five
        /// of the nine sit off the painted road while the last two land close enough together to
        /// overlap each other's markers. The stage table still declares them, and
        /// <see cref="WarnOnDeclaredAnchorDrift"/> reports the disagreement once per run rather
        /// than letting the field quietly become data nothing reads.
        /// </summary>
        public static Vector2 JourneyAnchor(int index)
        {
            WarnOnDeclaredAnchorDrift();
            return RoadPointForStop(index, JourneyCount);
        }

        /// <summary>
        /// Where the clipped viewport should open for one stage.
        ///
        /// This one DOES prefer the stage table's declared value, because a focus is a camera
        /// target rather than a position on the road: it frames the stop together with the label
        /// hanging off it, and a stage is entitled to say "open looking a little ahead of me".
        /// A stage that declares no usable focus is framed on its own marker.
        /// </summary>
        public static Vector2 JourneyFocusAnchor(int index)
        {
            StageDef[] stages = GameData.Stages;
            if (stages == null || stages.Length == 0)
            {
                return JourneyFocusAnchors[Mathf.Clamp(index, 0, JourneyFocusAnchors.Length - 1)];
            }

            StageDef stage = stages[Mathf.Clamp(index, 0, stages.Length - 1)];
            if (stage != null && stage.map_focus != null && stage.map_focus.Length == 2)
            {
                return new Vector2(Mathf.Clamp01(stage.map_focus[0]), Mathf.Clamp01(stage.map_focus[1]));
            }

            // A malformed entry is already a logged error from GameData.VerifyStages. Repeating it
            // here would only add noise to a run that is already failing for the right reason.
            return JourneyAnchor(index);
        }

        /// <summary>
        /// The point at a fraction of the way along the road, measured by arc length.
        ///
        /// The arc is measured in <see cref="ContentSize"/> units rather than in the normalised
        /// 0..1 space, because the sheet is half again as wide as it is tall: even spacing in
        /// normalised coordinates would crowd the stops on the steep stretches and stretch them on
        /// the flat ones, which is the opposite of what the eye reads as an even journey.
        /// </summary>
        static Vector2 RoadPoint(float fraction)
        {
            EnsureRoadMetrics();

            float target = Mathf.Clamp01(fraction) * _roadLength;
            for (int i = 1; i < _roadArc.Length; i++)
            {
                if (target > _roadArc[i] && i < _roadArc.Length - 1)
                {
                    continue;
                }

                float span = _roadArc[i] - _roadArc[i - 1];
                float within = span > 0f ? (target - _roadArc[i - 1]) / span : 0f;
                return Vector2.Lerp(RoadPath[i - 1], RoadPath[i], Mathf.Clamp01(within));
            }

            return RoadPath[RoadPath.Length - 1];
        }

        /// <summary>
        /// One stop of <paramref name="count"/>, at its share of the road. A one-stage season sits
        /// at the start rather than dividing by zero.
        /// </summary>
        static Vector2 RoadPointForStop(int index, int count)
        {
            if (count <= 1)
            {
                return RoadPath[0];
            }

            return RoadPoint(Mathf.Clamp(index, 0, count - 1) / (float)(count - 1));
        }

        static void EnsureRoadMetrics()
        {
            if (_roadArc != null)
            {
                return;
            }

            _roadArc = new float[RoadPath.Length];
            for (int i = 1; i < RoadPath.Length; i++)
            {
                Vector2 step = RoadPath[i] - RoadPath[i - 1];
                _roadArc[i] = _roadArc[i - 1] +
                    new Vector2(step.x * ContentSize.x, step.y * ContentSize.y).magnitude;
            }

            _roadLength = _roadArc[_roadArc.Length - 1];
        }

        static float[] _roadArc;
        static float _roadLength;

        /// <summary>
        /// Reports, once per run, every stage whose declared map_anchor is not where the road puts
        /// its stop.
        ///
        /// A warning and not an error, on purpose. The stage table is authored data this file does
        /// not own, the numbers in it today were written before the road was measured, and the
        /// built-player run turns a logged error into a run failure — so erroring here would fail
        /// the gate on a file the reader of the message has to go and fix. It is also not silence:
        /// a field that nothing reads and nothing mentions is how a data file drifts into fiction.
        /// </summary>
        static void WarnOnDeclaredAnchorDrift()
        {
            if (_checkedDeclaredAnchors)
            {
                return;
            }

            // Latched only once there is something to compare. An anchor asked for before the
            // stage table loads would otherwise burn the single check on an empty table and the
            // real one would never run.
            StageDef[] stages = GameData.Stages;
            if (stages == null || stages.Length == 0)
            {
                return;
            }

            _checkedDeclaredAnchors = true;

            int drifted = 0;
            float worstDistance = 0f;
            string worstStage = null;
            for (int i = 0; i < stages.Length; i++)
            {
                StageDef stage = stages[i];
                if (stage == null || stage.map_anchor == null || stage.map_anchor.Length != 2)
                {
                    continue;
                }

                Vector2 road = RoadPointForStop(i, stages.Length);
                var offset = new Vector2(
                    (stage.map_anchor[0] - road.x) * ContentSize.x,
                    (stage.map_anchor[1] - road.y) * ContentSize.y);
                float distance = offset.magnitude;
                if (distance <= DeclaredAnchorTolerance)
                {
                    continue;
                }

                drifted++;
                if (distance > worstDistance)
                {
                    worstDistance = distance;
                    worstStage = stage.id;
                }
            }

            if (drifted == 0)
            {
                return;
            }

            Debug.LogWarning("[MapArt] " + drifted + " of " + stages.Length +
                " stages declare a map_anchor further than " + DeclaredAnchorTolerance +
                " units from where the painted road puts their stop (worst: \"" + worstStage +
                "\" at " + worstDistance.ToString("0") + "). The road is what the map draws; " +
                "re-value map_anchor in stages.json against it to silence this. Logged once per run.");
        }

        static bool _checkedDeclaredAnchors;

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
        /// The featured item image beside each stage. These ids are character_catalog.json ids,
        /// never player-facing copy; the display name comes from the loaded locale catalogue.
        ///
        /// The three-case switch this replaces returned null for anything it had not been told
        /// about, which was survivable while there were three stages and three drawn items and is
        /// not survivable at nine: six of the nine featured items have no image yet, and six place
        /// cards with an empty square where the reward goes reads as a broken panel rather than as
        /// art still in flight. A drawn parcel says "a thing you have not seen yet" honestly, and
        /// the moment Art/Map/item_&lt;name&gt;.png lands it is picked up with no code change.
        /// </summary>
        public static Sprite RewardSprite(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return GenericRewardSprite();
            }

            string suffix = RewardResourceSuffix(itemId);
            Sprite drawn = TryLoadImageSprite(ItemResourcePrefix + suffix, "map_reward_" + suffix);
            return drawn != null ? drawn : GenericRewardSprite();
        }

        /// <summary>
        /// The file name half of a catalogue id. Catalogue ids carry the slot they fill as a prefix
        /// — acc_, hair_, outfit_ — and the map's item art is filed under the garment's own name,
        /// which is the convention the three shipped images already follow.
        /// </summary>
        static string RewardResourceSuffix(string itemId)
        {
            for (int i = 0; i < SlotPrefixes.Length; i++)
            {
                string prefix = SlotPrefixes[i];
                if (itemId.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    return itemId.Substring(prefix.Length);
                }
            }

            return itemId;
        }

        /// <summary>
        /// The stand-in shown where a featured item has no drawn image: a tied parcel, in the world
        /// palette, at the same size as the drawn items so a place card does not change shape when
        /// the real art arrives.
        /// </summary>
        static Sprite GenericRewardSprite()
        {
            if (_genericReward != null)
            {
                return _genericReward;
            }

            var canvas = new PixelCanvas(RewardIconPixels, RewardIconPixels);
            DrawParcel(canvas);

            Texture2D texture = canvas.ToTexture("map_reward_unknown");
            _genericReward = Sprite.Create(
                texture,
                new Rect(0f, 0f, RewardIconPixels, RewardIconPixels),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect);

            // The name matters as much as the pixels: the built-player run counts and palette-checks
            // map sprites by this prefix, so a stand-in that skipped it would quietly shrink the
            // count the run asserts on.
            _genericReward.name = "map_reward_unknown";
            return _genericReward;
        }

        /// <summary>
        /// A bundle tied with a cord. Drawn with Set-family calls in palette colours only: the
        /// built-player run rejects any map sprite carrying a pixel that is neither fully
        /// transparent nor an exact palette entry, and every blending or antialiased helper on
        /// <see cref="PixelCanvas"/> produces the in-between values that check exists to catch.
        /// </summary>
        static void DrawParcel(PixelCanvas canvas)
        {
            // Named for the parcel rather than for the sheet: this class already has a Width and a
            // Height, and they are the procedural chart's, not this icon's.
            const int BoxLeft = 22;
            const int BoxBottom = 30;
            const int BoxWidth = RewardIconPixels - BoxLeft * 2;
            const int BoxHeight = 62;

            canvas.FillRect(BoxLeft, BoxBottom, BoxWidth, BoxHeight, ArtPalette.StoneLight);
            canvas.FillRect(BoxLeft, BoxBottom, BoxWidth, 14, ArtPalette.StoneMid);
            canvas.Outline(BoxLeft, BoxBottom, BoxWidth, BoxHeight, ArtPalette.StoneDeep);

            // The cord: one band across, one down, and the knot where they cross.
            int bandY = BoxBottom + BoxHeight / 2 - 4;
            canvas.FillRect(BoxLeft, bandY, BoxWidth, 8, ArtPalette.ClayDark);
            int bandX = BoxLeft + BoxWidth / 2 - 4;
            canvas.FillRect(bandX, BoxBottom, 8, BoxHeight, ArtPalette.ClayDark);
            canvas.FillRect(bandX - 6, bandY - 5, 20, 18, ArtPalette.ClayMid);
            canvas.Outline(bandX - 6, bandY - 5, 20, 18, ArtPalette.ClayDeep);

            // The two loose ends above the knot, which is what makes it read as tied rather than as
            // a cross painted on a box. Two pixels each: a single diagonal Bresenham run comes out
            // dotted, and this is shown at about a third of its drawn size.
            for (int offset = 0; offset < 2; offset++)
            {
                canvas.Line(bandX + 2, bandY + 13 + offset, bandX - 12, bandY + 26 + offset, ArtPalette.ClayDark);
                canvas.Line(bandX + 6, bandY + 13 + offset, bandX + 20, bandY + 26 + offset, ArtPalette.ClayDark);
            }
        }

        static Sprite _genericReward;

        static Sprite LoadImageSprite(string resourcePath, string spriteName)
        {
            Sprite sprite = TryLoadImageSprite(resourcePath, spriteName);
            if (sprite == null)
            {
                Debug.LogError("[MapArt] Missing generated image at Resources/" + resourcePath + ".png.");
            }

            return sprite;
        }

        /// <summary>
        /// The same load, without the complaint. Used where a missing file is an expected answer
        /// rather than a broken build: a featured item whose art has not been drawn yet has a
        /// stand-in waiting for it, and six log errors a frame would bury the ones that matter.
        /// </summary>
        static Sprite TryLoadImageSprite(string resourcePath, string spriteName)
        {
            Sprite cached;
            if (ImageSprites.TryGetValue(resourcePath, out cached) && cached != null)
            {
                return cached;
            }

            if (MissingImages.Contains(resourcePath))
            {
                return null;
            }

            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                // Remembered, because the map re-asks for the featured item on every selection and
                // a Resources lookup that will never succeed should be paid for once.
                MissingImages.Add(resourcePath);
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
