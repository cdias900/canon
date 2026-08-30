using System;
using System.Collections.Generic;
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
    /// Key strings and key formats are owned by SheepGate.Art.ArtKeys and forwarded from here
    /// rather than rebuilt, so a change to the naming scheme cannot leave the UI asking for keys
    /// the art module stopped generating. What is genuinely UI-only is the clamping and the
    /// facing translation: screens hand over a SheepGate.Player facing, and the two modules order
    /// their facing enums differently.
    ///
    /// The name deliberately differs from the art module's UiSpriteKeys. Two public types sharing one
    /// name across two namespaces compile only until a single file imports both, at which point
    /// every unqualified mention of the name becomes a CS0104 ambiguity.
    /// </summary>
    public static class UiSpriteKeys
    {
        public const string Panel = SheepGate.Art.ArtKeys.UiPanel;
        public const string Bubble = SheepGate.Art.ArtKeys.UiBubble;
        public const string Button = SheepGate.Art.ArtKeys.UiButton;

        /// <summary>
        /// The Sistema Vale frames, one per radius step, plus the pergaminho card of section 06.
        /// All four are nine-slice: the corner is drawn at the radius the token names and survives
        /// being stretched to any panel, which is the whole reason a frame is art and not a value.
        /// </summary>
        public const string FrameSm = SheepGate.Art.ArtKeys.UiFrameSm;
        public const string FrameMd = SheepGate.Art.ArtKeys.UiFrameMd;
        public const string FrameLg = SheepGate.Art.ArtKeys.UiFrameLg;
        public const string FrameScroll = SheepGate.Art.ArtKeys.UiFrameScroll;

        /// <summary>
        /// The 2px ring with a transparent centre. It draws two different things: the focus outline
        /// the design system requires on every button variant, and the hairline border of the
        /// secondary and destructive fills. One sprite for both is deliberate — a border and a
        /// focus ring that disagreed about thickness would be visible the moment they met.
        /// </summary>
        public const string FocusRing = SheepGate.Art.ArtKeys.UiFocusRing;

        /// <summary>Pill track and pill fill, for progress. Never one without the other.</summary>
        public const string BarTrack = SheepGate.Art.ArtKeys.UiBarTrack;
        public const string BarFill = SheepGate.Art.ArtKeys.UiBarFill;

        /// <summary>
        /// The status icons. These are sprites and never characters: none of the three bundled
        /// families carries a check, a cross, a bullet, a hamburger or a padlock, and the design
        /// system's rule is that a missing glyph is never solved by substituting a lookalike.
        /// </summary>
        public const string IconCheck = SheepGate.Art.ArtKeys.IconCheck;
        public const string IconClose = SheepGate.Art.ArtKeys.IconClose;
        public const string IconDot = SheepGate.Art.ArtKeys.IconDot;
        public const string IconArrow = SheepGate.Art.ArtKeys.IconArrow;
        public const string IconMenu = SheepGate.Art.ArtKeys.IconMenu;
        public const string IconLock = SheepGate.Art.ArtKeys.IconLock;

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
        /// The semantic layer over <see cref="DesignTokens"/>: the ten names screens have always
        /// asked for, now resolving to Sistema Vale values.
        ///
        /// Kept as aliases rather than renamed at every call site on purpose. A screen asking for
        /// "the muted text colour" is asking a design question that outlives whichever hex answers
        /// it, and repointing here recoloured all fifteen screens in one move without touching a
        /// single layout. New code should prefer the token names directly — DesignTokens.Ink.Muted
        /// says where the value comes from, and this does not.
        ///
        /// The palette this replaced was three bases plus neutrals and carried the note "no gold,
        /// no glow". Gold is now the system accent; see DesignTokens.Brand.Secondary for why that
        /// does not collide with the smell checklist.
        /// </summary>
        public static class Palette
        {
            /// <summary>Screen background.</summary>
            public static readonly Color Ink = DesignTokens.Surface.Background;

            /// <summary>Primary text on a dark surface.</summary>
            public static readonly Color Parchment = DesignTokens.Ink.Primary;

            /// <summary>Stone and resources — the neutral mid step.</summary>
            public static readonly Color Stone = DesignTokens.Neutral.N500;

            /// <summary>Secondary and caption text.</summary>
            public static readonly Color Muted = DesignTokens.Ink.Secondary;

            /// <summary>elev.0 surface.</summary>
            public static readonly Color Panel = DesignTokens.Surface.Panel;

            /// <summary>elev.1 surface, one step up from <see cref="Panel"/>.</summary>
            public static readonly Color PanelSoft = DesignTokens.Surface.Card;

            /// <summary>The action colour.</summary>
            public static readonly Color Clay = DesignTokens.Brand.Primary;

            /// <summary>Growth and success. Named Olive from when the palette had no green.</summary>
            public static readonly Color Olive = DesignTokens.Ambient.Growth;

            /// <summary>Water, information, night. Named before the token existed.</summary>
            public static readonly Color Night = DesignTokens.Ambient.Sky;

            /// <summary>Dim behind a modal.</summary>
            public static readonly Color Scrim = DesignTokens.Surface.Scrim;

            /// <summary>
            /// The accent. Marks what can be touched, what is new, and where focus is; never
            /// lights a scene and never decorates one.
            /// </summary>
            public static readonly Color Gold = DesignTokens.Brand.Secondary;

            /// <summary>Hairline between surfaces.</summary>
            public static readonly Color Border = DesignTokens.Surface.Border;
        }

        /// <summary>
        /// The type scale this project started from, in reference-resolution pixels. Superseded by
        /// <see cref="DesignTokens.Type"/>; kept because fifteen screens name these constants.
        ///
        /// Two of them are below the design system's floor: <see cref="Meta"/> is 9.4 design points
        /// and <see cref="Small"/> is 8.7, against a stated minimum of 12. The floor is enforced in
        /// <see cref="CreateText"/> rather than by editing these numbers, so a screen still on the
        /// old scale gets readable text today and the constants keep meaning what their call sites
        /// think they mean until each screen is migrated.
        ///
        /// New code asks for <c>DesignTokens.Type.Body</c> and a <c>TypeRole</c>, not for these.
        /// </summary>
        public static class FontSize
        {
            public const int Title = 54;
            public const int Heading = 42;
            public const int Body = 34;
            public const int Button = 36;

            /// <summary>Every piece of metadata uses this size, scripture references included.</summary>
            public const int Meta = 26;

            /// <summary>Captions that must read as secondary, such as a closed city on the map.</summary>
            public const int Small = 24;
        }

        // ------------------------------------------------------------------ design system metrics

        /// <summary>
        /// Minimum height of any button, both axes for an icon button. 48 design points.
        ///
        /// This is a rule and not a preference: the design system's accessibility floor is a 48x48
        /// touch target with 8 of clear space around it, and a control below it is one a thumb
        /// misses. It is also 21 units taller than the size buttons were built at before, which is
        /// why every screen that lays buttons out by hand has to be re-measured, not just recoloured.
        /// </summary>
        public static readonly float ButtonMinHeight = DesignTokens.Space.TouchTarget;

        /// <summary>Horizontal padding inside a button. 18 design points.</summary>
        public static readonly float ButtonPadding = DesignTokens.Px(18f);

        /// <summary>Vertical breathing room around a button label.</summary>
        public static readonly float ButtonVerticalPadding = DesignTokens.Space.S8;

        /// <summary>Diameter of one loading dot in a button's trailing padding.</summary>
        public static readonly float ButtonIndicatorSize = DesignTokens.Px(8f);

        /// <summary>Size of the success check in a button's leading padding.</summary>
        public static readonly float ButtonCheckSize = DesignTokens.Px(16f);

        /// <summary>
        /// How far the focus ring sits outside the control it marks: 2 design points of gap plus
        /// the 2 the ring itself is thick. The design system specifies the offset; the thickness is
        /// what the nine-slice draws inside its own rect, so the rect has to carry both or the gap
        /// disappears under the stroke.
        ///
        /// Taken from the art module rather than converted again here. The ring's corner radius is
        /// baked into the sprite as "a button's radius plus this outset", so a second conversion
        /// that rounded differently would leave the corner tracing a slightly wrong arc.
        /// </summary>
        public static readonly float FocusRingOutset = UiArt.FocusOutset;

        /// <summary>
        /// The design system's icon grid is 24 design points. Taken from the art module, which
        /// draws the glyphs on a larger grid and publishes the size they are meant to be shown at.
        /// </summary>
        public static readonly float IconSize = UiArt.IconDisplaySize;

        /// <summary>Width a button starts at when no layout stretches it.</summary>
        public const float DefaultButtonWidth = 320f;

        /// <summary>
        /// Height of a progress track: the height the bar sprites are drawn at, which is where
        /// their caps come out exactly circular. About 8.7 design points, comfortably over the
        /// design system's floor of 6. The bar stays a pill at any other height — the caps are the
        /// sprite's end slices and stretch with it — so this is the value that saves the thinking,
        /// not the only value allowed.
        /// </summary>
        public static readonly float ProgressBarHeight = UiArt.BarHeight;

        /// <summary>Height of the label-and-fraction row above a progress track.</summary>
        public static readonly float ProgressHeaderHeight = DesignTokens.Px(22f);

        /// <summary>Space reserved for the fraction, so the bar does not resize as digits arrive.</summary>
        public static readonly float ProgressFractionWidth = DesignTokens.Px(40f);

        /// <summary>Full height of a progress component: header, gap, track.</summary>
        public static readonly float ProgressHeight =
            ProgressHeaderHeight + DesignTokens.Space.S8 + ProgressBarHeight;

        /// <summary>Width a progress component starts at when no layout stretches it.</summary>
        public const float DefaultProgressWidth = 480f;

        // ------------------------------------------------------------------ variants

        /// <summary>
        /// The six buttons the design system has, and the only six.
        ///
        /// The set is closed on purpose. A screen that needs a seventh look is nearly always a
        /// screen that is asking the player a question the other six already answer, and the cost
        /// of a one-off button is that nobody can tell any more which control on a screen is the
        /// one that moves the game forward.
        /// </summary>
        public enum ButtonVariant
        {
            /// <summary>Clay. The action that moves the game forward. One per screen is usual.</summary>
            Primary,

            /// <summary>A translucent parchment fill with a hairline border. The alternative.</summary>
            Secondary,

            /// <summary>No fill at all. For the option that costs the player nothing to ignore.</summary>
            Ghost,

            /// <summary>
            /// Gold. The call to action that opens something new.
            ///
            /// <b>At most one per screen.</b> The design system's rule 9, and the reason gold works
            /// at all: it means "this is the new thing", and two of them on one screen mean nothing.
            /// The rule is not enforced in code — a runtime count cannot tell a modal from the
            /// screen behind it — so it is enforced by reading, here and in review.
            /// </summary>
            Quest,

            /// <summary>Outlined in the error colour, filled only once the finger is on it.</summary>
            Destructive,

            /// <summary>
            /// A square with a glyph and no words. Always carries an <see cref="AccessibleLabel"/>:
            /// a player who does not recognise the shape otherwise has nothing to fall back on.
            /// </summary>
            Icon
        }

        /// <summary>The four surfaces a screen can put content on.</summary>
        public enum CardStyle
        {
            /// <summary>elev.0. A region of the screen, sitting on the background.</summary>
            Panel,

            /// <summary>elev.1. A card inside a panel, one radius step tighter so the nesting reads.</summary>
            Card,

            /// <summary>
            /// The pergaminho card of variation 1b, which section 06 of the design document applies
            /// to every screen. This is the project default for anything the player reads at length.
            /// Text on it uses <c>DesignTokens.Ink.OnScroll</c>, never the dark-surface inks.
            /// </summary>
            Scroll,

            /// <summary>
            /// A panel laid over the game scene. Sits on <c>Surface.SceneVeil</c> and nothing else:
            /// the design system's floor for text over the scene is 72% opacity, and our scene is a
            /// lit tilemap rather than key art, so a lighter veil leaves a sentence unreadable over
            /// whichever tile happens to be behind it.
            /// </summary>
            Glass
        }

        /// <summary>
        /// Every colour one button variant uses, across every state it can be in.
        ///
        /// Hover and pressed carry their own label colour because two variants change both at once:
        /// a destructive button is an outline until a finger lands on it and then becomes a filled
        /// one, and a ghost button's label brightens rather than its fill appearing.
        /// </summary>
        public struct ButtonSkin
        {
            public Color Fill;
            public Color FillHover;
            public Color FillPressed;
            public Color Label;
            public Color LabelHover;
            public Color LabelPressed;

            /// <summary>Hairline outline. Transparent means the variant has none.</summary>
            public Color Border;
        }

        /// <summary>
        /// The same colour at a different opacity.
        ///
        /// Opacity is the one modification allowed to a token: the token file does it itself for
        /// <c>Surface.SceneVeil</c> and <c>Surface.Scrim</c>. A new hue is not allowed, which is why
        /// the translucent fills below are parchment at 8% rather than a fifth grey nobody named.
        /// </summary>
        public static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        }

        /// <summary>The colours of one variant. The single place a button's palette is decided.</summary>
        public static ButtonSkin SkinFor(ButtonVariant variant)
        {
            switch (variant)
            {
                case ButtonVariant.Quest:
                    return new ButtonSkin
                    {
                        Fill = DesignTokens.Brand.Secondary,
                        FillHover = DesignTokens.Brand.SecondaryLight,
                        FillPressed = DesignTokens.Brand.SecondaryPressed,
                        Label = DesignTokens.Ink.OnSecondary,
                        LabelHover = DesignTokens.Ink.OnSecondary,
                        LabelPressed = DesignTokens.Ink.OnSecondary,
                        Border = Color.clear
                    };

                case ButtonVariant.Secondary:
                case ButtonVariant.Icon:
                    return new ButtonSkin
                    {
                        Fill = WithAlpha(DesignTokens.Ink.Primary, 0.08f),
                        FillHover = WithAlpha(DesignTokens.Ink.Primary, 0.14f),
                        FillPressed = WithAlpha(DesignTokens.Ink.Primary, 0.05f),
                        Label = DesignTokens.Ink.Primary,
                        LabelHover = DesignTokens.Ink.Primary,
                        LabelPressed = DesignTokens.Ink.Primary,
                        Border = WithAlpha(DesignTokens.Ink.Primary, 0.20f)
                    };

                case ButtonVariant.Ghost:
                    return new ButtonSkin
                    {
                        Fill = Color.clear,
                        FillHover = WithAlpha(DesignTokens.Ink.Primary, 0.06f),
                        FillPressed = WithAlpha(DesignTokens.Ink.Primary, 0.10f),
                        Label = DesignTokens.Ink.Secondary,
                        LabelHover = DesignTokens.Ink.Primary,
                        LabelPressed = DesignTokens.Ink.Primary,
                        Border = Color.clear
                    };

                case ButtonVariant.Destructive:
                    return new ButtonSkin
                    {
                        Fill = Color.clear,
                        FillHover = DesignTokens.Feedback.Error,
                        FillPressed = WithAlpha(DesignTokens.Feedback.Error, 0.80f),
                        Label = DesignTokens.Feedback.Error,
                        LabelHover = DesignTokens.Ink.OnPrimary,
                        LabelPressed = DesignTokens.Ink.OnPrimary,
                        Border = DesignTokens.Feedback.Error
                    };

                default:
                    return new ButtonSkin
                    {
                        Fill = DesignTokens.Brand.Primary,
                        FillHover = DesignTokens.Brand.PrimaryLight,
                        FillPressed = DesignTokens.Brand.PrimaryDark,
                        Label = DesignTokens.Ink.OnPrimary,
                        LabelHover = DesignTokens.Ink.OnPrimary,
                        LabelPressed = DesignTokens.Ink.OnPrimary,
                        Border = Color.clear
                    };
            }
        }

        /// <summary>The frame sprite for a card style. Outer surfaces are Lg, a nested card is Md.</summary>
        public static string FrameFor(CardStyle style)
        {
            switch (style)
            {
                case CardStyle.Card: return UiSpriteKeys.FrameMd;
                case CardStyle.Scroll: return UiSpriteKeys.FrameScroll;
                default: return UiSpriteKeys.FrameLg;
            }
        }

        /// <summary>The surface colour for a card style.</summary>
        public static Color SurfaceFor(CardStyle style)
        {
            switch (style)
            {
                case CardStyle.Card: return DesignTokens.Surface.Card;
                case CardStyle.Scroll: return DesignTokens.Surface.Scroll;
                case CardStyle.Glass: return DesignTokens.Surface.SceneVeil;
                default: return DesignTokens.Surface.Panel;
            }
        }

        /// <summary>
        /// The ink that reads on a card style. Screens should ask rather than assume: the scroll is
        /// the one surface in the game where the dark-surface inks are invisible.
        /// </summary>
        public static Color InkFor(CardStyle style)
        {
            return style == CardStyle.Scroll ? DesignTokens.Ink.OnScroll : DesignTokens.Ink.Primary;
        }

        static Font _font;
        static bool _fontResolved;

        /// <summary>
        /// The font every screen gets unless it asks for a role: Manrope Regular, the design
        /// system's body face.
        ///
        /// This used to be the built-in font, and that is now only the fallback — see
        /// <see cref="BuiltinFont"/>. Routing the default through <see cref="DesignTokens"/> is
        /// what puts the whole game on the design system's type in one step, including the
        /// screens nobody has revisited yet.
        /// </summary>
        public static Font DefaultFont
        {
            get { return DesignTokens.Font(DesignTokens.TypeRole.Body); }
        }

        /// <summary>
        /// The built-in dynamic font. Resolved once; a failure logs and leaves text invisible
        /// rather than throwing into a screen builder.
        ///
        /// Only reached when a bundled font asset is missing, which in practice means the
        /// Resources/Fonts folder did not survive an import. Text stays readable, and the warning
        /// naming the absent role is in the log.
        /// </summary>
        public static Font BuiltinFont
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
        const string SafeAreaName = "SafeArea";

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
        /// The rect every screen should build inside: a child of the canvas held to the device's
        /// safe area, created once per canvas and returned on every later call.
        ///
        /// Callers used to anchor straight to the canvas, which on this project's target hardware
        /// put the top strip under the camera housing and the bottom row under the home indicator.
        /// Anything that genuinely must cover the whole screen — a fade, a scrim — parents outside
        /// this and carries a <see cref="SafeAreaBleed"/> instead.
        /// </summary>
        public static RectTransform SafeArea(Canvas canvas)
        {
            if (canvas == null)
            {
                return null;
            }

            Transform existing = canvas.transform.Find(SafeAreaName);
            if (existing != null)
            {
                return (RectTransform)existing;
            }

            RectTransform rect = CreateRect(SafeAreaName, canvas.transform);
            Stretch(rect);
            rect.gameObject.AddComponent<SafeAreaFitter>();
            return rect;
        }

        /// <summary>Makes a graphic cover the whole screen even inside a safe-area rect.</summary>
        public static T Bleed<T>(T graphic) where T : Component
        {
            if (graphic != null && graphic.GetComponent<SafeAreaBleed>() == null)
            {
                graphic.gameObject.AddComponent<SafeAreaBleed>();
            }

            return graphic;
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

        /// <summary>
        /// A single-line text field on the legacy InputField, to match the rest of this kit. TMP's
        /// input field would drag in the TMP Essentials asset package, which a fresh clone does not
        /// have — the same reason every other control here is legacy uGUI.
        /// </summary>
        public static InputField CreateInputField(Transform parent, string name, string placeholder,
                                                  int characterLimit, Action<string> onChanged)
        {
            Image background = CreatePanel(parent, name, Palette.PanelSoft);
            var rect = (RectTransform)background.transform;

            Text text = CreateText(rect, "Text", string.Empty, FontSize.Body, Palette.Parchment, TextAnchor.MiddleLeft);
            var textRect = (RectTransform)text.transform;
            Stretch(textRect);
            textRect.offsetMin = new Vector2(20f, 0f);
            textRect.offsetMax = new Vector2(-20f, 0f);
            text.supportRichText = false;

            Text hint = CreateText(rect, "Placeholder", placeholder, FontSize.Body, Palette.Muted, TextAnchor.MiddleLeft);
            var hintRect = (RectTransform)hint.transform;
            Stretch(hintRect);
            hintRect.offsetMin = new Vector2(20f, 0f);
            hintRect.offsetMax = new Vector2(-20f, 0f);
            hint.fontStyle = FontStyle.Italic;

            InputField field = background.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = characterLimit;
            field.lineType = InputField.LineType.SingleLine;

            if (onChanged != null)
            {
                field.onValueChanged.AddListener(value => onChanged(value));
            }

            return field;
        }

        public static Image CreatePanel(Transform parent, string name, Color color, string spriteKey = UiSpriteKeys.Panel)
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

        /// <summary>
        /// A text field in the design system's type.
        ///
        /// The role picks the font file — legacy <see cref="Text"/> takes one asset per weight, not
        /// a family plus a weight — and the leading that goes with it. It defaults to
        /// <c>TypeRole.Body</c>, which is what every existing call site was already getting, so the
        /// six-argument form keeps working and keeps looking the same.
        ///
        /// The size is raised to <c>DesignTokens.Type.Minimum</c> if it is below it. That floor is
        /// stated as a rule and not a suggestion, and enforcing it here rather than at fifteen call
        /// sites is what makes it true of screens nobody has revisited: two steps of the old scale
        /// sat under it, and the smallest of them was 8.7 design points.
        /// </summary>
        public static Text CreateText(Transform parent, string name, string content, int size, Color color, TextAnchor anchor,
                                      DesignTokens.TypeRole role = DesignTokens.TypeRole.Body)
        {
            var go = new GameObject(name, typeof(Text));
            var rect = (RectTransform)go.transform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }

            rect.localScale = Vector3.one;

            Font font = DesignTokens.Font(role);

            Text text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = FloorFontSize(size);
            text.lineSpacing = LineSpacingFor(font, LeadingFor(role));
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

        static readonly HashSet<int> WarnedFontSizes = new HashSet<int>();

        /// <summary>
        /// Raises a size to the design system's floor, warning once per offending size.
        ///
        /// A warning and not an error: an error would fail the e2e run, and the screens still on
        /// the old scale are being migrated one at a time. The log line names the number so the
        /// migration has something to grep for.
        /// </summary>
        public static int FloorFontSize(int size)
        {
            if (size >= DesignTokens.Type.Minimum)
            {
                return size;
            }

            if (WarnedFontSizes.Add(size))
            {
                Debug.LogWarning("[UIKit] Font size " + size + " is below the design system floor of " +
                                 DesignTokens.Type.Minimum + " (12 design points) and was raised. Ask for a " +
                                 "DesignTokens.Type size instead of a UIKit.FontSize one.");
            }

            return DesignTokens.Type.Minimum;
        }

        /// <summary>The design system's leading for a role, as a multiple of the font size.</summary>
        public static float LeadingFor(DesignTokens.TypeRole role)
        {
            switch (role)
            {
                case DesignTokens.TypeRole.Display: return DesignTokens.Type.DisplayLeading;
                case DesignTokens.TypeRole.Title: return DesignTokens.Type.TitleLeading;
                default: return DesignTokens.Type.BodyLeading;
            }
        }

        /// <summary>
        /// Turns a design leading into the number <see cref="Text.lineSpacing"/> actually wants.
        ///
        /// This conversion is the whole point of the method. The design system writes leading the
        /// way type is written everywhere else — 22 over a 15pt body, so 1.47 times the font size —
        /// but Unity's legacy Text multiplies the *font's own* line height, which for these three
        /// families is already about 1.2 times the size. Assigning 1.47 straight across would set
        /// every paragraph in the game at roughly 1.8 times its size, and nothing would say so: the
        /// text stays readable, the blocks just grow and start colliding with what is under them.
        ///
        /// Dividing by the font's natural leading lands on the design's pitch. It also means the
        /// number ends up near 1.0 whenever a family's own leading is close to what the system
        /// asked for, which is why turning this on does not move the existing screens much.
        /// </summary>
        public static float LineSpacingFor(Font font, float leadingEm)
        {
            float natural = AssumedNaturalLeading;
            if (font != null && font.fontSize > 0 && font.lineHeight > 0f)
            {
                natural = font.lineHeight / font.fontSize;
            }

            if (natural <= 0.01f)
            {
                natural = AssumedNaturalLeading;
            }

            // Clamped because a font asset with odd metrics must not be able to stack a paragraph
            // on top of itself or spread it across a screen.
            return Mathf.Clamp(leadingEm / natural, 0.75f, 2f);
        }

        /// <summary>Leading assumed when a font reports no usable metrics. A typical text face.</summary>
        const float AssumedNaturalLeading = 1.2f;

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
            image.sprite = GetSprite(UiSpriteKeys.Button);
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

        /// <summary>
        /// Recolours a button built by the colour-pair overload of <see cref="CreateButton"/>,
        /// label included.
        ///
        /// Not for a button built with a <see cref="ButtonVariant"/>: that one owns its colours
        /// across seven states and repaints them on the next hover, so a tint applied here would
        /// survive exactly until the player's finger moved. A variant button changes its look
        /// through <see cref="SetButtonLoading"/>, <see cref="FlashButtonSuccess"/> or
        /// <c>interactable</c>.
        /// </summary>
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

        // ------------------------------------------------------------------ design system widgets

        /// <summary>
        /// A button in one of the six variants, with every state the design system specifies.
        ///
        /// What this gives a screen over the older colour-pair overload is the states nobody
        /// remembers to build: focus, disabled, loading and success. Focus in particular is a 2px
        /// gold outline at a 2px offset and is <b>identical on all six variants, ghost included</b>
        /// — a ring that changed with the variant would be a ring the player has to learn twice.
        ///
        /// The geometry is not negotiable either: at least <see cref="ButtonMinHeight"/> tall,
        /// <see cref="ButtonPadding"/> either side of the label, and a corner radius that never
        /// drops below Md. A <see cref="LayoutElement"/> carries the height so the button is the
        /// right size inside a layout group as well as outside one.
        ///
        /// Use <see cref="ButtonVariant.Quest"/> at most once per screen. That is the design
        /// system's rule 9, and it is what keeps gold meaning "this is the new thing".
        /// </summary>
        public static Button CreateButton(Transform parent, string name, string label, ButtonVariant variant, Action onClick)
        {
            bool iconOnly = variant == ButtonVariant.Icon;

            var go = new GameObject(name, typeof(Image), typeof(CanvasGroup));
            var rect = (RectTransform)go.transform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }

            rect.localScale = Vector3.one;
            rect.sizeDelta = iconOnly
                ? new Vector2(ButtonMinHeight, ButtonMinHeight)
                : new Vector2(DefaultButtonWidth, ButtonMinHeight);

            Image fill = go.GetComponent<Image>();

            // Md for every variant: the design system's floor for a button, and one radius for all
            // six is what stops a row of mixed variants from looking like a row of mixed controls.
            fill.sprite = GetSprite(UiSpriteKeys.FrameMd);
            fill.type = TypeFor(fill.sprite);
            fill.raycastTarget = true;

            ButtonSkin skin = SkinFor(variant);

            Image border = null;
            if (skin.Border.a > 0.001f)
            {
                // Drawn with the focus-ring sprite rather than a second frame behind the fill: a
                // frame behind a translucent fill shows through it and lightens the fill by exactly
                // the border's own opacity, which is how an 8% parchment ends up at 26%.
                //
                // The ring is drawn at a button's radius plus the focus outset, so pulling it in to
                // the control's own rect leaves its corner arc about two design points wider than
                // the fill it outlines. That is the cost of one outline sprite instead of one per
                // radius, and at a hairline's opacity it is the cheaper of the two mistakes.
                border = CreatePanel(rect, "Border", skin.Border, UiSpriteKeys.FocusRing);
                Stretch((RectTransform)border.transform);
                border.raycastTarget = false;
            }

            Text text = null;
            if (iconOnly)
            {
                if (string.IsNullOrEmpty(label))
                {
                    Debug.LogWarning("[UIKit] Icon button '" + name + "' was built without an accessible " +
                                     "label. An icon-only control always carries one — a player who does not " +
                                     "recognise the glyph has nothing else to go on.");
                }

                AccessibleLabel.Apply(go, label);
            }
            else
            {
                text = CreateText(rect, "Label", label, DesignTokens.Type.Body, skin.Label,
                                  TextAnchor.MiddleCenter, DesignTokens.TypeRole.BodyStrong);
                Stretch((RectTransform)text.transform, ButtonPadding, ButtonPadding,
                        ButtonVerticalPadding, ButtonVerticalPadding);
            }

            Image ring = CreatePanel(rect, "FocusRing", DesignTokens.Brand.Secondary, UiSpriteKeys.FocusRing);

            // Negative insets expand: the ring rect has to hold the 2 points of gap and the 2 the
            // stroke itself occupies, because a nine-slice draws its border inside its own rect.
            Stretch((RectTransform)ring.transform, -FocusRingOutset, -FocusRingOutset, -FocusRingOutset, -FocusRingOutset);
            ring.raycastTarget = false;
            ring.gameObject.SetActive(false);

            var button = go.AddComponent<VariantButton>();
            button.targetGraphic = fill;

            // The base tint is a multiply over the graphic's colour and cannot express "clay becomes
            // clay-light". VariantButton owns the colours outright; this leaves the base nothing to
            // fight it with.
            button.transition = Selectable.Transition.None;
            button.Bind(variant, fill, border, ring, text, go.GetComponent<CanvasGroup>());

            LayoutElement layout = Layout(button);
            layout.minHeight = ButtonMinHeight;
            layout.preferredHeight = ButtonMinHeight;
            if (iconOnly)
            {
                layout.minWidth = ButtonMinHeight;
                layout.preferredWidth = ButtonMinHeight;
            }

            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            return button;
        }

        /// <summary>
        /// A square button carrying one glyph, and the localised name of what it does.
        ///
        /// <paramref name="label"/> is never drawn — it is the accessible name, and it is required.
        /// Building the glyph here rather than leaving it to the caller is what makes that
        /// requirement hard to skip: the only convenient way to get an icon button also asks for
        /// its name.
        /// </summary>
        public static Button CreateIconButton(Transform parent, string name, string iconKey, string label, Action onClick)
        {
            Button button = CreateButton(parent, name, label, ButtonVariant.Icon, onClick);

            Image glyph = CreateIcon(button.transform, "Icon", iconKey, DesignTokens.Ink.Primary, IconSize);
            var glyphRect = (RectTransform)glyph.transform;
            glyphRect.anchorMin = new Vector2(0.5f, 0.5f);
            glyphRect.anchorMax = new Vector2(0.5f, 0.5f);
            glyphRect.pivot = new Vector2(0.5f, 0.5f);
            glyphRect.anchoredPosition = Vector2.zero;

            var variantButton = button as VariantButton;
            if (variantButton != null)
            {
                variantButton.SetGlyph(glyph);
            }

            return button;
        }

        /// <summary>
        /// A surface to put content on, in one of the four styles.
        ///
        /// <see cref="CardStyle.Scroll"/> is the project default: section 06 of the design document
        /// applies the pergaminho card to every screen, and it is the surface anything read at
        /// length belongs on. Ask <see cref="InkFor"/> for the text colour rather than assuming —
        /// the scroll is the one surface where the dark-surface inks vanish.
        ///
        /// <see cref="CardStyle.Glass"/> is the only style that may sit over the game scene, and it
        /// sits on <c>Surface.SceneVeil</c> for the reason the design system gives: a sentence over
        /// a lit tilemap is unreadable at anything thinner.
        /// </summary>
        public static Image CreateCard(Transform parent, string name, CardStyle style)
        {
            return CreatePanel(parent, name, SurfaceFor(style), FrameFor(style));
        }

        /// <summary>
        /// One icon sprite, tinted, square, aspect preserved.
        ///
        /// Square and aspect-preserving together are what keep a 24-grid icon from being stretched
        /// into a different shape by a layout group. A <see cref="LayoutElement"/> carries the size
        /// so a group sizing its children cannot squash it either.
        /// </summary>
        public static Image CreateIcon(Transform parent, string name, string iconKey, Color tint, float size)
        {
            if (size <= 0f)
            {
                size = IconSize;
            }

            var go = new GameObject(name, typeof(Image));
            var rect = (RectTransform)go.transform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }

            rect.localScale = Vector3.one;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);

            Image image = go.GetComponent<Image>();
            image.sprite = GetSprite(iconKey);

            // Simple and not sliced: an icon has no border to preserve, and slicing one would pull
            // its stroke apart at whatever size it happened to be drawn at.
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = tint;
            image.raycastTarget = false;

            LayoutElement layout = Layout(image);
            layout.minWidth = size;
            layout.minHeight = size;
            layout.preferredWidth = size;
            layout.preferredHeight = size;

            return image;
        }

        /// <summary>
        /// Progress, built the only way the design system allows: label, bar and fraction together.
        ///
        /// There is deliberately no overload that omits the label. A bare bar answers nothing —
        /// half of what, and half of how many — and the fraction beside it is what turns a
        /// decoration into a number the player can act on. The fraction is mono so its digits are
        /// tabular and do not shuffle sideways as the value climbs.
        ///
        /// Call <see cref="ProgressBar.SetValue"/> to move it. The transition takes
        /// <c>Motion.BarFill</c> and snaps under reduced motion, which is not the same as freezing:
        /// the value always arrives.
        /// </summary>
        public static ProgressBar CreateProgress(Transform parent, string name, string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                Debug.LogWarning("[UIKit] Progress '" + name + "' was built without a label. The design " +
                                 "system's rule is label, bar and fraction together — a bare bar does not say " +
                                 "what it is measuring.");
            }

            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }

            rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(DefaultProgressWidth, ProgressHeight);

            // A layout group and no ContentSizeFitter: the group is itself a layout element, so a
            // parent group asks it for a preferred height and gets the right one, while a fitter
            // inside a parent group would fight the parent for control of the same rect.
            VerticalGroup(go, DesignTokens.Space.S8, new RectOffset());

            RectTransform header = CreateRect("Header", rect);
            HorizontalGroup(header.gameObject, DesignTokens.Space.S8, new RectOffset(), TextAnchor.MiddleLeft);
            LayoutElement headerLayout = Layout(header);
            headerLayout.minHeight = ProgressHeaderHeight;
            headerLayout.preferredHeight = ProgressHeaderHeight;

            Text labelText = CreateText(header, "Label", label, DesignTokens.Type.Body,
                                        DesignTokens.Ink.Secondary, TextAnchor.MiddleLeft);
            LayoutElement labelLayout = Layout(labelText);
            labelLayout.flexibleWidth = 1f;

            Text fractionText = CreateText(header, "Fraction", string.Empty, DesignTokens.Type.Mono,
                                           DesignTokens.Ink.Primary, TextAnchor.MiddleRight,
                                           DesignTokens.TypeRole.Mono);
            LayoutElement fractionLayout = Layout(fractionText);
            fractionLayout.minWidth = ProgressFractionWidth;
            fractionLayout.flexibleWidth = 0f;

            Image track = CreatePanel(rect, "Track", DesignTokens.Surface.Card, UiSpriteKeys.BarTrack);
            track.raycastTarget = false;
            LayoutElement trackLayout = Layout(track);
            trackLayout.minHeight = ProgressBarHeight;
            trackLayout.preferredHeight = ProgressBarHeight;

            Image fillImage = CreatePanel(track.transform, "Fill", DesignTokens.Brand.Primary, UiSpriteKeys.BarFill);
            fillImage.raycastTarget = false;
            var fillRect = (RectTransform)fillImage.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = Vector2.zero;

            ProgressBar bar = go.AddComponent<ProgressBar>();
            bar.Bind(labelText, fractionText, (RectTransform)track.transform, fillRect);
            return bar;
        }

        /// <summary>
        /// Puts a button built by <see cref="CreateButton(Transform, string, string, ButtonVariant, Action)"/>
        /// into or out of its busy state. Safe on a button built by the older overloads, where it
        /// does nothing.
        /// </summary>
        public static void SetButtonLoading(Button button, bool loading)
        {
            var variantButton = button as VariantButton;
            if (variantButton != null)
            {
                variantButton.SetLoading(loading);
            }
        }

        /// <summary>Shows the success state on a variant button for the toast hold, then returns.</summary>
        public static void FlashButtonSuccess(Button button)
        {
            var variantButton = button as VariantButton;
            if (variantButton != null)
            {
                variantButton.FlashSuccess();
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

            // The viewport needs a graphic, invisible or not, and this is not cosmetic: a drag only
            // reaches a ScrollRect when the raycaster finds a raycast target under the finger and
            // the event bubbles up to it. CreateText sets raycastTarget = false on every label it
            // makes, so a scroll view whose content is nothing but text has nothing to hit — and it
            // does not scroll on touch at all. Every other scroll view here is full of buttons,
            // whose own Image is the target, which is why this went unseen. The one that is all
            // text is the chapter reader, and that is the screen deep_read is measured on: the
            // metric wants 60% of the chapter shown, and an unscrollable chapter never gets there.
            // Fully transparent, so it changes nothing on screen.
            var viewportImage = viewportGo.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0f);
            viewportImage.raycastTarget = true;

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
            barImage.sprite = GetSprite(UiSpriteKeys.Panel);
            barImage.type = TypeFor(barImage.sprite);
            barImage.color = new Color(0f, 0f, 0f, 0.28f);

            RectTransform slidingArea = CreateRect("Sliding Area", barRect);
            slidingArea.anchorMin = Vector2.zero;
            slidingArea.anchorMax = Vector2.one;
            slidingArea.pivot = new Vector2(0.5f, 0.5f);
            slidingArea.offsetMin = Vector2.zero;
            slidingArea.offsetMax = Vector2.zero;

            Image handleImage = CreatePanel(slidingArea, "Handle", Palette.Stone, UiSpriteKeys.Panel);
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

            Image background = CreatePanel(rootRect, "Background", Palette.PanelSoft, UiSpriteKeys.Panel);
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

            Image fill = CreatePanel(fillArea, "Fill", Palette.Olive, UiSpriteKeys.Panel);
            var fillRect = (RectTransform)fill.transform;
            fillRect.sizeDelta = new Vector2(48f, 0f);
            fill.raycastTarget = false;

            RectTransform handleArea = CreateRect("Handle Slide Area", rootRect);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.anchoredPosition = Vector2.zero;
            handleArea.sizeDelta = new Vector2(-88f, 0f);

            Image handle = CreatePanel(handleArea, "Handle", Palette.Parchment, UiSpriteKeys.Button);
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
