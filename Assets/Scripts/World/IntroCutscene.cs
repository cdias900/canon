using System;
using System.Collections;
using System.Collections.Generic;
using SheepGate.Art;
using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.Player;
using SheepGate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.World
{
    /// <summary>
    /// The opening. It runs once, on the first entry into the village, and it exists to do three
    /// things before the player is handed the controls:
    ///
    ///   1. Show that this city is one of several, and that the others are not open yet.
    ///   2. Show that this city's wall is down, which is the whole reason there is a game.
    ///   3. Put the player inside the crowd rather than in front of a menu.
    ///
    /// The player clicks through it and cannot skip it, but they are never asked to read anything:
    /// every verse here is spoken by a figure who actually said it, and the reading proper stays
    /// optional (AGENTS.md rules 19 and 20).
    ///
    /// The man on the stone is NOT named. Deferring a name is legitimate and removing one is not;
    /// he is "o homem da capital" until the day the page falls. Because he is a canonical figure
    /// with attested speech, every line of his is a verse reference resolved through the scripture
    /// service — this class never writes words for him.
    /// </summary>
    public sealed class IntroCutscene : MonoBehaviour
    {
        /// <summary>Raised once the player has control of the village.</summary>
        public static event Action Finished;

        const string SeenFlag = "intro_seen";

        const string NodeWorldMap = "intro_worldmap";
        const string NodeArrival = "intro_arrival";
        const string NodeSummons = "intro_summons";
        const string NodeDressed = "intro_dressed";
        const string NodeGathering = "intro_gathering";

        const float WorldMapHold = 1.1f;
        const float ZoomSeconds = 2.2f;
        const float FadeSeconds = 0.45f;
        const float BeatPause = 0.35f;

        /// <summary>
        /// How long after he sets off before you follow. Deliberately tiny: he is guiding you, not
        /// going on ahead. He walks at the player's own speed on the player's own path, so this
        /// delay is the entire gap between you and it stays constant the whole way.
        /// </summary>
        const float FollowDelay = 0.1f;

        /// <summary>How far out the camera sits while the world map is on screen.</summary>
        const float WorldSize = 34f;

        public static bool IsPlaying { get; private set; }

        TilemapBuilder _map;
        CameraRig _rig;
        PlayerController _player;
        DialogueSystem _dialogue;
        WorldMapOverlay _worldMap;
        readonly List<CutsceneActor> _crowd = new List<CutsceneActor>();
        readonly List<Vector2Int> _crowdSpots = new List<Vector2Int>();
        GameObject _stone;
        CutsceneActor _neighbour;
        CutsceneActor _governor;
        Image _fade;

        /// <summary>
        /// Attaches the cutscene when it still has something to do. Returns null once the opening
        /// has been seen, so a resumed save drops straight into the village.
        /// </summary>
        public static IntroCutscene TryAttach(GameObject host, TilemapBuilder map, CameraRig rig, GameObject playerObject)
        {
            PlayerController player = playerObject != null ? playerObject.GetComponent<PlayerController>() : null;

            GameState state = WorldRuntime.State;
            if (state == null || state.HasFlag(SeenFlag) || state.day > 1)
            {
                return null;
            }

            if (host == null || map == null || rig == null || player == null)
            {
                Debug.LogWarning("[World] The opening cannot run without a map, a camera and a player.");
                return null;
            }

            var cutscene = host.AddComponent<IntroCutscene>();
            cutscene._map = map;
            cutscene._rig = rig;
            cutscene._player = player;
            return cutscene;
        }

        void Start()
        {
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            IsPlaying = true;
            _dialogue = WorldRuntime.FindDialogueSystem();

            // The village is a place, not a control surface, until the crowd disperses.
            _player.InputEnabled = false;
            SetHudVisible(false);

            Vector3 playerPos = _player.transform.position;
            Vector2Int playerCell = _player.GridPosition;

            BuildFade();

            // --- 1. The region ---------------------------------------------------------------
            // Framed before the fade lifts, so the first thing on screen is already the wide shot.
            _worldMap = WorldMapOverlay.Show(_map, transform);
            _rig.FrameCutscene(_map.CenterWorld(), WorldMapOverlay.FramingSize, 0f);
            yield return Fade(1f, 0f, FadeSeconds);
            yield return new WaitForSeconds(WorldMapHold);
            yield return PlayNode(NodeWorldMap);

            // --- 2. Down into this one -------------------------------------------------------
            _rig.FrameCutscene(playerPos, CameraRig.CloseSize, ZoomSeconds);
            if (_worldMap != null)
            {
                yield return _worldMap.FadeOut(ZoomSeconds * 0.5f);
            }

            yield return new WaitForSeconds(ZoomSeconds * 0.5f);
            _rig.SetTarget(_player.transform);
            yield return new WaitForSeconds(BeatPause);

            // --- 3. Someone crosses the square -----------------------------------------------
            // He is spawned away from the player and walks over. Nobody speaks from off screen.
            Vector2Int approachFrom = FindOpenCellNear(playerCell, 6);
            _neighbour = CutsceneActor.Spawn("Neighbour", approachFrom, _map, transform, 1,
                new Color(0.62f, 0.53f, 0.44f, 1f));

            Vector2Int besidePlayer = FindOpenCellNear(playerCell, 1);
            yield return _neighbour.WalkToCell(besidePlayer);
            _neighbour.FaceTowards(_player.transform.position);
            FacePlayerTowards(_neighbour.transform.position);
            yield return new WaitForSeconds(BeatPause);

            yield return PlayNode(NodeArrival);
            yield return PlayNode(NodeSummons);

            // --- 4. The house ----------------------------------------------------------------
            // He leads. He offered the clothes, it is his door, and a stranger who walks in first
            // is a stranger letting himself into someone else's house.
            Vector2Int door = CellFrom(GameData.Map != null ? GameData.Map.player_house_door : null, new Vector2Int(14, 9));

            // You travel together. He pushes off first and you fall in behind a beat later, close
            // enough that it reads as being led rather than as two people taking turns walking.
            bool neighbourInside = false;
            if (_neighbour != null)
            {
                // Both of you walk the identical route, found once from where the player stands.
                StartCoroutine(LeadToDoor(door, PathForPlayer(door), () => neighbourInside = true));
                yield return new WaitForSeconds(FollowDelay);
            }

            yield return WalkTo(door);

            // He is normally through the door before you reach it; wait only if he is not.
            float waited = 0f;
            while (_neighbour != null && !neighbourInside && waited < 6f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            yield return new WaitForSeconds(BeatPause);
            yield return Fade(0f, 1f, FadeSeconds);

            bool dressed = false;
            CharacterCreationScreen.Compose(() => dressed = true, CharacterCreationScreen.CutsceneSortingOrder);
            while (!dressed)
            {
                yield return null;
            }

            ReapplyAppearance();

            // Both of you step back out, him first again.
            if (_neighbour != null)
            {
                _neighbour.SetVisible(true);
                _neighbour.WarpToCell(FindOpenCellNear(door, 1));
                _neighbour.FaceTowards(_player.transform.position);
            }

            yield return Fade(1f, 0f, FadeSeconds);
            yield return PlayNode(NodeDressed);

            // --- 5. The gathering ------------------------------------------------------------
            Vector2Int plaza = CellFrom(GameData.Map != null ? GameData.Map.plaza : null, new Vector2Int(19, 13));

            // He is already up on the stone when the square fills. The crowd forms an arc facing
            // him, and everyone keeps facing him for the whole speech.
            BuildGathering(plaza);

            // The neighbour takes his place in the crowd and the player stops beside him, not on
            // top of him: you arrive with the person who brought you.
            Vector2Int neighbourSpot = _crowdSpots.Count > 0 ? _crowdSpots[0] : FindOpenCellNear(plaza, 3);
            Vector2Int playerSpot = FindOpenCellNear(neighbourSpot, 1);

            if (_neighbour != null)
            {
                List<Vector2Int> route = PathForNeighbour(neighbourSpot);
                float speed = _player != null ? _player.MoveSpeed : CutsceneActor.WalkSpeed;
                StartCoroutine(route != null && route.Count > 0
                    ? _neighbour.WalkPath(route, speed)
                    : _neighbour.WalkToCell(neighbourSpot, speed));
                yield return new WaitForSeconds(FollowDelay);
            }

            yield return WalkTo(playerSpot);

            if (_neighbour != null)
            {
                _neighbour.FaceTowards(_governor.transform.position);
            }

            FacePlayerTowards(_governor.transform.position);
            yield return new WaitForSeconds(BeatPause);
            yield return PlayNode(NodeGathering);

            // --- 6. Hand over ----------------------------------------------------------------
            GameState state = WorldRuntime.State;
            if (state != null)
            {
                state.SetFlag(SeenFlag);
                WorldRuntime.SaveNow();
            }

            // Nobody vanishes. The gathering was the whole village turning up; a square that
            // empties the instant the speech ends would undo it. They stay, and they answer.
            PersistGathering();

            SetHudVisible(true);
            _player.InputEnabled = true;
            IsPlaying = false;

            Action finished = Finished;
            if (finished != null)
            {
                try
                {
                    finished();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[World] An IntroCutscene.Finished listener threw: " + exception.Message);
                }
            }

            Destroy(this);
        }

        /// <summary>
        /// Nearest walkable cell at roughly the requested distance from <paramref name="origin"/>.
        /// Used to place people beside each other without hardcoding coordinates that a redrawn map
        /// would silently invalidate.
        /// </summary>
        Vector2Int FindOpenCellNear(Vector2Int origin, int preferredDistance)
        {
            Vector2Int best = origin;
            float bestScore = float.MaxValue;

            for (int dy = -8; dy <= 8; dy++)
            {
                for (int dx = -8; dx <= 8; dx++)
                {
                    var cell = new Vector2Int(origin.x + dx, origin.y + dy);
                    if (!_map.InBounds(cell.x, cell.y) || !_map.IsWalkable(cell.x, cell.y))
                    {
                        continue;
                    }

                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float score = Mathf.Abs(distance - preferredDistance);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = cell;
                    }
                }
            }

            return best;
        }

        void FacePlayerTowards(Vector3 worldPosition)
        {
            CharacterAppearance appearance = _player != null ? _player.Appearance : null;
            if (appearance == null)
            {
                return;
            }

            Vector3 delta = worldPosition - _player.transform.position;
            appearance.SetDirection(FacingDirectionExtensions.FromDelta(
                new Vector2(delta.x, delta.y), FacingDirection.Down));
        }

        /// <summary>
        /// Walks the neighbour to his door and puts him through it. Run alongside the player's own
        /// walk rather than before it, so the two of you cross the village together.
        /// </summary>
        IEnumerator LeadToDoor(Vector2Int door, List<Vector2Int> path, Action onInside)
        {
            float speed = _player != null ? _player.MoveSpeed : CutsceneActor.WalkSpeed;

            if (path != null && path.Count > 0)
            {
                yield return _neighbour.WalkPath(path, speed);
            }
            else
            {
                yield return _neighbour.WalkToCell(door, speed);
            }

            _neighbour.SetVisible(false);
            if (onInside != null)
            {
                onInside();
            }
        }

        /// <summary>
        /// The route the player is about to walk, so an escort can be put on the same one. Returns
        /// null when no path exists, and the caller falls back to walking straight there.
        /// </summary>
        List<Vector2Int> PathForPlayer(Vector2Int destination)
        {
            if (_player == null || _player.Pathfinder == null)
            {
                return null;
            }

            try
            {
                return _player.Pathfinder.FindPath(_player.GridPosition, destination);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Could not path the escort: " + exception.Message);
                return null;
            }
        }


        // ---------------------------------------------------------------- the gathering

        /// <summary>How many people turn up besides you and the neighbour.</summary>
        const int CrowdSize = 11;

        /// <summary>
        /// Puts the man on a stone at the middle of the square and arranges everyone else in an arc
        /// in front of him, all facing him. The arc is built outward from the stone so the player,
        /// arriving last, is at the back of a crowd rather than in the middle of an empty square.
        /// </summary>
        void BuildGathering(Vector2Int plaza)
        {
            _governor = CutsceneActor.Spawn("ManFromTheCapital", plaza, _map, transform, 0,
                new Color(0.55f, 0.50f, 0.53f, 1f));
            _governor.LiftBy(0.34f);        // standing on the stone, not beside it
            BuildStone(plaza);

            var palettes = new[]
            {
                new Color(0.68f, 0.57f, 0.46f, 1f), new Color(0.56f, 0.49f, 0.42f, 1f),
                new Color(0.47f, 0.53f, 0.55f, 1f), new Color(0.64f, 0.56f, 0.43f, 1f),
                new Color(0.51f, 0.45f, 0.49f, 1f), new Color(0.60f, 0.53f, 0.47f, 1f)
            };

            var taken = new HashSet<Vector2Int> { plaza };
            int placed = 0;

            // Rings outward from the stone, so the front row fills before the back.
            for (int radius = 2; radius <= 5 && placed < CrowdSize; radius++)
            {
                int steps = Mathf.Max(8, radius * 6);
                for (int i = 0; i < steps && placed < CrowdSize; i++)
                {
                    float angle = (i / (float)steps) * Mathf.PI * 2f;
                    var cell = new Vector2Int(
                        plaza.x + Mathf.RoundToInt(Mathf.Cos(angle) * radius),
                        plaza.y + Mathf.RoundToInt(Mathf.Sin(angle) * radius));

                    if (taken.Contains(cell) || !_map.InBounds(cell.x, cell.y) || !_map.IsWalkable(cell.x, cell.y))
                    {
                        continue;
                    }

                    taken.Add(cell);
                    _crowdSpots.Add(cell);

                    CutsceneActor person = CutsceneActor.Spawn("Gathered" + placed, cell, _map, transform,
                        placed % 2, palettes[placed % palettes.Length]);
                    person.FaceTowards(_governor.transform.position);
                    _crowd.Add(person);
                    placed++;
                }
            }

            _governor.FaceTowards(_map.CellToWorldCenter(plaza + new Vector2Int(0, -3)));
        }

        /// <summary>The stone he stands on. Drawn from the rubble tile, which is what it is.</summary>
        void BuildStone(Vector2Int plaza)
        {
            _stone = new GameObject("Stone");
            _stone.transform.SetParent(transform, false);
            _stone.transform.position = _map.CellToWorldCenter(plaza);

            SpriteRenderer renderer = _stone.AddComponent<SpriteRenderer>();
            renderer.sprite = ArtLibrary.Get(ArtKeys.TileRubble);
            renderer.color = new Color(0.60f, 0.57f, 0.50f, 1f);
            renderer.sortingOrder = 118;                 // under the man, over the ground
            _stone.transform.localScale = new Vector3(1.35f, 0.85f, 1f);
        }

        /// <summary>Route for the neighbour, found from where he is rather than from the player.</summary>
        List<Vector2Int> PathForNeighbour(Vector2Int destination)
        {
            if (_neighbour == null || _player == null || _player.Pathfinder == null || _map == null)
            {
                return null;
            }

            try
            {
                Vector2Int from = _map.WorldToCell(_neighbour.transform.position);
                return _player.Pathfinder.FindPath(from, destination);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Could not path the neighbour to the gathering: " + exception.Message);
                return null;
            }
        }

        /// <summary>
        /// Hands the cutscene's people over to the village. They keep standing where they stood and
        /// answer when tapped, so the square the speech filled stays full.
        /// </summary>
        void PersistGathering()
        {
            if (_neighbour != null)
            {
                _neighbour.PersistAs(_map, "vizinho_after", Loc.T("world.speaker.neighbour"));
            }

            if (_governor != null)
            {
                _governor.PersistAs(_map, "governador_after", Loc.T("world.speaker.governor"));
            }

            for (int i = 0; i < _crowd.Count; i++)
            {
                if (_crowd[i] != null)
                {
                    _crowd[i].PersistAs(_map, "multidao_after", Loc.T("world.speaker.crowd"));
                }
            }

            _crowd.Clear();
        }

        // ---------------------------------------------------------------- beats

        IEnumerator PlayNode(string nodeId)
        {
            if (_dialogue == null)
            {
                _dialogue = WorldRuntime.FindDialogueSystem();
            }

            if (_dialogue == null)
            {
                Debug.LogWarning("[World] No DialogueSystem; the opening skipped \"" + nodeId + "\".");
                yield break;
            }

            bool done = false;
            Action<string> handler = finishedId =>
            {
                if (string.Equals(finishedId, nodeId, StringComparison.Ordinal))
                {
                    done = true;
                }
            };

            _dialogue.NodeFinished += handler;
            try
            {
                _dialogue.Play(nodeId);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] The opening could not play \"" + nodeId + "\": " + exception.Message);
                _dialogue.NodeFinished -= handler;
                yield break;
            }

            // A guard rather than a trust: a node that never reports finished must not strand the
            // player in a cutscene with no controls.
            float waited = 0f;
            while (!done && waited < 300f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            _dialogue.NodeFinished -= handler;
            yield return new WaitForSeconds(BeatPause);
        }

        IEnumerator WalkTo(Vector2Int cell)
        {
            bool arrived = false;
            if (!_player.MoveToCell(cell, () => arrived = true))
            {
                yield break;
            }

            float waited = 0f;
            while (!arrived && waited < 20f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }

        static Vector2Int CellFrom(GridPos pos, Vector2Int fallback)
        {
            return pos != null ? new Vector2Int(pos.x, pos.y) : fallback;
        }

        void ReapplyAppearance()
        {
            GameState state = WorldRuntime.State;
            if (state == null || _player == null)
            {
                return;
            }

            CharacterAppearance appearance = _player.Appearance;
            if (appearance != null)
            {
                appearance.Apply(state.appearance);
            }
        }

        // ---------------------------------------------------------------- presentation

        void BuildFade()
        {
            Canvas canvas = UIKit.CreateCanvas("IntroFadeCanvas", 400);
            // The fade covers everything, camera housing included, so it stays outside the safe area.
            _fade = UIKit.Bleed(UIKit.CreatePanel((RectTransform)canvas.transform, "Fade", UIKit.Palette.Ink));
            UIKit.Stretch((RectTransform)_fade.transform);
            _fade.raycastTarget = false;
            SetFadeAlpha(1f);
        }

        void SetFadeAlpha(float alpha)
        {
            if (_fade == null)
            {
                return;
            }

            Color colour = _fade.color;
            colour.a = Mathf.Clamp01(alpha);
            _fade.color = colour;
        }

        IEnumerator Fade(float from, float to, float seconds)
        {
            float elapsed = 0f;
            SetFadeAlpha(from);
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                SetFadeAlpha(Mathf.Lerp(from, to, seconds <= 0f ? 1f : elapsed / seconds));
                yield return null;
            }

            SetFadeAlpha(to);
        }

        static void SetHudVisible(bool visible)
        {
            HUD hud = FindFirstObjectByType<HUD>();
            if (hud != null)
            {
                hud.SetVisible(visible);
            }
        }

        void OnDestroy()
        {
            IsPlaying = false;
            if (_fade != null)
            {
                Destroy(_fade.canvas != null ? _fade.canvas.gameObject : _fade.gameObject);
            }

            if (_worldMap != null)
            {
                _worldMap.Dispose();
            }
        }
    }
}
