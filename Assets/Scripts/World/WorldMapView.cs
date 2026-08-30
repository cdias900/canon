using System;
using System.Collections;
using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.Player;
using SheepGate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.World
{
    /// <summary>
    /// The map, on demand.
    ///
    /// The opening already pulls the camera back far enough to show this city as one point among
    /// several, but that was a beat in a cutscene: it played once and was gone. This is the same
    /// picture reachable from the HUD at any point in the run, which is the difference between a
    /// thing the game showed you and a thing you can go and look at.
    ///
    /// It is deliberately the same view rather than a drawn panel. The village on screen is the
    /// real tilemap, the neighbours are the real world-space markers, and the roads run between
    /// them on the same ground the player walks — so opening the map never contradicts what
    /// walking around the city just taught them about where anything is.
    ///
    /// While it is open the player cannot move: the HUD is hidden, input is locked, and the camera
    /// is detached from the follow target. Closing puts all three back.
    /// </summary>
    public sealed class WorldMapView : MonoBehaviour
    {
        /// <summary>Above the HUD (50), below the dialogue canvas (100) and every modal (300).</summary>
        public const int CanvasSortingOrder = 90;

        const float OpenSeconds = 0.55f;
        const float CloseSeconds = 0.45f;

        static WorldMapView _current;

        WorldMapOverlay _overlay;
        Canvas _canvas;
        CameraRig _rig;
        PlayerController _player;
        bool _inputLocked;
        bool _closing;

        /// <summary>True while the map is on screen.</summary>
        public static bool IsOpen
        {
            get { return _current != null; }
        }

        /// <summary>
        /// Opens the map, or does nothing when it is already up or when something else owns the
        /// screen. A cutscene, a line of dialogue and a modal all mean the player is being shown
        /// something; pulling the camera to the region in the middle of that would cut across it.
        /// </summary>
        public static void Open()
        {
            if (_current != null || !CanOpen())
            {
                return;
            }

            var host = new GameObject("WorldMapView");
            host.AddComponent<WorldMapView>();
        }

        /// <summary>Closes the map if it is open. Safe to call when it is not.</summary>
        public static void Close()
        {
            if (_current != null)
            {
                _current.BeginClose();
            }
        }

        public static void Toggle()
        {
            if (_current != null)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        /// <summary>
        /// Whether the map may be opened right now. Public so the HUD can grey its own button out
        /// instead of offering a button that would silently do nothing.
        /// </summary>
        public static bool CanOpen()
        {
            if (IntroCutscene.IsPlaying || ModalRoot.IsOpen)
            {
                return false;
            }

            DialogueSystem dialogue = FindFirstObjectByType<DialogueSystem>();
            if (dialogue != null && dialogue.IsPlaying)
            {
                return false;
            }

            return GameScene.MapBuilder != null;
        }

        void Awake()
        {
            if (_current != null && _current != this)
            {
                Destroy(gameObject);
                return;
            }

            _current = this;
            BuildView();
        }

        void OnDestroy()
        {
            if (_current != this)
            {
                return;
            }

            _current = null;

            // Whatever went wrong, the player must not be left unable to move.
            ReleaseInput();
            HUD hud = HUD.Current;
            if (hud != null)
            {
                hud.SetVisible(true);
            }
        }

        // ------------------------------------------------------------------ construction

        void BuildView()
        {
            _rig = ResolveRig();
            _player = FindFirstObjectByType<PlayerController>();

            InputLock.Push();
            _inputLocked = true;

            if (_player != null)
            {
                _player.InputEnabled = false;
            }

            HUD hud = HUD.Current;
            if (hud != null)
            {
                hud.SetVisible(false);
            }

            // With the key: opened from the HUD the map is something the player reads, not a
            // beat being played at them.
            _overlay = WorldMapOverlay.Show(GameScene.MapBuilder, transform, true);

            if (_rig != null && GameScene.MapBuilder != null)
            {
                _rig.FrameCutscene(GameScene.MapBuilder.CenterWorld(), WorldMapOverlay.FramingSize, OpenSeconds);
            }

            BuildChrome();
        }

        /// <summary>
        /// One button, at the bottom of the screen where the map button that opened it was. The
        /// heading and the framing line belong to the overlay, so nothing is repeated here.
        /// </summary>
        void BuildChrome()
        {
            _canvas = UIKit.CreateCanvas("WorldMapViewCanvas", CanvasSortingOrder);
            _canvas.transform.SetParent(transform, false);
            RectTransform root = UIKit.SafeArea(_canvas);

            Button close = UIKit.CreateButton(root, "CloseMap", Loc.T("world.map.close"),
                UIKit.Palette.Clay, UIKit.Palette.Parchment, BeginClose);

            // Bottom centre: a zero horizontal margin against a 0.5 anchor is what centres it.
            UIKit.AnchorCorner((RectTransform)close.transform, new Vector2(0.5f, 0f),
                new Vector2(330f, 124f), new Vector2(0f, 44f));
        }

        // ------------------------------------------------------------------ closing

        void BeginClose()
        {
            if (_closing)
            {
                return;
            }

            _closing = true;

            if (_canvas != null)
            {
                // The button goes first: the camera takes half a second to come back and a second
                // tap in that window would otherwise queue another close.
                _canvas.enabled = false;
            }

            StartCoroutine(CloseRoutine());
        }

        IEnumerator CloseRoutine()
        {
            Vector3 destination = _player != null
                ? _player.transform.position
                : (GameScene.MapBuilder != null ? GameScene.MapBuilder.CenterWorld() : Vector3.zero);

            if (_rig != null)
            {
                // Same idiom the opening uses to come down from the region: frame the close size
                // first, then hand the camera back, or it keeps the wide size and follows anyway.
                _rig.FrameCutscene(destination, CameraRig.CloseSize, CloseSeconds);
            }

            if (_overlay != null)
            {
                yield return _overlay.FadeOut(CloseSeconds * 0.6f);
                _overlay = null;
            }
            else
            {
                yield return new WaitForSeconds(CloseSeconds * 0.6f);
            }

            yield return new WaitForSeconds(CloseSeconds * 0.4f);

            if (_rig != null && _player != null)
            {
                _rig.SetTarget(_player.transform);
            }

            if (_player != null)
            {
                _player.InputEnabled = true;
            }

            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ helpers

        void ReleaseInput()
        {
            if (!_inputLocked)
            {
                return;
            }

            _inputLocked = false;
            InputLock.Pop();
        }

        static CameraRig ResolveRig()
        {
            CameraRig registered;
            try
            {
                if (ServiceLocator.TryGet(out registered) && registered != null)
                {
                    return registered;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Looking up the CameraRig for the map failed: " + exception.Message);
            }

            return FindFirstObjectByType<CameraRig>();
        }
    }
}
