using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SheepGate.Core
{
    /// <summary>
    /// Whole-state persistence to a single JSON file. Newtonsoft is mandatory here: JsonUtility
    /// cannot round-trip the dictionaries and the flag set that GameState carries.
    /// Nothing in this class may throw at the caller: a missing, partial or corrupt file is a
    /// fresh run, never a crash.
    /// </summary>
    public static class SaveSystem
    {
        const string FileName = "save.json";
        const string TempFileName = "save.json.tmp";

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public static string SavePath
        {
            get { return Path.Combine(Application.persistentDataPath, FileName); }
        }

        static string TempPath
        {
            get { return Path.Combine(Application.persistentDataPath, TempFileName); }
        }

        public static bool HasSave()
        {
            try
            {
                return File.Exists(SavePath);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SaveSystem] Could not check for a save file: " + exception.Message);
                return false;
            }
        }

        /// <summary>Reads the save. Returns null when there is none or when it cannot be trusted.</summary>
        public static GameState Load()
        {
            string json;
            try
            {
                if (!File.Exists(SavePath))
                {
                    return null;
                }

                json = File.ReadAllText(SavePath);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SaveSystem] Could not read the save file: " + exception.Message);
                return null;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogError("[SaveSystem] The save file is empty; starting a new run.");
                return null;
            }

            GameState state;
            try
            {
                state = JsonConvert.DeserializeObject<GameState>(json, Settings);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SaveSystem] The save file is corrupt and was ignored: " + exception.Message);
                return null;
            }

            if (state == null)
            {
                Debug.LogError("[SaveSystem] The save file did not contain a game state; starting a new run.");
                return null;
            }

            Repair(state);
            return state;
        }

        public static void Save(GameState state)
        {
            if (state == null)
            {
                Debug.LogError("[SaveSystem] Refusing to save a null game state.");
                return;
            }

            string json;
            try
            {
                json = JsonConvert.SerializeObject(state, Settings);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SaveSystem] Could not serialize the game state: " + exception.Message);
                return;
            }

            // Write to a temporary file first, so a kill mid-write leaves the previous save intact
            // instead of a truncated one.
            try
            {
                var temporaryPath = TempPath;
                var finalPath = SavePath;

                File.WriteAllText(temporaryPath, json);

                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                File.Move(temporaryPath, finalPath);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SaveSystem] Could not write the save file: " + exception.Message);
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    File.Delete(SavePath);
                }

                if (File.Exists(TempPath))
                {
                    File.Delete(TempPath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[SaveSystem] Could not delete the save file: " + exception.Message);
            }
        }

        /// <summary>
        /// Fills in anything a partial or older save left null, so no consumer has to null check
        /// collections that GameState declares as always present.
        /// </summary>
        static void Repair(GameState state)
        {
            if (state.appearance == null)
            {
                state.appearance = new AppearanceState();
            }

            if (state.playerName == null)
            {
                state.playerName = "";
            }

            if (state.segments == null)
            {
                state.segments = new List<WallSegmentState>();
            }
            else
            {
                state.segments.RemoveAll(segment => segment == null || string.IsNullOrEmpty(segment.id));
            }

            if (state.vocationScores == null)
            {
                state.vocationScores = new Dictionary<string, int>();
            }

            if (state.flags == null)
            {
                state.flags = new HashSet<string>();
            }

            if (state.counters == null)
            {
                state.counters = new Dictionary<string, int>();
            }

            if (state.day < 1)
            {
                state.day = 1;
            }

            if (state.workCapacityMax < 1)
            {
                state.workCapacityMax = 12;
            }

            if (state.workCapacity < 0)
            {
                state.workCapacity = 0;
            }
        }
    }
}
