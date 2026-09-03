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

        /// <summary>Floor on the visible seconds required before deep_read may fire.</summary>
        public const float DeepReadSeconds = 20f;

        /// <summary>
        /// Seconds of dwell a chapter earns per verse. The floor alone let a chapter that fits on
        /// one screen award the north-star event after twenty seconds of sitting there, because a
        /// content shorter than the viewport reads as fully scrolled. The dwell now grows with the
        /// text: NEH.4 at 23 verses asks about 35 seconds, NEH.12 at 47 asks about 70. Reading pace
        /// at 13 is nearer two seconds a verse, so this is still a floor, not a proof of reading.
        /// Taken as a product decision on 2026-09-03 and revisable in one constant.
        /// </summary>
        public const float DeepReadSecondsPerVerse = 1.5f;

        /// <summary>The dwell a chapter of this many verses asks for.</summary>
        public static float DeepReadSecondsFor(int verseCount)
        {
            return Mathf.Max(DeepReadSeconds, verseCount * DeepReadSecondsPerVerse);
        }

        /// <summary>
        /// Trigger of a reader opened from the season's ending, where the game attached nothing to
        /// the chapter: no move, no page, no record. A deep read there is the one the product
        /// wants most, and it is reported under its own name.
        /// </summary>
        public const string UngamedTrigger = "season_end";

        /// <summary>Fraction of the chapter that must have been brought into view.</summary>
        public const float DeepReadScrollFraction = 0.60f;

        /// <summary>Telemetry trigger used when the caller does not name one.</summary>
        public const string DefaultTrigger = "player";

        /// <summary>Counter key prefix that makes deep_read once-per-chapter across sessions.</summary>
        public const string DeepReadCounterPrefix = "deep_read_";

        const string ScribeVocationId = "escriba";
        const int ScribePoints = 2;

        // ------------------------------------------------------------------ geometry
        //
        // Every length below comes from DesignTokens. The screen is one column — page, rule,
        // reading area, colophon — stacked by a VerticalLayoutGroup rather than by four sets of
        // hand-tuned offsets. That is not tidiness: the translation notice this build ships runs
        // to about 350 characters, and at the design system's minimum size it wraps to seven
        // lines. The old fixed 96-unit footer could not hold two of them, and Text overflows
        // upward from a LowerLeft anchor, so the notice was drawing over the last verses of the
        // chapter. A layout group asks the notice how tall it is and gives the reading area
        // whatever is left, in any language and at any notice length.

        /// <summary>Gutter between the modal's edge and the page.</summary>
        static readonly float SheetInsetX = DesignTokens.Space.S12;
        static readonly float SheetInsetTop = DesignTokens.Space.S20;
        static readonly float SheetInsetBottom = DesignTokens.Space.S16;

        /// <summary>
        /// Padding from the page's edge to anything printed on it.
        ///
        /// <c>UiArt.ScrollHalo</c> is added because the pergaminho frame keeps its soft elevated
        /// edge inside its own rect: without it the text sits two design points nearer the visible
        /// edge than the number says.
        /// </summary>
        static readonly float PagePadding = DesignTokens.Space.S16 + SheepGate.Art.UiArt.ScrollHalo;

        /// <summary>The header is a touch-target row: the close button sets its height.</summary>
        static readonly float HeaderHeight = DesignTokens.Space.TouchTarget;

        /// <summary>Room for "Fechar" at body-strong weight plus the button's own padding.</summary>
        static readonly float CloseButtonWidth = DesignTokens.Px(104f);

        /// <summary>One design point. A hairline, not a divider.</summary>
        static readonly float RuleThickness = DesignTokens.Px(1f);

        /// <summary>
        /// Width of the always-visible scrollbar, in the design system's units — the width it was
        /// already drawn at, restated rather than changed.
        ///
        /// Its permanence is the part that must not move. A bar that appears and disappears
        /// resizes the viewport, and the viewport height is the denominator of the scroll fraction
        /// that decides whether deep_read fires. The measurement stays honest because the bar
        /// never changes the geometry it is measured against.
        /// </summary>
        static readonly float ScrollbarWidth = DesignTokens.Px(4f);

        /// <summary>
        /// Floor under the reading area, so no colophon length can squeeze the chapter to nothing.
        /// If a notice ever overruns this the page overflows and someone sees it, which is the
        /// failure worth having: silently shrinking the one screen the product is measured on is
        /// not.
        /// </summary>
        static readonly float ReadingMinHeight = DesignTokens.Px(240f);

        /// <summary>Hanging indent for the verse numbers. Three mono digits, right aligned.</summary>
        static readonly float VerseNumberColumnWidth = DesignTokens.Px(24f);

        /// <summary>
        /// Leading for the chapter itself, as a multiple of the font size.
        ///
        /// The design system's body leading is 22 over 15, which is interface leading — sized for
        /// a sentence or two between other controls. This is the only screen in the game meant to
        /// be read for minutes, and a column read for minutes wants the long-form step. Expressed
        /// over the same 15 as the token so the two can be compared without arithmetic.
        /// </summary>
        const float ReadingLeading = 25f / 15f;

        /// <summary>
        /// Leading for the translation notice. Tighter than body, because a block of legal text
        /// reads as one object rather than as prose, and every line it saves is a line of the
        /// chapter the player gets back.
        /// </summary>
        const float ColophonLeading = 15f / 12f;

        // Chapters whose deep_read already fired in this process. Cheap protection against a
        // reader that is closed and reopened before the save is read again.
        static readonly HashSet<string> FiredChapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static ChapterReaderUI _current;

        string _chapterKey = string.Empty;
        string _trigger = DefaultTrigger;

        /// <summary>True when the game opened the reader rather than the player choosing to.</summary>
        bool _gameAsked;
        float _deepReadSeconds = DeepReadSeconds;
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

        /// <summary>
        /// Opens the reader on a chapter reference such as NEH.4, from a door the game put in front
        /// of the player: this is the overload the dialogue bridge reaches by reflection, and a
        /// "Saber mais" under a quotation the game just showed is the game asking. Counted as
        /// chapter_opened, never as unprompted_read.
        /// </summary>
        public static ChapterReaderUI Open(string chapterRef)
        {
            return Open(chapterRef, DefaultTrigger, true);
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
            _deepReadSeconds = DeepReadSecondsFor(chapter != null && chapter.verses != null ? chapter.verses.Length : 0);

            GameState state = TryGetState();
            _deepReadFired = FiredChapters.Contains(chapterKey) ||
                             (state != null && state.Counter(DeepReadCounterPrefix + chapterKey) > 0);

            BuildLayout(chapter);

            ScoreFirstOpen(state);
            TrackChapterOpened();

            // Measure from a settled layout: content height read on the frame it was built would
            // otherwise be zero and make the scroll fraction meaningless.
            //
            // The page is rebuilt first and the column second, and the order is load-bearing now
            // that the page is a layout group. The viewport's height is whatever the column above
            // it did not take, and the viewport height is the other half of the scroll fraction —
            // measuring the content against a viewport that had not been sized yet would report a
            // chapter as already read.
            UIKit.RebuildNow((RectTransform)transform);
            UIKit.RebuildNow(_content);

            // Set again now that the content has a real height: a scroll position assigned before
            // the fitter ran does not survive it.
            if (_scroll != null)
            {
                _scroll.verticalNormalizedPosition = 1f;
            }

            _maxScrollFraction = CurrentScrollFraction();
        }

        /// <summary>
        /// The page: header, hairline, chapter, colophon, stacked top to bottom.
        ///
        /// The surface is <see cref="UIKit.CardStyle.Scroll"/> — the pergaminho card section 06 of
        /// the design document applies to anything read at length, and this screen is the only one
        /// in the game read for minutes. Everything printed on it therefore takes
        /// <c>Ink.OnScroll</c> and <c>Ink.OnScrollMuted</c>: the dark-surface inks are invisible
        /// here, and asking <see cref="UIKit.InkFor"/> rather than assuming is the habit that keeps
        /// a future edit from painting parchment on parchment.
        /// </summary>
        void BuildLayout(ChapterEntry chapter)
        {
            var container = (RectTransform)transform;

            Image sheet = UIKit.CreateCard(container, "ReaderSheet", UIKit.CardStyle.Scroll);
            var sheetRect = (RectTransform)sheet.transform;
            UIKit.Stretch(sheetRect, SheetInsetX, SheetInsetX, SheetInsetTop, SheetInsetBottom);

            int pad = Mathf.RoundToInt(PagePadding);
            UIKit.VerticalGroup(sheet.gameObject, DesignTokens.Space.S12,
                                new RectOffset(pad, pad, pad, pad));

            Color ink = UIKit.InkFor(UIKit.CardStyle.Scroll);

            // ---- header
            RectTransform header = UIKit.CreateRect("Header", sheetRect);
            LayoutElement headerLayout = UIKit.Layout(header);
            headerLayout.minHeight = HeaderHeight;
            headerLayout.preferredHeight = HeaderHeight;

            string title = chapter != null && !string.IsNullOrEmpty(chapter.ref_display)
                ? chapter.ref_display
                : _chapterKey;

            Text titleText = UIKit.CreateText(header, "Title", title, DesignTokens.Type.Title, ink,
                                              TextAnchor.MiddleLeft, DesignTokens.TypeRole.Title);
            UIKit.Stretch((RectTransform)titleText.transform,
                          0f, CloseButtonWidth + DesignTokens.Space.S12, 0f, 0f);

            // Primary, and not the secondary or ghost the chrome of a reading screen would
            // normally take. The six variants are specified against the dark surfaces; on the
            // pergaminho only the two filled ones keep their contrast, because a translucent
            // parchment fill and a parchment label both vanish into a near-white card. Quest is
            // the other filled one and rule 9 reserves gold for what is new, so clay it is — and
            // the way out of the most important screen in the product had better be the one
            // control on it a thumb cannot miss.
            Button close = UIKit.CreateButton(header, "Close", Loc.T("reader.close"),
                                              UIKit.ButtonVariant.Primary, Close);
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(CloseButtonWidth, HeaderHeight);
            closeRect.anchoredPosition = Vector2.zero;

            // No sprite: a hairline is one design point tall, and the nine-slice frames carry
            // corner slices many times that. Sliced art squeezed into a rect thinner than its own
            // border does not draw a thin line, it draws a smeared corner.
            Image rule = UIKit.CreatePanel(sheetRect, "HeaderRule",
                                           UIKit.WithAlpha(DesignTokens.Ink.OnScrollMuted, 0.35f), null);
            rule.raycastTarget = false;
            LayoutElement ruleLayout = UIKit.Layout(rule);
            ruleLayout.minHeight = RuleThickness;
            ruleLayout.preferredHeight = RuleThickness;

            // ---- chapter body
            //
            // Built before the colophon so the sibling order is the reading order, and given the
            // only flexible height on the page: header, rule and colophon each ask for exactly
            // what they need, and every remaining unit goes to the chapter.
            RectTransform content;
            _scroll = UIKit.CreateScrollView(sheetRect, "Chapter", out content);
            _content = content;
            _viewport = _scroll.viewport;

            LayoutElement scrollLayout = UIKit.Layout(_scroll);
            scrollLayout.minHeight = ReadingMinHeight;
            scrollLayout.flexibleHeight = 1f;

            StyleContentColumn();

            // Permanent, and permanent on purpose. See ScrollbarWidth.
            UIKit.AttachVerticalScrollbar(_scroll, ScrollbarWidth);

            BuildVerses(chapter);

            // ---- colophon
            BuildColophon(sheetRect, chapter);

            // Start at the top of the chapter.
            _scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>
        /// Re-measures the column <see cref="UIKit.CreateScrollView"/> builds for a page of prose.
        ///
        /// The kit's defaults are sized for a list of controls inside a panel that has no padding
        /// of its own. Here the page is already padded, so the left inset goes to zero and the
        /// right inset becomes exactly the clearance the always-visible scrollbar needs — without
        /// it the last word of every line runs under the bar, which is the kind of thing that
        /// looks like a font problem and is a padding problem.
        /// </summary>
        void StyleContentColumn()
        {
            var column = _content.GetComponent<VerticalLayoutGroup>();
            if (column == null)
            {
                return;
            }

            column.spacing = DesignTokens.Space.S16;
            column.padding = new RectOffset(
                0,
                Mathf.RoundToInt(ScrollbarWidth + DesignTokens.Space.S8),
                Mathf.RoundToInt(DesignTokens.Space.S4),
                Mathf.RoundToInt(DesignTokens.Space.S32));
        }

        /// <summary>
        /// The reference, the translation's abbreviation, and its copyright notice.
        ///
        /// A nested layout group rather than a fixed-height strip, so the notice is measured
        /// rather than guessed at. It is also the reason there is no ContentSizeFitter here: a
        /// group is itself a layout element, so the page above asks it how tall it is and gets a
        /// real answer, while a fitter inside a group would fight the group for the same rect.
        /// </summary>
        void BuildColophon(RectTransform sheetRect, ChapterEntry chapter)
        {
            VersionInfo version = SafeVersion();

            RectTransform footer = UIKit.CreateRect("Colophon", sheetRect);
            UIKit.VerticalGroup(footer.gameObject, DesignTokens.Space.S4, new RectOffset());

            // Mono, because a chapter-and-verse reference is a reference: the design system's own
            // definition of what the mono face is for, and its digits are tabular so a two-digit
            // chapter does not shuffle the abbreviation sideways.
            UIKit.CreateText(footer, "Reference", BuildMetaLine(chapter, version),
                             DesignTokens.Type.Mono, DesignTokens.Ink.OnScrollMuted,
                             TextAnchor.LowerLeft, DesignTokens.TypeRole.Mono);

            string notice = version != null ? version.copyright : null;
            if (string.IsNullOrEmpty(notice))
            {
                return;
            }

            Text copyright = UIKit.CreateText(footer, "Copyright", notice,
                                              DesignTokens.Type.Minimum, DesignTokens.Ink.OnScrollMuted,
                                              TextAnchor.LowerLeft);
            copyright.lineSpacing = UIKit.LineSpacingFor(copyright.font, ColophonLeading);
        }

        /// <summary>
        /// One row per verse: a mono number in a hanging indent, then the verse.
        ///
        /// The verse itself is the only text in the game that gets <see cref="ReadingLeading"/>.
        /// The line this replaced set <c>lineSpacing = 1.15f</c> directly, which is not leading at
        /// all — <see cref="Text.lineSpacing"/> multiplies the font's own line height rather than
        /// the font size, so a raw 1.15 was setting the chapter about 20% tighter than the design
        /// system's interface copy on the one screen that wanted it looser.
        /// </summary>
        void BuildVerses(ChapterEntry chapter)
        {
            ChapterVerse[] verses = chapter != null ? chapter.verses : null;
            if (verses == null || verses.Length == 0)
            {
                Text empty = UIKit.CreateText(_content, "Empty", Loc.T("reader.empty"),
                    DesignTokens.Type.Body, DesignTokens.Ink.OnScrollMuted, TextAnchor.UpperLeft);
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
                UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.S8, new RectOffset(),
                                      TextAnchor.UpperLeft);

                Text number = UIKit.CreateText(row, "Number", verse.n.ToString(),
                    DesignTokens.Type.Mono, DesignTokens.Ink.OnScrollMuted, TextAnchor.UpperRight,
                    DesignTokens.TypeRole.Mono);
                LayoutElement numberLayout = UIKit.Layout(number);
                numberLayout.preferredWidth = VerseNumberColumnWidth;
                numberLayout.minWidth = VerseNumberColumnWidth;
                numberLayout.flexibleWidth = 0f;

                Text body = UIKit.CreateText(row, "Body", verse.text ?? string.Empty,
                    DesignTokens.Type.Body, DesignTokens.Ink.OnScroll, TextAnchor.UpperLeft);
                body.lineSpacing = UIKit.LineSpacingFor(body.font, ReadingLeading);
                LayoutElement bodyLayout = UIKit.Layout(body);
                bodyLayout.flexibleWidth = 1f;
            }
        }

        /// <summary>The reference and the translation's abbreviation, on one line.</summary>
        string BuildMetaLine(ChapterEntry chapter, VersionInfo version)
        {
            string reference = chapter != null && !string.IsNullOrEmpty(chapter.ref_display)
                ? chapter.ref_display
                : _chapterKey;

            if (version != null && !string.IsNullOrEmpty(version.abbrev))
            {
                reference += "  ·  " + version.abbrev;
            }

            return reference;
        }

        static VersionInfo SafeVersion()
        {
            try
            {
                return ScriptureService.Version;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[ChapterReaderUI] Could not read the translation metadata: " + exception.Message);
                return null;
            }
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
                _visibleSeconds >= _deepReadSeconds &&
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

            if (_trigger == UngamedTrigger)
            {
                Telemetry.Track(TelemetryEvents.UngamedRead, new Dictionary<string, object>
                {
                    { "ref", _chapterKey },
                    { "seconds", seconds }
                });
            }

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

            // Desire, not compliance. A door the game put on screen at a moment it chose — A
            // Página, the "Saber mais" under a quotation, the record at the gate — is the game
            // asking, and opening it is compliance with a prompt however gently the prompt was
            // worded. The one door the player has to go looking for is the study card in the
            // profile, and that is the only open that counts here. Until this distinction was
            // drawn every caller passed false and unprompted_read duplicated chapter_opened.
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
