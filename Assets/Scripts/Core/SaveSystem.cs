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

            if (state.workCapacityMax < 1)
            {
                state.workCapacityMax = GameState.DefaultWorkCapacityMax;
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

            // After, never before. The day this save carries only means something once the
            // migration has said which season's numbering it is written in.
            ClampDay(state);
        }

        /// <summary>
        /// Holds the day inside the season this build knows about — and only for a save this build
        /// knows about.
        ///
        /// It runs after <see cref="MigrateSchema"/> on purpose, and both halves need that order.
        /// A floor applied earlier would be reading the pre-migration number. A ceiling applied
        /// earlier would be measuring a day against a season that save was not written for, which
        /// is precisely the case the migration exists to translate.
        ///
        /// A save from a NEWER build is left alone entirely, floor and ceiling both. MigrateSchema
        /// already refuses to step such a save back; clamping its day here would undo that refusal
        /// by another route, and the very next <see cref="Save"/> would write the clawed-back
        /// number to disk. Guessing at a shape from the future is how a save loses progress.
        /// </summary>
        static void ClampDay(GameState state)
        {
            if (state.schemaVersion > GameState.CurrentSchemaVersion)
            {
                return;
            }

            if (state.day < 1)
            {
                state.day = 1;
            }

            // The season's length comes from the stage table rather than a constant here, so a
            // season that grows needs no edit in this file. When no table is loaded there is simply
            // no ceiling to apply and the day stands: a content-loading problem must never turn
            // into a lost day.
            StageDef[] stages = GameData.Stages;
            if (stages == null || stages.Length == 0 || stages[stages.Length - 1] == null)
            {
                return;
            }

            int lastDay = stages[stages.Length - 1].day;
            if (state.day > lastDay)
            {
                Debug.LogWarning(
                    "[SaveSystem] The save is on day " + state.day + " but this season ends at day " +
                    lastDay + "; resuming at the last stage.");
                state.day = lastDay;
            }
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
        ///
        /// <b>0/1/2 -> 3, the nine-stage season.</b> The first step in this file that actually
        /// does something. See <see cref="MigrateToNineStageSeason"/> for the whole argument; the
        /// short version is that <c>day: 3</c> used to mean "the last day, and the trial is tonight"
        /// and now means an ordinary third-of-nine working day, so a save carrying it has to be
        /// moved forward or it wakes up a third of the way through a season it had finished.
        /// </summary>
        static void MigrateSchema(GameState state)
        {
            if (state.schemaVersion >= GameState.CurrentSchemaVersion)
            {
                // Current, or written by a build newer than this one. Never step a version back:
                // guessing at a shape from the future is how a save loses material.
                return;
            }

            int from = state.schemaVersion;
            Debug.Log("[SaveSystem] Migrating a save from schema " + from +
                      " to " + GameState.CurrentSchemaVersion + "; stone carried over: " + state.stone + ".");

            if (from < 3)
            {
                MigrateToNineStageSeason(state);
            }

            if (from < 4)
            {
                MigrateToTheShortDay(state);
            }

            state.schemaVersion = GameState.CurrentSchemaVersion;
        }

        /// <summary>
        /// Moves a save written for the twelve-course day onto the four-course one.
        ///
        /// Only the ceiling and today's remainder change. Nothing the player holds moves: stone,
        /// timber, blocks and every course already standing are untouched, so rule 7 holds by
        /// construction. The remainder is clamped rather than reset because a save taken with two
        /// units left still has two units left; one taken with eight has four, which is the whole
        /// of the new day, and the extra four were never courses anyone could have laid — the
        /// material for them did not exist, which is the reason the ceiling moved.
        /// </summary>
        static void MigrateToTheShortDay(GameState state)
        {
            state.workCapacityMax = GameState.DefaultWorkCapacityMax;
            if (state.workCapacity > state.workCapacityMax)
            {
                state.workCapacity = state.workCapacityMax;
            }
        }

        // What the three-day season's last day was numbered. Not DayCycle.FinalDay: that follows
        // the CURRENT stage table, and a migration step has to keep describing the schema it was
        // written against however far the game moves afterwards.
        const int LegacySeasonFinalDay = 3;

        // Where schema 3 puts the trial the legacy final day carried, and the stage after it.
        // Literals rather than a lookup in stages.json, and that is the point: a migration step is
        // a fixed historical mapping from one named schema to the next. If a later season renumbers
        // the stages, the answer is a schema 4 step, never a quiet change of meaning under this one.
        // TrialConsistencyCheck below is what stops the literals going stale unnoticed.
        const int TrialStageDay = 6;
        const int AfterTrialStageDay = 7;

        // The only contest the three-day season had. Its flat flag becomes this contest's keyed one.
        const string LegacyContestId = "raid";

        /// <summary>
        /// Moves a save written for the three-day season into the nine-stage one.
        ///
        /// <b>Why the mapping is 3 -> 6.</b> The anchor is the beat, not the position. In the old
        /// build day 3 was "the day of the trial": the village half in the morning, the morale
        /// contest in the evening, and the ending straight after it. In the season, the trial is
        /// stage 6. So a save sitting on the old day 3 is sitting on the day the trial happens, and
        /// stage 6 is where that day now lives. The two alternatives were both considered and both
        /// are worse:
        ///   - Leaving it at 3 is the regression rule 7 forbids. That player was at the end of the
        ///     season; the map would put them a third of the way in, having lost nothing but their
        ///     place, which is exactly the punishment the rule exists to prevent.
        ///   - Sending it to 9, the new last stage, hands them the ending and skips the reveal —
        ///     the one beat the entire product exists to measure.
        /// Days 1 and 2 need no mapping at all: those stages are the same stages, unedited, so the
        /// save is already on the right one. Stages 3, 4 and 5 are new content a migrated day-3
        /// player does not get; that is content they never had, not progress taken away.
        ///
        /// <b>Why 7 when the trial was already fought.</b> A save carrying the flat
        /// <c>contest_resolved</c> had finished the trial, and in the old build that meant the run
        /// was over. Landing it on 6 would put it on the trial day with the trial already resolved,
        /// which reads as a replay and which the keyed short-circuit would skip anyway. Stage 7 is
        /// the first day of the season it has genuinely not seen.
        ///
        /// <b>What it never touches.</b> Not <c>segments</c>, not <c>rubble</c>/<c>timber</c>/
        /// <c>blocks</c>, not <c>vocationScores</c>, and no flag is ever cleared. That is the rule 4
        /// guarantee stated as code rather than as a promise: a completed masonry course cannot be
        /// lost here because nothing here can write one. The raised <c>stage_cost</c> in
        /// wall_segments.json cannot take one back either — <c>WallSegmentState.stage</c> is stored,
        /// not derived, and WallSystem.ApplyWork treats an over-full <c>workInStage</c> as already
        /// paid rather than as a debt.
        ///
        /// <b>Idempotent, and provably so rather than by assertion.</b> Belt: it runs behind the
        /// version guard in <see cref="MigrateSchema"/>, which stamps the version in the same pass,
        /// so it cannot run twice. Braces: both of its outputs are fixed points anyway. It can only
        /// ever produce day 6 or day 7, and it produces 6 exactly when the trial was not fought and
        /// 7 exactly when it was — so feeding either back in returns the same number. The reason
        /// that holds is that the branch reads the LEGACY flat flag, which this method never
        /// writes; had it branched on the keyed flag it raises, a second pass would have read its
        /// own output and moved a day-6 save to 7.
        ///
        /// <b>Accepted consequence, recorded so nobody "fixes" it.</b> A player who had finished the
        /// old run resumes at stage 7 with <c>vocation_revealed</c> and <c>references_revealed</c>
        /// already earned, so the final reveal does not fire again for them. Those are things they
        /// have already been given; re-arming them would mean clearing an earned flag, and a
        /// migration that clears what a player earned is the exact shape of the regression this
        /// whole method is written to avoid.
        /// </summary>
        static void MigrateToNineStageSeason(GameState state)
        {
            if (string.IsNullOrEmpty(state.seasonId))
            {
                // Belt and braces. GameState.seasonId has a field initializer, so a save written
                // before the field existed already deserializes as this season; this only catches a
                // file that carries the key with an empty value.
                state.seasonId = GameState.DefaultSeasonId;
            }

            // The three-day season had exactly one contest, so its flat flag means "the raid was
            // fought". Translating it to the keyed name is what lets the season's second contest
            // tell itself apart from the first. Additive: the legacy flag is kept, because dropping
            // a flag is dropping something the player did.
            bool trialFought = state.HasFlag(GameFlags.ContestResolved);
            if (trialFought)
            {
                state.SetFlag(GameFlags.ContestResolvedFor(LegacyContestId));
            }

            if (state.day < LegacySeasonFinalDay)
            {
                Debug.Log("[SaveSystem] Season migration: day " + state.day +
                          " needs no move; that stage is unchanged." +
                          (trialFought ? " contest_resolved -> " + GameFlags.ContestResolvedFor(LegacyContestId) + "." : ""));
                return;
            }

            TrialConsistencyCheck();

            int before = state.day;
            state.day = trialFought ? AfterTrialStageDay : TrialStageDay;

            Debug.Log("[SaveSystem] Season migration: day " + before + " -> " + state.day +
                      (trialFought
                          ? " (the trial was already fought; landing after it), contest_resolved -> " +
                            GameFlags.ContestResolvedFor(LegacyContestId) + "."
                          : " (the day of the trial, which is where that day now lives).") +
                      " No wall course, material or vocation score was touched, and no flag was cleared.");
        }

        /// <summary>
        /// Warns if stages.json has moved the trial away from the day this migration hardcodes.
        /// Deliberately a warning and deliberately not a lookup: the mapping must stay fixed, but
        /// the day it stops matching the shipped season is the day someone should be told, and the
        /// only person who can be told is whoever changed the table.
        /// </summary>
        static void TrialConsistencyCheck()
        {
            StageDef[] stages = GameData.Stages;
            if (stages == null || stages.Length == 0)
            {
                return;
            }

            for (int i = 0; i < stages.Length; i++)
            {
                StageDef stage = stages[i];
                if (stage != null && stage.reveals_page)
                {
                    if (stage.day != TrialStageDay)
                    {
                        Debug.LogWarning(
                            "[SaveSystem] stages.json now reveals the page on day " + stage.day +
                            ", but the schema 2 -> 3 migration lands old saves on day " + TrialStageDay +
                            ". The mapping is frozen on purpose; if the trial has genuinely moved, that " +
                            "is a new schema step, not an edit to this one.");
                    }

                    return;
                }
            }
        }
    }
}
