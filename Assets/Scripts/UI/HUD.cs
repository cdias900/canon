using System;
using SheepGate.Core;
using SheepGate.World;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The permanent overlay: what the player has to spend, what day it is, and the two buttons
    /// that change the frame — the wide patrol camera ("Ronda" on the button) and the end of the day.
    ///
    /// Deliberately small. This is a portrait phone screen where the ground itself is the primary
    /// control, so the readouts sit in a single strip at the top and the two buttons sit at the
    /// right edge. The middle and lower-left of the screen — where a thumb rests and where taps
    /// move the character — stay empty.
    ///
    /// State is polled from GameState rather than pushed by events. A HUD that quietly stops
    /// updating because a system forgot to raise a change event is a worse failure than one extra
    /// integer comparison per frame.
    ///
    /// Nothing here shows vocation progress, and nothing here ever will.
    /// </summary>
    public sealed class HUD : MonoBehaviour
    {
        /// <summary>
        /// Above the world and the night overlay, below the dialogue canvas (100) and below every
        /// modal (300). Two overlay canvases sharing a sorting order draw in an order nothing
        /// guarantees, so the HUD deliberately sits under the layer that is meant to cover it.
        /// </summary>
        public const int CanvasSortingOrder = 50;

        /// <summary>
        /// Counter bumped every time the wide patrol view is entered. Published so whoever owns
        /// vocation scoring can read it; the HUD deliberately awards nothing itself, because the
        /// rest of that archetype is scored by systems that own the fish and the map edge.
        ///
        /// The key string is what existing save files already contain, so it stays exactly as it
        /// is: renaming it would silently reset a player's progress.
        /// </summary>
        public const string PatrolCounter = "ronda_used";

        const float TopBarHeight = 140f;
        const float SideMargin = 24f;
        const float TopMargin = 28f;

        static HUD _current;

        Canvas _canvas;
        Text _dayText;
        Text _workText;
        Text _rubbleText;
        Button _patrolButton;
        Button _endDayButton;
        CameraRig _cameraRig;

        bool _patrolViewActive;
        int _cachedDay = int.MinValue;
        int _cachedWork = int.MinValue;
        int _cachedWorkMax = int.MinValue;
        int _cachedRubble = int.MinValue;

        /// <summary>
        /// Raised with true when the player enters the wide patrol view and false when they leave
        /// it. Free for anything that wants to follow the frame the player is in.
        ///
        /// Subscribing is optional: the HUD already drives the <see cref="CameraRig"/> registered
        /// in the scene, and <see cref="CameraRig.SetPatrolView"/> ignores a state it already has,
        /// so a second subscriber that pushes the same value costs nothing. A subscriber that
        /// forwards this to the rig must pass the bool through <see cref="CameraRig.SetPatrolView"/>
        /// — forwarding it to <see cref="CameraRig.TogglePatrolView"/> would flip the camera back
        /// and leave it disagreeing with the button.
        /// </summary>
        public event Action<bool> PatrolViewToggled;

        /// <summary>The HUD in the scene, or null.</summary>
        public static HUD Current
        {
            get { return _current; }
        }

        /// <summary>True while the wide patrol view is on.</summary>
        public bool IsPatrolView
        {
            get { return _patrolViewActive; }
        }

        /// <summary>Builds the HUD, or returns the one already in the scene.</summary>
        public static HUD Compose()
        {
            if (_current != null)
            {
                return _current;
            }

            HUD existing = FindFirstObjectByType<HUD>();
            if (existing != null)
            {
                _current = existing;
                return existing;
            }

            var go = new GameObject("HUD");
            return go.AddComponent<HUD>();
        }

        void Awake()
        {
            if (_current != null && _current != this)
            {
                Debug.LogWarning("[HUD] A second HUD was created; destroying the duplicate.");
                Destroy(gameObject);
                return;
            }

            _current = this;
            Build();
            Refresh();
        }

        void OnDestroy()
        {
            if (_current == this)
            {
                _current = null;
            }
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            _canvas = UIKit.CreateCanvas("HUDCanvas", CanvasSortingOrder);
            _canvas.transform.SetParent(transform, false);
            var root = (RectTransform)_canvas.transform;

            Image bar = UIKit.CreatePanel(root, "TopBar", UIKit.Palette.Panel);
            var barRect = (RectTransform)bar.transform;
            UIKit.AnchorTop(barRect, TopBarHeight, SideMargin, SideMargin, TopMargin);
            bar.raycastTarget = false;

            _dayText = BuildReadout(barRect, "Day", 0f, 0.30f, TextAnchor.MiddleLeft);
            _workText = BuildReadout(barRect, "Work", 0.30f, 0.70f, TextAnchor.MiddleCenter);
            _rubbleText = BuildReadout(barRect, "Rubble", 0.70f, 1f, TextAnchor.MiddleRight);

            // Object name in English, label in pt-BR: "Ronda" is the word the player reads.
            _patrolButton = UIKit.CreateButton(root, "PatrolButton", "Ronda", UIKit.Palette.PanelSoft, UIKit.Palette.Parchment, OnPatrolClicked);
            var patrolRect = (RectTransform)_patrolButton.transform;
            patrolRect.anchorMin = new Vector2(1f, 1f);
            patrolRect.anchorMax = new Vector2(1f, 1f);
            patrolRect.pivot = new Vector2(1f, 1f);
            patrolRect.sizeDelta = new Vector2(238f, 104f);
            patrolRect.anchoredPosition = new Vector2(-SideMargin, -(TopMargin + TopBarHeight + 18f));

            _endDayButton = UIKit.CreateButton(root, "EndDay", "Fim do dia", UIKit.Palette.Clay, UIKit.Palette.Parchment, OnEndDayClicked);
            var endDayRect = (RectTransform)_endDayButton.transform;
            UIKit.AnchorCorner(endDayRect, new Vector2(1f, 0f), new Vector2(330f, 124f), new Vector2(32f, 44f));
        }

        Text BuildReadout(RectTransform parent, string name, float anchorLeft, float anchorRight, TextAnchor alignment)
        {
            Text text = UIKit.CreateText(parent, name, string.Empty, UIKit.FontSize.Body, UIKit.Palette.Parchment, alignment);
            var rect = (RectTransform)text.transform;
            rect.anchorMin = new Vector2(anchorLeft, 0f);
            rect.anchorMax = new Vector2(anchorRight, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(28f, 0f);
            rect.offsetMax = new Vector2(-28f, 0f);
            return text;
        }

        // ------------------------------------------------------------------ readouts

        void Update()
        {
            GameState state = TryGetState();
            if (state == null)
            {
                return;
            }

            if (state.day == _cachedDay &&
                state.workCapacity == _cachedWork &&
                state.workCapacityMax == _cachedWorkMax &&
                state.rubble == _cachedRubble)
            {
                return;
            }

            Apply(state);
        }

        /// <summary>Forces the readouts to match the run right now.</summary>
        /// <summary>
        /// Hides the whole overlay for a scripted beat. The opening uses it: during the cutscene
        /// the village is a place being shown, not a board being operated, and a work-capacity
        /// readout on screen would say otherwise.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_canvas != null)
            {
                _canvas.enabled = visible;
            }
            else
            {
                gameObject.SetActive(visible);
            }
        }

        public void Refresh()
        {
            GameState state = TryGetState();
            if (state != null)
            {
                Apply(state);
            }
        }

        void Apply(GameState state)
        {
            _cachedDay = state.day;
            _cachedWork = state.workCapacity;
            _cachedWorkMax = state.workCapacityMax;
            _cachedRubble = state.rubble;

            if (_dayText != null)
            {
                _dayText.text = "Dia " + Mathf.Max(1, state.day);
            }

            if (_workText != null)
            {
                _workText.text = "Trabalho " + Mathf.Max(0, state.workCapacity) + "/" + Mathf.Max(0, state.workCapacityMax);
            }

            if (_rubbleText != null)
            {
                _rubbleText.text = "Entulho " + Mathf.Max(0, state.rubble);
            }
        }

        // ------------------------------------------------------------------ patrol view

        void OnPatrolClicked()
        {
            // A modal covers the HUD, so this should be unreachable while one is up. Cheap to be
            // sure, and it matches what the end-of-day button already does.
            if (ModalRoot.IsOpen)
            {
                return;
            }

            TogglePatrolView();
        }

        /// <summary>Flips between the close follow view and the wide patrol view.</summary>
        public void TogglePatrolView()
        {
            SetPatrolView(!_patrolViewActive);
        }

        /// <summary>
        /// Switches the wide patrol view on or off: moves the camera, relabels the button and tells
        /// every listener. Entering the wide view bumps a counter; leaving it does not.
        /// </summary>
        public void SetPatrolView(bool active)
        {
            if (_patrolViewActive == active)
            {
                return;
            }

            _patrolViewActive = active;

            if (active)
            {
                GameState state = TryGetState();
                if (state != null)
                {
                    state.Bump(PatrolCounter);
                }
            }

            if (_patrolButton != null)
            {
                UIKit.SetButtonLabel(_patrolButton, active ? "Voltar" : "Ronda");
                UIKit.TintButton(_patrolButton, active ? UIKit.Palette.Night : UIKit.Palette.PanelSoft, UIKit.Palette.Parchment);
            }

            ApplyToCameraRig(active);

            Action<bool> handler = PatrolViewToggled;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(active);
            }
            catch (Exception exception)
            {
                Debug.LogError("[HUD] A listener threw while the patrol view toggled: " + exception.Message);
            }
        }

        /// <summary>
        /// Moves the camera itself, rather than hoping something subscribed to
        /// <see cref="PatrolViewToggled"/>. The rig is registered by the scene composer, so it is
        /// resolved through the service locator and only searched for as a fallback; the lookup is
        /// lazy because the rig exists before the HUD does, but is not guaranteed to be registered
        /// by the time this component is built.
        ///
        /// Nothing is lost if some other module also forwards the event to the rig:
        /// <see cref="CameraRig.SetPatrolView"/> ignores a state it already has.
        /// </summary>
        void ApplyToCameraRig(bool active)
        {
            CameraRig rig = ResolveCameraRig();
            if (rig == null)
            {
                Debug.LogWarning("[HUD] No CameraRig is in the scene; the patrol view button changed nothing.");
                return;
            }

            try
            {
                rig.SetPatrolView(active);
            }
            catch (Exception exception)
            {
                Debug.LogError("[HUD] Moving the camera into the patrol view failed: " + exception.Message);
            }
        }

        CameraRig ResolveCameraRig()
        {
            // The cached rig is compared against null on every use: a destroyed component still
            // holds a live C# reference, and Unity's null comparison is what catches that.
            if (_cameraRig != null)
            {
                return _cameraRig;
            }

            CameraRig registered;
            try
            {
                if (ServiceLocator.TryGet(out registered) && registered != null)
                {
                    _cameraRig = registered;
                    return _cameraRig;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[HUD] Looking up the CameraRig failed: " + exception.Message);
            }

            _cameraRig = FindFirstObjectByType<CameraRig>();
            return _cameraRig;
        }

        // ------------------------------------------------------------------ end of day

        void OnEndDayClicked()
        {
            if (ModalRoot.IsOpen)
            {
                return;
            }

            DayCycle cycle = FindFirstObjectByType<DayCycle>();
            if (cycle == null)
            {
                Debug.LogError("[HUD] No DayCycle is in the scene; the day cannot be ended.");
                return;
            }

            // The day cycle owns what ending a day means. The HUD only asks.
            cycle.RequestEndDay();
        }

        /// <summary>Greys out the end-of-day button, for stretches where the day must not end.</summary>
        public void SetEndDayAvailable(bool available)
        {
            if (_endDayButton != null)
            {
                _endDayButton.interactable = available;
            }
        }

        // ------------------------------------------------------------------ helpers

        static GameState TryGetState()
        {
            GameState state;
            return ServiceLocator.TryGet(out state) ? state : null;
        }
    }
}
