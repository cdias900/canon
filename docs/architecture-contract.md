# Architecture Contract — Sheep Gate

The public seams nobody changes alone. **Accurate as of 2026-09-02**, which is a claim this file has
to keep earning: it was written as a frozen spec before the code existed, and by the time anyone
checked, eight of its declarations had drifted from the code they described. A frozen document that
is wrong is worse than no document.

So it is no longer frozen — it is **load-bearing**. What it lists are the signatures, scene names
and schemas that other modules and the test harnesses depend on: change one and something you are
not editing breaks. Read it before widening a public signature, and update it in the same commit
that changes one. Where this file and the code disagree, **the code wins and this file is stale —
fix it.**

## Non-negotiable rules (repeat to every agent)

1. **English only** for identifiers, comments, file names, commit messages, docs.
   **No player-facing string in C# at all** — the game is bilingual (pt-BR authoring, en
   translation), every such string lives in `Assets/Resources/Data/locales/<locale>/` and is read
   through `Loc.T`, and the validator fails the build on a literal reaching the screen.
2. **Scripture never appears as literal text** in C#, JSON authored by hand, prompts, or
   tests. Only references circulate: `NEH.4.6`. Literal text lives solely in the generated
   `verses.json`. Test fixtures use obviously synthetic strings such as
   `"PLACEHOLDER_VERSE_TEXT"`.
3. **No vocation progress may reach any UI.** `VocationTracker` exposes no public score
   getter. Only `Resolve()` at the end of the stage that declares `reveals_vocation`. The rule
   is not weakened by the season getting longer — there is still exactly one call, and it is
   still the only reader; only its location is now declared in data rather than in code.
4. **No game over, no death counter, no health bar.** Morale only. Completed masonry courses
   never regress, and absence delays rather than reverting. **"Stage" means two things one
   directory apart, and this is the only warning you get:** `WallSegmentState.stage` is a
   **masonry course**, 0..4 on one segment; "stage" unqualified everywhere else means one of the
   nine **season stages** in `stages.json`. The names were left alone deliberately — renaming
   `WallSegmentState.stage` would touch a persisted field and force a second save migration for
   pure hygiene, which is a worse trade than this sentence.
5. **No network calls at runtime.** `verses.json` is baked at build time.

## Root namespace

`SheepGate`, with one sub-namespace per folder. All fourteen, because a list that is nearly complete
sends a reader grepping for a module they were told does not exist — this one was four short of the
tree until it was checked with `grep -rh "^namespace SheepGate" Assets/Scripts/ | sort -u`, which is
the only way to keep it honest:

`SheepGate.Art`, `SheepGate.Audio`, `SheepGate.Boot`, `SheepGate.Contest`, `SheepGate.Core`,
`SheepGate.Dialogue`, `SheepGate.E2E`, `SheepGate.Economy`, `SheepGate.Player`, `SheepGate.Quiz`,
`SheepGate.Scripture`, `SheepGate.UI`, `SheepGate.Vocation`, `SheepGate.World`.

`SheepGate.Boot` holds only the three bootstraps the scenes point at (see *Scene entry points*), and
`SheepGate.E2E` only the run harness — neither is a module anything else may call into.

## Unity authoring strategy (read carefully — this is unusual on purpose)

Scenes are **near-empty by design**. Each `.unity` file contains only a Main Camera and one
`GameObject` holding a single bootstrap MonoBehaviour. **Every other GameObject, all UI, the
tilemap, and all sprites are created at runtime from C#.** Rationale: hand-authored scene
YAML with GUID cross-references is the highest-risk artifact for agent authoring; runtime
construction is verifiable by the compiler.

- UI is **uGUI built programmatically** (`Canvas` + `RectTransform` created in code).
  Do NOT use UI Toolkit, UXML, or `.asset` UI files.
- The tilemap is built at runtime via `Tilemap.SetTile` using `ScriptableObject.CreateInstance<Tile>()`.
- All sprites come from `SheepGate.Art.ArtLibrary`. Most are procedural `Texture2D`; a key may
  instead be backed by a drawn tile read out of one CC0 spritesheet, which ships as a `.bytes`
  file and is decoded at runtime. **This line is a change to a frozen document and needs
  ratifying.** The ban existed because hand-authored image assets bring import settings, `.meta`
  files and stored slicing that no agent can review; decoding one sheet in code keeps all of that
  inside compiler-checked source, which is the property the rule was protecting. See §11 of
  `MVP-SCOPE.md`, which asked for a CC0 tileset from the start.
- Never write a `[SerializeField]` that must be wired in the inspector. Resolve dependencies
  through `ServiceLocator` or `FindFirstObjectByType`.

## Core service surface — `SheepGate.Core`

