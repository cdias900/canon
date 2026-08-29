using System;
using UnityEngine;
using SheepGate.Core;

namespace SheepGate.World
{
    /// <summary>
    /// A pile of burnt stone lying in the village. Picking one up turns it into the material the
    /// wall is built from.
    ///
    /// A pile taken today comes back tomorrow: the ruins keep producing stone, and a run can never
    /// dead-end for want of material. What was already built is never touched.
    /// </summary>
    public sealed class RubblePile : InteractableBase
    {
        public const int DefaultAmount = 3;
        private const string TakenPrefix = "rubble_taken_";

        public int Index { get; private set; }
        public int Amount { get; private set; }

        private DayCycle _dayCycle;
        private SpriteRenderer _renderer;

        public static RubblePile Spawn(int index, Vector2Int cell, Transform parent, TilemapBuilder tilemap)
        {
            GameObject pileObject = new GameObject("RubblePile_" + index);
            if (parent != null)
            {
                pileObject.transform.SetParent(parent, false);
            }

            RubblePile pile = pileObject.AddComponent<RubblePile>();
            pile.Index = index;
            pile.Amount = DefaultAmount;
            pile.DisplayName = "Entulho";
            pile.Place(tilemap, cell, true, 0f);
            pile.BuildSprite(tilemap != null ? tilemap.Height : 0, cell.y);
            pile.Refresh();
            return pile;
        }

        private void BuildSprite(int mapHeight, int cellY)
        {
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(transform, false);
            _renderer = visual.AddComponent<SpriteRenderer>();
            _renderer.sortingOrder = WorldRuntime.SortingOrderForCell(mapHeight, cellY);

            Sprite sprite = WorldRuntime.GetSprite("prop_rubble");
            _renderer.sprite = sprite != null ? sprite : WorldRuntime.SolidSprite(new Color(0.44f, 0.39f, 0.33f, 1f));
        }

        private void Start()
        {
            _dayCycle = FindDayCycle();
            if (_dayCycle != null)
            {
                _dayCycle.MorningStarted += OnMorningStarted;
            }

            Refresh();
        }

        protected override void OnDestroy()
        {
            if (_dayCycle != null)
            {
                _dayCycle.MorningStarted -= OnMorningStarted;
                _dayCycle = null;
            }

            base.OnDestroy();
        }

        private static DayCycle FindDayCycle()
        {
            DayCycle cycle = null;
            try
            {
                ServiceLocator.TryGet(out cycle);
            }
            catch (Exception)
            {
                cycle = null;
            }

            if (cycle == null)
            {
                cycle = FindFirstObjectByType<DayCycle>();
            }

            return cycle;
        }

        private void OnMorningStarted(int day)
        {
            Refresh();
        }

        public override bool IsAvailable
        {
            get { return isActiveAndEnabled && !TakenToday(); }
        }

        private bool TakenToday()
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return false;
            }

            return state.Counter(TakenPrefix + Index) == state.day;
        }

        /// <summary>Shows or hides the pile for the current day and keeps the grid in sync.</summary>
        public void Refresh()
        {
            bool available = !TakenToday();

            if (_renderer != null)
            {
                _renderer.enabled = available;
            }

            Collider2D collider = GetComponent<Collider2D>();
            if (collider != null)
            {
                collider.enabled = available;
            }

            SetCellBlocking(available);
        }

        public override void Interact()
        {
            if (TakenToday())
            {
                Debug.Log("[World] Rubble pile " + Index + " was already collected today.");
                return;
            }

            ResourceSystem resources = ResourceSystem.Find();
            if (resources == null)
            {
                Debug.LogWarning("[World] No ResourceSystem in the scene; the rubble could not be collected.");
                return;
            }

            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            resources.AddRubble(Amount);
            state.counters[TakenPrefix + Index] = state.day;
            Refresh();
            WorldRuntime.SaveNow();
        }
    }
}
