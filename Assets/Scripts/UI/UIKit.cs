using System;
using SheepGate.Art;
using SheepGate.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// The sprite keys the UI layer asks <see cref="ArtLibrary"/> for.
    ///
    /// Key formats are owned by SheepGate.Art.ArtKeys and forwarded from here rather than
    /// rebuilt, so a change to the naming scheme cannot leave the UI asking for keys the art
    /// module stopped generating. The three constants exist because UI code reads better with
    /// ArtKeys.Panel than with the art module's longer spelling.
    /// </summary>
    public static class ArtKeys
    {
        public const string Panel = "ui_panel";
        public const string Bubble = "ui_bubble";
        public const string Button = "ui_button";

        /// <summary>Body layer, idle pose, in the art module's canonical key form.</summary>
        public static string Body(int index, FacingDirection direction, int frame)
        {
            return SheepGate.Art.ArtKeys.Body(Mathf.Clamp(index, 0, 1), ToArtFacing(direction), ArtAnim.Idle, frame);
        }

        public static string Top(int index)
        {
            return SheepGate.Art.ArtKeys.Top(Mathf.Clamp(index, 0, 3));
        }

        public static string Legs(int index)
        {
            return SheepGate.Art.ArtKeys.Legs(Mathf.Clamp(index, 0, 3));
        }

        public static string Accessory(int index)
        {
            return SheepGate.Art.ArtKeys.Accessory(Mathf.Clamp(index, 0, 3));
        }

        /// <summary>
        /// The two enums list the facings in different orders, so this maps by name and never by
        /// cast. FacingDirection is Down, Left, Right, Up; ArtFacing is Down, Up, Left, Right.
        /// </summary>
        public static ArtFacing ToArtFacing(FacingDirection direction)
        {
            switch (direction)
            {
                case FacingDirection.Up: return ArtFacing.Up;
                case FacingDirection.Left: return ArtFacing.Left;
                case FacingDirection.Right: return ArtFacing.Right;
                default: return ArtFacing.Down;
            }
        }
    }

    /// <summary>
    /// Programmatic uGUI builders shared by every screen in the POC.
    ///
    /// Why code and not prefabs: the project rule is that scenes stay near-empty and all UI is
    /// constructed at runtime, so the compiler — not hand-authored YAML with GUID references —
    /// is what verifies the interface exists.
    ///
    /// Deliberately built on the legacy <see cref="Text"/> component rather than TextMeshPro:
    /// TMP needs its imported Essentials asset package to exist, and a fresh clone of this
    /// repository does not have one. Legacy Text needs nothing but a built-in font.
    /// </summary>
    public static class UIKit
    {
        /// <summary>Portrait design resolution every screen is laid out against.</summary>
        public const float ReferenceWidth = 1080f;
        public const float ReferenceHeight = 1920f;

        /// <summary>
        /// Three base colours plus neutrals, matching the palette limit the art rules impose.
        /// No gold, no glow: the look is stone, clay and olive.
        /// </summary>
        public static class Palette
        {
            public static readonly Color Ink = new Color32(0x1B, 0x1A, 0x17, 0xFF);
            public static readonly Color Parchment = new Color32(0xE8, 0xE1, 0xD3, 0xFF);
            public static readonly Color Stone = new Color32(0x7C, 0x76, 0x68, 0xFF);
            public static readonly Color Muted = new Color32(0x9A, 0x93, 0x84, 0xFF);
            public static readonly Color Panel = new Color32(0x24, 0x22, 0x1E, 0xF7);
            public static readonly Color PanelSoft = new Color32(0x38, 0x35, 0x2E, 0xFF);
            public static readonly Color Clay = new Color32(0xA8, 0x55, 0x3A, 0xFF);
            public static readonly Color Olive = new Color32(0x5C, 0x6B, 0x4A, 0xFF);
            public static readonly Color Night = new Color32(0x35, 0x50, 0x6B, 0xFF);
            public static readonly Color Scrim = new Color32(0x00, 0x00, 0x00, 0xBE);
        }

        /// <summary>Type scale in reference-resolution pixels.</summary>
        public static class FontSize
        {
            public const int Title = 54;
            public const int Heading = 42;
            public const int Body = 34;
            public const int Button = 36;

            /// <summary>Every piece of metadata uses this size, scripture references included.</summary>
            public const int Meta = 26;
        }

        static Font _font;
        static bool _fontResolved;

        /// <summary>
        /// The built-in dynamic font. Resolved once; a failure logs and leaves text invisible
        /// rather than throwing into a screen builder.
        /// </summary>
        public static Font DefaultFont
        {
            get
            {
                if (_fontResolved && _font != null)
                {
                    return _font;
                }

                _fontResolved = true;
                _font = LoadBuiltinFont("LegacyRuntime.ttf");

                if (_font == null)
                {
                    _font = LoadBuiltinFont("Arial.ttf");
                }

                if (_font == null)
                {
                    try
                    {
                        _font = Font.CreateDynamicFontFromOSFont("Arial", 32);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[UIKit] Could not create an OS font: " + exception.Message);
                    }
                }

                if (_font == null)
                {
                    Debug.LogError("[UIKit] No usable font was found; UI text will not render.");
                }

                return _font;
            }
        }

        static Font LoadBuiltinFont(string resourceName)
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

        // ------------------------------------------------------------------ sprites

        /// <summary>Never throws and never logs an error twice: a missing key yields a flat colour rect.</summary>
        public static Sprite GetSprite(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            try
            {
                return ArtLibrary.Get(key);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[UIKit] ArtLibrary could not provide '" + key + "': " + exception.Message);
                return null;
            }
        }

        // ------------------------------------------------------------------ canvas and input

        /// <summary>
        /// Portrait-safe overlay canvas: 1080x1920 reference, match 0.5 so a wider or taller phone
        /// scales the whole layout instead of cropping it.
        /// </summary>
        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();
            return canvas;
        }

        /// <summary>
        /// Guarantees exactly one EventSystem exists. The module is picked to match the project's
        /// active input handling, which is why the type is resolved by name: referencing the
        /// Input System module directly would not compile in a legacy-input configuration.
        /// </summary>
        public static EventSystem EnsureEventSystem()
        {
            EventSystem existing = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject("EventSystem");
            EventSystem eventSystem = go.AddComponent<EventSystem>();

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            Type moduleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType != null)
            {
                go.AddComponent(moduleType);
            }
            else
            {
                Debug.LogError("[UIKit] Active input handling is Input System only, but InputSystemUIInputModule was not found. UI will not receive input.");
            }
