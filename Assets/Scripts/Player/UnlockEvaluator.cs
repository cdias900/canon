using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using SheepGate.Core;

namespace SheepGate.Player
{
    /// <summary>
    /// The complete vocabulary of unlock conditions. Four types, and there will not be a fifth
    /// without someone reading <see cref="UnlockEvaluator"/>'s header first.
    /// </summary>
    public enum UnlockConditionType
    {
        /// <summary>The highest stage the player has taken any wall segment to.</summary>
        WallStage,

        /// <summary>How many wall segments are finished.</summary>
        SegmentsComplete,

        /// <summary>Which day the run has reached.</summary>
        DayReached,

        /// <summary>A named discovery, from a closed list. Finding something, never doing something.</summary>
        Discovery
    }

    /// <summary>
    /// What a player has to have done for an item to be available. Authored as data, never as code:
    ///
    ///     {"type": "wall_stage", "value": 3}
    ///     {"type": "segments_complete", "value": 2}
    ///     {"type": "day_reached", "value": 2}
    ///     {"type": "discovery", "id": "intro_seen"}
    ///
    /// A null condition on an item means it is available from the first minute.
    /// </summary>
    public sealed class UnlockCondition
    {
        [JsonProperty("type")] public string type;

        /// <summary>Threshold for the three numeric types. Ignored by <c>discovery</c>.</summary>
        [JsonProperty("value")] public int value;

        /// <summary>Discovery id. Must be one of <see cref="UnlockEvaluator.DiscoveryIds"/>.</summary>
        [JsonProperty("id")] public string id;
    }

    /// <summary>
    /// Decides whether a wardrobe item is unlocked, and hands back the locale key that says why it
    /// is not. Reads <see cref="GameState"/> and nothing else; it never writes.
    ///
    /// ==================================================================================
    /// THE RULE THIS CLASS EXISTS TO ENFORCE
    /// ==================================================================================
    /// An unlock condition is printed to the player in full, under the padlock. That makes the
    /// vocabulary a broadcast channel, and CLAUDE.md rule 10 forbids ever broadcasting progress
    /// toward a vocation: a visible counter turns discovery into a task list, and naming a scored
    /// action teaches the player which behaviours are being marked.
    ///
    /// So: AN UNLOCK CONDITION MAY NEVER BE A COUNT OF AN ACTION THAT SCORES VOCATION.
    ///
    /// That is not a convention here, it is arithmetic about what the code can read. Every action
    /// tally in this game lives in <see cref="GameState.counters"/> — <c>ronda_used</c>,
    /// <c>npcs_talked</c>, <c>prophet_page_awarded</c>, <c>steward_rubble_qualified_d1</c> and the
    /// rest — and every one-shot outcome of a scored choice lives in <see cref="GameState.flags"/>.
    /// This class contains no code path that touches <c>counters</c>, and it reaches <c>flags</c>
    /// only through <see cref="DiscoveryIds"/>, a closed list declared in C#. There is no condition
    /// type that takes an arbitrary key into either dictionary, so "donate three times" is not a
    /// condition an author can write badly — it is a sentence the vocabulary cannot form. Adding a
    /// discovery id is a code change on purpose: the friction is the safety, and the deny list
    /// below is what a future author will hit first.
    ///
    /// What the four types read instead is world state the game already shows on screen: the wall
    /// and the calendar. A wall stage is reached by working any segment, by the night crew, by the
    /// force-completion the stage declaring <c>finishes_wall</c> applies, and by the closing stage
    /// shutting the gate — so it is nobody's proxy for a single scored action, and the player
    /// watches that number rise on the wall itself whether or not a wardrobe exists.
    ///
    /// ==================================================================================
    /// WHY AN UNLOCKED ITEM CAN NEVER RE-LOCK (rule 7 — never punish)
    /// ==================================================================================
    /// All four readings are monotonic, so <see cref="IsMet"/> can be a pure function and still
    /// never take something back:
    ///   * segment stage never regresses — WallSystem's own hard rule; a night without a watch
    ///     clears the work inside the current unfinished stage and nothing else;
    ///   * the count of finished segments therefore never falls;
    ///   * <see cref="GameState.day"/> NEVER RESETS FOR THE LIFE OF A SAVE. DayCycle only ever
    ///     increments it, and the schema 2 -> 3 migration only ever raises it — it maps the old
    ///     day 3 forward to the stage carrying the same beat and touches nothing else. There is no
    ///     code path anywhere that lowers it, which is why every <c>day_reached</c> item in the
    ///     catalogue is safe across a nine-stage season and across the migration into one;
    ///   * flags are only ever added — nothing in the project removes one.
    ///
    /// That day invariant is load-bearing beyond this file and is stated here because this is
    /// where it is relied on: <see cref="SheepGate.UI.BackpackPanel"/> caches each chip's locked
    /// state at build time, so a reading that could fall would put a padlock back on a garment the
    /// player has already worn. A padlock on something already earned is exactly the punishment
    /// rule 7 forbids, and it is the failure this class is shaped to make impossible rather than
    /// merely unlikely.
    ///
    /// ==================================================================================
    /// WHAT HAPPENS TO A CONDITION THIS CLASS CANNOT READ
    /// ==================================================================================
    /// It unlocks the item, loudly. An unknown type, an unknown discovery id, or an id from the
    /// deny list logs an error and returns true. Failing closed would leave a player permanently
    /// locked out of something by an authoring typo, which is the punishment rule 7 forbids, and
    /// failing open leaks nothing: an unlocked item shows no condition, so there is no sentence to
    /// read and no progress to infer. The error in the log is what gets it fixed.
    /// </summary>
    public static class UnlockEvaluator
    {
        /// <summary>
        /// Stage at which a wall segment is finished. Stated by
        /// <see cref="SheepGate.Core.WallSegmentState"/>: stage 4 means complete. Repeated as a
        /// local constant so the evaluator does not have to reach into the world layer to ask —
        /// nothing else in <c>SheepGate.Player</c> depends on <c>SheepGate.World</c>, and a
        /// wardrobe that could not be evaluated without the wall running would be worse than a
        /// duplicated number.
        ///
        /// The duplicate is now ASSERTED at first use rather than trusted. See the static
        /// constructor: the two constants disagreeing is silent and expensive — every
        /// <c>segments_complete</c> padlock in the catalogue would count the wrong thing while
        /// the wall itself drew correctly — so the one place the two numbers can be compared says
        /// so out loud instead of leaving it to a reader noticing two files at once.
        /// </summary>
        public const int CompleteStage = 4;

