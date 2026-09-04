using System;
using System.Collections.Generic;
using SheepGate.Core;
using UnityEngine;
using UnityEngine.UI;

// Aliased rather than imported. This file needs exactly two types from SheepGate.UI, and naming
// them one at a time keeps the token visible at the call site: the point of writing
// DesignTokens.Ink.Muted rather than a hex is that a reader sees where the value came from
// without leaving the line. SafeAreaFitter, the only other one, is written out in full below.
using UIKit = SheepGate.UI.UIKit;
using DesignTokens = SheepGate.UI.DesignTokens;

namespace SheepGate.Dialogue
{
    /// <summary>
    /// The dialogue bubble, built entirely in code with uGUI, in Sistema Vale's type and colour.
    /// Anchored to the bottom of a portrait screen, it shows one line at a time: the speaker's name,
    /// an optional framing line written by us, the line body (italic when the body is scripture),
    /// a footer carrying the reference and the way into the chapter, and the branches when a node
    /// ends in a decision.
    ///
    /// Design mock 05 is what the look is measured against: the name in <c>Brand.Secondary</c>, a
    /// quieter supporting line under it in <c>Ink.Muted</c>, and the body in <c>Ink.OnScene</c> over
    /// a veiled panel. The veil is not a taste decision — the design system's floor for text laid
    /// over the game scene is 72% opacity and our scene is a lit tilemap rather than key art, which
    /// is why the bubble is a <c>CardStyle.Glass</c> and may not be anything else.
    ///
    /// The mock's supporting line is a one-line description of who is speaking. This game has no
    /// such field: the only per-line supporting text the authored data carries is the framing line,
    /// so that is what fills the slot. Adding a role parameter nobody could pass would be correct
    /// code that nothing calls, which is a failure mode this project has already paid for twice.
    ///
    /// Two rules are load bearing here, and both survived the restyle with different numbers.
    /// 1. The reference is metadata and is drawn as metadata: mono, muted, never enlarged, never
    ///    emphasised, never made to look like the line it belongs to. Whether it is printed at all
    ///    is <see cref="ScriptureVisibility"/>'s decision and nothing in this file.
    /// 2. The "Saber mais" affordance is secondary by construction — a Secondary button and never
    ///    the gold Quest one — and it is on screen for every citation from the very first line. It
    ///    must never compete with the line itself: the game never pushes the read (AGENTS.md rule
    ///    20), it only keeps it one tap away.
    /// </summary>
    public class DialogueUI : MonoBehaviour
    {
        /// <summary>Raised when the player taps anywhere on the dialogue while it is on screen.</summary>
        public event Action AdvanceRequested;

        /// <summary>
        /// Raised when the player holds the screen: they want the lines already read.
        ///
        /// The same surface reports both gestures because there is only one surface — the catcher
        /// covers the screen, and adding a second control for this would put a permanent button on
        /// top of the conversation for something asked for rarely.
        /// </summary>
        public event Action HistoryRequested;

        /// <summary>Raised by the secondary affordance: chapter reference, then verse reference.</summary>
        public event Action<string, string> ChapterRequested;

        /// <summary>
        /// Raised with the index of the branch the player tapped, into the list last handed to
        /// <see cref="ShowChoices"/>. Nothing but the authored label of a branch ever reaches the
        /// screen: no node metadata, no scoring, no hint about which resident is telling the truth.
        /// </summary>
        public event Action<int> ChoiceSelected;

        private const int CanvasSortingOrder = 100;

        // Taken from the kit rather than restated: every screen in the game is laid out against the
        // same 1080x1920, and a second copy of the number is a second thing to get wrong.
        private const float ReferenceWidth = UIKit.ReferenceWidth;
        private const float ReferenceHeight = UIKit.ReferenceHeight;

        /// <summary>Screen gutter around the bubble. The design system's 21 design points.</summary>
        private static readonly float Margin = DesignTokens.Space.Gutter;

        /// <summary>
        /// Inset between the bubble's edge and its content. Equal to <c>Radius.Lg</c> on purpose:
        /// the card's corner is drawn at that radius, so a smaller inset would let the first
        /// character of a line sit inside the curve.
        /// </summary>
        private static readonly float Padding = DesignTokens.Space.S20;

