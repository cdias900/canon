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
    /// on both ends, so there is always someone building and always someone watching.
    ///
    /// The panel says where the people go and stops there. It promises no outcome, because the
    /// night is not this screen's to describe — it never marks a split as the right one, and the
    /// morning report is where the player finds out what happened.
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
        /// One, so neither side can be emptied. Every bound and clamp on this screen reads this
        /// constant and nothing else, so the decision is a single token to change.
        ///
        /// KNOWN CONFLICT: DayCycle treats any night with at least one watcher as watched
        /// (LastNightHadWatch = watchers > 0), so while this is 1 the player cannot reach an
        /// unwatched night, and the split has no mechanical consequence. Either this becomes 0 or
        /// DayCycle's threshold rises above 1 — the two cannot both stand.
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

            Text title = UIKit.CreateText(cardRect, "Title", "Fim do dia", UIKit.FontSize.Title, UIKit.Palette.Parchment, TextAnchor.MiddleLeft);
            UIKit.AnchorTop((RectTransform)title.transform, 72f, 56f, 56f, 44f);

            Text subtitle = UIKit.CreateText(cardRect, "Subtitle", "Divida a sua gente para esta noite.", UIKit.FontSize.Body, UIKit.Palette.Muted, TextAnchor.MiddleLeft);
            UIKit.AnchorTop((RectTransform)subtitle.transform, 56f, 56f, 56f, 126f);

            BuildSplitRow(cardRect);

            Slider slider = UIKit.CreateSlider(cardRect, "Split", MinimumPerSide, _total - MinimumPerSide, _workers, OnSliderChanged);
            UIKit.AnchorTop((RectTransform)slider.transform, 84f, 56f, 56f, 400f);

            // Read through a local so the check is not constant-folded: if the minimum is ever
            // set to zero, this line stops being true and the block stops being unreachable code.
            int minimumPerSide = MinimumPerSide;
            if (minimumPerSide > 0)
            {
                Text bounds = UIKit.CreateText(cardRect, "Bounds", "Sempre fica alguém dos dois lados.", UIKit.FontSize.Meta, UIKit.Palette.Stone, TextAnchor.MiddleLeft);
                UIKit.AnchorTop((RectTransform)bounds.transform, 44f, 56f, 56f, 494f);
            }

            _workHint = UIKit.CreateText(cardRect, "WorkHint", string.Empty, UIKit.FontSize.Body, UIKit.Palette.Parchment, TextAnchor.UpperLeft);
            UIKit.AnchorTop((RectTransform)_workHint.transform, 104f, 56f, 56f, 552f);

            _watchHint = UIKit.CreateText(cardRect, "WatchHint", string.Empty, UIKit.FontSize.Body, UIKit.Palette.Parchment, TextAnchor.UpperLeft);
            UIKit.AnchorTop((RectTransform)_watchHint.transform, 104f, 56f, 56f, 664f);

            Text note = UIKit.CreateText(cardRect, "Note", "De manhã você vê o que a noite fez.", UIKit.FontSize.Meta, UIKit.Palette.Muted, TextAnchor.UpperLeft);
            UIKit.AnchorTop((RectTransform)note.transform, 80f, 56f, 56f, 786f);

            Button confirm = UIKit.CreateButton(cardRect, "Confirm", "Fechar o dia", UIKit.Palette.Clay, UIKit.Palette.Parchment, Confirm);
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

            _workNumber = BuildSplitBox(row, "Work", "Obra", UIKit.Palette.Olive, 0f, 0.5f, 0f, 12f);
            _watchNumber = BuildSplitBox(row, "Watch", "Guarda", UIKit.Palette.Night, 0.5f, 1f, 12f, 0f);
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

            Text number = UIKit.CreateText(boxRect, "Number", "0", UIKit.FontSize.Title, UIKit.Palette.Parchment, TextAnchor.LowerLeft);
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
                _workHint.text = Plural(workers,
                    "1 pessoa segue na obra até o sol nascer.",
                    workers + " pessoas seguem na obra até o sol nascer.");
            }

            if (_watchHint != null)
            {
                _watchHint.text = Plural(watchers,
                    "1 pessoa fica no muro, de olho na estrada, e não levanta pedra.",
                    watchers + " pessoas ficam no muro, de olho na estrada, e não levantam pedra.");
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

        static string Plural(int count, string singular, string plural)
        {
            return count == 1 ? singular : plural;
        }
    }
}
