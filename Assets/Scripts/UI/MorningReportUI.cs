using System;
using System.Collections.Generic;
using SheepGate.Core;
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
        /// <summary>The morning that is starting, 1..3.</summary>
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
            summary.workDone = Mathf.Max(0, state.Counter(MorningReportUI.NightWorkCounter));
            summary.workRecorded = HasCounter(state, MorningReportUI.NightWorkCounter);
            summary.watchThreshold = DayCycle.WatchThreshold(summary.workers + summary.watchers);

            int previousDay = morningDay - 1;
            if (previousDay == 1)
            {
                summary.watchPosted = state.HasFlag(GameFlags.WatchPostedD1);
            }
            else if (previousDay == 2)
            {
                summary.watchPosted = state.HasFlag(GameFlags.WatchPostedD2);
            }

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
    }

    /// <summary>
    /// The morning after. This screen has one job beyond informing: a night that had a watch on the
    /// wall must not read like a night that did not. That difference is an acceptance criterion,
    /// so it is carried by three separate signals at once — the headline sentence, the accent
    /// colour, and an explicit status chip — rather than by a number the player has to compare
    /// against a memory of yesterday.
    ///
    /// It reports and does not judge. There is no line telling the player they should have split
    /// the crew differently, because there is no split this game considers correct.
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

        const float CardWidth = 1000f;
        const float CardHeight = 1000f;

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
            Color accent = watched ? UIKit.Palette.Olive : UIKit.Palette.Clay;

            var container = (RectTransform)transform;

            Image card = UIKit.CreatePanel(container, "Card", UIKit.Palette.Panel);
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(CardWidth, CardHeight);
            cardRect.anchoredPosition = Vector2.zero;

            Text kicker = UIKit.CreateText(cardRect, "Kicker", Loc.T("morning.kicker", Mathf.Max(1, summary.day)),
                UIKit.FontSize.Meta, UIKit.Palette.Muted, TextAnchor.MiddleLeft);
            UIKit.AnchorTop((RectTransform)kicker.transform, 44f, 56f, 56f, 44f);

            Text headline = UIKit.CreateText(cardRect, "Headline", Headline(watched),
                UIKit.FontSize.Heading, accent, TextAnchor.UpperLeft);
            UIKit.AnchorTop((RectTransform)headline.transform, 140f, 56f, 56f, 100f);

            Image rule = UIKit.CreatePanel(cardRect, "Accent", accent);
            var ruleRect = (RectTransform)rule.transform;
            UIKit.AnchorTop(ruleRect, 8f, 56f, 56f, 252f);
            rule.raycastTarget = false;

            BuildStatusChip(cardRect, watched, accent);
            BuildBody(cardRect, summary);

            Button confirm = UIKit.CreateButton(cardRect, "Confirm", Loc.T("morning.confirm"), UIKit.Palette.Clay, UIKit.Palette.Parchment, Close);
            var confirmRect = (RectTransform)confirm.transform;
            confirmRect.anchorMin = new Vector2(0.5f, 0f);
            confirmRect.anchorMax = new Vector2(0.5f, 0f);
            confirmRect.pivot = new Vector2(0.5f, 0f);
            confirmRect.sizeDelta = new Vector2(560f, 124f);
            confirmRect.anchoredPosition = new Vector2(0f, 52f);
        }

        void BuildStatusChip(RectTransform cardRect, bool watched, Color accent)
        {
            Image chip = UIKit.CreatePanel(cardRect, "Status", accent);
            var chipRect = (RectTransform)chip.transform;
            chipRect.anchorMin = new Vector2(0f, 1f);
            chipRect.anchorMax = new Vector2(0f, 1f);
            chipRect.pivot = new Vector2(0f, 1f);
            chipRect.sizeDelta = new Vector2(300f, 60f);
            chipRect.anchoredPosition = new Vector2(56f, -284f);
            chip.raycastTarget = false;

            Text label = UIKit.CreateText(chipRect, "Label", watched ? Loc.T("morning.chip.watched") : Loc.T("morning.chip.unwatched"),
                UIKit.FontSize.Meta, UIKit.Palette.Ink, TextAnchor.MiddleCenter);
            UIKit.Stretch((RectTransform)label.transform, 12f, 12f, 4f, 4f);
        }

        void BuildBody(RectTransform cardRect, NightSummary summary)
        {
            RectTransform body = UIKit.CreateRect("Body", cardRect);
            UIKit.AnchorTop(body, 420f, 56f, 56f, 376f);
            UIKit.VerticalGroup(body.gameObject, 22f, new RectOffset(0, 0, 0, 0));

            List<string> lines = BuildLines(summary);
            for (int i = 0; i < lines.Count; i++)
            {
                Text line = UIKit.CreateText(body, "Line_" + i, lines[i], UIKit.FontSize.Body, UIKit.Palette.Parchment, TextAnchor.UpperLeft);
                UIKit.Layout(line).flexibleWidth = 1f;
            }
        }

        // ------------------------------------------------------------------ copy

        static string Headline(bool watched)
        {
            return watched
                ? Loc.T("morning.headline.watched")
                : Loc.T("morning.headline.unwatched");
        }

        static List<string> BuildLines(NightSummary summary)
        {
            var lines = new List<string>(5);

            lines.Add(Loc.T("morning.split", Mathf.Max(0, summary.workers), Mathf.Max(0, summary.watchers)));

            // The rule, restated only on the morning it decided something. Stated after the fact
            // it is information, not a correction: the next split is the player's to make again.
            if (!summary.watchPosted && summary.watchThreshold > 0)
            {
                lines.Add(Loc.T("morning.watch_rule", summary.watchThreshold));
            }

            if (summary.workRecorded || summary.workDone > 0)
            {
                if (summary.workDone > 0)
                {
                    lines.Add(Loc.Plural("morning.work", summary.workDone));
                }
                else
                {
                    lines.Add(Loc.T("morning.work.none"));
                }
            }

            if (summary.damagedSegments > 0)
            {
                lines.Add(Loc.Plural("morning.damage", summary.damagedSegments));
            }
            else
            {
                lines.Add(Loc.T("morning.damage.none"));
            }

            // The promise the whole game is built on, stated as a fact and not as comfort.
            lines.Add(Loc.T("morning.no_regress"));

            return lines;
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
