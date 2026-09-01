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

            // Before anything is built, because the first thing on screen is an animation: the
            // opening fades and the camera zooms, and a player who asked their phone to reduce
            // motion should not have to sit through the one screen that ignored them.
            AccessibilityPreferences.Apply();

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

            // The cross-file audit, here because this is the only place both files are known to
            // have been read, in the order it needs: it compares character_presets.json against
            // character_catalog.json and names every field the two disagree on. Its own contract is
            // "call it once, after both loaders, not from either loader", and this line is that
            // call site — the one the wardrobe's lazy path was standing in for and could not reach
            // once the presets were already loaded here.
            //
            // It is deliberately not conditional and deliberately not counted. A clean pair of
            // content files logs nothing at all, so the cost on a healthy boot is a walk over two
            // characters, and running it again on every locale switch is the point: the files are
            // reread there too, and a check that only ever ran on the first read would stop
            // covering the language the player actually switched to.
            CharacterPresets.VerifyAgainstCatalog();

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

        /// <summary>
        /// The same switch, without the scene reload. Returns true when the language actually
        /// changed, so the caller knows whether it has anything to rebuild.
        ///
        /// <b>Why this exists.</b> Reloading is the right default — every label is built at runtime
        /// and nothing remembers the key that produced it — but there is one screen where the scene
        /// is not the player's context: character creation, which runs as a beat inside the opening
        /// cutscene. Reloading there restarts the cutscene from its first frame, and the opening
        /// cannot be skipped, so a player who switched language in the one place the toggle is
        /// guaranteed to be found paid for it by watching the whole opening again.
        ///
        /// The caller takes on the obligation the reload used to discharge: everything showing the
        /// old words has to be rebuilt. Only use it where that is a screen you own outright.
        /// </summary>
        public static bool SwitchLocaleInPlace(string locale)
        {
            string canonical = Locales.Canonical(locale);
            if (canonical == null || canonical == Locales.Active)
            {
                return false;
            }

            Locales.SetActive(canonical);
            Telemetry.Track(TelemetryEvents.LocaleChanged, new Dictionary<string, object>
            {
                { "locale", canonical }
            });
            Telemetry.Flush();

            ApplyLocale(canonical);

            Debug.Log("[Boot] Locale -> " + canonical + "; rebuilding in place.");
            return true;
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
