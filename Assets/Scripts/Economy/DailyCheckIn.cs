using System;
using System.Globalization;
using SheepGate.Core;

namespace SheepGate.Economy
{
    /// <summary>
    /// The first launch of a calendar day, and how long the player was away.
    ///
    /// This was a streak: a coin button on the HUD, a talent a day, three after four days running,
    /// and a card saying "come back tomorrow for more". Every part of that pulled against the
    /// game. Rule 7 says absence delays and never regresses, and a streak that resets is guilt
    /// with a counter on it; MVP-SCOPE §12 put a daily streak and spendable talents out of scope
    /// on the same line; and the talents bought nothing, which made the reward a number that
    /// pointed at a shop with the door locked.
    ///
    /// What stays is the fact underneath: the game notices when a real day has passed, once per
    /// day, and says so with grace rather than with a debt. The date is still kept in the save's
    /// <c>lastCheckInDate</c>, in the same format, so a save from the streak build reads straight
    /// through; the streak count and the talents it paid stay in the save unread.
    /// </summary>
    public static class DailyCheckIn
    {
        public const string DateFormat = "yyyy-MM-dd";

        /// <summary>One day's first launch. <c>First</c> is false for every launch after it that day.</summary>
        public struct Result
        {
            /// <summary>True on the first launch of this calendar day.</summary>
            public bool First;

            /// <summary>
            /// Whole days since the last recorded launch. Zero on the very first launch of the
            /// run, when there is nothing to have been away from, and zero when today was already
            /// recorded.
            /// </summary>
            public int DaysAway;
        }

        /// <summary>True when today has not been recorded yet.</summary>
        public static bool IsAvailable(GameState state, DateTime today)
        {
            return state != null && state.lastCheckInDate != today.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Records today and says how long the player was away. Safe to call more than once for
        /// the same day: every call after the first for that date is a no-op that answers
        /// <c>First = false</c>. Nothing is paid and nothing is reset.
        /// </summary>
        public static Result Apply(GameState state, DateTime today)
        {
            string todayKey = today.ToString(DateFormat, CultureInfo.InvariantCulture);
            if (state == null || state.lastCheckInDate == todayKey)
            {
                return new Result { First = false, DaysAway = 0 };
            }

            int daysAway = DaysBetween(state.lastCheckInDate, today);
            state.lastCheckInDate = todayKey;

            return new Result { First = true, DaysAway = daysAway };
        }

        /// <summary>Whole days from the stored date to today; 0 when nothing was stored or it does not parse.</summary>
        static int DaysBetween(string lastCheckInDate, DateTime today)
        {
            if (string.IsNullOrEmpty(lastCheckInDate))
            {
                return 0;
            }

            DateTime last;
            if (!DateTime.TryParseExact(lastCheckInDate, DateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out last))
            {
                return 0;
            }

            int days = (int)(today.Date - last.Date).TotalDays;
            return days < 0 ? 0 : days;
        }
    }
}