```csharp
public static class ServiceLocator {
    public static void Register<T>(T service) where T : class;
    public static T Get<T>() where T : class;          // throws if missing
    public static bool TryGet<T>(out T service) where T : class;
    public static void Clear();
}
```

```csharp
public enum Assignment { Work, Watch }

[Serializable]
public class WallSegmentState {
    public string id;
    public int stage;              // MASONRY COURSE, 0..4, 4 == complete. Not a season stage.
    public int workInStage;        // work units accumulated toward next stage
    public bool damaged;
}

[Serializable]
public class AppearanceState {
    public int body;               // 0..1  build
    public int skin;               // 0..3  skin tone
    public int hair;               // 0..3
    public int top;                // 0..3
    public int legs;               // 0..3
    public int accessory;          // 0..3

    public int BodyArtVariant { get; }   // build and tone PACKED: build * SkinTones + skin
}

[Serializable]
public class GameState {
    public int schemaVersion;                             // shape of the file on disk
    public const int CurrentSchemaVersion = 3;            // 1 rubble-only, 2 materials, 3 nine stages

    public int day = 1;                                   // 1..9, and it IS the stage number
    public string seasonId = "nehemiah";                  // stamped by NewGame; never branched on in game
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

    public bool HasFlag(string flag);
    public void SetFlag(string flag);
    public int Counter(string key);
    public void Bump(string key, int amount = 1);
    public WallSegmentState Segment(string id);
}
```

Flag names (string constants on `GameFlags`, all lowercase snake_case):
`watch_posted_d1`, `watch_posted_d2`, `accepted_invite`, `refused_invite`,
`page_shown`, `page_skipped`, `fish_caught`, `chapter_opened`, `deep_read`,
`contest_resolved`, `vocation_revealed`, `reached_map_edge`.

One more lives elsewhere and this line is the pointer, so the list above can be read as complete:
`references_revealed` is `ScriptureVisibility.RevealedFlag`, kept on that class because the rule
about when references appear is that class's whole subject.

Three families are **computed**, because their count follows the stage table rather than being
fixed. Their values are unchanged where they overlap the constants above, which is what let a
nine-stage season ship without renaming a single persisted key:

```csharp
public static string WatchPostedForDay(int day);        // "watch_posted_d" + day   — d1/d2 byte-for-byte
public static string ContestResolvedFor(string id);     // "contest_resolved_" + id — e.g. contest_resolved_raid
public static string StageDoneCounter(string stageId);  // "stage_done_" + stageId  — a counter, not a flag
```

`contest_resolved` (flat) is legacy: it means "the raid was fought" and is what a schema-2 save
carries. `SaveSystem`'s 2 → 3 migration additionally raises `contest_resolved_raid` for such a
save, and never clears the old one.

```csharp
public static class SaveSystem {
    public static string SavePath { get; }               // persistentDataPath/save.json
    public static bool HasSave();
    public static GameState Load();                      // null when absent or corrupt
    public static void Save(GameState state);
    public static void Delete();
}
```

`Load` repairs, then migrates, then clamps the day — **in that order**, and the order is
load-bearing. A schema-2 save carries `day: 3` meaning "the last day, and the trial is tonight";
under the season's numbering that day is the trial's stage, 6 — or 7 when `contest_resolved` says
the trial was already fought. A floor applied before the migration reads the pre-migration number,
and a ceiling applied before it measures a day against a season the save was not written for. A
save from a **newer** schema is never stepped back and never clamped, in either direction.

```csharp
public interface ITelemetrySink { void Track(string eventName, IDictionary<string, object> props); void Flush(); }

public sealed class JsonlFileSink : ITelemetrySink { public JsonlFileSink(string path); }

public static class Telemetry {
    public static void Initialize(ITelemetrySink sink);
    public static void Track(string eventName, IDictionary<string, object> props = null);
    public static void Flush();
}

public static class TelemetryEvents {
    public const string SessionStart     = "session_start";
    public const string VerseShown       = "verse_shown";
    public const string ChapterOpened    = "chapter_opened";
    public const string DeepRead         = "deep_read";
    public const string RevealShown      = "reveal_shown";
    public const string NodeCompleted    = "node_completed";
    public const string VocationRevealed = "vocation_revealed";
}
```

```csharp
public static class GameData {   // structure from Resources/Data, words merged from locales/<locale>
    public static void LoadAll();
    public static NpcDef[] Npcs { get; }
    public static IReadOnlyDictionary<string, DialogueNode> Dialogue { get; }
    public static WallSegmentDef[] WallSegments { get; }
    public static StageDef[] Stages { get; }                                  // the season, in order
    public static StageDef Stage(int day);                                    // never null; clamps and logs once
    public static IReadOnlyDictionary<string, ContestConfig> Contests { get; }
    public static ContestConfig Contest { get; }                              // == Contests["raid"]
    public static VocationDef[] Vocations { get; }
    public static QuizQuestion[] Quiz { get; }
    public static MapDef Map { get; }
    public static string LoadedLocale { get; }
}
```

