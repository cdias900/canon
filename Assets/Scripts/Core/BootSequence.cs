using System;
using System.Collections.Generic;
using System.IO;
using SheepGate.Player;
using SheepGate.Scripture;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SheepGate.Core
{
    /// <summary>
    /// Everything the Boot scene does. The scene itself holds only a camera and one bootstrap
    /// behaviour that calls Run(); all real construction happens here and in the scene composers,
    /// where the compiler can check it.
    /// </summary>
    public static class BootSequence
    {
        const string TelemetryFileName = "telemetry.jsonl";
        const string SceneCharacterCreation = "CharacterCreation";
        const string SceneGame = "Game";

        public static void Run()
        {
            var telemetryPath = Path.Combine(AppPaths.DataRoot, TelemetryFileName);
            Telemetry.Initialize(new JsonlFileSink(telemetryPath));

            // Logged because persistentDataPath differs between the editor and a player build, and
            // a playtest whose telemetry cannot be found is a playtest that measured nothing.
            Debug.Log("[Boot] Telemetry -> " + telemetryPath);
            Debug.Log("[Boot] Save -> " + SaveSystem.SavePath);

            // The locale is resolved before any content is read, because every content path
            // depends on it. Nothing loaded before this point can hold a player-facing string.
            ApplyLocale(Locales.Active);

            var state = SaveSystem.HasSave() ? SaveSystem.Load() : null;
            var freshStart = state == null;

            if (freshStart)
            {
                state = GameState.NewGame();
            }
            else
            {
                ReconcileSegments(state);
                AdoptSeason(state);
            }

            ServiceLocator.Clear();
            ServiceLocator.Register(state);

            Telemetry.Track(TelemetryEvents.SessionStart, new Dictionary<string, object>
            {
                { "day", state.day },

                // The stage's id, not its number — the number is already here as "day", because a
                // stage IS a day. What a funnel needs is the name: "which stage converts" is a
                // question about enemies_rise and the_dedication, and answering it from an integer
                // means every query has to carry a copy of the stage table and stay in step with it.
                //
                // Split by stage for the same reason the line below splits by locale. A nine-stage
                // season measured as one number cannot say where people stop, and where they stop
                // is the only thing that tells us which invitation is working.
                { "stage", GameData.Stage(state.day).id },

                // Carried so deep_read can be read per language. A conversion rate that is not
                // split by locale cannot say whether a translation is working.
                { "locale", Locales.Active }
            });
            Telemetry.Flush();

            // Brought up here rather than on the first sound, so the ambient bed is already
            // running when the opening fades in instead of arriving a beat late.
            SheepGate.Audio.AudioDirector.Ensure();

            Debug.Log("[Boot] Ready. Day " + state.day + ", " + (freshStart ? "new run." : "resumed run.") +
                      " Locale " + Locales.Active + "." +
                      " Audio " + (SheepGate.Audio.AudioDirector.Suppressed ? "suppressed" :
                                   "music " + (SheepGate.Audio.AudioDirector.MusicMuted ? "off" : "on") +
                                   ", effects " + (SheepGate.Audio.AudioDirector.EffectsMuted ? "off" : "on")) + ".");

            // Always the village. Character creation is no longer a screen in front of the game:
            // it is a beat inside the opening, played in the house the neighbour walks you into, so
            // the first thing a new player sees is a city rather than a menu. The CharacterCreation
            // scene stays in the build for the standalone route and for anyone who needs to reach
            // the wardrobe without replaying the opening.
            SceneManager.LoadScene(SceneGame);
        }

        /// <summary>
        /// Points every content system at a locale and rereads it. Call before anything that can
        /// put a string on screen; the boot sequence does so before it loads a save.
        ///
        /// Reading the same files twice is cheap and the alternative — systems that each remember
        /// which locale they were built for — is the kind of state that goes stale silently.
        /// </summary>
        public static void ApplyLocale(string locale)
        {
            Locales.SetActive(locale, false);
            Loc.Reload();
            GameData.LoadAll(Locales.Active);
            CharacterCatalog.LoadAll(Locales.Active);

            // After the catalogue, never before it: the presets resolve their default items against
            // it, and CharacterPresets.VerifyAgainstCatalog is silent when the catalogue is empty.
            // Loaded here rather than left to the first screen that asks, because the presets carry
            // player-facing strings — a character's name and personality line — and a lazy load at
            // the first Wardrobe call would leave them on whichever locale happened to be active
            // when that call ran, not this one.
            CharacterPresets.LoadAll(Locales.Active);

            LoadScripture();

            // Last, because it is the one cross-file check that needs both of the loads above.
            VerifyStageRewards();
        }

        /// <summary>
        /// Switches language for good: remembers the choice, rereads the content, and reloads the
        /// current scene so the new strings are on screen.
        ///
        /// The scene is reloaded rather than re-texted in place because every label in this project
        /// is constructed at runtime and nothing holds a reference back to the key that produced it.
        /// Rebuilding is the only way to be sure nothing was missed. The run itself is untouched:
        /// the save is on disk and the boot sequence resumes it, so switching language costs the
        /// player nothing.
        /// </summary>
        public static void SwitchLocale(string locale)
        {
            string canonical = Locales.Canonical(locale);
            if (canonical == null || canonical == Locales.Active)
            {
                return;
            }

            Locales.SetActive(canonical);
            Telemetry.Track(TelemetryEvents.LocaleChanged, new Dictionary<string, object>
            {
                { "locale", canonical }
            });
            Telemetry.Flush();

            // Reloading the scene is NOT enough on its own. Only the Boot scene runs Run(); the
            // Game scene's bootstrap just composes, and Loc, GameData and ScriptureService are
            // statics that survive a scene load with the previous language still in them. Without
            // this call the scene rebuilds in the old words while Locales.Active reports the new
            // one, so the toggle lights up and nothing else changes.
            ApplyLocale(canonical);

            Debug.Log("[Boot] Locale -> " + canonical + "; reloading the scene.");
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        static void LoadScripture()
        {
            // A scripture failure must not stop the boot: the reader degrades to a visible
            // missing-text marker, which is the behaviour ScriptureService already guarantees.
            try
            {
                ScriptureService.Reload();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Boot] Could not load the scripture index: " + exception.Message);
            }
        }

        /// <summary>
        /// Decides what to do with the season a resumed save says it belongs to. There are exactly
        /// three answers and none of them is "throw the save away".
        ///
        /// A save with no season named is stamped with this one. That is the ordinary case for
        /// every save written before the field existed, and it is safe because there has only ever
        /// been one season for it to have belonged to.
        ///
        /// A save naming this season proceeds, silently, which is the path every run takes.
        ///
        /// A save naming a season this build does not have is <b>kept and resumed as it is</b>, and
        /// the mismatch is logged loudly for whoever is holding the build. It is deliberately not
        /// re-stamped: overwriting the name would be this build claiming a run it cannot read, and
        /// the next save would make that claim permanent. It is deliberately not deleted either —
        /// no game over and no lost progress means the file survives even when the build cannot
        /// make full sense of it. The stage clamp in SaveSystem already keeps the day inside
        /// something playable, so the worst case is a run that resumes at a stage this season does
        /// have, with everything the player earned still in the file.
        /// </summary>
        static void AdoptSeason(GameState state)
        {
            if (string.IsNullOrEmpty(state.seasonId))
            {
                state.seasonId = GameState.DefaultSeasonId;
                Debug.Log("[Boot] The save named no season; stamping it \"" + GameState.DefaultSeasonId + "\".");
                return;
            }

            if (state.seasonId == GameState.DefaultSeasonId)
            {
                return;
            }

            Debug.LogWarning(
                "[Boot] This save belongs to season \"" + state.seasonId + "\", which this build does not have. " +
                "Keeping it and resuming anyway — a save is never discarded — but its content will be read " +
                "against season \"" + GameState.DefaultSeasonId + "\".");
        }

        /// <summary>
        /// The one stage-table invariant that cannot live in GameData: it needs the catalogue, and
        /// the catalogue is loaded a line after GameData is. Checking it here keeps it a load-time
        /// assertion — which is what the plan asks for — instead of a check nobody ever calls.
        ///
        /// A stage's reward_item is the item the progression map FEATURES, so pointing at an entry
        /// that does not exist is not a crash; it is a blank space on the map where a reason to
        /// come back tomorrow was supposed to be. That is exactly the class of miss that ships.
        /// </summary>
        static void VerifyStageRewards()
        {
            StageDef[] stages = GameData.Stages;
            if (stages == null)
            {
                return;
            }

            for (int i = 0; i < stages.Length; i++)
            {
                StageDef stage = stages[i];
                if (stage == null || string.IsNullOrEmpty(stage.reward_item))
                {
                    continue;
                }

                if (CharacterCatalog.Item(stage.reward_item) == null)
                {
                    Debug.LogError(
                        "[Boot] Stage \"" + stage.id + "\" features reward item \"" + stage.reward_item +
                        "\", which character_catalog.json does not define.");
                }
            }
        }

        /// <summary>
        /// Adds a runtime state for any segment defined in wall_segments.json that the save does not
        /// know about, so content added after a save was written still shows up. Existing segments
        /// are never removed and never reset: finished work does not regress.
        /// </summary>
        static void ReconcileSegments(GameState state)
        {
            if (state.segments == null)
            {
                state.segments = new List<WallSegmentState>();
            }

            var definitions = GameData.WallSegments;
            if (definitions == null)
            {
                return;
            }

            for (var i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                if (definition == null || string.IsNullOrEmpty(definition.id))
                {
                    continue;
                }

                if (state.Segment(definition.id) != null)
                {
                    continue;
                }

                state.segments.Add(new WallSegmentState
                {
                    id = definition.id,
                    stage = 0,
                    workInStage = 0,
                    damaged = false
                });
            }
        }
    }
}
