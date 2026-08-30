using System.Collections;
using System.Collections.Generic;
using SheepGate.Art;
using SheepGate.Core;
using SheepGate.Player;
using SheepGate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.World
{
    /// <summary>
    /// The region: this city, the ones around it, the water they sit in, and the roads between.
    ///
    /// Built in the world rather than drawn on a panel, so the opening performs the whole move as
    /// one continuous push-in from the region to the city — which is what makes "this is one place
    /// among many, and this one is mine" land without a caption explaining it. The same view is
    /// what the map button reopens, so the map never contradicts what walking around taught the
    /// player about where anything is.
    ///
    /// It reads as a map rather than as a zoomed-out game because of four things a map has and a
    /// wide shot does not: land with a coast, roads that go somewhere, a plate on every place, and
    /// a key saying what the plates mean.
    ///
    /// The other cities stay unnamed. Naming seasons we have not built would be a promise; the
    /// honest thing to put on their plates is that they are shut.
    /// </summary>
    public sealed class WorldMapOverlay
    {
        sealed class Place
        {
            public Vector2 Offset;      // world units from the centre of the village
            public float Size;
        }

        // Portrait framing gives far more vertical room than horizontal, so the neighbours sit
        // mostly above and below rather than beside.
        static readonly Place[] Places =
        {
            new Place { Offset = new Vector2(-14f,  34f), Size = 3.0f },
            new Place { Offset = new Vector2( 16f,  27f), Size = 2.6f },
            new Place { Offset = new Vector2(-17f, -26f), Size = 2.8f },
            new Place { Offset = new Vector2( 15f, -33f), Size = 2.4f }
        };

        /// <summary>Camera size that frames the village and every neighbour around it.</summary>
        public const float FramingSize = 44f;

        /// <summary>Above the HUD (50) and below the dialogue canvas (100) and every modal (300).</summary>
        public const int CanvasSortingOrder = 80;

        // ---- terrain -------------------------------------------------------
        // The island holds the village and all four neighbours; the field around it is water out
        // to the edge of what the framing can show, so the map never runs out of surface.
        // At this framing the camera shows about 40 world units across and 88 down, so an island
        // shaped like an island would run off both sides and never show a coast. The land is wider
        // than the view on purpose and shorter than it: the sea is what the region ends in, north
        // and south, and the two far cities sit on that coast.
        const float FieldHalfWidth = 36f;
        const float FieldHalfHeight = 52f;
        const float IslandRadiusX = 34f;
        const float IslandRadiusY = 38f;
        const float CoastWobble = 2.4f;

        // Two bays, one per side, at different heights. Named heights rather than noise: a coast
        // the player can navigate by needs features that stay where they were last time.
        const float BayDepth = 6.5f;
        const float BayWidth = 7f;
        const float BayNorthY = 13f;
        const float BaySouthY = -19f;

        /// <summary>Where the stony upland gives way to the low ground, in world units.</summary>
        const float DistrictBoundaryY = 4f;
        const float DistrictBlend = 9f;
        const int ScatterCount = 90;

        /// <summary>Above the terrain bands and below everything the player is meant to read.</summary>
        const int ScatterOrder = -1020;

        // Sorting orders for the terrain. TilemapBuilder draws the village at -1000, so everything
        // the region is made of has to sit below that; the cells outside the map take the land
        // sprite instead, which is what keeps the two seamless.
        const int TerrainOrderWater = -1060;
        const int TerrainOrderShallow = -1050;
        const int TerrainOrderShore = -1040;
        const int TerrainOrderLand = -1030;

        /// <summary>Width of the pale band where the land meets the water.</summary>
        const float ShoreWidth = 1.8f;

        /// <summary>Width of the lighter water just off the shore.</summary>
        const float ShallowWidth = 2.6f;

        // ---- roads ---------------------------------------------------------
        // Roads are drawn as a continuous trail rather than a dashed line. A dash reads as a
        // legend symbol; the reference this is drawn against uses trodden paths, and a path is
        // what a player is meant to imagine themselves walking.
        const int RoadSegments = 34;
        const float RoadEdgeMargin = 0.1f;
        const float RoadEndInset = 1.2f;
        const float RoadFallbackStart = 12f;

        /// <summary>How much of the map's bounding box the built city actually fills.</summary>
        const float VisibleCityFactor = 0.78f;
        const float RoadWidth = 1.15f;

        /// <summary>How far a road bows sideways between the village and a neighbour.</summary>
        const float RoadBow = 5.5f;

        // ---- chrome --------------------------------------------------------
        //
        // Everything below this line is UI and takes its lengths from DesignTokens. Everything
        // above it is world art, drawn in world units and tinted by a cartographer's palette
        // rather than by the interface's: a map reads by value contrast between land, shore and
        // water, and those three are not roles the design system has.
        static readonly float FrameInset = DesignTokens.Space.S8;
        static readonly float FrameThickness = DesignTokens.Px(1f);

        /// <summary>How far the corner cards sit inside the frame.</summary>
        static readonly float ChromeInset = DesignTokens.Space.S8;

        /// <summary>The compass needle, one step heavier than the frame so it reads as a mark.</summary>
        static readonly float NeedleThickness = DesignTokens.Px(1.5f);

        /// <summary>
        /// Width of the text column inside the title card, and the only thing stopping it from
        /// growing into the compass.
        ///
        /// A card that sizes itself to its longest line is a card whose width is a property of the
        /// translation, and at the design system's Title size "The cities of the exile" is wide
        /// enough to reach the corner the compass sits in. Capping the column makes the heading
        /// wrap instead, so the card is the same shape in every language and the collision cannot
        /// come back with the next one.
        /// </summary>
        static readonly float CardTextWidth = DesignTokens.Px(170f);

        const float PlateOffsetY = -2.4f;   // world units below a marker

        // Three tones of the same ground, not three colours: the palette is stone, clay and teal,
        // and a map reads by value contrast long before it reads by hue.
        static readonly Color LandTint = new Color(0.70f, 0.67f, 0.57f, 1f);
        static readonly Color ShoreTint = new Color(0.93f, 0.89f, 0.76f, 1f);
        static readonly Color WaterTint = new Color(0.52f, 0.70f, 0.74f, 1f);
        static readonly Color ShallowTint = new Color(0.78f, 0.92f, 0.93f, 1f);
        static readonly Color DeepWater = new Color(0.13f, 0.21f, 0.24f, 1f);
        // Multipliers, not colours: the district shading tints whatever band it is applied to, so
        // shore and land shift together and the coastline never splits into two palettes.
        static readonly Color UplandShade = new Color(1.06f, 1.05f, 1.02f, 1f);
        static readonly Color LowlandShade = new Color(0.92f, 0.89f, 0.83f, 1f);

        static readonly Color RockTint = new Color(0.56f, 0.53f, 0.46f, 1f);
        static readonly Color BrokenTint = new Color(0.78f, 0.74f, 0.65f, 1f);
        static readonly Color RiseTint = new Color(0.79f, 0.75f, 0.65f, 1f);
        static readonly Color RiseShadowTint = new Color(0.56f, 0.53f, 0.45f, 1f);
        static readonly Color RoadColour = new Color(0.86f, 0.79f, 0.62f, 1f);
        static readonly Color RoadEdgeColour = new Color(0.56f, 0.50f, 0.39f, 1f);
        static readonly Color TownGroundColour = new Color(0.88f, 0.83f, 0.70f, 1f);
        static readonly Color CityRingColour = new Color(0.66f, 0.60f, 0.49f, 0.30f);

        // The chrome, on design system tokens. Opacity is the one modification a token allows, and
        // it is what the plates use to let the region show faintly through them.
        static readonly Color FrameColour = UIKit.WithAlpha(DesignTokens.Neutral.N500, 0.55f);
        static readonly Color PlateFill = UIKit.WithAlpha(DesignTokens.Surface.Scroll, 0.96f);
        static readonly Color PlateInk = DesignTokens.Ink.OnScroll;
        static readonly Color PlateMutedInk = DesignTokens.Ink.OnScrollMuted;

        /// <summary>This city, in the action colour. The one plate that is not somewhere else.</summary>
        static readonly Color HomePlateFill = UIKit.WithAlpha(DesignTokens.Brand.Primary, 0.96f);
        static readonly Color HomePlateInk = DesignTokens.Ink.OnPrimary;

        /// <summary>The key's swatch for a shut city: the darkest neutral, not a fifth grey.</summary>
        static readonly Color ClosedSwatch = DesignTokens.Neutral.N700;

        readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();
        readonly List<Renderer> _hiddenActors = new List<Renderer>();
        readonly List<Graphic> _chrome = new List<Graphic>();
        readonly List<float> _chromeAlpha = new List<float>();

        GameObject _root;
        Canvas _canvas;
        TilemapBuilder _map;
        Camera _camera;
        Color _previousBackground;
        bool _fieldApplied;

        /// <summary>
        /// Builds the region. <paramref name="withLegend"/> is false for the opening, where the map
        /// is a shot rather than a tool and a key would sit under the narrator's own words.
        /// </summary>
        public static WorldMapOverlay Show(TilemapBuilder map, Transform parent, bool withLegend = false)
        {
            var overlay = new WorldMapOverlay();
            overlay.Build(map, parent, withLegend);
            return overlay;
        }

        void Build(TilemapBuilder map, Transform parent, bool withLegend)
        {
            _map = map;
            Vector3 centre = map != null ? map.CenterWorld() : Vector3.zero;

            _root = new GameObject("WorldMap");
            if (parent != null)
            {
                _root.transform.SetParent(parent, false);
            }

            ApplyField();
            HideActors();
            BuildTerrain(centre);

            Vector3 extents = map != null ? map.WorldBounds.extents : Vector3.zero;
            for (int i = 0; i < Places.Length; i++)
            {
                BuildRoad(Places[i], centre, extents, i);
            }

            for (int i = 0; i < Places.Length; i++)
            {
                BuildPlace(Places[i], centre, i);
            }

            // Below the dialogue canvas (100), not above it. At 340 the plates and the title card
            // drew over the narrator's own bubble during the opening, which is the map covering the
            // words it is being shown to illustrate.
            _canvas = UIKit.CreateCanvas("WorldMapCanvas", CanvasSortingOrder);
            RectTransform root = UIKit.SafeArea(_canvas);

            BuildFrame(root);
            BuildTitleCard(root);
            BuildCompass(root);
            BuildPlates(root, centre);

            if (withLegend)
            {
                BuildLegend(root);
            }
        }

        // ------------------------------------------------------------------ field and terrain

        /// <summary>
        /// Paints what lies outside the built map. The cells the tilemap draws there become the
        /// island's own ground, and the camera's background becomes deep water, so the village sits
        /// on the region instead of inside a rectangle cut out of it.
        /// </summary>
        void ApplyField()
        {
            _camera = Camera.main;
            if (_camera != null)
            {
                _previousBackground = _camera.backgroundColor;
                _camera.backgroundColor = DeepWater;
                _fieldApplied = true;
            }

            if (_map != null)
            {
                _map.SetVoidSprite(ArtLibrary.GetTinted(ArtKeys.TileGround, LandTint));
            }
        }

        /// <summary>
        /// Takes the people off the map.
        ///
        /// At this distance a resident is a four-pixel figure standing in open country, and a
        /// dozen of them read as litter rather than as a population. A map shows where a city is,
        /// not who is currently standing in it — and the city itself is still drawn, so nothing
        /// about where anything is has changed.
        /// </summary>
        void HideActors()
        {
            HideRenderersOn<NpcActor>();
            HideRenderersOn<StandingNpc>();
            HideRenderersOn<CutsceneActor>();
            HideRenderersOn<PlayerController>();
            HideRenderersOn<CharacterAppearance>();
        }

        void HideRenderersOn<T>() where T : MonoBehaviour
        {
            T[] found = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] == null)
                {
                    continue;
                }

                Renderer[] renderers = found[i].GetComponentsInChildren<Renderer>(true);
                for (int j = 0; j < renderers.Length; j++)
                {
                    Renderer renderer = renderers[j];
                    if (renderer == null || renderer.forceRenderingOff)
                    {
                        continue;
                    }

                    // forceRenderingOff, not enabled. CharacterAppearance sets enabled itself
                    // whenever it refreshes a layer, so a renderer switched off here comes back on
                    // the next time the player's sprite is rebuilt - which is why the player was
                    // still standing in the middle of the map after everyone else had gone.
                    renderer.forceRenderingOff = true;
                    _hiddenActors.Add(renderer);
                }
            }
        }

        void RestoreActors()
        {
            for (int i = 0; i < _hiddenActors.Count; i++)
            {
                Renderer renderer = _hiddenActors[i];
                if (renderer != null)
                {
                    renderer.forceRenderingOff = false;
                }
            }

            _hiddenActors.Clear();
        }

        void RestoreField()
        {
            if (_fieldApplied && _camera != null)
            {
                _camera.backgroundColor = _previousBackground;
            }

            _fieldApplied = false;

            if (_map != null)
            {
                _map.RestoreVoidColor();
            }
        }

        /// <summary>
        /// Water, then the island on top of it.
        ///
        /// The island is emitted one world-unit row at a time, each row a single tiled quad from
        /// one coast to the other. A grid of tiles at the resolution the village is drawn at would
        /// be several thousand renderers for a picture nobody walks on; a row is one, and the coast
        /// still steps at the same scale as everything else on screen.
        /// </summary>
        void BuildTerrain(Vector3 centre)
        {
            var host = new GameObject("Terrain");
            host.transform.SetParent(_root.transform, false);

            // Every terrain order is below TilemapBuilder's own -1000. The tilemap is drawn far
            // behind everything else in the scene, and terrain at the orders the rest of this
            // class uses covered the village completely: the map showed residents standing on
            // open ground with no city under them.
            SpriteRenderer water = NewRenderer(host.transform, "Water", TerrainOrderWater);
            water.sprite = ArtLibrary.Get(ArtKeys.TileWater);
            water.color = WaterTint;
            water.drawMode = SpriteDrawMode.Tiled;
            water.size = new Vector2(FieldHalfWidth * 2f, FieldHalfHeight * 2f);
            water.transform.position = new Vector3(centre.x, centre.y, 0f);

            // Three bands, each an ellipse a little wider than the last, drawn outermost first:
            // shallow water, then the shore, then the land. Emitting a band a world-unit row at a
            // time keeps the coast stepping at the same scale as the village, and costs one
            // renderer per row instead of one per cell.
            Sprite waterSprite = ArtLibrary.Get(ArtKeys.TileWater);
            Sprite groundSprite = ArtLibrary.Get(ArtKeys.TileGround);

            BuildBand(host.transform, centre, "Shallow", waterSprite, ShallowTint, ShoreWidth + ShallowWidth, TerrainOrderShallow);
            BuildBand(host.transform, centre, "Shore", groundSprite, ShoreTint, ShoreWidth, TerrainOrderShore);
            BuildBand(host.transform, centre, "Land", groundSprite, LandTint, 0f, TerrainOrderLand);

            BuildScatter(centre);
        }

        /// <summary>
        /// One terrain band: the island outline grown by <paramref name="expand"/>, filled row by
        /// row with a tiled sprite.
        /// </summary>
        void BuildBand(Transform parent, Vector3 centre, string name, Sprite sprite, Color tint, float expand, int order)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);

            for (float y = -FieldHalfHeight; y <= FieldHalfHeight; y += 1f)
            {
                float left = CoastAt(y, expand, -1);
                float right = CoastAt(y, expand, 1);
                float width = left + right;
                if (width <= 1f)
                {
                    continue;
                }

                SpriteRenderer row = NewRenderer(host.transform, "Row", order);
                row.sprite = sprite;
                row.color = tint * DistrictShade(y);
                row.drawMode = SpriteDrawMode.Tiled;
                row.size = new Vector2(width, 1f);
                row.transform.position = new Vector3(centre.x + (right - left) * 0.5f, centre.y + y, 0f);
            }
        }

        /// <summary>
        /// How far the coast lies from the centre line at one height, on one side.
        ///
        /// The two sides are computed separately and bow differently, and each carries a bay cut
        /// into it at its own height. A symmetrical outline reads as a drawn oval no matter how
        /// much is placed on top of it; an asymmetrical one reads as a coast, and the bays give
        /// the eye two fixed points to navigate by.
        ///
        /// <paramref name="expand"/> grows the same outline for the shore and shallow bands, so
        /// every band shares one coastline instead of three that only nearly agree. Deterministic,
        /// because two runs of the same map must produce the same coast.
        /// </summary>
        static float CoastAt(float y, float expand, int side)
        {
            float radiusY = IslandRadiusY + expand;
            float t = Mathf.Abs(y) / radiusY;
            if (t >= 1f)
            {
                return 0f;
            }

            float basis = (IslandRadiusX + expand) * Mathf.Sqrt(1f - t * t);
            float phase = side > 0 ? 0f : 2.2f;

            float wobble = Mathf.Sin(y * 0.21f + phase) * CoastWobble
                         + Mathf.Sin(y * 0.073f + 1.7f + phase) * (CoastWobble * 0.55f);

            float bay = side > 0 ? Bay(y, BayNorthY) : Bay(y, BaySouthY);

            return Mathf.Max(0f, basis + wobble - bay);
        }

        /// <summary>
        /// A district tint: cooler and paler in the north, warmer and darker in the south, blended
        /// across a band rather than switched at a line.
        ///
        /// This is the piece the map was missing most. Lynch's account of what makes a place
        /// legible lists districts alongside paths, edges, nodes and landmarks — and until now the
        /// whole interior was one undifferentiated tone, so every part of the map looked like
        /// every other part and distance carried no information.
        /// </summary>
        static Color DistrictShade(float y)
        {
            float t = Mathf.Clamp01((y - DistrictBoundaryY) / DistrictBlend + 0.5f);
            return Color.Lerp(LowlandShade, UplandShade, t);
        }

        /// <summary>A rounded bite out of the coast, deepest at <paramref name="at"/>.</summary>
        static float Bay(float y, float at)
        {
            float distance = (y - at) / BayWidth;
            return BayDepth * Mathf.Exp(-distance * distance);
        }

        /// <summary>
        /// What the country between the cities is made of: high ground, broken ground and rock.
        ///
        /// Three kinds rather than one, and ninety of them rather than a dozen, because the thing
        /// that made the first version read as a flat expanse was not the colour — it was that
        /// most of the map had nothing on it at all. A map earns its distances by filling them.
        ///
        /// Everything is placed on a fixed sequence, never at random: the map has to look the same
        /// every time it is opened, or it stops being a map of anywhere.
        /// </summary>
        void BuildScatter(Vector3 centre)
        {
            var host = new GameObject("Scatter");
            host.transform.SetParent(_root.transform, false);

            Sprite rock = ArtLibrary.Get(ArtKeys.PropRubble);
            Sprite broken = ArtLibrary.Get(ArtKeys.TileRubble);
            Sprite ground = ArtLibrary.Get(ArtKeys.TileGround);

            float villageClearance = _map != null
                ? Mathf.Max(_map.WorldBounds.extents.x, _map.WorldBounds.extents.y)
                : 16f;

            for (int i = 0; i < ScatterCount; i++)
            {
                // Golden-angle spiral: successive points land far apart and the set covers the
                // disc evenly, from a counter and no table.
                float angle = i * 2.39996f;
                float radius = Mathf.Sqrt((i + 0.5f) / ScatterCount);
                float x = Mathf.Cos(angle) * radius * (IslandRadiusX * 0.97f);
                float y = Mathf.Sin(angle) * radius * (IslandRadiusY * 0.97f);

                float edge = x >= 0f ? CoastAt(y, 0f, 1) : CoastAt(y, 0f, -1);
                if (Mathf.Abs(x) > edge - 2.2f)
                {
                    continue;
                }

                if (new Vector2(x, y).magnitude < villageClearance + 2.5f)
                {
                    continue;
                }

                var position = new Vector3(centre.x + x, centre.y + y, 0f);
                float wobble = Mathf.Sin(i * 12.9898f) * 0.5f + 0.5f;   // deterministic 0..1

                switch (i % 5)
                {
                    case 0:
                    case 3:
                        BuildRock(host.transform, rock, position, wobble);
                        break;
                    case 1:
                    case 4:
                        BuildHighGround(host.transform, ground, position, wobble);
                        break;
                    default:
                        BuildBrokenGround(host.transform, broken, position, wobble);
                        break;
                }
            }
        }

        void BuildRock(Transform parent, Sprite sprite, Vector3 position, float wobble)
        {
            SpriteRenderer renderer = NewRenderer(parent, "Rock", ScatterOrder);
            renderer.sprite = sprite;
            renderer.color = RockTint;
            renderer.transform.position = position;
            renderer.transform.localScale = Vector3.one * (1.1f + wobble * 0.9f);
        }

        /// <summary>
        /// A rise: a pale patch with its own shadow under it. Two quads is all the relief a flat
        /// map needs to stop reading as a sheet of one colour.
        /// </summary>
        void BuildHighGround(Transform parent, Sprite sprite, Vector3 position, float wobble)
        {
            float width = 3.4f + wobble * 3.6f;
            float height = 1.9f + wobble * 1.5f;

            SpriteRenderer shadow = NewRenderer(parent, "RiseShadow", ScatterOrder);
            shadow.sprite = sprite;
            shadow.color = RiseShadowTint;
            shadow.transform.position = position + new Vector3(0f, -0.45f, 0f);
            shadow.transform.localScale = new Vector3(width, height, 1f);

            SpriteRenderer rise = NewRenderer(parent, "Rise", ScatterOrder + 1);
            rise.sprite = sprite;
            rise.color = RiseTint;
            rise.transform.position = position;
            rise.transform.localScale = new Vector3(width, height, 1f);
        }

        void BuildBrokenGround(Transform parent, Sprite sprite, Vector3 position, float wobble)
        {
            SpriteRenderer renderer = NewRenderer(parent, "Broken", ScatterOrder);
            renderer.sprite = sprite;
            renderer.color = BrokenTint;
            renderer.transform.position = position;
            renderer.transform.localScale = new Vector3(2.2f + wobble * 2.4f, 1.6f + wobble * 1.4f, 1f);
        }

        // ------------------------------------------------------------------ roads and places

        /// <summary>
        /// A trail from the village out to one neighbour: a curve, drawn twice — once wide in the
        /// darker verge colour and once narrow in the pale trodden colour on top. The outline is
        /// what makes a path read as a path at a glance instead of as a smear of ground.
        ///
        /// The curve bows to one side rather than running straight, and alternates which side by
        /// index. Four straight spokes out of one point read as a diagram; four bowed trails read
        /// as roads that had a reason to go where they went.
        /// </summary>
        void BuildRoad(Place place, Vector3 centre, Vector3 villageExtents, int index)
        {
            float total = place.Offset.magnitude;
            if (total <= 0.01f)
            {
                return;
            }

            Vector2 heading = place.Offset / total;
            float from = RoadStart(heading, villageExtents);
            float to = total - RoadEndInset;
            if (to <= from)
            {
                return;
            }

            Vector2 startPoint = heading * from;
            Vector2 endPoint = heading * to;
            Vector2 perpendicular = new Vector2(-heading.y, heading.x);
            float side = (index % 2 == 0) ? 1f : -1f;
            Vector2 control = (startPoint + endPoint) * 0.5f + perpendicular * (RoadBow * side);

            var host = new GameObject("Road" + index);
            host.transform.SetParent(_root.transform, false);

            BuildTrail(host.transform, centre, startPoint, control, endPoint, RoadWidth + 0.5f, RoadEdgeColour, -14);
            BuildTrail(host.transform, centre, startPoint, control, endPoint, RoadWidth, RoadColour, -12);
        }

        /// <summary>
        /// Lays one pass of a trail: quads along a quadratic curve, each turned to the direction of
        /// travel and long enough to overlap its neighbour, so the line has no gaps on the bends.
        /// </summary>
        void BuildTrail(Transform parent, Vector3 centre, Vector2 a, Vector2 control, Vector2 b, float width, Color colour, int order)
        {
            Vector2 previous = a;

            for (int i = 1; i <= RoadSegments; i++)
            {
                float t = i / (float)RoadSegments;
                Vector2 point = QuadraticPoint(a, control, b, t);
                Vector2 delta = point - previous;
                float length = delta.magnitude;

                if (length > 0.0001f)
                {
                    Vector2 middle = (previous + point) * 0.5f;
                    float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

                    SpriteRenderer piece = NewRenderer(parent, "Segment" + i, order);
                    piece.sprite = ArtLibrary.Get(ArtKeys.TileGround);
                    piece.color = colour;
                    piece.transform.position = new Vector3(centre.x + middle.x, centre.y + middle.y, 0f);
                    piece.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                    piece.transform.localScale = new Vector3(length * 1.8f, width, 1f);
                }

                previous = point;
            }
        }

        static Vector2 QuadraticPoint(Vector2 a, Vector2 control, Vector2 b, float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * a + 2f * inverse * t * control + t * t * b;
        }

        /// <summary>
        /// Where a road begins: the point where the line to a neighbour leaves the village, plus a
        /// small gap. Measured from the tilemap's own bounds rather than a tuned constant, so a
        /// bigger map moves the roads out with it instead of drawing dashes across the houses.
        /// </summary>
        static float RoadStart(Vector2 heading, Vector3 villageExtents)
        {
            // The built ground is a rough disc inside the map's rectangle, so the rectangle's own
            // extents overshoot the edge the player can see by several units and left every road
            // starting in open country with a gap behind it.
            float halfWidth = villageExtents.x * VisibleCityFactor;
            float halfHeight = villageExtents.y * VisibleCityFactor;
            if (halfWidth <= 0.01f || halfHeight <= 0.01f)
            {
                return RoadFallbackStart;
            }

            float x = heading.x / halfWidth;
            float y = heading.y / halfHeight;
            float scale = Mathf.Sqrt(x * x + y * y);
            if (scale <= 0.0001f)
            {
                return RoadFallbackStart;
            }

            return 1f / scale + RoadEdgeMargin;
        }

        /// <summary>
        /// A neighbouring city: a cleared patch of ground with a few roofs on it, not one scaled
        /// house. A single sprite at this distance is a blob; three at different sizes read as a
        /// place where people live, which is the whole point of showing it.
        /// </summary>
        void BuildPlace(Place place, Vector3 centre, int index)
        {
            var host = new GameObject("Place" + index);
            host.transform.SetParent(_root.transform, false);
            host.transform.position = new Vector3(centre.x + place.Offset.x, centre.y + place.Offset.y, 0f);

            SpriteRenderer ground = NewRenderer(host.transform, "Ground", -8);
            ground.sprite = ArtLibrary.Get(ArtKeys.TileGround);
            ground.color = TownGroundColour;
            ground.transform.localScale = Vector3.one * (place.Size * 2.4f);

            // Offsets in units of the town's own size, so a bigger town spreads rather than
            // stacking its roofs on top of each other.
            BuildRoof(host.transform, place, new Vector2(-0.42f,  0.30f), 0.78f, 10);
            BuildRoof(host.transform, place, new Vector2( 0.46f,  0.16f), 0.66f, 11);
            BuildRoof(host.transform, place, new Vector2(-0.04f, -0.34f), 0.92f, 12);
        }

        void BuildRoof(Transform parent, Place place, Vector2 offset, float scale, int order)
        {
            SpriteRenderer roof = NewRenderer(parent, "Roof", order);
            roof.sprite = ArtLibrary.Get(ArtKeys.TileHouse);
            roof.color = new Color(0.58f, 0.49f, 0.42f, 1f);
            roof.transform.localPosition = new Vector3(offset.x * place.Size, offset.y * place.Size, 0f);
            roof.transform.localScale = Vector3.one * (place.Size * scale);
        }

        SpriteRenderer NewRenderer(Transform parent, string name, int order)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            SpriteRenderer renderer = host.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            _renderers.Add(renderer);
            return renderer;
        }

        // ------------------------------------------------------------------ chrome

        /// <summary>Four hairlines just inside the safe area: the edge of the sheet.</summary>
        void BuildFrame(RectTransform root)
        {
            AddBar(root, "FrameTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, FrameThickness), new Vector2(0f, -FrameInset));
            AddBar(root, "FrameBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, FrameThickness), new Vector2(0f, FrameInset));
            AddBar(root, "FrameLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(FrameThickness, 0f), new Vector2(FrameInset, 0f));
            AddBar(root, "FrameRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(FrameThickness, 0f), new Vector2(-FrameInset, 0f));
        }

        void AddBar(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 offset)
        {
            RectTransform rect = UIKit.CreateRect(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;

            var image = rect.gameObject.AddComponent<Image>();
            image.color = FrameColour;
            image.raycastTarget = false;
            Track(image);
        }

        /// <summary>
        /// The title, on a plate in the corner rather than floating over the middle of the region.
        /// Centred text across the top competes with whatever the map put there; a corner card is
        /// where a map keeps its name.
        /// </summary>
        void BuildTitleCard(RectTransform root)
        {
            // Top centre, not the corner. A corner card is where a paper map keeps its name, but
            // this map's corners are where two of the cities project, and a card over a city is
            // the map hiding the thing it exists to show.
            RectTransform card = Plate(root, "TitleCard", PlateFill,
                Inset(DesignTokens.Space.S16, DesignTokens.Space.S8), DesignTokens.Space.S4);
            Track(card.GetComponent<Image>());
            card.anchorMin = new Vector2(0.5f, 1f);
            card.anchorMax = new Vector2(0.5f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, -(FrameInset + ChromeInset));

            Text heading = UIKit.CreateText(card, "Heading", Loc.T("world.map.heading"),
                DesignTokens.Type.Title, PlateInk, TextAnchor.MiddleLeft, DesignTokens.TypeRole.Title);
            heading.raycastTarget = false;
            UIKit.Layout(heading).preferredWidth = CardTextWidth;
            Track(heading);

            Text note = UIKit.CreateText(card, "Note", Loc.T("world.map.note"),
                DesignTokens.Type.Minimum, PlateMutedInk, TextAnchor.MiddleLeft);
            note.raycastTarget = false;
            UIKit.Layout(note).preferredWidth = CardTextWidth;
            Track(note);
        }

        /// <summary>
        /// North, and a stroke pointing at it. The places are laid out around the village rather
        /// than surveyed, so this claims an orientation and nothing more precise than that.
        ///
        /// On a plate, like every other word on this map. The letter used to be parchment ink laid
        /// straight over the region, and the design system's floor for text over the scene is a
        /// veil at 72% — the terrain underneath is a lit tilemap, not key art, so whether an "N"
        /// was readable depended on which band of shore or water happened to be behind it.
        /// </summary>
        void BuildCompass(RectTransform root)
        {
            RectTransform card = Plate(root, "Compass", PlateFill,
                Inset(DesignTokens.Space.S12, DesignTokens.Space.S8), DesignTokens.Space.S4,
                TextAnchor.UpperCenter);
            Track(card.GetComponent<Image>());
            card.anchorMin = new Vector2(1f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(1f, 1f);
            card.anchoredPosition = new Vector2(-(FrameInset + ChromeInset), -(FrameInset + ChromeInset));

            Text north = UIKit.CreateText(card, "CompassNorth", Loc.T("world.map.north"),
                DesignTokens.Type.Title, PlateInk, TextAnchor.MiddleCenter, DesignTokens.TypeRole.Title);
            north.raycastTarget = false;
            Track(north);

            RectTransform needle = UIKit.CreateRect("CompassNeedle", card);
            var needleLayout = needle.gameObject.AddComponent<LayoutElement>();
            needleLayout.preferredWidth = NeedleThickness;
            needleLayout.preferredHeight = DesignTokens.Px(14f);

            var image = needle.gameObject.AddComponent<Image>();
            image.color = PlateMutedInk;
            image.raycastTarget = false;
            Track(image);
        }

        /// <summary>A plate on every place, including this one.</summary>
        void BuildPlates(RectTransform root, Vector3 centre)
        {
            float homeOffset = _map != null
                ? -(_map.WorldBounds.extents.y * VisibleCityFactor + 2.2f)
                : PlateOffsetY;
            TrackLabel(MapLabel.Create(root, Loc.T("world.map.here"), centre, homeOffset, HomePlateFill, HomePlateInk));

            for (int i = 0; i < Places.Length; i++)
            {
                Vector3 point = new Vector3(centre.x + Places[i].Offset.x, centre.y + Places[i].Offset.y, 0f);
                float offset = -(Places[i].Size * 1.2f + 1.2f);
                TrackLabel(MapLabel.Create(root, Loc.T("world.map.closed"), point, offset, PlateFill, PlateInk));
            }
        }

        /// <summary>
        /// The key. Three rows, because there are three kinds of thing on the map and a player who
        /// has to guess which shape means what is reading a picture, not a map.
        /// </summary>
        void BuildLegend(RectTransform root)
        {
            RectTransform legend = Plate(root, "Legend", PlateFill,
                Inset(DesignTokens.Space.S12, DesignTokens.Space.S8), DesignTokens.Space.S4);
            Track(legend.GetComponent<Image>());
            legend.anchorMin = new Vector2(0f, 0f);
            legend.anchorMax = new Vector2(0f, 0f);
            legend.pivot = new Vector2(0f, 0f);
            legend.anchoredPosition = new Vector2(FrameInset + ChromeInset, FrameInset + ChromeInset);

            AddLegendRow(legend, "LegendHome", HomePlateFill, Loc.T("world.map.legend.home"));
            AddLegendRow(legend, "LegendClosed", ClosedSwatch, Loc.T("world.map.legend.closed"));
            AddLegendRow(legend, "LegendRoad", RoadColour, Loc.T("world.map.legend.road"));
        }

        /// <summary>
        /// One key row: a swatch and the word for it. Both, always — the design system's rule that
        /// colour never carries a meaning on its own is the whole reason a legend exists.
        /// </summary>
        void AddLegendRow(RectTransform parent, string name, Color swatchColour, string text)
        {
            RectTransform row = UIKit.CreateRect(name, parent);

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = DesignTokens.Space.S8;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            RectTransform swatch = UIKit.CreateRect("Swatch", row);
            var swatchImage = swatch.gameObject.AddComponent<Image>();
            swatchImage.color = swatchColour;
            swatchImage.raycastTarget = false;
            var swatchLayout = swatch.gameObject.AddComponent<LayoutElement>();
            swatchLayout.preferredWidth = DesignTokens.Px(12f);
            swatchLayout.preferredHeight = DesignTokens.Px(8f);
            Track(swatchImage);

            Text label = UIKit.CreateText(row, "Text", text, DesignTokens.Type.Minimum, PlateInk,
                TextAnchor.MiddleLeft);
            label.raycastTarget = false;
            Track(label);
        }

        /// <summary>
        /// A parchment card that grows to whatever is put inside it. The caller says which way the
        /// contents stack, because a GameObject can only carry one layout group and adding the
        /// wrong one first leaves two fighting over the same children.
        /// </summary>
        static RectTransform Plate(RectTransform parent, string name, Color fill, RectOffset padding,
            float spacing, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            RectTransform rect = UIKit.CreateRect(name, parent);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = ArtLibrary.Get(ArtKeys.UiBubble);
            image.type = Image.Type.Sliced;
            image.color = fill;
            image.raycastTarget = false;

            var column = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = padding;
            column.spacing = spacing;
            column.childAlignment = alignment;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;

            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        /// <summary>
        /// A symmetric padding from two spacing tokens. <see cref="RectOffset"/> takes whole
        /// pixels and the tokens are the design document's points converted, so they round here
        /// rather than at every call site.
        /// </summary>
        static RectOffset Inset(float horizontal, float vertical)
        {
            int x = Mathf.RoundToInt(horizontal);
            int y = Mathf.RoundToInt(vertical);
            return new RectOffset(x, x, y, y);
        }

        // ------------------------------------------------------------------ fade and teardown

        /// <summary>Dissolves the region as the camera commits to this city.</summary>
        public IEnumerator FadeOut(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float alpha = 1f - Mathf.Clamp01(seconds <= 0f ? 1f : elapsed / seconds);

                for (int i = 0; i < _renderers.Count; i++)
                {
                    SpriteRenderer renderer = _renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    Color colour = renderer.color;
                    colour.a = alpha;
                    renderer.color = colour;
                }

                for (int i = 0; i < _chrome.Count; i++)
                {
                    SetGraphicAlpha(_chrome[i], _chromeAlpha[i] * alpha);
                }

                yield return null;
            }

            Dispose();
        }

        /// <summary>
        /// Remembers a chrome graphic and the alpha it was authored with, so the fade can scale it
        /// instead of multiplying the live value down to nothing over successive frames.
        /// </summary>
        void Track(Graphic graphic)
        {
            if (graphic == null)
            {
                return;
            }

            _chrome.Add(graphic);
            _chromeAlpha.Add(graphic.color.a);
        }

        void TrackLabel(MapLabel label)
        {
            if (label == null)
            {
                return;
            }

            Graphic[] graphics = label.Graphics;
            for (int i = 0; i < graphics.Length; i++)
            {
                Track(graphics[i]);
            }
        }

        static void SetGraphicAlpha(Graphic graphic, float alpha)
        {
            if (graphic == null)
            {
                return;
            }

            Color colour = graphic.color;
            colour.a = alpha;
            graphic.color = colour;
        }

        public void Dispose()
        {
            RestoreActors();
            RestoreField();
            _chrome.Clear();
            _chromeAlpha.Clear();

            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }

            if (_canvas != null)
            {
                Object.Destroy(_canvas.gameObject);
                _canvas = null;
            }

            _renderers.Clear();
        }
    }
}
