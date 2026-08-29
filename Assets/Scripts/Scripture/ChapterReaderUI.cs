using System;
using System.Collections.Generic;
using SheepGate.Core;
using SheepGate.UI;
using SheepGate.Vocation;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.Scripture
{
    /// <summary>
    /// The in-app chapter reader. This is the single screen the whole POC exists to measure: the
    /// question is whether a player opens a chapter and actually reads it, and the answer is the
    /// deep_read event this component emits.
    ///
    /// Two conditions, both required, both tracked honestly:
    ///   - 20 seconds of accumulated *visible* time (the clock stops when the app loses focus);
    ///   - 60% of the chapter brought into view (measured from real content and viewport heights).
    /// The event fires at most once per chapter, deduplicated within the session by a static set
    /// and across sessions by a counter written into the save.
    ///
    /// Nothing here is ever gated: the close button works from the first frame. A metric collected
    /// from a player who could not leave would not be worth collecting.
    ///
    /// No scripture literal exists in this file. Text is resolved from the generated verses.json
    /// through <see cref="ScriptureService"/>, which is the only component allowed to hold it.
    /// </summary>
    public sealed class ChapterReaderUI : MonoBehaviour
    {
        /// <summary>Id this screen occupies in the modal stack.</summary>
        public const string ModalId = "chapter_reader";

        /// <summary>Accumulated visible seconds required before deep_read may fire.</summary>
        public const float DeepReadSeconds = 20f;

        /// <summary>Fraction of the chapter that must have been brought into view.</summary>
        public const float DeepReadScrollFraction = 0.60f;

        /// <summary>Telemetry trigger used when the caller does not name one.</summary>
        public const string DefaultTrigger = "player";

        /// <summary>Counter key prefix that makes deep_read once-per-chapter across sessions.</summary>
        public const string DeepReadCounterPrefix = "deep_read_";

        const string ScribeVocationId = "escriba";
        const int ScribePoints = 2;

        const float PanelInsetX = 20f;
        const float PanelInsetTop = 48f;
        const float PanelInsetBottom = 36f;
        const float HeaderHeight = 132f;
        const float FooterHeight = 96f;
        const float VerseNumberColumnWidth = 76f;

        // Chapters whose deep_read already fired in this process. Cheap protection against a
        // reader that is closed and reopened before the save is read again.
        static readonly HashSet<string> FiredChapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static ChapterReaderUI _current;

        string _chapterKey = string.Empty;
        string _trigger = DefaultTrigger;

        /// <summary>True when the game opened the reader rather than the player choosing to.</summary>
        bool _gameAsked;
        bool _placeholderBuild;

        ScrollRect _scroll;
        RectTransform _content;
        RectTransform _viewport;

        float _visibleSeconds;
        float _maxScrollFraction;
        bool _deepReadFired;
        bool _visible = true;
        bool _closed;
        bool _built;

        /// <summary>Raised once when the reader goes away, whatever closed it.</summary>
        public event Action Closed;

        /// <summary>True while a reader is on screen.</summary>
        public static bool IsOpen
        {
            get { return _current != null && !_current._closed; }
        }

        /// <summary>The reader currently on screen, or null.</summary>
        public static ChapterReaderUI Current
        {
            get { return _current != null && !_current._closed ? _current : null; }
        }

        /// <summary>Chapter reference this reader is showing, for example NEH.4.</summary>
        public string ChapterRef
        {
            get { return _chapterKey; }
        }

        // ------------------------------------------------------------------ opening

        /// <summary>Opens the reader on a chapter reference such as NEH.4.</summary>
        public static ChapterReaderUI Open(string chapterRef)
        {
            return Open(chapterRef, DefaultTrigger);
        }

        /// <summary>
        /// Opens the reader and records how the player got here. <paramref name="trigger"/> is what
        /// separates a player-initiated open from one the game prompted, which is the difference
        /// the deep-read metric lives or dies on.
        /// </summary>
        public static ChapterReaderUI Open(string chapterRef, string trigger)
        {
            return Open(chapterRef, trigger, false);
        }

        /// <summary>
        /// <paramref name="gameAsked"/> distinguishes a reader the game opened from one the player
        /// chose to open. Only the second raises unprompted_read, because only the second is
        /// evidence of desire. Reading is never required and never rewarded with a number
        /// (AGENTS.md rules 19 and 20), so this flag is the measuring instrument.
        /// </summary>
        public static ChapterReaderUI Open(string chapterRef, string trigger, bool gameAsked)
        {
            string key = Normalize(chapterRef);
            if (key.Length == 0)
            {
                Debug.LogError("[ChapterReaderUI] Open was called without a chapter reference.");
                return null;
            }

            if (_current != null && !_current._closed)
            {
                if (string.Equals(_current._chapterKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    // Same chapter, already open: do not stack a second reader and do not count a
                    // second open.
                    return _current;
                }

                _current.Close();
            }

            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[ChapterReaderUI] No modal root is available; the reader cannot open.");
                return null;
            }

            RectTransform container = root.Push(ModalId);
            if (container == null)
            {
                return null;
            }

            var reader = container.gameObject.AddComponent<ChapterReaderUI>();
            reader.Build(key, string.IsNullOrEmpty(trigger) ? DefaultTrigger : trigger, gameAsked);
            _current = reader;
            return reader;
        }

        /// <summary>Closes the reader. Always available, never delayed.</summary>
        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            ModalRoot.CloseId(ModalId);
        }

        // ------------------------------------------------------------------ construction

        void Build(string chapterKey, string trigger, bool gameAsked)
        {
            if (_built)
            {
                return;
            }

            _built = true;
            _chapterKey = chapterKey;
            _trigger = trigger;
            _gameAsked = gameAsked;

            ChapterEntry chapter = ScriptureService.GetChapter(chapterKey);
            _placeholderBuild = SafeIsPlaceholderBuild();

            GameState state = TryGetState();
            _deepReadFired = FiredChapters.Contains(chapterKey) ||
                             (state != null && state.Counter(DeepReadCounterPrefix + chapterKey) > 0);

            BuildLayout(chapter);

            ScoreFirstOpen(state);
            TrackChapterOpened();

            // Measure from a settled layout: content height read on the frame it was built would
            // otherwise be zero and make the scroll fraction meaningless.
            UIKit.RebuildNow(_content);

            // Set again now that the content has a real height: a scroll position assigned before
            // the fitter ran does not survive it.
            if (_scroll != null)
            {
                _scroll.verticalNormalizedPosition = 1f;
            }

            _maxScrollFraction = CurrentScrollFraction();
        }

        void BuildLayout(ChapterEntry chapter)
        {
            var container = (RectTransform)transform;

            Image sheet = UIKit.CreatePanel(container, "ReaderSheet", UIKit.Palette.Panel);
            var sheetRect = (RectTransform)sheet.transform;
            UIKit.Stretch(sheetRect, PanelInsetX, PanelInsetX, PanelInsetTop, PanelInsetBottom);

            // ---- header
            RectTransform header = UIKit.CreateRect("Header", sheetRect);
            UIKit.AnchorTop(header, HeaderHeight, 24f, 24f, 16f);

            string title = chapter != null && !string.IsNullOrEmpty(chapter.ref_display)
                ? chapter.ref_display
                : _chapterKey;

            Text titleText = UIKit.CreateText(header, "Title", title, UIKit.FontSize.Title, UIKit.Palette.Parchment, TextAnchor.MiddleLeft);
            UIKit.Stretch((RectTransform)titleText.transform, 8f, 236f, 0f, 0f);

            Button close = UIKit.CreateButton(header, "Close", Loc.T("reader.close"), UIKit.Palette.PanelSoft, UIKit.Palette.Parchment, Close);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(212f, 96f);
            closeRect.anchoredPosition = new Vector2(-8f, 0f);

            Image rule = UIKit.CreatePanel(sheetRect, "HeaderRule", UIKit.Palette.Stone);
            var ruleRect = (RectTransform)rule.transform;
            UIKit.AnchorTop(ruleRect, 3f, 24f, 24f, 16f + HeaderHeight);
            rule.raycastTarget = false;

            // ---- footer
            RectTransform footer = UIKit.CreateRect("Footer", sheetRect);
            UIKit.AnchorBottom(footer, FooterHeight, 24f, 24f, 12f);

            Text meta = UIKit.CreateText(footer, "Meta", BuildMetaLine(chapter), UIKit.FontSize.Meta, UIKit.Palette.Muted, TextAnchor.LowerLeft);
            UIKit.Stretch((RectTransform)meta.transform, 8f, 8f, 4f, 4f);

            // ---- chapter body
            RectTransform content;
            _scroll = UIKit.CreateScrollView(sheetRect, "Chapter", out content);
            _content = content;
            _viewport = _scroll.viewport;

            var scrollRect = (RectTransform)_scroll.transform;
            UIKit.Stretch(
                scrollRect,
                16f,
                16f,
                16f + HeaderHeight + 14f,
                12f + FooterHeight + 10f);

            UIKit.AttachVerticalScrollbar(_scroll, 10f);

            BuildVerses(chapter);

            // Start at the top of the chapter.
            _scroll.verticalNormalizedPosition = 1f;
        }

        void BuildVerses(ChapterEntry chapter)
        {
            ChapterVerse[] verses = chapter != null ? chapter.verses : null;
            if (verses == null || verses.Length == 0)
            {
                Text empty = UIKit.CreateText(_content, "Empty", Loc.T("reader.empty"),
                    UIKit.FontSize.Body, UIKit.Palette.Muted, TextAnchor.UpperLeft);
                UIKit.Layout(empty).flexibleWidth = 1f;
                return;
            }

            for (int i = 0; i < verses.Length; i++)
            {
                ChapterVerse verse = verses[i];
                if (verse == null)
                {
                    continue;
                }

                RectTransform row = UIKit.CreateRect("Verse_" + verse.n, _content);
                UIKit.HorizontalGroup(row.gameObject, 16f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);

                Text number = UIKit.CreateText(row, "Number", verse.n.ToString(), UIKit.FontSize.Meta, UIKit.Palette.Muted, TextAnchor.UpperRight);
                LayoutElement numberLayout = UIKit.Layout(number);
                numberLayout.preferredWidth = VerseNumberColumnWidth;
                numberLayout.minWidth = VerseNumberColumnWidth;
                numberLayout.flexibleWidth = 0f;

                Text body = UIKit.CreateText(row, "Body", verse.text ?? string.Empty, UIKit.FontSize.Body, UIKit.Palette.Parchment, TextAnchor.UpperLeft);
                body.lineSpacing = 1.15f;
                LayoutElement bodyLayout = UIKit.Layout(body);
                bodyLayout.flexibleWidth = 1f;
            }
        }

        string BuildMetaLine(ChapterEntry chapter)
        {
            string reference = chapter != null && !string.IsNullOrEmpty(chapter.ref_display)
                ? chapter.ref_display
                : _chapterKey;

            VersionInfo version = null;
            try
            {
                version = ScriptureService.Version;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[ChapterReaderUI] Could not read the translation metadata: " + exception.Message);
            }

            string line = reference;

            if (version != null && !string.IsNullOrEmpty(version.abbrev))
            {
                line += "  ·  " + version.abbrev;
            }

            if (version != null && !string.IsNullOrEmpty(version.copyright))
            {
                line += "\n" + version.copyright;
            }

            return line;
        }

        // ------------------------------------------------------------------ measurement

        void Update()
        {
            if (_closed)
            {
                return;
            }

            if (_visible)
            {
                _visibleSeconds += Time.unscaledDeltaTime;
            }

            float fraction = CurrentScrollFraction();
            if (fraction > _maxScrollFraction)
            {
                _maxScrollFraction = fraction;
            }

            if (!_deepReadFired &&
                _visibleSeconds >= DeepReadSeconds &&
                _maxScrollFraction >= DeepReadScrollFraction)
            {
                FireDeepRead();
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            _visible = hasFocus;
        }

        void OnApplicationPause(bool paused)
        {
            _visible = !paused;
        }

        /// <summary>
        /// How much of the chapter has been brought into view, from 0 to 1. This measures what the
        /// player has actually had on screen — scroll offset plus one viewport — rather than how
        /// far the scrollbar travelled, which would report 0% while a third of a short chapter is
        /// already visible.
        /// </summary>
        float CurrentScrollFraction()
        {
            if (_scroll == null || _content == null || _viewport == null)
            {
                return 0f;
            }

            float contentHeight = _content.rect.height;
            float viewportHeight = _viewport.rect.height;

            // Before the first layout pass both are zero. Reporting 1 here would hand out a
            // deep_read for a chapter nobody has seen.
            if (contentHeight <= 0f || viewportHeight <= 0f)
            {
                return 0f;
            }

            if (contentHeight <= viewportHeight + 1f)
            {
                // The whole chapter fits on screen; there is nothing left to scroll to.
                return 1f;
            }

            float scrollable = contentHeight - viewportHeight;
            float normalized = Mathf.Clamp01(_scroll.verticalNormalizedPosition);
            float offsetFromTop = (1f - normalized) * scrollable;
            return Mathf.Clamp01((offsetFromTop + viewportHeight) / contentHeight);
        }

        void FireDeepRead()
        {
            _deepReadFired = true;
            FiredChapters.Add(_chapterKey);

            int scrollPercent = Mathf.Clamp(Mathf.RoundToInt(_maxScrollFraction * 100f), 0, 100);
            float seconds = Mathf.Round(_visibleSeconds * 10f) / 10f;

            GameState state = TryGetState();
            if (state != null)
            {
                state.SetFlag(GameFlags.DeepRead);
                state.Bump(DeepReadCounterPrefix + _chapterKey);

                // Persist immediately so a chapter cannot earn a second deep_read after a restart.
                SaveSystem.Save(state);
            }

            Telemetry.Track(TelemetryEvents.DeepRead, new Dictionary<string, object>
            {
                { "ref", _chapterKey },
                { "seconds", seconds },
                { "scroll_pct", scrollPercent },
                { "placeholder", _placeholderBuild }
            });
            Telemetry.Flush();
        }

        void TrackChapterOpened()
        {
            Telemetry.Track(TelemetryEvents.ChapterOpened, new Dictionary<string, object>
            {
                { "ref", _chapterKey },
                { "trigger", _trigger },
                { "placeholder", _placeholderBuild }
            });

            // Desire, not compliance. Every reader entry in the POC today is a button the player
            // chose to press and could have ignored, so every open is unprompted; the flag exists
            // so that the day some path opens the reader on the game's initiative, the funnel can
            // still tell the two apart instead of counting them together.
            if (!_gameAsked)
            {
                Telemetry.Track(TelemetryEvents.UnpromptedRead, new Dictionary<string, object>
                {
                    { "ref", _chapterKey },
                    { "trigger", _trigger }
                });
            }
        }

        // ------------------------------------------------------------------ vocation

        /// <summary>
        /// Opening the reader for the first time scores the escriba archetype. The points go
        /// through the tracker and are never read back: no screen in this project may show
        /// vocation progress.
        /// </summary>
        static void ScoreFirstOpen(GameState state)
        {
            if (state == null || state.HasFlag(GameFlags.ChapterOpened))
            {
                return;
            }

            // EnsureRegistered, not a bare registry lookup: nothing registers a tracker during
            // boot, so a plain TryGet would drop the points silently and the reader would score
            // nothing at all.
            VocationTracker tracker = VocationTracker.EnsureRegistered();
            if (tracker != null)
            {
                tracker.Add(ScribeVocationId, ScribePoints);
            }

            state.SetFlag(GameFlags.ChapterOpened);
        }

        // ------------------------------------------------------------------ teardown

        void OnDestroy()
        {
            _closed = true;

            if (_current == this)
            {
                _current = null;
            }

            Action handler = Closed;
            Closed = null;

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
                Debug.LogError("[ChapterReaderUI] A listener threw while the reader closed: " + exception.Message);
            }
        }

        // ------------------------------------------------------------------ helpers

        static GameState TryGetState()
        {
            GameState state;
            return ServiceLocator.TryGet(out state) ? state : null;
        }

        static bool SafeIsPlaceholderBuild()
        {
            try
            {
                return ScriptureService.IsPlaceholderBuild;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[ChapterReaderUI] Could not read the placeholder flag: " + exception.Message);
                return false;
            }
        }

        static string Normalize(string reference)
        {
            return reference == null ? string.Empty : reference.Trim();
        }
    }
}
