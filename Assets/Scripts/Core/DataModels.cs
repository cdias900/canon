using System.Collections.Generic;
using Newtonsoft.Json;

namespace SheepGate.Core
{
    // Authored content DTOs. Field names mirror the JSON keys under Resources/Data one to one,
    // which is why they are snake_case: the JSON is hand edited outside Unity and must stay readable.
    // None of these types ever carries scripture text. A DTO carries a reference such as NEH.4.6;
    // the literal text is resolved at runtime by SheepGate.Scripture.ScriptureService.

    /// <summary>Integer position on the tilemap grid.</summary>
    public class GridPos
    {
        [JsonProperty("x")] public int x;
        [JsonProperty("y")] public int y;
    }

    /// <summary>One resident of the village, from npcs.json.</summary>
    public class NpcDef
    {
        [JsonProperty("id")] public string id;
        [JsonProperty("display")] public string display;

        /// <summary>Reference that names this person in the text. Reference only, never text.</summary>
        [JsonProperty("source_ref")] public string source_ref;

        [JsonProperty("spawn")] public GridPos spawn;
        [JsonProperty("palette")] public string palette;

        /// <summary>
        /// The wall segment this builder repaired, for the record the ending draws — the one
        /// thing Nehemiah 3 does with every name it lists. Empty means the name goes on no
        /// stretch. Never the player's segment: that one carries the player's own name.
        /// </summary>
        [JsonProperty("segment")] public string segment;
    }

    /// <summary>
    /// One bubble in a dialogue node. A line carries either authored <c>text</c> or a scripture
    /// <c>verse</c> reference, never both. <c>frame</c> is the authored sentence that introduces
    /// the quotation and is only meaningful on a verse line.
    /// </summary>
    public class DialogueLine
    {
        [JsonProperty("text")] public string text;
        [JsonProperty("verse")] public string verse;
        [JsonProperty("frame")] public string frame;
    }

    /// <summary>Points and flags a dialogue node grants once it finishes playing.</summary>
    public class Grants
    {
        [JsonProperty("vocation")] public Dictionary<string, int> vocation;
        [JsonProperty("flags")] public Dictionary<string, int> flags;
        [JsonProperty("set_flag")] public string set_flag;
    }

    /// <summary>
    /// One branch offered when a node has been read to the end. Optional by construction: a node
    /// without choices ends on the next tap exactly as it did before branching existed.
    ///
    /// Every gate here is evaluated against <see cref="GameState"/> alone, so the dialogue layer
    /// never has to reach into the world to decide what to show.
    /// </summary>
    public class DialogueChoice
    {
        /// <summary>Authoring id, used in logs. Never shown to the player.</summary>
        [JsonProperty("id")] public string id;

        /// <summary>The sentence on the button, in the locale being played. The only field of this class a player sees.</summary>
        [JsonProperty("text")] public string text;

        /// <summary>Node played once this branch is taken. Empty simply ends the conversation.</summary>
        [JsonProperty("next")] public string next;

        /// <summary>Points and flags this branch grants, in the same shape a node uses.</summary>
        [JsonProperty("grants")] public Grants grants;

        /// <summary>Rubble the player must be carrying for the branch to be offered. 0 means always.</summary>
        [JsonProperty("requires_rubble")] public int requires_rubble;

        /// <summary>Branch disappears once this flag is raised. Empty means it never disappears.</summary>
        [JsonProperty("hidden_if_flag")] public string hidden_if_flag;
    }

    /// <summary>One conversation, keyed in dialogue.json by node id such as "hananias_d1".</summary>
    public class DialogueNode
    {
        [JsonProperty("npc")] public string npc;

        /// <summary>
        /// The season stage this node belongs to, and <b>0 is a sentinel, not a missing value</b>:
        /// it means "available at any stage". Eight shipped nodes rely on that reading — the
        /// gathering beats, the well and the page have no day of their own and are played by id
        /// rather than selected by day — and until now it was documented nowhere, so the obvious
        /// "fix" of treating 0 as day 1 would have silently retimed all of them.
        ///
        /// For the nodes that DO carry a day, the selector is
        /// <see cref="SheepGate.World.NpcActor"/>'s highest-node-with-day-&lt;=-today rule, which is
        /// also the coverage policy: a resident with nothing authored for today repeats their most
        /// recent line rather than going mute. Authoring a day band is therefore optional per
        /// resident per stage, by design.
        /// </summary>
        [JsonProperty("day")] public int day;

        [JsonProperty("lines")] public DialogueLine[] lines;
        [JsonProperty("grants")] public Grants grants;

