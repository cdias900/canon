using System;
using System.Collections;
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

        const float CardWidth = 1000f;
        const float CardHeight = 1160f;
        const float SecondaryButtonWidth = 440f;

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

        void Build(string builderName)
        {
            if (_built)
            {
                return;
            }

            _built = true;

            var container = (RectTransform)transform;

            Image card = UIKit.CreatePanel(container, "Card", UIKit.Palette.Panel);
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(CardWidth, CardHeight);
            cardRect.anchoredPosition = Vector2.zero;

            UIKit.VerticalGroup(card.gameObject, 26f, new RectOffset(52, 52, 48, 48));

            Text kicker = UIKit.CreateText(cardRect, "Kicker", Loc.T("vocation.kicker"),
                UIKit.FontSize.Meta, UIKit.Palette.Muted, TextAnchor.UpperLeft);
            SetMinHeight(kicker, 40f);

            Text headline = UIKit.CreateText(cardRect, "Headline", Loc.T("vocation.headline"),
                UIKit.FontSize.Heading, UIKit.Palette.Olive, TextAnchor.UpperLeft);
            SetMinHeight(headline, 64f);

            Image rule = UIKit.CreatePanel(cardRect, "Accent", UIKit.Palette.Olive);
            rule.raycastTarget = false;
            SetMinHeight(rule, 6f);

            Text body = UIKit.CreateText(cardRect, "Body",
                Loc.T("vocation.body"),
                UIKit.FontSize.Body, UIKit.Palette.Parchment, TextAnchor.UpperLeft);
            SetMinHeight(body, 96f);

            BuildRecord(cardRect, builderName);

            RectTransform spacer = UIKit.CreateRect("Spacer", cardRect);
            LayoutElement spacerLayout = UIKit.Layout(spacer);
            if (spacerLayout != null)
            {
                spacerLayout.minHeight = 12f;
                spacerLayout.flexibleHeight = 1f;
            }

            Text caption = UIKit.CreateText(cardRect, "ReaderCaption", Loc.T("vocation.reader_caption", ChapterRef),
                UIKit.FontSize.Meta, UIKit.Palette.Muted, TextAnchor.LowerLeft);
            SetMinHeight(caption, 36f);

            BuildSecondaryRow(cardRect);

            Button continueButton = UIKit.CreateButton(cardRect, "Continue", Loc.T("vocation.continue"),
                UIKit.Palette.Clay, UIKit.Palette.Parchment, Close);
            SetMinHeight(continueButton, 124f);
        }

        /// <summary>
        /// The record itself. A name when the run has one; the player, plainly, when it does not —
        /// this build never asks for a name, so the second line is the one most players will read.
        /// </summary>
        void BuildRecord(RectTransform cardRect, string builderName)
        {
            Image plate = UIKit.CreatePanel(cardRect, "Record", UIKit.Palette.PanelSoft);
            plate.raycastTarget = false;
            var plateRect = (RectTransform)plate.transform;
            UIKit.VerticalGroup(plate.gameObject, 10f, new RectOffset(28, 28, 24, 24));
            SetMinHeight(plate, 150f);

            string record = string.IsNullOrEmpty(builderName)
                ? Loc.T("vocation.record.anonymous")
                : Loc.T("vocation.record.named", builderName);

            Text line = UIKit.CreateText(plateRect, "Line", record,
                UIKit.FontSize.Body, UIKit.Palette.Parchment, TextAnchor.UpperLeft);
            SetMinHeight(line, 52f);

            Text reference = UIKit.CreateText(plateRect, "Reference", RecordRef,
                UIKit.FontSize.Meta, UIKit.Palette.Muted, TextAnchor.UpperLeft);
            SetMinHeight(reference, 32f);
        }

        void BuildSecondaryRow(RectTransform cardRect)
        {
            RectTransform row = UIKit.CreateRect("SecondaryRow", cardRect);
            UIKit.HorizontalGroup(row.gameObject, 16f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft);
            SetMinHeight(row, 100f);

            Button knowMore = UIKit.CreateButton(row, "KnowMore", Loc.T("vocation.know_more"),
                UIKit.Palette.PanelSoft, UIKit.Palette.Parchment, OnKnowMoreClicked);

            LayoutElement layout = UIKit.Layout(knowMore);
            if (layout != null)
            {
                layout.minWidth = SecondaryButtonWidth;
                layout.preferredWidth = SecondaryButtonWidth;
                layout.minHeight = 96f;
                layout.preferredHeight = 96f;
            }

            Text label = knowMore.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontSize = UIKit.FontSize.Meta;
                label.color = UIKit.Palette.Parchment;
            }
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
    /// </summary>
    public sealed class VocationRevealPanel : MonoBehaviour
    {
        /// <summary>Id this screen occupies in the modal stack.</summary>
        public const string ModalId = "vocation_reveal";

        const float CardSideMargin = 40f;
        const float CardTopMargin = 200f;
        const float CardBottomMargin = 200f;

        const float FadeSeconds = 0.55f;
        const float LineDelaySeconds = 0.75f;

        static VocationRevealPanel _current;

        Action _onClosed;
        CanvasGroup _cardGroup;
        CanvasGroup _lineGroup;
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

            Image card = UIKit.CreatePanel(container, "Card", UIKit.Palette.Panel);
            var cardRect = (RectTransform)card.transform;
            UIKit.Stretch(cardRect, CardSideMargin, CardSideMargin, CardTopMargin, CardBottomMargin);
            UIKit.VerticalGroup(card.gameObject, 30f, new RectOffset(56, 56, 64, 56));

            _cardGroup = card.gameObject.AddComponent<CanvasGroup>();

            Text kicker = UIKit.CreateText(cardRect, "Kicker", Loc.T("vocation.reveal.kicker"),
                UIKit.FontSize.Meta, UIKit.Palette.Muted, TextAnchor.UpperLeft);
            SetMinHeight(kicker, 40f);

            Text title = UIKit.CreateText(cardRect, "Name", name,
                UIKit.FontSize.Title, UIKit.Palette.Parchment, TextAnchor.UpperLeft);
            SetMinHeight(title, 84f);

            Image rule = UIKit.CreatePanel(cardRect, "Accent", UIKit.Palette.Clay);
            rule.raycastTarget = false;
            SetMinHeight(rule, 8f);

            Text body = UIKit.CreateText(cardRect, "Line", line,
                UIKit.FontSize.Body, UIKit.Palette.Parchment, TextAnchor.UpperLeft);
            body.lineSpacing = 1.15f;
            SetMinHeight(body, 300f);
            _lineGroup = body.gameObject.AddComponent<CanvasGroup>();

            RectTransform spacer = UIKit.CreateRect("Spacer", cardRect);
            LayoutElement spacerLayout = UIKit.Layout(spacer);
            if (spacerLayout != null)
            {
                spacerLayout.minHeight = 12f;
                spacerLayout.flexibleHeight = 1f;
            }

            Button close = UIKit.CreateButton(cardRect, "Close", Loc.T("vocation.reveal.close"),
                UIKit.Palette.Clay, UIKit.Palette.Parchment, Close);
            SetMinHeight(close, 124f);

            PlayEntrance();
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

        // ------------------------------------------------------------------ entrance

        /// <summary>
        /// The name lands, then the line. Purely a fade: the button is built, placed and usable
        /// from the first frame, and a panel that could not animate is shown whole rather than
        /// left invisible.
        /// </summary>
        void PlayEntrance()
        {
            if (!isActiveAndEnabled)
            {
                SetAlpha(_cardGroup, 1f);
                SetAlpha(_lineGroup, 1f);
                return;
            }

            SetAlpha(_cardGroup, 0f);
            SetAlpha(_lineGroup, 0f);
            StartCoroutine(FadeIn());
        }

        IEnumerator FadeIn()
        {
            yield return Fade(_cardGroup, FadeSeconds);
            yield return new WaitForSecondsRealtime(LineDelaySeconds);
            yield return Fade(_lineGroup, FadeSeconds);
        }

        static IEnumerator Fade(CanvasGroup group, float seconds)
        {
            if (group == null)
            {
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;

                if (group == null)
                {
                    yield break;
                }

                group.alpha = Mathf.Clamp01(elapsed / seconds);
                yield return null;
            }

            if (group != null)
            {
                group.alpha = 1f;
            }
        }

        static void SetAlpha(CanvasGroup group, float alpha)
        {
            if (group != null)
            {
                group.alpha = alpha;
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
