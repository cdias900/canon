using System;
using System.Collections;
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
    /// The menu states what each move does in plain words, because the point of the day is that
    /// the player recognises the strongest move when it arrives rather than discovering it by
    /// trial and error.
    /// </summary>
    public sealed class ContestUI : MonoBehaviour
    {
        public const string ModalId = "contest";

        const int LogEntries = 4;
        const float HighlightPulseSeconds = 2.4f;

        static ContestUI _instance;

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
        Meter _moraleMeter;
        Meter _resolveMeter;
        Text _logText;
        RectTransform _menuContent;
        RectTransform _outcomePanel;
        Text _outcomeTitle;
        Text _outcomeBody;

        readonly List<string> _log = new List<string>();
        readonly List<MoveCard> _cards = new List<MoveCard>();

        bool _visible;
        bool _lockHeld;
        bool _highlightPlayed;

        /// <summary>One card in the move menu. Kept so a repaint updates instead of rebuilding.</summary>
        sealed class MoveCard
        {
            public string Id;
            public GameObject Root;
            public Button Button;
            public Image Background;
            public Text Title;
            public Text Description;
            public bool Highlighted;
        }

        /// <summary>Label plus bar. Never called a health bar, and never used as one.</summary>
        sealed class Meter
        {
            public Text Value;
            public RectTransform Fill;

            public void Set(int value, int max)
            {
                if (Value != null)
                {
                    Value.text = Mathf.Max(0, value) + " / " + Mathf.Max(1, max);
                }

                if (Fill != null)
                {
                    float fraction = max > 0 ? Mathf.Clamp01(value / (float)max) : 0f;
                    Fill.anchorMin = new Vector2(0f, 0f);
                    Fill.anchorMax = new Vector2(fraction, 1f);
                    Fill.offsetMin = Vector2.zero;
                    Fill.offsetMax = Vector2.zero;
                }
            }
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
            _highlightPlayed = false;

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
            RectTransform header = UIKit.CreateRect("Header", container);
            UIKit.AnchorTop(header, 320f, 48f, 48f, 64f);
            UIKit.VerticalGroup(header.gameObject, 18f, new RectOffset(0, 0, 0, 0));

            RectTransform titleRow = UIKit.CreateRect("TitleRow", header);
            UIKit.HorizontalGroup(titleRow.gameObject, 12f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft);
            LayoutElement titleRowLayout = UIKit.Layout(titleRow);
            if (titleRowLayout != null)
            {
                titleRowLayout.minHeight = 44f;
                titleRowLayout.preferredHeight = 44f;
            }

            Text title = UIKit.CreateText(
                titleRow,
                "Title",
                "A INVESTIDA",
                UIKit.FontSize.Meta,
                UIKit.Palette.Muted,
                TextAnchor.MiddleLeft);
            LayoutElement titleLayout = UIKit.Layout(title);
            if (titleLayout != null)
            {
                titleLayout.flexibleWidth = 1f;
                titleLayout.minHeight = 44f;
            }

            _turnText = UIKit.CreateText(
                titleRow,
                "Turn",
                string.Empty,
                UIKit.FontSize.Meta,
                UIKit.Palette.Muted,
                TextAnchor.MiddleRight);
            LayoutElement turnLayout = UIKit.Layout(_turnText);
            if (turnLayout != null)
            {
                turnLayout.preferredWidth = 320f;
                turnLayout.minHeight = 44f;
            }

            _moraleMeter = BuildMeter(header, "MoraleMeter", "ÂNIMO", UIKit.Palette.Olive);
            _resolveMeter = BuildMeter(header, "ResolveMeter", "FIRMEZA DO OUTRO LADO", UIKit.Palette.Clay);
        }

        Meter BuildMeter(RectTransform parent, string name, string label, Color fillColor)
        {
            RectTransform root = UIKit.CreateRect(name, parent);
            UIKit.VerticalGroup(root.gameObject, 8f, new RectOffset(0, 0, 0, 0));
            LayoutElement rootLayout = UIKit.Layout(root);
            if (rootLayout != null)
            {
                rootLayout.minHeight = 96f;
                rootLayout.preferredHeight = 96f;
            }

            RectTransform labelRow = UIKit.CreateRect("LabelRow", root);
            UIKit.HorizontalGroup(labelRow.gameObject, 12f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft);
            LayoutElement labelRowLayout = UIKit.Layout(labelRow);
            if (labelRowLayout != null)
            {
                labelRowLayout.minHeight = 40f;
                labelRowLayout.preferredHeight = 40f;
            }

            Text caption = UIKit.CreateText(
                labelRow,
                "Label",
                label,
                UIKit.FontSize.Meta,
                UIKit.Palette.Parchment,
                TextAnchor.MiddleLeft);
            LayoutElement captionLayout = UIKit.Layout(caption);
            if (captionLayout != null)
            {
                captionLayout.flexibleWidth = 1f;
                captionLayout.minHeight = 40f;
            }

            Text value = UIKit.CreateText(
                labelRow,
                "Value",
                string.Empty,
                UIKit.FontSize.Meta,
                UIKit.Palette.Muted,
                TextAnchor.MiddleRight);
            LayoutElement valueLayout = UIKit.Layout(value);
            if (valueLayout != null)
            {
                valueLayout.preferredWidth = 260f;
                valueLayout.minHeight = 40f;
            }

            Image track = UIKit.CreatePanel(root, "Track", UIKit.Palette.PanelSoft, UiSpriteKeys.Panel);
            track.raycastTarget = false;
            LayoutElement trackLayout = UIKit.Layout(track);
            if (trackLayout != null)
            {
                trackLayout.minHeight = 34f;
                trackLayout.preferredHeight = 34f;
            }

            Image fill = UIKit.CreatePanel((RectTransform)track.transform, "Fill", fillColor, UiSpriteKeys.Panel);
            fill.raycastTarget = false;
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            return new Meter { Value = value, Fill = fillRect };
        }

        void BuildLog(RectTransform container)
        {
            Image panel = UIKit.CreatePanel(container, "Log", UIKit.Palette.Panel, UiSpriteKeys.Panel);
            panel.raycastTarget = false;
            UIKit.AnchorTop((RectTransform)panel.transform, 430f, 48f, 48f, 404f);

            RectTransform viewport = UIKit.CreateRect("Viewport", (RectTransform)panel.transform);
            UIKit.Stretch(viewport, 28f, 28f, 24f, 24f);
            viewport.gameObject.AddComponent<RectMask2D>();

            _logText = UIKit.CreateText(
                viewport,
                "Lines",
                string.Empty,
                UIKit.FontSize.Meta,
                UIKit.Palette.Parchment,
                TextAnchor.LowerLeft);
            _logText.lineSpacing = 1.1f;

            var textRect = (RectTransform)_logText.transform;
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 0f);
            textRect.pivot = new Vector2(0.5f, 0f);
            textRect.offsetMin = new Vector2(0f, 0f);
            textRect.offsetMax = new Vector2(0f, 0f);

            var fitter = _logText.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        void BuildMenu(RectTransform container)
        {
            RectTransform menuRect = UIKit.CreateRect("MenuArea", container);
            UIKit.AnchorBottom(menuRect, 1000f, 48f, 48f, 48f);

            RectTransform content;
            ScrollRect scroll = UIKit.CreateScrollView(menuRect, "MenuScroll", out content);
            UIKit.Stretch((RectTransform)scroll.transform);
            _menuContent = content;

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

        MoveCard BuildCard(RectTransform parent, ContestMoveDef move)
        {
            Image background = UIKit.CreatePanel(parent, "Move_" + move.id, UIKit.Palette.PanelSoft, UiSpriteKeys.Button);
            var rect = (RectTransform)background.transform;

            UIKit.VerticalGroup(background.gameObject, 10f, new RectOffset(28, 28, 24, 26));

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            Text title = UIKit.CreateText(
                rect,
                "Title",
                move.display ?? move.id,
                UIKit.FontSize.Button,
                UIKit.Palette.Parchment,
                TextAnchor.UpperLeft);

            Text description = UIKit.CreateText(
                rect,
                "Description",
                move.description ?? string.Empty,
                UIKit.FontSize.Meta,
                UIKit.Palette.Muted,
                TextAnchor.UpperLeft);

            string moveId = move.id;
            button.onClick.AddListener(() => OnMoveClicked(moveId));

            return new MoveCard
            {
                Id = moveId,
                Root = background.gameObject,
                Button = button,
                Background = background,
                Title = title,
                Description = description,
                Highlighted = false
            };
        }

        void BuildOutcome(RectTransform container)
        {
            Image panel = UIKit.CreatePanel(container, "Outcome", UIKit.Palette.Panel, UiSpriteKeys.Panel);
            _outcomePanel = (RectTransform)panel.transform;
            UIKit.AnchorBottom(_outcomePanel, 560f, 48f, 48f, 48f);
            UIKit.VerticalGroup(panel.gameObject, 22f, new RectOffset(36, 36, 36, 36));

            _outcomeTitle = UIKit.CreateText(
                _outcomePanel,
                "Title",
                string.Empty,
                UIKit.FontSize.Heading,
                UIKit.Palette.Parchment,
                TextAnchor.UpperLeft);

            _outcomeBody = UIKit.CreateText(
                _outcomePanel,
                "Body",
                string.Empty,
                UIKit.FontSize.Body,
                UIKit.Palette.Muted,
                TextAnchor.UpperLeft);
            LayoutElement bodyLayout = UIKit.Layout(_outcomeBody);
            if (bodyLayout != null)
            {
                bodyLayout.flexibleHeight = 1f;
            }

            Button continueButton = UIKit.CreateButton(
                _outcomePanel,
                "Continue",
                "Continuar",
                UIKit.Palette.Clay,
                UIKit.Palette.Parchment,
                Hide);
            LayoutElement continueLayout = UIKit.Layout(continueButton);
            if (continueLayout != null)
            {
                continueLayout.minHeight = 116f;
                continueLayout.preferredHeight = 116f;
            }

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

            if (_menuContent != null && _menuContent.parent != null && _menuContent.parent.parent != null)
            {
                _menuContent.parent.parent.gameObject.SetActive(false);
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
                    return "O outro lado desistiu.";

                case ContestOutcome.PlayerBroke:
                    return "Os seus desistiram primeiro.";

                default:
                    return "Ninguém desistiu hoje.";
            }
        }

        static string BodyFor(ContestOutcome outcome)
        {
            switch (outcome)
            {
                case ContestOutcome.EnemyWithdrew:
                    return "Eles vão embora pela mesma estrada por onde vieram. A muralha continua onde estava, e você também.";

                case ContestOutcome.PlayerBroke:
                    return "Eles recuam assim mesmo, tarde. O que estava pela metade no seu trecho voltou a ser pedra solta; o que já estava de pé continua de pé. Amanhã se assenta de novo.";

                default:
                    return "Passou o dia inteiro sem que ninguém cedesse. Eles vão embora cansados, e voltam quando quiserem.";
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
                _turnText.text = "Turno " + turn + " de " + _contest.TurnLimit;
            }

            if (_moraleMeter != null)
            {
                _moraleMeter.Set(_contest.Morale, _contest.MoraleMax);
            }

            if (_resolveMeter != null)
            {
                _resolveMeter.Set(_contest.EnemyResolve, _contest.EnemyResolveMax);
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

                if (!card.Highlighted && card.Id == MoraleContest.MoveHalfAndHalf)
                {
                    card.Highlighted = true;
                    HighlightCard(card);
                }
            }
        }

        /// <summary>
        /// The move the page unlocked arrives already marked: the accent fill the primary buttons
        /// use, plus one slow pulse so the eye lands on it. No badge, no banner, no explanation.
        /// </summary>
        void HighlightCard(MoveCard card)
        {
            if (card.Background != null)
            {
                card.Background.color = UIKit.Palette.Clay;
            }

            if (card.Description != null)
            {
                card.Description.color = UIKit.Palette.Parchment;
            }

            if (!_highlightPlayed && isActiveAndEnabled)
            {
                _highlightPlayed = true;
                StartCoroutine(PulseCard(card));
            }
        }

        IEnumerator PulseCard(MoveCard card)
        {
            Color baseColor = UIKit.Palette.Clay;
            Color peak = Color.Lerp(baseColor, UIKit.Palette.Parchment, 0.35f);

            float elapsed = 0f;
            while (elapsed < HighlightPulseSeconds)
            {
                elapsed += Time.unscaledDeltaTime;

                if (card == null || card.Background == null)
                {
                    yield break;
                }

                float wave = 0.5f + 0.5f * Mathf.Sin(elapsed * 6f);
                card.Background.color = Color.Lerp(baseColor, peak, wave);
                yield return null;
            }

            if (card != null && card.Background != null)
            {
                card.Background.color = baseColor;
            }
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
