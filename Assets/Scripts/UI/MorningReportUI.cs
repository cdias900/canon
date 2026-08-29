using System;
using System.Collections.Generic;
using SheepGate.Core;
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

        /// <summary>Segments that have stone out of place this morning.</summary>
        public int damagedSegments;

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

            int previousDay = morningDay - 1;
            if (previousDay == 1)
            {
                summary.watchPosted = state.HasFlag(GameFlags.WatchPostedD1);
            }
            else if (previousDay == 2)
            {
                summary.watchPosted = state.HasFlag(GameFlags.WatchPostedD2);
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
    /// </summary>
    public sealed class MorningReportUI : MonoBehaviour
    {
        /// <summary>Id this screen occupies in the modal stack.</summary>
        public const string ModalId = "morning_report";

        /// <summary>
        /// Optional counter the night resolver may write so the report can state how much the
        /// night crew actually built. Absent, the line is simply left out.
        /// </summary>
        public const string NightWorkCounter = "night_work_done";

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

            Text kicker = UIKit.CreateText(cardRect, "Kicker", "Manhã do dia " + Mathf.Max(1, summary.day),
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

            Button confirm = UIKit.CreateButton(cardRect, "Confirm", "Começar o dia", UIKit.Palette.Clay, UIKit.Palette.Parchment, Close);
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

            Text label = UIKit.CreateText(chipRect, "Label", watched ? "COM VIGIA" : "SEM VIGIA",
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
                ? "Houve vigia no muro a noite toda."
                : "O muro passou a noite sem vigia.";
        }

        static List<string> BuildLines(NightSummary summary)
        {
            var lines = new List<string>(4);

            lines.Add("Na obra: " + Mathf.Max(0, summary.workers) + ".   No muro: " + Mathf.Max(0, summary.watchers) + ".");

            if (summary.workDone > 0)
            {
                lines.Add(summary.workDone == 1
                    ? "A noite rendeu 1 de trabalho na muralha."
                    : "A noite rendeu " + summary.workDone + " de trabalho na muralha.");
            }

            if (summary.damagedSegments > 0)
            {
                lines.Add(summary.damagedSegments == 1
                    ? "1 trecho amanheceu com pedra fora do lugar."
                    : summary.damagedSegments + " trechos amanheceram com pedra fora do lugar.");
            }
            else
            {
                lines.Add("Nenhum trecho saiu do lugar.");
            }

            // The promise the whole game is built on, stated as a fact and not as comfort.
            lines.Add("Nada do que já ficou pronto voltou atrás.");

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
}
