using System;
using System.Collections;
using System.Collections.Generic;
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

        /// <summary>How far out the camera sits while the world map is on screen.</summary>
        const float WorldSize = 34f;

        public static bool IsPlaying { get; private set; }

        TilemapBuilder _map;
        CameraRig _rig;
        PlayerController _player;
        DialogueSystem _dialogue;
        WorldMapOverlay _worldMap;
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
            Vector2Int door = CellFrom(GameData.Map != null ? GameData.Map.player_house_door : null, new Vector2Int(14, 9));
            StartCoroutine(_neighbour.WalkToCell(FindOpenCellNear(door, 2)));
            yield return WalkTo(door);
            yield return Fade(0f, 1f, FadeSeconds);

            bool dressed = false;
            CharacterCreationScreen.Compose(() => dressed = true);
            while (!dressed)
            {
                yield return null;
            }

            ReapplyAppearance();
            yield return Fade(1f, 0f, FadeSeconds);
            if (_neighbour != null)
            {
                _neighbour.FaceTowards(_player.transform.position);
            }

            yield return PlayNode(NodeDressed);

            // --- 5. The gathering ------------------------------------------------------------
            Vector2Int plaza = CellFrom(GameData.Map != null ? GameData.Map.plaza : null, new Vector2Int(19, 13));
            if (_neighbour != null)
            {
                StartCoroutine(_neighbour.WalkToCell(FindOpenCellNear(plaza, 2)));
            }

            yield return WalkTo(plaza);

            // The man from the capital, never named. He is already up on the stone when you arrive.
            Vector2Int stone = FindOpenCellNear(plaza, 3);
            _governor = CutsceneActor.Spawn("ManFromTheCapital", stone, _map, transform, 0,
                new Color(0.52f, 0.48f, 0.52f, 1f));
            _governor.FaceTowards(_player.transform.position);
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

            if (_neighbour != null) _neighbour.Despawn();
            if (_governor != null) _governor.Despawn();

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
            _fade = UIKit.CreatePanel((RectTransform)canvas.transform, "Fade", UIKit.Palette.Ink);
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
