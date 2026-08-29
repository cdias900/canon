using System;
using SheepGate.Core;
using SheepGate.World;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The end-of-day split: one slider that divides the crew between the work and the watch.
    ///
    /// Two design constraints shape this screen.
    ///
    /// First, one control. The split is a single decision with a single cost, and two independent
    /// inputs would let the player dodge the decision by moving both.
    ///
    /// Second, neither side can be emptied: the slider is bounded by <see cref="MinimumPerSide"/>
    /// on both ends, so there is always someone building and always someone on the wall.
    ///
    /// The panel states one rule and no outcomes. The rule is where the watch starts counting,
    /// read live from <see cref="DayCycle.WatchThreshold"/> as the slider moves, because a player
    /// who cannot see the line cannot gamble against it — they can only guess, and a guess is not
    /// a decision. What the night then did with that split is the morning report's to tell, and
    /// this screen still never marks a split as the right one.
    /// </summary>
    public sealed class EndDayPanel : MonoBehaviour
    {
        /// <summary>Id this screen occupies in the modal stack.</summary>
        public const string ModalId = "end_day";

        /// <summary>Crew size used when the run does not tell us one.</summary>
        public const int DefaultCrew = 12;

        /// <summary>
        /// Smallest number of people that may be left on either side of the split.
        ///
        /// One, so neither side can be emptied (NEH.4.16). Every bound and clamp on this screen
        /// reads this constant and nothing else, so the decision is a single token to change.
        ///
        /// This used to conflict with DayCycle, which counted any single watcher as a watch and so
        /// made an unwatched night unreachable. The conflict is settled the other way round: the
        /// bound stays, because emptying a side is exactly what the chapter refuses, and DayCycle
        /// now asks for half the crew before it calls the wall watched. So one person on the wall
        /// is still a legal split, and it is still not a watch — which is the gamble the night was
        /// always supposed to offer.
        /// </summary>
        public const int MinimumPerSide = 1;

        const float CardWidth = 1000f;
        const float CardHeight = 1120f;

        static EndDayPanel _current;

        int _total = DefaultCrew;
        int _workers;
        bool _closed;
        bool _built;

        Action<int, int> _onConfirm;

        Text _workNumber;
        Text _watchNumber;
        Text _workHint;
        Text _watchHint;
        Text _watchStatus;
        Image _watchAccent;

        /// <summary>True while the split panel is on screen.</summary>
        public static bool IsOpen
        {
            get { return _current != null && !_current._closed; }
        }

        /// <summary>The panel currently on screen, or null.</summary>
        public static EndDayPanel Current
        {
            get { return _current != null && !_current._closed ? _current : null; }
        }

        // ------------------------------------------------------------------ opening

        /// <summary>
        /// Opens the split with the crew size the run implies. The confirmed split is handed to
        /// whichever DayCycle is in the scene.
        ///
        /// The arity matters: DayCycle.RequestEndDay reaches this by reflection and matches
        /// parameter counts exactly, so Open() and Open(DayCycle) both have to exist as written.
        /// </summary>
        public static EndDayPanel Open()
        {
            return Open(ResolveCrewSize(), null);
        }

        /// <summary>Opens the split and resolves the night on the day cycle that asked for it.</summary>
        public static EndDayPanel Open(DayCycle cycle)
        {
            if (cycle == null)
            {
                return Open();
            }

            return Open(ResolveCrewSize(), (workers, watchers) => cycle.EndDay(workers, watchers));
        }

        /// <summary>
        /// Opens the split for an explicit crew size. A null callback falls back to the DayCycle
        /// in the scene.
        /// </summary>
        public static EndDayPanel Open(int totalPeople, Action<int, int> onConfirm)
        {
            if (_current != null && !_current._closed)
            {
                // Already up. Opening twice would stack two panels over the same decision.
                return _current;
            }

            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[EndDayPanel] No modal root is available; the split cannot open.");
                return null;
            }

            RectTransform container = root.Push(ModalId);
            if (container == null)
            {
                return null;
            }

            var panel = container.gameObject.AddComponent<EndDayPanel>();
            panel.Build(totalPeople, onConfirm);
            _current = panel;
            return panel;
        }

        /// <summary>Closes without resolving the night.</summary>
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

        void Build(int totalPeople, Action<int, int> onConfirm)
        {
            if (_built)
            {
                return;
            }

            _built = true;
            _onConfirm = onConfirm;
            _total = Mathf.Max(MinimumPerSide * 2, Mathf.Max(1, totalPeople));
            _workers = ResolveInitialWorkers(_total);

            var container = (RectTransform)transform;

            Image card = UIKit.CreatePanel(container, "Card", UIKit.Palette.Panel);
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(CardWidth, CardHeight);
            cardRect.anchoredPosition = Vector2.zero;

            Text title = UIKit.CreateText(cardRect, "Title", Loc.T("end_day.title"), UIKit.FontSize.Title, UIKit.Palette.Parchment, TextAnchor.MiddleLeft);
            UIKit.AnchorTop((RectTransform)title.transform, 72f, 56f, 56f, 44f);

            Text subtitle = UIKit.CreateText(cardRect, "Subtitle", Loc.T("end_day.subtitle"), UIKit.FontSize.Body, UIKit.Palette.Muted, TextAnchor.MiddleLeft);
            UIKit.AnchorTop((RectTransform)subtitle.transform, 56f, 56f, 56f, 126f);

            BuildSplitRow(cardRect);

            Slider slider = UIKit.CreateSlider(cardRect, "Split", MinimumPerSide, _total - MinimumPerSide, _workers, OnSliderChanged);
            UIKit.AnchorTop((RectTransform)slider.transform, 84f, 56f, 56f, 400f);

            // Read through a local so the check is not constant-folded: if the minimum is ever
            // set to zero, this line stops being true and the block stops being unreachable code.
            int minimumPerSide = MinimumPerSide;
            if (minimumPerSide > 0)
            {
                Text bounds = UIKit.CreateText(cardRect, "Bounds", Loc.T("end_day.bounds"), UIKit.FontSize.Meta, UIKit.Palette.Stone, TextAnchor.MiddleLeft);
                UIKit.AnchorTop((RectTransform)bounds.transform, 40f, 56f, 56f, 494f);
            }

            _workHint = UIKit.CreateText(cardRect, "WorkHint", string.Empty, UIKit.FontSize.Body, UIKit.Palette.Parchment, TextAnchor.UpperLeft);
            UIKit.AnchorTop((RectTransform)_workHint.transform, 96f, 56f, 56f, 546f);

            _watchHint = UIKit.CreateText(cardRect, "WatchHint", string.Empty, UIKit.FontSize.Body, UIKit.Palette.Parchment, TextAnchor.UpperLeft);
            UIKit.AnchorTop((RectTransform)_watchHint.transform, 96f, 56f, 56f, 646f);

            // Where the watch starts counting, restated on every drag of the slider. The words
            // stay parchment so they are always legible; the accent bar beside them carries the
            // same colour pair the morning report uses, so the two screens agree at a glance.
            RectTransform statusRow = UIKit.CreateRect("Watch Status", cardRect);
            UIKit.AnchorTop(statusRow, 104f, 56f, 56f, 754f);

            _watchAccent = UIKit.CreatePanel(statusRow, "Accent", UIKit.Palette.Olive);
            var accentRect = (RectTransform)_watchAccent.transform;
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.sizeDelta = new Vector2(10f, 0f);
            accentRect.anchoredPosition = Vector2.zero;
            _watchAccent.raycastTarget = false;

            _watchStatus = UIKit.CreateText(statusRow, "Label", string.Empty, UIKit.FontSize.Body, UIKit.Palette.Parchment, TextAnchor.UpperLeft);
            UIKit.Stretch((RectTransform)_watchStatus.transform, 28f, 0f, 0f, 0f);

            Text note = UIKit.CreateText(cardRect, "Note", Loc.T("end_day.note"), UIKit.FontSize.Meta, UIKit.Palette.Muted, TextAnchor.UpperLeft);
            UIKit.AnchorTop((RectTransform)note.transform, 56f, 56f, 56f, 866f);

            Button confirm = UIKit.CreateButton(cardRect, "Confirm", Loc.T("end_day.confirm"), UIKit.Palette.Clay, UIKit.Palette.Parchment, Confirm);
            var confirmRect = (RectTransform)confirm.transform;
            confirmRect.anchorMin = new Vector2(0.5f, 0f);
            confirmRect.anchorMax = new Vector2(0.5f, 0f);
            confirmRect.pivot = new Vector2(0.5f, 0f);
            confirmRect.sizeDelta = new Vector2(560f, 124f);
            confirmRect.anchoredPosition = new Vector2(0f, 52f);

            Refresh();
        }

        void BuildSplitRow(RectTransform cardRect)
        {
            RectTransform row = UIKit.CreateRect("Split Row", cardRect);
            UIKit.AnchorTop(row, 180f, 56f, 56f, 200f);

            _workNumber = BuildSplitBox(row, "Work", Loc.T("end_day.split.work"), UIKit.Palette.Olive, 0f, 0.5f, 0f, 12f);
            _watchNumber = BuildSplitBox(row, "Watch", Loc.T("end_day.split.watch"), UIKit.Palette.Night, 0.5f, 1f, 12f, 0f);
        }

        Text BuildSplitBox(RectTransform parent, string name, string label, Color accent, float anchorLeft, float anchorRight, float padLeft, float padRight)
        {
            Image box = UIKit.CreatePanel(parent, name, UIKit.Palette.PanelSoft);
            var boxRect = (RectTransform)box.transform;
            boxRect.anchorMin = new Vector2(anchorLeft, 0f);
            boxRect.anchorMax = new Vector2(anchorRight, 1f);
            boxRect.pivot = new Vector2(0.5f, 0.5f);
            boxRect.offsetMin = new Vector2(padLeft, 0f);
            boxRect.offsetMax = new Vector2(-padRight, 0f);
            box.raycastTarget = false;

            Image bar = UIKit.CreatePanel(boxRect, "Accent", accent);
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.sizeDelta = new Vector2(10f, 0f);
            barRect.anchoredPosition = Vector2.zero;
            bar.raycastTarget = false;

            Text caption = UIKit.CreateText(boxRect, "Caption", label, UIKit.FontSize.Meta, UIKit.Palette.Muted, TextAnchor.UpperLeft);
            UIKit.AnchorTop((RectTransform)caption.transform, 40f, 32f, 20f, 20f);

            Text number = UIKit.CreateText(boxRect, "Number", string.Empty, UIKit.FontSize.Title, UIKit.Palette.Parchment, TextAnchor.LowerLeft);
            UIKit.AnchorBottom((RectTransform)number.transform, 92f, 32f, 20f, 18f);

            return number;
        }

        // ------------------------------------------------------------------ interaction

        void OnSliderChanged(int workers)
        {
            _workers = ClampWorkers(workers);
            Refresh();
        }

        void Refresh()
        {
            int workers = ClampWorkers(_workers);
            int watchers = _total - workers;

            if (_workNumber != null)
            {
                _workNumber.text = workers.ToString();
            }

            if (_watchNumber != null)
            {
                _watchNumber.text = watchers.ToString();
            }

            if (_workHint != null)
            {
                _workHint.text = Loc.Plural("end_day.work_hint", workers);
            }

            if (_watchHint != null)
            {
                _watchHint.text = Loc.Plural("end_day.watch_hint", watchers);
            }

            RefreshWatchStatus(watchers);
        }

        /// <summary>
        /// States the rule and where this split falls against it. DayCycle answers both questions,
        /// so the line can never promise a watch the night will not honour.
        /// </summary>
        void RefreshWatchStatus(int watchers)
        {
            if (_watchStatus == null)
            {
                return;
            }

            int threshold = DayCycle.WatchThreshold(_total);
            bool posted = DayCycle.CountsAsWatch(watchers, _total);
            string rule = Loc.T("end_day.watch_rule", threshold);

            _watchStatus.text = (posted ? Loc.T("end_day.watch_posted") : Loc.T("end_day.watch_none"))
                                + "\n" + rule;

            if (_watchAccent != null)
            {
                _watchAccent.color = posted ? UIKit.Palette.Olive : UIKit.Palette.Clay;
            }
        }

        void Confirm()
        {
            if (_closed)
            {
                return;
            }

            int workers = ClampWorkers(_workers);
            int watchers = _total - workers;

            GameState state = TryGetState();
            if (state != null)
            {
                // Raw counts only. Whether this counts as a watch being posted is DayCycle's
                // judgement to make, and two systems answering that question would contradict.
                state.workAssigned = workers;
                state.watchAssigned = watchers;
            }

            Action<int, int> callback = _onConfirm;
            Close();

            if (callback != null)
            {
                callback(workers, watchers);
                return;
            }

            DayCycle cycle = FindFirstObjectByType<DayCycle>();
            if (cycle != null)
            {
                cycle.EndDay(workers, watchers);
                return;
            }

            Debug.LogError("[EndDayPanel] The split was confirmed but no DayCycle is in the scene; the night cannot resolve.");
        }

        void OnDestroy()
        {
            _closed = true;

            if (_current == this)
            {
                _current = null;
            }
        }

        // ------------------------------------------------------------------ helpers

        static int ResolveCrewSize()
        {
            GameState state = TryGetState();
            if (state != null && state.workCapacityMax >= 2)
            {
                return state.workCapacityMax;
            }

            return DefaultCrew;
        }

        /// <summary>Last night's split is the starting point; a fresh run starts even.</summary>
        static int ResolveInitialWorkers(int total)
        {
            GameState state = TryGetState();
            int previous = state != null ? state.workAssigned : 0;
            int workers = previous > 0 ? previous : total / 2;
            return Mathf.Clamp(workers, MinimumPerSide, total - MinimumPerSide);
        }

        static GameState TryGetState()
        {
            GameState state;
            return ServiceLocator.TryGet(out state) ? state : null;
        }

        /// <summary>Keeps both sides above the minimum, whatever the minimum is set to.</summary>
        int ClampWorkers(int workers)
        {
            return Mathf.Clamp(workers, MinimumPerSide, _total - MinimumPerSide);
        }

    }
}
