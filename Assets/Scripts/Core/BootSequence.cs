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
            var telemetryPath = Path.Combine(Application.persistentDataPath, TelemetryFileName);
            Telemetry.Initialize(new JsonlFileSink(telemetryPath));

            // Logged because persistentDataPath differs between the editor and a player build, and
            // a playtest whose telemetry cannot be found is a playtest that measured nothing.
            Debug.Log("[Boot] Telemetry -> " + telemetryPath);
            Debug.Log("[Boot] Save -> " + SaveSystem.SavePath);

            GameData.LoadAll();
            LoadScripture();

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
                { "day", state.day }
            });
            Telemetry.Flush();

            Debug.Log("[Boot] Ready. Day " + state.day + ", " + (freshStart ? "new run." : "resumed run."));

            // Always the village. Character creation is no longer a screen in front of the game:
            // it is a beat inside the opening, played in the house the neighbour walks you into, so
            // the first thing a new player sees is a city rather than a menu. The CharacterCreation
            // scene stays in the build for the standalone route and for anyone who needs to reach
            // the wardrobe without replaying the opening.
            SceneManager.LoadScene(SceneGame);
        }

        static void LoadScripture()
        {
            // A scripture failure must not stop the boot: the reader degrades to a visible
            // missing-text marker, which is the behaviour ScriptureService already guarantees.
            try
            {
                ScriptureService.Load();
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