`Contest` is **joined by**, not replaced by, `Contests`: it keeps its exact signature and returns
the raid, so no existing caller changed on the day `contest.json` grew a second entry. New code
names the contest it means.

`LoadAll` asserts the stage table once it has read everything, and every failure is a
`Debug.LogError` that then carries on — this class's standing policy, and not leniency: the E2E
runner promotes a logged error to a run failure, so a broken table fails the gate loudly while
still leaving a build a person can open to see *which* stage is wrong. The invariants:

- days contiguous `1..N` in file order, no gaps and no repeats;
- `type` is one of `intro | work | rest | battle | boss | gate`;
- exactly one `terminal`, and it is the last;
- exactly one `closes_gate`, it is the last, and its `gate_segment` exists in `wall_segments.json`;
- exactly one `reveals_page`; exactly one `reveals_vocation`;
- `finishes_wall`'s day is strictly before `closes_gate`'s;
- every non-null `contest` resolves in `Contests`; every `cutscene_node` resolves in `Dialogue`;
- `map_anchor` and `map_focus` are exactly two entries each.

One more needs the catalogue, which loads a line after this one, so it runs at the end of
`BootSequence.ApplyLocale` instead: every `reward_item` resolves in `character_catalog.json`.

## Data transfer objects — `SheepGate.Core` (Newtonsoft attributes)

```csharp
public class NpcDef      { string id; string display; string source_ref; GridPos spawn; string palette; }
public class GridPos     { int x; int y; }
public class DialogueLine{ string text; string verse; string frame; }   // text XOR verse
public class DialogueNode{ string npc; int day; DialogueLine[] lines; Grants grants;
                           DialogueChoice[] choices; bool reliable;
                           bool canonical_speaker; bool needs_curation; }
public class DialogueChoice { string id; string text; string next; Grants grants;
                              int requires_rubble; string hidden_if_flag; }
public class Grants      { Dictionary<string,int> vocation; Dictionary<string,int> flags; string set_flag; }
public class WallSegmentDef { string id; int grid_x; int[] stage_cost; bool exposed; }
public class VocationDef { string id; string display; string reveal_line; }   // reveal_line is pt-BR, authored, never scripture
public class QuizQuestion{ int day; string prompt; string[] options; int answer; string note; }
public class MapDef      { int width; int height; string[] rows; GridPos player_spawn;
                           GridPos[] rubble; GridPos[] timber; GridPos well; GridPos plaza;
                           GridPos player_house_door; }
public class StageDef    { int day; string id; string type; bool terminal; bool night_threat;
                           string contest; string cutscene_node; bool finishes_wall;
                           bool closes_gate; string gate_segment; bool reveals_page;
                           bool reveals_vocation; string reward_item;
                           float[] map_anchor; float[] map_focus; }
public class ContestConfig { string id; int player_morale; int enemy_resolve_base; int turn_limit;
                             int base_pressure; int page_turn; string page_verse;
                             string enemy_line_prefix; ContestMoveDef[] moves; }
public class ContestMoveDef { string id; string display; string description; int resolve_delta; int morale_delta;
                              bool unlocked_by_page; string unlocked_by_flag; }
```

`DialogueNode.day` **0 is a sentinel, not a missing value**: it means "available at any stage", and
eight shipped nodes rely on that reading. `canonical_speaker` and `needs_curation` were keys in the
JSON and in the tooling long before they were members here, so Newtonsoft dropped them on load and
the runtime could not see rule 4's marking at all; they bind now.

`QuizQuestion.day` runs over every stage the season declares — nine today, three when this file was
written. The file shape and `GameData.MergeQuizStrings`' day-keyed join are unchanged, deliberately:
the join's own comment gives the reason and it still holds.

`ContestMoveDef.unlocked_by_flag` **generalises** `unlocked_by_page` without replacing it — the
boolean stays and is still honoured. Rule 19 is protected by *what* a move names there: the flag the
boss gates on is `refused_invite`, a choice in the fiction, never a reading.

`stages.json` is the single authority for how long the season is, what kind each stage is, and which
systems it turns on. Structure and numbers only — **there is not one word in it a player can read**;
stage titles live in each locale's `ui.json` like every other string.

## Scripture — `SheepGate.Scripture`