        /// <summary>
        /// The one line of this class that names the world layer, and it is a check rather than a
        /// use: <see cref="CompleteStage"/> is a deliberate copy of
        /// <c>SheepGate.World.WallSystem.StagesPerSegment</c>, and a copy nobody compares is a bug
        /// waiting for the day somebody re-balances the wall.
        ///
        /// Written fully qualified with no <c>using</c> added, so the dependency stays visible as
        /// the single exception it is. It runs at first touch of this class, which the wardrobe,
        /// the backpack and the map all do on an ordinary run, so it is a real call path and not a
        /// method waiting to be called.
        ///
        /// <c>Debug.LogError</c> and not <c>Debug.Assert</c>: the acceptance and e2e harnesses
        /// promote a logged error to a run failure, while an assert is stripped from a release
        /// build and would report nothing in the place it matters most.
        ///
        /// The world's number is taken through a local rather than compared const-to-const. Both
        /// sides are <c>const int</c>, so written directly the comparison would fold at compile
        /// time and the compiler would call the body unreachable — a warning on the very line
        /// whose whole job is to be reachable on the day the two disagree.
        /// </summary>
        static UnlockEvaluator()
        {
            int stagesPerSegment = SheepGate.World.WallSystem.StagesPerSegment;
            if (CompleteStage != stagesPerSegment)
            {
                Debug.LogError("[Unlock] UnlockEvaluator.CompleteStage is " + CompleteStage +
                               " but WallSystem.StagesPerSegment is " + stagesPerSegment +
                               ". They are a deliberate duplicate and must agree, or every " +
                               KeySegmentsComplete + " padlock counts a segment as finished at a " +
                               "course the wall does not. Change both.");
            }
        }

