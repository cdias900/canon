using System;
using UnityEngine;
using SheepGate.Core;

namespace SheepGate.World
{
    /// <summary>
    /// The two things the player spends: daily work capacity, which resets every morning, and
    /// rubble, which is the material the wall is made of. Both live in <see cref="GameState"/>, so
    /// this class is a thin, event-raising facade over the single source of truth.
    ///
    /// <see cref="Spend"/> takes work capacity only. Rubble is consumed separately with
    /// <see cref="TryConsumeRubble"/>, because some day-two outcomes cost a whole day of capacity
    /// without touching the material at all.
    /// </summary>
    public class ResourceSystem : MonoBehaviour
    {
        public const int FallbackCapacityMax = 12;
        private const string CapacityDayKey = "capacity_initialized_day";

        /// <summary>Raised whenever capacity or rubble changes, and once when the scene settles.</summary>
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

        public int Rubble
        {
            get
            {
                GameState state = WorldRuntime.State;
                return state != null ? Mathf.Max(0, state.rubble) : 0;
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

        public void AddRubble(int n)
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

            state.rubble = Mathf.Max(0, state.rubble + n);
            RaiseChanged();
        }

        public bool HasRubble(int n)
        {
            return n >= 0 && Rubble >= n;
        }

        /// <summary>Consumes material. Returns false when there is not enough rubble.</summary>
        public bool TryConsumeRubble(int n)
        {
            if (n <= 0)
            {
                return n == 0;
            }

            GameState state = WorldRuntime.State;
            if (state == null || state.rubble < n)
            {
                return false;
            }

            state.rubble -= n;
            RaiseChanged();
            return true;
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