        /// <summary>Gap between the bubble's blocks: header, body, footer, branches, hint.</summary>
        private static readonly float Spacing = DesignTokens.Space.S16;

        /// <summary>
        /// Gap between the speaker's name and the framing line under it. Tighter than
        /// <see cref="Spacing"/> because the two are one unit — a name with its caption — and a gap
        /// the size of the others would break them into two unrelated blocks.
        /// </summary>
        private static readonly float HeaderSpacing = DesignTokens.Space.S4;

        /// <summary>Gap between the reference and the affordance that opens the chapter.</summary>
        private static readonly float FooterSpacing = DesignTokens.Space.S12;

        /// <summary>
        /// Width reserved for "Saber mais". Measured against the longer of the two locales at
        /// <c>Type.Body</c> in the bold face, plus the padding the kit puts either side of a button
        /// label, so neither language wraps a two-word control onto two lines.
        /// </summary>
        private static readonly float MoreButtonWidth = DesignTokens.Px(140f);

        /// <summary>
        /// Least height of the footer row when the chapter affordance is not on it. With the button
        /// present the button is taller and decides the row; this is what keeps the reference from
        /// sitting in a hairline of its own when it is alone there.
        /// </summary>
        private static readonly float MetadataRowHeight = DesignTokens.Space.S24;

        // Keys, not sentences. Resolved through Loc at the point of use so a language change
        // between two builds of this screen picks up the new words.
        private const string MoreButtonKey = "dialogue.read_more";
        private const string AdvanceHintKey = "dialogue.advance_hint";

        /// <summary>
        /// Floor for one branch row: two lines of body copy at the design system's leading, plus
        /// the vertical padding a button puts around its own label.
        ///
        /// A floor and not the height. The rows used to be fixed at two lines on the strength of
        /// the longest branch then authored; the data grew past it and a three-line branch in
        /// pt-BR ran out of its box on the phone. A row now takes its height from its own label
        /// and this only stops a one-line branch from making the column look uneven. Never below
        /// the 48 point touch target whatever the arithmetic says — that floor is an accessibility
        /// rule, not a minimum this may undercut.
        /// </summary>
        private static readonly float ChoiceButtonHeight = Mathf.Max(
            UIKit.ButtonMinHeight,
            (2f * DesignTokens.Type.Body * DesignTokens.Type.BodyLeading)
            + (2f * UIKit.ButtonVerticalPadding));

        /// <summary>The clear space the design system requires between two touch targets.</summary>
        private static readonly float ChoiceSpacing = DesignTokens.Space.TouchGap;

        private bool built;
        private GameObject root;
        private Image bubbleImage;
        private GameObject header;
        private Text speakerText;
        private Text frameText;
        private Text bodyText;
        private Text refText;
        private Text hintText;
        private GameObject footer;
        private Button moreButton;
        private DialogueTapCatcher catcher;
        private GameObject choicesRoot;
        private readonly List<Button> choiceButtons = new List<Button>();

        private string fullBody = string.Empty;
        private string currentChapterRef;
        private string currentVerseRef;
        private int lastRevealed = -1;

        public bool IsVisible
        {
            get { return root != null && root.activeSelf; }
        }

        /// <summary>Creates a self-contained dialogue canvas. Safe to call when none exists yet.</summary>
        public static DialogueUI Create()
        {
            GameObject go = new GameObject("DialogueCanvas");
            return go.AddComponent<DialogueUI>();
        }

        /// <summary>Returns the dialogue UI in the scene, creating one when there is none.</summary>
        public static DialogueUI EnsureInstance()
        {
            DialogueUI existing = FindFirstObjectByType<DialogueUI>();
            return existing != null ? existing : Create();
        }

        public void Show()
        {
            Build();
            if (root != null)
            {
                root.SetActive(true);
            }
        }