        // ------------------------------------------------------------------ locale keys
        // Four keys, not one sentence per item. An author cannot then write a padlock line that
        // disagrees with the threshold it is standing under, because the threshold is the argument.
        // Keys are what LocaleKey returns, verbatim, and are looked up in the "unlock" object of
        // Resources/Data/locales/<locale>/catalog.json.

        public const string KeyWallStage = "wall_stage";
        public const string KeySegmentsComplete = "segments_complete";
        public const string KeyDayReached = "day_reached";

        /// <summary>Prefix for a discovery's sentence: "discovery." plus the discovery id.</summary>
        public const string KeyDiscoveryPrefix = "discovery.";

        // ------------------------------------------------------------------ the discovery list

        /// <summary>
        /// The arrival, at the end of the opening. Declared here as a literal rather than borrowed
        /// from IntroCutscene's private constant — the string is the contract, the same way the
        /// world layer declares its own flag literals.
        /// </summary>
        public const string DiscoveryIntroSeen = "intro_seen";

        /// <summary>
        /// Every flag id an unlock condition may name. Nothing else resolves.
        ///
        /// An entry belongs here only if setting it grants no vocation points, and neither does the
        /// sibling branch of the same choice. That second half matters: naming a flag whose twin
        /// scores still tells the player which fork was being watched.
        ///
        /// The list is short because most of the run is scored. Before adding one, check it against
        /// <see cref="ForbiddenFlags"/>, against <see cref="WatchPostedPrefix"/>, and against a
        /// fresh grep of <c>AwardOnce</c>, <c>AddVocation</c> and <c>"grants"</c> in dialogue.json
        /// — the scoring sites move.
        /// </summary>
        public static readonly IReadOnlyList<string> DiscoveryIds = new[]
        {
            // The player has arrived in the valley and the opening has played. Sets no points, and
            // has no sibling branch: everybody arrives.
            DiscoveryIntroSeen
        };

        /// <summary>
        /// Prefix of the per-stage watch flags — <c>watch_posted_d1</c>, <c>watch_posted_d7</c>,
        /// and one for every stage a season ever adds. Matched as a FAMILY rather than listed
        /// entry by entry, because the list could only ever be complete for the season it was
        /// written against: an author naming a later stage's flag would have fallen through to the
        /// generic "unknown discovery id" answer, which is true and useless, instead of being told
        /// the specific reason the whole family is barred.
        ///
        /// Must match <see cref="GameFlags.WatchPostedForDay"/>, which builds the same names on the
        /// write side.
        /// </summary>
        const string WatchPostedPrefix = "watch_posted_d";

        /// <summary>Why no member of the watch family may ever be an unlock condition.</summary>
        const string WatchPostedReason =
            "posting the watch grants Prophet points, on every stage that scores it";

        /// <summary>
        /// Flags that must never become discoveries, and why. This is a tripwire, not the
        /// enforcement — the allow list above is the enforcement — but it turns "unknown id" into a
        /// specific answer for the next person who tries.
        ///
        /// The watch flags are NOT in here. They are a family with one member per stage and are
        /// matched by <see cref="WatchPostedPrefix"/> instead; see that constant for why.
        /// </summary>
        static readonly Dictionary<string, string> ForbiddenFlags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Scored directly, in the same action that raises the flag.
            { "donated_rubble", "donating grants Shepherd points" },
            { "refused_invite", "refusing grants Zealot points" },
            { "accepted_invite", "the sibling branch of a scored choice" },
            { "fish_caught", "the catch grants Exile points" },
            { "reached_map_edge", "reaching the edge grants Exile points" },
            { "page_shown", "showing the page grants Prophet points" },
            { "page_skipped", "the sibling branch of a scored choice" },
            { "references_revealed", "raised in the same beat that scores Prophet" },

            // Doubly banned. Reading grants Scribe points, and rule 19 independently forbids paying
            // for a read in anything but understanding: a cosmetic for opening the chapter is the
            // slot machine with a Bible skin that rule exists to prevent. Do not "fix" this by
            // finding an unscored reading flag; the second reason survives the first.
            { "chapter_opened", "opening the chapter grants Scribe points, and rule 19 forbids paying for a read" },
            { "deep_read", "reading grants Scribe points, and rule 19 forbids paying for a read" }
        };

