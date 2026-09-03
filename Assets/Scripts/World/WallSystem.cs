using System;
using System.Collections.Generic;
using UnityEngine;
using SheepGate.Core;
using SheepGate.UI;

namespace SheepGate.World
{
    /// <summary>
    /// Owns wall progress. Four stages per segment, each stage costing the work units declared in
    /// wall_segments.json. Work accumulates inside the current stage and rolls over into the next.
    ///
    /// A work unit is one block and one point of daily capacity - see
    /// <see cref="WallSegmentInteractable"/>, which is where the paying happens. This class counts
    /// work and knows nothing about what it was bought with.
    ///
    /// Hard product rule: a completed stage NEVER regresses. <see cref="DamageSegment"/> only
    /// clears the work in progress inside the current, unfinished stage.
    /// </summary>
    public class WallSystem : MonoBehaviour
    {
        public const int StagesPerSegment = 4;
        public const int SegmentWidthInCells = 3;

        /// <summary>
        /// Used only for a stage wall_segments.json does not price. Kept equal to what that file
        /// carries, so a missing entry costs what its neighbours cost instead of silently pricing
        /// one stage at the old, pre-block figure.
        /// </summary>
        private static readonly int[] DefaultStageCost = { 1, 1, 1, 1 };

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
                fallback.stage_cost = new int[] { 1, 1, 1, 1 };
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
            SheepGate.Audio.AudioDirector.Play(SheepGate.Audio.AudioKeys.Stone);

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
    /// The tappable wall segment, and the place where material becomes wall. One work unit costs
    /// one point of daily capacity and one block; the stage prices come from wall_segments.json.
    ///
    /// <b>Blocks are made here</b>, at the foot of the wall, out of the stone and timber the player
    /// is carrying (<see cref="ResourceSystem.TryCraftBlock"/>). It is the smallest crafting step
    /// the village can hold today - there is no yard, no bench and no screen to put one on - and it
    /// means someone holding material never taps a wall that does nothing. Blocks already on hand
    /// are always spent before any are made, so a dedicated mixing yard can take the job later
    /// without a line changing here.
    ///
    /// <b>Until timber is in the village, the wall takes dry stone</b>, a block's worth of it per
    /// course (<see cref="ResourceSystem.StonePerBlock"/>). That is the day-1 pacing rule: the
    /// recipe is introduced one half at a time, so the first day's course is laid dry. It used to
    /// take stone one for one, which left eleven stones in the player's hands at the end of a
    /// four-course day and carried them into every day after; a course is three stones whether or
    /// not there is a beam between them. When timber arrives is
    /// <see cref="RubblePile.TimberFirstDay"/> and it is read from there rather than spelled again
    /// here, so re-timing the material re-times the wall with it, in one edit instead of two.
    ///
    /// The switch also trips on the material itself - a block in hand, or timber enough to make one
    /// - so a build that hands timber over early needs nothing changed here. And it never switches
    /// back: reading the timber count live would drop the wall onto the cheaper stone course the
    /// moment the player spent their last beam, which would pay them for running out.
    ///
    /// Working the exposed segment scores the zealot vocation once per day, silently.
    /// </summary>
    public sealed class WallSegmentInteractable : InteractableBase
    {
        private const string ExposedWorkDayKey = "zealot_exposed_work_last_day";

        // The three reasons a tap on the wall does nothing, as locale keys. Named "...Key" because
        // the content validator reads a const string named like player text as a hardcoded
        // sentence, and that suffix is the escape hatch it offers.
        private const string NoStoneKey = "toast.wall.no_stone";
        private const string NoBlocksKey = "toast.wall.no_blocks";
        private const string NoCapacityKey = "toast.wall.no_capacity";
        private const string LastUnitKey = "toast.wall.last_unit";

        /// <summary>
        /// Latched the first time the wall is asked for blocks. From then on the wall is raised out
        /// of blocks and the dry stone course underneath is over, whatever the player is carrying.
        /// </summary>
        private const string BlockEconomyKey = "block_economy_started";

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

            bool blocksRequired = BlocksRequired(resources);

            // Blocks in hand plus the ones the stone and timber in hand could still be made into.
            // Counting the uncrafted ones here is what makes one tap enough: the material the player
            // is carrying is the material they can spend.
            int blocksOnHand = resources.Blocks;
            int material = blocksRequired
                ? blocksOnHand + CraftableBlocks(resources)
                : resources.Stone / ResourceSystem.StonePerBlock;

            int remaining = _wall.RemainingInStage(SegmentId);
            int affordable = Mathf.Min(resources.Capacity, material);
            int units = Mathf.Min(remaining, affordable);