        /// <summary>
        /// Work capacity this node costs the day once it has been read to the end: an hour given
        /// to somebody else's stretch. Zero for almost every node. Applied by the resident who
        /// speaks it (NpcActor), because capacity is the world's to move and never the dialogue
        /// layer's; the locale parity check holds it equal across languages like a grant.
        /// </summary>
        [JsonProperty("spend_work")] public int spend_work;

        /// <summary>Branches offered after the last line. Null or empty on a straight-through node.</summary>
        [JsonProperty("choices")] public DialogueChoice[] choices;

        /// <summary>Whether what this node claims is true. The game never shows this to the player.</summary>
        [JsonProperty("reliable")] public bool reliable;

        /// <summary>
        /// This node puts authored words in the mouth of a figure the text names and quotes —
        /// Sanballat, Tobiah, the governor. Rule 4 permits that; what it forbids is the authored
        /// line asserting an event, a motive or a theological claim the passage does not carry,
        /// and no script can decide that. Marking it is what routes the node to a human.
        ///
        /// God, Jesus and the Holy Spirit are outside this permission entirely and never speak in
        /// authored text, with no exception and no marking that would make it acceptable.
        /// </summary>
        [JsonProperty("canonical_speaker")] public bool canonical_speaker;

        /// <summary>
        /// This node is waiting for a human to read it against the passage. Printed by
        /// <c>node tools/list-curation.mjs</c>, which is the queue that read works from.
        ///
        /// Both this and <see cref="canonical_speaker"/> have been keys in the shipped JSON and in
        /// the tooling for some time, but the DTO had no members to bind them to, so Newtonsoft
        /// dropped them on load and the runtime could not see rule 4's marking at all. Nine stages
        /// put three canonical speakers into authored dialogue across five stages; the safeguard
        /// should at least be visible to the build that ships it.
        /// </summary>
        [JsonProperty("needs_curation")] public bool needs_curation;
    }

    /// <summary>Static definition of a wall segment, from wall_segments.json.</summary>
    public class WallSegmentDef
    {
        [JsonProperty("id")] public string id;
        [JsonProperty("grid_x")] public int grid_x;

        /// <summary>Work units required per stage. Four entries, one per stage.</summary>
        [JsonProperty("stage_cost")] public int[] stage_cost;

        [JsonProperty("exposed")] public bool exposed;
    }

    /// <summary>One vocation archetype, from vocations.json.</summary>
    public class VocationDef
    {
        [JsonProperty("id")] public string id;
        [JsonProperty("display")] public string display;

        /// <summary>Authored line shown at the reveal. Merged in from the locale, never scripture.</summary>
        [JsonProperty("reveal_line")] public string reveal_line;
    }

    /// <summary>One daily check-in question, from quiz.json.</summary>
    public class QuizQuestion
    {
        [JsonProperty("day")] public int day;
        [JsonProperty("prompt")] public string prompt;
        [JsonProperty("options")] public string[] options;

        /// <summary>Index into <c>options</c>.</summary>
        [JsonProperty("answer")] public int answer;

        [JsonProperty("note")] public string note;

        /// <summary>Tomorrow, in one line, merged in from the locale. Empty on the last day.</summary>
        [JsonProperty("hook")] public string hook;
    }

    /// <summary>The single village map, from map.json. <c>rows</c> is one string per grid row.</summary>
    public class MapDef
    {
        [JsonProperty("width")] public int width;
        [JsonProperty("height")] public int height;
        [JsonProperty("rows")] public string[] rows;
        [JsonProperty("player_spawn")] public GridPos player_spawn;

        /// <summary>
        /// Cells that yield stone. Named "rubble" because that is what the file has always called
        /// them and map.json is content other people edit; the material they hand out is stone.
        /// </summary>
        [JsonProperty("rubble")] public GridPos[] rubble;

        /// <summary>
        /// Cells that yield timber, the second half of the block recipe. Optional: a map written
        /// before the material split simply has none, and the village is stone-only.
        /// </summary>
        [JsonProperty("timber")] public GridPos[] timber;

        [JsonProperty("well")] public GridPos well;

        /// <summary>Open ground at the centre of the city, where the crowd gathers.</summary>
        [JsonProperty("plaza")] public GridPos plaza;

        /// <summary>The walkable cell outside the house the opening walks the player into.</summary>
        [JsonProperty("player_house_door")] public GridPos player_house_door;
    }

    /// <summary>One move available in the morale contest.</summary>
    public class ContestMoveDef
    {
        [JsonProperty("id")] public string id;
        [JsonProperty("display")] public string display;
        [JsonProperty("description")] public string description;
        [JsonProperty("resolve_delta")] public int resolve_delta;
        [JsonProperty("morale_delta")] public int morale_delta;

