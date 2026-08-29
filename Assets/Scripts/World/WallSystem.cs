using System;
using System.Collections.Generic;
using UnityEngine;
using SheepGate.Core;

namespace SheepGate.World
{
    /// <summary>
    /// Owns wall progress. Four stages per segment, each stage costing the work units declared in
    /// wall_segments.json. Work accumulates inside the current stage and rolls over into the next.
    ///
    /// Hard product rule: a completed stage NEVER regresses. <see cref="DamageSegment"/> only
    /// clears the work in progress inside the current, unfinished stage.
    /// </summary>
    public class WallSystem : MonoBehaviour
    {
        public const int StagesPerSegment = 4;
        public const int SegmentWidthInCells = 3;

        private static readonly int[] DefaultStageCost = { 3, 3, 4, 4 };

        /// <summary>Raised with the segment id and its new stage whenever a stage completes.</summary>
        public event Action<string, int> SegmentStageChanged;

        /// <summary>Raised with the segment id when the fourth stage completes.</summary>
        public event Action<string> SegmentCompleted;

        /// <summary>Raised when a night resolved without a watch and the segment lost its work in progress.</summary>
        public event Action<string> SegmentDamaged;

        private sealed class SegmentRuntime
        {
            public WallSegmentDef Def;
            public WallSegmentState State;
            public readonly List<SpriteRenderer> Renderers = new List<SpriteRenderer>();
            public WallSegmentInteractable Interactable;
        }

        private readonly Dictionary<string, SegmentRuntime> _segments = new Dictionary<string, SegmentRuntime>();
        private readonly List<string> _order = new List<string>();
        private readonly List<string> _exposed = new List<string>();

        private TilemapBuilder _tilemap;

        public int TotalStages
        {
            get { return _order.Count * StagesPerSegment; }
        }

        public int CompletedStages
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _order.Count; i++)
                {
                    SegmentRuntime segment = _segments[_order[i]];
                    total += Mathf.Clamp(segment.State.stage, 0, StagesPerSegment);
                }

