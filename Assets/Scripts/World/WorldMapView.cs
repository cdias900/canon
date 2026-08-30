using System;
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
    /// The map the player opens from the HUD: the three-day road through this POC.
    ///
    /// It is a progression surface, not level selection: the road shows compact selectable days,
    /// while one fixed card below the map reveals the selected day's title, reward and condition.
    /// Selecting a day changes information only; it never skips the game.
    ///
    /// The opening keeps <see cref="WorldMapOverlay"/>. That beat has to push the camera in from the
    /// region to the city in one continuous move; this view is what the HUD reopens afterwards.
    ///
    /// Every word is live UI on the design system's scroll surface. The map and item art contain no
    /// text, so the same sprites serve both locales and every state still carries a word rather
    /// than relying on colour alone.
    /// </summary>
    public sealed class WorldMapView : MonoBehaviour
    {
        /// <summary>Above the HUD (50) and below the dialogue canvas (100) and every modal (300).</summary>
        public const int CanvasSortingOrder = 90;

        static readonly float ScreenInset = DesignTokens.Space.S8;
        static readonly float HeaderHeight = DesignTokens.Px(112f);
        static readonly float StatusHeight = DesignTokens.Px(150f);
        static readonly float ControlsHeight = UIKit.ButtonMinHeight;
        static readonly float NodeMarkerSize = DesignTokens.Px(72f);
        static readonly float RewardIconSize = DesignTokens.Px(38f);

        static readonly string[] DayTitleKeys =
        {
            "world.progress_map.day.1.title",
            "world.progress_map.day.2.title",
            "world.progress_map.day.3.title"
        };

        static readonly string[] DayRewardItemIds =
        {
            "acc_tool_bag",
            "hair_headscarf",
            "outfit_valley_mantle"
        };

        static WorldMapView _current;

        Canvas _canvas;
        PlayerController _player;
        MapViewportController _viewportController;
        GameState _state;
        int _currentDay;
        int _selectedDay;
        readonly List<Image> _selectionRings = new List<Image>();

        Text _detailState;
        Text _detailTitle;
        Text _detailItemName;
        Text _detailItemState;
        Image _detailRewardImage;
        bool _inputLocked;

        /// <summary>True while the map is on screen.</summary>
        public static bool IsOpen
        {
            get { return _current != null; }
        }

        /// <summary>
        /// Opens the map, or does nothing when it is already up or when something else owns the
        /// screen. A cutscene, a line of dialogue and a modal all mean the player is being shown
        /// something, and covering that with a chart cuts across it.
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
                _current.CloseNow();
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
        /// instead of offering one that would silently do nothing.
        /// </summary>
        public static bool CanOpen()
        {
            if (IntroCutscene.IsPlaying || ModalRoot.IsOpen)
            {
                return false;
            }

            DialogueSystem dialogue = FindFirstObjectByType<DialogueSystem>();
            return dialogue == null || !dialogue.IsPlaying;
        }

        void Awake()
        {
            if (_current != null && _current != this)
            {
                Destroy(gameObject);
                return;
            }

            _current = this;
            Build();
        }

        void OnDestroy()
        {
            if (_current != this)
            {
                return;
            }

            _current = null;

            // Whatever went wrong, the player must not be left unable to move.
            if (_inputLocked)
            {
                _inputLocked = false;
                InputLock.Pop();
            }

            if (_player != null)
            {
                _player.InputEnabled = true;
            }

            HUD hud = HUD.Current;
            if (hud != null)
            {
                hud.SetVisible(true);
            }
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            InputLock.Push();
            _inputLocked = true;

            _player = FindFirstObjectByType<PlayerController>();
            if (_player != null)
            {
                _player.InputEnabled = false;
            }

            HUD hud = HUD.Current;
            if (hud != null)
            {
                hud.SetVisible(false);
            }

            _canvas = UIKit.CreateCanvas("WorldMapViewCanvas", CanvasSortingOrder);
            _canvas.transform.SetParent(transform, false);

            // The backdrop covers the whole screen, chrome included: a chart with the village
            // showing around its edges would read as a window rather than as a sheet of paper.
            Image backdrop = UIKit.Bleed(UIKit.CreatePanel((RectTransform)_canvas.transform, "Backdrop", BackdropColour));
            backdrop.raycastTarget = true;

            RectTransform root = UIKit.SafeArea(_canvas);
            _state = WorldRuntime.State;
            _currentDay = _state != null ? Mathf.Clamp(_state.day, 1, MapChartArt.JourneyCount) : 1;
            BuildTitle(root);

            RectTransform viewport = BuildViewport(root);
            RectTransform content = BuildMapContent(viewport);
            BuildJourney(content, _state);

            BuildStatusCard(root, _state);
            BuildControls(root);

            _viewportController = viewport.gameObject.AddComponent<MapViewportController>();
            _viewportController.Configure(viewport, content, MapChartArt.JourneyFocusAnchor(_currentDay - 1));
            SelectDay(_currentDay);
        }

        /// <summary>
        /// The clipped window onto the map. Its transparent Image owns pointer events while
        /// RectMask2D keeps a wide map from drawing through the fixed header and controls.
        /// </summary>
        RectTransform BuildViewport(RectTransform root)
        {
            RectTransform viewport = UIKit.CreateRect("MapViewport", root);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.offsetMin = new Vector2(ScreenInset,
                ScreenInset + ControlsHeight + DesignTokens.Space.TouchGap +
                StatusHeight + DesignTokens.Space.S8);
            viewport.offsetMax = new Vector2(-ScreenInset,
                -(ScreenInset + HeaderHeight + DesignTokens.Space.S8));

            var input = viewport.gameObject.AddComponent<Image>();
            input.color = Color.clear;
            input.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            return viewport;
        }

        RectTransform BuildMapContent(RectTransform viewport)
        {
            RectTransform content = UIKit.CreateRect("MapContent", viewport);
            content.anchorMin = new Vector2(0.5f, 0.5f);
            content.anchorMax = new Vector2(0.5f, 0.5f);
            content.pivot = new Vector2(0.5f, 0.5f);
            content.sizeDelta = MapChartArt.ContentSize;

            var image = content.gameObject.AddComponent<Image>();
            image.sprite = MapChartArt.Get();
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
            return content;
        }

        /// <summary>
        /// The three actual days of this POC, laid over the clearings painted into the road.
        ///
        /// The terrain carries only annotation-sized information: marker, day and state. Selecting
        /// one updates the single detail card below the viewport. That progressive disclosure keeps
        /// neighbouring copy from colliding while preserving every title, reward and unlock rule.
        /// </summary>
        void BuildJourney(RectTransform sheet, GameState state)
        {
            int count = Mathf.Min(MapChartArt.JourneyCount, DayTitleKeys.Length, DayRewardItemIds.Length);
            for (int i = 0; i < count; i++)
            {
                BuildJourneyNode(sheet, state, i + 1, MapChartArt.JourneyAnchor(i), DayTitleKeys[i]);
            }
        }

        void BuildJourneyNode(RectTransform sheet, GameState state, int day, Vector2 anchor,
            string titleKey)
        {
            JourneyNodeState nodeState = StateForDay(state, day);

            RectTransform marker = UIKit.CreateRect("MapNode" + day + "Marker", sheet);
            marker.anchorMin = anchor;
            marker.anchorMax = anchor;
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(NodeMarkerSize, NodeMarkerSize);

            var markerImage = marker.gameObject.AddComponent<Image>();
            markerImage.sprite = MapChartArt.NodeSprite(nodeState);
            markerImage.type = Image.Type.Simple;
            markerImage.preserveAspect = true;
            markerImage.raycastTarget = true;

            var button = marker.gameObject.AddComponent<Button>();
            button.targetGraphic = markerImage;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SelectDay(day));

            string stateLabel = Loc.T(StateLocaleKey(nodeState));
            AccessibleLabel.Apply(marker.gameObject,
                Loc.T("world.progress_map.day_label", day, stateLabel) + " " + Loc.T(titleKey));

            RectTransform selected = UIKit.CreateRect("MapNode" + day + "Selection", sheet);
            selected.anchorMin = anchor;
            selected.anchorMax = anchor;
            selected.pivot = new Vector2(0.5f, 0.5f);
            float selectedSize = NodeMarkerSize + DesignTokens.Px(12f);
            selected.sizeDelta = new Vector2(selectedSize, selectedSize);

            var selectedImage = selected.gameObject.AddComponent<Image>();
            selectedImage.sprite = ArtLibrary.Get(ArtKeys.UiFocusRing);
            selectedImage.type = Image.Type.Sliced;
            selectedImage.color = DesignTokens.Brand.Primary;
            selectedImage.raycastTarget = false;
            selectedImage.gameObject.SetActive(false);
            _selectionRings.Add(selectedImage);

            if (nodeState == JourneyNodeState.Current)
            {
                BuildCurrentLocation(sheet, anchor);
            }

            Image labelImage = UIKit.CreateCard(sheet, "MapNode" + day + "Label", UIKit.CardStyle.Glass);
            labelImage.raycastTarget = false;
            RectTransform label = (RectTransform)labelImage.transform;
            label.anchorMin = anchor;
            label.anchorMax = anchor;
            bool below = day == 3;
            label.pivot = below ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
            label.anchoredPosition = new Vector2(0f,
                (NodeMarkerSize * 0.5f + DesignTokens.Space.S4) * (below ? -1f : 1f));

            UIKit.HorizontalGroup(label.gameObject, 0f,
                Inset(DesignTokens.Space.S8, DesignTokens.Space.S4), TextAnchor.MiddleCenter);
            var fitter = label.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text compact = UIKit.CreateText(label, "Text",
                Loc.T("world.progress_map.day_label", day, stateLabel),
                DesignTokens.Type.Minimum, DesignTokens.Ink.OnScene, TextAnchor.MiddleCenter,
                DesignTokens.TypeRole.BodyStrong);
            compact.raycastTarget = false;
            label.gameObject.AddComponent<MapScaleLock>().Configure(sheet);
        }

        /// <summary>
        /// A focus ring and an explicit location label at the current day.
        ///
        /// Gold is the design system's focus accent, not scene lighting, and the words carry the
        /// same meaning so position is never communicated by colour alone. The label is scale
        /// locked; it travels with the map without growing with it.
        /// </summary>
        void BuildCurrentLocation(RectTransform sheet, Vector2 anchor)
        {
            RectTransform ring = UIKit.CreateRect("CurrentLocationRing", sheet);
            ring.anchorMin = anchor;
            ring.anchorMax = anchor;
            ring.pivot = new Vector2(0.5f, 0.5f);
            float ringSize = NodeMarkerSize + DesignTokens.Px(20f);
            ring.sizeDelta = new Vector2(ringSize, ringSize);

            var ringImage = ring.gameObject.AddComponent<Image>();
            ringImage.sprite = ArtLibrary.Get(ArtKeys.UiFocusRing);
            ringImage.type = Image.Type.Sliced;
            ringImage.color = DesignTokens.Brand.Secondary;
            ringImage.raycastTarget = false;

            Image labelImage = UIKit.CreateCard(sheet, "CurrentLocationLabel", UIKit.CardStyle.Glass);
            labelImage.raycastTarget = false;
            RectTransform label = (RectTransform)labelImage.transform;
            label.anchorMin = anchor;
            label.anchorMax = anchor;
            label.pivot = new Vector2(0.5f, 1f);
            label.anchoredPosition = new Vector2(0f,
                -(NodeMarkerSize * 0.5f + DesignTokens.Space.S8));

            UIKit.HorizontalGroup(label.gameObject, 0f,
                Inset(DesignTokens.Space.S8, DesignTokens.Space.S4), TextAnchor.MiddleCenter);
            var fitter = label.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text text = UIKit.CreateText(label, "Text", Loc.T("world.progress_map.current_location"),
                DesignTokens.Type.Minimum, DesignTokens.Ink.OnScene, TextAnchor.MiddleCenter,
                DesignTokens.TypeRole.BodyStrong);
            text.raycastTarget = false;

            label.gameObject.AddComponent<MapScaleLock>().Configure(sheet);
        }

        void SelectDay(int day)
        {
            _selectedDay = Mathf.Clamp(day, 1, MapChartArt.JourneyCount);
            for (int i = 0; i < _selectionRings.Count; i++)
            {
                Image ring = _selectionRings[i];
                if (ring != null)
                {
                    int ringDay = i + 1;
                    ring.gameObject.SetActive(ringDay == _selectedDay && ringDay != _currentDay);
                }
            }

            UpdateSelectedDetail();
        }

        void UpdateSelectedDetail()
        {
            int index = Mathf.Clamp(_selectedDay - 1, 0, DayTitleKeys.Length - 1);
            JourneyNodeState nodeState = StateForDay(_state, _selectedDay);
            string stateLabel = Loc.T(StateLocaleKey(nodeState));

            if (_detailState != null)
            {
                _detailState.text = Loc.T("world.progress_map.day_label", _selectedDay, stateLabel);
                _detailState.color = nodeState == JourneyNodeState.Current
                    ? DesignTokens.Brand.PrimaryDark
                    : DesignTokens.Ink.OnScrollMuted;
            }

            if (_detailTitle != null)
            {
                _detailTitle.text = Loc.T(DayTitleKeys[index]);
            }

            string itemId = DayRewardItemIds[Mathf.Clamp(index, 0, DayRewardItemIds.Length - 1)];
            CatalogItemDef item = CharacterCatalog.Item(itemId);
            bool unlocked = item == null || UnlockEvaluator.IsUnlocked(_state, item);

            string display = item != null && !string.IsNullOrEmpty(item.display)
                ? item.display
                : Loc.T("world.progress_map.item.unavailable");

            if (_detailItemName != null)
            {
                _detailItemName.text = display;
            }

            if (_detailRewardImage != null)
            {
                _detailRewardImage.sprite = MapChartArt.RewardSprite(itemId);
                _detailRewardImage.color = unlocked
                    ? Color.white
                    : UIKit.WithAlpha(Color.white, DesignTokens.Feedback.DisabledOpacity);
            }

            string detail = unlocked
                ? Loc.T("world.progress_map.item.unlocked")
                : UnlockEvaluator.Sentence(item.unlock_condition);
            if (_detailItemState != null)
            {
                _detailItemState.text = detail;
            }
        }

        static JourneyNodeState StateForDay(GameState state, int day)
        {
            int currentDay = state != null ? Mathf.Clamp(state.day, 1, 3) : 1;
            bool journeyComplete = state != null && state.HasFlag(GameFlags.VocationRevealed);
            if (day < currentDay || journeyComplete)
            {
                return JourneyNodeState.Complete;
            }

            return day == currentDay ? JourneyNodeState.Current : JourneyNodeState.Locked;
        }

        static string StateLocaleKey(JourneyNodeState state)
        {
            switch (state)
            {
                case JourneyNodeState.Complete: return "world.progress_map.state.complete";
                case JourneyNodeState.Current: return "world.progress_map.state.current";
                default: return "world.progress_map.state.locked";
            }
        }

        /// <summary>The fixed title and gesture hint above the clipped map.</summary>
        void BuildTitle(RectTransform root)
        {
            Image cardImage = UIKit.CreateCard(root, "TitleCard", UIKit.CardStyle.Scroll);
            cardImage.raycastTarget = false;
            RectTransform card = (RectTransform)cardImage.transform;
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.offsetMin = new Vector2(ScreenInset, -ScreenInset - HeaderHeight);
            card.offsetMax = new Vector2(-ScreenInset, -ScreenInset);

            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S4,
                Inset(DesignTokens.Space.S16, DesignTokens.Space.S8), TextAnchor.UpperLeft);

            Text heading = UIKit.CreateText(card, "Heading", Loc.T("world.progress_map.heading"),
                DesignTokens.Type.Title, PlateInk, TextAnchor.MiddleLeft, DesignTokens.TypeRole.Title);
            heading.raycastTarget = false;

            Text note = UIKit.CreateText(card, "Note", Loc.T("world.progress_map.note"),
                DesignTokens.Type.Body, MutedInk, TextAnchor.MiddleLeft);
            note.raycastTarget = false;

            Text hint = UIKit.CreateText(card, "GestureHint", Loc.T("world.progress_map.gesture_hint"),
                DesignTokens.Type.Minimum, MutedInk, TextAnchor.MiddleLeft);
            hint.raycastTarget = false;
        }

        /// <summary>
        /// The single selected-node place card below the map.
        ///
        /// It owns every sentence that used to be repeated beside all three markers. The terrain
        /// stays legible, the selected marker stays visible, and this fixed-width surface can reflow
        /// either locale without forcing the player to pan sideways to finish a line.
        /// </summary>
        void BuildStatusCard(RectTransform root, GameState state)
        {
            Image cardImage = UIKit.CreateCard(root, "ProgressSummary", UIKit.CardStyle.Scroll);
            cardImage.raycastTarget = false;
            RectTransform card = (RectTransform)cardImage.transform;
            card.anchorMin = new Vector2(0f, 0f);
            card.anchorMax = new Vector2(1f, 0f);
            card.pivot = new Vector2(0.5f, 0f);
            float bottom = ScreenInset + ControlsHeight + DesignTokens.Space.TouchGap;
            card.offsetMin = new Vector2(ScreenInset, bottom);
            card.offsetMax = new Vector2(-ScreenInset, bottom + StatusHeight);

            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S4,
                Inset(DesignTokens.Space.S12, DesignTokens.Space.S8));

            _detailState = UIKit.CreateText(card, "SelectedNodeState", string.Empty,
                DesignTokens.Type.Minimum, DesignTokens.Brand.PrimaryDark, TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.BodyStrong);
            _detailState.raycastTarget = false;

            _detailTitle = UIKit.CreateText(card, "SelectedNodeTitle", string.Empty,
                DesignTokens.Type.Title, DesignTokens.Ink.OnScroll, TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.Title);
            _detailTitle.raycastTarget = false;

            RectTransform rewardRow = UIKit.CreateRect("SelectedReward", card);
            UIKit.HorizontalGroup(rewardRow.gameObject, DesignTokens.Space.S8, new RectOffset(),
                TextAnchor.MiddleLeft);
            LayoutElement rewardRowLayout = UIKit.Layout(rewardRow);
            rewardRowLayout.minHeight = RewardIconSize;
            rewardRowLayout.preferredHeight = RewardIconSize;

            var reward = new GameObject("SelectedRewardSprite", typeof(RectTransform), typeof(Image));
            var rewardRect = (RectTransform)reward.transform;
            rewardRect.SetParent(rewardRow, false);
            rewardRect.sizeDelta = new Vector2(RewardIconSize, RewardIconSize);
            _detailRewardImage = reward.GetComponent<Image>();
            _detailRewardImage.type = Image.Type.Simple;
            _detailRewardImage.preserveAspect = true;
            _detailRewardImage.raycastTarget = false;

            LayoutElement rewardLayout = UIKit.Layout(_detailRewardImage);
            rewardLayout.minWidth = RewardIconSize;
            rewardLayout.preferredWidth = RewardIconSize;
            rewardLayout.minHeight = RewardIconSize;
            rewardLayout.preferredHeight = RewardIconSize;

            RectTransform rewardWords = UIKit.CreateRect("SelectedRewardWords", rewardRow);
            UIKit.VerticalGroup(rewardWords.gameObject, DesignTokens.Space.S4, new RectOffset(),
                TextAnchor.MiddleLeft);
            UIKit.Layout(rewardWords).flexibleWidth = 1f;

            _detailItemName = UIKit.CreateText(rewardWords, "SelectedItemName", string.Empty,
                DesignTokens.Type.Body, DesignTokens.Ink.OnScroll, TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.BodyStrong);
            _detailItemName.raycastTarget = false;

            _detailItemState = UIKit.CreateText(rewardWords, "SelectedItemState", string.Empty,
                DesignTokens.Type.Minimum, DesignTokens.Ink.OnScrollMuted, TextAnchor.MiddleLeft);
            _detailItemState.raycastTarget = false;

            RectTransform rule = UIKit.CreateRect("Rule", card);
            Image ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.color = DesignTokens.Neutral.N300;
            ruleImage.raycastTarget = false;
            LayoutElement ruleLayout = UIKit.Layout(ruleImage);
            ruleLayout.minHeight = DesignTokens.Px(1f);
            ruleLayout.preferredHeight = DesignTokens.Px(1f);

            RectTransform progressRow = UIKit.CreateRect("UnlockProgress", card);
            UIKit.HorizontalGroup(progressRow.gameObject, DesignTokens.Space.S8, new RectOffset(),
                TextAnchor.MiddleLeft);

            Text unlockLabel = UIKit.CreateText(progressRow, "UnlockLabel",
                Loc.T("world.progress_map.unlocks.heading"), DesignTokens.Type.Minimum,
                DesignTokens.Ink.OnScrollMuted, TextAnchor.MiddleLeft, DesignTokens.TypeRole.BodyStrong);
            unlockLabel.raycastTarget = false;
            UIKit.Layout(unlockLabel).flexibleWidth = 1f;

            int total = 0;
            int unlocked = 0;
            CatalogItemDef next = null;
            CatalogItemDef[] items = CharacterCatalog.Items;
            for (int i = 0; items != null && i < items.Length; i++)
            {
                CatalogItemDef item = items[i];
                if (item == null || item.unlock_condition == null)
                {
                    continue;
                }

                total++;
                if (UnlockEvaluator.IsUnlocked(state, item))
                {
                    unlocked++;
                }
                else if (next == null)
                {
                    next = item;
                }
            }

            Text count = UIKit.CreateText(progressRow, "UnlockCount",
                Loc.T("world.progress_map.unlocks.count", unlocked, total),
                DesignTokens.Type.Mono, DesignTokens.Ink.OnScroll, TextAnchor.MiddleRight,
                DesignTokens.TypeRole.Mono);
            count.raycastTarget = false;

            string nextLine = next == null
                ? Loc.T("world.progress_map.unlocks.all")
                : Loc.T("world.progress_map.unlocks.next", next.display);
            Text nextItem = UIKit.CreateText(card, "NextUnlock", nextLine,
                DesignTokens.Type.Minimum, DesignTokens.Ink.OnScroll, TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.BodyStrong);
            nextItem.raycastTarget = false;
        }

        /// <summary>Gesture alternatives and the one clay action, all at the touch-target floor.</summary>
        void BuildControls(RectTransform root)
        {
            RectTransform row = UIKit.CreateRect("MapControls", root);
            row.anchorMin = new Vector2(0f, 0f);
            row.anchorMax = new Vector2(1f, 0f);
            row.pivot = new Vector2(0.5f, 0f);
            row.offsetMin = new Vector2(ScreenInset, ScreenInset);
            row.offsetMax = new Vector2(-ScreenInset, ScreenInset + ControlsHeight);
            UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.TouchGap, new RectOffset(),
                TextAnchor.MiddleCenter);

            Button zoomOut = UIKit.CreateButton(row, "ZoomOut", Loc.T("world.progress_map.zoom_out"),
                UIKit.ButtonVariant.Secondary, () =>
                {
                    if (_viewportController != null) _viewportController.ZoomOut();
                });
            UIKit.Layout(zoomOut).flexibleWidth = 1f;

            Button zoomIn = UIKit.CreateButton(row, "ZoomIn", Loc.T("world.progress_map.zoom_in"),
                UIKit.ButtonVariant.Secondary, () =>
                {
                    if (_viewportController != null) _viewportController.ZoomIn();
                });
            UIKit.Layout(zoomIn).flexibleWidth = 1f;

            Button close = UIKit.CreateButton(row, "CloseMap", Loc.T("world.progress_map.close"),
                UIKit.ButtonVariant.Primary, CloseNow);
            UIKit.Layout(close).flexibleWidth = 1.35f;
        }

        /// <summary>
        /// A symmetric padding from two spacing tokens. <see cref="RectOffset"/> takes whole
        /// pixels, and the tokens are the design document's points converted, so they round here
        /// rather than at every call site.
        /// </summary>
        static RectOffset Inset(float horizontal, float vertical)
        {
            int x = Mathf.RoundToInt(horizontal);
            int y = Mathf.RoundToInt(vertical);
            return new RectOffset(x, x, y, y);
        }

        void CloseNow()
        {
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ ink

        /// <summary>Below the sheet: the table the chart is lying on, not a colour of its own.</summary>
        static readonly Color BackdropColour = DesignTokens.Surface.Background;

        static readonly Color PlateInk = DesignTokens.Ink.OnScroll;
        static readonly Color MutedInk = DesignTokens.Ink.OnScrollMuted;
    }
}