#else
            go.AddComponent<StandaloneInputModule>();
#endif

            return eventSystem;
        }

        // ------------------------------------------------------------------ rect helpers

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }

            rect.localScale = Vector3.one;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(100f, 100f);
            return rect;
        }

        /// <summary>Fills the parent, inset by the given margins.</summary>
        public static RectTransform Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        /// <summary>Full-width strip pinned to the top of the parent.</summary>
        public static RectTransform AnchorTop(RectTransform rect, float height, float left, float right, float top)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        /// <summary>Full-width strip pinned to the bottom of the parent.</summary>
        public static RectTransform AnchorBottom(RectTransform rect, float height, float left, float right, float bottom)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, bottom + height);
            return rect;
        }

        /// <summary>Fixed-size box pinned to one corner. Corner is (0,0) bottom-left to (1,1) top-right.</summary>
        public static RectTransform AnchorCorner(RectTransform rect, Vector2 corner, Vector2 size, Vector2 margin)
        {
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(
                corner.x <= 0.5f ? margin.x : -margin.x,
                corner.y <= 0.5f ? margin.y : -margin.y);
            return rect;
        }

        // ------------------------------------------------------------------ widgets

        public static Image CreatePanel(Transform parent, string name, Color color, string spriteKey = ArtKeys.Panel)
        {
            var go = new GameObject(name, typeof(Image));
            var rect = (RectTransform)go.transform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }

            rect.localScale = Vector3.one;

            Image image = go.GetComponent<Image>();
            image.sprite = GetSprite(spriteKey);
            image.type = TypeFor(image.sprite);
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        /// <summary>
        /// Nine-slice when the art carries borders, a plain stretch when it does not. The art
        /// module draws ui_panel, ui_bubble and ui_button with borders precisely so their bevels
        /// survive being stretched to a panel.
        /// </summary>
        public static Image.Type TypeFor(Sprite sprite)
        {
            return sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        }

        /// <summary>Full-screen tap blocker. Also the visual separation between a modal and the world.</summary>
        public static Image CreateScrim(Transform parent, string name)
        {
            Image image = CreatePanel(parent, name, Palette.Scrim, null);
            Stretch((RectTransform)image.transform);
            image.raycastTarget = true;
            return image;
        }

        public static Text CreateText(Transform parent, string name, string content, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(Text));
            var rect = (RectTransform)go.transform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }

            rect.localScale = Vector3.one;

            Text text = go.GetComponent<Text>();
            text.font = DefaultFont;
            text.fontSize = size;
            text.text = content ?? string.Empty;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            // Authored content must never be reinterpreted as markup.
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        public static Button CreateButton(Transform parent, string name, string label, Action onClick)
        {
            return CreateButton(parent, name, label, Palette.Clay, Palette.Parchment, onClick);
        }

        public static Button CreateButton(Transform parent, string name, string label, Color background, Color foreground, Action onClick)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }

            rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(320f, 112f);

            Image image = go.GetComponent<Image>();
            image.sprite = GetSprite(ArtKeys.Button);
            image.type = TypeFor(image.sprite);
            image.color = background;
            image.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
            colors.pressedColor = new Color(0.76f, 0.76f, 0.76f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;

            Text text = CreateText(rect, "Label", label, FontSize.Button, foreground, TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform, 18f, 18f, 8f, 8f);

            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            return button;
        }

        /// <summary>Recolours a button built by <see cref="CreateButton"/>, label included.</summary>
        public static void TintButton(Button button, Color background, Color foreground)
        {
            if (button == null)
            {
                return;
            }

            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = background;
            }

            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = foreground;
            }
        }

        public static void SetButtonLabel(Button button, string label)
        {
            if (button == null)
            {
                return;
            }

            Text text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label ?? string.Empty;
            }
        }

        // ------------------------------------------------------------------ layout groups

        public static VerticalLayoutGroup VerticalGroup(GameObject target, float spacing, RectOffset padding, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var group = target.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset();
            group.childAlignment = alignment;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return group;
        }

        public static HorizontalLayoutGroup HorizontalGroup(GameObject target, float spacing, RectOffset padding, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var group = target.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset();
            group.childAlignment = alignment;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            return group;
        }

        public static LayoutElement Layout(Component target)
        {
            if (target == null)
            {
                return null;
            }

            var existing = target.GetComponent<LayoutElement>();
            return existing != null ? existing : target.gameObject.AddComponent<LayoutElement>();
        }

        // ------------------------------------------------------------------ scrolling

        /// <summary>
        /// Vertical scroll view with a layout-driven content column. The viewport is clipped with
        /// <see cref="RectMask2D"/> so no mask sprite is needed.
        /// </summary>
        public static ScrollRect CreateScrollView(Transform parent, string name, out RectTransform content)
        {
            var rootGo = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
            var rootRect = (RectTransform)rootGo.transform;
            if (parent != null)
            {
                rootRect.SetParent(parent, false);
            }

            rootRect.localScale = Vector3.one;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var viewportRect = (RectTransform)viewportGo.transform;
            viewportRect.SetParent(rootRect, false);
            viewportRect.localScale = Vector3.one;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.pivot = new Vector2(0f, 1f);
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            var contentRect = (RectTransform)contentGo.transform;
            contentRect.SetParent(viewportRect, false);
            contentRect.localScale = Vector3.one;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;
            contentRect.anchoredPosition = Vector2.zero;

            VerticalGroup(contentGo, 20f, new RectOffset(28, 28, 20, 40));

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = rootGo.GetComponent<ScrollRect>();
            scroll.content = contentRect;
            scroll.viewport = viewportRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;

            // The default of 1 is unusably slow on a mouse wheel; touch drag is unaffected.
            scroll.scrollSensitivity = 60f;

            content = contentRect;
            return scroll;
        }

        /// <summary>
        /// Slim always-visible vertical scrollbar. Permanent visibility is deliberate: it never
        /// resizes the viewport, which keeps the reader's scroll measurement honest.
        /// </summary>
        public static Scrollbar AttachVerticalScrollbar(ScrollRect scroll, float width)
        {
            if (scroll == null)
            {
                return null;
            }

            var barGo = new GameObject("Scrollbar", typeof(Image), typeof(Scrollbar));
            var barRect = (RectTransform)barGo.transform;
            barRect.SetParent(scroll.transform, false);
            barRect.localScale = Vector3.one;
            barRect.anchorMin = new Vector2(1f, 0f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 0.5f);
            barRect.sizeDelta = new Vector2(width, 0f);
            barRect.anchoredPosition = Vector2.zero;

            Image barImage = barGo.GetComponent<Image>();
            barImage.sprite = GetSprite(ArtKeys.Panel);
            barImage.type = TypeFor(barImage.sprite);
            barImage.color = new Color(0f, 0f, 0f, 0.28f);

            RectTransform slidingArea = CreateRect("Sliding Area", barRect);
            slidingArea.anchorMin = Vector2.zero;
            slidingArea.anchorMax = Vector2.one;
            slidingArea.pivot = new Vector2(0.5f, 0.5f);
            slidingArea.offsetMin = Vector2.zero;
            slidingArea.offsetMax = Vector2.zero;

            Image handleImage = CreatePanel(slidingArea, "Handle", Palette.Stone, ArtKeys.Panel);
            var handleRect = (RectTransform)handleImage.transform;
            handleRect.sizeDelta = Vector2.zero;

            Scrollbar bar = barGo.GetComponent<Scrollbar>();
            bar.handleRect = handleRect;
            bar.targetGraphic = handleImage;
            bar.direction = Scrollbar.Direction.BottomToTop;

            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            return bar;
        }

        // ------------------------------------------------------------------ slider

        /// <summary>
        /// Whole-number slider built to the same hierarchy the editor's own slider uses, because
        /// <see cref="Slider"/> drives the anchors of its fill and handle and needs both to have a
        /// container rect above them.
        /// </summary>
        public static Slider CreateSlider(Transform parent, string name, int min, int max, int value, Action<int> onChanged)
        {
            if (max < min)
            {
                max = min;
            }

            var rootGo = new GameObject(name, typeof(RectTransform), typeof(Slider));
            var rootRect = (RectTransform)rootGo.transform;
            if (parent != null)
            {
                rootRect.SetParent(parent, false);
            }

            rootRect.localScale = Vector3.one;
            rootRect.sizeDelta = new Vector2(600f, 72f);

            Image background = CreatePanel(rootRect, "Background", Palette.PanelSoft, ArtKeys.Panel);
            var backgroundRect = (RectTransform)background.transform;
            backgroundRect.anchorMin = new Vector2(0f, 0.34f);
            backgroundRect.anchorMax = new Vector2(1f, 0.66f);
            backgroundRect.sizeDelta = Vector2.zero;
            backgroundRect.anchoredPosition = Vector2.zero;

            RectTransform fillArea = CreateRect("Fill Area", rootRect);
            fillArea.anchorMin = new Vector2(0f, 0.34f);
            fillArea.anchorMax = new Vector2(1f, 0.66f);
            fillArea.anchoredPosition = new Vector2(-24f, 0f);
            fillArea.sizeDelta = new Vector2(-48f, 0f);

            Image fill = CreatePanel(fillArea, "Fill", Palette.Olive, ArtKeys.Panel);
            var fillRect = (RectTransform)fill.transform;
            fillRect.sizeDelta = new Vector2(48f, 0f);
            fill.raycastTarget = false;

            RectTransform handleArea = CreateRect("Handle Slide Area", rootRect);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.anchoredPosition = Vector2.zero;
            handleArea.sizeDelta = new Vector2(-88f, 0f);

            Image handle = CreatePanel(handleArea, "Handle", Palette.Parchment, ArtKeys.Button);
            var handleRect = (RectTransform)handle.transform;
            handleRect.sizeDelta = new Vector2(88f, 0f);

            Slider slider = rootGo.GetComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = true;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(Mathf.Clamp(value, min, max));

            if (onChanged != null)
            {
                slider.onValueChanged.AddListener(raw => onChanged(Mathf.RoundToInt(raw)));
            }

            return slider;
        }

        // ------------------------------------------------------------------ misc

        /// <summary>Forces a layout pass so measurements taken right after building are real.</summary>
        public static void RebuildNow(RectTransform target)
        {
            if (target == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(target);
        }

        public static void DestroyChildren(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
            }
        }
    }
}
