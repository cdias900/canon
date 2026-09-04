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
    /// Third, it opens itself. The day ends when its work capacity is spent, so this screen is
    /// not something the player asks for — it is the evening arriving. When it arrives early
    /// because the player chose the mat rather than because the day ran out, they can still turn
    /// it down: <see cref="DayCycle.CanDeferDusk"/> is what decides whether that way back exists,
    /// so the button is offered exactly when the day really could go on.
    ///
    /// The panel states one rule and no outcomes. The rule is where the watch starts counting,
    /// read live from <see cref="DayCycle.WatchThreshold"/> as the slider moves, because a player
    /// who cannot see the line cannot gamble against it — they can only guess, and a guess is not
    /// a decision. What the night then did with that split is the morning report's to tell, and
    /// this screen still never marks a split as the right one.
    ///
    /// <b>One action, and no gold.</b> This is a decision screen, so exactly one control is drawn
    /// as the primary: the confirm. The way back is a secondary, which is what it is — an
    /// alternative, not a control the player is being steered away from. Gold is reserved for the
    /// one call to action per screen that opens something new, and on a screen whose whole weight
    /// belongs on the split itself, a gold button would be pulling attention off the decision to
    /// put it on the button that ends the deciding.
    ///
    /// <b>Layout.</b> Everything but the actions lives in one self-measuring column inside a
    /// scroll view: each row is asked how tall it needs to be at the width it was given, so a
    /// wrapping hint or a longer translation pushes the column down instead of landing on top of
    /// what is under it. <see cref="FooterHeight"/> is the only measured constant, because the
    /// actions are pinned outside the scroll view where they can always be reached.
    /// </summary>
    public sealed class EndDayPanel : MonoBehaviour
    {
        /// <summary>Id this screen occupies in the modal stack.</summary>
        public const string ModalId = "end_day";

        /// <summary>The crew the split divides. Read from the day cycle, which owns the number.</summary>
        public const int DefaultCrew = DayCycle.CrewSize;

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

        /// <summary>
        /// Height of the pinned action strip: a token of clearance, the buttons, and a token again.
        /// The only hardcoded vertical measurement on this screen.
        /// </summary>
        static readonly float FooterHeight =
            DesignTokens.Space.S24 * 2f + UIKit.ButtonMinHeight;

        /// <summary>A hairline, never thinner than a device pixel at the shortest supported width.</summary>
        static readonly float HairlineHeight = Mathf.Max(2f, DesignTokens.Px(1f));

        /// <summary>
        /// Where the two actions meet, as a fraction of the strip. The confirm takes the larger
        /// share because it is the primary; both sides still clear the 48 point touch target by a
        /// wide margin at every width this game is laid out for.
        /// </summary>
        const float DeferShare = 0.38f;

        static EndDayPanel _current;

        int _total = DefaultCrew;
        int _workers;
        bool _vigil;
        bool _closed;
        bool _built;

        Action<int, int, bool> _onConfirm;
        Action _onDefer;

        Text _workNumber;
        Text _watchNumber;
        Text _workHint;
        Text _watchHint;
        Text _watchStatus;
        Text _watchRule;
        Image _watchIcon;
        Text _vigilState;

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
            return Open(DayCycle.Find());
        }

        /// <summary>Opens the split and resolves the night on the day cycle that asked for it.</summary>
        public static EndDayPanel Open(DayCycle cycle)
        {
            if (cycle == null)
            {
                return Open(ResolveCrewSize(), (Action<int, int, bool>)null, null);
            }

            // The way back exists only while the day could actually go on, which is the day cycle's
            // to answer and not this screen's to guess.
            Action defer = cycle.CanDeferDusk ? (Action)cycle.CancelDusk : null;
            return Open(ResolveCrewSize(), (workers, watchers, vigil) => cycle.EndDay(workers, watchers, vigil), defer);
        }

        /// <summary>
        /// Opens the split for an explicit crew size. A null confirm callback falls back to the
        /// DayCycle in the scene; a null defer callback means this evening cannot be turned down.
        /// </summary>
        public static EndDayPanel Open(int totalPeople, Action<int, int> onConfirm, Action onDefer)
        {
            Action<int, int, bool> confirm = onConfirm != null
                ? (workers, watchers, vigil) => onConfirm(workers, watchers)
                : (Action<int, int, bool>)null;
            return Open(totalPeople, confirm, onDefer);
        }

        /// <summary>
        /// The split with the vigil in it. A caller that only cares about the two counts uses the
        /// overload above; this one is what the day cycle takes, because the night's work is what
        /// the vigil costs and the day cycle is where the night's work is resolved.
        /// </summary>
        public static EndDayPanel Open(int totalPeople, Action<int, int, bool> onConfirm, Action onDefer)
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
            panel.Build(totalPeople, onConfirm, onDefer);
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

        void Build(int totalPeople, Action<int, int, bool> onConfirm, Action onDefer)
        {
            if (_built)
            {
                return;
            }

            _built = true;
            _onConfirm = onConfirm;
            _onDefer = onDefer;
            _total = Mathf.Max(MinimumPerSide * 2, Mathf.Max(1, totalPeople));
            _workers = ResolveInitialWorkers(_total);
            _vigil = false;

            var container = (RectTransform)transform;

            // Full height rather than a fixed card: this is a screen, and a screen that measures
            // itself cannot be outgrown by a longer translation.
            Image card = UIKit.CreateCard(container, "Card", UIKit.CardStyle.Panel);
            var cardRect = (RectTransform)card.transform;
            UIKit.Stretch(cardRect,
                          DesignTokens.Space.S16, DesignTokens.Space.S16,
                          DesignTokens.Space.S24, DesignTokens.Space.S24);

            RectTransform column;
            ScrollRect scroll = UIKit.CreateScrollView(cardRect, "Sheet", out column);
            UIKit.Stretch((RectTransform)scroll.transform, 0f, 0f, 0f, FooterHeight);
            PrepareScroll(scroll, column);

            // The one Display on this screen. The split's own numbers are Title sized and mono,
            // which is what the design system asks a quantity to be, and two Display elements on
            // one screen would leave neither of them meaning "look here first".
            UIKit.CreateText(column, "Title", Loc.T("end_day.title"),
                DesignTokens.Type.Display, DesignTokens.Ink.Primary,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.Display);

            UIKit.CreateText(column, "Subtitle", Loc.T("end_day.subtitle"),
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.Body);

            BuildSplitRow(column);
            BuildSlider(column);

            // Read through a local so the check is not constant-folded: if the minimum is ever
            // set to zero, this line stops being true and the block stops being unreachable code.
            int minimumPerSide = MinimumPerSide;
            if (minimumPerSide > 0)
            {
                UIKit.CreateText(column, "Bounds", Loc.T("end_day.bounds"),
                    DesignTokens.Type.Mono, DesignTokens.Ink.Muted,
                    TextAnchor.UpperLeft, DesignTokens.TypeRole.Mono);
            }

            BuildVigil(column);
            BuildHints(column);
            BuildWatchStatus(column);

            UIKit.CreateText(column, "Note", Loc.T("end_day.note"),
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.Mono);

            BuildActions(cardRect);

            Refresh();
        }

        /// <summary>
        /// Retunes the kit's scroll view to the design system's rhythm, and gives it something to
        /// be dragged by.
        ///
        /// The drag target is not optional. Every text and icon the kit builds is
        /// <c>raycastTarget = false</c>, so a scroll view full of them has nothing under the
        /// finger: the ray would pass through the whole column and land on the card behind it,
        /// which is not inside the scroll view and would never scroll it. An invisible graphic on
        /// the viewport is Unity's own answer, and it sits behind the content, so the slider on
        /// top of it still receives its own drags.
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
        /// The two sides of the split, side by side, each a card carrying its own count.
        ///
        /// The numbers are mono, which is the design system's rule for every quantity and is what
        /// stops the digits shuffling sideways as the slider moves. The accent lives in a dot
        /// beside the caption rather than in a bar down the card's edge: a straight bar cannot
        /// follow a rounded corner, and a colour that carries a meaning has to arrive with a shape
        /// anyway.
        /// </summary>
        void BuildSplitRow(RectTransform column)
        {
            RectTransform row = UIKit.CreateRect("Split Row", column);
            UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.S16, new RectOffset(), TextAnchor.UpperLeft);

            _workNumber = BuildSplitBox(row, "Work", Loc.T("end_day.split.work"), DesignTokens.Ambient.Growth);
            _watchNumber = BuildSplitBox(row, "Watch", Loc.T("end_day.split.watch"), DesignTokens.Ambient.Sky);
        }

        /// <summary>One side of the split. Returns the label its count is written into.</summary>
        static Text BuildSplitBox(RectTransform parent, string name, string caption, Color accent)
        {
            Image box = UIKit.CreateCard(parent, name, UIKit.CardStyle.Card);
            box.raycastTarget = false;
            var boxRect = (RectTransform)box.transform;

            UIKit.VerticalGroup(box.gameObject, DesignTokens.Space.S8,
                                Pad(DesignTokens.Space.S16, DesignTokens.Space.S16));

            // Half the row each. The preferred width comes from the group inside the card, so the
            // two boxes only share the surplus rather than fighting over their own contents.
            LayoutElement boxLayout = UIKit.Layout(box);
            boxLayout.flexibleWidth = 1f;
            boxLayout.minWidth = DesignTokens.Space.TouchTarget;

            RectTransform captionRow = UIKit.CreateRect("Caption", boxRect);
            UIKit.HorizontalGroup(captionRow.gameObject, DesignTokens.Space.S8, new RectOffset(), TextAnchor.MiddleLeft);

            UIKit.CreateIcon(captionRow, "Dot", UiSpriteKeys.IconDot, accent, UIKit.IconSize);

            Text captionText = UIKit.CreateText(captionRow, "Label", caption,
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted,
                TextAnchor.MiddleLeft, DesignTokens.TypeRole.Mono);
            UIKit.Layout(captionText).flexibleWidth = 1f;

            return UIKit.CreateText(boxRect, "Number", string.Empty,
                DesignTokens.Type.Title, DesignTokens.Ink.Primary,
                TextAnchor.MiddleLeft, DesignTokens.TypeRole.Mono);
        }

        /// <summary>
        /// The one control, at the design system's touch size.
        ///
        /// The kit builds its slider at the width its handle was drawn at, which is under the 48
        /// point floor, so the handle and the area it travels in are both widened here. The height
        /// carries the same floor, and a <see cref="LayoutElement"/> is what tells the surrounding
        /// column about it: a Slider is not a layout element of its own, so without one the column
        /// would size it to nothing and the control would vanish without logging anything.
        /// </summary>
        void BuildSlider(RectTransform column)
        {
            Slider slider = UIKit.CreateSlider(column, "Split", MinimumPerSide, _total - MinimumPerSide,
                                               _workers, OnSliderChanged);

            LayoutElement layout = UIKit.Layout(slider);
            layout.minHeight = DesignTokens.Space.TouchTarget;
            layout.preferredHeight = DesignTokens.Space.TouchTarget;

            RectTransform handle = slider.handleRect;
            if (handle == null)
            {
                return;
            }

            handle.sizeDelta = new Vector2(DesignTokens.Space.TouchTarget, 0f);

            // The travel area is inset by exactly the handle's width, or the handle would run off
            // both ends of the track by half of whatever the two disagreed about.
            var slideArea = handle.parent as RectTransform;
            if (slideArea != null)
            {
                slideArea.sizeDelta = new Vector2(-DesignTokens.Space.TouchTarget, 0f);
            }
        }

        /// <summary>What each side of the split will be doing, in words, under the control.</summary>
        void BuildHints(RectTransform column)
        {
            RectTransform hints = UIKit.CreateRect("Hints", column);
            UIKit.VerticalGroup(hints.gameObject, DesignTokens.Space.S12, new RectOffset());

            _workHint = UIKit.CreateText(hints, "WorkHint", string.Empty,
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.Body);

            _watchHint = UIKit.CreateText(hints, "WatchHint", string.Empty,
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.Body);
        }

        /// <summary>
        /// The vigil: whoever is not on the wall stays up over the page instead of building, and
        /// in the morning the report shows what was written about the day ahead.
        ///
        /// Rule 8 in one row. It is not a prayer button (rule 13 forbids one) and it is not a
        /// power: it returns information and it costs the night's work, and both halves are said
        /// on this screen before the player commits — the bill in plain sight is what makes it a
        /// decision. It is independent of the slider on purpose: the watch is the other half of
        /// NEH.4.9, so a vigil with no watch still loses the wall, and the line under the toggle
        /// says so in the same breath.
        ///
        /// Offered only on a night that has a page to return, which the stage table decides; on
        /// any other night the row is simply not there, so nothing on this screen can promise
        /// what the morning cannot show.
        ///
        /// It sits directly under the slider, above the hints, and says its price in one line. The
        /// first draft put it under the hints with a three-line explanation, and at 1080×1920 the
        /// toggle landed under the pinned footer: reachable by scrolling, invisible on arrival,
        /// and the e2e's tap found the scrim instead of the button. A decision the player has to
        /// scroll to find is not in plain sight.
        /// </summary>
        void BuildVigil(RectTransform column)
        {
            GameState state = TryGetState();
            if (state == null || !DayCycle.VigilOffers(state.day))
            {
                return;
            }

            RectTransform field = UIKit.CreateRect("Vigil", column);
            UIKit.VerticalGroup(field.gameObject, DesignTokens.Space.S8, new RectOffset());

            UIKit.CreateText(field, "VigilLabel", Loc.T("end_day.vigil.label"),
                DesignTokens.Type.Body, DesignTokens.Ink.Primary,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.BodyStrong);

            UIKit.CreateText(field, "VigilCost", Loc.T("end_day.vigil.cost"),
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.Mono);

            // The state, not the action, on the button — the settings toggles set that rule and
            // this row follows it, so a player who has read one knows how to read the other.
            Button toggle = UIKit.CreateButton(field, "VigilToggle", VigilStateLabel(_vigil),
                                               UIKit.ButtonVariant.Secondary, ToggleVigil);
            _vigilState = toggle.GetComponentInChildren<Text>();
        }

        static string VigilStateLabel(bool vigil)
        {
            return vigil ? Loc.T("end_day.vigil.on") : Loc.T("end_day.vigil.off");
        }

        /// <summary>
        /// Where this split falls against the rule, restated on every drag.
        ///
        /// Three signals, as on the morning report, and the same pair of accents: growth when the
        /// watch counts, sky when it does not. Never the error colour and never a warning — the
        /// player has not done anything yet, and a screen that reddened while they were still
        /// choosing would be arguing with them rather than telling them where the line is.
        /// </summary>
        void BuildWatchStatus(RectTransform column)
        {
            RectTransform row = UIKit.CreateRect("Watch Status", column);
            UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.S12, new RectOffset(), TextAnchor.MiddleLeft);

            _watchIcon = UIKit.CreateIcon(row, "Icon", UiSpriteKeys.IconCheck,
                                          DesignTokens.Ambient.Growth, UIKit.IconSize);

            RectTransform lines = UIKit.CreateRect("Lines", row);
            UIKit.VerticalGroup(lines.gameObject, DesignTokens.Space.S4, new RectOffset());
            UIKit.Layout(lines).flexibleWidth = 1f;

            _watchStatus = UIKit.CreateText(lines, "Label", string.Empty,
                DesignTokens.Type.Body, DesignTokens.Ink.Primary,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.BodyStrong);

            _watchRule = UIKit.CreateText(lines, "Rule", string.Empty,
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary,
                TextAnchor.UpperLeft, DesignTokens.TypeRole.Body);
        }

        /// <summary>
        /// The confirm, and the way back when there is one, pinned outside the scroll view so both
        /// are reachable however long the column runs. Exactly one primary, and no gold.
        /// </summary>
        void BuildActions(RectTransform cardRect)
        {
            RectTransform footer = UIKit.CreateRect("Footer", cardRect);
            UIKit.AnchorBottom(footer, FooterHeight, 0f, 0f, 0f);

            Image divider = UIKit.CreatePanel(footer, "Divider", DesignTokens.Surface.Border, null);
            UIKit.AnchorTop((RectTransform)divider.transform, HairlineHeight,
                            DesignTokens.Space.Gutter, DesignTokens.Space.Gutter, 0f);
            divider.raycastTarget = false;

            bool deferrable = _onDefer != null;

            if (deferrable)
            {
                Button defer = UIKit.CreateButton(footer, "Defer", Loc.T("end_day.defer"),
                                                  UIKit.ButtonVariant.Secondary, Defer);
                PlaceAction((RectTransform)defer.transform, 0f, DeferShare,
                            DesignTokens.Space.Gutter, DesignTokens.Space.S8);
            }

            Button confirm = UIKit.CreateButton(footer, "Confirm", Loc.T("end_day.confirm"),
                                                UIKit.ButtonVariant.Primary, Confirm);
            PlaceAction((RectTransform)confirm.transform,
                        deferrable ? DeferShare : 0f, 1f,
                        deferrable ? DesignTokens.Space.S8 : DesignTokens.Space.Gutter,
                        DesignTokens.Space.Gutter);
        }

        /// <summary>
        /// Places one action across a fraction of the strip.
        ///
        /// Proportional anchors rather than widths, because the strip's real width is not known
        /// until a layout pass has run and this is built before one has. The two insets meeting in
        /// the middle add up to the design system's clear space between touch targets.
        /// </summary>
        static void PlaceAction(RectTransform rect, float fromFraction, float toFraction,
                                float leftInset, float rightInset)
        {
            rect.anchorMin = new Vector2(fromFraction, 0f);
            rect.anchorMax = new Vector2(toFraction, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(leftInset, DesignTokens.Space.S24);
            rect.offsetMax = new Vector2(-rightInset, DesignTokens.Space.S24 + UIKit.ButtonMinHeight);
        }

        // ------------------------------------------------------------------ interaction

        void OnSliderChanged(int workers)
        {
            _workers = ClampWorkers(workers);
            Refresh();
        }

        void ToggleVigil()
        {
            if (_closed)
            {
                return;
            }

            _vigil = !_vigil;

            if (_vigilState != null)
            {
                _vigilState.text = VigilStateLabel(_vigil);
            }

            Refresh();
        }

        /// <summary>True while the split on screen has the vigil switched on.</summary>
        public bool VigilSelected
        {
            get { return _vigil; }
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
                // The same people, a different night: the hint under the work count is the one
                // place the vigil's price is written next to the number it applies to.
                _workHint.text = _vigil
                    ? Loc.Plural("end_day.vigil_hint", workers)
                    : Loc.Plural("end_day.work_hint", workers);
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
            int threshold = DayCycle.WatchThreshold(_total);
            bool posted = DayCycle.CountsAsWatch(watchers, _total);

            if (_watchStatus != null)
            {
                _watchStatus.text = posted ? Loc.T("end_day.watch_posted") : Loc.T("end_day.watch_none");
            }

            if (_watchRule != null)
            {
                _watchRule.text = Loc.T("end_day.watch_rule", threshold);
            }

            if (_watchIcon != null)
            {
                _watchIcon.sprite = UIKit.GetSprite(posted ? UiSpriteKeys.IconCheck : UiSpriteKeys.IconDot);
                _watchIcon.color = posted ? DesignTokens.Ambient.Growth : DesignTokens.Ambient.Sky;
            }
        }

        /// <summary>
        /// Turns the evening down and goes back to the day. Nothing is spent and nothing is
        /// recorded: this is the player saying they are not finished, which is only ever offered
        /// while that is true.
        /// </summary>
        void Defer()
        {
            if (_closed)
            {
                return;
            }

            Action callback = _onDefer;
            Close();

            if (callback != null)
            {
                callback();
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

            bool vigil = _vigil;
            Action<int, int, bool> callback = _onConfirm;
            Close();

            if (callback != null)
            {
                callback(workers, watchers, vigil);
                return;
            }

            DayCycle cycle = FindFirstObjectByType<DayCycle>();
            if (cycle != null)
            {
                cycle.EndDay(workers, watchers, vigil);
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

        /// <summary>
        /// The crew is a fixed number and not the day's work capacity. It used to be read off
        /// <c>workCapacityMax</c>, which was twelve by coincidence; the day is four courses now and
        /// a split over four people would have put the watch threshold at two.
        /// </summary>
        static int ResolveCrewSize()
        {
            return DayCycle.CrewSize;
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
