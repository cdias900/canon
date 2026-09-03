using System;
using UnityEngine;
using SheepGate.Core;

namespace SheepGate.World
{
    /// <summary>
    /// What a run spends, and there are two kinds of it that must not be confused:
    ///
    /// <b>Work capacity</b> is the daily action budget. It resets every morning and it buys the act
    /// of laying a course, never the material in it. <see cref="Spend"/> takes capacity only,
    /// because some day-two outcomes cost a whole day of it without touching material at all.
    ///
    /// <b>Materials</b> are stone, timber, and the blocks made out of them:
    /// <c>stone + timber -> block -> wall</c>, at <see cref="StonePerBlock"/> stone and
    /// <see cref="TimberPerBlock"/> timber each. Day 1 hands out stone only and timber appears on
    /// day 2, but nothing here gates a material by day: this class holds counts, and the pacing
    /// belongs to whatever spawns material and draws the screens.
    ///
    /// Everything is stored in <see cref="GameState"/>, so this class is a thin, event-raising
    /// facade over the single source of truth rather than a second copy of it.
    ///
    /// The four Rubble members near the bottom are compatibility shims over stone; read their note
    /// before touching them.
    /// </summary>
    public class ResourceSystem : MonoBehaviour
    {
        public const int FallbackCapacityMax = GameState.DefaultWorkCapacityMax;
        private const string CapacityDayKey = "capacity_initialized_day";

        /// <summary>
        /// Stone in one block. The recipe lives in these two constants so that no screen, spawner
        /// or piece of dialogue logic spells the numbers by hand and drifts from the others.
        /// </summary>
        public const int StonePerBlock = 3;

        /// <summary>Timber in one block. See <see cref="StonePerBlock"/>.</summary>
        public const int TimberPerBlock = 2;

        /// <summary>
        /// Raised whenever capacity or any material changes, and once when the scene settles.
        /// Screens redraw off it, so every mutation in this class raises it - once, and only after
        /// the whole mutation has settled.
        /// </summary>
        public event Action Changed;

        private bool _firstFrameNotified;

        public static ResourceSystem Find()
        {
            ResourceSystem system = null;
            try
            {
                ServiceLocator.TryGet(out system);
            }
            catch (Exception)
            {
                system = null;
            }

            if (system == null)
            {
                system = FindFirstObjectByType<ResourceSystem>();
            }

            return system;
        }

        /// <summary>Stone the player is carrying.</summary>
        public int Stone
        {
            get
            {
                GameState state = WorldRuntime.State;
                return state != null ? Mathf.Max(0, state.stone) : 0;
            }
        }

        /// <summary>Timber the player is carrying. Zero all through day 1, by design.</summary>
        public int Timber
        {
            get
            {
                GameState state = WorldRuntime.State;
                return state != null ? Mathf.Max(0, state.timber) : 0;
            }
        }

        /// <summary>Blocks ready to be laid.</summary>
        public int Blocks
        {
            get
            {
                GameState state = WorldRuntime.State;
                return state != null ? Mathf.Max(0, state.blocks) : 0;
            }
        }

        public int Capacity
        {
            get
            {
                GameState state = WorldRuntime.State;
                return state != null ? Mathf.Max(0, state.workCapacity) : 0;
            }
        }

        public int CapacityMax
        {
            get
            {
                GameState state = WorldRuntime.State;
                if (state == null || state.workCapacityMax <= 0)
                {
                    return FallbackCapacityMax;
                }

                return state.workCapacityMax;
            }
        }

        private void Awake()
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            // A day that has never handed out its capacity gets it now. Reloading in the middle of
            // a day keeps whatever the player had left.
            if (state.Counter(CapacityDayKey) != state.day)
            {
                state.counters[CapacityDayKey] = state.day;
                state.workCapacity = CapacityMax;
            }
        }

        private void Start()
        {
            RaiseChanged();
        }

        private void Update()
        {
            if (_firstFrameNotified)
            {
                enabled = false;
                return;
            }

            // Subscribers created in the same composition pass may have attached after Start.
            _firstFrameNotified = true;
            RaiseChanged();
        }

        public bool CanSpend(int units)
        {
            return units >= 0 && Capacity >= units;
        }

        /// <summary>Spends daily work capacity. Returns false when there is not enough left.</summary>
        public bool Spend(int units)
        {
            if (units <= 0)
            {
                return units == 0;
            }

            GameState state = WorldRuntime.State;
            if (state == null || state.workCapacity < units)
            {
                return false;
            }

            state.workCapacity -= units;
            RaiseChanged();
            return true;
        }

        /// <summary>Gives capacity back, used when a spend could not be completed.</summary>
        public void AddCapacity(int units)
        {
            if (units <= 0)
            {
                return;
            }

            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            state.workCapacity = Mathf.Clamp(state.workCapacity + units, 0, CapacityMax);
            RaiseChanged();
        }

