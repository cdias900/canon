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
        [JsonProperty("day")] public int day;
        [JsonProperty("lines")] public DialogueLine[] lines;
        [JsonProperty("grants")] public Grants grants;

        /// <summary>Branches offered after the last line. Null or empty on a straight-through node.</summary>
        [JsonProperty("choices")] public DialogueChoice[] choices;

        /// <summary>Whether what this node claims is true. The game never shows this to the player.</summary>
        [JsonProperty("reliable")] public bool reliable;
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
    }

    /// <summary>Tuning for the day-3 morale contest, from contest.json.</summary>
    public class ContestConfig
    {
        [JsonProperty("player_morale")] public int player_morale;
        [JsonProperty("enemy_resolve_base")] public int enemy_resolve_base;
        [JsonProperty("turn_limit")] public int turn_limit;
        [JsonProperty("moves")] public ContestMoveDef[] moves;
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
    }
}