        /// <summary>True when the move only exists after the page has been shown.</summary>
        [JsonProperty("unlocked_by_page")] public bool unlocked_by_page;

        /// <summary>
        /// Units of today's work capacity this move spends, or 0. The famine's grammar: the
        /// correct play is deliberately losing a resource, and the honest resource in this game is
        /// the day — a costed move shortens it. A costed move is offered once per contest and only
        /// while the day still has the price left; below it the card stays on the menu, greyed,
        /// so the player sees what the move would have cost.
        /// </summary>
        [JsonProperty("costs_work")] public int costs_work;

        /// <summary>
        /// Flag that has to be raised for the move to be offered. Null or empty means always.
        /// The same shape <see cref="DialogueChoice.hidden_if_flag"/> already uses, inverted.
        ///
        /// This generalises <see cref="unlocked_by_page"/> without replacing it: that boolean stays
        /// and is still honoured, so no authored move and no call site hard-breaks on the day this
        /// field appears.
        ///
        /// Rule 19 is protected by WHAT a move names here, not by the mechanism. The flag the boss
        /// contest gates on is <c>refused_invite</c> — a choice the player made in the fiction,
        /// never a reading they did. Gating a move on having read something would make reading pay
        /// in numbers, which is the one thing reading may never do.
        /// </summary>
        [JsonProperty("unlocked_by_flag")] public string unlocked_by_flag;
    }

    /// <summary>
    /// Tuning for one morale contest, from contest.json, which is a map keyed by the id below.
    /// There is more than one of these now — a stage declares which by name — so nothing in this
    /// type may assume it is describing the only encounter in the season.
    ///
    /// The move EFFECTS are deliberately not here. <c>MoraleContest.ApplyPlayerMove</c> keeps one
    /// case per literal move id and a default that applies the flat deltas, so two contests differ
    /// by tuning, by which moves they draw, by their enemy-line prefix and by whether they carry a
    /// reveal — never by what a move does. That is a knowingly deferred cost: the day a contest
    /// needs a move whose effect scales with the wall or the roster, the effect model has to become
    /// data before that contest is authored, not after.
    /// </summary>
    public class ContestConfig
    {
        /// <summary>Authoring id, and the key this config sits under in contest.json.</summary>
        [JsonProperty("id")] public string id;

        [JsonProperty("player_morale")] public int player_morale;
        [JsonProperty("enemy_resolve_base")] public int enemy_resolve_base;
        [JsonProperty("turn_limit")] public int turn_limit;

        /// <summary>
        /// Morale the enemy takes off the player each turn before any move is applied. Promoted out
        /// of a C# const because it was the one tuning number with no data override at all, which
        /// meant a boss encounter could not press harder than a first raid.
        /// </summary>
        [JsonProperty("base_pressure")] public int base_pressure;

        /// <summary>
        /// Turn on which the page arrives, or 0 for a contest that carries no reveal. Exactly one
        /// stage in the season declares <c>reveals_page</c>, so at most one contest may set this.
        /// </summary>
        [JsonProperty("page_turn")] public int page_turn;

        /// <summary>Reference the page shows. Reference only; the text is resolved at runtime.</summary>
        [JsonProperty("page_verse")] public string page_verse;

        /// <summary>
        /// Locale key prefix for this enemy's lines, numbered from 1. Kept as a prefix rather than
        /// an array of keys so the line count is a content decision in the locale file instead of a
        /// number that has to agree across two files in two languages.
        /// </summary>
        [JsonProperty("enemy_line_prefix")] public string enemy_line_prefix;

        /// <summary>
        /// Whether the trumpet sounds when this contest begins. True unless the row says
        /// otherwise, because the two fights that existed before the field did are an assault
        /// and a siege of letters, and NEH.4.20 puts the horn at the assault. Mockery has no
        /// assault to announce — nobody is converging on a breach — so it is the one row that
        /// sets this false.
        /// </summary>
        [JsonProperty("trumpet")] public bool trumpet = true;

        [JsonProperty("moves")] public ContestMoveDef[] moves;
    }

    /// <summary>
    /// The six kinds of stage the runtime switches on. Constants rather than an enum because the
    /// JSON carries the raw token and a DTO field mirrors its key one to one; this is the
    /// <see cref="GameFlags"/> precedent — nobody spells one of these by hand in a switch.
    /// </summary>
    public static class StageTypes
    {
        /// <summary>Opens in a cutscene and has no morning. Its check-in lands at the evening.</summary>
        public const string Intro = "intro";

        /// <summary>An ordinary day: morning report, the village, the split, the night.</summary>
        public const string Work = "work";