```csharp
public class VerseEntry   { public string ref_display; public string text; }
public class ChapterVerse { public int n; public string text; }
public class ChapterEntry { public string ref_display; public ChapterVerse[] verses; }
public class VersionInfo  { public string id; public string abbrev; public string copyright; }

public static class ScriptureService {
    public static bool IsPlaceholderBuild { get; }       // true when verses.json was generated without a real fetch
    public static VersionInfo Version { get; }
    public static void Load();                           // reads Resources/Data/locales/<locale>/verses.json,
                                                         // never hits the network
    public static bool TryGetVerse(string reference, out VerseEntry verse);
    public static VerseEntry GetVerse(string reference);     // never null; returns a visible missing-text marker
    public static ChapterEntry GetChapter(string chapterRef);
    public static string ChapterRefOf(string verseRef);   // "NEH.4.6" -> "NEH.4"
}
```

`GetVerse` on a miss returns `text` set to the pt-BR marker `"⟨texto indisponível — NEH.4.6⟩"`
(reference substituted). It must be visually obvious, never silently empty, and never invented.

## World — `SheepGate.World`

```csharp
public class WallSystem : MonoBehaviour {
    public event Action<string,int> SegmentStageChanged;     // id, newStage
    public event Action<string> SegmentCompleted;
    public bool ApplyWork(string id, int units);             // false when already complete
    public void DamageSegment(string id);                    // clears in-progress work only, never a finished stage
    public int TotalStages { get; }
    public int CompletedStages { get; }
}

public class ResourceSystem : MonoBehaviour {
    public bool Spend(int units);
    public void AddRubble(int n);
    public void ResetDailyCapacity();
    public event Action Changed;
}

public class DayCycle : MonoBehaviour {
    public static int FinalDay { get; }                      // the last stage's day, from stages.json
    public event Action<int> MorningStarted;                 // day number
    public event Action<int> DuskBegan;                      // the day is turning to evening
    public void RequestEndDay();                             // opens the split panel
    public void EndDay(int workers, int watchers);           // resolves the night, advances the day
    public void HoldDusk(string reason);                     // named hold; ReleaseDusk gives it back
    public void ReleaseDusk(string reason);
    public float NightAmount { get; }                        // 0 day .. 1 night

    // The night waits for whatever has the screen. The chapter reader is a panel like any other,
    // so a day must never end during a reading — rule 20 pointed at its own foot.
    public static bool DuskWaits(bool held, bool inputLocked, bool modalOpen);
}
```

`FinalDay` was `public const int FinalDay = 3`. It is a property now because the season's length is
declared in `stages.json`, and a const would have been baked into every call site that read it. The
season is nine stages; nothing in code should say so.

The final-day hold, `HoldFinalDay`, is taken by **the stage that declares `terminal`** and by no
other. Taken unconditionally it stops the calendar dead wherever it is taken, whatever the stage
table says comes next — which is why scoping it is the single change that makes a longer season
reachable at all.

### The map grid — `TilemapBuilder`

```csharp
public sealed class TilemapBuilder : MonoBehaviour {
    public const char GroundChar = '.';
    public const char VoidChar   = ' ';                  // off the map, and the map's own boundary
    public enum CellKind { Ground, Rubble, Water, House, Wall, Void }

    public bool[,] Walkable { get; private set; }        // the array the pathfinder consumes
    public Bounds WorldBounds { get; private set; }      // the map rectangle
    public Bounds ContentBounds { get; private set; }    // the box around every non-Void cell
    public CellKind KindAt(int x, int y);                // anything out of bounds reads Void
    public bool IsWalkable(int x, int y);

    public void SetVoidColor(Color color);               // the region map flattens the outside…
    public void SetVoidSprite(Sprite sprite);
    public void RestoreVoidColor();                      // …and closing it puts the terrain back
}
```

**The void invariant, and it is a contract rather than a description.** A `Void` cell **may draw
anything. It is never walkable, never pathable, never an entity.** Appearance and walkability are
two separate questions about the same cell, and exactly one line answers the second —
`TilemapBuilder.cs:352`, `Walkable[x, y] = kind == CellKind.Ground || kind == CellKind.Rubble`.
Nothing that changes what the outside *looks* like may go near it. The invariant is not hygiene:
the space character was once read as a second ground character, which made the whole outer border
walkable and let the player stroll around the wall and out of the village
(`TilemapBuilder.cs:47-54`).

That invariant is now **gated**, because it can no longer be seen. It used to be caught by eye —
the outside looked wrong — and the outside looks right now, which is precisely when an unasserted
invariant starts costing sessions. `E2ERunner.VerifyTheOutsideIsNotWalkable`
(`Assets/Scripts/E2E/E2ERunner.cs:3698`, run from the e2e village sweep at `:454`) records **two**
results, and the first is the load-bearing one: *the map has an outside to test*, `voidCells > 0`,
because an assertion that passes for want of anything to check is the failure mode it exists to
refuse. On the shipped `map.json` — 40 by 28, with the drawn city occupying well under half of it —
that is **640 void cells of 1120**, none walkable.

