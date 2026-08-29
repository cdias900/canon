using System;
using System.Collections.Generic;
using SheepGate.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

// Aliased rather than imported: SheepGate.UI and SheepGate.Art both publish a type called
// ArtKeys, and a plain using of either namespace here would make every unqualified mention
// of that name ambiguous.
using UIKit = SheepGate.UI.UIKit;

namespace SheepGate.Dialogue
{
    /// <summary>
    /// The dialogue bubble, built entirely in code with uGUI. Anchored to the bottom of a portrait
    /// screen, it shows one line at a time: an optional framing line written by us, the line body
    /// (italic when the body is scripture), and a footer carrying the reference.
    ///
    /// Two rules are load bearing here.
    /// 1. The reference sits in the footer at the same type size as every other metadata on the
    ///    bubble. It is visible from the very first line, never hidden and never enlarged.
    /// 2. The "Saber mais" affordance is secondary by construction: metadata type size, muted fill,
    ///    muted label. It must never compete with the line itself.
    /// </summary>
    public class DialogueUI : MonoBehaviour
    {
        /// <summary>Raised when the player taps anywhere on the dialogue while it is on screen.</summary>
        public event Action AdvanceRequested;

        /// <summary>Raised by the secondary affordance: chapter reference, then verse reference.</summary>
        public event Action<string, string> ChapterRequested;

        /// <summary>
        /// Raised with the index of the branch the player tapped, into the list last handed to
        /// <see cref="ShowChoices"/>. Nothing but the authored label of a branch ever reaches the
        /// screen: no node metadata, no scoring, no hint about which resident is telling the truth.
        /// </summary>
        public event Action<int> ChoiceSelected;

        private const int CanvasSortingOrder = 100;
        private const float ReferenceWidth = 1080f;
        private const float ReferenceHeight = 1920f;
        private const float Margin = 28f;
        private const float Padding = 30f;
        private const float Spacing = 14f;
        private const int BodyFontSize = 34;

        /// <summary>
        /// One size for every piece of metadata on the bubble: speaker, reference, secondary label,
        /// advance hint. The reference is not allowed to be smaller or larger than its neighbours.
        /// </summary>
        private const int MetadataFontSize = 26;

        // Keys, not sentences. Resolved through Loc at the point of use so a language change
        // between two builds of this screen picks up the new words.
        private const string MoreButtonKey = "dialogue.read_more";
        private const string AdvanceHintKey = "dialogue.advance_hint";

        /// <summary>Tall enough for a two-line branch label at body size, plus its insets.</summary>
        private const float ChoiceButtonHeight = 108f;

        private const float ChoiceSpacing = 12f;

        private static readonly Color BubbleTint = new Color(0.94f, 0.92f, 0.88f, 1f);
        private static readonly Color BodyColor = new Color(0.13f, 0.12f, 0.10f, 1f);
        private static readonly Color SpeakerColor = new Color(0.27f, 0.24f, 0.20f, 1f);
        private static readonly Color MetadataColor = new Color(0.44f, 0.41f, 0.36f, 1f);
        private static readonly Color SecondaryFill = new Color(0.87f, 0.85f, 0.80f, 1f);

        private bool built;
        private GameObject root;
        private Image bubbleImage;
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

            if (speakerText != null)
            {
                bool hasSpeaker = !string.IsNullOrEmpty(speaker);
                speakerText.gameObject.SetActive(hasSpeaker);
                speakerText.text = hasSpeaker ? speaker : string.Empty;
            }

            if (frameText != null)
            {
                bool hasFrame = !string.IsNullOrEmpty(frame);
                frameText.gameObject.SetActive(hasFrame);
                frameText.text = hasFrame ? frame : string.Empty;
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

            Button button = UIKit.CreateButton(
                choicesRoot.transform,
                "Choice" + index,
                string.Empty,
                UIKit.Palette.PanelSoft,
                UIKit.Palette.Parchment,
                () => OnChoiceClicked(captured));

            if (button == null)
            {
                return null;
            }

            // The bubble's vertical group controls child sizes, so an Image alone gives the row no
            // height to work with; the layout element is what makes the button a real row.
            LayoutElement layout = UIKit.Layout(button);
            if (layout != null)
            {
                layout.minHeight = ChoiceButtonHeight;
                layout.preferredHeight = ChoiceButtonHeight;
                layout.flexibleWidth = 1f;
            }

            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                // Body size, not button size: a branch is a sentence the player says, and it has to
                // sit at the same weight as the line it answers.
                label.fontSize = UIKit.FontSize.Body;
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

            EnsureEventSystem();
            EnsureCanvas();

            Font font = ResolveFont();

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

            RectTransform bubbleRect = NewRect("Bubble", rootRect);
            bubbleRect.anchorMin = new Vector2(0f, 0f);
            bubbleRect.anchorMax = new Vector2(1f, 0f);
            bubbleRect.pivot = new Vector2(0.5f, 0f);
            bubbleRect.sizeDelta = new Vector2(-2f * Margin, 0f);
            bubbleRect.anchoredPosition = new Vector2(0f, Margin);

            bubbleImage = bubbleRect.gameObject.AddComponent<Image>();
            ApplySprite(bubbleImage, "ui_bubble", BubbleTint);
            bubbleImage.raycastTarget = false;

            VerticalLayoutGroup layout = bubbleRect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)Padding, (int)Padding, (int)Padding, (int)Padding);
            layout.spacing = Spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = bubbleRect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            speakerText = NewText("Speaker", bubbleRect, font, MetadataFontSize, SpeakerColor,
                FontStyle.Bold, TextAnchor.UpperLeft);
            frameText = NewText("Frame", bubbleRect, font, BodyFontSize, BodyColor,
                FontStyle.Normal, TextAnchor.UpperLeft);
            bodyText = NewText("Body", bubbleRect, font, BodyFontSize, BodyColor,
                FontStyle.Normal, TextAnchor.UpperLeft);