            if (units <= 0)
            {
                // Said on screen, not only in the log. This is the central verb of the game and it
                // used to refuse in silence, which is indistinguishable from a tap that missed.
                // The order matters: material first, because a player with no stone and no capacity
                // is short of both and the one they can do something about tonight is the pile.
                if (material <= 0)
                {
                    Toast.Show(Loc.T(blocksRequired ? NoBlocksKey : NoStoneKey));

                    Debug.Log(blocksRequired
                        ? "[World] No blocks, and not enough stone and timber to make one, for \"" + SegmentId + "\"."
                        : "[World] No stone left to lay on \"" + SegmentId + "\".");
                }
                else if (resources.Capacity <= 0)
                {
                    Toast.Show(Loc.T(NoCapacityKey));
                    Debug.Log("[World] Daily work capacity is spent; the segment waits for tomorrow.");
                }

                return;
            }

            if (!resources.Spend(units))
            {
                return;
            }

            if (!TakeMaterial(resources, blocksRequired, units, blocksOnHand))
            {
                // The capacity goes straight back: this tap cost the player nothing at all.
                resources.AddCapacity(units);
                return;
            }

            _wall.ApplyWork(SegmentId, units);

            // The day ends by itself the moment capacity reaches zero, with no confirmation and no
            // way back, so the last warning a player can act on is the one before it. Announced at
            // one remaining rather than at zero: at zero the split is already opening over the top
            // of the message, and there is nothing left to decide. One remaining is one course,
            // not one stone — the copy behind LastUnitKey says so.
            if (resources.Capacity == 1)
            {
                Toast.Show(Loc.T(LastUnitKey));
            }

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

        /// <summary>
        /// True once the wall is raised out of blocks rather than out of loose stone. Trips on the
        /// day timber reaches the village, or earlier if the player is somehow already holding a
        /// block or timber enough for one, and latches so it can never trip back.
        ///
        /// The day is not the only test on purpose, and neither is the material on its own: the day
        /// alone would leave a run that never picked up a beam laying cheap stone forever, which
        /// would make the whole recipe optional and worse than ignoring it; the material alone would
        /// hand the same loophole to anyone who simply walked past the timber.
        ///
        /// A single stray beam does not count - it would put the wall on a currency the player
        /// cannot afford yet - which is why the test is a whole block's worth.
        /// </summary>
        private static bool BlocksRequired(ResourceSystem resources)
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return false;
            }

            if (state.Counter(BlockEconomyKey) != 0)
            {
                return true;
            }

            bool timberInTheVillage = state.day >= RubblePile.TimberFirstDay;
            bool materialInHand = resources.Blocks > 0 || resources.Timber >= ResourceSystem.TimberPerBlock;
            if (!timberInTheVillage && !materialInHand)
            {
                return false;
            }

            state.counters[BlockEconomyKey] = 1;
            Debug.Log("[World] Timber is in the village; the wall is raised out of blocks from here on.");
            return true;
        }

        /// <summary>Blocks the stone and timber in hand could still be made into.</summary>
        private static int CraftableBlocks(ResourceSystem resources)
        {
            int fromStone = resources.Stone / ResourceSystem.StonePerBlock;
            int fromTimber = resources.Timber / ResourceSystem.TimberPerBlock;
            return Mathf.Max(0, Mathf.Min(fromStone, fromTimber));
        }

        /// <summary>
        /// Takes the material for <paramref name="units"/> work units. Blocks already on hand go
        /// first and only the shortfall is crafted, so a stockpile is never bypassed in favour of
        /// raw material. Returns false when the material could not be taken, having left nothing
        /// half-spent: crafting is all or nothing, and a block that was made and not laid is still
        /// a block the player owns.
        /// </summary>
        private bool TakeMaterial(ResourceSystem resources, bool blocksRequired, int units, int blocksOnHand)
        {
            if (!blocksRequired)
            {
                return resources.TryConsumeStone(units * ResourceSystem.StonePerBlock);
            }

            int shortfall = units - blocksOnHand;
            if (shortfall > 0 && !resources.TryCraftBlock(shortfall))
            {
                // Unreachable: the affordability above already counted what is craftable. Guarded
                // anyway, because a wall raised out of blocks nobody paid for is the kind of bug
                // that is invisible instead of broken.
                Debug.LogWarning("[World] Could not craft " + shortfall + " block(s) for \"" + SegmentId + "\".");
                return false;
            }

            return resources.TryConsumeBlocks(units);
        }
    }
}