`ContentBounds` is the second half of the same story and is why the outside had to be drawn at all:
`CameraRig` clamps to it (`CameraRig.cs:283`) rather than to `WorldBounds`, and a camera clamped to
the drawn content still frames the corners of the band. Near-black rectangles there read as a
rendering hole, not as the edge of a valley.

```csharp
namespace SheepGate.World {
    internal static class VoidScatter {
        public static bool[,] Build(bool[,] isVoid, int width, int height, int skirtX, int skirtY);
    }
}
```

`internal`, and stated as it is because this document is worth nothing if a signature in it is
approximate. It decides **which off-map cells carry the fallen wall, and nothing else** — the grid
it returns is indexed `[x + skirtX, y + skirtY]`, covering the map rectangle plus the skirt painted
on all four sides, so column `x` runs from `-skirtX` to `width + skirtX - 1`. Cells the map actually
draws are always `false`. Its one caller is `TilemapBuilder.Build`
(`TilemapBuilder.cs:250`), and what it hands back is consumed by `OutsideTileFor` as **a sprite swap
and nothing else** (`TilemapBuilder.cs:548`): the cell stays void, stays unwalkable, and the stone on
it is terrain rather than a `RubblePile` the player clears.

Two properties are load-bearing and cheap to break:

- **It has no Unity dependency.** The file opens on `namespace SheepGate.World` with no `using` at
  all, and reaches for `System.Math.Sqrt` where it needs a square root. That is deliberate and its
  own summary says so: a scatter can only be judged by *counting* what it produces over a whole map,
  and a Unity reference puts that count behind a device build. The density numbers in its summary
  are of that kind — the metric it uses today replaced one that measured 75% stone on column 0 and
  11-15% on the columns a close camera can actually frame, which is a drawn line down the edge of
  the map and is invisible to any assertion this repository has.
- **It is deterministic from integer coordinates.** Every number comes from a hash of `(x, y, salt)`
  — no `System.Random`, no `UnityEngine.Random`, no clock, no build order — so the same map scatters
  identically on every device, on every run, and in both locales. A screenshot taken twice shows the
  same stones, which is what makes a visual diff mean anything.

## Dialogue — `SheepGate.Dialogue`

```csharp
public class DialogueSystem : MonoBehaviour {
    public bool IsPlaying { get; }
    public event Action<string> NodeFinished;
    public void Play(string nodeId);
    public void Advance();                                   // completes typing, then moves to the next line
}
```
Typing reveal is 40 characters/second. A `verse` line renders italic with `ref_display` in the
bubble footer, at the same type size as any other metadata, and fires `verse_shown`.

## Contest — `SheepGate.Contest`

```csharp
public enum ContestOutcome { EnemyWithdrew, PlayerBroke, TurnLimit }

public class MoraleContest : MonoBehaviour {
    public event Action<int> TurnStarted;
    public event Action<ContestOutcome> Finished;
    public void Begin();                    // the current stage's declared contest
    public void Begin(string contestId);    // a named one
    public void UseMove(string moveId);
    public bool IsMoveAvailable(string moveId);
}
```

`Begin()` keeps its exact signature; the overload is additive. The season has two encounters and one
`MoraleContest` instance, so "which contest" had to become sayable at the call site.

## Vocation — `SheepGate.Vocation`

```csharp
public class VocationTracker {
    public void Add(string vocationId, int points);          // silent
    public string Resolve();                                 // highest score; ties break by vocations.json order
    // NO public score getter. Nothing may read progress before Resolve().
}
```

## UI — `SheepGate.UI`

### How a screen learns how wide it is

```csharp
public static class UIKit {
    public const float ReferenceWidth  = 1080f;   // the design resolution, NOT a device width
    public const float ReferenceHeight = 1920f;
    public const float CanvasMatch     = 0.5f;    // the scaler's matchWidthOrHeight, named once
    public static float CanvasWidth();            // what a screen actually gets, in canvas units
}
```

**Every fixed width a screen computes starts at `CanvasWidth()` and never at `ReferenceWidth`.** The
canvas is 1080 units across only on a device whose aspect is exactly 1080x1920; an iPhone 17 Pro
(1206x2622) reports about 976, and a width pinned to the reference overflows its parent by the
difference (`UIKit.cs:175-192`).

Two properties of that method are the contract, not the implementation:

