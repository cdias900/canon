using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using SheepGate.Art;
using SheepGate.Contest;
using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.Scripture;
using SheepGate.Vocation;

namespace SheepGate.World
{
    /// <summary>
    /// The single integration point for the playable scene. Game.unity contains nothing but a
    /// camera and the bootstrap component; that bootstrap calls <see cref="Compose"/> and every
    /// other object in the scene is created here at runtime.
    ///
    /// Compose() is deliberately tolerant: any missing content (map, wall definitions, npc
    /// definitions, art keys, UI types owned by other modules) produces an English warning and
    /// the composition keeps going, so the scene is always playable to the extent possible.
    /// </summary>
    public static class GameScene
    {
        public const string RootName = "World";

        /// <summary>Player object built by <see cref="Compose"/>; null before composition.</summary>
        public static GameObject Player { get; private set; }

        /// <summary>Root transform every world object is parented to.</summary>
        public static Transform Root { get; private set; }

        /// <summary>Runtime tilemap, also the source of the walkable grid for the pathfinder.</summary>
        public static TilemapBuilder MapBuilder { get; private set; }

        /// <summary>Camera rig driving the close follow view and the wide patrol view.</summary>
        public static CameraRig Rig { get; private set; }

        public static void Compose()
        {
            WorldRuntime.ResetCaches();
            EnsureContentLoaded();

            GameState state = WorldRuntime.State;

            GameObject rootObject = new GameObject(RootName);
            Root = rootObject.transform;

            MapDef map = ResolveMap();

            // 1. Tilemap first: everything else needs cell coordinates and the walkable grid.
            GameObject tilemapObject = new GameObject("Tilemap");
            tilemapObject.transform.SetParent(Root, false);
            TilemapBuilder builder = tilemapObject.AddComponent<TilemapBuilder>();
            builder.Build(map);
            MapBuilder = builder;
            SafeRegister(builder);

            // 2. Systems, before anything that talks to them.
            GameObject systemsObject = new GameObject("Systems");
            systemsObject.transform.SetParent(Root, false);
            ResourceSystem resources = systemsObject.AddComponent<ResourceSystem>();
            WallSystem wall = systemsObject.AddComponent<WallSystem>();
            DayCycle dayCycle = systemsObject.AddComponent<DayCycle>();
            SafeRegister(resources);
            SafeRegister(wall);
            SafeRegister(dayCycle);

            // 3. Wall segments (visuals plus their interactables).
            wall.Build(builder);

            // 4. Props.
            BuildRubblePiles(map, builder);
            BuildWell(map, builder);

            // 5. Residents.
            BuildNpcs(builder);

            // 6. Player, with the appearance chosen in character creation.
            Player = BuildPlayer(map, builder, state);

            // 7. Camera rig.
            Rig = SetUpCamera(builder, Player);

            // 8. Systems owned by other modules that the world expects to find in the scene.
            EnsureDialogueSystem(systemsObject);
            EnsureContestSystem(systemsObject);
            EnsureQuizSystem(systemsObject);

            // 9. The director of the last day. Nothing else starts the trial or ends the run.
            EnsureDay3Director(systemsObject);

            // 10. HUD and the rest of the UI layer. The HUD finds the camera rig through the
            // service locator — registered in step 7 — and drives the patrol view itself, so the
            // world layer wires nothing between the two.
            ComposeUserInterface();

            // 11. Silent map-edge observation (exile vocation, never displayed).
            MapEdgeWatcher watcher = rootObject.AddComponent<MapEdgeWatcher>();
            watcher.Configure(builder, Player);

            Debug.Log("[World] GameScene composed. Day " + state.day + ", map " + builder.Width + "x" + builder.Height + ".");
        }