        // ------------------------------------------------------------------ evaluation

        /// <summary>
        /// Whether this condition is satisfied. A null condition is satisfied: an item with no
        /// condition is available from the start.
        /// </summary>
        public static bool IsMet(GameState state, UnlockCondition condition)
        {
            if (condition == null)
            {
                return true;
            }

            UnlockConditionType type;
            if (!TryParseType(condition.type, out type))
            {
                Debug.LogError("[Unlock] Unknown condition type '" + condition.type +
                               "' in character_catalog.json. Expected " + KeyWallStage + ", " +
                               KeySegmentsComplete + ", " + KeyDayReached +
                               " or discovery. Treating the item as unlocked.");
                return true;
            }

            if (state == null)
            {
                // No run in progress means nothing to measure. Showing the wardrobe as fully
                // available reads better than showing every padlock at once.
                return true;
            }

            switch (type)
            {
                case UnlockConditionType.WallStage:
                    return HighestWallStage(state) >= condition.value;

                case UnlockConditionType.SegmentsComplete:
                    return CompletedSegments(state) >= condition.value;

                case UnlockConditionType.DayReached:
                    return state.day >= condition.value;

                case UnlockConditionType.Discovery:
                    return IsDiscovered(state, condition.id);
            }

            return true;
        }

        /// <summary>Convenience for the wardrobe: is this item available in this run?</summary>
        public static bool IsUnlocked(GameState state, CatalogItemDef item)
        {
            return item == null || IsMet(state, item.unlock_condition);
        }

        /// <summary>
        /// The locale key for the sentence printed under the padlock, or null when the condition is
        /// null or unreadable — in both of those cases the item is available and there is nothing
        /// to print.
        ///
        /// No sentence is ever assembled in C#. This returns a key; the words live in
        /// <c>locales/&lt;locale&gt;/catalog.json</c> under <c>unlock</c>, and the threshold arrives
        /// as <c>{0}</c>. Numeric keys carry the same <c>.one</c> / <c>.other</c> suffix rule
        /// <see cref="Loc.Plural"/> uses, so "1 trecho" and "3 trechos" are one authored pair rather
        /// than a string built by code.
        /// </summary>
        public static string LocaleKey(UnlockCondition condition)
        {
            if (condition == null)
            {
                return null;
            }

            UnlockConditionType type;
            if (!TryParseType(condition.type, out type))
            {
                return null;
            }

            switch (type)
            {
                case UnlockConditionType.WallStage:
                    return KeyWallStage + PluralSuffix(condition.value);

                case UnlockConditionType.SegmentsComplete:
                    return KeySegmentsComplete + PluralSuffix(condition.value);

                case UnlockConditionType.DayReached:
                    return KeyDayReached + PluralSuffix(condition.value);

                case UnlockConditionType.Discovery:
                    return IsKnownDiscovery(condition.id) ? KeyDiscoveryPrefix + condition.id.Trim() : null;
            }

            return null;
        }

        /// <summary>
        /// The finished sentence for the padlock, in the loaded locale. Empty when there is nothing
        /// to say, which is exactly when the item is available.
        /// </summary>
        public static string Sentence(UnlockCondition condition)
        {
            string key = LocaleKey(condition);
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            return condition.id != null && key.StartsWith(KeyDiscoveryPrefix, StringComparison.Ordinal)
                ? CharacterCatalog.Text(key)
                : CharacterCatalog.Text(key, condition.value);
        }

        // ------------------------------------------------------------------ readings
        // Three readings, all of them of the wall and the calendar, none of them of a counter.