- **It computes the scaler's own formula — a log-space lerp between the two axis ratios — instead of
  reading a `RectTransform`.** A canvas created this frame has not had its scale factor set, so
  every rect under it is still measured in **raw screen pixels**: at 402x874 a safe-area rect reads
  402 where it will read 977 one frame later, and a width derived from it came out a third of the
  size — enough to hand `WardrobeRow.MetricsFor` a **negative** text column. `CanvasMatch` is a named
  constant for the same reason: a second literal is how the formula and the scaler silently disagree.
- **A safe-area inset is applied as a *ratio* of `Screen.width` / `Screen.height`, never as a rect.**
  The ratio is the one thing about the inset that is true before a layout pass, and on a screen with
  no inset it is 1, which is why desktop numbers do not move.

Both of those defects survive every `tools/e2e.sh` run by construction, because the macOS player is
launched at exactly 1080x1920 (`tools/e2e.sh:159`) — the one resolution where pixels and canvas units
are the same number. **The e2e gate cannot see this class of bug. Only a phone can.**

### `WardrobeRow` — one row renderer, two screens

```csharp
public static class WardrobeRow {
    public static readonly float BodyLineHeight;          // Type.Body * Type.BodyLeading
    public static readonly float ThumbSize;               // Px(56)
    public static readonly float ScrollbarWidth;          // Space.S8

    public struct Metrics       { public float RowWidth, TextColumnWidth, ThumbSize; }
    public sealed class View    { public string ItemId; public CharacterSlot Slot; /* … */ }

    public static Metrics MetricsFor(float contentWidth);
    public static View Build(RectTransform content, CatalogItemDef item, CharacterSlot slot,
                             Metrics metrics, int bodyArtVariant, bool locked, bool showNewBadge,
                             bool isNew, string namePrefix, Action<string, CharacterSlot> onTap,
                             bool showTalentPrice = false);
    public static void BuildEmpty(RectTransform content, Metrics metrics);
    public static void Apply(View view, bool worn);
    public static void ApplyBody(View view, int bodyArtVariant);
    public static bool Tap(GameState state, string itemId, CharacterSlot slot, out string refusalSentence);
    public static float PinText(Text text, float boxWidth);
    public static void PinTextBox(Text text, float boxWidth, float height);
}
```

**Two screens list the wardrobe, and exactly one of them draws a row.** `BackpackPanel` and
`CharacterCreationScreen` both go through this class, and that is a seam rather than a tidy-up,
because almost everything that makes a row correct is arithmetic that looks like nothing when it
drifts: `MetricsFor` derives the text column as `rowWidth - (3·S12 + ThumbSize)` and **logs an error
below 80 points**, because legacy `Text` breaks a word it cannot fit — "Túnica de carregador" once
rendered as "Túnica de / carregad / or" when the column lost sixteen points to an S8 gap. A second
copy of that row is a second copy of that threshold, and a phone is the only place either is visible.

`Metrics` is a **struct passed down, not mutable statics**, and that is part of the seam rather than
style: one sheet at a time could get away with statics, two screens cannot, and a static that one
screen sets and the other reads is the quietest possible way to lay a row out against a width it
does not have.

The corollary a caller must honour: **`MetricsFor` subtracts the scrollbar lane, so the caller has
to reserve it as right-hand padding** on the layout group its rows are parented into — otherwise the
row is built narrower than the space it is given and sits with a gap on its right. Both callers do
it, on the `VerticalLayoutGroup` of their scroll content. And `contentWidth` comes from
`UIKit.CanvasWidth()` by the chain above, never from `ReferenceWidth`.

`showTalentPrice` is trailing and optional deliberately — adding it could not silently reorder the
three `bool` parameters ahead of it at an existing call site.

### The wardrobe slot keys

Both screens name the three slots from the **same key namespace** — `slot.hair`, `slot.outfit`,
`slot.accessory` (`CharacterCreationScreen.cs:209`, `BackpackPanel.cs:196-198`, defined per locale at
`locales/<locale>/ui.json:217-219`). That is the point of them: renaming a slot is one edit in each
locale file and it lands on both screens at once. The last rename — Detalhes → **Extras** — is why
the key is `slot.accessory` and the word is not; the key is the seam, the word is content.

## Art — `SheepGate.Art`

```csharp
public static class ArtLibrary {
    public static Sprite Get(string key);                    // cached, procedurally generated on first use
    public static Sprite GetTinted(string key, Color tint);
}
```
Keys are declared in `SheepGate.Art.ArtKeys` — read that rather than a list here, which is how this
section drifted last time. **Today exactly one key resolves from the drawn CC0 sheet: `tile_water`**
(`Tileset.cs:42`). This paragraph used to say ground, rubble and water, and it was wrong in the
direction that matters — `Tileset.cs:47` records ground and rubble as *deliberately* unmapped, because
the sheet's seamless fills are pale interior floors and its textured ones are autotile edge pieces
that seam every tile, judged as a five-by-five field rather than as single tiles. Everything else is
generated procedurally, and a key with no drawn tile behind it falls through to the generated one
(`ArtLibrary.cs:186`) — so mapping a new key is additive and needs no caller to change.