        private static void EnsureContentLoaded()
        {
            // Emptiness, not null: GameData initialises every property to an empty array or an
            // empty dictionary, so a null test can never be true and opening Game.unity directly
            // would compose a village with no residents, no wall and no dialogue. GameData.LoadAll
            // is documented as safe to call more than once — the last read wins — so reloading when
            // any of the three sets the world needs came back empty costs nothing.
            bool needsLoad = false;
            try
            {
                WallSegmentDef[] segments = GameData.WallSegments;
                NpcDef[] npcs = GameData.Npcs;
                IReadOnlyDictionary<string, DialogueNode> dialogue = GameData.Dialogue;

                needsLoad = segments == null || segments.Length == 0
                            || npcs == null || npcs.Length == 0
                            || dialogue == null || dialogue.Count == 0;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] GameData was not readable before LoadAll: " + exception.Message);
                needsLoad = true;
            }

            if (needsLoad)
            {
                try
                {
                    GameData.LoadAll();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[World] GameData.LoadAll failed: " + exception.Message);
                }
            }

            try
            {
                if (ScriptureService.Version == null)
                {
                    ScriptureService.Load();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] ScriptureService.Load failed: " + exception.Message);
            }
        }

        private static void SafeRegister<T>(T service) where T : class
        {
            try
            {
                ServiceLocator.Register(service);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Could not register " + typeof(T).Name + ": " + exception.Message);
            }
        }

        private static MapDef ResolveMap()
        {
            MapDef map = null;
            try
            {
                map = GameData.Map;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Reading GameData.Map failed: " + exception.Message);
            }

            bool usable = map != null && map.width > 0 && map.height > 0 && map.rows != null && map.rows.Length > 0;
            if (!usable)
            {
                Debug.LogWarning("[World] Map definition missing or empty; composing the fallback layout instead.");
                map = BuildFallbackMap();
            }

            return map;
        }

        /// <summary>
        /// Minimal playable layout used when no map file ships with the build: a walled village
        /// with a spawn point, five rubble cells and a well.
        /// </summary>
        private static MapDef BuildFallbackMap()
        {
            const int width = 40;
            const int height = 22;
            const int wallRow = 15;

            string[] rows = new string[height];
            for (int rowIndex = 0; rowIndex < height; rowIndex++)
            {
                int y = height - 1 - rowIndex;
                char[] line = new char[width];
                for (int x = 0; x < width; x++)
                {
                    char cell = TilemapBuilder.GroundChar;
                    if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                    {
                        cell = TilemapBuilder.HouseChar;
                    }
                    else if (y == wallRow)
                    {
                        cell = TilemapBuilder.WallChar;
                    }
                    else if (y < wallRow && (x == 5 || x == 6) && (y == 3 || y == 4))
                    {
                        cell = TilemapBuilder.WaterChar;
                    }
                    else if (y < wallRow && (x == 9 || x == 10) && (y == 11 || y == 12))
                    {
                        cell = TilemapBuilder.HouseChar;
                    }
                    else if (y < wallRow && (x == 27 || x == 28) && (y == 11 || y == 12))
                    {
                        cell = TilemapBuilder.HouseChar;
                    }
                    else if (y < wallRow && (x >= 16 && x <= 23) && y == 13)
                    {
                        cell = TilemapBuilder.RubbleChar;
                    }

                    line[x] = cell;
                }

                rows[rowIndex] = new string(line);
            }

            MapDef map = new MapDef();
            map.width = width;
            map.height = height;
            map.rows = rows;
            map.player_spawn = new GridPos { x = 20, y = 8 };
            map.well = new GridPos { x = 13, y = 6 };
            map.rubble = new GridPos[]
            {
                new GridPos { x = 17, y = 11 },
                new GridPos { x = 21, y = 12 },
                new GridPos { x = 25, y = 9 },
                new GridPos { x = 30, y = 12 },
                new GridPos { x = 34, y = 8 }
            };
            return map;
        }

        private static void BuildRubblePiles(MapDef map, TilemapBuilder builder)
        {
            GridPos[] spots = map != null ? map.rubble : null;
            if (spots == null || spots.Length == 0)
            {
                Debug.LogWarning("[World] Map has no rubble positions; no rubble piles were placed.");
                return;
            }

            GameObject parent = new GameObject("RubblePiles");
            parent.transform.SetParent(Root, false);

            for (int i = 0; i < spots.Length; i++)
            {
                GridPos spot = spots[i];
                if (spot == null)
                {
                    continue;
                }

                Vector2Int cell = builder.NearestWalkable(builder.ClampCell(new Vector2Int(spot.x, spot.y)));
                RubblePile.Spawn(i, cell, parent.transform, builder);
            }
        }

