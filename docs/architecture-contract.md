# Architecture Contract — Sheep Gate POC

Binding interface spec for all implementation agents. Every public signature here is
frozen: implement exactly, do not rename, do not add parameters. If something is missing,
add it *inside* your own files without changing anything declared here.

## Non-negotiable rules (repeat to every agent)

1. **English only** for identifiers, comments, file names, commit messages, docs.
   **pt-BR only** for strings displayed to the player.
2. **Scripture never appears as literal text** in C#, JSON authored by hand, prompts, or
   tests. Only references circulate: `NEH.4.6`. Literal text lives solely in the generated
   `verses.json`. Test fixtures use obviously synthetic strings such as
   `"PLACEHOLDER_VERSE_TEXT"`.
3. **No vocation progress may reach any UI.** `VocationTracker` exposes no public score
   getter. Only `Resolve()` at the end of day 3.
4. **No game over, no death counter, no health bar.** Morale only. Completed wall stages
   never regress.
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
  `POC-IMPLEMENTATION.md`, which asked for a CC0 tileset from the start.
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
    public int stage;              // 0..4, 4 == complete
    public int workInStage;        // work units accumulated toward next stage
    public bool damaged;
}

[Serializable]
public class AppearanceState {
    public int body;               // 0..1
    public int top;                // 0..3
    public int legs;               // 0..3
    public int accessory;          // 0..3
}

[Serializable]
public class GameState {
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

```csharp
public static class SaveSystem {
    public static string SavePath { get; }               // persistentDataPath/save.json
    public static bool HasSave();
    public static GameState Load();                      // null when absent or corrupt
    public static void Save(GameState state);
    public static void Delete();
}
```

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
public static class GameData {                          // loads every JSON in Resources/Data
    public static void LoadAll();
    public static NpcDef[] Npcs { get; }
    public static IReadOnlyDictionary<string, DialogueNode> Dialogue { get; }
    public static WallSegmentDef[] WallSegments { get; }
    public static ContestConfig Contest { get; }
    public static VocationDef[] Vocations { get; }
    public static QuizQuestion[] Quiz { get; }
    public static MapDef Map { get; }
}
```

## Data transfer objects — `SheepGate.Core` (Newtonsoft attributes)

```csharp
public class NpcDef      { string id; string display; string source_ref; GridPos spawn; string palette; }
public class GridPos     { int x; int y; }
public class DialogueLine{ string text; string verse; string frame; }   // text XOR verse
public class DialogueNode{ string npc; int day; DialogueLine[] lines; Grants grants; bool reliable; }
public class Grants      { Dictionary<string,int> vocation; Dictionary<string,int> flags; string set_flag; }
public class WallSegmentDef { string id; int grid_x; int[] stage_cost; bool exposed; }
public class VocationDef { string id; string display; string reveal_line; }   // reveal_line is pt-BR, authored, never scripture
public class QuizQuestion{ int day; string prompt; string[] options; int answer; string note; }
public class MapDef      { int width; int height; string[] rows; GridPos player_spawn; GridPos[] rubble; GridPos well; }
public class ContestConfig { int player_morale; int enemy_resolve_base; int turn_limit; ContestMoveDef[] moves; }
public class ContestMoveDef { string id; string display; string description; int resolve_delta; int morale_delta; bool unlocked_by_page; }
```

## Scripture — `SheepGate.Scripture`

```csharp
public class VerseEntry   { public string ref_display; public string text; }
public class ChapterVerse { public int n; public string text; }
public class ChapterEntry { public string ref_display; public ChapterVerse[] verses; }
public class VersionInfo  { public string id; public string abbrev; public string copyright; }

public static class ScriptureService {
    public static bool IsPlaceholderBuild { get; }       // true when verses.json was generated without a real fetch
    public static VersionInfo Version { get; }
    public static void Load();                           // reads Resources/Data/verses.json, never hits network
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
    public event Action<int> MorningStarted;                 // day number
    public void RequestEndDay();                             // opens the split panel
    public void EndDay(int workers, int watchers);           // resolves the night, advances the day
    public float NightAmount { get; }                        // 0 day .. 1 night, drives Light2D or the tint overlay
}
```

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
    public void Begin();
    public void UseMove(string moveId);
    public bool IsMoveAvailable(string moveId);
}
```

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
Keys: `tile_ground`, `tile_rubble`, `tile_water`, `tile_house`, `wall_0`..`wall_4`,
`prop_rubble`, `prop_well`, `body_0`/`body_1` + `_dir` + `_frame`, `top_0..3`, `legs_0..3`,
`acc_0..3`, `ui_panel`, `ui_bubble`, `ui_button`. Palette is limited to three base colors
plus neutrals. No gold light, no dove, no cross, no praying hands, no robes, no sandals.

## Content guardrails for authored pt-BR text

Forbidden words anywhere in player-facing strings: bênção, propósito, jornada de fé,
devocional, versículo do dia, testemunho, "Deus tem um plano". The narrator never corrects
the player morally. God, Jesus and the Holy Spirit never speak in authored text.

## Build/verify commands

```
node tools/fetch-verses.mjs --provider youversion     # needs YOUVERSION_API_KEY
node tools/fetch-verses.mjs --placeholder             # structure-only, marks is_placeholder
node tools/validate-content.mjs                       # deterministic layer-1 validator
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
