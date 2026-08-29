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

        public static NpcDef[] Npcs { get; private set; } = Array.Empty<NpcDef>();

        public static IReadOnlyDictionary<string, DialogueNode> Dialogue { get; private set; } =
            new Dictionary<string, DialogueNode>();

        public static WallSegmentDef[] WallSegments { get; private set; } = Array.Empty<WallSegmentDef>();

        public static ContestConfig Contest { get; private set; } = EmptyContest();

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
            Contest = LoadObject(ResourceFolder + "contest", EmptyContest());
            Vocations = LoadArray<VocationDef>(ResourceFolder + "vocations");
            Quiz = LoadArray<QuizQuestion>(ResourceFolder + "quiz");
            Map = LoadObject(ResourceFolder + "map", EmptyMap());

            // ---- strings: one file per language, merged onto the objects above
            Dialogue = LoadDictionary<DialogueNode>(localeFolder + "dialogue");
            MergeNpcNames(localeFolder);
            MergeContestStrings(localeFolder);
            MergeVocationStrings(localeFolder);
            MergeQuizStrings(localeFolder);
        }

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

        static void MergeContestStrings(string localeFolder)
        {
            var strings = LoadDictionary<ContestMoveStrings>(localeFolder + "contest");
            ContestMoveDef[] moves = Contest != null ? Contest.moves : null;
            if (moves == null)
            {
                return;
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
