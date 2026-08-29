using System;
using System.Collections.Generic;
using UnityEngine;

namespace SheepGate.Core
{
    /// <summary>How a person is posted for the night when the day is split.</summary>
    public enum Assignment
    {
        Work,
        Watch
    }

    /// <summary>Runtime progress of one wall segment. Stage 4 means complete.</summary>
    [Serializable]
    public class WallSegmentState
    {
        public string id;
        public int stage;          // 0..4, 4 == complete
        public int workInStage;    // work units accumulated toward the next stage
        public bool damaged;
    }

    /// <summary>Layered look chosen in character creation. Indices into the art library slots.</summary>
    [Serializable]
    public class AppearanceState
    {
        public int body;       // 0..1  build
        public int skin;       // 0..3  skin tone
        public int hair;       // 0..3  hair
        public int top;        // 0..3
        public int legs;       // 0..3
        public int accessory;  // 0..3

        /// <summary>
        /// Build and skin share one sprite, so they share one art variant: build * SkinTones + skin.
        /// Packing them keeps the existing body_{variant}_{dir}_{anim}_{frame} key format untouched,
        /// which matters because that format is parsed in the art library and spelled out by hand in
        /// the world's fallback lookups.
        /// </summary>
        public const int SkinTones = 4;

        public int BodyArtVariant
        {
            get { return Mathf.Clamp(body, 0, 1) * SkinTones + Mathf.Clamp(skin, 0, SkinTones - 1); }
        }
    }

    /// <summary>
    /// Single source of truth for the run. Serialized whole by SaveSystem, so every member here
    /// must round-trip through Newtonsoft.Json.
    /// </summary>
    [Serializable]
    public class GameState
    {
        public int day = 1;                                   // 1..3
        public int rubble;                                    // collected rubble units
        public int workCapacity;                              // resets each morning
        public int workCapacityMax = 12;
        public int morale = 100;
        public AppearanceState appearance = new AppearanceState();
        public string playerName = "";
        public List<WallSegmentState> segments = new List<WallSegmentState>();
        public Dictionary<string, int> vocationScores = new Dictionary<string, int>();
        public HashSet<string> flags = new HashSet<string>();
        public Dictionary<string, int> counters = new Dictionary<string, int>();
        public int watchAssigned;                             // people posted to watch last night
        public int workAssigned;

        public bool HasFlag(string flag)
        {
            if (string.IsNullOrEmpty(flag) || flags == null)
            {
                return false;
            }

            return flags.Contains(flag);
        }

        public void SetFlag(string flag)
        {
            if (string.IsNullOrEmpty(flag))
            {
                return;
            }

            if (flags == null)
            {
                flags = new HashSet<string>();
            }

            flags.Add(flag);
        }

        public int Counter(string key)
        {
            if (string.IsNullOrEmpty(key) || counters == null)
            {
                return 0;
            }

            return counters.TryGetValue(key, out var value) ? value : 0;
        }

        public void Bump(string key, int amount = 1)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (counters == null)
            {
                counters = new Dictionary<string, int>();
            }

            counters.TryGetValue(key, out var current);
            counters[key] = current + amount;
        }

        /// <summary>Returns the segment with this id, or null when the run has no such segment.</summary>
        public WallSegmentState Segment(string id)
        {
            if (string.IsNullOrEmpty(id) || segments == null)
            {
                return null;
            }

            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment != null && segment.id == id)
                {
                    return segment;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds the day-1 state, seeding one WallSegmentState per definition in wall_segments.json.
        /// Requires GameData.LoadAll() to have run first; an empty definition set only warns.
        /// </summary>
        public static GameState NewGame()
        {
            var state = new GameState();
            state.workCapacity = state.workCapacityMax;

            var definitions = GameData.WallSegments;
            if (definitions == null || definitions.Length == 0)
            {
                Debug.LogWarning("[GameState] No wall segment definitions available; starting with an empty wall. Check Resources/Data/wall_segments.json.");
                return state;
            }

            for (var i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                if (definition == null || string.IsNullOrEmpty(definition.id))
                {
                    Debug.LogError("[GameState] Skipping a wall segment definition with no id in wall_segments.json.");
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

            return state;
        }
    }

    /// <summary>
    /// Every flag name the POC writes. Constants exist so no system spells a flag by hand.
    /// Values are lowercase snake_case and are what lands in the save file.
    /// </summary>
    public static class GameFlags
    {
        public const string WatchPostedD1 = "watch_posted_d1";
        public const string WatchPostedD2 = "watch_posted_d2";
        public const string AcceptedInvite = "accepted_invite";
        public const string RefusedInvite = "refused_invite";
        public const string PageShown = "page_shown";
        public const string PageSkipped = "page_skipped";
        public const string FishCaught = "fish_caught";
        public const string ChapterOpened = "chapter_opened";
        public const string DeepRead = "deep_read";
        public const string ContestResolved = "contest_resolved";
        public const string VocationRevealed = "vocation_revealed";
        public const string ReachedMapEdge = "reached_map_edge";
    }
}
