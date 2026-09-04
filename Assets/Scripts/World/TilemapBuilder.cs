using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using SheepGate.Art;
using SheepGate.Core;

namespace SheepGate.World
{
    /// <summary>
    /// Builds the village tilemap at runtime from <see cref="MapDef"/> and publishes the walkable
    /// grid consumed by the pathfinder.
    ///
    /// THE COORDINATE CONVENTION, AND THE ONLY ONE. Two spaces meet in this class and they are
    /// not the same one, so read this before editing a map file:
    ///
    ///   * rows[0] is the TOP row of the map, the way a map reads in a text editor. A row index
    ///     becomes a cell row as y = height - 1 - rowIndex, which is
    ///     <see cref="RowIndexForCellY"/>, and that expression is its own inverse.
    ///   * Every GridPos authored in content is a CELL coordinate: x grows right, y grows UP, and
    ///     cell y = 0 is the bottom row of the map, which is rows[height - 1]. That covers
    ///     map.json player_spawn, rubble and well, and the npcs.json spawns.
    ///
    /// So the tile under an entity at (x, y) is rows[height - 1 - y][x], never rows[y][x]. Cell
    /// space wins because GameScene, NpcActor and WallSystem each consume a GridPos straight as a
    /// cell and there is no conversion hook between them and the map file. If props ever look
    /// mirrored against the ground again, a map file moved, not this parser: fix the map file.
    ///
    /// A space is void, meaning off the map and never walkable. Rows shorter than width, and rows
    /// the file does not supply at all, are filled with void for the same reason: padding with
    /// ground would hand the player cells the author never drew. Unknown characters still fall
    /// back to ground with a single warning.
    ///
    /// Void is not walkability and appearance in one word, and reading it as one is how the outer
    /// border became walkable the first time. What a void cell IS was settled then and does not
    /// move: never walkable, never pathable, never an entity. What a void cell DRAWS is a
    /// separate question, and the answer is the valley carrying on — the same ground the city
    /// stands on, with the fallen wall scattered over it by <see cref="VoidScatter"/> — nearly
    /// clear against the city and thickening outward, saturating within about three cells rather
    /// than climbing all the way to the map's edge. Nothing in that second answer may touch the
    /// first.
    ///
    /// Cell size is one world unit, matching 32 px sprites at 32 pixels per unit.
    /// </summary>
    public sealed class TilemapBuilder : MonoBehaviour
    {
        public const char GroundChar = '.';

        /// <summary>
        /// Off-map filler. A space used to be read as a second ground character, which left the
        /// whole outer border walkable and let the player stroll around the wall and out of the
        /// village. It is void now: the map's boundary is drawn with it.
        /// </summary>
        public const char VoidChar = ' ';

        public const char RubbleChar = 'r';
        public const char WaterChar = '~';
        public const char HouseChar = '#';
        public const char WallChar = 'W';
        public const char WallAltChar = '=';

        public enum CellKind
        {
            Ground = 0,
            Rubble = 1,
            Water = 2,
            House = 3,
            Wall = 4,

            /// <summary>
            /// Outside the map. Never walkable; drawn as the valley continuing, with the ruins of
            /// the wall strewn across it. The map view flattens it to one sprite while it is up.
            /// </summary>
            Void = 5
        }

        /// <summary>Most recently built tilemap. Convenience for modules without a service handle.</summary>
        public static TilemapBuilder Instance { get; private set; }

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>Walkable grid indexed [x, y]. This is the array the pathfinder consumes.</summary>
        public bool[,] Walkable { get; private set; }

        public Tilemap Tilemap { get; private set; }
        public Grid Grid { get; private set; }
        public MapDef Map { get; private set; }

        /// <summary>World-space rectangle covered by the map, used to clamp the camera.</summary>
        public Bounds WorldBounds { get; private set; }

        /// <summary>
        /// The part of the map that is actually drawn: the smallest box holding every cell that is
        /// not Void. A circular city inside a rectangular map leaves wide empty margins, and a
        /// camera clamped to <see cref="WorldBounds"/> is free to travel into them and frame
        /// nothing but the void colour — which is what the patrol view was doing.
        /// </summary>
        public Bounds ContentBounds { get; private set; }

        /// <summary>Row the wall segments are drawn on, taken from the map when it marks one.</summary>
        public int WallRowY { get; private set; }

        /// <summary>World position of the middle of the map, used to frame the opening.</summary>
        public Vector3 CenterWorld()
        {
            return CellToWorldCenter(Width / 2, Height / 2);
        }

        private CellKind[,] _kinds;
        private readonly Dictionary<CellKind, Tile> _tiles = new Dictionary<CellKind, Tile>();

