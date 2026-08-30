using System;
using System.Collections.Generic;
using SheepGate.Core;
using SheepGate.Player;
using SheepGate.Scripture;
using SheepGate.UI;
using SheepGate.Vocation;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.Contest
{
    /// <summary>
    /// The page. This is the moment the POC exists to test, so it is built as a scene and not as
    /// a toast: the fight stops, the screen goes quiet, and one passage comes up on its own card.
    ///
    /// It is the one screen in the game built on <see cref="UIKit.CardStyle.Scroll"/> — the
    /// pergaminho of section 06, warm and near-white — while everything around it is the dark
    /// stack the rest of day three lives on. That contrast is the whole treatment. The text does
    /// not need a bigger font to carry weight here; it needs to be the only lit thing on screen,
    /// and it is. Ink on it is <c>Ink.OnScroll</c>, never a dark-surface ink, which would vanish.
    ///
    /// Two things happen at the same instant, and the order matters. The player finds out what
    /// this text is, and the text becomes the strongest move on the menu. Nothing announces
    /// either. The reference has been visible since the first bubble of day one; here it is
    /// simply impossible to miss.
    ///
    /// Scripture is never written in this file. Only the reference travels, and
    /// <see cref="ScriptureService"/> resolves it against the generated verses.json.
    ///
    /// Leaving is free. "Pular" is on screen from the first frame, outside the card and outside
    /// the card's canvas group, so it is tappable while the card is still arriving: closing the
    /// page for real is what scores, skipping it just does not.
    /// </summary>
    public sealed class ThePagePanel : MonoBehaviour
    {
        public const string ModalId = "the_page";

        /// <summary>The passage behind the strongest move. Reference only, resolved at runtime.</summary>
        public const string VerseRef = "NEH.4.17";

        /// <summary>Telemetry context for the exposure this panel produces.</summary>
        public const string TelemetryContext = "the_page";

        const int ProphetPoints = 3;
        const string ProphetCounter = "prophet_page_awarded";

        // ------------------------------------------------------------------ layout

        /// <summary>Left and right margin of the card, matching every other screen's gutter.</summary>
        static readonly float SideMargin = DesignTokens.Space.Gutter;

        /// <summary>
        /// Padding inside the card.
        ///
        /// The pergaminho frame is drawn with a soft elevated edge rather than a border, and its
        /// visible body is inset by <c>UiArt.ScrollHalo</c> on every side. Spending the halo here
        /// is what keeps the first word of the passage a full S20 from the paper's edge instead of
        /// that much less. Fully qualified rather than imported: the art module publishes a
        /// <c>UiSpriteKeys</c> of its own, and a using directive for it would make every
        /// unqualified mention of that name in this file ambiguous.
        /// </summary>
        static readonly float CardPadding = DesignTokens.Space.S20 + SheepGate.Art.UiArt.ScrollHalo;

        /// <summary>Gap between the eyebrow, the passage, the footer and the way out.</summary>
        static readonly float CardSpacing = DesignTokens.Space.S20;

        /// <summary>How far the card travels on its way in. Decoration, so reduced motion drops it.</summary>
        static readonly float EntranceRise = DesignTokens.Space.S32;

        /// <summary>
        /// Room held for "Saber mais". Wide enough for the label at <c>Type.Body</c> plus the
        /// button's own padding in both languages, so the one control that leads to the chapter
        /// never wraps its own words.
        /// </summary>
        static readonly float ReadButtonWidth = DesignTokens.Px(132f);

        /// <summary>"Pular" plus its padding, on a full touch target. Never below 48 either way.</summary>
        static readonly Vector2 SkipSize = new Vector2(DesignTokens.Px(96f), DesignTokens.Space.TouchTarget);

        static ThePagePanel _current;

        Action _onClosed;
        RectTransform _card;
        CanvasGroup _group;
        Button _closeButton;
        Button _skipButton;
        Button _readButton;

        bool _notified;
        bool _closing;
        bool _lockHeld;

        /// <summary>True while the page is on screen.</summary>
        public static bool IsOpen
        {
            get { return _current != null; }
        }

        /// <summary>
        /// Shows the page for the given turn and calls back once it is gone, whichever way it
        /// went away. Returns null only when there is no modal root to build into, which is the
        /// one case the contest has to survive on its own.
        /// </summary>
        public static ThePagePanel Show(int turn, Action onClosed)
        {
            if (_current != null)
            {
                Debug.LogWarning("[ThePage] The page is already on screen.");
                return _current;
            }

            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[ThePage] No modal root is available; the page cannot be shown.");
                return null;
            }

            RectTransform container = root.Push(ModalId);
            if (container == null)
            {
                return null;
            }

            var panel = container.gameObject.AddComponent<ThePagePanel>();
            panel._onClosed = onClosed;
            panel.Build(container, turn);
            _current = panel;
            return panel;
        }

        /// <summary>Closes the page as a read page: this is the one that scores.</summary>
        public void Close()
        {
            Dismiss(false);
        }

        /// <summary>Closes the page as skipped. Costs nothing but the points it does not give.</summary>
        public void Skip()
        {
            Dismiss(true);
        }

        // ------------------------------------------------------------------ construction

        void Build(RectTransform container, int turn)
        {
            InputLock.Push();
            _lockHeld = true;

            VerseEntry verse = ScriptureService.GetVerse(VerseRef);

            Image card = UIKit.CreateCard(container, "PageCard", UIKit.CardStyle.Scroll);
            _card = (RectTransform)card.transform;
            _card.anchorMin = new Vector2(0f, 0.5f);
            _card.anchorMax = new Vector2(1f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);
            _card.offsetMin = new Vector2(SideMargin, _card.offsetMin.y);
            _card.offsetMax = new Vector2(-SideMargin, _card.offsetMax.y);

            UIKit.VerticalGroup(card.gameObject, CardSpacing, Pad(CardPadding));

            // The card is exactly as tall as what is on it. The passage runs from two lines to
            // six depending on the translation the build shipped with, and a fixed height would
            // either clip the long one or leave a slab of empty paper under the short one.
            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _group = card.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;

            UIKit.CreateText(
                _card,
                "Eyebrow",
                Loc.T("page.eyebrow"),
                DesignTokens.Type.Minimum,
                DesignTokens.Ink.OnScrollMuted,
                TextAnchor.UpperLeft,
                DesignTokens.TypeRole.BodyStrong);

            Text body = UIKit.CreateText(
                _card,
                "Verse",
                verse != null ? verse.text : string.Empty,
                DesignTokens.Type.Body,
                DesignTokens.Ink.OnScroll,
                TextAnchor.UpperLeft);

            // Italic marks it as quoted rather than spoken. The leading is left where CreateText
            // put it: the design system's body leading is the same on this card as anywhere else,
            // and a page that set its own would be the one paragraph in the game out of pitch.
            body.fontStyle = FontStyle.Italic;

            BuildFooter(verse);

            _closeButton = UIKit.CreateButton(
                _card,
                "CloseButton",
                Loc.T("page.close"),
                UIKit.ButtonVariant.Primary,
                Close);

            // Outside the card, so the card's canvas group cannot make it wait for the entrance,
            // and Ghost because it is the option that costs the player nothing to ignore — which
            // is the whole promise this panel makes about skipping.
            _skipButton = UIKit.CreateButton(
                container,
                "SkipButton",
                Loc.T("page.skip"),
                UIKit.ButtonVariant.Ghost,
                Skip);
            UIKit.AnchorCorner(
                (RectTransform)_skipButton.transform,
                new Vector2(1f, 1f),
                SkipSize,
                new Vector2(DesignTokens.Space.Gutter, DesignTokens.Space.S16));

            Report(turn, verse);
            PlayEntrance();
        }

        void BuildFooter(VerseEntry verse)
        {
            RectTransform footer = UIKit.CreateRect("Footer", _card);
            UIKit.HorizontalGroup(footer.gameObject, DesignTokens.Space.S12, new RectOffset(), TextAnchor.MiddleLeft);

            // No height on the row: the button carries the touch target and the reference is
            // allowed to take a second line when a translation's abbreviation is long, which the
            // English build's is. A fixed height would have cropped the second line silently.

            Text reference = UIKit.CreateText(
                footer,
                "Reference",
                BuildReferenceLabel(verse),
                DesignTokens.Type.Mono,
                DesignTokens.Ink.OnScrollMuted,
                TextAnchor.MiddleLeft,
                DesignTokens.TypeRole.Mono);
            LayoutElement referenceLayout = UIKit.Layout(reference);
            if (referenceLayout != null)
            {
                referenceLayout.flexibleWidth = 1f;
            }

            // Gold, and the only gold on this screen. The design system spends that accent on the
            // call to action that opens something new, and the only thing this page opens is the
            // whole chapter — which is the number the entire product is measured by. It is also
            // never on screen at the same time as the contest's own gold card: the move behind
            // this page does not become available until the page has closed.
            _readButton = UIKit.CreateButton(
                footer,
                "ReadButton",
                Loc.T("page.read_more"),
                UIKit.ButtonVariant.Quest,
                OpenChapter);
            LayoutElement readLayout = UIKit.Layout(_readButton);
            if (readLayout != null)
            {
                readLayout.minWidth = ReadButtonWidth;
                readLayout.preferredWidth = ReadButtonWidth;
                readLayout.flexibleWidth = 0f;
            }
        }

        static RectOffset Pad(float uniform)
        {
            int p = Mathf.RoundToInt(uniform);
            return new RectOffset(p, p, p, p);
        }

        /// <summary>The reference, plus the translation abbreviation when the build has one.</summary>
        static string BuildReferenceLabel(VerseEntry verse)
        {
            string label = verse != null && !string.IsNullOrEmpty(verse.ref_display)
                ? verse.ref_display
                : VerseRef;

            try
            {
                VersionInfo version = ScriptureService.Version;
                if (version != null && !string.IsNullOrEmpty(version.abbrev))
                {
                    label += " · " + version.abbrev;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[ThePage] Could not read the scripture version: " + exception.Message);
            }

            return label;
        }

        /// <summary>
        /// The card arrives: a fade, and a short rise under it.
        ///
        /// The two halves are deliberately different kinds of motion. The fade carries the news
        /// that the page is here, so it runs whatever the accessibility setting says — a panel
        /// that appeared with no transition at all would read as a glitch rather than as calm. The
        /// rise is decoration, so reduced motion drops it, and dropping it is safe because a
        /// suppressed decorative tween applies its own end state instead of leaving the card
        /// stranded off-centre.
        /// </summary>
        void PlayEntrance()
        {
            if (_card == null)
            {
                return;
            }

            Vector2 target = _card.anchoredPosition;
            Vector2 start = target - new Vector2(0f, EntranceRise);
            _card.anchoredPosition = start;

            UIMotion.Fade(_group, 1f, DesignTokens.Motion.Reward, () =>
            {
                if (_group != null)
                {
                    _group.alpha = 1f;
                    _group.interactable = true;
                }
            });

            UIMotion.Decorative(_card, DesignTokens.Motion.Reward, progress =>
            {
                if (_card != null)
                {
                    _card.anchoredPosition = Vector2.Lerp(start, target, progress);
                }
            });
        }

        // ------------------------------------------------------------------ reporting

        void Report(int turn, VerseEntry verse)
        {
            GameState state = ResolveState();
            if (state != null)
            {
                state.SetFlag(GameFlags.PageShown);
            }

            // The page is where the game stops being coy: from here every quotation carries its
            // reference.
            ScriptureVisibility.Reveal(WorldStateForReveal());

            Telemetry.Track(TelemetryEvents.RevealShown, new Dictionary<string, object>
            {
                { "turn", turn }
            });

            // The page is the loudest verse exposure in the POC; leaving it out would put a hole
            // in the funnel deep_read is measured against.
            Telemetry.Track(TelemetryEvents.VerseShown, new Dictionary<string, object>
            {
                { "ref", VerseRef },
                { "context", TelemetryContext }
            });

            if (verse == null || string.IsNullOrEmpty(verse.text))
            {
                Debug.LogWarning("[ThePage] " + VerseRef + " has no text in verses.json; the panel shows the missing marker.");
            }
        }

        void OpenChapter()
        {
            string chapterRef = ScriptureService.ChapterRefOf(VerseRef);
            if (string.IsNullOrEmpty(chapterRef))
            {
                return;
            }

            // The reader owns its own telemetry and its own scoring; this only hands it the id.
            ChapterReaderUI.Open(chapterRef, TelemetryContext);
        }

        // ------------------------------------------------------------------ closing

        void Dismiss(bool skipped)
        {
            if (_closing)
            {
                return;
            }

            _closing = true;

            // The card is on its way out; a second tap must not choose the other ending.
            SetInteractable(_closeButton, false);
            SetInteractable(_skipButton, false);
            SetInteractable(_readButton, false);

            GameState state = ResolveState();
            if (state != null)
            {
                if (skipped)
                {
                    state.SetFlag(GameFlags.PageSkipped);
                }
                else
                {
                    AwardProphetOnce(state);
                }
            }

            ModalRoot.CloseId(ModalId);
        }

        static void SetInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        static void AwardProphetOnce(GameState state)
        {
            if (state.Counter(ProphetCounter) != 0)
            {
                return;
            }

            state.Bump(ProphetCounter);

            VocationTracker tracker = VocationTracker.EnsureRegistered();
            if (tracker != null)
            {
                tracker.Add(VocationIds.Prophet, ProphetPoints);
            }
        }

        void OnDestroy()
        {
            if (_current == this)
            {
                _current = null;
            }

            if (_lockHeld)
            {
                _lockHeld = false;
                InputLock.Pop();
            }

            NotifyClosed();
        }

        /// <summary>
        /// The callback fires exactly once and no matter how the panel went away, including a
        /// hardware back press that destroys the container from under it. The contest is waiting
        /// on this, and a fight that never resumes would be worse than any of the endings.
        /// </summary>
        void NotifyClosed()
        {
            if (_notified)
            {
                return;
            }

            _notified = true;

            Action handler = _onClosed;
            _onClosed = null;
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
                Debug.LogError("[ThePage] A listener threw while the page closed: " + exception.Message);
            }
        }

        static GameState ResolveState()
        {
            GameState state;
            try
            {
                if (ServiceLocator.TryGet(out state) && state != null)
                {
                    return state;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[ThePage] Could not read the game state: " + exception.Message);
            }

            return null;
        }

        /// <summary>The run's state, for turning citations on when the page appears.</summary>
        static GameState WorldStateForReveal()
        {
            GameState state;
            return ServiceLocator.TryGet(out state) ? state : null;
        }
    }
}
