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
        /// <summary>
        /// Build: 0 or 1. It comes from the chosen character, not from the wardrobe — the only
        /// catalogue item that may write it is a <c>base</c> item, which declares a build and
        /// nothing else. Stored unpacked; see <see cref="BodyArtVariant"/> for the sprite index.
        /// </summary>
        public int body;

        /// <summary>
        /// Skin tone: 0..<see cref="SkinTones"/>-1. The one layer here that belongs to the person
        /// playing rather than to the character or to anything worn, and the reason the character
        /// supplies a build and never a tone. Two fixed characters would otherwise mean two fixed
        /// tones, which is not a thing the choice of character was ever meant to decide.
        ///
        /// Nothing in the catalogue may write this layer. A character floor that carried a tone
        /// would silently replace the player's choice on every recomposition, so
        /// <see cref="SheepGate.Player.CharacterPresets.ApplyTo"/> puts it back after composing —
        /// belt and braces over content that is authored not to name it in the first place.
        /// </summary>
        public int skin;

        // The four worn layers, each an index into that layer's art variants. The upper bounds are
        // deliberately not written here: the counts live in SheepGate.Art.CharacterArt
        // (HairVariants, TopVariants, LegsVariants, AccessoryVariants) and CharacterAppearance
        // clamps against them. A bound copied into a comment here is a bound that goes stale the
        // next time a shape is drawn, and a stale one reads as a rule.
        public int hair;
        public int top;
        public int legs;
        public int accessory;

        /// <summary>
        /// Build and skin share one sprite, so they share one art variant: build * SkinTones + skin.
        /// Packing them keeps the existing body_{variant}_{dir}_{anim}_{frame} key format untouched,
        /// which matters because that format is parsed in the art library and spelled out by hand in
        /// the world's fallback lookups.
        /// </summary>
        public const int SkinTones = 4;

        /// <summary>
        /// The packed body sprite index: build 0 owns variants 0..3, build 1 owns 4..7, and the
        /// remainder is the tone. The art unpacks it the other way round (variant / SkinCount is
        /// the build, variant % SkinCount is the tone), so the two halves recombine without either
        /// side storing a packed value — which is what lets a character declare a build while the
        /// player keeps the tone.
        /// </summary>
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
        /// The shape this build writes. 1 was rubble-only; 2 is stone/timber/blocks; 3 is the
        /// nine-stage season, in which <see cref="day"/> stopped meaning "one of three" and started
        /// meaning "one of nine"; 4 is the short day, in which <see cref="workCapacityMax"/> stopped
        /// being the size of the crew and became the courses one person lays before dusk. Bump it
        /// in the same change that adds a migration to SaveSystem, never on its own.
        /// </summary>
        public const int CurrentSchemaVersion = 4;

        /// <summary>
        /// Courses a day holds, and therefore the day's clock: the light reaches dusk when this
        /// many work units have been spent. Four, because four is what one morning's material makes
        /// — four timber piles, a block each — so a day that gathered everything ends on its own
        /// with nothing left over. It was twelve, which was also the size of the night crew, and
        /// the two numbers had nothing to do with each other: with twelve the material ran out at
        /// four and the day never ended by itself again after the first one. The crew is
        /// <c>DayCycle.CrewSize</c> now, and it is still twelve.
        /// </summary>
        public const int DefaultWorkCapacityMax = 4;

        /// <summary>
        /// Which stage of the season the run is on, and it IS the stage number: there is no second
        /// progression axis anywhere in this project. 1..9 today, and the real bound is whatever
        /// stages.json declares, which is why nothing here hardcodes the length.
        ///
        /// Everything already hangs off this one monotonic int — the calendar, the dialogue
        /// selector, the quiz selector, the map node, and the daily-reset token that ResourceSystem,
        /// RubblePile and WallSystem each compare against. That is the reason a stage is a day
        /// rather than a thing beside one.
        /// </summary>
        public int day = 1;

        /// <summary>
        /// Which season's content this run belongs to. Stamped by <see cref="NewGame"/> and never
        /// branched on by the game itself; the one decision it drives is in BootSequence, which
        /// keeps a save from a season it does not recognise rather than discarding it.
        ///
        /// It is here now, folded into a schema bump the day numbering already required, purely so
        /// that a second season does not have to spend a schema bump of its own on one string.
        /// The field initializer means every save ever written already reads back as this season,
        /// which is correct — there has only been one.
        /// </summary>
        public string seasonId = DefaultSeasonId;

        /// <summary>The only season that exists. Season 2 is explicitly out of scope.</summary>
        public const string DefaultSeasonId = "nehemiah";

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
        public int workCapacityMax = DefaultWorkCapacityMax;
        public int morale = 100;
        public AppearanceState appearance = new AppearanceState();
        public string playerName = "";

        /// <summary>
        /// Which character this run is: a preset id from <c>Resources/Data/character_presets.json</c>
        /// — <c>adar</c> or <c>neriah</c> — written once, when creation is confirmed.
        ///
        /// Purely additive, like the two wardrobe lists below. A save written before this field
        /// existed simply has no such key, so it deserializes onto the initializer and the run keeps
        /// the character it always had: nothing to migrate, no entry in SaveSystem.Repair, and no
        /// step that could run twice.
        ///
        /// <b>Empty is a legitimate answer, and it means "not known".</b> It is what a save from
        /// before this field carries, and what a run that never passed through creation carries.
        /// Nothing here backfills it. The look could be read backwards to guess a character, and
        /// that is exactly what must not happen: a guess would record an identity the player never
        /// chose, and the save is the one place that distinction is still legible.
        ///
        /// So consumers read it defensively and degrade rather than assume. Treat null and empty
        /// alike — the field round-trips through Newtonsoft, and a hand-edited save can carry an
        /// explicit null however the initializer is written — and where the answer is needed but
        /// missing, fall back the way <see cref="SheepGate.Player.Wardrobe.CharacterId"/> does
        /// today: infer from the signature piece being worn, and protect no silhouette anchor when
        /// even that says nothing.
        ///
        /// It is an id and not a <see cref="SheepGate.Player.PresetDef"/> on purpose. The presets
        /// file is reloaded on every language switch, so a resolved object in the save would be a
        /// reference into a table that no longer exists; the id survives that, and survives a
        /// character being re-authored between builds.
        /// </summary>
        public string characterId = "";

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

        // ---------------------------------------------------------------------- daily check-in
        //
        // A calendar-day reward, independent of `day` (the in-fiction day, 1..3). lastCheckInDate is
        // the device's local date in "yyyy-MM-dd" — see DailyCheckIn for the read/write logic. All
        // three fields are purely additive: a save written before this feature existed carries none
        // of them and loads them at their zero value, so no schemaVersion bump is needed (the same
        // reasoning as equippedItems/seenItems above).

        /// <summary>Local date of the last awarded check-in, "yyyy-MM-dd", or empty before the first one.</summary>
        public string lastCheckInDate = "";

        /// <summary>Consecutive calendar days checked in. Resets to 1 (not 0) on any gap greater than one day.</summary>
        public int checkInStreak;

        /// <summary>Cosmetic-only currency awarded by the daily check-in. Never spent by anything in this build.</summary>
        public int talents;

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
            state.seasonId = DefaultSeasonId;
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
    /// Every flag name the game writes. Constants exist so no system spells a flag by hand.
    /// Values are lowercase snake_case and are what lands in the save file.
    ///
    /// Not quite every one: <see cref="ScriptureVisibility.RevealedFlag"/> is a flag in this same
    /// namespace and lives on that class instead, because the rule about when references appear is
    /// that class's whole subject and splitting the name from the rule would be worse than the
    /// inconsistency. This doc is the pointer, so the list can be read as complete again.
    ///
    /// Three families are computed rather than constant, because their count follows the stage
    /// table rather than being fixed. THE VALUES ARE UNCHANGED where they overlap: WatchPostedForDay
    /// reproduces the two legacy spellings byte for byte, which is why a longer season needed no
    /// key rename and no save migration for flags at all.
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

        /// <summary>
        /// A watch was posted on the night of this stage. Equals <see cref="WatchPostedD1"/> and
        /// <see cref="WatchPostedD2"/> exactly for days 1 and 2, which is deliberate and is what
        /// makes this migration-free: those two constants stay valid, the acceptance harness keeps
        /// reading them symbolically, and no persisted key is renamed.
        ///
        /// It exists because the write side used to set one of two flags and write nothing on any
        /// later day, so every night after the second left no record and the following morning
        /// reported "no watch" as fact — silently, and for most of a nine-stage season.
        /// </summary>
        public static string WatchPostedForDay(int day)
        {
            return "watch_posted_d" + day;
        }

        /// <summary>
        /// This contest has been fought. Keyed by contest id, because a season with two encounters
        /// cannot share one boolean: the flat <see cref="ContestResolved"/> would short-circuit the
        /// second contest with the first one's ending, which is a failure that logs nothing and
        /// looks like a design decision.
        /// </summary>
        public static string ContestResolvedFor(string contestId)
        {
            return "contest_resolved_" + contestId;
        }

        /// <summary>
        /// Counter key, not a flag: how many times this stage has been played to its end. Keyed by
        /// stage id rather than by day so it survives the day a stage moves in the running order.
        /// </summary>
        public static string StageDoneCounter(string stageId)
        {
            return "stage_done_" + stageId;
        }
    }
}