        /// <summary>
        /// How far the terrain is painted past the map rectangle, in cells, on every side.
        ///
        /// The rectangle in <c>map.json</c> is where the author drew; it is not where the valley
        /// ends, and the camera can see past it. A cell nobody paints is the camera's clear
        /// colour, which is the flat black this whole change exists to remove.
        ///
        /// Both depths are derived rather than chosen — see <see cref="SkirtDepth"/>. On this map
        /// they come out at twenty columns and seven rows.
        ///
        /// FOUR SIDES, and what makes that safe. The version before this painted rows only, on the
        /// argument that no shipped aspect can see past the left and right edges. That is true of
        /// the close view and false of the patrol view: ProjectSettings ships
        /// <c>fullscreenMode: 1</c>, so a Mac player runs at the display's own landscape aspect,
        /// and at 1470x956 the patrol view spans world x [-10.8, 50.8] against paint that stopped
        /// at [0, 40] — a sixth of the screen on each side was clear colour with a hard edge
        /// against real terrain. The reason the sides had been left out was the region map, which
        /// draws these same cells as its island: measured against <c>WorldMapOverlay.CoastAt</c>,
        /// the painted rectangle already clears the south bay on the left by only 0.71 world units
        /// at its bottom corner, so even one extra painted column there would put land on the sea.
        /// That is settled by <see cref="OutsideSprite"/> rather than by the width — the skirt
        /// draws nothing at all while the region map is up, and the region draws its own coastline
        /// through the gap, which is exactly what those coordinates held before any of this
        /// existed.
        /// </summary>
        private int _skirtColumns;
        private int _skirtRows;

        /// <summary>
        /// The widest viewport the skirt is sized for, as width over height. Two covers every
        /// phone in landscape and every 16:9 and 16:10 desktop window; past it the outermost strip
        /// falls back to the clear colour, which is the state every landscape window was in
        /// before this.
        /// </summary>
        private const float MaxCoveredAspect = 2f;

        /// <summary>
        /// Cells that draw the fallen wall, indexed
        /// [x + <c>_skirtColumns</c>, y + <c>_skirtRows</c>] — see
        /// <see cref="VoidScatter.Build"/>. Appearance only.
        /// </summary>
        private bool[,] _fallenWall;

        /// <summary>
        /// The tiles the cells outside the city are drawn with, one per variant, and four caches
        /// because a tile carries a sprite and these four groups do not always show the same one.
        ///
        /// Ground and fallen wall are the obvious split. The other one is less obvious and is
        /// the whole reason the skirt can be as wide as it is: cells inside the map rectangle and cells
        /// beyond it part company as soon as the region map asks for the outside to be flattened.
        /// The ones inside the rectangle take the flat sprite, because the region draws the
        /// village's own bounding box as land. The ones beyond it draw nothing, because the region
        /// draws its coastline there and the rectangle plus a full skirt would reach into the sea.
        ///
        /// All four are separate from the city's own ground tiles, and have to be: flattening the
        /// outside may not take the ground the player is standing on with it.
        /// </summary>
        private readonly Dictionary<int, Tile> _voidGroundTiles = new Dictionary<int, Tile>();
        private readonly Dictionary<int, Tile> _voidFallenTiles = new Dictionary<int, Tile>();
        private readonly Dictionary<int, Tile> _skirtGroundTiles = new Dictionary<int, Tile>();
        private readonly Dictionary<int, Tile> _skirtFallenTiles = new Dictionary<int, Tile>();

        /// <summary>
        /// What the cells off the map are drawing. Three states, and the map view drives all
        /// three: the close view wants terrain, the region view flattens the outside to one sprite
        /// so the city reads as a shape, and closing it puts the terrain back.
        /// </summary>
        private enum VoidLook
        {
            Terrain = 0,
            Flat = 1,
            Custom = 2
        }

        private VoidLook _voidLook = VoidLook.Terrain;
        private Sprite _voidOverride;

        /// <summary>
        /// The flat colour the cells outside the map are painted when something asks for them to
        /// be flattened. It used to be what they were painted always, and that was the bug: a
        /// camera clamped to the drawn content still frames the corners of the band, and near
        /// black corners read as a rendering hole rather than as the edge of a valley. Those cells
        /// draw terrain now; this colour is only the default for <see cref="SetVoidColor"/>.
        /// </summary>
        private static readonly Color DefaultVoidColor = new Color(0.05f, 0.055f, 0.06f);

        /// <summary>Colours the tiles fall back to when the sprite library has not been built.</summary>
        private static readonly Color GroundFallbackColor = new Color(0.50f, 0.46f, 0.39f);
        private static readonly Color RubbleFallbackColor = new Color(0.42f, 0.38f, 0.33f);