        public void Hide()
        {
            HideChoices();

            if (root != null)
            {
                root.SetActive(false);
            }

            fullBody = string.Empty;
            currentChapterRef = null;
            currentVerseRef = null;
            lastRevealed = -1;

            if (bodyText != null)
            {
                bodyText.text = string.Empty;
            }

            if (hintText != null)
            {
                hintText.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Prepares one line for display. The body starts fully hidden; the owner reveals it with
        /// <see cref="SetRevealedCharacters"/>. A non-empty <paramref name="refDisplay"/> puts the
        /// footer on screen, and a non-empty <paramref name="chapterRef"/> enables the secondary
        /// affordance that opens the chapter.
        /// </summary>
        public void BeginLine(
            string speaker,
            string frame,
            string body,
            bool italic,
            string refDisplay,
            string verseRef,
            string chapterRef)
        {
            Build();
            HideChoices();

            fullBody = body ?? string.Empty;
            currentChapterRef = chapterRef;
            currentVerseRef = verseRef;
            lastRevealed = -1;

            bool hasSpeaker = !string.IsNullOrEmpty(speaker);
            bool hasFrame = !string.IsNullOrEmpty(frame);

            if (speakerText != null)
            {
                speakerText.gameObject.SetActive(hasSpeaker);
                speakerText.text = hasSpeaker ? speaker : string.Empty;
            }

            if (frameText != null)
            {
                frameText.gameObject.SetActive(hasFrame);
                frameText.text = hasFrame ? frame : string.Empty;
            }

            if (header != null)
            {
                // A header with nothing in it still costs the bubble one gap of Spacing at the top,
                // which does not read as nothing — it reads as a crooked inset.
                header.SetActive(hasSpeaker || hasFrame);
            }

            if (bodyText != null)
            {
                bodyText.fontStyle = italic ? FontStyle.Italic : FontStyle.Normal;
                bodyText.text = string.Empty;
            }

            bool hasReference = !string.IsNullOrEmpty(refDisplay);

            // Two separate questions, and conflating them would be a mistake: whether to PRINT the
            // citation, and whether to OFFER the chapter. Early in the game the first is no and the
            // second is still yes - the reference is withheld from the footer, never taken out of
            // reach, and one tap on "Saber mais" shows the chapter with its name and numbering.
            bool showReference = hasReference && ScriptureVisibility.ReferencesVisible();
            bool offerChapter = hasReference && !string.IsNullOrEmpty(chapterRef);

            if (footer != null)
            {
                // The footer still carries the chapter affordance once the citation is hidden.
                footer.SetActive(showReference || offerChapter);
            }

            if (refText != null)
            {
                refText.text = showReference ? refDisplay : string.Empty;
                refText.gameObject.SetActive(showReference);
            }

            if (moreButton != null)
            {
                moreButton.gameObject.SetActive(offerChapter);
            }

            if (hintText != null)
            {
                hintText.gameObject.SetActive(false);
            }

            SetRevealedCharacters(0);
        }

        /// <summary>
        /// Reveals the first <paramref name="count"/> characters of the body. The hidden remainder
        /// stays in the string as fully transparent rich text so the layout never reflows mid-line.
        /// </summary>
        public void SetRevealedCharacters(int count)
        {
            if (bodyText == null)
            {
                return;
            }

            int clamped = Mathf.Clamp(count, 0, fullBody.Length);
            if (clamped == lastRevealed)
            {
                return;
            }

            lastRevealed = clamped;

            if (clamped >= fullBody.Length)
            {
                bodyText.text = fullBody;
            }
            else
            {
                bodyText.text = fullBody.Substring(0, clamped)
                                + "<color=#00000000>"
                                + fullBody.Substring(clamped)
                                + "</color>";
            }

            if (hintText != null)
            {
                hintText.gameObject.SetActive(clamped >= fullBody.Length);
            }
        }

        /// <summary>
        /// Puts one button per branch under the line, in the order given, and takes the advance
        /// hint away: while branches are on screen, tapping the bubble is no longer what moves the
        /// conversation forward. Labels are authored pt-BR and are shown verbatim.
        ///
        /// Returns false when not a single button could be put on screen, which is the caller's cue
        /// to keep the ordinary tap-to-finish behaviour rather than wait for a tap that can never
        /// come.
        /// </summary>
        public bool ShowChoices(IList<string> labels)
        {
            Build();

            if (choicesRoot == null)
            {
                return false;
            }

            int count = labels != null ? labels.Count : 0;
            int shown = 0;

            for (int i = 0; i < count; i++)
            {
                Button button = i < choiceButtons.Count ? choiceButtons[i] : null;
                if (button == null)
                {
                    button = CreateChoiceButton(i);
                }

                if (button == null)
                {
                    continue;
                }

                UIKit.SetButtonLabel(button, labels[i]);
                button.gameObject.SetActive(true);
                shown++;
            }

            for (int i = count; i < choiceButtons.Count; i++)
            {
                if (choiceButtons[i] != null)
                {
                    choiceButtons[i].gameObject.SetActive(false);
                }
            }

            choicesRoot.SetActive(shown > 0);

            if (shown > 0 && hintText != null)
            {
                hintText.gameObject.SetActive(false);
            }

            return shown > 0;
        }

        /// <summary>Takes every branch button off screen. Safe to call when none is showing.</summary>
        public void HideChoices()
        {
            for (int i = 0; i < choiceButtons.Count; i++)
            {
                if (choiceButtons[i] != null)
                {
                    choiceButtons[i].gameObject.SetActive(false);
                }
            }

            if (choicesRoot != null)
            {
                choicesRoot.SetActive(false);
            }
        }

        private Button CreateChoiceButton(int index)
        {
            if (choicesRoot == null)
            {
                return null;
            }

            // The index is captured in a local so every button keeps its own position, not the
            // loop variable that created it.
            int captured = index;

            // Secondary for every branch, and deliberately not one Primary among them. The design
            // system allows the forward-most choice to be clay, but nothing in the authored data
            // says which branch that is — and painting one of them the action colour would tell the
            // player which answer the game prefers, on a screen whose branches are scored into a
            // vocation the player is never shown. A row of equals is the honest shape.
            Button button = UIKit.CreateButton(
                choicesRoot.transform,
                "Choice" + index,
                string.Empty,
                UIKit.ButtonVariant.Secondary,
                () => OnChoiceClicked(captured));

            if (button == null)
            {
                return null;
            }

            // The kit already gives a button a layout element at the 48 point touch height; a branch
            // is a sentence that wraps, so the row is sized by its label instead: a layout group on
            // the button hands the label the row's width, and the row's preferred height is the
            // wrapped text plus the button's own padding. The border and the focus ring stay
            // stretched over the whole button, outside the group's arithmetic.
            LayoutElement layout = UIKit.Layout(button);
            if (layout != null)
            {
                layout.minHeight = ChoiceButtonHeight;
                layout.preferredHeight = -1f;
                layout.flexibleWidth = 1f;
            }

            Text label = button.GetComponentInChildren<Text>();
            foreach (Transform child in button.transform)
            {
                if (label == null || child != label.transform)
                {
                    UIKit.Layout(child).ignoreLayout = true;
                }
            }

            int horizontal = Mathf.RoundToInt(UIKit.ButtonPadding);
            int vertical = Mathf.RoundToInt(UIKit.ButtonVerticalPadding);
            UIKit.VerticalGroup(button.gameObject, 0f,
                new RectOffset(horizontal, horizontal, vertical, vertical), TextAnchor.MiddleLeft);

            if (label != null)
            {
                // Left aligned, because a branch is a sentence the player says rather than a verb on
                // a control. The old override that forced it to body size is gone: the kit builds
                // every button label at Type.Body already, so a branch now sits at the size of the
                // line it answers by construction instead of by correction.
                label.alignment = TextAnchor.MiddleLeft;
            }

            while (choiceButtons.Count <= index)
            {
                choiceButtons.Add(null);
            }

            choiceButtons[index] = button;
            return button;
        }

        private void OnChoiceClicked(int index)
        {
            Action<int> handler = ChoiceSelected;
            if (handler != null)
            {
                handler(index);
            }
        }

        private void Awake()
        {
            Build();
        }

        private void Build()
        {
            if (built)
            {
                return;
            }

            built = true;

            UIKit.EnsureEventSystem();
            EnsureCanvas();

            RectTransform rootRect = NewRect("DialogueRoot", transform);
            Stretch(rootRect);
            root = rootRect.gameObject;

            RectTransform catcherRect = NewRect("TapCatcher", rootRect);
            Stretch(catcherRect);
            Image catcherImage = catcherRect.gameObject.AddComponent<Image>();
            catcherImage.color = new Color(0f, 0f, 0f, 0f);
            catcherImage.raycastTarget = true;
            catcher = catcherRect.gameObject.AddComponent<DialogueTapCatcher>();
            catcher.Clicked += OnCatcherClicked;
            catcher.LongPressed += OnCatcherLongPressed;

            // The catcher above covers the whole screen so a tap anywhere advances; the bubble
            // does not, because at the bottom of a modern phone "anywhere" includes the strip the
            // home indicator sits in, and text under it cannot be read.
            RectTransform safeRect = NewRect("SafeArea", rootRect);
            Stretch(safeRect);
            safeRect.gameObject.AddComponent<SheepGate.UI.SafeAreaFitter>();

            // Glass, which is the one card style allowed over the game scene: it sits on
            // Surface.SceneVeil at 88%, above the design system's 72% floor for text over a scene.
            bubbleImage = UIKit.CreateCard(safeRect, "Bubble", UIKit.CardStyle.Glass);

            // A card is a raycast target everywhere else in the game, and here that would kill the
            // screen: the catcher is behind the bubble, so a bubble that eats the tap makes the
            // middle of the screen the one place tapping does not advance the line.
            bubbleImage.raycastTarget = false;

            RectTransform bubbleRect = (RectTransform)bubbleImage.transform;
            bubbleRect.anchorMin = new Vector2(0f, 0f);
            bubbleRect.anchorMax = new Vector2(1f, 0f);
            bubbleRect.pivot = new Vector2(0.5f, 0f);
            bubbleRect.sizeDelta = new Vector2(-2f * Margin, 0f);
            bubbleRect.anchoredPosition = new Vector2(0f, Margin);

            UIKit.VerticalGroup(bubbleRect.gameObject, Spacing,
                new RectOffset((int)Padding, (int)Padding, (int)Padding, (int)Padding));

            // The bubble is as tall as its line and no taller, and it grows upward from the bottom
            // because its pivot is there. Nothing here is a fixed height: the type scale decides
            // how many lines a paragraph takes, and a hardcoded height would clip the day the
            // language changed.
            ContentSizeFitter fitter = bubbleRect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildHeader(bubbleRect);

            bodyText = NewText("Body", bubbleRect, DesignTokens.TypeRole.Body,
                DesignTokens.Type.Body, DesignTokens.Ink.OnScene, TextAnchor.UpperLeft);

            // The only markup in the bubble, and it is ours rather than the author's: the reveal
            // hides the tail of the line inside a transparent colour tag so the block never reflows
            // mid-sentence. Everything else here is drawn with rich text off, which is what stops an
            // authored angle bracket from being read as a tag.
            bodyText.supportRichText = true;

            BuildFooter(bubbleRect);
            BuildChoices(bubbleRect);

            hintText = NewText("AdvanceHint", bubbleRect, DesignTokens.TypeRole.Body,
                DesignTokens.Type.Minimum, DesignTokens.Ink.Secondary, TextAnchor.MiddleRight);
            hintText.text = Loc.T(AdvanceHintKey);
            hintText.gameObject.SetActive(false);

            root.SetActive(false);
        }

        /// <summary>
        /// The name and the line under it, as one block. They are grouped rather than laid out
        /// beside the rest so the gap between them can be tighter than the gap between the blocks,
        /// and so a line with neither a speaker nor a framing line takes no room at all.
        /// </summary>
        private void BuildHeader(RectTransform bubbleRect)
        {
            RectTransform headerRect = NewRect("Header", bubbleRect);
            header = headerRect.gameObject;

            UIKit.VerticalGroup(header, HeaderSpacing, new RectOffset());

            // Gold, at the Title role — which the design system names for exactly this: a card
            // heading, a mission title, a character's name. No bold style is applied on top of it:
            // the Title role already resolves to the 700 weight file, and asking legacy Text for
            // bold as well makes Unity synthesise a second, coarser weight over a real one.
            speakerText = NewText("Speaker", headerRect, DesignTokens.TypeRole.Title,
                DesignTokens.Type.Title, DesignTokens.Brand.Secondary, TextAnchor.UpperLeft);

            // Mock 05's one-line description of the speaker, filled by the only supporting text the
            // data has: the authored framing line. Muted, so it reads as the caption of the name
            // above it rather than as the start of what is being said.
            frameText = NewText("Frame", headerRect, DesignTokens.TypeRole.Body,
                DesignTokens.Type.Body, DesignTokens.Ink.Muted, TextAnchor.UpperLeft);
        }

        private void BuildFooter(RectTransform bubbleRect)
        {
            RectTransform footerRect = NewRect("Footer", bubbleRect);
            footer = footerRect.gameObject;

            UIKit.HorizontalGroup(footerRect.gameObject, FooterSpacing, new RectOffset(),
                TextAnchor.MiddleLeft);

            // Mono, because the design system's mono role is for quantities, counts and references,
            // and a chapter-and-verse is a reference. Muted, because it is a label on the line and
            // never the line itself.
            refText = NewText("Reference", footerRect, DesignTokens.TypeRole.Mono,
                DesignTokens.Type.Mono, DesignTokens.Ink.Muted, TextAnchor.MiddleLeft);
            LayoutElement refLayout = UIKit.Layout(refText);
            refLayout.flexibleWidth = 1f;
            refLayout.minHeight = MetadataRowHeight;

            // Secondary and never Quest. Gold would read as the call to action of the screen, and
            // this control is the opposite of that by design: the read is always optional, and a
            // game that pushed it would be measuring people it had pushed. The kit's Secondary
            // variant also brings the two things this button never had — a 48 point touch target
            // and the focus ring every button in the system shares.
            moreButton = UIKit.CreateButton(footerRect, "MoreButton", Loc.T(MoreButtonKey),
                UIKit.ButtonVariant.Secondary, null);

            // Added rather than passed to the builder so the pairing with RemoveListener in
            // OnDestroy stays a pairing: the builder wraps what it is given in a lambda, which
            // nothing can later remove.
            moreButton.onClick.AddListener(OnMoreClicked);

            LayoutElement buttonLayout = UIKit.Layout(moreButton);
            buttonLayout.minWidth = MoreButtonWidth;
            buttonLayout.preferredWidth = MoreButtonWidth;
            buttonLayout.flexibleWidth = 0f;

            footer.SetActive(false);
        }

        /// <summary>
        /// The column the branch buttons live in. It sits below the reference footer so the
        /// reference is never pushed out of sight by a decision, and it stays empty and inactive
        /// until a node actually offers branches.
        /// </summary>
        private void BuildChoices(RectTransform bubbleRect)
        {
            RectTransform choicesRect = NewRect("Choices", bubbleRect);
            choicesRoot = choicesRect.gameObject;

            UIKit.VerticalGroup(choicesRoot, ChoiceSpacing, new RectOffset(), TextAnchor.UpperCenter);
            choicesRoot.SetActive(false);
        }

        private void OnCatcherClicked()
        {
            Action handler = AdvanceRequested;
            if (handler != null)
            {
                handler();
            }
        }

        private void OnCatcherLongPressed()
        {
            Action handler = HistoryRequested;
            if (handler != null)
            {
                handler();
            }
        }

        private void OnMoreClicked()
        {
            Action<string, string> handler = ChapterRequested;
            if (handler != null && !string.IsNullOrEmpty(currentChapterRef))
            {
                handler(currentChapterRef, currentVerseRef);
            }
        }

        private void OnDestroy()
        {
            if (catcher != null)
            {
                catcher.Clicked -= OnCatcherClicked;
            }

            if (moreButton != null)
            {
                moreButton.onClick.RemoveListener(OnMoreClicked);
            }
        }

        private void EnsureCanvas()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// An empty text field in the design system's type, through the kit so the font file for the
        /// role, the leading that goes with it and the 12 point floor are all decided in one place
        /// rather than five times in this file. Words arrive afterwards, from <c>Loc</c> or from the
        /// authored line.
        /// </summary>
        private static Text NewText(
            string name,
            Transform parent,
            DesignTokens.TypeRole role,
            int size,
            Color color,
            TextAnchor anchor)
        {
            return UIKit.CreateText(parent, name, string.Empty, size, color, anchor, role);
        }
    }
}
