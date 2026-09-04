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
    /// The map the player opens from the HUD: the road through the season, one stop per stage.
    ///
    /// It is a progression surface, not level selection: the road shows compact selectable stages,
    /// while one fixed card below the map reveals the selected stage's title, reward and condition.
    /// Selecting a stage changes information only; it never skips the game.
    ///
    /// How many stops there are is read from the stage table through
    /// <see cref="MapChartArt.JourneyCount"/>, and where each one sits is read from the road traced
    /// through the painted background. Nothing here counts the season itself: this file used to
    /// carry three title keys and three reward ids beside three anchors, and the quiet
    /// <c>Mathf.Min</c> over those three lengths meant a fourth stage could be declared and simply
    /// not drawn, with nothing logged. The lengths are now checked against each other out loud.
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
        // 256 held the title, the piece and a diary of up to five lines with nothing to spare on a
        // phone; the objective's two lines are what the extra is for. Measured on an iPhone 17
        // Pro: at 312 a four-line diary under the objective left about half a line, and stage 7
        // writes five. The map viewport gives up the same amount, which it can afford.
        static readonly float StatusHeight = DesignTokens.Px(344f);
        static readonly float ControlsHeight = UIKit.ButtonMinHeight;
        static readonly float RewardIconSize = DesignTokens.Px(38f);

        /// <summary>
        /// The marker at its largest, which is the size a three-stop road draws at and the size
        /// this map shipped with. A longer season draws smaller markers; see
        /// <see cref="MarkerSizeFor"/>.
        /// </summary>
        static readonly float MaxNodeMarkerSize = DesignTokens.Px(72f);

        /// <summary>
        /// The marker at its smallest. Well under the design system's touch floor as a length on
        /// the sheet, and comfortably over it on screen: the map content is never drawn below about
        /// 1.4x, so the smallest marker still lands around 56 points under a finger.
        /// </summary>
        static readonly float MinNodeMarkerSize = DesignTokens.Px(40f);

        /// <summary>
        /// The share of the gap between two stops a marker may occupy. Below one, so consecutive
        /// markers keep clear air between them instead of touching.
        /// </summary>
        const float NodeMarkerClearance = 0.7f;

        /// <summary>Selection ring, as a multiple of the marker. Px(72) + Px(12) at full size.</summary>
        const float SelectionRingRatio = 84f / 72f;

        /// <summary>Current-location ring, as a multiple of the marker. Px(72) + Px(20) at full size.</summary>
        const float CurrentRingRatio = 92f / 72f;

        /// <summary>
        /// One title key per stage, written out.
        ///
        /// They stay literals rather than being composed from a stage id at runtime, because the
        /// content validator's locale-key check reads key-shaped literals out of .cs files. A key
        /// assembled from a loop counter is invisible to it, and the first anyone would learn that
        /// a stage title is missing from ui.json is a player reading
        /// "world.progress_map.day.7.title" off the place card, in both languages at once.
        /// </summary>
        static readonly string[] DayTitleKeys =
        {
            "world.progress_map.day.1.title",
            "world.progress_map.day.2.title",
            "world.progress_map.day.3.title",
            "world.progress_map.day.4.title",
            "world.progress_map.day.5.title",
            "world.progress_map.day.6.title",
            "world.progress_map.day.7.title",
            "world.progress_map.day.8.title",
            "world.progress_map.day.9.title"
        };

        /// <summary>
        /// One objective per stage, written out — the same reason as the titles. What the stop asks
        /// of the player in the fiction: the day's work, its choice, its threat. Never a reading
        /// (rules 19 and 20), never a prayer (rule 13), never a reference (rule 12).
        /// </summary>
        static readonly string[] DayObjectiveKeys =
        {
            "world.progress_map.objective.1",
            "world.progress_map.objective.2",
            "world.progress_map.objective.3",
            "world.progress_map.objective.4",
            "world.progress_map.objective.5",
            "world.progress_map.objective.6",
            "world.progress_map.objective.7",
            "world.progress_map.objective.8",
            "world.progress_map.objective.9"
        };

        static WorldMapView _current;

        Canvas _canvas;
        PlayerController _player;
        MapViewportController _viewportController;
        GameState _state;
        int _currentDay;
        int _selectedDay;

        /// <summary>How many stops this build of the map actually drew. See <see cref="NodeCount"/>.</summary>
        int _dayCount;

        /// <summary>Marker side for this build, sized so nine stops do not overlap. See <see cref="MarkerSizeFor"/>.</summary>
        float _nodeMarkerSize;

        readonly List<Image> _selectionRings = new List<Image>();

        Text _detailState;
        Text _detailTitle;
        Text _detailObjective;
        Text _detailItemName;
        Text _detailItemState;
        Text _detailDiary;
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

            // Settled before anything reads a day, because the current stop, the selection clamp
            // and the place card all have to agree with the number of markers actually drawn.
            _dayCount = NodeCount();
            _currentDay = _state != null ? Mathf.Clamp(_state.day, 1, _dayCount) : 1;
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
        /// How many stops to draw, and a complaint when the two sources disagree.
        ///
        /// The season declares its length in stages.json; this file declares one title key per
        /// stage. They are meant to be the same number, and when they are not the map can only draw
        /// the shorter of the two — but it says so, which is the whole change. What stood here was
        /// a bare <c>Mathf.Min</c> over three lengths: adding a stage without adding its title drew
        /// the old count and logged nothing, so the failure arrived as a map that looked finished
        /// and simply stopped one stage early.
        ///
        /// The complaint is gated on the stage table having loaded. With no stages the count falls
        /// back to the three painted clearings, which disagrees with nine title keys on purpose and
        /// which GameData has already reported as the real fault.
        /// </summary>
        int NodeCount()
        {
            int declared = MapChartArt.JourneyCount;
            int drawn = Mathf.Min(declared, DayTitleKeys.Length);
            if (declared != DayTitleKeys.Length && GameData.Stages.Length > 0)
            {
                Debug.LogError("[WorldMap] The stage table declares " + declared +
                    " stage(s) and WorldMapView carries " + DayTitleKeys.Length +
                    " title key(s), so the progression map can only draw " + drawn +
                    ". Add or remove a world.progress_map.day.<n>.title key to match stages.json.");
            }

            return drawn;
        }

        /// <summary>
        /// The stops of the season, spaced along the road painted into the background.
        ///
        /// The terrain carries only annotation-sized information: marker, day and state. Selecting
        /// one updates the single detail card below the viewport. That progressive disclosure keeps
        /// neighbouring copy from colliding while preserving every title, reward and unlock rule.
        /// </summary>
        void BuildJourney(RectTransform sheet, GameState state)
        {
            var anchors = new Vector2[_dayCount];
            for (int i = 0; i < anchors.Length; i++)
            {
                anchors[i] = MapChartArt.JourneyAnchor(i);
            }

            _nodeMarkerSize = MarkerSizeFor(anchors);

            for (int i = 0; i < anchors.Length; i++)
            {
                BuildJourneyNode(sheet, state, i + 1, anchors[i], DayTitleKeys[i]);
            }
        }

        /// <summary>
        /// A marker small enough that two neighbouring stops do not overlap.
        ///
        /// The markers travel with the map, so their size is a length on the sheet and the gap
        /// between two stops shrinks as the season gets longer: three stops leave about 566 units
        /// between them, nine leave about 154, and the marker this map shipped with is 199. Fixing
        /// the size would have nine circles growing through each other. Fixing the season length
        /// instead is worse — the whole point of the stage table is that the length is data.
        ///
        /// The clamp's upper bound is the shipped size, so a three-stop road draws exactly what it
        /// drew before, and the rings scale with the marker rather than adding a fixed inset that
        /// would reach into the neighbouring stop at the small end.
        /// </summary>
        static float MarkerSizeFor(Vector2[] anchors)
        {
            float closest = float.MaxValue;
            for (int i = 1; i < anchors.Length; i++)
            {
                var step = new Vector2(
                    (anchors[i].x - anchors[i - 1].x) * MapChartArt.ContentSize.x,
                    (anchors[i].y - anchors[i - 1].y) * MapChartArt.ContentSize.y);
                closest = Mathf.Min(closest, step.magnitude);
            }

            if (closest == float.MaxValue)
            {
                return MaxNodeMarkerSize;
            }

            return Mathf.Clamp(closest * NodeMarkerClearance, MinNodeMarkerSize, MaxNodeMarkerSize);
        }

        void BuildJourneyNode(RectTransform sheet, GameState state, int day, Vector2 anchor,
            string titleKey)
        {
            JourneyNodeState nodeState = StateForDay(state, day);

            RectTransform marker = UIKit.CreateRect("MapNode" + day + "Marker", sheet);
            marker.anchorMin = anchor;
            marker.anchorMax = anchor;
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(_nodeMarkerSize, _nodeMarkerSize);

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
            float selectedSize = _nodeMarkerSize * SelectionRingRatio;
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

            // Which side the card hangs on is the node's own position, not its number. A stop high
            // on the sheet hangs its label below so the card does not run off the top edge of the
            // map; the old `day == 3` said the same thing about the only high stop there was, and
            // stopped being true the moment there were nine.
            bool below = anchor.y > 0.5f;
            label.pivot = below ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
            label.anchoredPosition = new Vector2(0f,
                (_nodeMarkerSize * 0.5f + DesignTokens.Space.S4) * (below ? -1f : 1f));

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
            float ringSize = _nodeMarkerSize * CurrentRingRatio;
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

            // The opposite side from the node's own DIA N card, whose side is chosen in
            // BuildJourneyNode by the same test inverted. Both cards are glass, both are about the
            // same height, and their offsets differ by four points — put them on one side and the
            // second is a smudge over the first. Only day three ever sat high enough on the sheet
            // to hang its card downwards, and the player leaves it after one session; on a
            // nine-stop road the top half of the map is stages five through nine, which is most of
            // the season and every screenshot of it.
            bool below = anchor.y <= 0.5f;
            label.pivot = below ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
            label.anchoredPosition = new Vector2(0f,
                (_nodeMarkerSize * 0.5f + DesignTokens.Space.S8) * (below ? -1f : 1f));

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
            // Clamped to the stops this build drew, not to the stages declared: when the two
            // disagree the extra stages have no marker, no title and no ring to light up.
            _selectedDay = Mathf.Clamp(day, 1, Mathf.Max(1, _dayCount));
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

            // Shown for every stop, reached or not: the title already foreshadows each day, and
            // what a day asks is the one thing the map can say about a stop nobody has reached.
            // The diary below stays reached-only, because it is what the day wrote.
            if (_detailObjective != null)
            {
                _detailObjective.text = Loc.T(DayObjectiveKeys[Mathf.Clamp(index, 0, DayObjectiveKeys.Length - 1)]);
            }

            // The featured item comes from the stage table, which is the file that decides what a
            // stage is about. A stage may feature any catalogue entry whether or not that entry
            // happens to open here — it is a signpost, not a grant — and a stage that names an item
            // the catalogue has not got yet still shows a card, with the stand-in image.
            string itemId = GameData.Stage(_selectedDay).reward_item;
            CatalogItemDef item = string.IsNullOrEmpty(itemId) ? null : CharacterCatalog.Item(itemId);
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

            if (_detailDiary != null)
            {
                _detailDiary.text = DiaryFor(_state, _selectedDay, nodeState);
            }
        }

        /// <summary>
        /// The day, told back from what it wrote: courses laid, the night, the choice made, the
        /// contest's ending, the gate. One sentence per fact, and only facts the run recorded — a
        /// choice never made leaves no line, and a day not reached says so and nothing else.
        /// </summary>
        static string DiaryFor(GameState state, int day, JourneyNodeState nodeState)
        {
            if (state == null || nodeState == JourneyNodeState.Locked)
            {
                return Loc.T("world.progress_map.diary.locked");
            }

            var lines = new List<string>();
            StageDef stage = GameData.Stage(day);

            int laid = state.Counter(GameFlags.LaidCounter(day));
            if (laid > 0)
            {
                lines.Add(Loc.Plural("world.progress_map.diary.laid", laid));
            }

            // Choices, by the flags the branches raise. Day-bound through the stage that hosts
            // the conversation, so a flag reads on the stop it belongs to and nowhere else.
            if (day == 2)
            {
                if (state.HasFlag(GameFlags.AcceptedInvite)) lines.Add(Loc.T("world.progress_map.diary.invite.accepted"));
                else if (state.HasFlag(GameFlags.RefusedInvite)) lines.Add(Loc.T("world.progress_map.diary.invite.refused"));
                if (state.HasFlag("donated_rubble")) lines.Add(Loc.T("world.progress_map.diary.donated"));
            }
            else if (day == 3)
            {
                if (state.HasFlag(GameFlags.TekoaHelped)) lines.Add(Loc.T("world.progress_map.diary.tekoa.helped"));
                else if (state.HasFlag(GameFlags.TekoaKept)) lines.Add(Loc.T("world.progress_map.diary.tekoa.kept"));
            }
            else if (day == 5)
            {
                if (state.HasFlag(GameFlags.PathCleared)) lines.Add(Loc.T("world.progress_map.diary.path.cleared"));
                else if (state.HasFlag(GameFlags.PathOwn)) lines.Add(Loc.T("world.progress_map.diary.path.own"));
            }
            else if (day == DayCycle.HalfAndHalfStage)
            {
                if (state.HasFlag(GameFlags.RoadDoubted)) lines.Add(Loc.T("world.progress_map.diary.road.doubted"));
                else if (state.HasFlag(GameFlags.RoadBelieved)) lines.Add(Loc.T("world.progress_map.diary.road.believed"));
            }

            if (stage != null && !string.IsNullOrEmpty(stage.contest))
            {
                int outcome = state.Counter(GameFlags.ContestOutcomeCounter(stage.contest)) - 1;
                if (outcome == (int)SheepGate.Contest.ContestOutcome.EnemyWithdrew) lines.Add(Loc.T("world.progress_map.diary.contest.withdrew"));
                else if (outcome == (int)SheepGate.Contest.ContestOutcome.PlayerBroke) lines.Add(Loc.T("world.progress_map.diary.contest.broke"));
                else if (outcome == (int)SheepGate.Contest.ContestOutcome.TurnLimit) lines.Add(Loc.T("world.progress_map.diary.contest.limit"));
            }

            // The night that ended this day, once it has: the work counter is only ever written
            // by a resolved night, so its presence is the record that the night happened.
            if (state.counters != null && state.counters.ContainsKey(GameFlags.NightWorkCounter(day)))
            {
                lines.Add(Loc.T(state.HasFlag(GameFlags.WatchPostedForDay(day))
                    ? "world.progress_map.diary.night.watched"
                    : "world.progress_map.diary.night.unwatched"));
                int nightWork = state.Counter(GameFlags.NightWorkCounter(day));
                if (nightWork > 0)
                {
                    lines.Add(Loc.Plural("world.progress_map.diary.night_work", nightWork));
                }
            }

            if (stage != null && stage.terminal && stage.closes_gate)
            {
                if (state.HasFlag(GameFlags.VocationRevealed))
                {
                    lines.Add(Loc.T("world.progress_map.diary.gate.closed"));
                }
                else
                {
                    int left = StageDirector.GateCoursesLeft(stage);
                    if (left > 0)
                    {
                        lines.Add(Loc.Plural("toast.gate.waits", left));
                    }
                }
            }

            if (lines.Count == 0)
            {
                return Loc.T("world.progress_map.diary.nothing_yet");
            }

            return string.Join("\n", lines.ToArray());
        }

        /// <summary>
        /// What one stop shows: behind us, here, or not yet.
        ///
        /// The clamp is the number of stops drawn, not a literal season length — this method used
        /// to clamp against 3 while the two clamps either side of it already asked the map how long
        /// the road was, and that lone 3 is what would have pinned every save past stage 3 onto
        /// stage 3's marker.
        ///
        /// The vocation flag still means "this run reached its end", and it still turns the stop the
        /// player is standing on from AGORA into CONCLUÍDO so a finished season reads as finished.
        /// What it no longer does is complete the stops AHEAD: a save migrated from the three-day
        /// build resumes mid-season with that flag already earned, and marking all nine stops
        /// complete for it would tell the player they had finished a season they have five stages
        /// left of. Rule 7 is untouched either way — nothing here ever moves a stop backwards.
        /// </summary>
        JourneyNodeState StateForDay(GameState state, int day)
        {
            int currentDay = state != null ? Mathf.Clamp(state.day, 1, Mathf.Max(1, _dayCount)) : 1;
            if (day < currentDay)
            {
                return JourneyNodeState.Complete;
            }

            if (day == currentDay)
            {
                bool journeyComplete = state != null && state.HasFlag(GameFlags.VocationRevealed);
                return journeyComplete ? JourneyNodeState.Complete : JourneyNodeState.Current;
            }

            return JourneyNodeState.Locked;
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

            Text objectiveHeading = UIKit.CreateText(card, "SelectedObjectiveHeading",
                Loc.T("world.progress_map.objective.heading"), DesignTokens.Type.Minimum,
                DesignTokens.Ink.OnScrollMuted, TextAnchor.MiddleLeft, DesignTokens.TypeRole.BodyStrong);
            objectiveHeading.raycastTarget = false;

            _detailObjective = UIKit.CreateText(card, "SelectedObjective", string.Empty,
                DesignTokens.Type.Minimum, DesignTokens.Ink.OnScroll, TextAnchor.UpperLeft);
            _detailObjective.raycastTarget = false;

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

            // The diary: what that day actually was in this run — what the player laid, what the
            // night did, what they chose, how a contest ended, and on the last day whether the
            // gate is still waiting. Read off counters and flags the day wrote; nothing here is
            // authored per stage, so a stop the run has not reached says only that.
            _detailDiary = UIKit.CreateText(card, "SelectedDiary", string.Empty,
                DesignTokens.Type.Minimum, DesignTokens.Ink.OnScroll, TextAnchor.UpperLeft);
            _detailDiary.raycastTarget = false;

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