        private static void BuildWell(MapDef map, TilemapBuilder builder)
        {
            GridPos spot = map != null ? map.well : null;
            if (spot == null)
            {
                Debug.LogWarning("[World] Map has no well position; the well was not placed.");
                return;
            }

            Vector2Int cell = builder.NearestWalkable(builder.ClampCell(new Vector2Int(spot.x, spot.y)));
            Well.Spawn(cell, Root, builder);
        }

        private static void BuildNpcs(TilemapBuilder builder)
        {
            NpcDef[] defs = null;
            try
            {
                defs = GameData.Npcs;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Reading GameData.Npcs failed: " + exception.Message);
            }

            if (defs == null || defs.Length == 0)
            {
                Debug.LogWarning("[World] No npc definitions found; the village will be empty.");
                return;
            }

            GameObject parent = new GameObject("Residents");
            parent.transform.SetParent(Root, false);

            for (int i = 0; i < defs.Length; i++)
            {
                NpcDef def = defs[i];
                if (def == null || string.IsNullOrEmpty(def.id))
                {
                    Debug.LogWarning("[World] Skipped an npc definition without an id.");
                    continue;
                }

                NpcActor.Spawn(def, parent.transform, builder);
            }
        }

        private static GameObject BuildPlayer(MapDef map, TilemapBuilder builder, GameState state)
        {
            Vector2Int spawn = new Vector2Int(builder.Width / 2, builder.Height / 2);
            if (map != null && map.player_spawn != null)
            {
                spawn = new Vector2Int(map.player_spawn.x, map.player_spawn.y);
            }

            spawn = builder.NearestWalkable(builder.ClampCell(spawn));

            GameObject playerObject = new GameObject("Player");
            playerObject.transform.SetParent(Root, false);
            playerObject.transform.position = builder.CellToWorldCenter(spawn);

            try
            {
                playerObject.tag = "Player";
            }
            catch (Exception)
            {
                // The "Player" tag is a Unity default; ignore projects where tags were stripped.
            }

            AppearanceState appearance = state != null && state.appearance != null ? state.appearance : new AppearanceState();

            Type appearanceType = TypeBridge.Find("SheepGate.Player.CharacterAppearance");
            if (appearanceType != null)
            {
                Component component = TypeBridge.AddComponent(playerObject, appearanceType);
                bool applied = TypeBridge.Invoke(component, ApplyAppearanceMethods, new object[] { appearance })
                               || TypeBridge.Invoke(component, ApplyAppearanceMethods, new object[0]);
                if (!applied)
                {
                    Debug.LogWarning("[World] CharacterAppearance was attached but no apply method matched; it is expected to read GameState itself.");
                }
            }
            else
            {
                Debug.LogWarning("[World] SheepGate.Player.CharacterAppearance not found; using the world fallback sprite layers.");
                BuildFallbackAppearance(playerObject, appearance, builder, spawn.y);
            }

            Type controllerType = TypeBridge.Find("SheepGate.Player.PlayerController");
            if (controllerType != null)
            {
                TypeBridge.AddComponent(playerObject, controllerType);
            }
            else
            {
                Debug.LogWarning("[World] SheepGate.Player.PlayerController not found; the player object cannot move yet.");
            }

            return playerObject;
        }

        private static readonly string[] ApplyAppearanceMethods = { "Apply", "SetAppearance", "Set", "Rebuild", "Refresh", "Build" };

        private static void BuildFallbackAppearance(GameObject owner, AppearanceState appearance, TilemapBuilder builder, int cellY)
        {
            int order = WorldRuntime.SortingOrderForCell(builder != null ? builder.Height : 0, cellY);
            AddLayer(owner, "Legs", WorldRuntime.FirstSprite("legs_" + appearance.legs), order + 1, new Color(0.36f, 0.33f, 0.30f));
            AddLayer(owner, "Body", WorldRuntime.FirstSprite(
                "body_" + appearance.body + "_down_0",
                "body_" + appearance.body + "_down",
                "body_" + appearance.body + "_0",
                "body_" + appearance.body), order + 2, new Color(0.72f, 0.60f, 0.48f));
            AddLayer(owner, "Top", WorldRuntime.FirstSprite("top_" + appearance.top), order + 3, new Color(0.55f, 0.42f, 0.34f));
            AddLayer(owner, "Accessory", WorldRuntime.FirstSprite("acc_" + appearance.accessory), order + 4, new Color(0.42f, 0.45f, 0.48f));
        }

