using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
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

            /// <summary>Outside the map. Never walkable, and drawn as a dark edge band.</summary>
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

        /// <summary>Row the wall segments are drawn on, taken from the map when it marks one.</summary>
        public int WallRowY { get; private set; }

        /// <summary>World position of the middle of the map, used to frame the opening.</summary>
        public Vector3 CenterWorld()
        {
            return CellToWorldCenter(Width / 2, Height / 2);
        }

        private CellKind[,] _kinds;
        private readonly Dictionary<CellKind, Tile> _tiles = new Dictionary<CellKind, Tile>();
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
            PaintTiles();

            Vector3 min = Grid.CellToWorld(new Vector3Int(0, 0, 0));
            Vector3 max = Grid.CellToWorld(new Vector3Int(Width, Height, 0));
            Bounds bounds = new Bounds();
            bounds.SetMinMax(new Vector3(min.x, min.y, 0f), new Vector3(max.x, max.y, 0f));
            WorldBounds = bounds;
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
            bool wallRowFound = false;

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

                    if (kind == CellKind.Wall && !wallRowFound)
                    {
                        WallRowY = y;
                        wallRowFound = true;
                    }
                }
            }

            if (!wallRowFound)
            {
                Debug.LogWarning("[World] Map marks no wall row; wall segments will be placed on row y=" + WallRowY + ".");
            }
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
                    Tile tile = TileFor(kind);
                    if (tile != null)
                    {
                        Tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                    }
                }
            }
        }

        private Tile TileFor(CellKind kind)
        {
            Tile tile;
            if (_tiles.TryGetValue(kind, out tile) && tile != null)
            {
                return tile;
            }

            tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = "tile_" + kind.ToString().ToLowerInvariant();
            tile.colliderType = Tile.ColliderType.None;
            tile.sprite = SpriteFor(kind);
            _tiles[kind] = tile;
            return tile;
        }

        private Sprite SpriteFor(CellKind kind)
        {
            switch (kind)
            {
                case CellKind.Void:
                    // Deliberately darker than the camera's clear colour, so the edge of the map
                    // reads as ground dropping away rather than as a rendering gap.
                    return WorldRuntime.SolidSprite(new Color(0.05f, 0.055f, 0.06f));
                case CellKind.Rubble:
                    return WorldRuntime.GetSprite("tile_rubble") ?? WorldRuntime.SolidSprite(new Color(0.42f, 0.38f, 0.33f));
                case CellKind.Water:
                    return WorldRuntime.GetSprite("tile_water") ?? WorldRuntime.SolidSprite(new Color(0.20f, 0.31f, 0.38f));
                case CellKind.House:
                    return WorldRuntime.GetSprite("tile_house") ?? WorldRuntime.SolidSprite(new Color(0.30f, 0.27f, 0.25f));
                case CellKind.Wall:
                    // The wall line renders ground; wall stages are drawn by WallSystem on top.
                    return WorldRuntime.GetSprite("tile_ground") ?? WorldRuntime.SolidSprite(new Color(0.50f, 0.46f, 0.39f));
                default:
                    return WorldRuntime.GetSprite("tile_ground") ?? WorldRuntime.SolidSprite(new Color(0.50f, 0.46f, 0.39f));
            }
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