        private Color _voidColor = DefaultVoidColor;
        private readonly HashSet<char> _warnedChars = new HashSet<char>();

        private void Awake()
        {
            Instance = this;
            if (Walkable == null)
            {
                Walkable = new bool[0, 0];
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Build(MapDef map)
        {
            Instance = this;
            Map = map;

            Width = map != null && map.width > 0 ? map.width : 32;
            Height = map != null && map.height > 0 ? map.height : 20;
            WallRowY = Mathf.Clamp(Height - 6, 1, Height - 2);

            Walkable = new bool[Width, Height];
            _kinds = new CellKind[Width, Height];

            CreateGrid();
            ParseRows(map);

            // Bounds first, because how far the terrain has to reach is a question about where the
            // camera can go, and where the camera can go is ContentBounds.
            Vector3 min = Grid.CellToWorld(new Vector3Int(0, 0, 0));
            Vector3 max = Grid.CellToWorld(new Vector3Int(Width, Height, 0));
            Bounds bounds = new Bounds();
            bounds.SetMinMax(new Vector3(min.x, min.y, 0f), new Vector3(max.x, max.y, 0f));
            WorldBounds = bounds;
            ContentBounds = ComputeContentBounds(bounds);

            _skirtColumns = SkirtDepth(CameraRig.PatrolSize * MaxCoveredAspect, ContentBounds.center.x, Width);
            _skirtRows = SkirtDepth(CameraRig.PatrolSize, ContentBounds.center.y, Height);

            _fallenWall = VoidScatter.Build(VoidMask(), Width, Height, _skirtColumns, _skirtRows, _ruinScale);
            PaintTiles();
            BuildWallFooting();
        }

        /// <summary>
        /// The footing course on every wall cell of the map, so the ring reads as the broken wall
        /// it is and not as a gap in the ground. The wall cells paint as ground in the tilemap;
        /// without this only the four segments existed on screen, and on a phone the close view
        /// is seven cells wide, so the player saw one lone stretch of stones in a field and asked
        /// where the wall was. The segments draw on top of this on the row they build.
        /// </summary>
        private void BuildWallFooting()
        {
            Sprite footing = WorldRuntime.GetSprite(ArtKeys.Wall(0));
            if (footing == null || _kinds == null)
            {
                return;
            }

            GameObject root = new GameObject("WallFooting");
            root.transform.SetParent(transform, false);

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (_kinds[x, y] != CellKind.Wall)
                    {
                        continue;
                    }

                    GameObject cell = new GameObject("Footing_" + x + "_" + y);
                    cell.transform.SetParent(root.transform, false);
                    cell.transform.position = CellToWorldCenter(x, y);
                    SpriteRenderer renderer = cell.AddComponent<SpriteRenderer>();
                    renderer.sprite = footing;
                    // One below the segment that shares the cell, so a course always covers its footing.
                    renderer.sortingOrder = WorldRuntime.SortingOrderForCell(Height, y) - 1;
                }
            }
        }

        /// <summary>
        /// Share of the fallen wall still lying outside once every course is standing. Not zero:
        /// a city that has just been rebuilt is not a lawn, and a field that emptied entirely would
        /// take the boundary of the world with it — the stone is what says where the map ends.
        /// </summary>
        public const float RuinLeftAtFullWall = 0.3f;

        private float _ruinScale = 1f;

        /// <summary>
        /// The world's stage, derived from the wall (design system rule 11): the ruin outside the
        /// city thins as the courses go up, from all of it at bare ground to
        /// <see cref="RuinLeftAtFullWall"/> at a finished wall. Only the cells outside the city are
        /// repainted; nothing the player walks on changes, and nothing here is set by content.
        /// Cheap enough to call on every course: the map is a few hundred cells and the tiles are
        /// cached by variant.
        /// </summary>
        public void ApplyWallProgress(float fraction)
        {
            float scale = Mathf.Lerp(1f, RuinLeftAtFullWall, Mathf.Clamp01(fraction));
            if (Mathf.Approximately(scale, _ruinScale) || Tilemap == null || _kinds == null)
            {
                return;
            }

            _ruinScale = scale;
            _fallenWall = VoidScatter.Build(VoidMask(), Width, Height, _skirtColumns, _skirtRows, _ruinScale);
            PaintOutside();
        }

        /// <summary>Repaints the void inside the rectangle and the skirt around it, and nothing else.</summary>
        private void PaintOutside()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (_kinds[x, y] != CellKind.Void)
                    {
                        continue;
                    }

