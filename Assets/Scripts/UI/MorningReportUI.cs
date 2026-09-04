using System;
using System.Collections.Generic;
using SheepGate.Core;
using SheepGate.Scripture;
using SheepGate.World;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// What the night did, as facts. Built either from the run's own state or from an explicit
    /// summary handed over by whoever resolved the night.
    /// </summary>
    public struct NightSummary
    {
        /// <summary>
        /// The morning that is starting, 1..9, and it IS the stage number — the season table in
        /// stages.json is keyed by day, so there is no second numbering for this screen to drift
        /// against. Written as a range rather than a constant because nothing here reads the table:
        /// the value arrives from whoever opened the report.
        /// </summary>
        public int day;

        /// <summary>Whether the night counted as having a watch on the wall. DayCycle decides this.</summary>
        public bool watchPosted;

        /// <summary>People sent to the work.</summary>
        public int workers;

        /// <summary>People posted to the watch.</summary>
        public int watchers;

        /// <summary>Work units the night crew added, or 0 when nothing recorded it.</summary>
        public int workDone;

        /// <summary>True when the night resolver recorded a work figure, zero included.</summary>
        public bool workRecorded;

        /// <summary>Segments that have stone out of place this morning.</summary>
        public int damagedSegments;

        /// <summary>People the wall needed for the watch to count, or 0 when unknown.</summary>
        public int watchThreshold;

        /// <summary>True when last night's crew built double on the path the player cleared.</summary>
        public bool pathCleared;

        /// <summary>Units the Tekoites returned on the player's stretch this morning, 0 on every other morning.</summary>
        public int tekoaReturned;

        /// <summary>True from the morning after the guard stage: every hand on the wall, the night counts differently.</summary>
        public bool armedNight;

        /// <summary>
        /// Courses still missing on the segment the season's gate hangs on, when this morning is
        /// the dedication's day and the doors cannot be hung yet. Zero on every other morning.
        /// The one number this screen adds on the last day, so a day that repeats never repeats
        /// in silence.
        /// </summary>
        public int gateCoursesLeft;

        /// <summary>Last night the crew off the wall kept vigil and laid nothing.</summary>
        public bool vigilKept;

        /// <summary>The page the vigil returned, as a reference into verses.json; null when no vigil was kept.</summary>
        public string vigilVerse;

        /// <summary>
        /// Reads the morning off the run. Only facts already written into the state are used: the
        /// watch flag is read, never inferred, so this screen can never contradict the system that
        /// actually resolved the night.
        /// </summary>
        public static NightSummary FromState(GameState state, int morningDay)
        {
            var summary = new NightSummary();
            summary.day = morningDay;

            if (state == null)
            {
                return summary;
            }

            summary.workers = Mathf.Max(0, state.workAssigned);
            summary.watchers = Mathf.Max(0, state.watchAssigned);
            summary.gateCoursesLeft = GateCoursesLeft(morningDay);
            summary.pathCleared = state.Counter(DayCycle.NightPathClearedCounter) > 0;
            summary.tekoaReturned = Mathf.Max(0, state.Counter(DayCycle.NightTekoaReturnedCounter));
            summary.armedNight = morningDay - 1 >= DayCycle.HalfAndHalfStage;
            summary.workDone = Mathf.Max(0, state.Counter(MorningReportUI.NightWorkCounter));
            summary.workRecorded = HasCounter(state, MorningReportUI.NightWorkCounter);
            summary.watchThreshold = DayCycle.WatchThreshold(summary.workers + summary.watchers);

            // One reading of one flag family, and it is deliberately not a chain of per-day
            // branches. It used to be `if (previousDay == 1) ... else if (previousDay == 2)` with
            // no else, which was complete for a three-day build and became a silent lie the moment
            // the season grew: from the fourth morning on, watchPosted stayed false whatever the
            // night had actually done, so the accent flipped to Sky, the headline became
            // morning.headline.unwatched and the chip said so — a screen reporting a failed watch
            // to a player who posted one, with no exception thrown and nothing in the log.
            //
            // A nine-stage season has eight mornings — MorningStarted is only ever raised out of a
            // resolved night, so stage 1 has none — and six of those eight would have said it.
            //
            // The shape is what fixed it: an expression over a computed flag name cannot be
            // missing a case, so a tenth stage needs no edit here and cannot reintroduce the bug.
            // Stage 1 would read watch_posted_d0, which nothing writes and which is false — the
            // same answer the old fall-through gave, and the right one for a morning that has no
            // night behind it.
            int previousDay = morningDay - 1;
            summary.watchPosted = state.HasFlag(GameFlags.WatchPostedForDay(previousDay));

            // The counter says whether last night was a vigil; the stage table says what it was
            // for. Read off the night that just ended, never the morning's own stage: the page is
            // about today, but it was bought yesterday.
            summary.vigilKept = state.Counter(DayCycle.NightVigilCounter) > 0
                && state.HasFlag(GameFlags.VigilKeptForDay(previousDay));
            summary.vigilVerse = summary.vigilKept ? DayCycle.VigilVerseFor(previousDay) : null;

            if (HasCounter(state, MorningReportUI.NightDamageCounter))
            {
                // What last night did. Preferred over the scan below, which cannot tell stone
                // knocked over tonight from stone still lying where an earlier night left it —
                // and a report that says COM VIGIA next to fresh damage reads like a lie.
                summary.damagedSegments = Mathf.Max(0, state.Counter(MorningReportUI.NightDamageCounter));
                return summary;
            }

            if (state.segments != null)
            {
                for (int i = 0; i < state.segments.Count; i++)
                {
                    WallSegmentState segment = state.segments[i];
                    if (segment != null && segment.damaged)
                    {
                        summary.damagedSegments++;
                    }
                }
            }

            return summary;
        }

        static bool HasCounter(GameState state, string key)
        {
            return state != null && state.counters != null && state.counters.ContainsKey(key);
        }

        /// <summary>
        /// Courses the gate segment still needs on the morning of the stage that closes it. The
        /// director owns the rule of what "earned" means; this only asks it.
        /// </summary>
        static int GateCoursesLeft(int morningDay)
        {
            StageDef stage = GameData.Stage(morningDay);
            if (stage == null || !stage.terminal || !stage.closes_gate)
            {
                return 0;
            }

            return StageDirector.GateCoursesLeft(stage);
        }
    }

    /// <summary>
    /// The morning after, laid out as the design system's progress screen: an eyebrow, one Display
    /// number, the state of the wall as a heading, then the split as labelled bars and the night's
    /// facts as step rows.
    ///
    /// This screen has one job beyond informing: a night that had a watch on the wall must not read
    /// like a night that did not. That difference is an acceptance criterion, so it is carried by
    /// three separate signals at once — the headline sentence, the accent colour, and an explicit
    /// status chip carrying an icon and a word — rather than by a number the player has to compare
    /// against a memory of yesterday.
    ///
    /// <b>The accent pair is deliberately Growth and Sky, never Growth and Clay.</b> An unwatched
    /// night is a fact, not a penalty, and CLAUDE.md rule 7 forbids anything here reading as
    /// punishment. Clay is the action colour and sits a hue away from the error red; the sky blue
    /// is the design system's information colour and says "worth noticing" without saying "you
    /// failed". Nothing on this screen is ever drawn in <c>Feedback.Error</c>.
    ///
    /// It reports and does not judge. There is no line telling the player they should have split
    /// the crew differently, because there is no split this game considers correct.
    ///
    /// <b>Layout.</b> Everything but the confirm lives in one self-measuring column inside a scroll
    /// view: a vertical layout group asks each row how tall it needs to be at the width it was
    /// given, so a longer translation or a headline that wraps to three lines pushes the column
    /// down rather than colliding with what is under it. The one measured constant is
    /// <see cref="FooterHeight"/>, because the confirm is pinned outside the scroll view where it
    /// can always be reached. This matters more than it looks: the design system's type is between
    /// 1.2 and 1.7 times the scale this screen was first built at, and a hand-measured column would
    /// have had to be re-measured again for every string that changed.
    ///
    /// It appears on its own: <see cref="MorningReportInstaller"/> at the foot of this file holds
    /// the subscription to DayCycle.MorningStarted, so every morning that follows a night opens
    /// with this screen.
    /// </summary>
    public sealed class MorningReportUI : MonoBehaviour
    {
        /// <summary>Id this screen occupies in the modal stack.</summary>
        public const string ModalId = "morning_report";

        /// <summary>
        /// Counter the night resolver writes with what the night crew actually built. Absent — an
        /// explicit summary, or a save from before the night recorded it — the line is left out.
        /// Must match SheepGate.World.DayCycle.NightWorkCounter.
        /// </summary>
        public const string NightWorkCounter = "night_work_done";

        /// <summary>
        /// Counter the night resolver writes with the segments last night damaged. Absent, the
        /// report falls back to counting segments that are damaged right now.
        /// Must match SheepGate.World.DayCycle.NightDamageCounter.
        /// </summary>
        public const string NightDamageCounter = "night_damaged_segments";

        /// <summary>
        /// Trigger on the chapter_opened this card raises, and the context on its verse_shown.
        /// One word, shared, so the funnel can join the vigil's exposure to its read.
        /// </summary>
        public const string VigilTrigger = "vigil";

        /// <summary>
        /// Height of the pinned action strip: a token of clearance, the button, and a token again.
        /// The only hardcoded vertical measurement on this screen — everything above it measures
        /// itself, which is what lets the type scale grow without a second pass over the layout.
        /// </summary>
        static readonly float FooterHeight =
            DesignTokens.Space.S24 * 2f + UIKit.ButtonMinHeight;

        /// <summary>A hairline, never thinner than a device pixel at the shortest supported width.</summary>
        static readonly float HairlineHeight = Mathf.Max(2f, DesignTokens.Px(1f));

        static MorningReportUI _current;

        NightSummary _summary;
        Action _onClosed;
        bool _closed;
        bool _built;

        /// <summary>True while the morning report is on screen.</summary>
        public static bool IsOpen
        {
            get { return _current != null && !_current._closed; }
        }

        /// <summary>The report currently on screen, or null.</summary>
        public static MorningReportUI Current
        {
            get { return _current != null && !_current._closed ? _current : null; }
        }

        /// <summary>The night this report is showing.</summary>
        public NightSummary Summary
        {
            get { return _summary; }
        }

        // ------------------------------------------------------------------ opening

        /// <summary>
        /// Shows the morning report, reading the night off the run's own state.
        ///
        /// The arity matters: DayCycle reaches this by reflection and matches parameter counts
        /// exactly, so a one-argument Show has to exist as written.
        /// </summary>
        public static MorningReportUI Show(int day)
        {
            return Show(day, null);
        }

        /// <summary>Shows the morning report and calls back once the player dismisses it.</summary>
        public static MorningReportUI Show(int day, Action onClosed)
        {
            return Show(day, NightSummary.FromState(TryGetState(), day), onClosed);
        }

        /// <summary>Shows the morning report from an explicit summary.</summary>
        public static MorningReportUI Show(int day, NightSummary summary, Action onClosed)
        {
            if (_current != null && !_current._closed)
            {
                return _current;
            }

            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[MorningReportUI] No modal root is available; the morning report cannot open.");
                return null;
            }

            RectTransform container = root.Push(ModalId);
            if (container == null)
            {
                return null;
            }

            summary.day = day;

            var report = container.gameObject.AddComponent<MorningReportUI>();
            report.Build(summary, onClosed);
            _current = report;
            return report;
        }

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

        void Build(NightSummary summary, Action onClosed)
        {
            if (_built)
            {
                return;
            }

            _built = true;
            _summary = summary;
            _onClosed = onClosed;

            bool watched = summary.watchPosted;
            Color accent = watched ? DesignTokens.Ambient.Growth : DesignTokens.Ambient.Sky;

            var container = (RectTransform)transform;

            // Full height rather than a fixed card: the report is a screen, and a screen that
            // measures itself cannot be outgrown by a longer translation.
            Image card = UIKit.CreateCard(container, "Card", UIKit.CardStyle.Panel);
            var cardRect = (RectTransform)card.transform;
            UIKit.Stretch(cardRect,
                          DesignTokens.Space.S16, DesignTokens.Space.S16,
                          DesignTokens.Space.S24, DesignTokens.Space.S24);

            RectTransform column;
            ScrollRect scroll = UIKit.CreateScrollView(cardRect, "Report", out column);
            UIKit.Stretch((RectTransform)scroll.transform, 0f, 0f, 0f, FooterHeight);
            PrepareScroll(scroll, column);

            BuildHeader(column, summary, watched, accent);
            BuildAssignment(column, summary);

            // Before the steps, not after them. The page is the whole return on a night's work,
            // and the first build put it under four step rows, where at 1080×1920 it sat below
            // the pinned footer: present in the hierarchy, passed by the e2e, and invisible to
            // anyone who did not scroll. What the vigil bought is the news of this morning, so it
            // comes right after the numbers and the routine lines come after it.
            BuildVigil(column, summary);
            BuildSteps(column, summary);
            BuildFooter(cardRect);
        }

        /// <summary>
        /// Retunes the kit's scroll view to the design system's rhythm, and gives it something to
        /// be dragged by.
        ///
        /// The drag target is not optional. Every text and icon the kit builds is
        /// <c>raycastTarget = false</c>, so a scroll view full of them has nothing under the
        /// finger: the ray would pass through the whole column and land on the card behind it,
        /// which is not inside the scroll view and would never scroll it. An invisible graphic on
        /// the viewport is Unity's own answer, and it sits behind the content rather than over it,
        /// so nothing interactive is covered.
        /// </summary>
        static void PrepareScroll(ScrollRect scroll, RectTransform column)
        {
            if (scroll != null && scroll.viewport != null && scroll.viewport.GetComponent<Graphic>() == null)
            {
                var catcher = scroll.viewport.gameObject.AddComponent<Image>();
                catcher.color = Color.clear;
                catcher.raycastTarget = true;
            }

            var group = column != null ? column.GetComponent<VerticalLayoutGroup>() : null;
            if (group == null)
            {
                return;
            }

            group.spacing = DesignTokens.Space.S16;
            group.padding = Pad(DesignTokens.Space.Gutter, DesignTokens.Space.S32);
        }

        /// <summary>
        /// The eyebrow and the status chip on one line, then the one Display number this screen is
        /// allowed, then the heading.
        ///
        /// Eyebrow and chip share a row rather than stacking because this screen is a column that
        /// can run past the bottom of a short phone, and the line the whole report is written to
        /// keep in view is the last one — that nothing already finished went backwards. Every row
        /// the header saves is a row that promise does not have to be scrolled to.
        /// </summary>
        static void BuildHeader(RectTransform column, NightSummary summary, bool watched, Color accent)
        {
            RectTransform top = UIKit.CreateRect("Header Row", column);
            UIKit.HorizontalGroup(top.gameObject, DesignTokens.Space.S12, new RectOffset(), TextAnchor.MiddleLeft);

            Text eyebrow = UIKit.CreateText(top, "Eyebrow", Loc.T("morning.eyebrow"),
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted,
                TextAnchor.MiddleLeft, DesignTokens.TypeRole.Mono);

            // The eyebrow takes the slack, which is what pushes the chip to the far edge without
            // anything here having to know how wide the card came out.
            UIKit.Layout(eyebrow).flexibleWidth = 1f;

            BuildStatusChip(top, watched, accent);

            // Named DayNumber and not "Day": the HUD's own day readout is called "Day", and
            // tools/e2e.sh reads it by that name three times (TextOf("Day")) to prove the screen
            // rebuilt in the new language. E2ERunner.Find returns the first ACTIVE match in an
            // unspecified order, so a second "Day" anywhere in the hierarchy makes those
            // assertions read whichever object the scan reached first — a test that fails, or
            // worse passes, for a reason that has nothing to do with what it is checking.
            UIKit.CreateText(column, "DayNumber", Loc.T("morning.day", Mathf.Max(1, summary.day)),
                DesignTokens.Type.Display, DesignTokens.Ink.Primary,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.Display);

            UIKit.CreateText(column, "Headline", Headline(watched),
                DesignTokens.Type.Title, DesignTokens.Ink.Primary,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.Title);
        }

        /// <summary>
        /// The watched / unwatched state, said three ways at once: filled in the accent colour,
        /// marked with an icon, and spelled out in a word. The design system's rule is that colour
        /// never carries a state on its own, and the icon is a sprite rather than a character
        /// because none of the three bundled font families has a check mark in it.
        /// </summary>
        static void BuildStatusChip(RectTransform row, bool watched, Color accent)
        {
            // Radius Sm, and no flexible width. Sm because Md is the floor every button in this
            // game is drawn at, so a chip at Md would read as a control the player can press; no
            // flexible width because the surrounding row then gives it exactly the width of its
            // own contents instead of stretching it into a band across the card.
            Image chip = UIKit.CreatePanel(row, "Status", accent, UiSpriteKeys.FrameSm);
            chip.raycastTarget = false;
            UIKit.HorizontalGroup(chip.gameObject, DesignTokens.Space.S8,
                                  Pad(DesignTokens.Space.S12, DesignTokens.Space.S8), TextAnchor.MiddleLeft);

            UIKit.CreateIcon(chip.transform, "Icon",
                             watched ? UiSpriteKeys.IconCheck : UiSpriteKeys.IconDot,
                             DesignTokens.Surface.Background, UIKit.IconSize);

            // Surface.Background rather than Ink.OnSecondary: the dark ink meant for gold falls
            // just under 4.5:1 on the sky blue, and the background ink clears it on both accents.
            UIKit.CreateText(chip.transform, "Label",
                watched ? Loc.T("morning.chip.watched") : Loc.T("morning.chip.unwatched"),
                DesignTokens.Type.Body, DesignTokens.Surface.Background,
                TextAnchor.MiddleLeft, DesignTokens.TypeRole.BodyStrong);
        }

        /// <summary>
        /// Last night's split, as two labelled bars over the same crew.
        ///
        /// A bar and not a sentence because the design system draws every quantity this way —
        /// label, bar and fraction together — and because the two sides read as one decision when
        /// they share a denominator. Neither bar is coloured by outcome: the split is the player's
        /// and the night has already happened, so there is nothing here to approve of.
        ///
        /// Skipped entirely when the run recorded no split, which is the only case where the
        /// fraction would have to read "0 of 0" and mean nothing.
        /// </summary>
        static void BuildAssignment(RectTransform column, NightSummary summary)
        {
            int workers = Mathf.Max(0, summary.workers);
            int watchers = Mathf.Max(0, summary.watchers);
            int crew = workers + watchers;

            if (crew <= 0)
            {
                return;
            }

            RectTransform bars = UIKit.CreateRect("Assignment", column);
            UIKit.VerticalGroup(bars.gameObject, DesignTokens.Space.S16, new RectOffset());

            ProgressBar work = UIKit.CreateProgress(bars, "WorkProgress", Loc.T("end_day.split.work"));
            work.SetValue(workers, crew);

            ProgressBar watch = UIKit.CreateProgress(bars, "WatchProgress", Loc.T("end_day.split.watch"));
            watch.SetValue(watchers, crew);
        }

        /// <summary>
        /// The night's facts, one row each, every one of them an icon and a sentence.
        ///
        /// The icon set is two marks and never three: a check for something that stands, a dot for
        /// something worth noticing. There is no cross and no error colour anywhere in here. Stone
        /// out of place is news, not a verdict, and a report that scored the player's night would
        /// be exactly the punishment this game does not do.
        /// </summary>
        static void BuildSteps(RectTransform column, NightSummary summary)
        {
            RectTransform steps = UIKit.CreateRect("Steps", column);
            UIKit.VerticalGroup(steps.gameObject, DesignTokens.Space.S12, new RectOffset());

            // The rule, restated only on the morning it decided something. Stated after the fact
            // it is information, not a correction: the next split is the player's to make again.
            if (!summary.watchPosted && summary.watchThreshold > 0)
            {
                BuildStep(steps, "Step_WatchRule", UiSpriteKeys.IconDot, DesignTokens.Ink.Muted,
                          Loc.T("morning.watch_rule", summary.watchThreshold), DesignTokens.Ink.Secondary);
            }

            if (summary.workRecorded || summary.workDone > 0)
            {
                bool built = summary.workDone > 0;
                BuildStep(steps, "Step_Work",
                          built ? UiSpriteKeys.IconCheck : UiSpriteKeys.IconDot,
                          built ? DesignTokens.Ambient.Growth : DesignTokens.Ink.Muted,
                          built ? Loc.Plural("morning.work", summary.workDone) : Loc.T("morning.work.none"),
                          DesignTokens.Ink.Primary);

                // What changed the count, said right under it. Each is a fact about a decision
                // the player made the day before, or about the day the whole city went armed.
                if (summary.vigilKept)
                {
                    BuildStep(steps, "Step_Vigil", UiSpriteKeys.IconDot, DesignTokens.Ambient.Sky,
                              Loc.T("morning.vigil"), DesignTokens.Ink.Secondary);
                }
                else if (summary.pathCleared)
                {
                    BuildStep(steps, "Step_PathCleared", UiSpriteKeys.IconCheck, DesignTokens.Ambient.Growth,
                              Loc.T("morning.path_cleared"), DesignTokens.Ink.Secondary);
                }
                else if (summary.armedNight && built)
                {
                    BuildStep(steps, "Step_Armed", UiSpriteKeys.IconDot, DesignTokens.Ink.Muted,
                              Loc.T("morning.armed_night"), DesignTokens.Ink.Secondary);
                }
            }

            if (summary.tekoaReturned > 0)
            {
                BuildStep(steps, "Step_Tekoa", UiSpriteKeys.IconCheck, DesignTokens.Ambient.Growth,
                          Loc.Plural("morning.tekoa_returned", summary.tekoaReturned), DesignTokens.Ink.Primary);
            }

            // The last day, when the doors cannot be hung yet. Sky, a dot and a count: news of
            // what is left, in the design system's information colour, and never a verdict.
            if (summary.gateCoursesLeft > 0)
            {
                BuildStep(steps, "Step_Gate", UiSpriteKeys.IconDot, DesignTokens.Ambient.Sky,
                          Loc.Plural("morning.gate_waits", summary.gateCoursesLeft), DesignTokens.Ink.Primary);
            }

            bool moved = summary.damagedSegments > 0;
            BuildStep(steps, "Step_Damage",
                      moved ? UiSpriteKeys.IconDot : UiSpriteKeys.IconCheck,
                      moved ? DesignTokens.Ambient.Sky : DesignTokens.Ambient.Growth,
                      moved ? Loc.Plural("morning.damage", summary.damagedSegments) : Loc.T("morning.damage.none"),
                      DesignTokens.Ink.Primary);

            // The promise the whole game is built on, stated as a fact and not as comfort. It is
            // always a check, because it is always true.
            BuildStep(steps, "Step_NoRegress", UiSpriteKeys.IconCheck, DesignTokens.Ambient.Growth,
                      Loc.T("morning.no_regress"), DesignTokens.Ink.Primary);
        }

        /// <summary>
        /// What the vigil bought: the page, quoted on its own card under the steps, with the way
        /// into its chapter beside the reference.
        ///
        /// Drawn like A Página — parchment, italic, the reference in mono — because it is the
        /// same kind of object: text that was fetched, not spoken, and that the player can walk
        /// into. The reference is gated by <see cref="ScriptureVisibility"/> exactly as every
        /// quotation in the game is, so a vigil kept before the reveal shows the words and not
        /// yet where they come from (rule 12), and the chapter is reachable either way, which is
        /// the half of that rule that is never deferred.
        ///
        /// Nothing here pays anything. The card is the whole return on the night's work, and the
        /// button opens the reader with the game asking — a vigil is the game's invitation, not
        /// the player's own idea, so it is never counted as unprompted.
        /// </summary>
        static void BuildVigil(RectTransform column, NightSummary summary)
        {
            if (!summary.vigilKept || string.IsNullOrEmpty(summary.vigilVerse))
            {
                return;
            }

            string verseRef = summary.vigilVerse.Trim();
            VerseEntry verse = ScriptureService.GetVerse(verseRef);

            Image card = UIKit.CreateCard(column, "VigilPage", UIKit.CardStyle.Scroll);
            card.raycastTarget = false;
            var cardRect = (RectTransform)card.transform;
            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S12,
                                Pad(DesignTokens.Space.S16, DesignTokens.Space.S16));

            UIKit.CreateText(cardRect, "VigilEyebrow", Loc.T("morning.vigil.eyebrow"),
                DesignTokens.Type.Minimum, DesignTokens.Ink.OnScrollMuted,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.BodyStrong);

            Text body = UIKit.CreateText(cardRect, "VigilVerse",
                verse != null && !string.IsNullOrEmpty(verse.text) ? verse.text : Loc.T("scripture.unavailable", verseRef),
                DesignTokens.Type.Body, DesignTokens.Ink.OnScroll, TextAnchor.UpperLeft);
            body.fontStyle = FontStyle.Italic;

            RectTransform footer = UIKit.CreateRect("VigilFooter", cardRect);
            UIKit.HorizontalGroup(footer.gameObject, DesignTokens.Space.S12, new RectOffset(), TextAnchor.MiddleLeft);

            Text reference = UIKit.CreateText(footer, "VigilReference",
                ReferenceLabel(verse, verseRef),
                DesignTokens.Type.Mono, DesignTokens.Ink.OnScrollMuted,
                TextAnchor.MiddleLeft, DesignTokens.TypeRole.Mono);
            UIKit.Layout(reference).flexibleWidth = 1f;

            string chapterRef = ScriptureService.ChapterRefOf(verseRef);
            if (!string.IsNullOrEmpty(chapterRef))
            {
                UIKit.CreateButton(footer, "VigilReadMore", Loc.T("dialogue.read_more"),
                    UIKit.ButtonVariant.Secondary,
                    () => ChapterReaderUI.Open(chapterRef, VigilTrigger, true));
            }

            Telemetry.Track(TelemetryEvents.VerseShown, new Dictionary<string, object>
            {
                { "ref", verseRef },
                { "context", VigilTrigger },
                { "stage", ResolveStageId(summary.day - 1) }
            });

            if (verse == null || string.IsNullOrEmpty(verse.text))
            {
                Debug.LogWarning("[Morning] " + verseRef + " has no text in verses.json; the vigil card shows the missing marker.");
            }
        }

        /// <summary>
        /// The reference under the page, or nothing at all before the reveal. Same rule as the
        /// dialogue and A Página: hidden is not denied, the chapter button beside it still opens.
        /// </summary>
        static string ReferenceLabel(VerseEntry verse, string verseRef)
        {
            if (!ScriptureVisibility.ReferencesVisible())
            {
                return string.Empty;
            }

            string label = verse != null && !string.IsNullOrEmpty(verse.ref_display) ? verse.ref_display : verseRef;
            VersionInfo version = ScriptureService.Version;
            if (version != null && !string.IsNullOrEmpty(version.abbrev))
            {
                label += " · " + version.abbrev;
            }

            return label;
        }

        /// <summary>The stage id the night belonged to, for the funnel; the number when the table has no row.</summary>
        static string ResolveStageId(int day)
        {
            StageDef stage = GameData.Stage(day);
            return stage != null && !string.IsNullOrEmpty(stage.id) ? stage.id : ("d" + day);
        }

        /// <summary>
        /// One step row: a fixed icon and a sentence that takes whatever height it needs.
        ///
        /// The row is centred rather than top-aligned so the icon sits against the middle of a
        /// two-line sentence instead of floating beside its first line.
        /// </summary>
        static void BuildStep(RectTransform parent, string name, string iconKey, Color iconTint,
                              string content, Color ink)
        {
            RectTransform row = UIKit.CreateRect(name, parent);
            UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.S12, new RectOffset(), TextAnchor.MiddleLeft);

            UIKit.CreateIcon(row, "Icon", iconKey, iconTint, UIKit.IconSize);

            Text text = UIKit.CreateText(row, "Text", content, DesignTokens.Type.Body, ink,
                                         TextAnchor.MiddleLeft, DesignTokens.TypeRole.Body);
            UIKit.Layout(text).flexibleWidth = 1f;
        }

        /// <summary>
        /// The one action, pinned outside the scroll view so it is reachable however long the
        /// report runs. Primary and not Quest: clay is the action that moves the game forward, and
        /// the gold call to action means "here is something new", which a morning is not.
        /// </summary>
        void BuildFooter(RectTransform cardRect)
        {
            RectTransform footer = UIKit.CreateRect("Footer", cardRect);
            UIKit.AnchorBottom(footer, FooterHeight, 0f, 0f, 0f);

            Image divider = UIKit.CreatePanel(footer, "Divider", DesignTokens.Surface.Border, null);
            UIKit.AnchorTop((RectTransform)divider.transform, HairlineHeight,
                            DesignTokens.Space.Gutter, DesignTokens.Space.Gutter, 0f);
            divider.raycastTarget = false;

            Button confirm = UIKit.CreateButton(footer, "Confirm", Loc.T("morning.confirm"),
                                                UIKit.ButtonVariant.Primary, Close);
            var rect = (RectTransform)confirm.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(DesignTokens.Space.Gutter, DesignTokens.Space.S24);
            rect.offsetMax = new Vector2(-DesignTokens.Space.Gutter,
                                         DesignTokens.Space.S24 + UIKit.ButtonMinHeight);
        }

        // ------------------------------------------------------------------ copy

        static string Headline(bool watched)
        {
            return watched
                ? Loc.T("morning.headline.watched")
                : Loc.T("morning.headline.unwatched");
        }

        // ------------------------------------------------------------------ teardown

        void OnDestroy()
        {
            _closed = true;

            if (_current == this)
            {
                _current = null;
            }

            Action callback = _onClosed;
            _onClosed = null;

            if (callback == null)
            {
                return;
            }

            try
            {
                callback();
            }
            catch (Exception exception)
            {
                Debug.LogError("[MorningReportUI] A listener threw while the report closed: " + exception.Message);
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// A spacing token as layout padding. <see cref="RectOffset"/> is integral, so a token has
        /// to be rounded on the way in — writing the conversion once keeps the rounding consistent
        /// and keeps the token names at the call sites.
        /// </summary>
        static RectOffset Pad(float horizontal, float vertical)
        {
            int x = Mathf.RoundToInt(horizontal);
            int y = Mathf.RoundToInt(vertical);
            return new RectOffset(x, x, y, y);
        }

        static GameState TryGetState()
        {
            GameState state;
            return ServiceLocator.TryGet(out state) ? state : null;
        }
    }

    /// <summary>
    /// Keeps the morning report attached to whatever <see cref="DayCycle"/> is in the scene.
    ///
    /// The report itself only exists while it is on screen — it is a component added to a modal
    /// container and destroyed with it — so something else has to be holding the subscription when
    /// a night ends. This is that something: one object, created before any scene runs, that finds
    /// the day cycle and listens.
    ///
    /// Why it installs itself rather than being composed: the world layer reaches the UI through
    /// reflection precisely so it never names a UI type, and the composer it does call builds only
    /// the HUD. Installing here adds no wiring to a file the UI layer does not own. Living in the
    /// SheepGate.UI namespace is also what lets DayCycle see that a UI listener exists and stop
    /// warning that the morning went unreported.
    /// </summary>
    internal sealed class MorningReportInstaller : MonoBehaviour
    {
        const float SearchInterval = 0.5f;

        static MorningReportInstaller _instance;

        DayCycle _cycle;
        float _nextSearch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        internal static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var host = new GameObject("MorningReportInstaller");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<MorningReportInstaller>();
        }

        void Update()
        {
            // A destroyed DayCycle compares equal to null, so loading another scene re-arms the
            // search on its own and the next day cycle gets its own subscription.
            if (_cycle != null || Time.unscaledTime < _nextSearch)
            {
                return;
            }

            _nextSearch = Time.unscaledTime + SearchInterval;

            DayCycle cycle = FindFirstObjectByType<DayCycle>();
            if (cycle == null)
            {
                return;
            }

            _cycle = cycle;
            _cycle.MorningStarted += OnMorningStarted;
        }

        void OnMorningStarted(int day)
        {
            MorningReportUI.Show(day);
        }

        void OnDestroy()
        {
            if (_cycle != null)
            {
                _cycle.MorningStarted -= OnMorningStarted;
                _cycle = null;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
