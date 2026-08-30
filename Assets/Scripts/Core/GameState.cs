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
        /// <summary>
        /// Shape of this save file. 0 means it was written before the material split, when stone
        /// was the only material and was called rubble. <see cref="SaveSystem"/> stamps it forward
        /// on load; nothing else writes it.
        /// </summary>
        public int schemaVersion;

        /// <summary>
        /// The shape this build writes. 1 was rubble-only; 2 is stone/timber/blocks. Bump it in the
        /// same change that adds a migration to SaveSystem, never on its own.
        /// </summary>
        public const int CurrentSchemaVersion = 2;

        public int day = 1;                                   // 1..3

        // ---------------------------------------------------------------------- materials
        //
        // The wall is built from blocks, and a block is crafted from stone and timber
        // (ResourceSystem.StonePerBlock and TimberPerBlock hold the recipe):
        //
        //     stone + timber  ->  block  ->  wall
        //
        // Day 1 hands out stone only and timber appears on day 2, but that is pacing, not model:
        // nothing here gates a material by day. Work capacity is not a material - it is the daily
        // action budget, and it lives below with its own maximum.
        //
        // "rubble" is what stone was called before the split, and it is still the field that
        // carries stone through the save file. `stone` is a property over that same storage, which
        // is deliberate and does two jobs at once:
        //   - a save written before the split has "rubble": n and no "stone", so reading it through
        //     the shared storage converts it by construction. There is nothing to move, nothing to
        //     lose, and no conversion that could run a second time and double or drop a unit -
        //     which is rule 7 (progress already made never regresses) held by the data model
        //     rather than by a migration everyone has to remember to get right.
        //   - systems this change does not own still read and write state.rubble directly: the HUD
        //     readout, the steward scoring in DayCycle, the requires_rubble dialogue branch, and
        //     NpcActor's donation fallback. One storage keeps all of them exact. A separate field
        //     kept in sync would only be as correct as the last person who remembered to sync it.
        // When those callers have moved to `stone`, rename the field and drop the property - and
        // see the note in SaveSystem.MigrateSchema, which has to gain a real copy step that day.

        /// <summary>
        /// Stone the player is carrying, under the name the save file has always used. This is the
        /// storage behind <see cref="stone"/>; new code should say <c>stone</c>.
        /// </summary>
        public int rubble;

        /// <summary>Stone the player is carrying. Aliases <see cref="rubble"/>; see the note above.</summary>
        public int stone
        {
            get { return rubble; }
            set { rubble = value; }
        }

        /// <summary>Timber the player is carrying. Day 1 has none, which is a pacing rule callers keep.</summary>
        public int timber;

        /// <summary>Blocks crafted from stone and timber. The wall is raised out of these.</summary>
        public int blocks;

        public int workCapacity;                              // resets each morning
        public int workCapacityMax = 12;
        public int morale = 100;
        public AppearanceState appearance = new AppearanceState();
        public string playerName = "";

        // ---------------------------------------------------------------------- the wardrobe
        //
        // Two lists, both purely additive: a save written before the backpack existed carries
        // neither key, so both keep their field initializer and load empty. Nothing else in the
        // save changes shape, and in particular AppearanceState keeps its six ints — those stay
        // the storage the renderer reads, and the wardrobe writes into them rather than beside
        // them. See SheepGate.Player.Wardrobe, which owns every read and write of both lists.

        /// <summary>
        /// Catalogue item ids currently worn, in the order they were put on. Order is not
        /// decoration: <see cref="SheepGate.Player.CharacterCatalog.Compose"/> applies items in
        /// the order it is handed them, so within one art layer the last one in this list is the
        /// one that draws.
        ///
        /// Empty is a valid, ordinary state — it means the look came from character creation
        /// rather than from a catalogue item, not that the player is undressed.
        /// </summary>
        public List<string> equippedItems = new List<string>();

        /// <summary>
        /// Item ids whose "new" badge has been spent. An id lands here when the player has
        /// actually looked at the slot holding it; it never leaves, and an item is never marked
        /// here while it is still locked — that would spend the badge before the player could
        /// ever see it. <see cref="SheepGate.Player.Wardrobe.MarkSeen"/> holds both rules.
        /// </summary>
        public List<string> seenItems = new List<string>();
        public List<WallSegmentState> segments = new List<WallSegmentState>();
        public Dictionary<string, int> vocationScores = new Dictionary<string, int>();
        public HashSet<string> flags = new HashSet<string>();
        public Dictionary<string, int> counters = new Dictionary<string, int>();
        // Where the player is standing. Unset until they have moved, so a fresh run still starts
        // at the map's spawn point. It is here rather than in the scene because the scene is
        // rebuilt from scratch on a language switch, and being put back at the village entrance
        // is not what "change the language" is supposed to mean.
        public int playerCellX = -1;
        public int playerCellY = -1;

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
            state.schemaVersion = CurrentSchemaVersion;
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