                    Tile tile = OutsideTileFor(x, y, false);
                    if (tile != null)
                    {
                        Tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                    }
                }
            }

            PaintSkirt();
        }

        /// <summary>
        /// How many cells past one edge of the map rectangle a camera can frame, along one axis.
        ///
        /// <c>CameraRig.ClampToBounds</c> keeps the viewport inside <see cref="ContentBounds"/>
        /// while it fits and centres it on <see cref="ContentBounds"/> when it does not, so the
        /// widest thing the player ever sees along an axis is the content's centre plus the
        /// viewport's half-size. The patrol view is the worst case on both axes — it is the larger
        /// orthographic size, and clamping only ever shows less.
        ///
        /// <paramref name="half"/> is that half-size in world units, <paramref name="center"/> the
        /// centre of the content on this axis, and <paramref name="span"/> the map rectangle's
        /// length on it. The two ends are measured separately and the deeper one wins, because the
        /// skirt is the same depth all the way round.
        /// </summary>
        private static int SkirtDepth(float half, float center, float span)
        {
            float before = half - center;
            float after = center + half - span;
            float deepest = Mathf.Max(before, after);
            return deepest <= 0f ? 0 : Mathf.CeilToInt(deepest);
        }

        /// <summary>The box around every non-Void cell, or the whole map when there are none.</summary>
        private Bounds ComputeContentBounds(Bounds fallback)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (_kinds[x, y] == CellKind.Void)
                    {
                        continue;
                    }

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (minX > maxX || minY > maxY)
            {
                return fallback;
            }

            Vector3 low = Grid.CellToWorld(new Vector3Int(minX, minY, 0));
            Vector3 high = Grid.CellToWorld(new Vector3Int(maxX + 1, maxY + 1, 0));

            Bounds content = new Bounds();
            content.SetMinMax(new Vector3(low.x, low.y, 0f), new Vector3(high.x, high.y, 0f));
            return content;
        }


        /// <summary>
        /// Index into <see cref="MapDef.rows"/> that holds cell row <paramref name="y"/>, and,
        /// because the expression is its own inverse, the cell row held by a row index. This is
        /// the single place the row-order convention described on the class is spelled out.
        /// </summary>
        public static int RowIndexForCellY(int y, int height)
        {
            return height - 1 - y;
        }

        private void CreateGrid()
        {
            GameObject gridObject = new GameObject("Grid");
            gridObject.transform.SetParent(transform, false);
            Grid = gridObject.AddComponent<UnityEngine.Grid>();
            Grid.cellSize = new Vector3(1f, 1f, 0f);

            GameObject tilemapObject = new GameObject("Ground");
            tilemapObject.transform.SetParent(gridObject.transform, false);
            Tilemap = tilemapObject.AddComponent<UnityEngine.Tilemaps.Tilemap>();
            TilemapRenderer renderer = tilemapObject.AddComponent<UnityEngine.Tilemaps.TilemapRenderer>();
            renderer.sortingOrder = -1000;
        }

        private void ParseRows(MapDef map)
        {
            string[] rows = map != null ? map.rows : null;
            int rowCount = rows != null ? rows.Length : 0;

            for (int y = 0; y < Height; y++)
            {
                int rowIndex = RowIndexForCellY(y, Height);
                string row = rowIndex >= 0 && rowIndex < rowCount ? rows[rowIndex] : null;

                for (int x = 0; x < Width; x++)
                {
                    // Anything the file did not draw is off the map, not free ground.
                    char symbol = row != null && x < row.Length ? row[x] : VoidChar;
                    CellKind kind = KindOf(symbol);
                    _kinds[x, y] = kind;
                    Walkable[x, y] = kind == CellKind.Ground || kind == CellKind.Rubble;
                }
            }

            int resolved = ResolveWallRow(map, Width, Height);
            if (resolved >= 0)
            {
                WallRowY = resolved;
            }
            else
            {
                Debug.LogWarning("[World] Map marks no wall row; wall segments will be placed on row y=" + WallRowY + ".");
            }
        }

        /// <summary>
        /// The cell y the wall segments are built on, or -1 when the map draws no wall at all.
        ///
        /// The row with the most wall cells wins: on a ring every row has a wall cell or two at its
        /// sides, and the first one a scan meets is whichever edge it starts from, not the stretch
        /// the player is assigned. The circular map shipped with its straight run along the north
        /// while the segments sat on a lone ring cell at the south, on open ground, under the
        /// bottom of the phone's screen; that is what this rule replaces. A map may also say the
        /// row outright with <c>wall_row</c>, which wins, with a warning when the drawing disagrees
        /// so the two are never silently out of step.
        /// </summary>
        public static int ResolveWallRow(MapDef map, int width, int height)
        {
            string[] rows = map != null ? map.rows : null;
            int rowCount = rows != null ? rows.Length : 0;
            int bestRow = -1;
            int bestCount = 0;

            for (int y = 0; y < height; y++)
            {
                int rowIndex = RowIndexForCellY(y, height);
                string row = rowIndex >= 0 && rowIndex < rowCount ? rows[rowIndex] : null;
                if (row == null)
                {
                    continue;
                }

                int count = 0;
                for (int x = 0; x < width && x < row.Length; x++)
                {
                    if (row[x] == WallChar || row[x] == WallAltChar)
                    {
                        count++;
                    }
                }

                if (count > bestCount)
                {
                    bestCount = count;
                    bestRow = y;
                }
            }

            int declared = map != null ? map.wall_row : -1;
            if (declared >= 0 && declared < height)
            {
                if (bestRow >= 0 && declared != bestRow)
                {
                    Debug.LogWarning("[World] map.json says wall_row=" + declared + " but the row with the most wall cells is y=" + bestRow + "; building on the declared row.");
                }

                return declared;
            }

            return bestRow;
        }

        private CellKind KindOf(char symbol)
        {
            switch (symbol)
            {
                case VoidChar:
                    return CellKind.Void;
                case GroundChar:
                case ',':
                case '_':
                case '0':
                    return CellKind.Ground;
                case RubbleChar:
                case 'R':
                    return CellKind.Rubble;
                case WaterChar:
                    return CellKind.Water;
                case HouseChar:
                case 'x':
                case 'X':
                    return CellKind.House;
                case WallChar:
                case 'w':
                case WallAltChar:
                    return CellKind.Wall;
                default:
                    if (_warnedChars.Add(symbol))
                    {
                        Debug.LogWarning("[World] Unknown map character '" + symbol + "'; treated as ground.");
                    }

                    return CellKind.Ground;
            }
        }

        private void PaintTiles()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    CellKind kind = _kinds[x, y];
                    Tile tile;
                    if (kind == CellKind.Void)
                    {
                        tile = OutsideTileFor(x, y, false);
                    }
                    else if (kind == CellKind.Ground)
                    {
                        tile = GroundTileFor(x, y);
                    }
                    else
                    {
                        tile = TileFor(kind);
                    }

                    if (tile != null)
                    {
                        Tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                    }
                }
            }

            PaintSkirt();
        }

        /// <summary>
        /// Paints the terrain that carries on past the map rectangle, on all four sides — see
        /// <see cref="SkirtDepth"/> for how deep, and the field it fills for why.
        ///
        /// These cells are outside the grid entirely: they are not in <c>_kinds</c>, not in
        /// <see cref="Walkable"/>, and <see cref="InBounds"/> already answers false for every one
        /// of them, which is what makes this safe. A Unity tilemap has no bounds of its own, so
        /// this is a sprite at a coordinate and nothing more. Nothing about where the world ends
        /// moves: <see cref="WorldBounds"/> and <see cref="ContentBounds"/> are computed from the
        /// map rectangle and the camera still clamps to the same box it always did.
        /// </summary>
        private void PaintSkirt()
        {
            for (int x = -_skirtColumns; x < Width + _skirtColumns; x++)
            {
                for (int y = -_skirtRows; y < Height + _skirtRows; y++)
                {
                    if (x >= 0 && x < Width && y >= 0 && y < Height)
                    {
                        continue;
                    }

                    Tile tile = OutsideTileFor(x, y, true);
                    if (tile != null)
                    {
                        Tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                    }
                }
            }
        }

        /// <summary>
        /// One of several ground tiles, chosen by the cell's own coordinates.
        ///
        /// A single ground tile repeated over a map draws a grid: whatever pebble or crack the
        /// tile happens to carry lands at the same spot in every cell, and the eye finds the
        /// lattice immediately. Hiding it under louder noise is what made the ground read as
        /// camouflage, so the fix is more tiles rather than a busier one.
        ///
        /// The choice is a hash of the coordinates, not a counter and not a random draw: the same
        /// cell must pick the same tile every time the map is built, or the ground would shuffle
        /// itself between the village and the map view.
        /// </summary>
        private Tile GroundTileFor(int x, int y)
        {
            int variant = GroundVariantFor(x, y);

            Tile tile;
            if (_groundTiles.TryGetValue(variant, out tile) && tile != null)
            {
                return tile;
            }

            tile = CreateTile("tile_ground_" + variant, GroundSpriteFor(variant));
            _groundTiles[variant] = tile;
            return tile;
        }

        /// <summary>Which ground variant a cell draws. The cells off the map ask this too.</summary>
        private static int GroundVariantFor(int x, int y)
        {
            int hash = x * 73856093 ^ y * 19349663;
            return Mathf.Abs(hash) % ArtKeys.GroundVariantCount;
        }

        private static Sprite GroundSpriteFor(int variant)
        {
            return WorldRuntime.GetSprite(ArtKeys.GroundVariant(variant))
                ?? WorldRuntime.GetSprite(ArtKeys.TileGround)
                ?? WorldRuntime.SolidSprite(GroundFallbackColor);
        }

        private readonly Dictionary<int, Tile> _groundTiles = new Dictionary<int, Tile>();

        /// <summary>Which cells are off the map, for the scatter. It reads this and nothing else.</summary>
        private bool[,] VoidMask()
        {
            bool[,] mask = new bool[Width, Height];
            if (_kinds == null)
            {
                return mask;
            }

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    mask[x, y] = _kinds[x, y] == CellKind.Void;
                }
            }

            return mask;
        }

        /// <summary>
        /// The tile a cell outside the city draws.
        ///
        /// Ground, chosen with the same variant hash the city's own ground uses, so the texture
        /// runs straight across the boundary instead of stopping at it — the valley does not end
        /// where the author stopped drawing. Some of those cells draw a fallen-wall tile instead,
        /// and which ones is <see cref="VoidScatter"/>'s decision: the wall that fell outward is
        /// what tells the player where the world ends, now that the ground no longer does. The
        /// stone picks its variant from the cell's coordinates exactly as the ground does, and
        /// with its own multipliers, so the two fields do not line up with each other.
        ///
        /// <paramref name="skirt"/> is true for the cells beyond the map rectangle. It changes
        /// nothing about what is drawn here and everything about what happens when the region map
        /// flattens the outside; see <see cref="OutsideSprite"/>.
        ///
        /// It is a sprite swap and nothing else. The cell is still void, still unwalkable, and the
        /// stone here is terrain — not a <see cref="RubblePile"/>, which is a thing the player
        /// clears and which draws prop_rubble on top of the ground rather than as it. That is why
        /// it is <c>tile_fallen_wall</c> and not <c>tile_rubble</c>: two cells that mean opposite
        /// things may not draw the same pixels.
        /// </summary>
        private Tile OutsideTileFor(int x, int y, bool skirt)
        {
            bool fallen = FallenWallAt(x, y);
            int variant = fallen ? FallenWallVariantFor(x, y) : GroundVariantFor(x, y);

            Dictionary<int, Tile> cache = fallen
                ? (skirt ? _skirtFallenTiles : _voidFallenTiles)
                : (skirt ? _skirtGroundTiles : _voidGroundTiles);

            Tile tile;
            if (cache.TryGetValue(variant, out tile) && tile != null)
            {
                return tile;
            }

            string name = (skirt ? "tile_skirt_" : "tile_void_") + (fallen ? "fallen_" : "ground_") + variant;
            tile = CreateTile(name, OutsideSprite(fallen, variant, skirt));
            cache[variant] = tile;
            return tile;
        }

        /// <summary>
        /// Whether the cell draws the fallen wall rather than bare ground. The scatter grid covers
        /// the skirt on every side, so it is offset by both skirt depths, and this is the one
        /// place those offsets are spelled out.
        /// </summary>
        private bool FallenWallAt(int x, int y)
        {
            if (_fallenWall == null)
            {
                return false;
            }

            int column = x + _skirtColumns;
            int row = y + _skirtRows;
            return column >= 0 && row >= 0
                && column < _fallenWall.GetLength(0) && row < _fallenWall.GetLength(1)
                && _fallenWall[column, row];
        }

        /// <summary>
        /// Which fallen-wall variant a cell draws, chosen from the cell's own coordinates exactly
        /// as the ground's is. Different multipliers from <see cref="GroundVariantFor"/> on
        /// purpose: sharing them would make the stone a function of the ground underneath it, and
        /// the two fields would repeat together.
        /// </summary>
        /// <summary>
        /// Which fallen-wall variant a cell draws.
        ///
        /// <b>The mix is load-bearing, not decoration.</b> This was
        /// <c>Mathf.Abs(x * 40503461 ^ y * 12582917) % 12</c>, and both multipliers are 1 mod 4
        /// while the variant count divides by 4 — so the low bits never moved and
        /// <c>variant % 4</c> was exactly <c>((x ^ y) &amp; 3)</c> for every cell on the map. Twelve
        /// variants collapsed into four classes on a 4x4 lattice: the same lattice this tile was
        /// drawn to break, reintroduced by the code that picks it. A modulo only sees the low bits,
        /// so the finalizer below moves the high ones down before it.
        /// </summary>
        private static int FallenWallVariantFor(int x, int y)
        {
            unchecked
            {
                uint hash = (uint)(x * 40503461) ^ (uint)(y * 12582917);
                hash ^= hash >> 16;
                hash *= 0x7feb352du;
                hash ^= hash >> 15;
                hash *= 0x846ca68bu;
                hash ^= hash >> 16;
                return (int)(hash % (uint)ArtKeys.FallenWallVariantCount);
            }
        }

        /// <summary>
        /// What one of the outside tiles should be showing right now. Every outside tile is
        /// created and repainted through here, so the region map finds the outside as it asked
        /// for it even if it asked before the map was built.
        ///
        /// The skirt is the interesting case. While the region map is up it draws nothing at all,
        /// and the region's own coastline shows through the gap. That is deliberate and it is what
        /// lets the skirt be twenty columns wide: the region draws the map rectangle as land
        /// inside an island, and the rectangle's bottom-left corner already sits 0.71 world units
        /// inside the south bay of <c>WorldMapOverlay.CoastAt</c>. One extra painted column there
        /// would put village ground on the sea; twenty would put it in open water on both sides.
        /// Blank is also exactly what those coordinates held before the skirt existed, so the
        /// region map looks the way it always did.
        /// </summary>
        private Sprite OutsideSprite(bool fallen, int variant, bool skirt)
        {
            Sprite terrain = fallen ? FallenWallSpriteFor(variant) : GroundSpriteFor(variant);
            if (_voidLook == VoidLook.Terrain)
            {
                return terrain;
            }

            if (skirt)
            {
                return null;
            }

            if (_voidLook == VoidLook.Flat)
            {
                return WorldRuntime.SolidSprite(_voidColor);
            }

            return _voidOverride != null ? _voidOverride : terrain;
        }

        private static Sprite FallenWallSpriteFor(int variant)
        {
            return WorldRuntime.GetSprite(ArtKeys.FallenWallVariant(variant))
                ?? WorldRuntime.SolidSprite(RubbleFallbackColor);
        }

        private static Tile CreateTile(string name, Sprite sprite)
        {
            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = name;
            tile.colliderType = Tile.ColliderType.None;
            tile.sprite = sprite;
            return tile;
        }

        private Tile TileFor(CellKind kind)
        {
            Tile tile;
            if (_tiles.TryGetValue(kind, out tile) && tile != null)
            {
                return tile;
            }

            tile = CreateTile("tile_" + kind.ToString().ToLowerInvariant(), SpriteFor(kind));
            _tiles[kind] = tile;
            return tile;
        }

        /// <summary>
        /// The sprite a cell kind is drawn with. Void is not here: what the cells outside the
        /// city draw depends on where they sit and on whether the region map is up, which is
        /// <see cref="OutsideTileFor"/> and <see cref="OutsideSprite"/>.
        /// </summary>
        private Sprite SpriteFor(CellKind kind)
        {
            switch (kind)
            {
                case CellKind.Rubble:
                    return WorldRuntime.GetSprite(ArtKeys.TileRubble) ?? WorldRuntime.SolidSprite(RubbleFallbackColor);
                case CellKind.Water:
                    return WorldRuntime.GetSprite(ArtKeys.TileWater) ?? WorldRuntime.SolidSprite(new Color(0.20f, 0.31f, 0.38f));
                case CellKind.House:
                    return WorldRuntime.GetSprite(ArtKeys.TileHouse) ?? WorldRuntime.SolidSprite(new Color(0.30f, 0.27f, 0.25f));
                case CellKind.Wall:
                    // The wall line renders ground; wall stages are drawn by WallSystem on top.
                    return WorldRuntime.GetSprite(ArtKeys.TileGround) ?? WorldRuntime.SolidSprite(GroundFallbackColor);
                default:
                    return WorldRuntime.GetSprite(ArtKeys.TileGround) ?? WorldRuntime.SolidSprite(GroundFallbackColor);
            }
        }

        /// <summary>
        /// Pushes the current look onto every tile the outside is drawn with. A couple of dozen
        /// shared tiles back three and a half thousand cells, so this is a handful of sprite
        /// assignments and one refresh rather than a walk over the grid.
        /// </summary>
        private void ApplyVoidLook()
        {
            RepaintOutside(_voidGroundTiles, false, false);
            RepaintOutside(_voidFallenTiles, true, false);
            RepaintOutside(_skirtGroundTiles, false, true);
            RepaintOutside(_skirtFallenTiles, true, true);

            if (Tilemap != null)
            {
                Tilemap.RefreshAllTiles();
            }
        }

        private void RepaintOutside(Dictionary<int, Tile> cache, bool fallen, bool skirt)
        {
            foreach (KeyValuePair<int, Tile> entry in cache)
            {
                if (entry.Value != null)
                {
                    entry.Value.sprite = OutsideSprite(fallen, entry.Key, skirt);
                }
            }
        }

        /// <summary>
        /// Flattens every cell outside the map to one colour, losing the terrain until something
        /// restores it. Pulled back far enough the outside is a field rather than a place, and a
        /// field of ground texture with a village in the middle of it reads as clutter.
        /// </summary>
        public void SetVoidColor(Color color)
        {
            if (_voidLook == VoidLook.Flat && _voidColor == color)
            {
                return;
            }

            _voidColor = color;
            _voidOverride = null;
            _voidLook = VoidLook.Flat;
            ApplyVoidLook();
        }

        /// <summary>
        /// Replaces the sprite drawn outside the map. The map view uses it to make those cells the
        /// same ground the region around them is drawn with, so the village sits on the island
        /// instead of inside a rectangle cut out of it.
        /// </summary>
        public void SetVoidSprite(Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            _voidOverride = sprite;
            _voidLook = VoidLook.Custom;
            ApplyVoidLook();
        }

        /// <summary>
        /// Puts the cells outside the map back to the terrain the close view is built around:
        /// valley ground, and the ruins of the wall scattered across it. The name is older than
        /// the terrain — it used to restore a flat colour — and it is kept because it is the seam
        /// the map overlay closes through.
        /// </summary>
        public void RestoreVoidColor()
        {
            if (_voidLook == VoidLook.Terrain)
            {
                return;
            }

            _voidColor = DefaultVoidColor;
            _voidOverride = null;
            _voidLook = VoidLook.Terrain;
            ApplyVoidLook();
        }

        public bool InBounds(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        public bool IsWalkable(int x, int y)
        {
            return InBounds(x, y) && Walkable != null && Walkable[x, y];
        }

        public bool IsWalkable(Vector2Int cell)
        {
            return IsWalkable(cell.x, cell.y);
        }

        public void SetWalkable(int x, int y, bool value)
        {
            if (!InBounds(x, y) || Walkable == null)
            {
                return;
            }

            Walkable[x, y] = value;
        }

        /// <summary>Cell kind at a cell. Anything outside the map reads as <see cref="CellKind.Void"/>.</summary>
        public CellKind KindAt(int x, int y)
        {
            return InBounds(x, y) && _kinds != null ? _kinds[x, y] : CellKind.Void;
        }

        /// <summary>Independent copy of the walkable grid, for consumers that want to keep one.</summary>
        public bool[,] CopyWalkable()
        {
            bool[,] copy = new bool[Width, Height];
            if (Walkable == null)
            {
                return copy;
            }

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    copy[x, y] = Walkable[x, y];
                }
            }

            return copy;
        }

        public Vector3 CellToWorldCenter(int x, int y)
        {
            if (Tilemap != null)
            {
                Vector3 center = Tilemap.GetCellCenterWorld(new Vector3Int(x, y, 0));
                center.z = 0f;
                return center;
            }

            return new Vector3(x + 0.5f, y + 0.5f, 0f);
        }

        public Vector3 CellToWorldCenter(Vector2Int cell)
        {
            return CellToWorldCenter(cell.x, cell.y);
        }

        public Vector2Int WorldToCell(Vector3 world)
        {
            if (Grid != null)
            {
                Vector3Int cell = Grid.WorldToCell(world);
                return new Vector2Int(cell.x, cell.y);
            }

            return new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));
        }

        public Vector2Int ClampCell(Vector2Int cell)
        {
            int x = Mathf.Clamp(cell.x, 0, Mathf.Max(0, Width - 1));
            int y = Mathf.Clamp(cell.y, 0, Mathf.Max(0, Height - 1));
            return new Vector2Int(x, y);
        }

        /// <summary>Closest walkable cell to the given one, searched in growing rings.</summary>
        public Vector2Int NearestWalkable(Vector2Int cell)
        {
            if (IsWalkable(cell))
            {
                return cell;
            }

            int maxRadius = Mathf.Max(Width, Height);
            for (int radius = 1; radius <= maxRadius; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Mathf.Abs(dx) != radius && Mathf.Abs(dy) != radius)
                        {
                            continue;
                        }

                        Vector2Int candidate = new Vector2Int(cell.x + dx, cell.y + dy);
                        if (IsWalkable(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return ClampCell(cell);
        }
    }
}
