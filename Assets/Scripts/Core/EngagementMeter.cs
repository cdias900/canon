using UnityEngine;

namespace SheepGate.Core
{
    /// <summary>
    /// How far into the game the player has come, as one number between zero and
    /// <see cref="Ceiling"/>. The profile tab draws it; nothing else reads it and nothing spends it.
    ///
    /// <b>What this is not.</b> It is not vocation. Rule 10 forbids showing progress toward a
    /// vocation, and that rule stands: this number is deliberately built out of signals that no
    /// vocation scores, it never names an archetype, and no branch anywhere reads it. A player who
    /// watched it all day would learn how much of the valley they had touched and nothing about who
    /// the game thinks they are.
    ///
    /// <b>The tension worth stating.</b> <c>AGENTS.md</c> rule 19 says reading pays in
    /// understanding and never in a number, and rule 20 explains why: if reading paid, everyone
    /// would "read" and <c>deep_read</c> would stop measuring anything. Reading moves this meter, so
    /// the design leans on three things to keep the rule's intent. Reading is <b>one contributor
    /// among six</b> and cannot fill the bar on its own. The meter <b>buys nothing</b> — no item, no
    /// capacity, no shortcut, so there is no reward to farm. And every other contributor is
    /// something the player was going to do anyway, so the honest description of the bar is "how
    /// much of this you have done", not "how much you have read".
    ///
    /// Each signal has its own cap, which is what stops any one of them from filling the bar alone.
    /// The caps sum to <see cref="Ceiling"/>.
    /// </summary>
    public static class EngagementMeter
    {
        /// <summary>The top of the scale. A round number because it is shown as a fraction.</summary>
        public const int Ceiling = 100;

        /// <summary>
        /// Where the meter starts before the run has done anything.
        ///
        /// <b>This is a test value and it is a lie.</b> It exists so the bar can be looked at with
        /// something in it while the screen is being built, which was asked for explicitly. It has
        /// to go to zero before this ships: a meter that starts most of the way full tells a new
        /// player they have already done most of something, which is both untrue and the exact
        /// shape of a progress bar nobody trusts.
        /// </summary>
        public const int TestBaseline = 62;

        // The six contributors, and what each one is worth at its own ceiling.
        const int TalkedCap = 12;
        const int WorkCap = 12;
        const int DaysCap = 8;
        const int ReadCap = 6;
        const int DeepReadCap = 8;
        const int TrialCap = 4;

        /// <summary>Distinct residents worth counting before the conversation signal is full.</summary>
        const int TalkedTarget = 6;

        /// <summary>Wall stages worth counting before the work signal is full.</summary>
        const int WorkTarget = 8;

        /// <summary>The meter, clamped to the scale.</summary>
        public static int Value(GameState state)
        {
            if (state == null)
            {
                return Mathf.Clamp(TestBaseline, 0, Ceiling);
            }

            int earned = 0;

            earned += Portion(state.Counter("npcs_talked"), TalkedTarget, TalkedCap);
            earned += Portion(WallStages(state), WorkTarget, WorkCap);
            earned += Portion(state.day - 1, 2, DaysCap);
            earned += state.HasFlag(GameFlags.ChapterOpened) ? ReadCap : 0;
            earned += state.HasFlag(GameFlags.DeepRead) ? DeepReadCap : 0;
            earned += state.HasFlag(GameFlags.ContestResolved) ? TrialCap : 0;

            return Mathf.Clamp(TestBaseline + earned, 0, Ceiling);
        }

        /// <summary>One signal's share, straight-line to its own cap and never past it.</summary>
        static int Portion(int reached, int target, int cap)
        {
            if (reached <= 0 || target <= 0)
            {
                return 0;
            }

            return Mathf.Min(cap, Mathf.RoundToInt(cap * (reached / (float)target)));
        }

        /// <summary>Stages standing on the wall, counted from the save rather than the scene.</summary>
        static int WallStages(GameState state)
        {
            if (state.segments == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < state.segments.Count; i++)
            {
                WallSegmentState segment = state.segments[i];
                if (segment != null)
                {
                    total += Mathf.Max(0, segment.stage);
                }
            }

            return total;
        }
    }
}
