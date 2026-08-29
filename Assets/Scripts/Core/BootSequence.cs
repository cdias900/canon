using System;
using System.Collections.Generic;
using System.IO;
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
            }

            ServiceLocator.Clear();
            ServiceLocator.Register(state);

            Telemetry.Track(TelemetryEvents.SessionStart, new Dictionary<string, object>
            {
                { "day", state.day },

                // Carried so deep_read can be read per language. A conversion rate that is not
                // split by locale cannot say whether a translation is working.
                { "locale", Locales.Active }
            });
            Telemetry.Flush();

            Debug.Log("[Boot] Ready. Day " + state.day + ", " + (freshStart ? "new run." : "resumed run.") +
                      " Locale " + Locales.Active + ".");

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
            LoadScripture();
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