        private static void AddLayer(GameObject owner, string layerName, Sprite sprite, int sortingOrder, Color fallbackColor)
        {
            GameObject layer = new GameObject(layerName);
            layer.transform.SetParent(owner.transform, false);
            layer.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            SpriteRenderer renderer = layer.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = sortingOrder;
            if (sprite != null)
            {
                renderer.sprite = sprite;
            }
            else
            {
                renderer.sprite = WorldRuntime.SolidSprite(fallbackColor);
                renderer.enabled = layerName == "Body";
            }
        }

        private static CameraRig SetUpCamera(TilemapBuilder builder, GameObject player)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[World] No camera tagged MainCamera was found; creating one.");
                GameObject cameraObject = new GameObject("Main Camera");
                camera = cameraObject.AddComponent<Camera>();
                try
                {
                    cameraObject.tag = "MainCamera";
                }
                catch (Exception)
                {
                    // Ignore projects without the default tag.
                }
            }

            camera.orthographic = true;
            camera.orthographicSize = CameraRig.CloseSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.12f, 1f);

            CameraRig rig = camera.GetComponent<CameraRig>();
            if (rig == null)
            {
                rig = camera.gameObject.AddComponent<CameraRig>();
            }

            rig.Configure(builder, player != null ? player.transform : null);
            SafeRegister(rig);
            return rig;
        }

        private static void EnsureDialogueSystem(GameObject host)
        {
            DialogueSystem existing = UnityEngine.Object.FindFirstObjectByType<DialogueSystem>();
            if (existing != null)
            {
                return;
            }

            try
            {
                host.AddComponent<DialogueSystem>();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Could not create the DialogueSystem: " + exception.Message);
            }
        }

        private static void EnsureContestSystem(GameObject host)
        {
            MoraleContest existing = UnityEngine.Object.FindFirstObjectByType<MoraleContest>();
            if (existing != null)
            {
                return;
            }

            try
            {
                // Created on every day; Day3Director is what calls Begin(), and only on the last one.
                host.AddComponent<MoraleContest>();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Could not create the MoraleContest: " + exception.Message);
            }
        }

        /// <summary>
        /// Attaches the director of the last day. Without it the trial never begins, the page never
        /// appears and no vocation is ever named, so a failure here is logged loudly.
        /// </summary>
        private static void EnsureDay3Director(GameObject host)
        {
            Day3Director existing = UnityEngine.Object.FindFirstObjectByType<Day3Director>();
            if (existing != null)
            {
                return;
            }

            try
            {
                host.AddComponent<Day3Director>();
            }
            catch (Exception exception)
            {
                Debug.LogError("[World] Could not create the Day3Director; day three has no ending: " + exception.Message);
            }
        }

        private static void EnsureQuizSystem(GameObject host)
        {
            Type quizType = TypeBridge.Find("SheepGate.Quiz.DailyQuiz");
            if (quizType == null || !typeof(Component).IsAssignableFrom(quizType))
            {
                return;
            }

            UnityEngine.Object existing = UnityEngine.Object.FindFirstObjectByType(quizType);
            if (existing != null)
            {
                return;
            }

            TypeBridge.AddComponent(host, quizType);
        }

        private static readonly string[] UiComposeMethods = { "Compose", "Mount", "Create", "Build", "Install", "Show" };

        private static void ComposeUserInterface()
        {
            Type hudType = TypeBridge.Find("SheepGate.UI.HUD");
            if (hudType == null)
            {
                Debug.LogWarning("[World] SheepGate.UI.HUD not found; the scene runs without a HUD.");
                return;
            }

            if (TypeBridge.InvokeStatic(hudType, UiComposeMethods, new object[0]))
            {
                return;
            }

            if (!typeof(Component).IsAssignableFrom(hudType))
            {
                Debug.LogWarning("[World] SheepGate.UI.HUD exposes no static composer and is not a component; the HUD was not wired.");
                return;
            }

            UnityEngine.Object existing = UnityEngine.Object.FindFirstObjectByType(hudType);
            if (existing != null)
            {
                return;
            }

            GameObject hudObject = new GameObject("HUD");
            Component hud = TypeBridge.AddComponent(hudObject, hudType);
            if (hud == null)
            {
                Debug.LogWarning("[World] SheepGate.UI.HUD could not be attached.");
            }
        }
    }

    /// <summary>
    /// Watches the player for the map edge. The exile vocation scores for walking to the border;
    /// nothing about it is ever displayed, which is why this lives outside any UI code.
    /// </summary>
    internal sealed class MapEdgeWatcher : MonoBehaviour
    {
        private const float PollSeconds = 0.25f;
        private const int EdgeMargin = 1;

        private TilemapBuilder _tilemap;
        private Transform _player;
        private float _nextPoll;

        public void Configure(TilemapBuilder tilemap, GameObject player)
        {
            _tilemap = tilemap;
            _player = player != null ? player.transform : null;
            enabled = _tilemap != null && _player != null;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextPoll)
            {
                return;
            }

            _nextPoll = Time.unscaledTime + PollSeconds;

            if (_tilemap == null || _player == null)
            {
                return;
            }

            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            if (state.HasFlag(WorldRuntime.FlagReachedMapEdge))
            {
                enabled = false;
                return;
            }

            Vector2Int cell = _tilemap.WorldToCell(_player.position);
            bool atEdge = cell.x <= EdgeMargin
                          || cell.y <= EdgeMargin
                          || cell.x >= _tilemap.Width - 1 - EdgeMargin
                          || cell.y >= _tilemap.Height - 1 - EdgeMargin;

            if (!atEdge)
            {
                return;
            }

            state.SetFlag(WorldRuntime.FlagReachedMapEdge);
            WorldRuntime.AwardOnce("exile_map_edge_awarded", WorldRuntime.VocationExile, 2);
            WorldRuntime.SaveNow();
            enabled = false;
        }
    }

    /// <summary>
    /// Shared helpers for the world layer: state access, silent vocation scoring, art access with
    /// fallbacks, and dialogue lookups. Everything here is internal on purpose: no UI may read it.
    /// </summary>
    internal static class WorldRuntime
    {
        // Flags are declared locally as literals so the world layer never depends on the exact
        // constant member names of SheepGate.Core.GameFlags. The values are the contract values.
        public const string FlagWatchPostedD1 = "watch_posted_d1";
        public const string FlagWatchPostedD2 = "watch_posted_d2";
        public const string FlagFishCaught = "fish_caught";
        public const string FlagReachedMapEdge = "reached_map_edge";

        // Vocation ids as authored in vocations.json.
        public const string VocationZealot = "zelote";
        public const string VocationScribe = "escriba";
        public const string VocationShepherd = "pastor";
        public const string VocationExile = "exilado";
        public const string VocationProphet = "profeta";
        public const string VocationSteward = "mordomo";

        private static GameState _state;
        private static bool _vocationWarningLogged;
        private static readonly HashSet<string> WarnedSpriteKeys = new HashSet<string>();
        private static readonly Dictionary<string, Sprite> SolidSprites = new Dictionary<string, Sprite>();

        public static void ResetCaches()
        {
            _state = null;
            _vocationWarningLogged = false;
            WarnedSpriteKeys.Clear();
        }

        public static GameState State
        {
            get
            {
                // The ServiceLocator is asked first, every time, rather than trusting the cache.
                // Caching the first state seen would keep the world writing flags into a stale
                // object after a run is restarted in-process — the Reset Save menu item, or any
                // return to character creation — because ServiceLocator.Clear cannot reach a
                // static field here. The cache stays only as a fallback so a scene without a
                // registered state does not hit SaveSystem.Load on every access.
                GameState resolved = null;
                try
                {
                    ServiceLocator.TryGet(out resolved);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[World] ServiceLocator lookup for GameState failed: " + exception.Message);
                }

                if (resolved == null)
                {
                    resolved = _state;
                }

                if (resolved == null)
                {
                    try
                    {
                        resolved = SaveSystem.Load();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[World] SaveSystem.Load failed: " + exception.Message);
                    }

                    if (resolved == null)
                    {
                        Debug.LogWarning("[World] No GameState was registered and no save was found; starting from a fresh state.");
                        resolved = new GameState();
                    }

                    try
                    {
                        ServiceLocator.Register(resolved);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[World] Could not register the recovered GameState: " + exception.Message);
                    }
                }

                _state = resolved;
                return _state;
            }
        }

        public static void SaveNow()
        {
            GameState state = State;
            if (state == null)
            {
                return;
            }

            try
            {
                SaveSystem.Save(state);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] SaveSystem.Save failed: " + exception.Message);
            }
        }

        /// <summary>Adds vocation points. Never logs the amount: progress must stay invisible.</summary>
        public static void AddVocation(string vocationId, int points)
        {
            if (string.IsNullOrEmpty(vocationId) || points == 0)
            {
                return;
            }

            VocationTracker tracker = null;
            try
            {
                ServiceLocator.TryGet(out tracker);
            }
            catch (Exception)
            {
                tracker = null;
            }

            if (tracker == null)
            {
                if (!_vocationWarningLogged)
                {
                    _vocationWarningLogged = true;
                    Debug.LogWarning("[World] No VocationTracker is registered; world actions cannot be scored.");
                }

                return;
            }

            try
            {
                tracker.Add(vocationId, points);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] VocationTracker.Add failed: " + exception.Message);
            }
        }

        /// <summary>Scores a vocation action at most once for the whole run, tracked in the save.</summary>
        public static void AwardOnce(string counterKey, string vocationId, int points)
        {
            GameState state = State;
            if (state == null || string.IsNullOrEmpty(counterKey))
            {
                return;
            }

            if (state.Counter(counterKey) != 0)
            {
                return;
            }

            state.counters[counterKey] = 1;
            AddVocation(vocationId, points);
        }

        public static int SortingOrderForCell(int mapHeight, int cellY)
        {
            int height = mapHeight > 0 ? mapHeight : 32;
            return 100 + (height - Mathf.Clamp(cellY, 0, height)) * 8;
        }

        public static Sprite GetSprite(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            try
            {
                return ArtLibrary.Get(key);
            }
            catch (Exception exception)
            {
                if (WarnedSpriteKeys.Add(key))
                {
                    Debug.LogWarning("[World] ArtLibrary.Get(\"" + key + "\") failed: " + exception.Message);
                }

                return null;
            }
        }

        public static Sprite GetTintedSprite(string key, Color tint)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            try
            {
                return ArtLibrary.GetTinted(key, tint);
            }
            catch (Exception)
            {
                return GetSprite(key);
            }
        }

        public static Sprite FirstSprite(params string[] keys)
        {
            if (keys == null)
            {
                return null;
            }

            for (int i = 0; i < keys.Length; i++)
            {
                Sprite sprite = GetSprite(keys[i]);
                if (sprite != null)
                {
                    return sprite;
                }
            }

            return null;
        }

        /// <summary>
        /// Flat colour sprite used only when an art key is unavailable, so a missing art library
        /// never produces an invisible scene.
        /// </summary>
        public static Sprite SolidSprite(Color color)
        {
            string key = ColorUtility.ToHtmlStringRGBA(color);
            Sprite cached;
            if (SolidSprites.TryGetValue(key, out cached) && cached != null)
            {
                return cached;
            }

            const int size = 8;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[size * size];
            Color32 packed = color;
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = packed;
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = "world_fallback_" + key;
            SolidSprites[key] = sprite;
            return sprite;
        }

        public static bool DialogueNodeExists(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return false;
            }

            try
            {
                IReadOnlyDictionary<string, DialogueNode> nodes = GameData.Dialogue;
                return nodes != null && nodes.ContainsKey(nodeId);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string FirstExistingNode(params string[] candidates)
        {
            if (candidates == null)
            {
                return null;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                if (DialogueNodeExists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            return null;
        }

        public static DialogueSystem FindDialogueSystem()
        {
            DialogueSystem system = null;
            try
            {
                ServiceLocator.TryGet(out system);
            }
            catch (Exception)
            {
                system = null;
            }

            if (system == null)
            {
                system = UnityEngine.Object.FindFirstObjectByType<DialogueSystem>();
            }

            return system;
        }

        /// <summary>Plays an authored dialogue node. Returns false when nothing could be played.</summary>
        public static bool PlayDialogue(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return false;
            }

            DialogueSystem system = FindDialogueSystem();
            if (system == null)
            {
                Debug.LogWarning("[World] No DialogueSystem in the scene; node \"" + nodeId + "\" was not played.");
                return false;
            }

            if (system.IsPlaying)
            {
                return false;
            }

            try
            {
                system.Play(nodeId);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] DialogueSystem.Play(\"" + nodeId + "\") failed: " + exception.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// Late-bound access to types owned by other modules whose signatures are not frozen by the
    /// architecture contract (UI panels, player components, the URP 2D light). Reflection keeps the
    /// world layer compiling and running whether or not those modules are present.
    /// </summary>
    internal static class TypeBridge
    {
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();

        public static Type Find(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            Type cached;
            if (TypeCache.TryGetValue(fullName, out cached))
            {
                return cached;
            }

            Type found = null;
            try
            {
                found = Type.GetType(fullName);
            }
            catch (Exception)
            {
                found = null;
            }

            if (found == null)
            {
                try
                {
                    Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < assemblies.Length && found == null; i++)
                    {
                        try
                        {
                            found = assemblies[i].GetType(fullName);
                        }
                        catch (Exception)
                        {
                            found = null;
                        }
                    }
                }
                catch (Exception)
                {
                    found = null;
                }
            }

            TypeCache[fullName] = found;
            return found;
        }

        public static Component AddComponent(GameObject owner, Type componentType)
        {
            if (owner == null || componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                return null;
            }

            try
            {
                return owner.AddComponent(componentType);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Could not attach " + componentType.FullName + ": " + exception.Message);
                return null;
            }
        }

        public static bool InvokeStatic(Type type, string[] methodNames, object[] args)
        {
            if (type == null || methodNames == null)
            {
                return false;
            }

            MethodInfo method = FindMethod(type, methodNames, args, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (method == null)
            {
                return false;
            }

            try
            {
                method.Invoke(null, args);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Static call " + type.FullName + "." + method.Name + " failed: " + exception.Message);
                return false;
            }
        }

        public static bool Invoke(object target, string[] methodNames, object[] args)
        {
            if (target == null || methodNames == null)
            {
                return false;
            }

            MethodInfo method = FindMethod(target.GetType(), methodNames, args, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (method == null)
            {
                return false;
            }

            try
            {
                method.Invoke(target, args);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Call " + target.GetType().FullName + "." + method.Name + " failed: " + exception.Message);
                return false;
            }
        }

        public static PropertyInfo FindProperty(Type type, string propertyName)
        {
            if (type == null || string.IsNullOrEmpty(propertyName))
            {
                return null;
            }

            try
            {
                return type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static MethodInfo FindMethod(Type type, string[] methodNames, object[] args, BindingFlags flags)
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(flags);
            }
            catch (Exception)
            {
                return null;
            }

            for (int nameIndex = 0; nameIndex < methodNames.Length; nameIndex++)
            {
                string wanted = methodNames[nameIndex];
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].Name != wanted)
                    {
                        continue;
                    }

                    if (Matches(methods[i], args))
                    {
                        return methods[i];
                    }
                }
            }

            return null;
        }

        private static bool Matches(MethodInfo method, object[] args)
        {
            ParameterInfo[] parameters = method.GetParameters();
            int argCount = args != null ? args.Length : 0;
            if (parameters.Length != argCount)
            {
                return false;
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                object arg = args[i];
                if (arg == null)
                {
                    if (parameters[i].ParameterType.IsValueType)
                    {
                        return false;
                    }

                    continue;
                }

                if (!parameters[i].ParameterType.IsInstanceOfType(arg))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
