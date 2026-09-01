using System;
using System.Collections.Generic;
using SheepGate.Core;
using UnityEngine;

namespace SheepGate.Vocation
{
    /// <summary>
    /// The six archetypes the POC can reveal. Ids match the "id" field in vocations.json and are
    /// the only form that circulates in code; the display name and the reveal line are authored
    /// content and are read from that file at reveal time.
    /// </summary>
    public static class VocationIds
    {
        public const string Zealot = "zelote";
        public const string Scribe = "escriba";
        public const string Shepherd = "pastor";
        public const string Exile = "exilado";
        public const string Prophet = "profeta";
        public const string Steward = "mordomo";
    }

    /// <summary>
    /// Accumulates vocation points in silence and names the winner exactly once, at the end of the
    /// stage that declares <c>reveals_vocation</c> in stages.json — the last one in the season.
    ///
    /// The stage is named that way rather than by number on purpose. These sentences were the only
    /// written statement anywhere of when the beat fires, they said "day three", and they were
    /// wrong the moment the season grew past three stages; nothing in this class reads a day, so a
    /// number here can go stale without a single line of code disagreeing with it.
    ///
    /// THE RULE THIS CLASS EXISTS TO ENFORCE: no score may reach any UI before the reveal. There
    /// is deliberately no public getter for a score, no "points remaining", no count of anything.
    /// If a screen could read progress, the discovery would turn into a checklist and the whole
    /// mechanic would be worth nothing. <see cref="Resolve"/> is the only way out, it returns a
    /// single id, and the score map it reports goes to telemetry and nowhere else.
    ///
    /// The tracker holds no state of its own: every call reads and writes
    /// <see cref="GameState.vocationScores"/> through the currently registered state, so points
    /// survive a save/load round trip and a second tracker instance cannot fork the totals.
    /// </summary>
    public class VocationTracker
    {
        /// <summary>Registers a tracker if none exists yet and returns the one in use.</summary>
        public static VocationTracker EnsureRegistered()
        {
            VocationTracker existing;
            try
            {
                if (ServiceLocator.TryGet(out existing) && existing != null)
                {
                    return existing;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Vocation] Could not read the service registry: " + exception.Message);
            }

            var tracker = new VocationTracker();
            try
            {
                ServiceLocator.Register(tracker);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Vocation] Could not register the tracker: " + exception.Message);
            }

            return tracker;
        }

        /// <summary>
        /// Adds points to one vocation. Silent by contract: nothing is logged about the amount,
        /// nothing is raised, no UI is notified. A negative or zero amount is ignored, because no
        /// action in the POC takes points away from the player.
        /// </summary>
        public void Add(string vocationId, int points)
        {
            if (string.IsNullOrEmpty(vocationId) || points <= 0)
            {
                return;
            }

            GameState state = ResolveState();
            if (state == null)
            {
                Debug.LogWarning("[Vocation] No GameState is registered; a grant was dropped.");
                return;
            }

            if (state.vocationScores == null)
            {
                state.vocationScores = new Dictionary<string, int>();
            }

            if (!IsKnown(vocationId))
            {
                // Content bug, not a player-visible one: the id is named, the amount never is.
                Debug.LogWarning("[Vocation] Unknown vocation id '" + vocationId + "'; check vocations.json.");
            }

            int current;
            state.vocationScores.TryGetValue(vocationId, out current);
            state.vocationScores[vocationId] = current + points;
        }

        /// <summary>
        /// Names the vocation with the highest score. Ties break by the order of vocations.json,
        /// which is why the comparison is strictly greater: the first definition to hold the top
        /// score keeps it. Returns null only when there is no content to choose from.
        ///
        /// Reporting is one-shot: the first call raises vocation_revealed with the full score map
        /// and raises the flag; later calls return the same id in silence.
        /// </summary>
        public string Resolve()
        {
            GameState state = ResolveState();
            Dictionary<string, int> scores = state != null && state.vocationScores != null
                ? state.vocationScores
                : new Dictionary<string, int>();

            string winner = Pick(scores);
            if (winner == null)
            {
                Debug.LogWarning("[Vocation] No vocation could be resolved; vocations.json looks empty.");
                return null;
            }

            bool alreadyReported = state != null && state.HasFlag(GameFlags.VocationRevealed);
            if (alreadyReported)
            {
                return winner;
            }

            if (state != null)
            {
                state.SetFlag(GameFlags.VocationRevealed);
            }

            Telemetry.Track(TelemetryEvents.VocationRevealed, new Dictionary<string, object>
            {
                { "vocation", winner },
                { "scores", new Dictionary<string, int>(scores) }
            });

            return winner;
        }

        /// <summary>
        /// Walks the authored order and keeps the first id holding the top score. Falls back to
        /// the recorded scores when no definitions loaded, so a content failure still names
        /// something instead of leaving the closing stage without an ending.
        /// </summary>
        static string Pick(IDictionary<string, int> scores)
        {
            string best = null;
            int bestScore = int.MinValue;

            VocationDef[] definitions = SafeDefinitions();
            for (int i = 0; i < definitions.Length; i++)
            {
                VocationDef definition = definitions[i];
                if (definition == null || string.IsNullOrEmpty(definition.id))
                {
                    continue;
                }

                int score;
                if (!scores.TryGetValue(definition.id, out score))
                {
                    score = 0;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = definition.id;
                }
            }

            if (best != null)
            {
                return best;
            }

            foreach (KeyValuePair<string, int> pair in scores)
            {
                if (string.IsNullOrEmpty(pair.Key))
                {
                    continue;
                }

                if (pair.Value > bestScore)
                {
                    bestScore = pair.Value;
                    best = pair.Key;
                }
            }

            return best;
        }

        static bool IsKnown(string vocationId)
        {
            VocationDef[] definitions = SafeDefinitions();
            if (definitions.Length == 0)
            {
                // Nothing loaded yet: assume the id is fine rather than crying wolf.
                return true;
            }

            for (int i = 0; i < definitions.Length; i++)
            {
                VocationDef definition = definitions[i];
                if (definition != null && definition.id == vocationId)
                {
                    return true;
                }
            }

            return false;
        }

        static VocationDef[] SafeDefinitions()
        {
            try
            {
                VocationDef[] definitions = GameData.Vocations;
                return definitions ?? Array.Empty<VocationDef>();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Vocation] Could not read the vocation definitions: " + exception.Message);
                return Array.Empty<VocationDef>();
            }
        }

        static GameState ResolveState()
        {
            GameState state;
            try
            {
                if (ServiceLocator.TryGet(out state) && state != null)
                {
                    return state;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Vocation] Could not read the game state: " + exception.Message);
            }

            return null;
        }
    }
}