```csharp
// ArtKeys — the wall lying where it fell: the terrain of the ruin band outside the city.
public const string TileFallenWall = "tile_fallen_wall";
public const int FallenWallVariantCount = 12;
public static string FallenWallVariant(int variant);   // variant 0 is the plain key

// TileArt — the drawing itself, on the standing wall's own masonry.
public static PixelCanvas FallenWall(int seed);        // TileArt.cs:440
```

`FallenWallVariant` **mirrors `GroundVariant`'s shape exactly** — clamp, modulo the count, variant 0
returns the bare key and every other returns `<key>_<n>` — because `ArtLibrary` resolves both by
`StartsWith` on the base key and seeds the drawing from the key itself (`ArtLibrary.cs:228-239`). A
variant scheme that did not match would resolve to the wrong family or to nothing.

**The count is 12 against ground's 6, and that is a decision, not a spare digit.** A ground tile
carries noise, so a repeat is invisible; a fallen-wall tile carries block silhouettes the eye matches
across the screen. The ruin band is around two hundred cells, which puts any one tile at roughly
sixteen appearances (`ArtKeys.cs:121-129`). Note also what the key is *not*: `tile_fallen_wall` is
not `tile_rubble`. Rubble is a cell the player walks onto and clears; this is terrain on a cell that
is not walkable at all. **Two things that mean opposite things may not draw the same pixels.**

Two constraints on this tile are not stylistic and cost a session each when they were missed. The
drawing must **leave the base tile unshaded and let blocks run off the edge and wrap** — a stone
drawn wholly inside its own cell leaves the 32px lattice readable however the cell is shaded, which
is exactly the checkerboard the first attempt produced (`TileArt.cs:428-438`). And the **variant
picker must mix before it takes the modulo**: it was `Mathf.Abs(x * 40503461 ^ y * 12582917) % 12`,
and since both multipliers are 1 mod 4 while 12 divides by 4, `variant % 4` was exactly
`((x ^ y) & 3)` on every cell — twelve variants collapsed to four classes on the same 4x4 lattice
the tile exists to break (`TilemapBuilder.cs:605-618`). Both passed compile, validator and e2e. Both
were found by counting, not by reading, which is what `tools/tile-preview.sh field` is for.

```csharp
public static string Hair(int variant, ArtFacing facing);
public static string Accessory(int variant, ArtFacing facing);   // WAS: Accessory(int variant)
public static string Top(int variant);                           // facing-free, and correctly so
public static string Legs(int variant);
```

**`Accessory` gained a facing, and this is the entry this whole document exists for.** It took only
`(int variant)` and built a bare `acc_<n>`; the permissive parser reads a missing direction token as
`Down`. The accessory is the one worn layer that is never mirrored — every variant is anchored to a
side or a face (`shoulder_r`, `wrist_r`, `back_center`, `waist_front`) and `CharacterArt.Accessory`
draws all four facings by hand — so the omission did not draw a coarse accessory, it drew **the
front view on a back view**, and four correct drawings per variant existed and never reached a
screen. The builder is now shaped like `Hair`, which always took a facing, and not like `Top` and
`Legs`, whose layers really are facing-free. The permissive parse survives for the world-figure
fallbacks that still build a bare `acc_<n>` by hand (`ArtLibrary.cs:36-42`), which is why the
compiler could not have caught this and cannot catch the next one: **the wrapper is the guard.**
`UiSpriteKeys.Accessory(int index, FacingDirection direction)` is that wrapper for UI callers
(`UIKit.cs:114-118`), and it clamps to `CharacterArt.AccessoryVariants` so a newly drawn variant is
reachable the moment it exists.

**No gold light, no dove, no cross, no praying hands, no robes, no sandals** — rule 13. Note that
`Brand.Secondary` gold *is* ratified as an interaction colour: see `design-system.md` §Gold. It
never lights or decorates a scene.

## Content guardrails for authored text

Forbidden terms are **curated per language**, never translated between them, and live in
`FORBIDDEN_TERMS_BY_LOCALE` in `tools/validate-content.mjs`. A literal translation of the pt-BR list
puts bare "purpose" on the English one, which fires on "picked on purpose" and teaches everyone to
ignore the validator.

