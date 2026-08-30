using System;
using System.Globalization;
using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.Player;
using SheepGate.World;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The permanent overlay, which is now one control: a menu button. Everything else — the
    /// readouts, the wall's progress, and the four buttons that change the frame (the wide patrol
    /// camera, the map, the backpack, settings) — lives in the drawer it opens.
    ///
    /// <b>What this costs, stated plainly.</b> Work capacity is the number that falls with every
    /// action and decides the next one, and it is now a tap away instead of on screen. That is a
    /// real loss and it was the point of the change: the screen this game most wants you looking at
    /// is the ground, and Sistema Vale names "HUD tampando a cena" as the way this project fails.
    /// If the drawer turns out to be opened constantly to check the same number, the answer is to
    /// bring that one readout back out — not to undo the drawer.
    ///
    /// The drawer is not a modal. It takes no input lock and pushes nothing onto
    /// <see cref="ModalRoot"/>, for a concrete reason: every control inside it refuses to run while
    /// a modal is up or the input lock is held, so a drawer that took either would be a menu whose
    /// own buttons did nothing. What stops a tap reaching the village is an invisible full-screen
    /// scrim behind the card, which is also what closes the drawer when you tap away from it.
    ///
    /// A control chosen closes the drawer <b>before</b> it acts, so those same gates see the frame
    /// they are actually being asked about.
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

        /// <summary>
        /// Height of the badge that says how many wardrobe items the player has not looked at yet.
        /// Its width follows its content, so a two digit count is a wider pill rather than a
        /// smaller number — the design system's floor on type is a floor everywhere.
        /// </summary>
        static readonly float BadgeHeight = DesignTokens.Px(20f);

        /// <summary>Breathing room either side of the count inside the badge.</summary>
        static readonly float BadgePadding = DesignTokens.Space.S8;

        /// <summary>
        /// How far the badge hangs off the plate's top right corner. Small on purpose: the plate
        /// already sits a full gutter in from the screen edge, so this cannot reach the safe area.
        /// </summary>
        static readonly float BadgeOverhang = DesignTokens.Space.S4;

        /// <summary>
        /// How often the badge count is recomputed, in unscaled seconds.
        ///
        /// It is polled rather than pushed because an item does not open when the wardrobe changes
        /// — it opens when the wall reaches a stage or the day turns, which raises no wardrobe
        /// event at all. Polling is the only way the badge can appear on the day it is earned.
        /// The interval exists because the answer walks the whole catalogue, and a quarter of a
        /// second is far below the point where a player would notice a number arriving late.
        ///
        /// A wardrobe change still updates it immediately; the interval only bounds the idle cost.
        /// </summary>
        const float BadgePollInterval = 0.25f;

        static HUD _current;

        Canvas _canvas;
        Text _dayText;
        Text _workText;
        Text _rubbleText;
        ProgressBar _wallProgress;
        Button _patrolButton;
        Image _patrolPlate;
        Button _menuButton;
        Button _helpButton;
        RectTransform _drawer;
        Button _mapButton;
        Button _settingsButton;
        Button _backpackButton;
        Image _backpackBadge;
        Text _backpackBadgeCount;
        CameraRig _cameraRig;
        PlayerController _player;

        bool _patrolViewActive;
        int _cachedNewItems = int.MinValue;
        float _nextBadgePoll;
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

            // The badge follows the wardrobe as well as polling it: a badge spent inside the
            // backpack has to be gone the moment the sheet closes, and a quarter of a second of a
            // stale count on a screen the player just came back to reads as a bug.
            //
            // This is also where the world character learns it has been dressed. Subscribing here
            // rather than from whoever owns the player's sprite stack is safe for one specific
            // reason: an equip can only happen inside the backpack, the backpack can only be opened
            // from this HUD's button, and this HUD outlives the modal it opened. It is the same
            // shape as this component driving the CameraRig itself instead of hoping something
            // subscribed to PatrolViewToggled.
            Wardrobe.Changed += OnWardrobeChanged;

            Refresh();
        }

        void OnDestroy()
        {
            // A static event outlives the scene, and this scene is rebuilt from scratch on a
            // language switch. Removing a handler that was never added — the duplicate destroyed
            // above — is a no-op, so this needs no guard of its own.
            Wardrobe.Changed -= OnWardrobeChanged;

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

            BuildDrawer(root);

            // The two the player reaches for while playing stay out on the screen, in the corners a
            // thumb reaches on a phone held in one hand. Only what is read rather than used — the
            // readouts — and what is visited rather than played — settings, the patrol frame — went
            // into the drawer.
            //
            // SafeAreaBottom is added on top of the safe-area inset rather than instead of it: the
            // inset clears the home indicator, and the design system asks for breathing room above
            // it as well, so a control does not sit flush against the gesture bar.
            Image mapPlate;
            _mapButton = BuildPlatedButton(root, "MapButton", Loc.T("hud.map"), OnMapClicked, out mapPlate);
            UIKit.AnchorCorner((RectTransform)mapPlate.transform, new Vector2(0f, 0f),
                               new Vector2(PlateWidth, PlateHeight),
                               new Vector2(DesignTokens.Space.Gutter, DesignTokens.Space.SafeAreaBottom));

            BuildBackpack(root);
            BuildHelpButton(root);

            // Built last so it draws over the drawer it opens: the button stays put and stays
            // pressable while the drawer is down, which is what makes it a toggle rather than a
            // control that vanishes under what it opened.
            BuildMenuButton(root);
        }

        /// <summary>
        /// The backpack, in the other reachable corner.
        ///
        /// Square and wordless, which is the one place this HUD spends an icon. The design system
        /// keeps filled silhouettes for resources and outlines for navigation, and this is
        /// navigation: it opens a sheet, it counts nothing and it spends nothing. The icon-button
        /// variant is also the one that will not let the control exist without an accessible name.
        ///
        /// <b>The glyph is drawn, not borrowed.</b> This button briefly stood in with the menu
        /// outline, which was wrong in a way worth recording: a hamburger says "more options", and
        /// the one thing this control must not be confused with is settings. That distinction earns
        /// its keep now that the hamburger is on the same screen.
        /// </summary>
        void BuildBackpack(RectTransform root)
        {
            Image plate;
            _backpackButton = BuildPlatedIconButton(root, "BackpackButton", UiSpriteKeys.IconBag,
                                                    Loc.T("hud.backpack"), OnBackpackClicked, out plate);

            var plateRect = (RectTransform)plate.transform;
            UIKit.AnchorCorner(plateRect, new Vector2(1f, 0f),
                               new Vector2(PlateHeight, PlateHeight),
                               new Vector2(DesignTokens.Space.Gutter, DesignTokens.Space.SafeAreaBottom));

            BuildBackpackBadge(plateRect);
        }

        /// <summary>
        /// Help, in the top left — the corner the readouts used to start in, and the one place left
        /// where a control is neither in the thumb zone nor under the menu button.
        ///
        /// It is a modal rather than a drawer because it is read rather than operated: it is the one
        /// thing on this HUD the player stops to take in, and the darkened world behind a modal is
        /// what says so.
        /// </summary>
        void BuildHelpButton(RectTransform root)
        {
            Image plate;
            _helpButton = BuildPlatedIconButton(root, "HelpButton", UiSpriteKeys.IconHelp,
                                                Loc.T("hud.help"), OnHelpClicked, out plate);

            UIKit.AnchorCorner((RectTransform)plate.transform, new Vector2(0f, 1f),
                               new Vector2(PlateHeight, PlateHeight),
                               new Vector2(DesignTokens.Space.Gutter, TopMargin));
        }

        void OnHelpClicked()
        {
            CloseMenu();
            HelpPanel.Show();
        }

        /// <summary>
        /// The drawer: an invisible scrim that catches taps, and the column of everything this HUD
        /// used to show all the time.
        ///
        /// The scrim is transparent rather than dimmed. A modal is a card over a darkened world and
        /// this is not one — it is a drawer on the same overlay, at the same opacity as the rest of
        /// it, and darkening the village to show a readout would be the HUD covering the scene by
        /// another route. It still takes every tap that misses the card, which is the whole reason
        /// an invisible graphic is here at all: without a raycast target under the finger there is
        /// nothing to close the drawer with, and the tap would land in the village instead.
        /// </summary>
        void BuildDrawer(RectTransform root)
        {
            _drawer = UIKit.CreateRect("HudMenu", root);
            UIKit.Stretch(_drawer, 0f, 0f, 0f, 0f);

            RectTransform scrim = UIKit.CreateRect("MenuScrim", _drawer);
            UIKit.Stretch(scrim, 0f, 0f, 0f, 0f);

            Image scrimImage = scrim.gameObject.AddComponent<Image>();
            scrimImage.color = new Color(0f, 0f, 0f, 0f);
            scrimImage.raycastTarget = true;

            Button scrimButton = scrim.gameObject.AddComponent<Button>();
            scrimButton.transition = Selectable.Transition.None;
            scrimButton.targetGraphic = scrimImage;
            scrimButton.onClick.AddListener(CloseMenu);

            // Below the menu button rather than under the top of the screen: the button stays where
            // it is when the drawer opens, and a card sliding out from underneath it would cover
            // the only way to close it again.
            RectTransform column = BuildTopColumn(_drawer, TopMargin + PlateHeight + ColumnSpacing);
            BuildReadoutCard(column);
            BuildFrameControls(column);

            _drawer.gameObject.SetActive(false);
        }

        /// <summary>
        /// The one thing left on screen. Square, wordless and in the top right — out of the thumb
        /// zone, because a control you press by accident is worse here than anywhere: it is now the
        /// only door to everything else.
        ///
        /// </summary>
        void BuildMenuButton(RectTransform root)
        {
            Image plate;
            _menuButton = BuildPlatedIconButton(root, "MenuButton", UiSpriteKeys.IconMenu,
                                                Loc.T("hud.menu"), ToggleMenu, out plate);

            var plateRect = (RectTransform)plate.transform;
            UIKit.AnchorCorner(plateRect, new Vector2(1f, 1f),
                               new Vector2(PlateHeight, PlateHeight),
                               new Vector2(DesignTokens.Space.Gutter, TopMargin));
        }

        /// <summary>True while the drawer is down. Read by the e2e run, which has to open it.</summary>
        public bool IsMenuOpen
        {
            get { return _drawer != null && _drawer.gameObject.activeSelf; }
        }

        public void ToggleMenu()
        {
            if (IsMenuOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }

        public void OpenMenu()
        {
            if (_drawer == null || IsMenuOpen)
            {
                return;
            }

            _drawer.gameObject.SetActive(true);
            Refresh();
        }

        public void CloseMenu()
        {
            if (_drawer != null)
            {
                _drawer.gameObject.SetActive(false);
            }
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
        static RectTransform BuildTopColumn(RectTransform root, float topOffset)
        {
            RectTransform column = UIKit.CreateRect("TopColumn", root);
            column.anchorMin = new Vector2(0f, 1f);
            column.anchorMax = new Vector2(1f, 1f);
            column.pivot = new Vector2(0.5f, 1f);
            column.sizeDelta = new Vector2(-2f * DesignTokens.Space.Gutter, 0f);
            column.anchoredPosition = new Vector2(0f, -topOffset);

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

        /// <summary>
        /// The same plate, carrying a square icon control instead of a labelled one.
        ///
        /// Written beside <see cref="BuildPlatedButton"/> rather than folded into it. The two
        /// differ in the plate's width and in the variant, and the three controls that already
        /// exist are laid out against a plate exactly <see cref="PlateWidth"/> wide — a shared
        /// helper with a width parameter would have put every one of them one argument away from
        /// moving, for no gain.
        ///
        /// <paramref name="label"/> is never drawn. It is the control's accessible name, and
        /// <see cref="UIKit.CreateIconButton"/> requires it: a square with a glyph on it is
        /// unreadable to anyone who does not already recognise the shape.
        /// </summary>
        Button BuildPlatedIconButton(Transform parent, string name, string iconKey, string label,
                                     Action onClick, out Image plate)
        {
            plate = UIKit.CreateCard(parent, name + "Plate", UIKit.CardStyle.Glass);
            plate.raycastTarget = false;

            var plateRect = (RectTransform)plate.transform;
            plateRect.sizeDelta = new Vector2(PlateHeight, PlateHeight);

            LayoutElement plateLayout = UIKit.Layout(plate);
            plateLayout.minWidth = PlateHeight;
            plateLayout.preferredWidth = PlateHeight;
            plateLayout.minHeight = PlateHeight;
            plateLayout.preferredHeight = PlateHeight;

            // Inset by the plate's own clearance, which leaves the control at exactly the touch
            // target the design system asks for: the plate is that target plus the padding twice.
            Button button = UIKit.CreateIconButton(plateRect, name, iconKey, label, onClick);
            UIKit.Stretch((RectTransform)button.transform, PlatePadding, PlatePadding, PlatePadding, PlatePadding);
            return button;
        }

        /// <summary>
        /// How many things in the backpack the player has not looked at yet.
        ///
        /// <b>A count and not a dot.</b> The design system's rule is that an icon never stands on
        /// its own; a bare dot would say only that something is different, and the player would
        /// have to open the sheet to find out how different. The number is the whole message.
        ///
        /// <b>Where the word is.</b> Rule 8 asks for a quantity to carry its label, and the label
        /// here rides on the control's accessible name rather than inside the pill — the button is
        /// one touch target wide, and a pill spelling out the word as well would be nearly as wide
        /// as the control it sits on and would read as a second control. So the name the button
        /// answers to changes with the count, and the pill carries the digits. Mono either way, so
        /// the digits are tabular and 9 becoming 10 does not shuffle the pill sideways.
        ///
        /// <b>Why gold.</b> Gold is the system's mark for "this is new", which is exactly and only
        /// what this says. It does not spend the screen's one gold call to action, because this is
        /// not a call to action: it is a count on a navigation control, and the control underneath
        /// it stays an ordinary icon button.
        ///
        /// Nothing here is a progress bar and nothing here is a fraction. It counts items already
        /// open and not yet seen — it never counts how close anything is to opening, which is the
        /// number this game does not have.
        /// </summary>
        void BuildBackpackBadge(RectTransform plate)
        {
            Image badge = UIKit.CreatePanel(plate, "BackpackBadge", DesignTokens.Brand.Secondary,
                                            UiSpriteKeys.FrameSm);
            badge.raycastTarget = false;

            var badgeRect = (RectTransform)badge.transform;
            badgeRect.anchorMin = new Vector2(1f, 1f);
            badgeRect.anchorMax = new Vector2(1f, 1f);
            badgeRect.pivot = new Vector2(1f, 1f);
            badgeRect.sizeDelta = new Vector2(BadgeHeight, BadgeHeight);
            badgeRect.anchoredPosition = new Vector2(BadgeOverhang, BadgeOverhang);

            // The pill measures itself around the digits: a group to centre them, and a fitter on
            // the horizontal axis only, so the height stays the constant above whatever the count is.
            UIKit.HorizontalGroup(badge.gameObject, 0f, new RectOffset(
                Mathf.RoundToInt(BadgePadding), Mathf.RoundToInt(BadgePadding), 0, 0),
                TextAnchor.MiddleCenter);

            var fitter = badge.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            _backpackBadgeCount = UIKit.CreateText(badgeRect, "Count", string.Empty, DesignTokens.Type.Mono,
                                                   DesignTokens.Ink.OnSecondary, TextAnchor.MiddleCenter,
                                                   DesignTokens.TypeRole.Mono);
            _backpackBadgeCount.raycastTarget = false;
            _backpackBadgeCount.horizontalOverflow = HorizontalWrapMode.Overflow;

            // A single digit still gets a round pill rather than a narrow slot.
            LayoutElement countLayout = UIKit.Layout(_backpackBadgeCount);
            countLayout.minWidth = BadgeHeight - 2f * BadgePadding;
            countLayout.minHeight = BadgeHeight;

            _backpackBadge = badge;
            badge.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ readouts

        void Update()
        {
            GameState state = TryGetState();
            if (state == null)
            {
                return;
            }

            PollBackpackBadge(state);

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
            // A drawer left open through a cutscene would be the first thing on screen when the
            // overlay comes back, over a beat that had nothing to do with it.
            if (!visible)
            {
                CloseMenu();
            }

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
                ApplyBackpackBadge(state);
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
            CloseMenu();
            SettingsPanel.Show();
        }

        void OnPatrolClicked()
        {
            // Closed first, and not only for tidiness: the patrol view is a change of frame, and
            // watching it from behind the drawer that asked for it would be the drawer covering the
            // one thing it was opened to show.
            CloseMenu();

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
            CloseMenu();

            if (ModalRoot.IsOpen)
            {
                return;
            }

            WorldMapView.Open();
        }

        // ------------------------------------------------------------------ backpack

        /// <summary>
        /// Opens the backpack, when the frame is the player's to operate.
        ///
        /// The gate is spelled out here rather than borrowed from the map's, because the two are
        /// not the same question and a shared predicate would quietly make them so. It reads the
        /// four states in which this screen is not the thing in front of the player:
        /// <list type="bullet">
        ///   <item>a modal is already up — the backpack is one, and a wardrobe stacked on the
        ///   quiz would leave two sheets fighting for the same card;</item>
        ///   <item>something holds the input lock — the same gate the player's own taps on the
        ///   ground pass through;</item>
        ///   <item>the opening cutscene is running. It hides this whole overlay for exactly this
        ///   reason, so the button is already unreachable, and this is the belt to that brace;</item>
        ///   <item>somebody is talking. Dialogue does not take the input lock, so it has to be
        ///   asked directly — the same thing <see cref="WorldMapView.CanOpen"/> does, for the same
        ///   reason. The lookup happens on the tap and never per frame.</item>
        /// </list>
        ///
        /// A refusal is silent and nothing is greyed out, which matches every other control on this
        /// HUD. There is no disabled state on this screen and this is not the place to invent one:
        /// during three of those four states the overlay is either hidden or behind something the
        /// player is reading, and a control that dims itself for a beat and comes back would be a
        /// new thing to watch rather than a clearer one.
        /// </summary>
        void OnBackpackClicked()
        {
            CloseMenu();

            if (!CanOpenBackpack())
            {
                return;
            }

            BackpackPanel.Show();
        }

        /// <summary>Whether the backpack may be opened this instant. See <see cref="OnBackpackClicked"/>.</summary>
        static bool CanOpenBackpack()
        {
            if (ModalRoot.IsOpen || InputLock.IsLocked || IntroCutscene.IsPlaying)
            {
                return false;
            }

            DialogueSystem dialogue = FindFirstObjectByType<DialogueSystem>();
            return dialogue == null || !dialogue.IsPlaying;
        }

        /// <summary>
        /// The wardrobe changed: recount the badge without waiting for the next poll, and repaint
        /// the character standing in the village.
        /// </summary>
        void OnWardrobeChanged()
        {
            GameState state = TryGetState();
            if (state != null)
            {
                ApplyBackpackBadge(state);
            }

            _nextBadgePoll = Time.unscaledTime + BadgePollInterval;
            RepaintPlayer();
        }

        /// <summary>Recomputes the badge at most once per <see cref="BadgePollInterval"/>.</summary>
        void PollBackpackBadge(GameState state)
        {
            if (Time.unscaledTime < _nextBadgePoll)
            {
                return;
            }

            _nextBadgePoll = Time.unscaledTime + BadgePollInterval;
            ApplyBackpackBadge(state);
        }

        /// <summary>
        /// Puts the count on the badge, and the same count into the name the control answers to.
        ///
        /// Zero hides the pill outright rather than drawing an empty one: there is nothing new, and
        /// a badge showing 0 is a notice that nothing happened.
        /// </summary>
        void ApplyBackpackBadge(GameState state)
        {
            int count = Wardrobe.NewCount(state);
            if (count == _cachedNewItems)
            {
                return;
            }

            _cachedNewItems = count;

            if (_backpackBadgeCount != null)
            {
                _backpackBadgeCount.text = count.ToString(CultureInfo.InvariantCulture);
            }

            if (_backpackBadge != null)
            {
                _backpackBadge.gameObject.SetActive(count > 0);

                // The pill sizes itself around the digits, and a fitter on an object that was just
                // switched on lays out a frame later. Doing it now is what keeps a two digit count
                // from appearing in a one digit pill for a frame.
                if (count > 0)
                {
                    UIKit.RebuildNow((RectTransform)_backpackBadge.transform);
                }
            }

            if (_backpackButton != null)
            {
                AccessibleLabel.Apply(_backpackButton.gameObject,
                                      count > 0 ? Loc.T("hud.backpack_new", count) : Loc.T("hud.backpack"));
            }
        }

        /// <summary>
        /// Repaints the player's sprite stack from the appearance the wardrobe just wrote.
        ///
        /// Nothing is respawned and no scene is reloaded: the wardrobe writes the six ints of the
        /// very <c>AppearanceState</c> the character was built from, and
        /// <see cref="CharacterAppearance.Refresh"/> re-reads them in place. The character keeps
        /// their position, their facing and their animation.
        ///
        /// A missing player is a warning and not an error. The wardrobe is reachable from screens
        /// the village is not standing behind, and a run with no player object is not a reason to
        /// refuse an equip that the save has already accepted.
        /// </summary>
        void RepaintPlayer()
        {
            PlayerController player = ResolvePlayer();
            if (player == null)
            {
                Debug.LogWarning("[HUD] No PlayerController is in the scene; the wardrobe change " +
                                 "was saved but nothing was repainted.");
                return;
            }

            try
            {
                CharacterAppearance appearance = player.Appearance;
                if (appearance != null)
                {
                    appearance.Reapply();
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[HUD] Repainting the character after a wardrobe change failed: " +
                               exception.Message);
            }
        }

        PlayerController ResolvePlayer()
        {
            // Compared against null on every use for the reason the camera rig is: a destroyed
            // component still holds a live C# reference, and Unity's null comparison is what
            // catches that.
            if (_player != null)
            {
                return _player;
            }

            PlayerController registered;
            try
            {
                if (ServiceLocator.TryGet(out registered) && registered != null)
                {
                    _player = registered;
                    return _player;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[HUD] Looking up the PlayerController failed: " + exception.Message);
            }

            _player = FindFirstObjectByType<PlayerController>();
            return _player;
        }

        // ------------------------------------------------------------------ helpers

        static GameState TryGetState()
        {
            GameState state;
            return ServiceLocator.TryGet(out state) ? state : null;
        }
    }
}