        public void ResetDailyCapacity()
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            state.workCapacity = CapacityMax;
            state.counters[CapacityDayKey] = state.day;
            RaiseChanged();
        }

        // ------------------------------------------------------------------ materials

        /// <summary>
        /// Adds stone. A negative amount takes stone away - that is how a donation to a resident is
        /// paid - and the total never falls below zero.
        /// </summary>
        public void AddStone(int n)
        {
            if (n == 0)
            {
                return;
            }

            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            state.stone = Mathf.Max(0, state.stone + n);
            RaiseChanged();
        }

        public bool HasStone(int n)
        {
            return n >= 0 && Stone >= n;
        }

        /// <summary>Consumes stone. Returns false, changing nothing, when there is not enough.</summary>
        public bool TryConsumeStone(int n)
        {
            if (n <= 0)
            {
                return n == 0;
            }

            GameState state = WorldRuntime.State;
            if (state == null || state.stone < n)
            {
                return false;
            }

            state.stone -= n;
            RaiseChanged();
            return true;
        }

        /// <summary>Adds timber. A negative amount takes it away; the total never falls below zero.</summary>
        public void AddTimber(int n)
        {
            if (n == 0)
            {
                return;
            }

            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            state.timber = Mathf.Max(0, state.timber + n);
            RaiseChanged();
        }

        public bool HasTimber(int n)
        {
            return n >= 0 && Timber >= n;
        }

        /// <summary>Consumes timber. Returns false, changing nothing, when there is not enough.</summary>
        public bool TryConsumeTimber(int n)
        {
            if (n <= 0)
            {
                return n == 0;
            }

            GameState state = WorldRuntime.State;
            if (state == null || state.timber < n)
            {
                return false;
            }

            state.timber -= n;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// Adds finished blocks without charging for them. Crafting goes through
        /// <see cref="TryCraftBlock"/>; this is for blocks that arrive some other way, such as a
        /// neighbour's help. A negative amount takes them away, never below zero.
        /// </summary>
        public void AddBlocks(int n)
        {
            if (n == 0)
            {
                return;
            }

            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            state.blocks = Mathf.Max(0, state.blocks + n);
            RaiseChanged();
        }

        public bool HasBlocks(int n)
        {
            return n >= 0 && Blocks >= n;
        }

        /// <summary>Consumes blocks. Returns false, changing nothing, when there are not enough.</summary>
        public bool TryConsumeBlocks(int n)
        {
            if (n <= 0)
            {
                return n == 0;
            }

            GameState state = WorldRuntime.State;
            if (state == null || state.blocks < n)
            {
                return false;
            }

            state.blocks -= n;
            RaiseChanged();
            return true;
        }

        /// <summary>True when there is material on hand for at least one block.</summary>
        public bool CanCraftBlock()
        {
            return Stone >= StonePerBlock && Timber >= TimberPerBlock;
        }

        /// <summary>
        /// Turns stone and timber into blocks, all or nothing: either the whole batch is paid for
        /// and delivered, or nothing moves at all. Returns false when the material is short, and in
        /// that case no count has changed and no event has been raised.
        ///
        /// <see cref="Changed"/> fires exactly once, after every count has settled. Raising it
        /// between the debit and the credit would show a listener - and the listener redraws the
        /// screen - a moment where the stone is gone and the block does not exist yet.
        /// </summary>
        public bool TryCraftBlock(int count = 1)
        {
            if (count <= 0)
            {
                return count == 0;
            }

            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return false;
            }

            // Widened on purpose: a nonsense count must not multiply into a wrapped, affordable
            // price. The comparisons below promote to long, so an overflowed total cannot pass.
            long stoneNeeded = (long)StonePerBlock * count;
            long timberNeeded = (long)TimberPerBlock * count;

            if (Stone < stoneNeeded || Timber < timberNeeded)
            {
                return false;
            }

            state.stone -= (int)stoneNeeded;
            state.timber -= (int)timberNeeded;
            state.blocks = Mathf.Max(0, state.blocks + count);
            RaiseChanged();
            return true;
        }

        // ------------------------------------------------------------------ compatibility shims
        //
        // Stone used to be called rubble, and the four members below are the old spelling kept
        // alive. They exist because screens being restyled in another workflow still call them.
        // Each one forwards to its stone counterpart and holds nothing of its own, so the two
        // spellings cannot drift. Delete them once every caller says stone - WallSystem's segment
        // interactable, NpcActor's donation, RubblePile's pickup and the HUD readout are the ones
        // to check.
        //
        // Deliberately not marked [Obsolete]: nothing here builds warnings as errors, so it would
        // compile, but a fresh warning inside files that other agents are editing right now reads
        // to them as a defect in their own change, and gets "fixed".

        /// <summary>Shim: stone under its old name. See the note above.</summary>
        public int Rubble
        {
            get { return Stone; }
        }

        /// <summary>
        /// Shim: <see cref="AddStone"/> under the old name. Negative amounts have to keep working -
        /// NpcActor pays a donation with AddRubble(-cost).
        /// </summary>
        public void AddRubble(int n)
        {
            AddStone(n);
        }

        /// <summary>Shim: <see cref="HasStone"/> under the old name.</summary>
        public bool HasRubble(int n)
        {
            return HasStone(n);
        }

        /// <summary>Shim: <see cref="TryConsumeStone"/> under the old name.</summary>
        public bool TryConsumeRubble(int n)
        {
            return TryConsumeStone(n);
        }

        /// <summary>Re-raises <see cref="Changed"/>, for listeners that attach late.</summary>
        public void NotifyChanged()
        {
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            Action handler = Changed;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] A ResourceSystem.Changed listener threw: " + exception.Message);
            }
        }
    }
}
