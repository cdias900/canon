using System;
using System.Collections.Generic;
using UnityEngine;

namespace SheepGate.Player
{
    /// <summary>
    /// Plain A* over a rectangular walkability grid. Four-neighbour only, so diagonal
    /// corner cutting cannot happen by construction. The POC map is roughly 40x24, so the
    /// working arrays are preallocated once per instance and reused between queries via a
    /// generation stamp instead of being cleared.
    ///
    /// Grid space and world space are related by the project scale: sprites are 32 pixels
    /// per unit and tiles are 32 pixels wide, so one cell is exactly one world unit and a
    /// cell's world anchor is its centre.
    /// </summary>
    public class GridPathfinder
    {
        /// <summary>Project-wide pixels per unit (see the POC stack table).</summary>
        public const int PixelsPerUnit = 32;

        /// <summary>Tile artwork size in pixels.</summary>
        public const int TileSizePixels = 32;

        /// <summary>World size of one grid cell. 32 px art at 32 PPU is exactly one unit.</summary>
        public const float CellSize = TileSizePixels / (float)PixelsPerUnit;

        private readonly bool[,] _walkable;
        private readonly int _width;
        private readonly int _height;

        private readonly int[] _gScore;
        private readonly int[] _cameFrom;
        private readonly int[] _stamp;
        private readonly byte[] _state; // 0 unvisited, 1 open, 2 closed

        private int[] _heapNode;
        private int[] _heapKey;
        private int _heapCount;

        private int _generation;

        /// <param name="walkable">Indexed [x, y]. The instance keeps the reference, it does not copy.</param>
        public GridPathfinder(bool[,] walkable)
        {
            if (walkable == null) throw new ArgumentNullException(nameof(walkable));
            _walkable = walkable;
            _width = walkable.GetLength(0);
            _height = walkable.GetLength(1);

            int cells = Mathf.Max(1, _width * _height);
            _gScore = new int[cells];
            _cameFrom = new int[cells];
            _stamp = new int[cells];
            _state = new byte[cells];

            int heapCapacity = Mathf.Max(16, cells / 4);
            _heapNode = new int[heapCapacity];
            _heapKey = new int[heapCapacity];
        }

        public int Width { get { return _width; } }
        public int Height { get { return _height; } }

        public bool InBounds(Vector2Int cell)
        {
            return cell.x >= 0 && cell.y >= 0 && cell.x < _width && cell.y < _height;
        }

        public bool IsWalkable(Vector2Int cell)
        {
            return InBounds(cell) && _walkable[cell.x, cell.y];
        }

        /// <summary>Lets the scene composer patch the grid after props are placed.</summary>
        public void SetWalkable(Vector2Int cell, bool value)
        {
            if (InBounds(cell)) _walkable[cell.x, cell.y] = value;
        }

        /// <summary>Clamps an arbitrary cell into the grid.</summary>
        public Vector2Int Clamp(Vector2Int cell)
        {
            return new Vector2Int(
                Mathf.Clamp(cell.x, 0, Mathf.Max(0, _width - 1)),
                Mathf.Clamp(cell.y, 0, Mathf.Max(0, _height - 1)));
        }

        public static Vector2 GridToWorld(Vector2Int cell)
        {
            return new Vector2((cell.x + 0.5f) * CellSize, (cell.y + 0.5f) * CellSize);
        }

        public static Vector2Int WorldToGrid(Vector2 world)
        {
            return new Vector2Int(
                Mathf.FloorToInt(world.x / CellSize),
                Mathf.FloorToInt(world.y / CellSize));
        }

        public static Vector2Int WorldToGrid(Vector3 world)
        {
            return WorldToGrid(new Vector2(world.x, world.y));
        }

        /// <summary>
        /// Shortest four-neighbour path from <paramref name="from"/> to <paramref name="to"/>.
        /// The returned list excludes the start cell and includes the goal cell. It is empty
        /// when the goal is unreachable, blocked, out of bounds, or already the start cell.
        /// </summary>
        public List<Vector2Int> FindPath(Vector2Int from, Vector2Int to)
        {
            var result = new List<Vector2Int>();
            FindPath(from, to, result);
            return result;
        }