        /// <summary>A working day whose night applies no damage. See StageDef.night_threat.</summary>
        public const string Rest = "rest";

        /// <summary>A day whose morning is taken by a morale contest.</summary>
        public const string Battle = "battle";

        /// <summary>The second contest, keyed separately and tuned harder.</summary>
        public const string Boss = "boss";

        /// <summary>The last stage: the gate is hung, the record is written, the vocation is named.</summary>
        public const string Gate = "gate";
    }

    /// <summary>
    /// One stage of the season, from stages.json — the single authority for how long the season is,
    /// what kind each stage is, and which systems it turns on.
    ///
    /// <b>A stage IS a day.</b> <see cref="day"/> is the same int <see cref="GameState.day"/>
    /// carries, and that identity is load-bearing rather than a convenience: calendar day, dialogue
    /// selector, quiz selector, map-node index and the daily-reset token that ResourceSystem,
    /// RubblePile and WallSystem each compare against all already hang off that one monotonic
    /// number. A second progression axis would mean telling every one of those which axis it resets
    /// against, and the failure when one of them is told wrong is capacity or the rubble pile
    /// silently never refreshing — no exception, no log.
    ///
    /// This file holds structure and numbers only: there is not one word in it a player can read.
    /// Stage titles live in the locale's ui.json like every other string.
    /// </summary>
    public class StageDef
    {
        /// <summary>1..N, contiguous, and the stage's identity. Asserted at load.</summary>
        [JsonProperty("day")] public int day;

        /// <summary>Authoring id, used in flags, counters and logs. Never shown to the player.</summary>
        [JsonProperty("id")] public string id;

        /// <summary>One of <see cref="StageTypes"/>. Asserted at load.</summary>
        [JsonProperty("type")] public string type;

        /// <summary>
        /// The last stage, and the only one that takes DayCycle's final-day hold and never gives it
        /// back — it has no tomorrow. Exactly one stage sets this and it is the last.
        ///
        /// Scoping that hold to a declared stage is the change that makes a season longer than the
        /// hold's original day possible at all: taken unconditionally, it stops the calendar dead
        /// wherever it is taken, whatever the stage table says comes next.
        /// </summary>
        [JsonProperty("terminal")] public bool terminal;

        /// <summary>
        /// False on a stage whose night damages nothing. The night still RESOLVES — the same split
        /// panel, the same resolve, the same advance to morning, the same watch flag written — it
        /// simply applies no unwatched-segment damage, because nobody has moved against the wall
        /// yet.
        ///
        /// That graft is deliberate and the alternative was considered and rejected: a second way
        /// to end a day, or a suppressed morning report, fails by showing the player the previous
        /// night's stale counters as this morning's fact, with no exception and no log. NEH.4.9
        /// supports a watch on every night, including a quiet one.
        /// </summary>
        [JsonProperty("night_threat")] public bool night_threat;

        /// <summary>Contest id resolved in GameData.Contests, or null for a stage with no contest.</summary>
        [JsonProperty("contest")] public string contest;

        /// <summary>Gathering node the director plays on this stage, or null. Resolved in GameData.Dialogue.</summary>
        [JsonProperty("cutscene_node")] public string cutscene_node;

        /// <summary>
        /// At the end of this stage's beat, every segment except the one the gate stage declares is
        /// force-completed through the ordinary forward-only work path. It can only ever raise a
        /// course, never lower one, so rule 4 holds by construction.
        ///
        /// It exists so the dedication cannot open on a half-built wall when a player — or the
        /// harness's cheap traversal — under-builds. The stage that sets this must come before the
        /// stage that closes the gate, and the loader asserts that.
        /// </summary>
        [JsonProperty("finishes_wall")] public bool finishes_wall;

        /// <summary>The stage that hangs the doors. Exactly one, and it is the last.</summary>
        [JsonProperty("closes_gate")] public bool closes_gate;

        /// <summary>Segment id the gate is hung on. Meaningful only when <see cref="closes_gate"/>.</summary>
        [JsonProperty("gate_segment")] public string gate_segment;

        /// <summary>
        /// This stage turns chapter-and-verse on for the rest of the run, via
        /// <see cref="ScriptureVisibility.Reveal"/>. Exactly one stage in the season.
        ///
        /// One-way by design: every stage after it carries a visible reference footer, which is the
        /// second and third invitation the funnel needs and which a shorter season had no room for.
        /// </summary>
        [JsonProperty("reveals_page")] public bool reveals_page;

