using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace SheepGate.Core
{
    /// <summary>
    /// Loads every authored content file under Resources/Data and keeps it in memory for the run.
    /// Content lives in JSON rather than ScriptableObjects so it can be edited outside Unity.
    ///
    /// Content is split in two. Resources/Data holds structure and numbers — ids, coordinates,
    /// costs, deltas — and there is exactly one copy of it, so balance cannot drift between
    /// languages. Resources/Data/locales/&lt;locale&gt; holds every string a player can read, and is
    /// merged onto the same DTOs once loaded. Consumers see one object with every field filled,
    /// exactly as they did before there were locales.
    ///
    /// This class deliberately does NOT touch verses.json: the scripture index is owned by
    /// SheepGate.Scripture.ScriptureService, and that is the only place literal text is ever read.
    ///
    /// Every property is non-null before and after LoadAll. A missing or malformed file logs an
    /// error and leaves the empty default in place, so a content mistake degrades the build
    /// instead of stopping it.
    /// </summary>
    public static class GameData
    {
        const string ResourceFolder = "Data/";

        /// <summary>
        /// Id of the contest <see cref="Contest"/> resolves to. Named here rather than spelled at
        /// the property, because it is the one place the legacy singleton accessor is tied to a
        /// particular entry in a file that now holds several.
        /// </summary>
        const string FirstContestId = "raid";

        public static NpcDef[] Npcs { get; private set; } = Array.Empty<NpcDef>();

        public static IReadOnlyDictionary<string, DialogueNode> Dialogue { get; private set; } =
            new Dictionary<string, DialogueNode>();

        public static WallSegmentDef[] WallSegments { get; private set; } = Array.Empty<WallSegmentDef>();

        /// <summary>
        /// The season, in order, one entry per stage. Read <see cref="Stage"/> rather than indexing
        /// this: the day is 1-based and the array is not, and getting that wrong is off by one
        /// stage everywhere at once.
        /// </summary>
        public static StageDef[] Stages { get; private set; } = Array.Empty<StageDef>();

        /// <summary>Every morale contest, keyed by the id a stage names in its <c>contest</c> field.</summary>
        public static IReadOnlyDictionary<string, ContestConfig> Contests { get; private set; } =
            new Dictionary<string, ContestConfig>();

        /// <summary>
        /// The first contest of the season, kept under the name it has always had. There is more
        /// than one contest now, so new code should name the one it means through
        /// <see cref="Contests"/>; this stays so no existing caller had to change on the day the
        /// file grew a second entry.
        /// </summary>
        public static ContestConfig Contest
        {
            get
            {
                ContestConfig raid;
                if (Contests != null && Contests.TryGetValue(FirstContestId, out raid) && raid != null)
                {
                    return raid;
                }

                return MissingContest;
            }
        }

        public static VocationDef[] Vocations { get; private set; } = Array.Empty<VocationDef>();

        public static QuizQuestion[] Quiz { get; private set; } = Array.Empty<QuizQuestion>();

        public static MapDef Map { get; private set; } = EmptyMap();

        /// <summary>The locale whose strings are merged into the content in memory.</summary>
        public static string LoadedLocale { get; private set; } = string.Empty;

        /// <summary>Reads every content file for the active locale.</summary>
        public static void LoadAll()
        {
            LoadAll(Locales.Active);
        }

        /// <summary>
        /// Reads every content file, taking player-facing strings from this locale.
        /// Safe to call more than once; the last read wins, which is what a locale switch relies on.
        /// </summary>
        public static void LoadAll(string locale)
        {
            string canonical = Locales.Canonical(locale) ?? Locales.Source;
            LoadedLocale = canonical;
            string localeFolder = Locales.ResourceFolder(canonical) + "/";

            // ---- structure and numbers: one copy, shared by every language
            Npcs = LoadArray<NpcDef>(ResourceFolder + "npcs");
            WallSegments = LoadArray<WallSegmentDef>(ResourceFolder + "wall_segments");
            Stages = LoadArray<StageDef>(ResourceFolder + "stages");
            Contests = LoadDictionary<ContestConfig>(ResourceFolder + "contest");
            Vocations = LoadArray<VocationDef>(ResourceFolder + "vocations");
            Quiz = LoadArray<QuizQuestion>(ResourceFolder + "quiz");
            Map = LoadObject(ResourceFolder + "map", EmptyMap());

            // ---- strings: one file per language, merged onto the objects above
            Dialogue = LoadDictionary<DialogueNode>(localeFolder + "dialogue");
            MergeNpcNames(localeFolder);
            MergeContestStrings(localeFolder);
            MergeVocationStrings(localeFolder);
            MergeQuizStrings(localeFolder);

            // Last, because two of the checks read Dialogue and Contests, which the lines above
            // fill. Nothing between here and the end of this method may depend on the result:
            // a broken stage table degrades the build, it does not stop it.
            VerifyStages();
        }

        /// <summary>
        /// The stage for this day. Never null, and never throws — every caller here is inside a
        /// frame or a UI build where an exception is a black screen.
        ///
        /// Out-of-range clamps to the nearest real stage and logs once. A day past the end is the
        /// shape a save from a longer season takes, and rule 7 says the answer to that is "carry on
        /// at the last stage we have", never a reset and never a game over.
        /// </summary>
        public static StageDef Stage(int day)
        {
            StageDef[] stages = Stages;
            if (stages == null || stages.Length == 0)
            {
                if (!_loggedEmptyStages)
                {
                    _loggedEmptyStages = true;
                    Debug.LogError(
                        "[GameData] No stages are loaded; falling back to a single blank stage. " +
                        "Check Resources/Data/stages.json.");
                }

                return FallbackStage;
            }

            StageDef first = null;
            StageDef last = null;
            for (int i = 0; i < stages.Length; i++)
            {
                StageDef stage = stages[i];
                if (stage == null)
                {
                    // A null entry is already a logged error from VerifyStages. Skipping it here
                    // rather than indexing past it is what keeps a malformed file from turning a
                    // logged content error into a null reference three systems away.
                    continue;
                }

                if (stage.day == day)
                {
                    return stage;
                }

                if (first == null)
                {
                    first = stage;
                }

                last = stage;
            }

            if (first == null)
            {
                return FallbackStage;
            }

            StageDef clamped = day < first.day ? first : last;
            if (!_loggedStageClamp)
            {
                _loggedStageClamp = true;
                Debug.LogWarning(
                    "[GameData] No stage declares day " + day + "; using stage \"" + clamped.id +
                    "\" (day " + clamped.day + "). Logged once per run.");
            }

            return clamped;
        }

        // Latches for the two Stage() diagnostics. Stage() is called from Update paths and from
        // every UI rebuild, so an unlatched log would be thousands of identical lines a second and
        // would bury the one that mattered. Deliberately not re-armed by a second LoadAll either:
        // a locale switch rereads every file, and a stage table that is wrong is wrong in both
        // languages, so re-arming would only reprint the same line on every switch. VerifyStages,
        // which runs once per load and not per frame, is the one that is meant to speak up again.
        static bool _loggedEmptyStages;
        static bool _loggedStageClamp;

        /// <summary>
        /// The stage handed back when stages.json produced nothing at all. Blank on purpose rather
        /// than plausible: it turns on no system, ends no season and reveals nothing, so a missing
        /// stage table cannot look like a design decision.
        /// </summary>
        static readonly StageDef FallbackStage = new StageDef
        {
            day = 1,
            id = "",
            type = StageTypes.Work,
            map_anchor = new float[] { 0.5f, 0.5f },
            map_focus = new float[] { 0.5f, 0.5f }
        };

        // ------------------------------------------------------------------ merges
        // A merge never invents a string. When the locale file has no entry for an id the field is
        // left null, and the consumer's own fallback (usually the raw id) makes the gap visible.
        // tools/validate-content.mjs is what stops that reaching a player: it fails the build when
        // a locale is missing an id that the structural file defines.

        static void MergeNpcNames(string localeFolder)
        {
            var names = LoadDictionary<string>(localeFolder + "npcs");
            for (int i = 0; i < Npcs.Length; i++)
            {
                NpcDef npc = Npcs[i];
                if (npc == null || string.IsNullOrEmpty(npc.id))
                {
                    continue;
                }

                string display;
                if (names.TryGetValue(npc.id, out display))
                {
                    npc.display = display;
                }
                else
                {
                    LogMissingString("npcs", npc.id);
                }
            }
        }

        // The locale copy stays one flat move_id -> {display, description} map however many
        // contests the structural file grows, because the move table is shared: two contests draw
        // from the same moves and differ by tuning, not by vocabulary. So this walks every
        // contest's moves against the one map, and a move that appears in both is simply filled in
        // twice with the same words.
        static void MergeContestStrings(string localeFolder)
        {
            var strings = LoadDictionary<ContestMoveStrings>(localeFolder + "contest");
            if (Contests == null)
            {
                return;
            }

            foreach (var pair in Contests)
            {
                ContestConfig contest = pair.Value;
                ContestMoveDef[] moves = contest != null ? contest.moves : null;
                if (moves == null)
                {
                    continue;
                }

                for (int i = 0; i < moves.Length; i++)
                {
                    ContestMoveDef move = moves[i];
                    if (move == null || string.IsNullOrEmpty(move.id))
                    {
                        continue;
                    }

                    ContestMoveStrings entry;
                    if (strings.TryGetValue(move.id, out entry) && entry != null)
                    {
                        move.display = entry.display;
                        move.description = entry.description;
                    }
                    else
                    {
                        LogMissingString("contest", move.id);
                    }
                }
            }
        }

        static void MergeVocationStrings(string localeFolder)
        {
            var strings = LoadDictionary<VocationStrings>(localeFolder + "vocations");
            for (int i = 0; i < Vocations.Length; i++)
            {
                VocationDef vocation = Vocations[i];
                if (vocation == null || string.IsNullOrEmpty(vocation.id))
                {
                    continue;
                }

                VocationStrings entry;
                if (strings.TryGetValue(vocation.id, out entry) && entry != null)
                {
                    vocation.display = entry.display;
                    vocation.reveal_line = entry.reveal_line;
                }
                else
                {
                    LogMissingString("vocations", vocation.id);
                }
            }
        }

        // Keyed by day rather than by an id of its own, because the day is how DailyQuiz selects
        // a question. Inventing a second key would let the two disagree.
        static void MergeQuizStrings(string localeFolder)
        {
            var strings = LoadDictionary<QuizStrings>(localeFolder + "quiz");
            for (int i = 0; i < Quiz.Length; i++)
            {
                QuizQuestion question = Quiz[i];
                if (question == null)
                {
                    continue;
                }

                string key = question.day.ToString();
                QuizStrings entry;
                if (strings.TryGetValue(key, out entry) && entry != null)
                {
                    question.prompt = entry.prompt;
                    question.options = entry.options;
                    question.note = entry.note;
                    question.hook = entry.hook;
                }
                else
                {
                    LogMissingString("quiz", key);
                }
            }
        }

        static void LogMissingString(string fileName, string key)
        {
            Debug.LogError(
                "[GameData] Locale " + LoadedLocale + " has no entry '" + key + "' in " + fileName +
                ".json. Run: node tools/validate-content.mjs");
        }

        // ------------------------------------------------------------------ stage invariants
        //
        // Every check here is a Debug.LogError and then carries on, which is this class's standing
        // policy for content: a bad file degrades the build instead of stopping it. That is not
        // leniency. The E2E runner treats a logged error as a run failure, so a broken stage table
        // fails the gate loudly while still leaving a build a person can open and look at, which is
        // how you find out WHICH stage is wrong.
        //
        // The reason there are so many of them: a stage table that is merely miswired — two stages
        // claiming the reveal, or none claiming the ending — produces no exception anywhere. It
        // produces a season that plays through and quietly never does the thing the whole build
        // exists to do, in a system nobody edits after this package lands.

        static void VerifyStages()
        {
            StageDef[] stages = Stages;
            if (stages == null || stages.Length == 0)
            {
                Debug.LogError("[GameData] Resources/Data/stages.json declares no stages; the season has no length.");
                return;
            }

            int terminal = 0;
            int revealsPage = 0;
            int revealsVocation = 0;
            int closesGate = 0;
            int finishesWallDay = 0;
            int closesGateDay = 0;

            for (int i = 0; i < stages.Length; i++)
            {
                StageDef stage = stages[i];
                if (stage == null)
                {
                    Debug.LogError("[GameData] stages.json entry " + i + " is null.");
                    continue;
                }

                string where = "stage \"" + stage.id + "\" (entry " + i + ")";

                if (string.IsNullOrEmpty(stage.id))
                {
                    Debug.LogError("[GameData] stages.json entry " + i + " has no id.");
                }

                // Contiguous 1..N, in file order. Days are ids as much as they are numbers: the
                // dialogue selector, the quiz selector and every daily-reset token compare against
                // this int, so a gap is a stage nothing can ever select and a repeat is two stages
                // fighting over one day's content.
                if (stage.day != i + 1)
                {
                    Debug.LogError(
                        "[GameData] " + where + " declares day " + stage.day + " but sits at position " +
                        (i + 1) + ". Days must run 1.." + stages.Length + " in file order, with no gaps and no repeats.");
                }

                if (!IsKnownStageType(stage.type))
                {
                    Debug.LogError(
                        "[GameData] " + where + " has type \"" + stage.type + "\", which is not one of " +
                        "intro, work, rest, battle, boss, gate.");
                }

                // WP-5 indexes [0] and [1] on both of these without looking, which is exactly the
                // silent miswire this method exists to catch: a one-element anchor is an
                // IndexOutOfRange inside a UI build, three stages later, with nothing naming the file.
                VerifyAnchor(where, "map_anchor", stage.map_anchor);
                VerifyAnchor(where, "map_focus", stage.map_focus);

                if (!string.IsNullOrEmpty(stage.contest) &&
                    (Contests == null || !Contests.ContainsKey(stage.contest)))
                {
                    Debug.LogError(
                        "[GameData] " + where + " names contest \"" + stage.contest +
                        "\", which contest.json does not define.");
                }

                if (!string.IsNullOrEmpty(stage.cutscene_node) &&
                    (Dialogue == null || !Dialogue.ContainsKey(stage.cutscene_node)))
                {
                    Debug.LogError(
                        "[GameData] " + where + " names cutscene node \"" + stage.cutscene_node +
                        "\", which locale " + LoadedLocale + " does not define in dialogue.json.");
                }

                if (stage.terminal)
                {
                    terminal++;
                    if (i != stages.Length - 1)
                    {
                        Debug.LogError(
                            "[GameData] " + where + " is marked terminal but is not the last stage. " +
                            "The terminal stage takes the final-day hold and never releases it, so every " +
                            "stage after it is unreachable.");
                    }
                }

                if (stage.reveals_page)
                {
                    revealsPage++;
                }

                if (stage.reveals_vocation)
                {
                    revealsVocation++;
                }

                if (stage.finishes_wall)
                {
                    finishesWallDay = stage.day;
                }

                if (stage.closes_gate)
                {
                    closesGate++;
                    closesGateDay = stage.day;
                    if (i != stages.Length - 1)
                    {
                        Debug.LogError("[GameData] " + where + " closes the gate but is not the last stage.");
                    }

                    if (string.IsNullOrEmpty(stage.gate_segment))
                    {
                        Debug.LogError("[GameData] " + where + " closes the gate but names no gate_segment.");
                    }
                    else if (!HasSegment(stage.gate_segment))
                    {
                        Debug.LogError(
                            "[GameData] " + where + " names gate segment \"" + stage.gate_segment +
                            "\", which wall_segments.json does not define.");
                    }
                }
            }

            RequireExactlyOne("terminal", terminal);
            RequireExactlyOne("reveals_page", revealsPage);
            RequireExactlyOne("reveals_vocation", revealsVocation);
            RequireExactlyOne("closes_gate", closesGate);

            // finishes_wall force-completes the wall except the gate, so the gate has to still be
            // ahead when it runs. Behind it, the dedication would open on a wall the player watched
            // finish itself a stage ago.
            // Guarded on closesGateDay so a table with no gate stage at all reports that once,
            // through RequireExactlyOne, rather than twice in two different vocabularies.
            if (finishesWallDay != 0 && closesGateDay != 0 && finishesWallDay >= closesGateDay)
            {
                Debug.LogError(
                    "[GameData] stages.json finishes the wall on day " + finishesWallDay +
                    " but closes the gate on day " + closesGateDay +
                    "; the wall must be finished before the gate is hung, never on or after it.");
            }
        }

        static void RequireExactlyOne(string field, int count)
        {
            if (count == 1)
            {
                return;
            }

            Debug.LogError(
                "[GameData] stages.json has " + count + " stages with " + field +
                " set; exactly one is required. " +
                (count == 0 ? "With none, that beat never happens." : "With more than one, it happens twice."));
        }

        static void VerifyAnchor(string where, string field, float[] anchor)
        {
            if (anchor != null && anchor.Length == 2)
            {
                return;
            }

            Debug.LogError(
                "[GameData] " + where + " has " + field + " with " +
                (anchor == null ? "no value" : anchor.Length + " entries") +
                "; it must be exactly two, normalised 0..1 from the bottom left.");
        }

        static bool IsKnownStageType(string type)
        {
            return type == StageTypes.Intro
                   || type == StageTypes.Work
                   || type == StageTypes.Rest
                   || type == StageTypes.Battle
                   || type == StageTypes.Boss
                   || type == StageTypes.Gate;
        }

        static bool HasSegment(string id)
        {
            WallSegmentDef[] definitions = WallSegments;
            if (definitions == null)
            {
                return false;
            }

            for (int i = 0; i < definitions.Length; i++)
            {
                if (definitions[i] != null && definitions[i].id == id)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ reading

        static T[] LoadArray<T>(string resourcePath) where T : class
        {
            var json = ReadText(resourcePath);
            if (json == null)
            {
                return Array.Empty<T>();
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<T[]>(json);
                if (parsed == null)
                {
                    LogParseFailure(resourcePath, "the file did not contain an array");
                    return Array.Empty<T>();
                }

                return parsed;
            }
            catch (Exception exception)
            {
                LogParseFailure(resourcePath, exception.Message);
                return Array.Empty<T>();
            }
        }

        static Dictionary<string, T> LoadDictionary<T>(string resourcePath)
        {
            var json = ReadText(resourcePath);
            if (json == null)
            {
                return new Dictionary<string, T>();
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<Dictionary<string, T>>(json);
                if (parsed == null)
                {
                    LogParseFailure(resourcePath, "the file did not contain an object");
                    return new Dictionary<string, T>();
                }

                return parsed;
            }
            catch (Exception exception)
            {
                LogParseFailure(resourcePath, exception.Message);
                return new Dictionary<string, T>();
            }
        }

        static T LoadObject<T>(string resourcePath, T fallback) where T : class
        {
            var json = ReadText(resourcePath);
            if (json == null)
            {
                return fallback;
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<T>(json);
                if (parsed == null)
                {
                    LogParseFailure(resourcePath, "the file did not contain an object");
                    return fallback;
                }

                return parsed;
            }
            catch (Exception exception)
            {
                LogParseFailure(resourcePath, exception.Message);
                return fallback;
            }
        }

        static string ReadText(string resourcePath)
        {
            TextAsset asset;
            try
            {
                asset = Resources.Load<TextAsset>(resourcePath);
            }
            catch (Exception exception)
            {
                Debug.LogError("[GameData] Could not load Resources/" + resourcePath + ".json: " + exception.Message);
                return null;
            }

            if (asset == null)
            {
                Debug.LogError("[GameData] Missing content file Resources/" + resourcePath + ".json; using an empty default.");
                return null;
            }

            var text = asset.text;
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogError("[GameData] Content file Resources/" + resourcePath + ".json is empty; using an empty default.");
                return null;
            }

            return text;
        }

        static void LogParseFailure(string resourcePath, string reason)
        {
            Debug.LogError("[GameData] Could not parse Resources/" + resourcePath + ".json: " + reason + ". Using an empty default.");
        }

        // Defaults are deliberately blank rather than plausible: a silent fallback that looks like
        // real tuning would hide a content bug behind a playable-looking build.
        // Shared rather than built per call, because Contest is read from a frame path and its miss
        // branch would otherwise allocate on every one of them.
        static readonly ContestConfig MissingContest = EmptyContest();

        static ContestConfig EmptyContest()
        {
            return new ContestConfig { moves = Array.Empty<ContestMoveDef>() };
        }

        static MapDef EmptyMap()
        {
            return new MapDef
            {
                rows = Array.Empty<string>(),
                rubble = Array.Empty<GridPos>()
            };
        }
    }
}