        /// <summary>Allocation-free overload. Returns true when a non-empty path was written.</summary>
        public bool FindPath(Vector2Int from, Vector2Int to, List<Vector2Int> result)
        {
            if (result == null) return false;
            result.Clear();

            if (!InBounds(from) || !InBounds(to)) return false;
            if (!_walkable[to.x, to.y]) return false;
            if (from == to) return false;

            _generation++;

            int startIndex = Index(from);
            int goalIndex = Index(to);

            _heapCount = 0;
            Touch(startIndex);
            _gScore[startIndex] = 0;
            _state[startIndex] = 1;
            _cameFrom[startIndex] = -1;
            HeapPush(startIndex, Heuristic(from, to));

            bool found = false;
            while (_heapCount > 0)
            {
                int current = HeapPop();
                if (_state[current] == 2) continue; // stale duplicate entry
                _state[current] = 2;

                if (current == goalIndex)
                {
                    found = true;
                    break;
                }

                int cx = current % _width;
                int cy = current / _width;
                int nextG = _gScore[current] + 1;

                TryRelax(cx - 1, cy, nextG, current, to);
                TryRelax(cx + 1, cy, nextG, current, to);
                TryRelax(cx, cy - 1, nextG, current, to);
                TryRelax(cx, cy + 1, nextG, current, to);
            }

            if (!found) return false;

            // Walk the parent chain back, then reverse in place.
            int node = goalIndex;
            while (node != -1 && node != startIndex)
            {
                result.Add(new Vector2Int(node % _width, node / _width));
                node = _cameFrom[node];
            }
            if (node != startIndex)
            {
                result.Clear();
                return false;
            }
            result.Reverse();
            return result.Count > 0;
        }

        /// <summary>
        /// Breadth-first search outward from <paramref name="origin"/> for the closest walkable
        /// cell, used when the player taps a blocked tile. Radius is measured in cells.
        /// </summary>
        public bool TryFindNearestWalkable(Vector2Int origin, int maxRadius, out Vector2Int nearest)
        {
            nearest = origin;
            if (IsWalkable(origin)) return true;
            if (maxRadius < 1) return false;

            for (int r = 1; r <= maxRadius; r++)
            {
                bool found = false;
                int bestDistance = int.MaxValue;

                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        // Only the ring at exactly radius r, so nearer rings are exhausted first.
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;

                        var candidate = new Vector2Int(origin.x + dx, origin.y + dy);
                        if (!IsWalkable(candidate)) continue;

                        // Within a ring, prefer the true closest: an orthogonal neighbour reads as
                        // the obvious destination where a diagonal one looks like a misfire.
                        int distance = dx * dx + dy * dy;
                        if (distance >= bestDistance) continue;

                        bestDistance = distance;
                        nearest = candidate;
                        found = true;
                    }
                }

                if (found) return true;
            }
            return false;
        }

        private void TryRelax(int x, int y, int tentativeG, int parent, Vector2Int goal)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height) return;
            if (!_walkable[x, y]) return;

            int index = y * _width + x;
            Touch(index);
            if (_state[index] == 2) return;
            if (_state[index] == 1 && _gScore[index] <= tentativeG) return;

            _gScore[index] = tentativeG;
            _cameFrom[index] = parent;
            _state[index] = 1;
            HeapPush(index, tentativeG + Heuristic(new Vector2Int(x, y), goal));
        }

        private static int Heuristic(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }

        private int Index(Vector2Int cell)
        {
            return cell.y * _width + cell.x;
        }

        /// <summary>Resets a node lazily when it is first seen during the current query.</summary>
        private void Touch(int index)
        {
            if (_stamp[index] == _generation) return;
            _stamp[index] = _generation;
            _gScore[index] = int.MaxValue;
            _cameFrom[index] = -1;
            _state[index] = 0;
        }

        private void HeapPush(int node, int key)
        {
            if (_heapCount == _heapNode.Length)
            {
                int grown = _heapNode.Length * 2;
                Array.Resize(ref _heapNode, grown);
                Array.Resize(ref _heapKey, grown);
            }

            int i = _heapCount++;
            _heapNode[i] = node;
            _heapKey[i] = key;

            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (_heapKey[parent] <= _heapKey[i]) break;
                HeapSwap(parent, i);
                i = parent;
            }
        }

        private int HeapPop()
        {
            int top = _heapNode[0];
            _heapCount--;
            if (_heapCount > 0)
            {
                _heapNode[0] = _heapNode[_heapCount];
                _heapKey[0] = _heapKey[_heapCount];

                int i = 0;
                while (true)
                {
                    int left = (i << 1) + 1;
                    int right = left + 1;
                    int smallest = i;
                    if (left < _heapCount && _heapKey[left] < _heapKey[smallest]) smallest = left;
                    if (right < _heapCount && _heapKey[right] < _heapKey[smallest]) smallest = right;
                    if (smallest == i) break;
                    HeapSwap(smallest, i);
                    i = smallest;
                }
            }
            return top;
        }

        private void HeapSwap(int a, int b)
        {
            int node = _heapNode[a];
            _heapNode[a] = _heapNode[b];
            _heapNode[b] = node;

            int key = _heapKey[a];
            _heapKey[a] = _heapKey[b];
            _heapKey[b] = key;
        }
    }
}
