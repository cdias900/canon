using System.Collections;
using System.Collections.Generic;
using SheepGate.Art;
using SheepGate.Core;
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
        const int ScatterCount = 14;

        // ---- roads ---------------------------------------------------------
        const int RoadDashes = 8;
        const float RoadEdgeMargin = 1.6f;
        const float RoadEndInset = 3.4f;
        const float RoadFallbackStart = 12f;
        const float RoadDashLength = 2.1f;
        const float RoadDashWidth = 0.5f;

        // ---- chrome --------------------------------------------------------
        const float FrameInset = 16f;
        const float FrameThickness = 3f;
        const float PlateOffsetY = -2.4f;   // world units below a marker

        static readonly Color LandTint = new Color(0.74f, 0.71f, 0.62f, 1f);
        static readonly Color WaterTint = new Color(0.74f, 0.86f, 0.88f, 1f);
        static readonly Color DeepWater = new Color(0.13f, 0.21f, 0.24f, 1f);
        static readonly Color ScatterTint = new Color(0.55f, 0.52f, 0.45f, 1f);
        static readonly Color RoadColour = new Color(0.53f, 0.48f, 0.38f, 0.95f);
        static readonly Color FrameColour = new Color(0.63f, 0.58f, 0.48f, 0.55f);
        static readonly Color CityRingColour = new Color(0.66f, 0.60f, 0.49f, 0.30f);
        static readonly Color PlateFill = new Color(0.91f, 0.88f, 0.81f, 0.96f);
        static readonly Color PlateInk = new Color(0.17f, 0.15f, 0.13f, 1f);
        static readonly Color HomePlateFill = new Color(0.78f, 0.42f, 0.29f, 0.96f);
        static readonly Color HomePlateInk = new Color(0.97f, 0.94f, 0.89f, 1f);

        readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();
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

            SpriteRenderer water = NewRenderer(host.transform, "Water", -40);
            water.sprite = ArtLibrary.Get(ArtKeys.TileWater);
            water.color = WaterTint;
            water.drawMode = SpriteDrawMode.Tiled;
            water.size = new Vector2(FieldHalfWidth * 2f, FieldHalfHeight * 2f);
            water.transform.position = new Vector3(centre.x, centre.y, 0f);

            Sprite land = ArtLibrary.Get(ArtKeys.TileGround);
            for (float y = -FieldHalfHeight; y <= FieldHalfHeight; y += 1f)
            {
                float half = LandHalfWidthAt(y);
                if (half <= 0.5f)
                {
                    continue;
                }

                SpriteRenderer row = NewRenderer(host.transform, "Land", -30);
                row.sprite = land;
                row.color = LandTint;
                row.drawMode = SpriteDrawMode.Tiled;
                row.size = new Vector2(half * 2f, 1f);
                row.transform.position = new Vector3(centre.x, centre.y + y, 0f);
            }

            BuildScatter(centre);
        }

        /// <summary>
        /// Half the island's width at one height: an ellipse with the coast pushed in and out so it
        /// does not read as a drawn oval. Deterministic, because two runs of the same map must
        /// produce the same coast.
        /// </summary>
        static float LandHalfWidthAt(float y)
        {
            float t = Mathf.Abs(y) / IslandRadiusY;
            if (t >= 1f)
            {
                return 0f;
            }

            float wobble = Mathf.Sin(y * 0.21f) * CoastWobble
                         + Mathf.Sin(y * 0.073f + 1.7f) * (CoastWobble * 0.55f);

            return IslandRadiusX * Mathf.Sqrt(1f - t * t) + wobble;
        }

        /// <summary>
        /// Rocks and scrub on the open ground. Placed on a fixed sequence rather than at random:
        /// the map has to look the same every time it is opened, or it stops being a map.
        /// </summary>
        void BuildScatter(Vector3 centre)
        {
            var host = new GameObject("Scatter");
            host.transform.SetParent(_root.transform, false);

            // The rubble prop is a pale heap and at this distance a field of them reads as litter
            // dropped on the map. The rubble tile, darkened, reads as broken ground.
            Sprite mark = ArtLibrary.GetTinted(ArtKeys.TileRubble, ScatterTint);
            float villageClearance = _map != null ? Mathf.Max(_map.WorldBounds.extents.x, _map.WorldBounds.extents.y) : 16f;

            for (int i = 0; i < ScatterCount; i++)
            {
                // A cheap deterministic spiral: successive marks land far apart without a table.
                float angle = i * 2.39996f;
                float radius = Mathf.Sqrt((i + 0.5f) / ScatterCount);
                float x = Mathf.Cos(angle) * radius * IslandRadiusX * 0.96f;
                float y = Mathf.Sin(angle) * radius * IslandRadiusY * 0.96f;

                if (Mathf.Abs(x) > LandHalfWidthAt(y) - 1.6f)
                {
                    continue;
                }

                // Nothing is dropped on the village or on the ring around it.
                if (new Vector2(x, y).magnitude < villageClearance + 2f)
                {
                    continue;
                }

                SpriteRenderer renderer = NewRenderer(host.transform, "Mark" + i, -20);
                renderer.sprite = mark;
                renderer.transform.position = new Vector3(centre.x + x, centre.y + y, 0f);
                renderer.transform.localScale = Vector3.one * (1.4f + (i % 3) * 0.35f);
            }
        }

        // ------------------------------------------------------------------ roads and places

        /// <summary>
        /// A track from the village out towards one neighbour: short dashes angled along the
        /// direction of travel, starting where the line leaves the village and stopping short of
        /// the city, so the road leads to it rather than ending on it.
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

            float angle = Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg;

            var host = new GameObject("Road" + index);
            host.transform.SetParent(_root.transform, false);

            for (int i = 0; i < RoadDashes; i++)
            {
                float step = RoadDashes == 1 ? 0.5f : i / (float)(RoadDashes - 1);
                float distance = Mathf.Lerp(from, to, step);

                SpriteRenderer dash = NewRenderer(host.transform, "Dash" + i, -10);
                dash.sprite = ArtLibrary.Get(ArtKeys.TileGround);
                dash.color = RoadColour;
                dash.transform.position = new Vector3(
                    centre.x + heading.x * distance,
                    centre.y + heading.y * distance,
                    0f);
                dash.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                dash.transform.localScale = new Vector3(RoadDashLength, RoadDashWidth, 1f);
            }
        }

        /// <summary>
        /// Where a road begins: the point where the line to a neighbour leaves the village, plus a
        /// small gap. Measured from the tilemap's own bounds rather than a tuned constant, so a
        /// bigger map moves the roads out with it instead of drawing dashes across the houses.
        /// </summary>
        static float RoadStart(Vector2 heading, Vector3 villageExtents)
        {
            float halfWidth = villageExtents.x;
            float halfHeight = villageExtents.y;
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

        void BuildPlace(Place place, Vector3 centre, int index)
        {
            var host = new GameObject("Place" + index);
            host.transform.SetParent(_root.transform, false);
            host.transform.position = new Vector3(centre.x + place.Offset.x, centre.y + place.Offset.y, 0f);

            // A closed city reads as a shape behind its own walls, not as a building you can visit.
            SpriteRenderer body = NewRenderer(host.transform, "Body", 60);
            body.sprite = ArtLibrary.Get(ArtKeys.TileHouse);
            body.color = new Color(0.42f, 0.40f, 0.36f, 1f);
            body.transform.localScale = Vector3.one * place.Size;

            SpriteRenderer ring = NewRenderer(host.transform, "Ring", 59);
            ring.sprite = ArtLibrary.Get(ArtKeys.UiPanel);
            ring.color = new Color(0.22f, 0.21f, 0.19f, 0.75f);
            ring.transform.localScale = Vector3.one * (place.Size * 2.1f);
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
            RectTransform card = Plate(root, "TitleCard", PlateFill, new RectOffset(24, 24, 14, 16), 2f);
            Track(card.GetComponent<Image>());
            card.anchorMin = new Vector2(0.5f, 1f);
            card.anchorMax = new Vector2(0.5f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, -(FrameInset + 20f));

            Text heading = UIKit.CreateText(card, "Heading", Loc.T("world.map.heading"),
                UIKit.FontSize.Body, PlateInk, TextAnchor.MiddleLeft);
            heading.raycastTarget = false;
            Track(heading);

            Text note = UIKit.CreateText(card, "Note", Loc.T("world.map.note"),
                UIKit.FontSize.Small, new Color(0.35f, 0.32f, 0.27f, 1f), TextAnchor.MiddleLeft);
            note.raycastTarget = false;
            Track(note);
        }

        /// <summary>
        /// North, and a stroke pointing at it. The places are laid out around the village rather
        /// than surveyed, so this claims an orientation and nothing more precise than that.
        /// </summary>
        void BuildCompass(RectTransform root)
        {
            Text north = UIKit.CreateText(root, "CompassNorth", Loc.T("world.map.north"),
                UIKit.FontSize.Body, UIKit.Palette.Parchment, TextAnchor.MiddleCenter);
            var northRect = (RectTransform)north.transform;
            northRect.anchorMin = new Vector2(1f, 1f);
            northRect.anchorMax = new Vector2(1f, 1f);
            northRect.pivot = new Vector2(1f, 1f);
            northRect.sizeDelta = new Vector2(64f, 48f);
            northRect.anchoredPosition = new Vector2(-(FrameInset + 26f), -(FrameInset + 26f));
            north.raycastTarget = false;
            Track(north);

            RectTransform needle = UIKit.CreateRect("CompassNeedle", root);
            needle.anchorMin = new Vector2(1f, 1f);
            needle.anchorMax = new Vector2(1f, 1f);
            needle.pivot = new Vector2(0.5f, 1f);
            needle.sizeDelta = new Vector2(FrameThickness, 40f);
            needle.anchoredPosition = new Vector2(-(FrameInset + 52f), -(FrameInset + 78f));

            var image = needle.gameObject.AddComponent<Image>();
            image.color = FrameColour;
            image.raycastTarget = false;
            Track(image);
        }

        /// <summary>A plate on every place, including this one.</summary>
        void BuildPlates(RectTransform root, Vector3 centre)
        {
            float homeOffset = _map != null ? -(_map.WorldBounds.extents.y + 2.4f) : PlateOffsetY;
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
            RectTransform legend = Plate(root, "Legend", PlateFill, new RectOffset(20, 20, 12, 14), 6f);
            Track(legend.GetComponent<Image>());
            legend.anchorMin = new Vector2(0f, 0f);
            legend.anchorMax = new Vector2(0f, 0f);
            legend.pivot = new Vector2(0f, 0f);
            legend.anchoredPosition = new Vector2(FrameInset + 20f, FrameInset + 20f);

            AddLegendRow(legend, "LegendHome", HomePlateFill, Loc.T("world.map.legend.home"));
            AddLegendRow(legend, "LegendClosed", new Color(0.42f, 0.40f, 0.36f, 1f), Loc.T("world.map.legend.closed"));
            AddLegendRow(legend, "LegendRoad", RoadColour, Loc.T("world.map.legend.road"));
        }

        void AddLegendRow(RectTransform parent, string name, Color swatchColour, string text)
        {
            RectTransform row = UIKit.CreateRect(name, parent);

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
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
            swatchLayout.preferredWidth = 26f;
            swatchLayout.preferredHeight = 18f;
            Track(swatchImage);

            Text label = UIKit.CreateText(row, "Text", text, UIKit.FontSize.Small, PlateInk, TextAnchor.MiddleLeft);
            label.raycastTarget = false;
            Track(label);
        }

        /// <summary>
        /// A parchment card that grows to whatever is put inside it. The caller says which way the
        /// contents stack, because a GameObject can only carry one layout group and adding the
        /// wrong one first leaves two fighting over the same children.
        /// </summary>
        static RectTransform Plate(RectTransform parent, string name, Color fill, RectOffset padding, float spacing)
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
            column.childAlignment = TextAnchor.UpperLeft;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;

            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
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
