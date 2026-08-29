using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

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

        private const string MoreButtonLabel = "Saber mais";
        private const string AdvanceHintLabel = "toque para continuar";

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
            if (footer != null)
            {
                footer.SetActive(hasReference);
            }

            if (refText != null)
            {
                refText.text = hasReference ? refDisplay : string.Empty;
            }

            if (moreButton != null)
            {
                moreButton.gameObject.SetActive(hasReference && !string.IsNullOrEmpty(chapterRef));
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

            hintText = NewText("AdvanceHint", bubbleRect, font, MetadataFontSize, MetadataColor,
                FontStyle.Normal, TextAnchor.MiddleRight);
            hintText.text = AdvanceHintLabel;
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
            buttonLabel.text = MoreButtonLabel;
            buttonLabel.raycastTarget = false;

            footer.SetActive(false);
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
