using System;
using System.Globalization;
using SheepGate.Core;

namespace SheepGate.Economy
{
    /// <summary>
    /// The daily check-in: once per real calendar day, at boot, the player is paid talents. The
    /// streak that decides the payout tier resets on any gap greater than one day; the talents
    /// already paid out never do — see the design doc's note on rule 7 for why that split is the
    /// deliberate boundary rather than an oversight.
    /// </summary>
    public static class DailyCheckIn
    {
        public const string DateFormat = "yyyy-MM-dd";

        /// <summary>Streak at or above which a check-in pays the higher tier.</summary>
        const int EscalationStreak = 4;

        const int BaseTalents = 1;
        const int EscalatedTalents = 3;

        /// <summary>One check-in's outcome. `Awarded` is false when today was already paid.</summary>
        public struct Result
        {
            public bool Awarded;
            public int Streak;
            public int TalentsAwarded;
        }

        /// <summary>
        /// Set by <see cref="SheepGate.Core.BootSequence"/> right after a boot that paid a reward,
        /// and cleared by whatever shows the toast for it. In-memory only — never serialized, so a
        /// reward can never replay itself from a save written mid-toast.
        /// </summary>
        public static Result? PendingResult;

        /// <summary>
        /// Applies today's check-in to <paramref name="state"/>, mutating it when a reward is due.
        /// Safe to call more than once for the same day: every call after the first for that date
        /// is a no-op that returns <c>Awarded = false</c>.
        /// </summary>
        public static Result Apply(GameState state, DateTime today)
        {
            string todayKey = today.ToString(DateFormat, CultureInfo.InvariantCulture);
            if (state.lastCheckInDate == todayKey)
            {
                return new Result { Awarded = false, Streak = state.checkInStreak, TalentsAwarded = 0 };
            }

            bool consecutive = IsNextCalendarDay(state.lastCheckInDate, today);
            state.checkInStreak = consecutive ? state.checkInStreak + 1 : 1;

            int talents = state.checkInStreak >= EscalationStreak ? EscalatedTalents : BaseTalents;
            state.talents += talents;
            state.lastCheckInDate = todayKey;

            return new Result { Awarded = true, Streak = state.checkInStreak, TalentsAwarded = talents };
        }

        /// <summary>True when today is exactly one calendar day after the stored date.</summary>
        static bool IsNextCalendarDay(string lastCheckInDate, DateTime today)
        {
            if (string.IsNullOrEmpty(lastCheckInDate))
            {
                return false;
            }

            DateTime last;
            if (!DateTime.TryParseExact(lastCheckInDate, DateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out last))
            {
                return false;
            }

            return today.Date == last.Date.AddDays(1);
        }
    }
}
