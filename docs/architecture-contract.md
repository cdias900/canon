# Architecture Contract — Sheep Gate

The public seams nobody changes alone. **Accurate as of 2026-08-30**, which is a claim this file has
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

`SheepGate`, with one sub-namespace per folder: `SheepGate.Core`, `SheepGate.Player`,
`SheepGate.World`, `SheepGate.Dialogue`, `SheepGate.Scripture`, `SheepGate.Contest`,
`SheepGate.Vocation`, `SheepGate.Quiz`, `SheepGate.UI`, `SheepGate.Art`.

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

## Art — `SheepGate.Art`

```csharp
public static class ArtLibrary {
    public static Sprite Get(string key);                    // cached, procedurally generated on first use
    public static Sprite GetTinted(string key, Color tint);
}
```
Keys are declared in `SheepGate.Art.ArtKeys` — read that rather than a list here, which is how this
section drifted last time. Ground, rubble and water resolve from a drawn CC0 sheet decoded at
runtime; every other key is generated procedurally, and a key with no drawn tile behind it falls
through to the generated one.

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
```

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
