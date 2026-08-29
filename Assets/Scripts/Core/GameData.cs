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

        /// <summary>Reads every content file. Safe to call more than once; the last read wins.</summary>
        public static void LoadAll()
        {
            Npcs = LoadArray<NpcDef>("npcs");
            Dialogue = LoadDictionary<DialogueNode>("dialogue");
            WallSegments = LoadArray<WallSegmentDef>("wall_segments");
            Contest = LoadObject("contest", EmptyContest());
            Vocations = LoadArray<VocationDef>("vocations");
            Quiz = LoadArray<QuizQuestion>("quiz");
            Map = LoadObject("map", EmptyMap());
        }

        static T[] LoadArray<T>(string fileName) where T : class
        {
            var json = ReadText(fileName);
            if (json == null)
            {
                return Array.Empty<T>();
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<T[]>(json);
                if (parsed == null)
                {
                    LogParseFailure(fileName, "the file did not contain an array");
                    return Array.Empty<T>();
                }

                return parsed;
            }
            catch (Exception exception)
            {
                LogParseFailure(fileName, exception.Message);
                return Array.Empty<T>();
            }
        }

        static IReadOnlyDictionary<string, T> LoadDictionary<T>(string fileName) where T : class
        {
            var json = ReadText(fileName);
            if (json == null)
            {
                return new Dictionary<string, T>();
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<Dictionary<string, T>>(json);
                if (parsed == null)
                {
                    LogParseFailure(fileName, "the file did not contain an object");
                    return new Dictionary<string, T>();
                }

                return parsed;
            }
            catch (Exception exception)
            {
                LogParseFailure(fileName, exception.Message);
                return new Dictionary<string, T>();
            }
        }

        static T LoadObject<T>(string fileName, T fallback) where T : class
        {
            var json = ReadText(fileName);
            if (json == null)
            {
                return fallback;
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<T>(json);
                if (parsed == null)
                {
                    LogParseFailure(fileName, "the file did not contain an object");
                    return fallback;
                }

                return parsed;
            }
            catch (Exception exception)
            {
                LogParseFailure(fileName, exception.Message);
                return fallback;
            }
        }

        static string ReadText(string fileName)
        {
            TextAsset asset;
            try
            {
                asset = Resources.Load<TextAsset>(ResourceFolder + fileName);
            }
            catch (Exception exception)
            {
                Debug.LogError("[GameData] Could not load Resources/Data/" + fileName + ".json: " + exception.Message);
                return null;
            }

            if (asset == null)
            {
                Debug.LogError("[GameData] Missing content file Resources/Data/" + fileName + ".json; using an empty default.");
                return null;
            }

            var text = asset.text;
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogError("[GameData] Content file Resources/Data/" + fileName + ".json is empty; using an empty default.");
                return null;
            }

            return text;
        }

        static void LogParseFailure(string fileName, string reason)
        {
            Debug.LogError("[GameData] Could not parse Resources/Data/" + fileName + ".json: " + reason + ". Using an empty default.");
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
