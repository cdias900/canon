using System.Collections.Generic;
using UnityEngine;

namespace SheepGate.Art
{
    /// <summary>
    /// Drawn tiles, read out of one CC0 spritesheet.
    ///
    /// The rest of this folder generates its art from code, which was always meant to be
    /// temporary: MVP-SCOPE.md section 11 asks for "Tileset CC0 (Kenney ou equivalente)",
    /// and the README calls what the code draws "placeholder art" whose replacement means
    /// implementing one seam. This is that seam being implemented. Code can lay out a map and it
    /// can draw a line, but it cannot compete with tiles somebody sat down and shaded, and two
    /// rounds of trying to make procedural ground look like country was the evidence.
    ///
    /// Nothing here replaces <see cref="ArtLibrary"/>. A key either has a drawn tile behind it or
    /// falls through to the generated one, so the game keeps running with a missing sheet and the
    /// swap can happen a key at a time.
    ///
    /// The sheet ships as a .bytes file and is decoded at runtime rather than imported as a Unity
    /// sprite. That keeps the whole thing inside compiler-checked code: no import settings to get
    /// wrong, no meta file to hand-write, no sprite slicing stored in an asset nobody can review
    /// in a diff.
    /// </summary>
    public static class Tileset
    {
        /// <summary>Kenney's Roguelike/RPG pack, CC0. Licence in Assets/Art/.</summary>
        const string ResourcePath = "Art/roguelike_sheet";

        /// <summary>Source tiles are 16x16 with a 1px margin, and are doubled on the way out.</summary>
        const int SourceTile = 16;
        const int Pitch = 17;
        const int Scale = 2;

        /// <summary>Size of a tile as the rest of the game expects it.</summary>
        public const int TileSize = SourceTile * Scale;

        // Which drawn tile stands behind which key. Column and row on the sheet, counted from the
        // top left, the way the sheet is read rather than the way a texture is stored.
        static readonly Dictionary<string, Vector2Int> Tiles = new Dictionary<string, Vector2Int>
        {
            { ArtKeys.TileWater, new Vector2Int(11, 23) }    // still water
        };

        // Deliberately not mapped:
        //
        // tile_ground, tile_rubble - tiled five by five and looked at as a field, which is the
        //   only honest way to judge a fill, this sheet has nothing that works outdoors. Its
        //   seamless fills are pale interior floors that read as washed out over a whole screen,
        //   and its textured ones are autotile edge pieces that show a seam every tile. The
        //   generated ground was tuned instead; see TileArt.Ground.
        //
        // Deliberately not mapped yet:
        //
        // tile_house - every roof on this sheet is a piece of a building several tiles across:
        //   ridges, eaves and corners. A house here is one cell, and a single corner piece reads
        //   as a brown square, which is worse than the generated house that at least has a door.
        //   It needs either a multi-tile house in the tilemap or a tile drawn for one cell.
        //
        // wall_0..4 - the wall has to read as five stages of the same wall going up. Picking five
        //   unrelated tiles that merely look different would break the one thing that row of the
        //   map has to communicate.

        static Color32[] _sheet;
        static int _sheetWidth;
        static int _sheetHeight;
        static bool _loadAttempted;

        /// <summary>True once the sheet has been found and decoded.</summary>
        public static bool Available
        {
            get
            {
                Load();
                return _sheet != null;
            }
        }

        /// <summary>
        /// Pixels for a key, doubled to <see cref="TileSize"/>, or false when the key has no drawn
        /// tile behind it yet. Rows come back bottom-up, which is what a Unity texture wants.
        /// </summary>
        public static bool TryGetPixels(string key, out Color32[] pixels, out int size)
        {
            pixels = null;
            size = TileSize;

            Vector2Int cell;
            if (string.IsNullOrEmpty(key) || !Tiles.TryGetValue(key, out cell))
            {
                return false;
            }

            Load();
            if (_sheet == null)
            {
                return false;
            }

            // The sheet is read top-down and stored bottom-up, so the row index is flipped here
            // and nowhere else.
            int originX = cell.x * Pitch;
            int originY = _sheetHeight - (cell.y * Pitch + SourceTile);
            if (originX < 0 || originY < 0 || originX + SourceTile > _sheetWidth || originY + SourceTile > _sheetHeight)
            {
                Debug.LogWarning("[Art] Tile " + cell.x + "," + cell.y + " for '" + key + "' is off the sheet.");
                return false;
            }

            pixels = new Color32[TileSize * TileSize];

            for (int y = 0; y < SourceTile; y++)
            {
                for (int x = 0; x < SourceTile; x++)
                {
                    Color32 source = _sheet[(originY + y) * _sheetWidth + originX + x];

                    // Nearest-neighbour double. Anything smoother would blur pixel art, and the
                    // doubling exists so a drawn tile lands on the same 32px grid the generated
                    // ones use rather than at half the size of everything around it.
                    for (int j = 0; j < Scale; j++)
                    {
                        int row = (y * Scale + j) * TileSize;
                        for (int i = 0; i < Scale; i++)
                        {
                            pixels[row + x * Scale + i] = source;
                        }
                    }
                }
            }

            return true;
        }

        static void Load()
        {
            if (_loadAttempted)
            {
                return;
            }

            _loadAttempted = true;

            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.Log("[Art] No drawn tileset at Resources/" + ResourcePath + "; using generated art.");
                return;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(asset.bytes))
            {
                Debug.LogWarning("[Art] The tileset at Resources/" + ResourcePath + " could not be decoded.");
                return;
            }

            texture.filterMode = FilterMode.Point;
            _sheetWidth = texture.width;
            _sheetHeight = texture.height;
            _sheet = texture.GetPixels32();

            Debug.Log("[Art] Drawn tileset loaded: " + _sheetWidth + "x" + _sheetHeight + ", " + Tiles.Count + " key(s) mapped.");
        }
    }
}
