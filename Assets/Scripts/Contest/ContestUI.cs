using System;
using System.Collections.Generic;
using System.Text;
using SheepGate.Core;
using SheepGate.Player;
using SheepGate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.Contest
{
    /// <summary>
    /// The screen for the trial of morale, built programmatically with <see cref="UIKit"/> and
    /// pushed onto the <see cref="ModalRoot"/> stack.
    ///
    /// What this screen refuses to be: a combat HUD. There is no health, no damage number, no
    /// body count and no defeat banner. The two meters are ÂNIMO — how much fight is left in the
    /// people on this wall — and the firmness of the people on the other side. The fight ends
    /// when one of the two gives up, which is what the passage behind the day actually describes.
    ///
    /// That refusal is why neither meter is red. Ours is <c>Ambient.Growth</c>, the colour the
    /// world gains as it heals; theirs is <c>Neutral.N500</c>, stone. A feedback red on either one
    /// would say "damage" in a screen whose whole argument is that nobody is being hurt, and
    /// <c>Feedback.Error</c> exists for a thing going wrong, not for a person on the other side.
    /// Nothing here flashes on a hit, because there are no hits to flash on.
    ///
    /// The menu states what each move does in plain words, because the point of the day is that
    /// the player recognises the strongest move when it arrives rather than discovering it by
    /// trial and error. The move the page unlocks arrives in the gold Quest variant — the design
    /// system's "this is the new thing" — and gold is the one accent this screen spends.
    /// </summary>
    public sealed class ContestUI : MonoBehaviour
    {
        public const string ModalId = "contest";

        /// <summary>
        /// How many report lines the log keeps.
        ///
        /// Three and not four since the migration to the design system's type: the log reads at
        /// <c>Type.Body</c> now, which is a third larger than the metadata size it used to use, and
        /// a report line is two to four lines of it. Four entries would mean the buffer holds twice
        /// what the panel can ever show, and the extra pair only ever exists to be clipped.
        /// </summary>
        const int LogEntries = 3;

        /// <summary>
        /// How far a card swells when it is first offered. The design system specifies a reward
        /// entrance as scale 1 to 1.04 to 1 over <c>Motion.Reward</c>, and no confetti.
        /// </summary>
        const float RewardPeakScale = 1.04f;

        static ContestUI _instance;

        // ------------------------------------------------------------------ layout

        // Every length below is in canvas reference units and comes from a token, so the screen
        // re-measures itself if the scale ever moves. The header and the menu are fixed budgets
        // pinned to the top and the bottom; the log is stretched between them and absorbs all the
        // slack, which is what makes the arrangement safe on a phone whose safe area eats a
        // different amount of height than the one this was measured on.

        static readonly float Gutter = DesignTokens.Space.Gutter;
        static readonly float TopInset = DesignTokens.Space.S16;
        static readonly float SectionGap = DesignTokens.Space.S16;
        static readonly float BottomInset = DesignTokens.Space.SafeAreaBottom;

        /// <summary>Padding inside the header card and inside a move card.</summary>
        static readonly float CardPadding = DesignTokens.Space.S16;
        static readonly float MoveCardPadding = DesignTokens.Space.S20;

        /// <summary>The eyebrow-and-turn row. Comfortably over one line of <c>Type.Mono</c>.</summary>
        static readonly float TurnRowHeight = DesignTokens.Px(26f);

        /// <summary>Space held for the turn counter so the eyebrow does not shift as digits change.</summary>
        static readonly float TurnWidth = DesignTokens.Px(120f);

        /// <summary>
        /// Header card: the row, then two progress components, then the card's own padding.
        /// Written as a sum rather than as a number so that a change to the progress component or
        /// to the spacing scale moves the card instead of silently overflowing it.
        /// </summary>
        static readonly float HeaderHeight =
            TurnRowHeight
            + DesignTokens.Space.S16 + UIKit.ProgressHeight
            + DesignTokens.Space.S16 + UIKit.ProgressHeight
            + CardPadding * 2f;

        /// <summary>
        /// Height the move menu is given. Two typical cards are about 15 units taller than this,
        /// on purpose: the second card is cut just inside its own bottom padding, which is what
        /// tells a thumb there is more below without spending a scrollbar on saying so.
        /// </summary>
        static readonly float MenuHeight = DesignTokens.Px(236f);

        /// <summary>
        /// How far the log's clipping mask fades its edges.
        ///
        /// The log is a running report anchored to its bottom, so an entry that no longer fits
        /// leaves through the top. A hard cut through the middle of a word reads as a rendering
        /// fault; a fade reads as older news. The text is inset by the same amount at the bottom,
        /// because <see cref="RectMask2D.softness"/> is symmetric per axis and the newest line —
        /// the one that matters — sits exactly there.
        /// </summary>
        static readonly int LogFade = Mathf.RoundToInt(DesignTokens.Space.S12);

        /// <summary>
        /// Raised when the screen leaves, which is after the player has read the ending and tapped
        /// on. Whatever runs the rest of day three can either react to
        /// <see cref="MoraleContest.Finished"/> at once or wait for this and keep the beats in
        /// order.
        /// </summary>
        public event Action Dismissed;

        MoraleContest _contest;
        RectTransform _container;

        Text _turnText;
        ProgressBar _moraleMeter;
        ProgressBar _resolveMeter;
        Text _logText;
        ScrollRect _menuScroll;
        RectTransform _outcomePanel;
        Text _outcomeTitle;
        Text _outcomeBody;

        readonly List<string> _log = new List<string>();
        readonly List<MoveCard> _cards = new List<MoveCard>();

        bool _visible;
        bool _lockHeld;

        /// <summary>One card in the move menu. Kept so a repaint updates instead of rebuilding.</summary>
        sealed class MoveCard
        {
            public string Id;
            public GameObject Root;
            public VariantButton Button;
            public Text Description;
            public UIKit.ButtonVariant Variant;

            /// <summary>Set the first time the card is offered, so it is announced exactly once.</summary>
            public bool Announced;
        }

        public bool IsVisible
        {
            get { return _visible; }
        }

        /// <summary>Finds the screen or creates it. It lives on its own object, outside the
        /// modal container, so closing the panel never destroys the component that owns it.</summary>
        public static ContestUI EnsureInstance()
        {
            if (_instance != null)
            {
                return _instance;
            }

            ContestUI existing = FindFirstObjectByType<ContestUI>();
            if (existing != null)
            {
                _instance = existing;
                return _instance;
            }

            var go = new GameObject("ContestUI");
            _instance = go.AddComponent<ContestUI>();
            return _instance;
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[ContestUI] A second contest screen was created; destroying the duplicate.");
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        void OnDestroy()
        {
            Unsubscribe();
            ReleaseLock();

            if (_instance == this)
            {
                _instance = null;
            }
        }

        // ------------------------------------------------------------------ binding

        /// <summary>Listens to a contest. Rebinding drops the previous one.</summary>
        public void Bind(MoraleContest contest)
        {
            if (_contest == contest)
            {
                return;
            }

            Unsubscribe();
            _contest = contest;

            if (_contest == null)
            {
                return;
            }

            _contest.Changed += Repaint;
            _contest.TurnStarted += OnTurnStarted;
            _contest.Reported += OnReported;
            _contest.Finished += OnFinished;
        }

        void Unsubscribe()
        {
            if (_contest == null)
            {
                return;
            }

            _contest.Changed -= Repaint;
            _contest.TurnStarted -= OnTurnStarted;
            _contest.Reported -= OnReported;
            _contest.Finished -= OnFinished;
            _contest = null;
        }

        // ------------------------------------------------------------------ visibility

        public void Show()
        {
            if (_visible && _container != null)
            {
                Repaint();
                return;
            }

            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[ContestUI] No modal root is available; the trial has no screen.");
                return;
            }

            _container = root.Push(ModalId);
            if (_container == null)
            {
                return;
            }

            _log.Clear();
            _cards.Clear();

            Build(_container);
            _visible = true;

            if (!_lockHeld)
            {
                _lockHeld = true;
                InputLock.Push();
            }

            Repaint();
        }

        public void Hide()
        {
            bool wasVisible = _visible;

            _visible = false;
            _container = null;
            _cards.Clear();
            _menuScroll = null;

            ModalRoot.CloseId(ModalId);
            ReleaseLock();

            if (wasVisible)
            {
                RaiseDismissed();
            }
        }

        /// <summary>
        /// Watchdog for a screen closed from outside, such as the hardware back button. While the
        /// trial is still running the screen comes back; once it is over the lock is released, so
        /// the world can never end up locked behind a panel that no longer exists.
        /// </summary>
        void Update()
        {
            if (!_visible || _container != null)
            {
                return;
            }

            if (_contest != null && _contest.IsRunning)
            {
                _visible = false;
                Show();
                return;
            }

            Hide();
        }

        void RaiseDismissed()
        {
            Action handler = Dismissed;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception exception)
            {
                Debug.LogError("[ContestUI] A listener threw while the screen closed: " + exception.Message);
            }
        }

        void ReleaseLock()
        {
            if (!_lockHeld)
            {
                return;
            }

            _lockHeld = false;
            InputLock.Pop();
        }

        // ------------------------------------------------------------------ construction

        void Build(RectTransform container)
        {
            BuildHeader(container);
            BuildLog(container);
            BuildMenu(container);
            BuildOutcome(container);
        }

        void BuildHeader(RectTransform container)
        {
            // Glass and not Panel: this card sits over the lit tilemap, and the design system's
            // floor for text over the scene is a 72% veil. Surface.SceneVeil is 88%, which is what
            // Glass resolves to.
            Image card = UIKit.CreateCard(container, "Header", UIKit.CardStyle.Glass);
            card.raycastTarget = false;

            var header = (RectTransform)card.transform;
            UIKit.AnchorTop(header, HeaderHeight, Gutter, Gutter, TopInset);
            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S16, Pad(CardPadding, CardPadding));

            RectTransform titleRow = UIKit.CreateRect("TitleRow", header);
            UIKit.HorizontalGroup(titleRow.gameObject, DesignTokens.Space.S12, new RectOffset(), TextAnchor.MiddleLeft);
            LayoutElement titleRowLayout = UIKit.Layout(titleRow);
            if (titleRowLayout != null)
            {
                titleRowLayout.minHeight = TurnRowHeight;
                titleRowLayout.preferredHeight = TurnRowHeight;
            }

            // An eyebrow, not a screen title: the loudest thing here should be the two meters and
            // the moves, not the name of a fight the player is already in.
            Text title = UIKit.CreateText(
                titleRow,
                "Title",
                Loc.T("contest.title"),
                DesignTokens.Type.Minimum,
                DesignTokens.Ink.Muted,
                TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.BodyStrong);
            LayoutElement titleLayout = UIKit.Layout(title);
            if (titleLayout != null)
            {
                titleLayout.flexibleWidth = 1f;
                titleLayout.minHeight = TurnRowHeight;
            }

            // Mono, because it is a count, and it counts down to a number the player can plan
            // against. Tabular digits are what stop the row twitching as the turn rolls over.
            _turnText = UIKit.CreateText(
                titleRow,
                "Turn",
                string.Empty,
                DesignTokens.Type.Mono,
                DesignTokens.Ink.Secondary,
                TextAnchor.MiddleRight,
                DesignTokens.TypeRole.Mono);
            LayoutElement turnLayout = UIKit.Layout(_turnText);
            if (turnLayout != null)
            {
                // A minimum as well as a preferred width: the eyebrow beside it is flexible, and
                // without a floor a long translation of the eyebrow would squeeze the counter until
                // it wrapped "de 8" onto a second line.
                turnLayout.minWidth = TurnWidth;
                turnLayout.preferredWidth = TurnWidth;
                turnLayout.flexibleWidth = 0f;
                turnLayout.minHeight = TurnRowHeight;
            }

            _moraleMeter = BuildMeter(header, "MoraleMeter", Loc.T("contest.meter.morale"),
                                      DesignTokens.Ambient.Growth);

            _resolveMeter = BuildMeter(header, "ResolveMeter", Loc.T("contest.meter.resolve"),
                                       DesignTokens.Neutral.N500);
        }

        /// <summary>
        /// One meter: label, bar and fraction, which is the only shape the design system allows a
        /// progress reading to take. <see cref="UIKit.CreateProgress"/> builds all three together
        /// and animates the value over <c>Motion.BarFill</c>, so nothing here draws a bar by hand.
        /// </summary>
        ProgressBar BuildMeter(RectTransform parent, string name, string label, Color fillColor)
        {
            ProgressBar bar = UIKit.CreateProgress(parent, name, label);
            TintMeter(bar, fillColor);
            return bar;
        }

        /// <summary>
        /// Repaints a progress bar's fill.
        ///
        /// The component is built in the brand's clay, which is right for a resource and wrong for
        /// two readings the player has to tell apart at a glance. It warns rather than failing
        /// quietly if the hierarchy ever moves: a meter that silently kept the wrong colour is
        /// exactly the class of bug this project keeps finding in runtime-built UI.
        /// </summary>
        static void TintMeter(ProgressBar bar, Color fillColor)
        {
            if (bar == null)
            {
                return;
            }

            Transform fill = bar.transform.Find("Track/Fill");
            Image image = fill != null ? fill.GetComponent<Image>() : null;
            if (image == null)
            {
                Debug.LogWarning("[ContestUI] Progress bar '" + bar.name + "' has no Track/Fill image; " +
                                 "the meter keeps the component's default colour.");
                return;
            }

            image.color = fillColor;
        }

        void BuildLog(RectTransform container)
        {
            Image panel = UIKit.CreateCard(container, "Log", UIKit.CardStyle.Panel);
            panel.raycastTarget = false;

            // Stretched rather than given a height: the header and the menu are fixed budgets at
            // the two ends, and whatever the safe area leaves between them belongs to the report.
            UIKit.Stretch(
                (RectTransform)panel.transform,
                Gutter,
                Gutter,
                TopInset + HeaderHeight + SectionGap,
                BottomInset + MenuHeight + SectionGap);

            RectTransform viewport = UIKit.CreateRect("Viewport", (RectTransform)panel.transform);
            UIKit.Stretch(viewport, DesignTokens.Space.S20, DesignTokens.Space.S20,
                          DesignTokens.Space.S16, DesignTokens.Space.S8);

            var mask = viewport.gameObject.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(0, LogFade);

            _logText = UIKit.CreateText(
                viewport,
                "Lines",
                string.Empty,
                DesignTokens.Type.Body,
                DesignTokens.Ink.Primary,
                TextAnchor.LowerLeft);

            var textRect = (RectTransform)_logText.transform;
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 0f);
            textRect.pivot = new Vector2(0.5f, 0f);

            // Lifted clear of the mask's bottom fade, so the newest line — the one the player is
            // actually reading — arrives at full opacity.
            textRect.offsetMin = new Vector2(0f, LogFade);
            textRect.offsetMax = new Vector2(0f, LogFade);

            var fitter = _logText.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        void BuildMenu(RectTransform container)
        {
            RectTransform menuRect = UIKit.CreateRect("MenuArea", container);
            UIKit.AnchorBottom(menuRect, MenuHeight, Gutter, Gutter, BottomInset);

            RectTransform content;
            _menuScroll = UIKit.CreateScrollView(menuRect, "MenuScroll", out content);
            UIKit.Stretch((RectTransform)_menuScroll.transform);

            // The scroll view ships with padding of its own, sized for a narrower column than this
            // one. The gutter is already spent by MenuArea, so the content column keeps only the
            // vertical rhythm: cards a touch-gap apart, and room under the last one.
            var contentGroup = content.GetComponent<VerticalLayoutGroup>();
            if (contentGroup != null)
            {
                contentGroup.spacing = DesignTokens.Space.S12;
                contentGroup.padding = new RectOffset(0, 0, 0, Mathf.RoundToInt(DesignTokens.Space.S24));
            }

            if (_contest == null)
            {
                return;
            }

            ContestMoveDef[] moves = _contest.Moves;
            for (int i = 0; i < moves.Length; i++)
            {
                ContestMoveDef move = moves[i];
                if (move == null || string.IsNullOrEmpty(move.id))
                {
                    continue;
                }

                _cards.Add(BuildCard(content, move));
            }
        }

        /// <summary>
        /// One move, as a card the whole of which is the control.
        ///
        /// It is a <see cref="VariantButton"/> and not a bare <c>Button</c> because the states the
        /// design system asks for — a 2px gold focus ring at a 2px offset, and 0.40 opacity while
        /// the card is not takeable — live in that type, and a hand-rolled copy of them here would
        /// be a second opinion about how a control looks. The button owns the fill, the border, the
        /// ring and the title's ink; the description is left alone, which is why its colour is
        /// chosen against the variant rather than assumed.
        ///
        /// The frame, the ring and the border are all excluded from the card's own layout group:
        /// they are overlays on the whole rect, and a vertical group would otherwise stack them as
        /// two more rows above the words.
        /// </summary>
        MoveCard BuildCard(RectTransform parent, ContestMoveDef move)
        {
            // Gold is "this is the new thing", which is precisely what the move behind the page is.
            // It is the only gold on this screen — the design system allows one — and it is not on
            // screen at the same time as the page's own gold, because this card does not become
            // available until the page has closed.
            UIKit.ButtonVariant variant = move.id == MoraleContest.MoveHalfAndHalf
                ? UIKit.ButtonVariant.Quest
                : UIKit.ButtonVariant.Secondary;

            UIKit.ButtonSkin skin = UIKit.SkinFor(variant);

            Image fill = UIKit.CreatePanel(parent, "Move_" + move.id, skin.Fill, UiSpriteKeys.FrameMd);
            var rect = (RectTransform)fill.transform;

            var group = fill.gameObject.AddComponent<CanvasGroup>();
            UIKit.VerticalGroup(fill.gameObject, DesignTokens.Space.S8,
                                Pad(MoveCardPadding, MoveCardPadding));

            Image border = null;
            if (skin.Border.a > 0.001f)
            {
                border = UIKit.CreatePanel(rect, "Border", skin.Border, UiSpriteKeys.FocusRing);
                UIKit.Stretch((RectTransform)border.transform);
                border.raycastTarget = false;
                IgnoreLayout(border);
            }

            Text title = UIKit.CreateText(
                rect,
                "Title",
                move.display ?? move.id,
                DesignTokens.Type.Body,
                skin.Label,
                TextAnchor.UpperLeft,
                DesignTokens.TypeRole.BodyStrong);

            Text description = UIKit.CreateText(
                rect,
                "Description",
                move.description ?? string.Empty,
                DesignTokens.Type.Body,
                DescriptionInk(variant),
                TextAnchor.UpperLeft);

            Image ring = UIKit.CreatePanel(rect, "FocusRing", DesignTokens.Brand.Secondary, UiSpriteKeys.FocusRing);
            UIKit.Stretch((RectTransform)ring.transform,
                          -UIKit.FocusRingOutset, -UIKit.FocusRingOutset,
                          -UIKit.FocusRingOutset, -UIKit.FocusRingOutset);
            ring.raycastTarget = false;
            IgnoreLayout(ring);
            ring.gameObject.SetActive(false);

            var button = fill.gameObject.AddComponent<VariantButton>();

            // The base tint multiplies the graphic's colour and cannot express "parchment at 8%
            // becomes parchment at 14%". VariantButton owns the colours outright.
            button.transition = Selectable.Transition.None;
            button.targetGraphic = fill;
            button.Bind(variant, fill, border, ring, title, group);

            // A minimum and deliberately no preferred height: a LayoutElement's preferred height
            // outranks whatever the card's own vertical group computes, so setting one here would
            // freeze every card at one touch target and push the description out through the
            // bottom of its own frame.
            LayoutElement cardLayout = UIKit.Layout(button);
            if (cardLayout != null)
            {
                cardLayout.minHeight = DesignTokens.Space.TouchTarget;
            }

            string moveId = move.id;
            button.onClick.AddListener(() => OnMoveClicked(moveId));

            return new MoveCard
            {
                Id = moveId,
                Root = fill.gameObject,
                Button = button,
                Description = description,
                Variant = variant,
                Announced = false
            };
        }

        /// <summary>
        /// The description's colour, which the button does not own.
        ///
        /// On the gold card the dark-surface inks are invisible, so the supporting line is the
        /// on-gold ink held back a little rather than a lighter grey — opacity is the one change
        /// the token file permits, and a fifth grey nobody named is not.
        /// </summary>
        static Color DescriptionInk(UIKit.ButtonVariant variant)
        {
            return variant == UIKit.ButtonVariant.Quest
                ? UIKit.WithAlpha(DesignTokens.Ink.OnSecondary, 0.78f)
                : DesignTokens.Ink.Secondary;
        }

        /// <summary>Takes a full-rect overlay out of its parent's layout group.</summary>
        static void IgnoreLayout(Component target)
        {
            LayoutElement layout = UIKit.Layout(target);
            if (layout != null)
            {
                layout.ignoreLayout = true;
            }
        }

        static RectOffset Pad(float horizontal, float vertical)
        {
            int h = Mathf.RoundToInt(horizontal);
            int v = Mathf.RoundToInt(vertical);
            return new RectOffset(h, h, v, v);
        }

        void BuildOutcome(RectTransform container)
        {
            Image panel = UIKit.CreateCard(container, "Outcome", UIKit.CardStyle.Card);
            _outcomePanel = (RectTransform)panel.transform;

            // Anchored to the bottom with a zero height, then grown upward by the fitter. Endings
            // run from one line to five depending on which one arrived and which language it is
            // in, and a fixed height either clipped the long one or left a hole under the short
            // one.
            _outcomePanel.anchorMin = new Vector2(0f, 0f);
            _outcomePanel.anchorMax = new Vector2(1f, 0f);
            _outcomePanel.pivot = new Vector2(0.5f, 0f);
            _outcomePanel.offsetMin = new Vector2(Gutter, BottomInset);
            _outcomePanel.offsetMax = new Vector2(-Gutter, BottomInset);

            UIKit.VerticalGroup(panel.gameObject, DesignTokens.Space.S16,
                                Pad(DesignTokens.Space.S24, DesignTokens.Space.S24));

            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _outcomeTitle = UIKit.CreateText(
                _outcomePanel,
                "Title",
                string.Empty,
                DesignTokens.Type.Title,
                DesignTokens.Ink.Primary,
                TextAnchor.UpperLeft,
                DesignTokens.TypeRole.Title);

            _outcomeBody = UIKit.CreateText(
                _outcomePanel,
                "Body",
                string.Empty,
                DesignTokens.Type.Body,
                DesignTokens.Ink.Secondary,
                TextAnchor.UpperLeft);

            // Clay and not gold: this button ends the day, it does not open anything new, and the
            // screen has already spent its one gold on the move the page unlocked.
            UIKit.CreateButton(
                _outcomePanel,
                "Continue",
                Loc.T("contest.continue"),
                UIKit.ButtonVariant.Primary,
                Hide);

            _outcomePanel.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ contest events

        void OnMoveClicked(string moveId)
        {
            if (_contest == null)
            {
                return;
            }

            _contest.UseMove(moveId);
        }

        void OnTurnStarted(int turn)
        {
            Repaint();
        }

        void OnReported(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            _log.Add(line);
            while (_log.Count > LogEntries)
            {
                _log.RemoveAt(0);
            }

            PaintLog();
        }

        void OnFinished(ContestOutcome outcome)
        {
            if (_outcomePanel == null)
            {
                return;
            }

            if (_menuScroll != null)
            {
                _menuScroll.gameObject.SetActive(false);
            }

            _outcomeTitle.text = TitleFor(outcome);
            _outcomeBody.text = BodyFor(outcome);
            _outcomePanel.gameObject.SetActive(true);
            _outcomePanel.SetAsLastSibling();

            Repaint();
        }

        /// <summary>Three endings, no defeat among them. Nobody counts casualties on this wall.</summary>
        static string TitleFor(ContestOutcome outcome)
        {
            switch (outcome)
            {
                case ContestOutcome.EnemyWithdrew:
                    return Loc.T("contest.outcome.withdrew.title");

                case ContestOutcome.PlayerBroke:
                    return Loc.T("contest.outcome.broke.title");

                default:
                    return Loc.T("contest.outcome.limit.title");
            }
        }

        static string BodyFor(ContestOutcome outcome)
        {
            switch (outcome)
            {
                case ContestOutcome.EnemyWithdrew:
                    return Loc.T("contest.outcome.withdrew.body");

                case ContestOutcome.PlayerBroke:
                    return Loc.T("contest.outcome.broke.body");

                default:
                    return Loc.T("contest.outcome.limit.body");
            }
        }

        // ------------------------------------------------------------------ painting

        void Repaint()
        {
            if (!_visible)
            {
                return;
            }

            if (_container == null)
            {
                // The container was closed from outside (a hardware back press, for instance).
                // Rebuild rather than leave the trial without a screen.
                if (_contest != null && _contest.IsRunning)
                {
                    _visible = false;
                    Show();
                }

                return;
            }

            if (_contest == null)
            {
                return;
            }

            if (_turnText != null)
            {
                int turn = Mathf.Max(1, _contest.Turn);
                _turnText.text = Loc.T("contest.turn", turn, _contest.TurnLimit);
            }

            if (_moraleMeter != null)
            {
                _moraleMeter.SetValue(_contest.Morale, _contest.MoraleMax);
            }

            if (_resolveMeter != null)
            {
                _resolveMeter.SetValue(_contest.EnemyResolve, _contest.EnemyResolveMax);
            }

            PaintCards();
        }

        void PaintCards()
        {
            bool awaiting = _contest != null && _contest.IsAwaitingMove;

            for (int i = 0; i < _cards.Count; i++)
            {
                MoveCard card = _cards[i];
                if (card == null || card.Root == null)
                {
                    continue;
                }

                bool available = _contest != null && _contest.IsMoveAvailable(card.Id);
                if (card.Root.activeSelf != available)
                {
                    card.Root.SetActive(available);
                }

                if (!available)
                {
                    continue;
                }

                if (card.Button != null)
                {
                    card.Button.interactable = awaiting;
                }

                if (card.Description != null && _contest != null)
                {
                    string description = _contest.DescriptionFor(card.Id);
                    if (card.Description.text != description)
                    {
                        card.Description.text = description;
                    }
                }

                if (!card.Announced)
                {
                    card.Announced = true;
                    Announce(card);
                }
            }
        }

        /// <summary>
        /// What happens the first time a move is offered.
        ///
        /// Only the move the page unlocked gets anything: it is moved to the head of the menu and
        /// the list is scrolled back to the top, so the strongest move on the board is under the
        /// thumb rather than three tall cards further down where nobody would find it. Then one
        /// scale bump, at the reward duration, and nothing else — no badge, no banner, no
        /// explanation, and no sound. The reward is that the move exists.
        /// </summary>
        void Announce(MoveCard card)
        {
            if (card.Variant != UIKit.ButtonVariant.Quest || card.Root == null)
            {
                return;
            }

            card.Root.transform.SetAsFirstSibling();

            // One frame later: the card was activated this frame, so the content column has not
            // been remeasured yet and a scroll position set now would be clamped against a height
            // that does not include it.
            UIMotion.NextFrame(this, () =>
            {
                if (_menuScroll != null)
                {
                    _menuScroll.verticalNormalizedPosition = 1f;
                }
            });

            UIMotion.Pulse((RectTransform)card.Root.transform, RewardPeakScale, DesignTokens.Motion.Reward);
        }

        void PaintLog()
        {
            if (_logText == null)
            {
                return;
            }

            var builder = new StringBuilder();
            for (int i = 0; i < _log.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append("\n\n");
                }

                builder.Append(_log[i]);
            }

            _logText.text = builder.ToString();
        }
    }
}