                return total;
            }
        }

        /// <summary>Ids of the segments flagged as exposed in wall_segments.json.</summary>
        public IReadOnlyList<string> ExposedSegmentIds
        {
            get { return _exposed; }
        }

        /// <summary>First exposed segment, the one the night resolves against.</summary>
        public string PrimaryExposedSegmentId
        {
            get
            {
                if (_exposed.Count > 0)
                {
                    return _exposed[0];
                }

                return _order.Count > 0 ? _order[0] : null;
            }
        }

        public IReadOnlyList<string> SegmentIds
        {
            get { return _order; }
        }

        /// <summary>Creates the segment objects and their interactables. Safe to call once per scene.</summary>
        public void Build(TilemapBuilder tilemap)
        {
            _tilemap = tilemap;
            _segments.Clear();
            _order.Clear();
            _exposed.Clear();

            WallSegmentDef[] defs = null;
            try
            {
                defs = GameData.WallSegments;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Reading GameData.WallSegments failed: " + exception.Message);
            }

            if (defs == null || defs.Length == 0)
            {
                Debug.LogWarning("[World] No wall segment definitions found; composing a single fallback segment.");
                WallSegmentDef fallback = new WallSegmentDef();
                fallback.id = "seg_01";
                fallback.grid_x = tilemap != null ? Mathf.Max(1, tilemap.Width / 2) : 20;
                fallback.stage_cost = new int[] { 3, 3, 4, 4 };
                fallback.exposed = true;
                defs = new WallSegmentDef[] { fallback };
            }

            GameState state = WorldRuntime.State;
            GameObject parent = new GameObject("WallSegments");
            parent.transform.SetParent(transform, false);

            for (int i = 0; i < defs.Length; i++)
            {
                WallSegmentDef def = defs[i];
                if (def == null || string.IsNullOrEmpty(def.id))
                {
                    Debug.LogWarning("[World] Skipped a wall segment definition without an id.");
                    continue;
                }

                if (_segments.ContainsKey(def.id))
                {
                    Debug.LogWarning("[World] Duplicate wall segment id \"" + def.id + "\" ignored.");
                    continue;
                }

                SegmentRuntime segment = new SegmentRuntime();
                segment.Def = def;
                segment.State = ResolveState(state, def.id);
                CreateVisual(segment, parent.transform);

                _segments[def.id] = segment;
                _order.Add(def.id);
                if (def.exposed)
                {
                    _exposed.Add(def.id);
                }

                UpdateVisual(segment);
            }

            if (_exposed.Count == 0 && _order.Count > 0)
            {
                Debug.LogWarning("[World] No wall segment is flagged as exposed; the night will resolve against \"" + _order[0] + "\".");
            }
        }

        private WallSegmentState ResolveState(GameState state, string id)
        {
            WallSegmentState segmentState = null;
            if (state != null)
            {
                try
                {
                    segmentState = state.Segment(id);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[World] GameState.Segment(\"" + id + "\") failed: " + exception.Message);
                }
            }

            if (segmentState == null)
            {
                segmentState = new WallSegmentState();
                segmentState.id = id;
                segmentState.stage = 0;
                segmentState.workInStage = 0;
                segmentState.damaged = false;
                if (state != null && state.segments != null)
                {
                    state.segments.Add(segmentState);
                }
            }

            segmentState.stage = Mathf.Clamp(segmentState.stage, 0, StagesPerSegment);
            return segmentState;
        }

        private void CreateVisual(SegmentRuntime segment, Transform parent)
        {
            int centerX = segment.Def.grid_x;
            int rowY = _tilemap != null ? _tilemap.WallRowY : 0;

            GameObject segmentObject = new GameObject("Wall_" + segment.Def.id);
            segmentObject.transform.SetParent(parent, false);
            segmentObject.transform.position = _tilemap != null
                ? _tilemap.CellToWorldCenter(centerX, rowY)
                : new Vector3(centerX + 0.5f, rowY + 0.5f, 0f);

            int half = SegmentWidthInCells / 2;
            for (int offset = -half; offset <= half; offset++)
            {
                int cellX = centerX + offset;
                if (_tilemap != null && !_tilemap.InBounds(cellX, rowY))
                {
                    continue;
                }

                GameObject cellObject = new GameObject("Cell_" + cellX);
                cellObject.transform.SetParent(segmentObject.transform, false);
                cellObject.transform.position = _tilemap != null
                    ? _tilemap.CellToWorldCenter(cellX, rowY)
                    : new Vector3(cellX + 0.5f, rowY + 0.5f, 0f);

                SpriteRenderer renderer = cellObject.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = WorldRuntime.SortingOrderForCell(_tilemap != null ? _tilemap.Height : 0, rowY);
                segment.Renderers.Add(renderer);

                if (_tilemap != null)
                {
                    _tilemap.SetWalkable(cellX, rowY, false);
                }
            }

            WallSegmentInteractable interactable = segmentObject.AddComponent<WallSegmentInteractable>();
            Vector2Int approachCell = new Vector2Int(centerX, rowY);
            interactable.Configure(this, segment.Def.id);
            interactable.Place(_tilemap, approachCell, false, 0f);
            segment.Interactable = interactable;
        }

        private void UpdateVisual(SegmentRuntime segment)
        {
            int stage = Mathf.Clamp(segment.State.stage, 0, StagesPerSegment);
            Sprite sprite = WorldRuntime.GetSprite("wall_" + stage);
            Color tint = segment.State.damaged
                ? new Color(0.72f, 0.66f, 0.60f, 1f)
                : Color.white;

            for (int i = 0; i < segment.Renderers.Count; i++)
            {
                SpriteRenderer renderer = segment.Renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (sprite != null)
                {
                    renderer.sprite = sprite;
                    renderer.color = tint;
                    renderer.enabled = true;
                }
                else
                {
                    // Fallback bar that grows with the stage, so progress stays readable without art.
                    float shade = 0.28f + 0.14f * stage;
                    renderer.sprite = WorldRuntime.SolidSprite(new Color(shade, shade * 0.92f, shade * 0.80f, 1f));
                    renderer.color = tint;
                    renderer.enabled = stage > 0;
                    renderer.transform.localScale = new Vector3(1f, Mathf.Max(0.2f, 0.25f * stage), 1f);
                }
            }
        }

        public bool Contains(string id)
        {
            return !string.IsNullOrEmpty(id) && _segments.ContainsKey(id);
        }

        public int StageOf(string id)
        {
            SegmentRuntime segment;
            return _segments.TryGetValue(id ?? string.Empty, out segment) ? segment.State.stage : 0;
        }

        public bool IsComplete(string id)
        {
            return StageOf(id) >= StagesPerSegment;
        }

        public bool IsExposed(string id)
        {
            SegmentRuntime segment;
            return _segments.TryGetValue(id ?? string.Empty, out segment) && segment.Def != null && segment.Def.exposed;
        }

        public bool IsDamaged(string id)
        {
            SegmentRuntime segment;
            return _segments.TryGetValue(id ?? string.Empty, out segment) && segment.State.damaged;
        }

        /// <summary>Work units still needed to finish the stage currently under construction.</summary>
        public int RemainingInStage(string id)
        {
            SegmentRuntime segment;
            if (!_segments.TryGetValue(id ?? string.Empty, out segment))
            {
                return 0;
            }

            if (segment.State.stage >= StagesPerSegment)
            {
                return 0;
            }

            return Mathf.Max(0, CostOf(segment, segment.State.stage) - segment.State.workInStage);
        }

        /// <summary>Applies work units to a segment. Returns false when the segment is already complete.</summary>
        public bool ApplyWork(string id, int units)
        {
            if (units <= 0)
            {
                return false;
            }

            SegmentRuntime segment;
            if (!_segments.TryGetValue(id ?? string.Empty, out segment))
            {
                Debug.LogWarning("[World] ApplyWork called for unknown segment \"" + id + "\".");
                return false;
            }

            WallSegmentState state = segment.State;
            if (state.stage >= StagesPerSegment)
            {
                return false;
            }

            state.damaged = false;

            int remaining = units;
            bool completedNow = false;

            while (remaining > 0 && state.stage < StagesPerSegment)
            {
                int cost = CostOf(segment, state.stage);
                if (cost <= 0)
                {
                    state.stage++;
                    state.workInStage = 0;
                    RaiseStageChanged(segment.Def.id, state.stage);
                    if (state.stage >= StagesPerSegment)
                    {
                        completedNow = true;
                    }

                    continue;
                }

                int need = Mathf.Max(1, cost - state.workInStage);
                int applied = Mathf.Min(need, remaining);
                state.workInStage += applied;
                remaining -= applied;

                if (state.workInStage >= cost)
                {
                    state.stage++;
                    state.workInStage = 0;
                    RaiseStageChanged(segment.Def.id, state.stage);
                    if (state.stage >= StagesPerSegment)
                    {
                        completedNow = true;
                    }
                }
            }

            UpdateVisual(segment);

            if (completedNow)
            {
                RaiseCompleted(segment.Def.id);
            }

            WorldRuntime.SaveNow();
            return true;
        }

        /// <summary>
        /// Night damage. Clears only the work accumulated inside the current stage; a stage that is
        /// already finished can never be taken away.
        /// </summary>
        public void DamageSegment(string id)
        {
            SegmentRuntime segment;
            if (!_segments.TryGetValue(id ?? string.Empty, out segment))
            {
                Debug.LogWarning("[World] DamageSegment called for unknown segment \"" + id + "\".");
                return;
            }

            WallSegmentState state = segment.State;
            state.workInStage = 0;
            state.damaged = true;

            UpdateVisual(segment);

            Action<string> handler = SegmentDamaged;
            if (handler != null)
            {
                handler(segment.Def.id);
            }

            WorldRuntime.SaveNow();
        }

        private int CostOf(SegmentRuntime segment, int stage)
        {
            int[] costs = segment.Def != null ? segment.Def.stage_cost : null;
            if (costs != null && stage >= 0 && stage < costs.Length)
            {
                return costs[stage];
            }

            if (stage >= 0 && stage < DefaultStageCost.Length)
            {
                return DefaultStageCost[stage];
            }

            return 4;
        }

        private void RaiseStageChanged(string id, int stage)
        {
            Action<string, int> handler = SegmentStageChanged;
            if (handler != null)
            {
                handler(id, stage);
            }
        }

        private void RaiseCompleted(string id)
        {
            Action<string> handler = SegmentCompleted;
            if (handler != null)
            {
                handler(id);
            }

            GameState state = WorldRuntime.State;
            int day = state != null ? state.day : 0;

            try
            {
                Dictionary<string, object> props = new Dictionary<string, object>();
                props["segment"] = id;
                props["day"] = day;
                Telemetry.Track(TelemetryEvents.NodeCompleted, props);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Telemetry for node_completed failed: " + exception.Message);
            }
        }
    }

    /// <summary>
    /// The tappable wall segment. Turns rubble plus daily work capacity into wall stages.
    /// Working the exposed segment scores the zealot vocation once per day, silently.
    /// </summary>
    public sealed class WallSegmentInteractable : InteractableBase
    {
        private const string ExposedWorkDayKey = "zealot_exposed_work_last_day";

        private WallSystem _wall;

        public string SegmentId { get; private set; }

        public void Configure(WallSystem wall, string segmentId)
        {
            _wall = wall;
            SegmentId = segmentId;
            DisplayName = Loc.T("world.wall_segment");
        }

        public override bool IsAvailable
        {
            get { return _wall != null && !_wall.IsComplete(SegmentId); }
        }

        public override void Interact()
        {
            if (_wall == null)
            {
                Debug.LogWarning("[World] Wall segment interactable has no WallSystem.");
                return;
            }

            if (_wall.IsComplete(SegmentId))
            {
                Debug.Log("[World] Segment \"" + SegmentId + "\" is already complete.");
                return;
            }

            ResourceSystem resources = ResourceSystem.Find();
            if (resources == null)
            {
                Debug.LogWarning("[World] No ResourceSystem in the scene; work cannot be applied.");
                return;
            }

            int remaining = _wall.RemainingInStage(SegmentId);
            int affordable = Mathf.Min(resources.Capacity, resources.Rubble);
            int units = Mathf.Min(remaining, affordable);

            if (units <= 0)
            {
                if (resources.Rubble <= 0)
                {
                    Debug.Log("[World] No rubble left to lay on \"" + SegmentId + "\".");
                }
                else if (resources.Capacity <= 0)
                {
                    Debug.Log("[World] Daily work capacity is spent; the segment waits for tomorrow.");
                }

                return;
            }

            if (!resources.Spend(units))
            {
                return;
            }

            if (!resources.TryConsumeRubble(units))
            {
                resources.AddCapacity(units);
                return;
            }

            _wall.ApplyWork(SegmentId, units);

            if (_wall.IsExposed(SegmentId))
            {
                GameState state = WorldRuntime.State;
                if (state != null && state.Counter(ExposedWorkDayKey) != state.day)
                {
                    state.counters[ExposedWorkDayKey] = state.day;
                    WorldRuntime.AddVocation(WorldRuntime.VocationZealot, 2);
                }
            }
        }
    }
}
