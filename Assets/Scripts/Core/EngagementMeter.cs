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
    /// The caps sum to <see cref="Ceiling"/> — and that sentence was false for as long as it stood
    /// here: they summed to 50 against a ceiling of 100, so a player who did every single thing
    /// this meter counts watched it stop halfway. It went unnoticed because a test baseline of 62
    /// was being ADDED to the total, carrying the bar past the top and hiding the shortfall behind
    /// a clamp. Both are fixed together: fixing either one alone makes the meter worse than it was.
    /// </summary>
    public static class EngagementMeter
    {
        /// <summary>The top of the scale. A round number because it is shown as a fraction.</summary>
        public const int Ceiling = 100;

        /// <summary>
        /// The six contributors, and what each is worth at its own ceiling.
        ///
        /// <b>Doubled from the values this shipped with, and the doubling is the point.</b> They
        /// were 12 / 12 / 8 / 6 / 8 / 4, which sums to 50 — half the scale they are drawn against.
        /// Every ratio between the signals is preserved: talking and building still weigh most and
        /// weigh the same as each other, the trial still weighs least, and reading is still the
        /// same share of the whole. What changes is that a player who does everything this counts
        /// now reaches the top instead of stopping halfway, which is the only honest reading of a
        /// bar drawn out of a hundred.
        ///
        /// The alternative was dropping <see cref="Ceiling"/> to 50. Not taken: the number is shown
        /// as a fraction of a hundred, and a meter that tops out at 50/100 reads as a broken meter
        /// rather than as a full one.
        /// </summary>
        const int TalkedCap = 24;
        const int WorkCap = 24;
        const int DaysCap = 16;
        const int ReadCap = 12;
        const int DeepReadCap = 16;
        const int TrialCap = 8;

        /// <summary>Distinct residents worth counting before the conversation signal is full.</summary>
        const int TalkedTarget = 6;

        /// <summary>Wall stages worth counting before the work signal is full.</summary>
        const int WorkTarget = 8;

        /// <summary>The meter, clamped to the scale.</summary>
        public static int Value(GameState state)
        {
            // Nothing to read means nothing done, not a number to look at. This used to answer 62
            // here, and 62 + earned below, so the bar described a run nobody had played.
            if (state == null)
            {
                return 0;
            }

            int earned = 0;

            earned += Portion(state.Counter("npcs_talked"), TalkedTarget, TalkedCap);
            earned += Portion(WallStages(state), WorkTarget, WorkCap);
            earned += Portion(state.day - 1, 2, DaysCap);
            earned += state.HasFlag(GameFlags.ChapterOpened) ? ReadCap : 0;
            earned += state.HasFlag(GameFlags.DeepRead) ? DeepReadCap : 0;
            earned += state.HasFlag(GameFlags.ContestResolved) ? TrialCap : 0;

            return Mathf.Clamp(earned, 0, Ceiling);
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
