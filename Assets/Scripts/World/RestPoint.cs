using SheepGate.Core;
using UnityEngine;

namespace SheepGate.World
{
    /// <summary>
    /// The mat by the door, and the only way to stop for the night on purpose.
    ///
    /// A day ends on its own once its work capacity is spent — see <see cref="DayCycle"/> — so
    /// nothing here is ever required. It exists for the two cases the daylight clock alone does
    /// not cover:
    ///
    ///   - a player who is done before their capacity is, and would otherwise have to spend stone
    ///     they did not want to spend just to reach the evening;
    ///   - a player who has decided to build nothing at all today, whose day would otherwise never
    ///     run out;
    ///   - a player whose evening is already pending but held open for a beat they have decided not
    ///     to go back for. Accepting the invitation on day two is exactly that, and without a way
    ///     through it the day could not end at all.
    ///
    /// It is unavailable rather than refusing out loud whenever the day may not end — the last day,
    /// a night already resolving, an evening already arriving on its own. That is the same thing a
    /// rubble pile already does once it has been taken, so it reads as spent, not broken.
    /// </summary>
    public sealed class RestPoint : InteractableBase
    {
        public static RestPoint Spawn(Vector2Int cell, Transform parent, TilemapBuilder tilemap)
        {
            GameObject matObject = new GameObject("RestPoint");
            if (parent != null)
            {
                matObject.transform.SetParent(parent, false);
            }

            RestPoint mat = matObject.AddComponent<RestPoint>();
            mat.DisplayName = Loc.T("world.rest_point");

            // Never blocking: this is the cell the opening walks the player onto to go inside and
            // get dressed, and a mat that took the cell out of the grid would strand that walk.
            mat.Place(tilemap, cell, false, 0f);
            mat.BuildSprite(tilemap != null ? tilemap.Height : 0, cell.y);
            return mat;
        }

        private void BuildSprite(int mapHeight, int cellY)
        {
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();

            // One under its own row, so whoever is standing on the mat is drawn on top of it.
            renderer.sortingOrder = WorldRuntime.SortingOrderForCell(mapHeight, cellY) - 1;

            Sprite sprite = WorldRuntime.GetSprite("prop_mat");
            renderer.sprite = sprite != null ? sprite : WorldRuntime.SolidSprite(new Color(0.69f, 0.56f, 0.39f, 1f));
        }

        public override bool IsAvailable
        {
            get
            {
                if (!isActiveAndEnabled)
                {
                    return false;
                }

                DayCycle cycle = DayCycle.Find();
                return cycle != null && cycle.CanRest;
            }
        }

        public override void Interact()
        {
            DayCycle cycle = DayCycle.Find();
            if (cycle == null)
            {
                Debug.LogWarning("[World] No DayCycle is in the scene; the mat cannot end the day.");
                return;
            }

            cycle.RequestRest();
        }
    }
}
