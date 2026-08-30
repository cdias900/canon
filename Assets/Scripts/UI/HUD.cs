using System;
using SheepGate.Core;
using SheepGate.World;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The permanent overlay: what the player has to spend, how far the wall has come, what day it
    /// is, and the three buttons that change the frame — the wide patrol camera ("Ronda" on the
    /// button), the map, and settings.
    ///
    /// Deliberately small. This is a portrait phone screen where the ground itself is the primary
    /// control, so the readouts sit in a single glass card at the top and the map hugs the bottom
    /// left edge. The middle of the screen — where taps move the character — stays empty.
    ///
    /// The two bottom corners are the reachable ones on a phone held in one hand, which is why the
    /// map sits there and settings does not: settings is a thing you visit once, and a control in
    /// the thumb zone is a control you press by accident.
    ///
    /// <b>The design system's stated UX risk is this screen.</b> "HUD tampando a cena" is what
    /// Sistema Vale names as the way this project fails, and its priority order is scene, then
    /// character, then mission, then HUD. Three things here answer that and none of them is
    /// decoration: the whole overlay is drawn at <see cref="BaseOpacity"/>; every surface is the
    /// Glass card style, which is <c>Surface.SceneVeil</c> and nothing else; and nothing on this
    /// screen paints where a tap would otherwise reach the ground.
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

        /// <summary>
        /// What the entire overlay is drawn at. The design system asks for the HUD to sit behind
        /// the scene in the reading order, and this is the one mechanism that does it — the veil
        /// and the card colours are left at their token values, because two knobs for one effect
        /// is how a screen ends up unreadable with nobody able to say which knob did it.
        ///
        /// The arithmetic that makes this legal: <c>Surface.SceneVeil</c> is 88% opaque, and
        /// 0.88 x 0.86 = 0.757, so text on this HUD still sits on a veil above the design system's
        /// 72% floor for anything laid over the scene. Lowering either number breaks that rule.
        ///
        /// A <see cref="CanvasGroup"/> alpha only affects what is drawn. Raycasts, interactability
        /// and <see cref="Canvas.enabled"/> — which is what the e2e run reads to decide the HUD is
        /// on screen — are untouched.
        /// </summary>
        public const float BaseOpacity = 0.86f;

        /// <summary>Gap between the top card and the row of frame controls under it.</summary>
        static readonly float ColumnSpacing = DesignTokens.Space.S8;

        /// <summary>Distance from the top of the safe area to the first card.</summary>
        static readonly float TopMargin = DesignTokens.Space.S12;

        /// <summary>
        /// How far a glass plate extends past the control standing on it.
        ///
        /// It is not padding for looks. A plate is drawn with the Lg frame and a button with the Md
        /// one, so a plate exactly the size of its button would have the button's tighter corner
        /// poking out past the plate's rounder one, over the scene, at every corner. One step of
        /// the spacing scale is enough clearance for the two arcs to nest.
        /// </summary>
        static readonly float PlatePadding = DesignTokens.Space.S4;

        /// <summary>A plate is the kit's default button plus its clearance, on both axes.</summary>
        static readonly float PlateWidth = UIKit.DefaultButtonWidth + 2f * PlatePadding;
        static readonly float PlateHeight = UIKit.ButtonMinHeight + 2f * PlatePadding;

        static HUD _current;

        Canvas _canvas;
        Text _dayText;
        Text _workText;
        Text _rubbleText;
        ProgressBar _wallProgress;
        Button _patrolButton;
        Image _patrolPlate;
        Button _mapButton;
        Button _settingsButton;
        CameraRig _cameraRig;

        bool _patrolViewActive;
        int _cachedDay = int.MinValue;
        int _cachedWork = int.MinValue;
        int _cachedWorkMax = int.MinValue;
        int _cachedRubble = int.MinValue;
        int _cachedWallStages = int.MinValue;
        int _cachedWallTotal = int.MinValue;

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
            RectTransform root = UIKit.SafeArea(_canvas);
            ApplyBaseOpacity(root);

            RectTransform column = BuildTopColumn(root);
            BuildReadoutCard(column);
            BuildFrameControls(column);

            // The opening shows the region once and then leaves; this is the way back to it.
            //
            // The bottom right used to hold a button that ended the day, and nothing replaced it:
            // the day now ends when the work runs out, and stopping early is the mat at the door,
            // in the world. A HUD button asking the player to declare the day over made stopping
            // a chore performed on the interface instead of a thing done in the village.
            //
            // SafeAreaBottom is added on top of the safe-area inset rather than instead of it: the
            // inset clears the home indicator, and the design system asks for breathing room above
            // it as well, so a control does not sit flush against the gesture bar.
            Image mapPlate;
            _mapButton = BuildPlatedButton(root, "MapButton", Loc.T("hud.map"), OnMapClicked, out mapPlate);
            UIKit.AnchorCorner((RectTransform)mapPlate.transform, new Vector2(0f, 0f),
                               new Vector2(PlateWidth, PlateHeight),
                               new Vector2(DesignTokens.Space.Gutter, DesignTokens.Space.SafeAreaBottom));
        }

        /// <summary>
        /// Drops the whole overlay to <see cref="BaseOpacity"/> in one place. Added to the safe-area
        /// rect rather than to the canvas so a component that looks the canvas up — the cutscene
        /// toggling <see cref="Canvas.enabled"/>, the e2e run reading it — sees exactly what it saw
        /// before.
        /// </summary>
        static void ApplyBaseOpacity(RectTransform root)
        {
            if (root == null)
            {
                return;
            }

            CanvasGroup group = root.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = root.gameObject.AddComponent<CanvasGroup>();
            }

            group.alpha = BaseOpacity;
            group.interactable = true;
            group.blocksRaycasts = true;
        }

        /// <summary>
        /// The column everything at the top of the screen hangs from: the information card, then
        /// the two frame controls under it.
        ///
        /// It measures itself. The card's height is now decided by the type inside it rather than
        /// by a constant, and the type moved when the game went onto the design system's scale, so
        /// a hardcoded offset for the buttons underneath would have been a number that was correct
        /// on the day it was written. One <see cref="ContentSizeFitter"/> at the top of the column
        /// and layout groups underneath it means the row of buttons follows the card wherever the
        /// card ends.
        ///
        /// Exactly one fitter, deliberately: nested groups already report their preferred height
        /// upward, and a second fitter inside the column would fight the group above it for the
        /// same rect.
        /// </summary>
        static RectTransform BuildTopColumn(RectTransform root)
        {
            RectTransform column = UIKit.CreateRect("TopColumn", root);
            column.anchorMin = new Vector2(0f, 1f);
            column.anchorMax = new Vector2(1f, 1f);
            column.pivot = new Vector2(0.5f, 1f);
            column.sizeDelta = new Vector2(-2f * DesignTokens.Space.Gutter, 0f);
            column.anchoredPosition = new Vector2(0f, -TopMargin);

            UIKit.VerticalGroup(column.gameObject, ColumnSpacing, new RectOffset());

            var fitter = column.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return column;
        }

        /// <summary>
        /// The information card: the three readouts on one line, and the wall under them.
        ///
        /// Glass, which is the only card style allowed over the game scene, and the reason is the
        /// design system's rule 4 — a sentence over a lit tilemap is unreadable on anything
        /// thinner than a 72% veil.
        ///
        /// It does not take taps. The card is a good deal taller than the strip it replaced, and a
        /// tap that lands on the HUD has always fallen through to the ground underneath; making it
        /// swallow taps now would change how the game plays, which is not what a restyle is for.
        /// </summary>
        void BuildReadoutCard(RectTransform column)
        {
            Image card = UIKit.CreateCard(column, "TopBar", UIKit.CardStyle.Glass);
            card.raycastTarget = false;
            var cardRect = (RectTransform)card.transform;

            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S8, new RectOffset(
                Mathf.RoundToInt(DesignTokens.Space.S20),
                Mathf.RoundToInt(DesignTokens.Space.S20),
                Mathf.RoundToInt(DesignTokens.Space.S12),
                Mathf.RoundToInt(DesignTokens.Space.S12)));

            RectTransform readouts = UIKit.CreateRect("Readouts", cardRect);
            UIKit.HorizontalGroup(readouts.gameObject, DesignTokens.Space.S16, new RectOffset(),
                                  TextAnchor.MiddleLeft);

            // The same height as a progress header, so the two rows of this card sit on one pitch.
            LayoutElement readoutLayout = UIKit.Layout(readouts);
            readoutLayout.minHeight = UIKit.ProgressHeaderHeight;
            readoutLayout.preferredHeight = UIKit.ProgressHeaderHeight;

            // The day is context rather than a resource: it is the one number here the player
            // cannot spend, so it is the one drawn in the supporting ink. Nothing is carried by
            // that colour — all three readouts say in words what they are.
            _dayText = BuildReadout(readouts, "Day", TextAnchor.MiddleLeft, DesignTokens.Ink.Secondary);
            _workText = BuildReadout(readouts, "Work", TextAnchor.MiddleCenter, UIKit.InkFor(UIKit.CardStyle.Glass));
            _rubbleText = BuildReadout(readouts, "Rubble", TextAnchor.MiddleRight, UIKit.InkFor(UIKit.CardStyle.Glass));

            // Label, bar and fraction, which is the only shape the design system allows progress to
            // take. The wall is the one number in this game that means anything on its own, and it
            // used to be somewhere else entirely: the player had to walk to a segment to learn how
            // far the work had come.
            _wallProgress = UIKit.CreateProgress(cardRect, "WallProgress", Loc.T("hud.wall"));
        }

        /// <summary>
        /// One resource readout: mono, tabular, and never without the word that says what it counts.
        ///
        /// Both halves are design system rules rather than taste. Quantities are mono so the digits
        /// are the same width and the number does not shuffle sideways as it climbs, and a
        /// quantity always carries its label — this HUD has no icon standing in for a word, and no
        /// bare number standing in for either.
        ///
        /// Overflow rather than wrap: these sit in a single-line row of a fixed height, and a
        /// locale long enough to wrap would push a second line out of the card silently. Bleeding
        /// past the neighbour is the visible failure, and the visible one is the one that gets
        /// fixed.
        /// </summary>
        static Text BuildReadout(RectTransform parent, string name, TextAnchor alignment, Color color)
        {
            Text text = UIKit.CreateText(parent, name, string.Empty, DesignTokens.Type.Mono, color,
                                         alignment, DesignTokens.TypeRole.Mono);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Preferred width comes from the string itself, so no readout can be squeezed narrower
            // than its own text; the flexible share only decides who gets the slack left over.
            LayoutElement layout = UIKit.Layout(text);
            layout.flexibleWidth = 1f;
            return text;
        }

        /// <summary>
        /// The two controls that change the frame rather than the game: settings on the left,
        /// patrol on the right, with the whole width between them.
        ///
        /// Neither is a call to action, so neither is clay and neither is gold — the design
        /// system's rule 9 allows one gold per screen and this screen spends it on nothing, because
        /// nothing here opens something new.
        /// </summary>
        void BuildFrameControls(RectTransform column)
        {
            RectTransform row = UIKit.CreateRect("FrameControls", column);
            UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.TouchGap, new RectOffset(),
                                  TextAnchor.MiddleLeft);

            LayoutElement rowLayout = UIKit.Layout(row);
            rowLayout.minHeight = PlateHeight;
            rowLayout.preferredHeight = PlateHeight;

            // Object names in English, labels from the locale table: the player never reads a
            // string written in this file.
            //
            // Settings opens a panel rather than being the setting. Language is chosen once, and a
            // permanent control for it sat on top of the village every second of every day.
            Image settingsPlate;
            _settingsButton = BuildPlatedButton(row, "SettingsButton", Loc.T("hud.settings"),
                                                OnSettingsClicked, out settingsPlate);

            // The gap is the whole slack of the row, so the two controls end up in the two upper
            // corners however wide the phone is.
            RectTransform spacer = UIKit.CreateRect("Spacer", row);
            UIKit.Layout(spacer).flexibleWidth = 1f;

            _patrolButton = BuildPlatedButton(row, "PatrolButton", Loc.T("hud.patrol"),
                                              OnPatrolClicked, out _patrolPlate);
        }

        /// <summary>
        /// A control standing on its own glass plate.
        ///
        /// The plate is the point. A Secondary button is an 8% parchment fill with a hairline, and
        /// over a lit tilemap that is a label floating on nothing — the design system's rule 4 asks
        /// for a 72% veil under any text laid over the scene, and the button's own fill is nowhere
        /// near it. The plate supplies the veil; the button supplies the control.
        ///
        /// The plate never takes a tap. Only the button inside it does, which is what keeps a
        /// finger that lands beside the label from counting as a press.
        /// </summary>
        Button BuildPlatedButton(Transform parent, string name, string label, Action onClick, out Image plate)
        {
            plate = UIKit.CreateCard(parent, name + "Plate", UIKit.CardStyle.Glass);
            plate.raycastTarget = false;

            var plateRect = (RectTransform)plate.transform;
            plateRect.sizeDelta = new Vector2(PlateWidth, PlateHeight);

            LayoutElement plateLayout = UIKit.Layout(plate);
            plateLayout.minWidth = PlateWidth;
            plateLayout.preferredWidth = PlateWidth;
            plateLayout.minHeight = PlateHeight;
            plateLayout.preferredHeight = PlateHeight;

            Button button = UIKit.CreateButton(plateRect, name, label, UIKit.ButtonVariant.Secondary, onClick);
            UIKit.Stretch((RectTransform)button.transform, PlatePadding, PlatePadding, PlatePadding, PlatePadding);
            return button;
        }

        // ------------------------------------------------------------------ readouts

        void Update()
        {
            GameState state = TryGetState();
            if (state == null)
            {
                return;
            }

            int wallStages;
            int wallTotal;
            WallStages(state, out wallStages, out wallTotal);

            if (state.day == _cachedDay &&
                state.workCapacity == _cachedWork &&
                state.workCapacityMax == _cachedWorkMax &&
                state.rubble == _cachedRubble &&
                wallStages == _cachedWallStages &&
                wallTotal == _cachedWallTotal)
            {
                return;
            }

            Apply(state);
        }

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

        /// <summary>Forces the readouts to match the run right now.</summary>
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
                _dayText.text = Loc.T("hud.day", Mathf.Max(1, state.day));
            }

            if (_workText != null)
            {
                _workText.text = Loc.T("hud.work", Mathf.Max(0, state.workCapacity), Mathf.Max(0, state.workCapacityMax));
            }

            if (_rubbleText != null)
            {
                _rubbleText.text = Loc.T("hud.rubble", Mathf.Max(0, state.rubble));
            }

            int wallStages;
            int wallTotal;
            WallStages(state, out wallStages, out wallTotal);
            _cachedWallStages = wallStages;
            _cachedWallTotal = wallTotal;

            if (_wallProgress != null)
            {
                _wallProgress.SetValue(wallStages, wallTotal);
            }
        }

        /// <summary>
        /// How much of the wall stands, counted in stages rather than in segments.
        ///
        /// Read off <see cref="GameState"/> rather than off <see cref="WallSystem"/> on purpose:
        /// the state is what a save file holds and what every other readout on this HUD already
        /// polls, so the bar cannot disagree with the numbers beside it, and it still draws
        /// correctly on a frame where the wall system has not been registered yet.
        ///
        /// A stage that is finished is never taken back — that is the product rule the wall system
        /// enforces — so this number only ever climbs, which is exactly what a progress bar is
        /// allowed to promise.
        /// </summary>
        static void WallStages(GameState state, out int completed, out int total)
        {
            completed = 0;
            total = 0;

            if (state == null || state.segments == null)
            {
                return;
            }

            for (int i = 0; i < state.segments.Count; i++)
            {
                WallSegmentState segment = state.segments[i];
                if (segment == null)
                {
                    continue;
                }

                total += WallSystem.StagesPerSegment;
                completed += Mathf.Clamp(segment.stage, 0, WallSystem.StagesPerSegment);
            }
        }

        // ------------------------------------------------------------------ patrol view

        void OnSettingsClicked()
        {
            SettingsPanel.Show();
        }

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

            ApplyPatrolAppearance(active);
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
        /// Shows that the wide view is on. The word is what carries the state — "Ronda" becomes
        /// "Voltar" — because the design system's rule is that colour never says anything on its
        /// own. The plate colour is the second signal, not the only one.
        ///
        /// The plate and not the button: a button built from a <see cref="UIKit.ButtonVariant"/>
        /// owns its colours across seven states and repaints them the moment a finger moves, so
        /// <see cref="UIKit.TintButton"/> on one of these would survive until the next hover and no
        /// longer. The plate underneath is a plain image this component owns outright.
        ///
        /// Sky at the veil's own opacity, so the plate stays exactly as opaque as every other
        /// surface on this HUD and rule 4 holds whichever state it is in.
        /// </summary>
        void ApplyPatrolAppearance(bool active)
        {
            if (_patrolButton != null)
            {
                UIKit.SetButtonLabel(_patrolButton, active ? Loc.T("hud.patrol_return") : Loc.T("hud.patrol"));
            }

            if (_patrolPlate != null)
            {
                _patrolPlate.color = active
                    ? UIKit.WithAlpha(DesignTokens.Ambient.Sky, DesignTokens.Surface.SceneVeil.a)
                    : DesignTokens.Surface.SceneVeil;
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

        // ------------------------------------------------------------------ map

        /// <summary>
        /// Opens the region view. The HUD only asks: <see cref="WorldMapView"/> owns what the map
        /// is and refuses on its own while a cutscene, a line of dialogue or a modal is running,
        /// so this never has to know which of those is true.
        /// </summary>
        void OnMapClicked()
        {
            if (ModalRoot.IsOpen)
            {
                return;
            }

            WorldMapView.Open();
        }

        // ------------------------------------------------------------------ helpers

        static GameState TryGetState()
        {
            GameState state;
            return ServiceLocator.TryGet(out state) ? state : null;
        }
    }
}
