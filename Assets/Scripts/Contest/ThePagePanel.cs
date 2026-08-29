using System;
using System.Collections;
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
    /// a toast: the fight stops, the screen goes quiet, and one passage comes up on its own card
    /// with its reference in the footer, in the same type size as any other metadata.
    ///
    /// Two things happen at the same instant, and the order matters. The player finds out what
    /// this text is, and the text becomes the strongest move on the menu. Nothing announces
    /// either. The reference has been visible since the first bubble of day one; here it is
    /// simply impossible to miss.
    ///
    /// Scripture is never written in this file. Only the reference travels, and
    /// <see cref="ScriptureService"/> resolves it against the generated verses.json.
    ///
    /// Leaving is free. "Pular" is on screen from the first frame and costs the player nothing:
    /// closing the page for real is what scores, skipping it just does not.
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

        const float SlideSeconds = 0.45f;
        const float SlideDistance = 260f;

        const float SideMargin = 56f;
        const float CardPadding = 44f;

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

            Image card = UIKit.CreatePanel(container, "PageCard", UIKit.Palette.Parchment, UiSpriteKeys.Panel);
            _card = (RectTransform)card.transform;
            _card.anchorMin = new Vector2(0f, 0.5f);
            _card.anchorMax = new Vector2(1f, 0.5f);
            _card.pivot = new Vector2(0.5f, 0.5f);
            _card.offsetMin = new Vector2(SideMargin, _card.offsetMin.y);
            _card.offsetMax = new Vector2(-SideMargin, _card.offsetMax.y);

            UIKit.VerticalGroup(
                card.gameObject,
                28f,
                new RectOffset((int)CardPadding, (int)CardPadding, (int)CardPadding, (int)CardPadding));

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
                UIKit.FontSize.Meta,
                UIKit.Palette.Stone,
                TextAnchor.UpperLeft);

            Text body = UIKit.CreateText(
                _card,
                "Verse",
                verse != null ? verse.text : string.Empty,
                UIKit.FontSize.Body,
                UIKit.Palette.Ink,
                TextAnchor.UpperLeft);
            body.fontStyle = FontStyle.Italic;
            body.lineSpacing = 1.15f;

            BuildFooter(verse);

            _closeButton = UIKit.CreateButton(
                _card,
                "CloseButton",
                Loc.T("page.close"),
                UIKit.Palette.Clay,
                UIKit.Palette.Parchment,
                Close);
            LayoutElement closeLayout = UIKit.Layout(_closeButton);
            if (closeLayout != null)
            {
                closeLayout.preferredHeight = 116f;
                closeLayout.minHeight = 116f;
            }

            _skipButton = UIKit.CreateButton(
                container,
                "SkipButton",
                Loc.T("page.skip"),
                new Color(0f, 0f, 0f, 0f),
                UIKit.Palette.Muted,
                Skip);
            UIKit.AnchorCorner(
                (RectTransform)_skipButton.transform,
                new Vector2(1f, 1f),
                new Vector2(200f, 80f),
                new Vector2(40f, 40f));

            Report(turn, verse);
            StartCoroutine(SlideIn());
        }

        void BuildFooter(VerseEntry verse)
        {
            RectTransform footer = UIKit.CreateRect("Footer", _card);
            UIKit.HorizontalGroup(footer.gameObject, 18f, new RectOffset(0, 0, 6, 0), TextAnchor.MiddleLeft);

            LayoutElement footerLayout = UIKit.Layout(footer);
            if (footerLayout != null)
            {
                footerLayout.minHeight = 72f;
                footerLayout.preferredHeight = 72f;
            }

            Text reference = UIKit.CreateText(
                footer,
                "Reference",
                BuildReferenceLabel(verse),
                UIKit.FontSize.Meta,
                UIKit.Palette.Stone,
                TextAnchor.MiddleLeft);
            LayoutElement referenceLayout = UIKit.Layout(reference);
            if (referenceLayout != null)
            {
                referenceLayout.flexibleWidth = 1f;
                referenceLayout.minHeight = 60f;
            }

            _readButton = UIKit.CreateButton(
                footer,
                "ReadButton",
                Loc.T("page.read_more"),
                UIKit.Palette.PanelSoft,
                UIKit.Palette.Parchment,
                OpenChapter);
            LayoutElement readLayout = UIKit.Layout(_readButton);
            if (readLayout != null)
            {
                readLayout.preferredWidth = 240f;
                readLayout.minWidth = 240f;
                readLayout.preferredHeight = 68f;
                readLayout.minHeight = 68f;
                readLayout.flexibleWidth = 0f;
            }

            Text readLabel = _readButton.GetComponentInChildren<Text>();
            if (readLabel != null)
            {
                readLabel.fontSize = UIKit.FontSize.Meta;
            }
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

        IEnumerator SlideIn()
        {
            if (_card == null)
            {
                yield break;
            }

            Vector2 target = _card.anchoredPosition;
            Vector2 start = target + new Vector2(0f, -SlideDistance);
            _card.anchoredPosition = start;

            float elapsed = 0f;
            while (elapsed < SlideSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / SlideSeconds);
                float eased = 1f - (1f - t) * (1f - t);

                if (_card != null)
                {
                    _card.anchoredPosition = Vector2.Lerp(start, target, eased);
                }

                if (_group != null)
                {
                    _group.alpha = eased;
                }

                yield return null;
            }

            if (_card != null)
            {
                _card.anchoredPosition = target;
            }

            if (_group != null)
            {
                _group.alpha = 1f;
                _group.interactable = true;
            }
        }

        // ------------------------------------------------------------------ reporting

        void Report(int turn, VerseEntry verse)
        {
            GameState state = ResolveState();
            if (state != null)
            {
                state.SetFlag(GameFlags.PageShown);
            }

            // The page is where the game stops being coy: from here every quotation carries

            // its reference.

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
