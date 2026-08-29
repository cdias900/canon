using System;
using System.Reflection;
using UnityEngine;
using SheepGate.Core;

namespace SheepGate.Player
{
    /// <summary>
    /// Derives a walkability grid from <see cref="MapDef"/>. The row encoding is deliberately
    /// permissive: only characters listed in <see cref="BlockingCharacters"/> block movement and
    /// everything else is walkable. Failing soft (the player walks somewhere odd) is far cheaper
    /// than failing hard (the player cannot move at all), and the scene composer can always
    /// override the grid it feeds to <see cref="PlayerController.Pathfinder"/>.
    /// </summary>
    public static class MapGrid
    {
        /// <summary>Fallback grid size when the map data is missing, matching the POC map.</summary>
        public const int FallbackWidth = 40;
        public const int FallbackHeight = 24;

        /// <summary>
        /// Characters treated as impassable terrain in <c>MapDef.rows</c>.
        ///
        /// Deliberately identical to the world module's own character table: '~' water, '#' house,
        /// 'W' and '=' wall. Ground '.', the blank ' ' filler and the rubble marker 'r' stay
        /// walkable, rubble because the player steps onto a pile to collect it. Anything not
        /// listed here stays walkable: a map that gains a new decorative character should let the
        /// player through, not freeze them in place.
        ///
        /// Settable so the world composer can realign it without editing this file.
        /// </summary>
        public static string BlockingCharacters = "~#W=";

        /// <summary>
        /// True when the first entry of <c>MapDef.rows</c> is the TOP row of the map, so a row
        /// index maps to cell y as height - 1 - rowIndex. Matches the world module's tilemap
        /// builder, which is the authority on what the player can actually see.
        ///
        /// Note for whoever owns the map data: the entity coordinates in map.json disagree with
        /// this flip. All five rubble piles satisfy rows[y][x] == 'r' read straight, and none of
        /// them under the flip, which puts the well on plain ground instead of beside the water.
        /// Only the fallback grid below depends on this value, because the live grid published by
        /// the tilemap is preferred, so movement tracks the drawn map either way.
        /// </summary>
        public static bool RowZeroIsTop = true;

        /// <summary>Builds the walkability grid, indexed [x, y] with y growing upward in world space.</summary>
        public static bool[,] BuildWalkable(MapDef map)
        {
            return BuildWalkable(map, RowZeroIsTop, BlockingCharacters);
        }

        public static bool[,] BuildWalkable(MapDef map, bool rowZeroIsTop, string blockingCharacters)
        {
            string blockers = string.IsNullOrEmpty(blockingCharacters) ? string.Empty : blockingCharacters;

            string[] rows = map != null ? map.rows : null;

            int width = 0;
            int height = 0;

            if (rows != null && rows.Length > 0)
            {
                height = rows.Length;
                for (int i = 0; i < rows.Length; i++)
                {
                    if (rows[i] != null && rows[i].Length > width) width = rows[i].Length;
                }
            }

            if (map != null)
            {
                if (width <= 0) width = map.width;
                if (height <= 0) height = map.height;
            }

            if (width <= 0) width = FallbackWidth;
            if (height <= 0) height = FallbackHeight;

            var walkable = new bool[width, height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++) walkable[x, y] = true;
            }

            if (rows == null) return walkable;

            for (int r = 0; r < rows.Length; r++)
            {
                string row = rows[r];
                if (string.IsNullOrEmpty(row)) continue;

                int y = rowZeroIsTop ? (height - 1 - r) : r;
                if (y < 0 || y >= height) continue;

                int columns = Mathf.Min(row.Length, width);
                for (int x = 0; x < columns; x++)
                {
                    if (blockers.IndexOf(row[x]) >= 0) walkable[x, y] = false;
                }
            }

            return walkable;
        }

        /// <summary>
        /// The walkable grid published by the world module's tilemap, when there is one.
        ///
        /// That array is the single source of truth for what the player can walk on: it is built
        /// alongside the tiles the player actually sees, and it is mutated live as wall stages
        /// rise and rubble piles are cleared. It is returned by reference for exactly that reason,
        /// so a pathfinder built over it stays current without polling.
        ///
        /// The lookup is by name rather than by type because the tilemap is not part of the
        /// frozen architecture contract, and binding to it at compile time would let one
        /// signature change in another module break this one.
        /// </summary>
        public static bool TryGetWorldWalkable(out bool[,] walkable)
        {
            walkable = null;
            ResolveWorldBridge();
            if (WalkableProperty == null) return false;

            try
            {
                object builder = InstanceProperty != null ? InstanceProperty.GetValue(null, null) : null;

                // The Unity lifetime check matters here: a destroyed builder is a non-null
                // reference that must still be treated as absent.
                if (builder as UnityEngine.Object == null)
                {
                    builder = UnityEngine.Object.FindFirstObjectByType(BuilderType);
                    if (builder as UnityEngine.Object == null) return false;
                }

                var grid = WalkableProperty.GetValue(builder, null) as bool[,];
                if (grid == null || grid.GetLength(0) <= 0 || grid.GetLength(1) <= 0) return false;

                walkable = grid;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// A pathfinder over the live world grid when the tilemap has published one, otherwise
        /// over a grid derived from the loaded map, otherwise over an open fallback.
        /// </summary>
        public static GridPathfinder CreatePathfinder()
        {
            bool[,] live;
            if (TryGetWorldWalkable(out live)) return new GridPathfinder(live);

            MapDef map = null;
            try
            {
                map = GameData.Map;
            }
            catch (Exception)
            {
                map = null;
            }
            return new GridPathfinder(BuildWalkable(map));
        }

        private const string BuilderTypeName = "SheepGate.World.TilemapBuilder";

        private static Type BuilderType;
        private static PropertyInfo InstanceProperty;
        private static PropertyInfo WalkableProperty;
        private static bool _bridgeResolved;

        private static void ResolveWorldBridge()
        {
            if (_bridgeResolved) return;

            try
            {
                Type found = Type.GetType(BuilderTypeName, false);
                if (found == null)
                {
                    Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < assemblies.Length && found == null; i++)
                    {
                        found = assemblies[i].GetType(BuilderTypeName, false);
                    }
                }
                if (found == null) return; // Not latched: the assembly may simply not be loaded yet.

                BuilderType = found;
                InstanceProperty = found.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                WalkableProperty = found.GetProperty("Walkable", BindingFlags.Public | BindingFlags.Instance);
                _bridgeResolved = true;
            }
            catch (Exception)
            {
                BuilderType = null;
                InstanceProperty = null;
                WalkableProperty = null;
            }
        }

        /// <summary>The spawn cell declared by the map, or the grid centre when it is absent.</summary>
        public static Vector2Int PlayerSpawnCell(MapDef map)
        {
            if (map != null && map.player_spawn != null)
            {
                return new Vector2Int(map.player_spawn.x, map.player_spawn.y);
            }

            int width = map != null && map.width > 0 ? map.width : FallbackWidth;
            int height = map != null && map.height > 0 ? map.height : FallbackHeight;
            return new Vector2Int(width / 2, height / 2);
        }
    }
}