The narrator never corrects the player morally. God, Jesus and the Holy Spirit never speak in
authored text — rule 3, without exception. A canonical figure *may* speak authored dialogue that
asserts nothing the passage does not support (rule 4); those nodes carry `canonical_speaker` and
`needs_curation`, and `node tools/list-curation.mjs` prints them for a human read.

## Build/verify commands

```
node tools/fetch-verses.mjs                           # every locale; needs YOUVERSION_API_KEY
node tools/fetch-verses.mjs --locale en               # just one
node tools/validate-content.mjs                       # deterministic layer-1 validator
node tools/list-curation.mjs                          # authored canonical speech awaiting a read
tools/unity-check.sh                                  # headless compile
tools/acceptance.sh                                   # the product rules, per locale
tools/e2e.sh                                          # build a player and play every stage the table declares
tools/tile-preview.sh sheet|zoom|field [d]|check      # the world tiles, drawn without Unity
```

`tools/tile-preview.sh` is a seam of its own, and the reason it is listed here rather than in a
tooling note: it **compiles the shipping `ArtPalette.cs`, `PixelCanvas.cs`, `ValueNoise.cs` and
`TileArt.cs`** — the real files, not copies — against a small `UnityEngine` stub, using the Roslyn
inside Unity. So those four files must stay free of anything Unity actually implements, and drift
breaks that build rather than producing a preview that lies. `check` asserts every pixel is in the
world palette and opaque.

That is also the only palette check that reaches generated **world** tiles, and the trap is the name
of the other one. E2E records *"generated map art uses only the exact world palette"*
(`E2ERunner.cs:1588-1590`), which reads as coverage it does not have: it walks `Image` components and
filters by `IsMapSpriteName`, matching `map_progress_*`, `map_node_*` and `map_reward_*` and nothing
else (`E2ERunner.cs:1685-1710`). That is the progression map's chart art. No tile the tilemap draws
is an `Image`, and neither `tools/acceptance.sh` nor `tools/validate-content.mjs` asserts anything
about a palette. **Any claim that e2e palette-checks the world is wrong.**

## Scene entry points (the integration seam)

Each `.unity` scene holds exactly one bootstrap MonoBehaviour from `SheepGate.Boot`, and each
bootstrap does nothing but delegate to a composer owned by another module. This keeps the
hand-authored YAML trivial while the real construction stays in compiler-checked code.

| Scene | Bootstrap (`SheepGate.Boot`) | Delegates to |
|---|---|---|
| `Boot.unity` | `BootLoader` | `SheepGate.Core.BootSequence.Run()` |
| `CharacterCreation.unity` | `CharacterCreationBootstrap` | `SheepGate.UI.CharacterCreationScreen.Compose()` |
| `Game.unity` | `GameBootstrap` | `SheepGate.World.GameScene.Compose()` |

```csharp
namespace SheepGate.Core {
    public static class BootSequence {
        public static void Run();   // GameData.LoadAll + ScriptureService.Load + Telemetry.Initialize
                                    // + register services, then load "Game" — always.
    }
}
namespace SheepGate.UI    { public static class CharacterCreationScreen { public static void Compose(); } }
namespace SheepGate.World { public static class GameScene             { public static void Compose(); } }
```

Scene names for `SceneManager.LoadScene` are exactly `Boot`, `CharacterCreation`, `Game`.

**Boot always goes to `Game`.** Character creation is no longer a screen in front of the game; it
is a beat inside the opening cutscene, played in the house the neighbour walks the player into, so
the first thing a new player sees is a city rather than a menu. `CharacterCreationScreen.Compose()`
still builds the standalone screen and loads `Game` when it finishes; `Compose(Action onDone)` runs
the same screen inside a live scene and hands control back instead. The `CharacterCreation` scene
stays registered in the build so that route keeps working.

## Fixed asset GUIDs (scene YAML depends on these — do not change)

| Asset | GUID |
|---|---|
| `Assets/Scripts/Boot/BootLoader.cs` | `a1b2c3d4e5f60718293a4b5c6d7e8f90` |
| `Assets/Scripts/Boot/CharacterCreationBootstrap.cs` | `b2c3d4e5f60718293a4b5c6d7e8f90a1` |
| `Assets/Scripts/Boot/GameBootstrap.cs` | `c3d4e5f60718293a4b5c6d7e8f90a1b2` |
| `Assets/Scenes/Boot.unity` | `d4e5f60718293a4b5c6d7e8f90a1b2c3` |
| `Assets/Scenes/CharacterCreation.unity` | `e5f60718293a4b5c6d7e8f90a1b2c3d4` |
| `Assets/Scenes/Game.unity` | `f60718293a4b5c6d7e8f90a1b2c3d4e5` |
