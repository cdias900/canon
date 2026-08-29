using System;
using System.Collections.Generic;
using UnityEngine;

namespace SheepGate.World
{
    /// <summary>
    /// Everything in the world the player can tap: residents, rubble piles, the well, the wall.
    ///
    /// Walk-adjacent-first contract, the one PlayerController implements:
    ///   1. hit test the tap with <see cref="FindAt"/> (or a Physics2D raycast: every interactable
    ///      carries a trigger collider);
    ///   2. call <see cref="TryInteract"/> with the player position. It returns true when the
    ///      player already stands close enough and the interaction has run;
    ///   3. when it returns false, path to <see cref="GetApproachCell"/> (or
    ///      <see cref="ApproachWorldPoint"/>) and call <see cref="Interact"/> on arrival.
    ///
    /// Interactables occupy a cell that is marked unwalkable, so the player always ends up beside
    /// them rather than on top of them.
    /// </summary>
    public abstract class InteractableBase : MonoBehaviour
    {
        private static readonly List<InteractableBase> Registry = new List<InteractableBase>();

        private static readonly Vector2Int[] Neighbours =
        {
            new Vector2Int(0, -1),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(1, -1),
            new Vector2Int(-1, -1),
            new Vector2Int(1, 1),
            new Vector2Int(-1, 1)
        };

        /// <summary>Every interactable currently alive in the scene.</summary>
        public static IReadOnlyList<InteractableBase> All
        {
            get { return Registry; }
        }

        /// <summary>Grid cell this interactable stands on.</summary>
        public Vector2Int Cell { get; protected set; }

        /// <summary>Short pt-BR label the UI may show next to the interaction prompt.</summary>
        public string DisplayName { get; protected set; }

        /// <summary>How close the player must be, in world units, for <see cref="TryInteract"/> to fire.</summary>
        public virtual float InteractRadius
        {
            get { return 1.6f; }
        }

        /// <summary>False when the interactable is spent for now, such as a rubble pile taken today.</summary>
        public virtual bool IsAvailable
        {
            get { return isActiveAndEnabled; }
        }

        protected TilemapBuilder Tilemap { get; private set; }

        protected virtual void Awake()
        {
            if (DisplayName == null)
            {
                DisplayName = string.Empty;
            }

            if (!Registry.Contains(this))
            {
                Registry.Add(this);
            }

            EnsureCollider();
        }

        protected virtual void OnDestroy()
        {
            Registry.Remove(this);
        }

        private void EnsureCollider()
        {
            if (GetComponent<Collider2D>() != null)
            {
                return;
            }

            BoxCollider2D collider = gameObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(0.9f, 0.9f);
        }

        /// <summary>
        /// Places the interactable on a cell. When <paramref name="blocksMovement"/> is true the
        /// cell is removed from the walkable grid so the player stops beside it.
        /// </summary>
        public void Place(TilemapBuilder tilemap, Vector2Int cell, bool blocksMovement, float yOffset)
        {
            Tilemap = tilemap;
            Cell = cell;

            if (tilemap != null)
            {
                Vector3 position = tilemap.CellToWorldCenter(cell);
                position.y += yOffset;
                transform.position = position;
                if (blocksMovement)
                {
                    tilemap.SetWalkable(cell.x, cell.y, false);
                }
            }
            else
            {
                transform.position = new Vector3(cell.x + 0.5f, cell.y + 0.5f + yOffset, 0f);
            }
        }

        /// <summary>Marks the occupied cell walkable again, used when a pile is picked up.</summary>
        protected void SetCellBlocking(bool blocks)
        {
            if (Tilemap == null)
            {
                return;
            }

            Tilemap.SetWalkable(Cell.x, Cell.y, !blocks);
        }

        public bool InRange(Vector3 worldPosition)
        {
            Vector2 delta = new Vector2(worldPosition.x - transform.position.x, worldPosition.y - transform.position.y);
            return delta.sqrMagnitude <= InteractRadius * InteractRadius;
        }

        /// <summary>
        /// Runs the interaction when the player is already adjacent. Returns false when the player
        /// must walk first, which is the caller's cue to path to <see cref="GetApproachCell"/>.
        /// </summary>
        public bool TryInteract(Vector3 playerWorldPosition)
        {
            if (!IsAvailable)
            {
                return false;
            }

            if (!InRange(playerWorldPosition))
            {
                return false;
            }

            Interact();
            return true;
        }

        /// <summary>Walkable cell next to this one, closest to <paramref name="from"/>.</summary>
        public Vector2Int GetApproachCell(Vector2Int from)
        {
            Vector2Int best = Cell;
            int bestDistance = int.MaxValue;
            bool found = false;

            for (int i = 0; i < Neighbours.Length; i++)
            {
                Vector2Int candidate = Cell + Neighbours[i];
                if (Tilemap != null && !Tilemap.IsWalkable(candidate))
                {
                    continue;
                }

                int distance = Mathf.Abs(candidate.x - from.x) + Mathf.Abs(candidate.y - from.y);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                    found = true;
                }
            }

            if (!found && Tilemap != null)
            {
                return Tilemap.NearestWalkable(Cell);
            }

            return best;
        }

        /// <summary>World point the player should walk to before interacting.</summary>
        public Vector3 ApproachWorldPoint(Vector3 from)
        {
            Vector2Int fromCell = Tilemap != null
                ? Tilemap.WorldToCell(from)
                : new Vector2Int(Mathf.FloorToInt(from.x), Mathf.FloorToInt(from.y));

            Vector2Int cell = GetApproachCell(fromCell);
            return Tilemap != null ? Tilemap.CellToWorldCenter(cell) : new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
        }

        /// <summary>What happens when the player is beside this and taps it.</summary>
        public abstract void Interact();

        /// <summary>Closest available interactable to a world point, or null.</summary>
        public static InteractableBase FindAt(Vector2 worldPoint, float radius)
        {
            InteractableBase best = null;
            float bestDistance = radius * radius;

            for (int i = 0; i < Registry.Count; i++)
            {
                InteractableBase candidate = Registry[i];
                if (candidate == null || !candidate.IsAvailable)
                {
                    continue;
                }

                Vector3 position = candidate.transform.position;
                float distance = (new Vector2(position.x, position.y) - worldPoint).sqrMagnitude;
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        public static InteractableBase FindAt(Vector2 worldPoint)
        {
            return FindAt(worldPoint, 0.75f);
        }

        /// <summary>Interactable standing on a given cell, or null.</summary>
        public static InteractableBase FindOnCell(Vector2Int cell)
        {
            for (int i = 0; i < Registry.Count; i++)
            {
                InteractableBase candidate = Registry[i];
                if (candidate != null && candidate.Cell == cell)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