            BuildFooter(bubbleRect, font);
            BuildChoices(bubbleRect);

            hintText = NewText("AdvanceHint", bubbleRect, font, MetadataFontSize, MetadataColor,
                FontStyle.Normal, TextAnchor.MiddleRight);
            hintText.text = Loc.T(AdvanceHintKey);
            hintText.gameObject.SetActive(false);

            root.SetActive(false);
        }

        private void BuildFooter(RectTransform bubbleRect, Font font)
        {
            RectTransform footerRect = NewRect("Footer", bubbleRect);
            footer = footerRect.gameObject;

            HorizontalLayoutGroup row = footerRect.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 16f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            refText = NewText("Reference", footerRect, font, MetadataFontSize, MetadataColor,
                FontStyle.Normal, TextAnchor.MiddleLeft);
            LayoutElement refLayout = refText.gameObject.AddComponent<LayoutElement>();
            refLayout.flexibleWidth = 1f;
            refLayout.minHeight = 56f;

            RectTransform buttonRect = NewRect("MoreButton", footerRect);
            Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
            ApplySprite(buttonImage, "ui_button", SecondaryFill);
            buttonImage.raycastTarget = true;

            moreButton = buttonRect.gameObject.AddComponent<Button>();
            moreButton.targetGraphic = buttonImage;
            moreButton.onClick.AddListener(OnMoreClicked);

            LayoutElement buttonLayout = buttonRect.gameObject.AddComponent<LayoutElement>();
            buttonLayout.preferredWidth = 210f;
            buttonLayout.preferredHeight = 56f;
            buttonLayout.flexibleWidth = 0f;

            Text buttonLabel = NewText("Label", buttonRect, font, MetadataFontSize, MetadataColor,
                FontStyle.Normal, TextAnchor.MiddleCenter);
            Stretch(buttonLabel.rectTransform);
            buttonLabel.text = Loc.T(MoreButtonKey);
            buttonLabel.raycastTarget = false;

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

            VerticalLayoutGroup column = choicesRoot.AddComponent<VerticalLayoutGroup>();
            column.spacing = ChoiceSpacing;
            column.childAlignment = TextAnchor.UpperCenter;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

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

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            GameObject go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
#if ENABLE_LEGACY_INPUT_MANAGER
            go.AddComponent<StandaloneInputModule>();
#elif ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#endif
        }

        private static void ApplySprite(Image image, string spriteKey, Color tint)
        {
            Sprite sprite = SafeSprite(spriteKey);
            image.sprite = sprite;
            image.color = tint;
            image.type = (sprite != null && sprite.border != Vector4.zero)
                ? Image.Type.Sliced
                : Image.Type.Simple;
        }

        private static Sprite SafeSprite(string spriteKey)
        {
            try
            {
                return SheepGate.Art.ArtLibrary.Get(spriteKey);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Dialogue] ArtLibrary.Get(\"" + spriteKey + "\") failed: " + e.Message);
                return null;
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

        private static Text NewText(
            string name,
            Transform parent,
            Font font,
            int fontSize,
            Color color,
            FontStyle style,
            TextAnchor anchor)
        {
            RectTransform rect = NewRect(name, parent);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = color;
            text.fontStyle = style;
            text.alignment = anchor;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        private static Font ResolveFont()
        {
            Font font = TryBuiltinFont("LegacyRuntime.ttf");
            if (font == null)
            {
                font = TryBuiltinFont("Arial.ttf");
            }

            if (font == null)
            {
                try
                {
                    string[] installed = Font.GetOSInstalledFontNames();
                    if (installed != null && installed.Length > 0)
                    {
                        font = Font.CreateDynamicFontFromOSFont(installed[0], BodyFontSize);
                    }
                }
                catch (Exception)
                {
                    font = null;
                }
            }

            if (font == null)
            {
                Debug.LogWarning("[Dialogue] No usable font was resolved; bubbles will render empty.");
            }

            return font;
        }

        private static Font TryBuiltinFont(string resourceName)
        {
            try
            {
                return Resources.GetBuiltinResource<Font>(resourceName);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