        /// <summary>
        /// The highest stage any one segment has reached, 0 to <see cref="CompleteStage"/>.
        ///
        /// The highest rather than the lowest on purpose. "The wall has reached stage N" measured
        /// as the lowest across every segment asks the player to bring the whole wall up together,
        /// which no stage ever asks them to do, and an item nobody can plausibly unlock is a
        /// padlock that reads as a tease. The highest is what the player has actually managed
        /// somewhere on the wall, and they can see it standing there.
        ///
        /// A longer season makes the lowest reading reachable but does not make it right: the
        /// wardrobe would then open in one lump near the end instead of giving each stage
        /// something to reach for, which is the whole argument for the backpack existing.
        /// </summary>
        public static int HighestWallStage(GameState state)
        {
            if (state == null || state.segments == null)
            {
                return 0;
            }

            int highest = 0;
            for (int i = 0; i < state.segments.Count; i++)
            {
                WallSegmentState segment = state.segments[i];
                if (segment != null && segment.stage > highest)
                {
                    highest = segment.stage;
                }
            }

            return highest;
        }

        /// <summary>How many segments are finished.</summary>
        public static int CompletedSegments(GameState state)
        {
            if (state == null || state.segments == null)
            {
                return 0;
            }

            int complete = 0;
            for (int i = 0; i < state.segments.Count; i++)
            {
                WallSegmentState segment = state.segments[i];
                if (segment != null && segment.stage >= CompleteStage)
                {
                    complete++;
                }
            }

            return complete;
        }

        /// <summary>
        /// Whether a named discovery has happened. An id outside
        /// <see cref="DiscoveryIds"/> never reaches <see cref="GameState.flags"/>: it is refused
        /// here, and refused loudly.
        /// </summary>
        public static bool IsDiscovered(GameState state, string id)
        {
            if (!IsKnownDiscovery(id))
            {
                LogRefusedDiscovery(id);
                return true;
            }

            return state != null && state.HasFlag(id.Trim());
        }

        /// <summary>True when this id is one an unlock condition is allowed to name.</summary>
        public static bool IsKnownDiscovery(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            string trimmed = id.Trim();
            for (int i = 0; i < DiscoveryIds.Count; i++)
            {
                if (string.Equals(DiscoveryIds[i], trimmed, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        static void LogRefusedDiscovery(string id)
        {
            string trimmed = id == null ? string.Empty : id.Trim();

            string reason;
            if (trimmed.StartsWith(WatchPostedPrefix, StringComparison.Ordinal))
            {
                // Checked before the dictionary, and by prefix, so every stage's watch flag gets
                // the specific answer rather than only the two a three-day season happened to have.
                reason = WatchPostedReason;
            }
            else
            {
                // Misses leave reason null, which is the "no specific answer" branch below.
                ForbiddenFlags.TryGetValue(trimmed, out reason);
            }

            if (reason != null)
            {
                Debug.LogError("[Unlock] '" + trimmed + "' may never be an unlock condition: " + reason +
                               ". CLAUDE.md rule 10 forbids showing progress toward a vocation, and an unlock " +
                               "condition is printed to the player in full. Use a wall or day milestone instead. " +
                               "Treating the item as unlocked.");
                return;
            }

            Debug.LogError("[Unlock] Unknown discovery id '" + trimmed +
                           "' in character_catalog.json. Only these are allowed: " +
                           string.Join(", ", DiscoveryIdsArray()) +
                           ". Adding one is a deliberate code change in UnlockEvaluator. Treating the item as unlocked.");
        }

        static string[] DiscoveryIdsArray()
        {
            var copy = new string[DiscoveryIds.Count];
            for (int i = 0; i < DiscoveryIds.Count; i++)
            {
                copy[i] = DiscoveryIds[i];
            }

            return copy;
        }

        // ------------------------------------------------------------------ token parsing
        // Newtonsoft matches enum member names, not snake_case tokens, so the type is parsed by
        // hand. This is also what makes the fail-open rule controllable: an unrecognised token is
        // one branch, in one place.

        public static bool TryParseType(string token, out UnlockConditionType type)
        {
            type = UnlockConditionType.DayReached;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            switch (token.Trim().ToLowerInvariant())
            {
                case KeyWallStage: type = UnlockConditionType.WallStage; return true;
                case KeySegmentsComplete: type = UnlockConditionType.SegmentsComplete; return true;
                case KeyDayReached: type = UnlockConditionType.DayReached; return true;
                case "discovery": type = UnlockConditionType.Discovery; return true;
                default: return false;
            }
        }

        static string PluralSuffix(int value)
        {
            return value == 1 ? Loc.PluralOne : Loc.PluralOther;
        }
    }
}
