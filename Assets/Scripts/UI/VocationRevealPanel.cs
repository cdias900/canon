using System;
using System.Collections.Generic;
using SheepGate.Core;
using SheepGate.Scripture;
using SheepGate.Vocation;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The first of the two closing screens of day three: the gate is shut, and the segment says
    /// who repaired it — the one thing Nehemiah 3 does with every name it lists.
    ///
    /// It also carries the only affordance the whole POC is built to measure: a plainly secondary
    /// way into the chapter. It is secondary on purpose — narrow, muted, above the button that
    /// actually moves the game on — because the number is only worth anything if the player opens
    /// the chapter without being pushed. Nothing here opens the reader by itself, nothing waits
    /// for the reader to be opened, and the reading happens inside the app, never in a browser.
    ///
    /// No scripture is written here. Only references travel, and the reader resolves them.
    ///
    /// SURFACE: this is a modal, and the design system puts a modal at elev.2, which it defines as
    /// <c>Surface.Card</c> over <c>Surface.Scrim</c>. <see cref="ModalRoot"/> supplies the scrim,
    /// so the panel itself is a <see cref="UIKit.CardStyle.Card"/>. The pergaminho is deliberately
    /// left to <see cref="VocationRevealPanel"/>, the screen after this one: two consecutive
    /// full-bleed scrolls would spend the surface that is supposed to mark the bigger moment.
    /// </summary>
    public sealed class GateClosedPanel : MonoBehaviour
    {
        /// <summary>Id this screen occupies in the modal stack.</summary>
        public const string ModalId = "gate_closed";

        /// <summary>Chapter the secondary affordance opens, in the in-app reader.</summary>
        public const string ChapterRef = "NEH.4";

        /// <summary>Where the naming of builders comes from. Shown as a reference, never as text.</summary>
        public const string RecordRef = "NEH.3";

        /// <summary>Telemetry trigger, so a player-chosen open is never confused with a prompted one.</summary>
        public const string OpenTrigger = "know_more";

        /// <summary>
        /// The card stretches to the screen gutter rather than carrying a fixed width.
        ///
        /// It used to be 1000 units wide against a 1080 reference, which looked safe and was not:
        /// a 19.5:9 phone laid out at match 0.5 gives a canvas about 976 units across, so the card
        /// was wider than the screen it sat on and lost a strip off both edges. Anchoring to the
        /// gutter is correct at every aspect ratio the game runs at.
        /// </summary>
        static readonly float CardSideMargin = DesignTokens.Space.Gutter;

        /// <summary>Width of the secondary way into the chapter. Narrow on purpose; see the class doc.</summary>
        static readonly float SecondaryButtonWidth = DesignTokens.Px(160f);

        static GateClosedPanel _current;

        Action _onContinue;
        bool _built;
        bool _closed;

        /// <summary>True while the panel is on screen.</summary>
        public static bool IsOpen
        {
            get { return _current != null && !_current._closed; }
        }

        /// <summary>
        /// Shows the panel and calls back once the player moves on, whichever way the panel went
        /// away — the continue button, the hardware back button, or a scene change. Returns null
        /// only when there is no modal root to build into.
        /// </summary>
        public static GateClosedPanel Show(string builderName, Action onContinue)
        {
            if (_current != null && !_current._closed)
            {
                return _current;
            }

            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[GateClosedPanel] No modal root is available; the gate panel cannot open.");
                return null;
            }

            RectTransform container = root.Push(ModalId);
            if (container == null)
            {
                return null;
            }

            var panel = container.gameObject.AddComponent<GateClosedPanel>();
            panel._onContinue = onContinue;
            panel.Build(builderName);
            _current = panel;
            return panel;
        }

        /// <summary>Closes the panel and lets the day carry on.</summary>
        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            ModalRoot.CloseId(ModalId);
            RaiseContinue();
        }

        // ------------------------------------------------------------------ construction

        /// <summary>
        /// Builds the card. Height is left to a <see cref="ContentSizeFitter"/> rather than fixed:
        /// the design system's type is between 1.2 and 1.4 times what this screen was measured at,
        /// the record line is one or two lines depending on whether the run carries a name, and
        /// both of those move again with the locale. A fixed height would have to be right for the
        /// longest of eight combinations, and would be wrong for the other seven.
        /// </summary>
        void Build(string builderName)
        {
            if (_built)
            {
                return;
            }

            _built = true;

            var container = (RectTransform)transform;

            Image card = UIKit.CreateCard(container, "Card", UIKit.CardStyle.Card);
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = new Vector2(0f, 0.5f);
            cardRect.anchorMax = new Vector2(1f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.offsetMin = new Vector2(CardSideMargin, cardRect.offsetMin.y);
            cardRect.offsetMax = new Vector2(-CardSideMargin, cardRect.offsetMax.y);

            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S16,
                Pad(DesignTokens.Space.S24, DesignTokens.Space.S24));

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Eyebrow. Gold is the accent that marks what is new, and this card is dark enough for
            // Brand.Secondary itself — about 8:1 against Surface.Card.
            UIKit.CreateText(cardRect, "Kicker", Loc.T("vocation.kicker"),
                DesignTokens.Type.Mono, DesignTokens.Brand.Secondary, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.BodyStrong);

            UIKit.CreateText(cardRect, "Headline", Loc.T("vocation.headline"),
                DesignTokens.Type.Title, DesignTokens.Ink.Primary, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.Title);

            // A hairline, not a bar. Two design points thick and in the ink's own colour at 14%:
            // the design system's progress track has a floor of six and is always accompanied by a
            // label and a fraction, so nothing this thin and this quiet can be read as progress.
            //
            // Drawn with no sprite at all. The panel art is a nine-slice with a bevel, and a
            // nine-slice squeezed to two points tall renders its borders on top of each other.
            Image rule = UIKit.CreatePanel(cardRect, "Accent",
                UIKit.WithAlpha(DesignTokens.Ink.Primary, 0.14f), null);
            rule.raycastTarget = false;
            SetFixedHeight(rule, DesignTokens.Px(2f));

            UIKit.CreateText(cardRect, "Body", Loc.T("vocation.body"),
                DesignTokens.Type.Body, DesignTokens.Ink.Secondary, TextAnchor.UpperLeft);

            BuildRecord(cardRect, builderName);

            UIKit.CreateText(cardRect, "ReaderCaption", Loc.T("vocation.reader_caption", ScriptureService.ChapterDisplay(ChapterRef)),
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted, TextAnchor.UpperLeft);

            BuildSecondaryRow(cardRect);

            UIKit.CreateButton(cardRect, "Continue", Loc.T("vocation.continue"),
                UIKit.ButtonVariant.Primary, Close);
        }

        /// <summary>
        /// The record itself. A name when the run has one; the player, plainly, when it does not —
        /// this build never asks for a name, so the second line is the one most players will read.
        ///
        /// The plate is a step down in elevation from the card it sits in, and one radius step
        /// tighter, so the nesting reads as nesting rather than as two cards that happen to touch.
        /// </summary>
        void BuildRecord(RectTransform cardRect, string builderName)
        {
            Image plate = UIKit.CreatePanel(cardRect, "Record", DesignTokens.Surface.Panel,
                UiSpriteKeys.FrameSm);
            plate.raycastTarget = false;
            var plateRect = (RectTransform)plate.transform;
            UIKit.VerticalGroup(plate.gameObject, DesignTokens.Space.S8,
                Pad(DesignTokens.Space.S20, DesignTokens.Space.S16));

            string record = string.IsNullOrEmpty(builderName)
                ? Loc.T("vocation.record.anonymous")
                : Loc.T("vocation.record.named", builderName);

            UIKit.CreateText(plateRect, "Line", record,
                DesignTokens.Type.Body, DesignTokens.Ink.Primary, TextAnchor.UpperLeft);

            // Mono, because the design system reserves it for quantities, counts and references,
            // and this is a reference. Never the verse text: only the reference ever travels.
            UIKit.CreateText(plateRect, "Reference", ScriptureService.ChapterDisplay(RecordRef),
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.Mono);
        }

        /// <summary>
        /// The way into the chapter, kept deliberately quiet.
        ///
        /// The design system would make this <see cref="UIKit.ButtonVariant.Quest"/>: gold is "the
        /// call to action that opens something new", and opening a chapter the player has not seen
        /// is exactly that. <b>It stays Secondary anyway.</b> The product metric is the player who
        /// opens the chapter without being pushed, and a gold button is a push — it would move the
        /// number it exists to measure. Do not promote this button.
        /// </summary>
        void BuildSecondaryRow(RectTransform cardRect)
        {
            RectTransform row = UIKit.CreateRect("SecondaryRow", cardRect);
            UIKit.HorizontalGroup(row.gameObject, DesignTokens.Space.S16, new RectOffset(),
                TextAnchor.MiddleLeft);
            SetFixedHeight(row, DesignTokens.Space.TouchTarget);

            Button knowMore = UIKit.CreateButton(row, "KnowMore", Loc.T("vocation.know_more"),
                UIKit.ButtonVariant.Secondary, OnKnowMoreClicked);

            LayoutElement layout = UIKit.Layout(knowMore);
            if (layout != null)
            {
                layout.minWidth = SecondaryButtonWidth;
                layout.preferredWidth = SecondaryButtonWidth;
            }
        }

        /// <summary>Pins a layout child to one height, both as its floor and as its preference.</summary>
        static void SetFixedHeight(Component target, float height)
        {
            LayoutElement layout = UIKit.Layout(target);
            if (layout == null)
            {
                return;
            }

            layout.minHeight = height;
            layout.preferredHeight = height;
        }

        /// <summary>Layout padding from two spacing tokens. RectOffset is integral; tokens are not.</summary>
        static RectOffset Pad(float horizontal, float vertical)
        {
            int h = Mathf.RoundToInt(horizontal);
            int v = Mathf.RoundToInt(vertical);
            return new RectOffset(h, h, v, v);
        }

        // ------------------------------------------------------------------ the reader

        /// <summary>
        /// Opens the chapter inside the app. The panel stays where it is: the reader stacks on top
        /// and the player comes back here when they close it, so reading costs the ending nothing.
        /// </summary>
        void OnKnowMoreClicked()
        {
            ChapterReaderUI reader = ChapterReaderUI.Open(ChapterRef, OpenTrigger);
            if (reader == null)
            {
                Debug.LogWarning("[GateClosedPanel] The chapter reader could not open on " + ChapterRef + ".");
            }
        }

        // ------------------------------------------------------------------ teardown

        void OnDestroy()
        {
            if (_current == this)
            {
                _current = null;
            }

            if (_closed)
            {
                return;
            }

            // Closed from outside — the hardware back button, or the scene going away. Moving on is
            // still the right answer; nothing about this screen is a gate.
            _closed = true;
            RaiseContinue();
        }

        void RaiseContinue()
        {
            Action callback = _onContinue;
            _onContinue = null;

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
                Debug.LogError("[GateClosedPanel] A listener threw while the panel closed: " + exception.Message);
            }
        }
    }

    /// <summary>
    /// The last screen of the POC: the vocation the run earned, named once.
    ///
    /// THE RULE THIS SCREEN EXISTS INSIDE OF: no progress may ever be shown. There is no score
    /// here, no total, no runner-up, no "you were two points from another one", and no bar. The
    /// only thing that ever leaves <see cref="VocationTracker"/> is one id, and all this screen
    /// does with it is read the authored display name and reveal line out of vocations.json.
    ///
    /// It is built as a closing beat rather than a toast: the screen belongs to it, the name
    /// arrives first and the line a moment later, and the way out is worded as going back to the
    /// wall, because the day is not over for the player who wants to keep walking around it.
    ///
    /// DESIGN SYSTEM. This is the one screen in the game that spends <c>Type.Display</c> at full
    /// size, and the one that gets the pergaminho — <see cref="UIKit.CardStyle.Scroll"/> — rather
    /// than the dark modal card every other panel uses. Both are deliberate and both are singular:
    /// the rule is one Display per screen, and the effect of a warm near-white card only survives
    /// while it is the exception.
    ///
    /// Its reference is the design document's "Missão concluída" mock: eyebrow, then a very large
    /// statement, then rows that cascade in at <c>Motion.RewardStagger</c>. <b>The reward rows are
    /// deliberately empty.</b> The mock's rows are counts — stone gained, a step completed — and
    /// there is nothing here that may be counted: naming what the run scored, or how near it came
    /// to another vocation, is precisely what the no-progress rule forbids. The cascade is applied
    /// to the blocks that do exist instead, so the choreography of the mock survives without a
    /// number being invented to fill it.
    ///
    /// Gold on the pergaminho is <c>Brand.SecondaryDark</c>, not <c>Brand.Secondary</c>. The two
    /// are the same accent; the bright one measures about 1.8:1 against <c>Surface.Scroll</c> and
    /// is unreadable there, and the dark one measures about 5:1. Nothing else changes.
    /// </summary>
    public sealed class VocationRevealPanel : MonoBehaviour
    {
        /// <summary>Id this screen occupies in the modal stack.</summary>
        public const string ModalId = "vocation_reveal";

        /// <summary>
        /// The card is nearly the whole screen, which is the point: this is the last beat of the
        /// POC and it is allowed to own the display. The margins are what is left of the scrim.
        /// </summary>
        static readonly float CardSideMargin = DesignTokens.Space.Gutter;
        static readonly float CardTopMargin = DesignTokens.Px(56f);
        static readonly float CardBottomMargin = DesignTokens.Px(40f);

        static VocationRevealPanel _current;

        Action _onClosed;

        /// <summary>The blocks the entrance cascades, in the order they arrive.</summary>
        readonly List<CanvasGroup> _cascade = new List<CanvasGroup>();

        bool _built;
        bool _closed;

        /// <summary>True while the reveal is on screen.</summary>
        public static bool IsOpen
        {
            get { return _current != null && !_current._closed; }
        }

        /// <summary>
        /// Resolves the vocation and shows it. Calls back once the player closes the screen, or
        /// when the screen goes away for any other reason. Returns null only when there is no
        /// modal root to build into.
        /// </summary>
        public static VocationRevealPanel Show(Action onClosed)
        {
            if (_current != null && !_current._closed)
            {
                return _current;
            }

            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[VocationRevealPanel] No modal root is available; the reveal cannot open.");
                return null;
            }

            RectTransform container = root.Push(ModalId);
            if (container == null)
            {
                return null;
            }

            var panel = container.gameObject.AddComponent<VocationRevealPanel>();
            panel._onClosed = onClosed;
            panel.Build();
            _current = panel;
            return panel;
        }

        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            ModalRoot.CloseId(ModalId);
            RaiseClosed();
        }

        // ------------------------------------------------------------------ construction

        void Build()
        {
            if (_built)
            {
                return;
            }

            _built = true;

            VocationDef definition = ResolveVocation();

            string name = definition != null && !string.IsNullOrEmpty(definition.display)
                ? definition.display
                : Loc.T("vocation.reveal.fallback_name");

            string line = definition != null && !string.IsNullOrEmpty(definition.reveal_line)
                ? definition.reveal_line
                : Loc.T("vocation.reveal.fallback_line");

            var container = (RectTransform)transform;

            Image card = UIKit.CreateCard(container, "Card", UIKit.CardStyle.Scroll);
            var cardRect = (RectTransform)card.transform;
            UIKit.Stretch(cardRect, CardSideMargin, CardSideMargin, CardTopMargin, CardBottomMargin);

            // Generous vertical padding and a flexible spacer below the paragraph: the card is
            // stretched rather than fitted, so the slack has to go somewhere that is not between
            // the lines the player is reading.
            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S16,
                Pad(DesignTokens.Space.S24, DesignTokens.Space.S32));

            Text kicker = UIKit.CreateText(cardRect, "Kicker", Loc.T("vocation.reveal.kicker"),
                DesignTokens.Type.Mono, DesignTokens.Brand.SecondaryDark, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.BodyStrong);
            Cascade(kicker);

            // The one Display in the game, at the size the design system draws it. Two lines are
            // allowed for and the layout group measures the real one, so a long name in a long
            // language wraps rather than being cut.
            Text title = UIKit.CreateText(cardRect, "Name", name,
                DesignTokens.Type.Display, DesignTokens.Ink.OnScroll, TextAnchor.UpperLeft,
                DesignTokens.TypeRole.Display);
            SetMinHeight(title, DesignTokens.Type.Display);
            Cascade(title);

            Text body = UIKit.CreateText(cardRect, "Line", line,
                DesignTokens.Type.Body, DesignTokens.Ink.OnScroll, TextAnchor.UpperLeft);
            Cascade(body);

            RectTransform spacer = UIKit.CreateRect("Spacer", cardRect);
            LayoutElement spacerLayout = UIKit.Layout(spacer);
            if (spacerLayout != null)
            {
                spacerLayout.minHeight = 0f;
                spacerLayout.preferredHeight = 0f;
                spacerLayout.flexibleHeight = 1f;
            }

            // The button sits in a row of its own so the cascade has a CanvasGroup to fade that is
            // not the one VariantButton owns for its disabled state. It stays interactable the
            // whole time; only its opacity arrives late.
            RectTransform closeRow = UIKit.CreateRect("CloseRow", cardRect);
            UIKit.VerticalGroup(closeRow.gameObject, 0f, new RectOffset());
            SetMinHeight(closeRow, DesignTokens.Space.TouchTarget);

            UIKit.CreateButton(closeRow, "Close", Loc.T("vocation.reveal.close"),
                UIKit.ButtonVariant.Primary, Close);
            Cascade(closeRow);

            PlayEntrance(title);
        }

        /// <summary>
        /// Asks the tracker for the one thing it will say, then looks the id up in vocations.json.
        /// Nothing but the id crosses the boundary, which is why no score can ever reach a screen.
        /// </summary>
        static VocationDef ResolveVocation()
        {
            string id = null;

            try
            {
                VocationTracker tracker = VocationTracker.EnsureRegistered();
                if (tracker != null)
                {
                    id = tracker.Resolve();
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[VocationRevealPanel] Resolving the vocation failed: " + exception.Message);
                return null;
            }

            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning("[VocationRevealPanel] No vocation was resolved; showing the neutral ending.");
                return null;
            }

            VocationDef[] definitions;
            try
            {
                definitions = GameData.Vocations;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[VocationRevealPanel] Could not read the vocation definitions: " + exception.Message);
                return null;
            }

            if (definitions != null)
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    VocationDef definition = definitions[i];
                    if (definition != null && definition.id == id)
                    {
                        return definition;
                    }
                }
            }

            Debug.LogWarning("[VocationRevealPanel] vocations.json has no entry for '" + id + "'; showing the neutral ending.");
            return null;
        }

        static void SetMinHeight(Component target, float height)
        {
            LayoutElement layout = UIKit.Layout(target);
            if (layout == null)
            {
                return;
            }

            layout.minHeight = height;
        }

        /// <summary>Layout padding from two spacing tokens. RectOffset is integral; tokens are not.</summary>
        static RectOffset Pad(float horizontal, float vertical)
        {
            int h = Mathf.RoundToInt(horizontal);
            int v = Mathf.RoundToInt(vertical);
            return new RectOffset(h, h, v, v);
        }

        // ------------------------------------------------------------------ entrance

        /// <summary>Enrols a block in the entrance cascade, in call order.</summary>
        void Cascade(Component block)
        {
            if (block == null)
            {
                return;
            }

            CanvasGroup group = block.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = block.gameObject.AddComponent<CanvasGroup>();
            }

            _cascade.Add(group);
        }

        /// <summary>
        /// The blocks arrive one after another, a <c>Motion.RewardStagger</c> apart, each over
        /// <c>Motion.Reward</c>. That is the design system's reward-card entrance, applied to the
        /// only screen that has a reward to hand over.
        ///
        /// Every step is a fade, which the design system classes as information rather than as
        /// decoration and therefore keeps running under reduced motion — the whole cascade is
        /// under half a second, and a screen that arrived blank and stayed blank would be worse
        /// than one that arrived at once. The single decorative flourish is the scale bump on the
        /// name, which <see cref="UIMotion.Pulse"/> suppresses when motion is reduced.
        ///
        /// The close button is built, placed and hittable from the first frame regardless: a
        /// CanvasGroup at zero alpha still takes a tap, and nothing about this screen is a gate.
        /// </summary>
        void PlayEntrance(Text title)
        {
            if (!isActiveAndEnabled)
            {
                ShowEverything();
                return;
            }

            for (int i = 0; i < _cascade.Count; i++)
            {
                CanvasGroup group = _cascade[i];
                if (group == null)
                {
                    continue;
                }

                group.alpha = 0f;

                // The first block starts this frame. UIMotion.After always costs a frame before it
                // fires, and a screen that opens on nothing at all reads as a hitch.
                if (i == 0)
                {
                    UIMotion.Fade(group, 1f, DesignTokens.Motion.Reward);
                    continue;
                }

                CanvasGroup captured = group;
                UIMotion.After(this, i * DesignTokens.Motion.RewardStagger,
                    () => UIMotion.Fade(captured, 1f, DesignTokens.Motion.Reward));
            }

            if (title != null)
            {
                UIMotion.After(this, DesignTokens.Motion.RewardStagger,
                    () => UIMotion.Pulse((RectTransform)title.transform, NamePeakScale, DesignTokens.Motion.Reward));
            }
        }

        /// <summary>The design system's reward entrance: 1 to 1.04 to 1, and no confetti.</summary>
        const float NamePeakScale = 1.04f;

        void ShowEverything()
        {
            for (int i = 0; i < _cascade.Count; i++)
            {
                if (_cascade[i] != null)
                {
                    _cascade[i].alpha = 1f;
                }
            }
        }

        // ------------------------------------------------------------------ teardown

        void OnDestroy()
        {
            if (_current == this)
            {
                _current = null;
            }

            if (_closed)
            {
                return;
            }

            _closed = true;
            RaiseClosed();
        }

        void RaiseClosed()
        {
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
                Debug.LogError("[VocationRevealPanel] A listener threw while the reveal closed: " + exception.Message);
            }
        }
    }
}
