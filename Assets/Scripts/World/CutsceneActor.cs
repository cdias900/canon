using System;
using System.Collections;
using SheepGate.Art;
using SheepGate.Core;
using SheepGate.Player;
using UnityEngine;

namespace SheepGate.World
{
    /// <summary>
    /// A person the opening can move around: the neighbour who crosses the square to meet you, and
    /// the man who climbs onto a stone at the gathering.
    ///
    /// Deliberately not an <see cref="NpcActor"/>. That class exists to be tapped and to resolve a
    /// dialogue node per day; this one is never interactive and is driven entirely by the cutscene.
    /// Keeping them apart means the opening cannot leave a tappable ghost behind when it ends.
    ///
    /// Movement is a straight walk, not a path. Every route the opening uses crosses open ground by
    /// construction, and a pathfinder here would only add a way for the opening to stall.
    /// </summary>
    public sealed class CutsceneActor : MonoBehaviour
    {
        public const float WalkSpeed = 2.6f;

        CharacterAppearance _appearance;
        TilemapBuilder _map;

        public static CutsceneActor Spawn(string name, Vector2Int cell, TilemapBuilder map, Transform parent,
                                          int bodyVariant, Color tint)
        {
            var host = new GameObject(name);
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            var actor = host.AddComponent<CutsceneActor>();
            actor._map = map;
            host.transform.position = map != null
                ? map.CellToWorldCenter(cell)
                : new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);

            actor._appearance = host.AddComponent<CharacterAppearance>();
            actor._appearance.Apply(new AppearanceState { body = bodyVariant, top = 1, legs = 2, accessory = 0 });
            actor._appearance.Tint = tint;
            actor._appearance.SortingOrderBase = 120;
            actor._appearance.SetAnimation("idle");
            return actor;
        }

        /// <summary>Walks to a world position, facing the way it travels, then idles.</summary>
        public IEnumerator WalkTo(Vector3 worldTarget, float speed = WalkSpeed)
        {
            worldTarget.z = transform.position.z;

            if (_appearance != null)
            {
                _appearance.SetAnimation("walk");
            }

            while ((transform.position - worldTarget).sqrMagnitude > 0.01f)
            {
                Vector3 before = transform.position;
                transform.position = Vector3.MoveTowards(before, worldTarget, speed * Time.deltaTime);

                Vector3 delta = transform.position - before;
                if (_appearance != null && delta.sqrMagnitude > 0.000001f)
                {
                    _appearance.SetDirection(FacingDirectionExtensions.FromDelta(
                        new Vector2(delta.x, delta.y), FacingDirection.Down));
                }

                yield return null;
            }

            transform.position = worldTarget;
            if (_appearance != null)
            {
                _appearance.SetAnimation("idle");
            }
        }

        /// <summary>Walks to a cell on the map.</summary>
        public IEnumerator WalkToCell(Vector2Int cell, float speed = WalkSpeed)
        {
            Vector3 target = _map != null
                ? _map.CellToWorldCenter(cell)
                : new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
            yield return WalkTo(target, speed);
        }

        /// <summary>Turns to look at something without moving.</summary>
        public void FaceTowards(Vector3 worldPosition)
        {
            if (_appearance == null)
            {
                return;
            }

            Vector3 delta = worldPosition - transform.position;
            _appearance.SetDirection(FacingDirectionExtensions.FromDelta(
                new Vector2(delta.x, delta.y), FacingDirection.Down));
        }

        /// <summary>Hides or shows the actor, for stepping through a door the map has no interior for.</summary>
        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        /// <summary>Moves without walking, for repositioning while off screen.</summary>
        public void WarpToCell(Vector2Int cell)
        {
            transform.position = _map != null
                ? _map.CellToWorldCenter(cell)
                : new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
        }

        public void Despawn()
        {
            if (this != null && gameObject != null)
            {
                Destroy(gameObject);
            }
        }
    }
}