        /// <summary>
        /// This stage names the vocation, once, at the end. Exactly one stage in the season, and it
        /// is the only place VocationTracker.Resolve() is ever called. Progress toward it is never
        /// shown anywhere before this — rule 5, unchanged; only the location of the single call
        /// moved.
        /// </summary>
        [JsonProperty("reveals_vocation")] public bool reveals_vocation;

        /// <summary>
        /// Catalogue item the progression map features for this stage. FEATURED, not granted: it is
        /// a signpost, and it may point at any catalogue entry whether or not that entry's unlock
        /// condition happens to open here. That looseness is the art fallback — if a new garment
        /// slips, a stage features an existing one and nothing is blocked.
        /// </summary>
        [JsonProperty("reward_item")] public string reward_item;

        /// <summary>
        /// What the vigil returns on this stage's night, as a reference into verses.json, or null
        /// on a night that offers none (the terminal stage has no night at all). The vigil is rule
        /// 8's information half: whoever is not on the wall stays up over the page instead of
        /// building, so the night's work is the price and this verse is what it buys. The verse is
        /// always ABOUT what the next stage brings — the enemy's intent before the raid, the letters'
        /// intent before the letters — and never a number; rule 19 stays intact by construction.
        /// Resolved through ScriptureService, so rule 2 holds: the text is fetched, never typed.
        /// </summary>
        [JsonProperty("vigil_verse")] public string vigil_verse;

        /// <summary>
        /// Gathering node the director plays after the gate closes and before the vocation is
        /// revealed, or null. The season's closing event — the reading of the Law, `NEH.8` — lives
        /// here so the director still counts nothing: a stage that wants a closing beat names it.
        /// </summary>
        [JsonProperty("closing_node")] public string closing_node;

        /// <summary>
        /// Where this stage's node sits on the progression map. Two entries, normalised 0..1 against
        /// the map image with a bottom-left origin. Asserted to be length 2 at load, because the
        /// map view indexes [0] and [1] without looking.
        /// </summary>
        [JsonProperty("map_anchor")] public float[] map_anchor;

        /// <summary>Where the map viewport centres when this stage is the current one. Two entries, as above.</summary>
        [JsonProperty("map_focus")] public float[] map_focus;
    }

    // ---------------------------------------------------------------- locale strings
    // One type per locale file under Resources/Data/locales/<locale>. These exist only long
    // enough for GameData to copy their fields onto the DTOs above; nothing else ever sees them.

    /// <summary>Player-facing half of a contest move, from locales/&lt;locale&gt;/contest.json.</summary>
    public class ContestMoveStrings
    {
        [JsonProperty("display")] public string display;
        [JsonProperty("description")] public string description;
    }

    /// <summary>
    /// One of the ten gates of Nehemiah 3, from gates.json. Structure only: the verse it is read
    /// from and, for the one gate the season builds, the wall segment that stands for it. The two
    /// player-facing strings are merged in from the locale copy.
    /// </summary>
    public class GateDef
    {
        [JsonProperty("id")] public string id;

        /// <summary>The verse of NEH.3 that names this gate. Rendered as a citation, never typed.</summary>
        [JsonProperty("verse")] public string verse;

        /// <summary>
        /// The wall segment this gate is, when the season builds it; null for the nine it does not.
        /// The player's own gate is the one whose segment the terminal stage's <c>gate_segment</c>
        /// names, so the codex marks it without spelling a segment id anywhere in code.
        /// </summary>
        [JsonProperty("segment")] public string segment;

        [JsonIgnore] public string display;

        /// <summary>Who raised it, in our own words — narration about the names, never a quotation.</summary>
        [JsonIgnore] public string builders;
    }

    /// <summary>Player-facing half of a gate, from locales/&lt;locale&gt;/gates.json.</summary>
    public class GateStrings
    {
        [JsonProperty("display")] public string display;
        [JsonProperty("builders")] public string builders;
    }

    /// <summary>Player-facing half of a vocation, from locales/&lt;locale&gt;/vocations.json.</summary>
    public class VocationStrings
    {
        [JsonProperty("display")] public string display;
        [JsonProperty("reveal_line")] public string reveal_line;
    }

    /// <summary>Player-facing half of a check-in question, from locales/&lt;locale&gt;/quiz.json.</summary>
    public class QuizStrings
    {
        [JsonProperty("prompt")] public string prompt;
        [JsonProperty("options")] public string[] options;
        [JsonProperty("note")] public string note;

        /// <summary>
        /// What tomorrow holds, shown under the note once the question is answered. The question
        /// closes the session, so this is the last thing read before the split, and it points at
        /// the next day's chapter rather than at a reward. Optional; the last day has none.
        /// </summary>
        [JsonProperty("hook")] public string hook;
    }
}
