using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace SheepGate.Core
{
    /// <summary>
    /// Whole-state persistence to a single JSON file. Newtonsoft is mandatory here: the built-in Unity JSON helper
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
            get { return Path.Combine(AppPaths.DataRoot, FileName); }
        }

        static string TempPath
        {
            get { return Path.Combine(AppPaths.DataRoot, TempFileName); }
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

        /// <summary>
        /// While true, <see cref="Save"/> writes nothing.
        ///
        /// It exists for one moment: the player asking to start over. Deleting the file is not
        /// enough on its own, because tearing the village down runs a scene's worth of teardown —
        /// panels closing, the wall flushing its progress — and any one of those calls
        /// <c>WorldRuntime.SaveNow</c> and writes the run straight back out. A flag on the writer
        /// closes every one of those doors at once, which auditing the callers could not.
        ///
        /// It is raised by <see cref="SuspendWrites"/> rather than by <see cref="Delete"/>, because
        /// Delete has an existing caller — the acceptance harness — that deletes a scratch save and
        /// then carries on writing real ones. Restarting is a decision, so it says so in its own
        /// call.
        /// </summary>
        public static bool Suspended { get; private set; }

        /// <summary>
        /// Stops every further write for the lifetime of the process. Nothing lowers it: the only
        /// supported way back is the boot sequence, which starts from a fresh process state.
        /// </summary>
        public static void SuspendWrites()
        {
            Suspended = true;
        }

        public static void Save(GameState state)
        {
            if (Suspended)
            {
                return;
            }

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

            // Materials. state.rubble is the storage behind state.stone, so clamping it clamps both.
            if (state.rubble < 0)
            {
                state.rubble = 0;
            }

            if (state.timber < 0)
            {
                state.timber = 0;
            }

            if (state.blocks < 0)
            {
                state.blocks = 0;
            }

            MigrateSchema(state);
        }

        /// <summary>
        /// Brings an older save up to <see cref="GameState.CurrentSchemaVersion"/>. It is called
        /// from <see cref="Repair"/>, which is the one place a save is read back, so a migration
        /// cannot run anywhere else and cannot run twice: the version is stamped in the same pass
        /// that applies it.
        ///
        /// Rule 7 governs every step written here - progress already made never regresses - so a
        /// step may add or rename, never drop a unit the player had already collected.
        ///
        /// <b>0/1 -> 2, the material split.</b> Stone kept the field that old saves already wrote,
        /// <c>GameState.rubble</c>, and <c>GameState.stone</c> is a property over that same field.
        /// So every unit of rubble in an old save is already stone the moment it deserializes:
        /// there is nothing to copy, and nothing a second run of this method could double. Timber
        /// and blocks default to 0, which is exactly what a player who never had them should have.
        /// All this step does is record the shape.
        ///
        /// The day someone renames the field for real and makes <c>stone</c> its own storage, this
        /// step stops being free: it has to gain the actual copy (stone = rubble), still guarded by
        /// the version so it runs once.
        /// </summary>
        static void MigrateSchema(GameState state)
        {
            if (state.schemaVersion >= GameState.CurrentSchemaVersion)
            {
                // Current, or written by a build newer than this one. Never step a version back:
                // guessing at a shape from the future is how a save loses material.
                return;
            }

            Debug.Log("[SaveSystem] Migrating a save from schema " + state.schemaVersion +
                      " to " + GameState.CurrentSchemaVersion + "; stone carried over: " + state.stone + ".");

            state.schemaVersion = GameState.CurrentSchemaVersion;
        }
    }
}
