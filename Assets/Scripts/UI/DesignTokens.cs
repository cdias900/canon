using System;
using System.Collections.Generic;
using UnityEngine;

namespace SheepGate.UI
{
    /// <summary>
    /// The Sistema Vale design system, transcribed. This file is the single source of truth for
    /// colour, type, spacing, radius and motion; nothing outside it may invent a value.
    ///
    /// Token names match the design document one for one (color.primary, neutral.500, radius.md)
    /// so a value can be checked against the handoff without a translation step. Where the
    /// document names a token in Portuguese the English name is used here, because this is code.
    ///
    /// Units: the design system is drawn at 390x844, the canvas is laid out at 1080x1920, so every
    /// length is multiplied by <see cref="DesignScale"/> on the way in. Sizes are therefore stored
    /// already converted — a reader comparing against the document should divide, not multiply.
    /// The reference resolution is deliberately not changed to match the document: every existing
    /// layout constant in this project is expressed in 1080-wide units, and moving the reference
    /// would silently rescale all of them at once.
    /// </summary>
    public static class DesignTokens
    {
        /// <summary>Width the design system is drawn at, in device points.</summary>
        public const float DesignWidth = 390f;

        /// <summary>Design points to canvas reference units. 1080 / 390.</summary>
        public const float DesignScale = UIKit.ReferenceWidth / DesignWidth;

        /// <summary>Converts a length written in the design document to canvas reference units.</summary>
        public static float Px(float designPoints)
        {
            return designPoints * DesignScale;
        }

        /// <summary>Same conversion, rounded, for the integer-valued font size API.</summary>
        public static int PxInt(float designPoints)
        {
            return Mathf.RoundToInt(designPoints * DesignScale);
        }

        static Color32 Hex(uint rgb)
        {
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xFF);
        }

        static Color32 Hex(uint rgb, float alpha)
        {
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb,
                               (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));
        }

        // ================================================================== colour

        /// <summary>BRAND · terra e fogo. Clay is the action colour; gold is the accent.</summary>
        public static class Brand
        {
            public static readonly Color Primary = Hex(0xC2643A);
            public static readonly Color PrimaryLight = Hex(0xD2724A);
            public static readonly Color PrimaryDark = Hex(0x9A4B29);

            /// <summary>
            /// The accent the whole system hangs on. Ratified against the smell checklist in
            /// CLAUDE.md rule 13: what that rule bans is "luz dourada" as art direction — a
            /// devotional glow. This is an interaction colour that marks what can be touched,
            /// and it never lights a scene.
            /// </summary>
            public static readonly Color Secondary = Hex(0xE8B44A);
            public static readonly Color SecondaryLight = Hex(0xF3CE83);
            public static readonly Color SecondaryDark = Hex(0x8A6320);

            /// <summary>Pressed state of the gold quest call to action.</summary>
            public static readonly Color SecondaryPressed = Hex(0xC9982F);
        }

        /// <summary>NEUTRAL · areia e pedra, 50 (lightest) to 900 (darkest).</summary>
        public static class Neutral
        {
            public static readonly Color N50 = Hex(0xFBF6EC);
            public static readonly Color N100 = Hex(0xF1E8D8);
            public static readonly Color N300 = Hex(0xD6C4A8);
            public static readonly Color N500 = Hex(0xA98C6E);
            public static readonly Color N700 = Hex(0x6B5A4C);
            public static readonly Color N800 = Hex(0x3A2E24);
            public static readonly Color N900 = Hex(0x17120E);
        }

        /// <summary>AMBIENTE · céu e vegetação. Water and growth, the two colours the world gains.</summary>
        public static class Ambient
        {
            public static readonly Color Sky = Hex(0x4E86A8);
            public static readonly Color SkyLight = Hex(0x8FC0DB);
            public static readonly Color Growth = Hex(0x4E9A6A);
            public static readonly Color GrowthLight = Hex(0x7FBF97);
        }

        /// <summary>
        /// FEEDBACK. Every one of these is paired with an icon or a label at the call site —
        /// the design system's accessibility rule is that colour never carries a state alone.
        /// </summary>
        public static class Feedback
        {
            public static readonly Color Success = Hex(0x4E9A6A);
            public static readonly Color Warning = Hex(0xD9962F);
            public static readonly Color Error = Hex(0xC4472F);
            public static readonly Color Info = Hex(0x4E86A8);
            public static readonly Color Disabled = Hex(0x5A4A3C);

            /// <summary>Opacity a disabled control is drawn at, per the button specification.</summary>
            public const float DisabledOpacity = 0.40f;
        }

        /// <summary>
        /// Surfaces and text, in the roles screens actually ask for. These are the tokens most
        /// call sites want: the ramps above say what colours exist, this says what they are for.
        /// </summary>
        public static class Surface
        {
            /// <summary>Screen background, below everything.</summary>
            public static readonly Color Background = Neutral.N900;

            /// <summary>elev.0 — a panel sitting on the background.</summary>
            public static readonly Color Panel = Hex(0x1E1813);

            /// <summary>elev.1 — a card inside a panel.</summary>
            public static readonly Color Card = Hex(0x2A211A);

            /// <summary>Hairline between surfaces.</summary>
            public static readonly Color Border = Hex(0x2E241C);

            /// <summary>
            /// The pergaminho card of variation 1b, which section 06 of the design document
            /// applies to every screen. Warm, near-white, for anything the player reads at length.
            /// </summary>
            public static readonly Color Scroll = Hex(0xFBF6EC);

            /// <summary>
            /// Veil behind text laid over the game scene. The design system's floor is 72%
            /// opacity; this is above it, because our scene is a lit tilemap rather than key art.
            /// </summary>
            public static readonly Color SceneVeil = Hex(0x17120E, 0.88f);

            /// <summary>Full-screen dim behind a modal.</summary>
            public static readonly Color Scrim = Hex(0x120E0B, 0.78f);
        }

        /// <summary>Text colours by role, all verified against the surface they sit on.</summary>
        public static class Ink
        {
            /// <summary>Body and heading text on a dark surface.</summary>
            public static readonly Color Primary = Hex(0xF4EADA);

            /// <summary>The brighter variant the screen mocks use over a scene.</summary>
            public static readonly Color OnScene = Hex(0xF8F0E2);

            /// <summary>Supporting sentences.</summary>
            public static readonly Color Secondary = Hex(0xB4A08C);

            /// <summary>Labels, captions, eyebrow text.</summary>
            public static readonly Color Muted = Hex(0x8A7462);

            /// <summary>The quietest readable step. Never used for a sentence the player must read.</summary>
            public static readonly Color Faint = Hex(0x6B5A4C);

            /// <summary>Text on clay and on gold respectively.</summary>
            public static readonly Color OnPrimary = Hex(0xFFF7EC);
            public static readonly Color OnSecondary = Hex(0x2E241C);

            /// <summary>Text on a pergaminho card.</summary>
            public static readonly Color OnScroll = Hex(0x2E241C);
            public static readonly Color OnScrollMuted = Hex(0x7A6553);
        }

        // ================================================================== type

        /// <summary>
        /// The three families the design system specifies, bundled as static TrueType instances
        /// under Resources/Fonts.
        ///
        /// Legacy <see cref="UnityEngine.UI.Text"/> takes one Font asset per weight rather than a
        /// family plus a weight, so a role maps to a file. TextMeshPro would collapse these into
        /// one asset, but it needs its Essentials package imported and a fresh clone of this
        /// repository does not have one — the same reason the rest of the kit is legacy uGUI.
        /// </summary>
        public enum TypeRole
        {
            /// <summary>Bricolage Grotesque 800. Screen titles, the moment a chapter closes.</summary>
            Display,

            /// <summary>Bricolage Grotesque 700. Card headings, a character's name, a mission title.</summary>
            Title,

            /// <summary>Manrope 400. Narrative and interface. The default.</summary>
            Body,

            /// <summary>Manrope 700. Emphasis inside body copy, and button labels.</summary>
            BodyStrong,

            /// <summary>IBM Plex Mono 500. Quantities, counts, references. Tabular by design.</summary>
            Mono
        }

        static readonly Dictionary<TypeRole, string> FontFiles = new Dictionary<TypeRole, string>
        {
            { TypeRole.Display,    "Fonts/BricolageGrotesque-ExtraBold" },
            { TypeRole.Title,      "Fonts/BricolageGrotesque-Bold" },
            { TypeRole.Body,       "Fonts/Manrope-Regular" },
            { TypeRole.BodyStrong, "Fonts/Manrope-Bold" },
            { TypeRole.Mono,       "Fonts/IBMPlexMono-Medium" }
        };

        static readonly Dictionary<TypeRole, Font> Loaded = new Dictionary<TypeRole, Font>();
        static readonly HashSet<TypeRole> Warned = new HashSet<TypeRole>();

        /// <summary>
        /// The font asset for a role, or the built-in fallback if it is missing. Never throws and
        /// never logs twice: a missing font must degrade to readable text, not to an exception in
        /// the middle of a screen builder.
        /// </summary>
        public static Font Font(TypeRole role)
        {
            if (Loaded.TryGetValue(role, out Font cached) && cached != null)
            {
                return cached;
            }

            Font font = null;
            if (FontFiles.TryGetValue(role, out string path))
            {
                try
                {
                    font = Resources.Load<Font>(path);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[DesignTokens] Loading '" + path + "' failed: " + exception.Message);
                }
            }

            if (font == null)
            {
                if (Warned.Add(role))
                {
                    Debug.LogWarning("[DesignTokens] No font asset for role " + role + " at Resources/" +
                                     (path ?? "?") + "; falling back to the built-in font. The screen will " +
                                     "render but will not match the design system.");
                }

                font = UIKit.BuiltinFont;
            }

            Loaded[role] = font;
            return font;
        }

        /// <summary>Drops the memoised font lookups. Only needed if the assets are reimported.</summary>
        public static void ClearFontCache()
        {
            Loaded.Clear();
            Warned.Clear();
        }

        /// <summary>
        /// The type scale, already converted to canvas reference units. The design document's own
        /// numbers are in the comments so the two can be compared without arithmetic.
        /// </summary>
        public static class Type
        {
            /// <summary>34/34 — the largest thing on a screen, one per screen.</summary>
            public static readonly int Display = PxInt(34f);
            public const float DisplayLeading = 1.0f;

            /// <summary>21/24 — card and section headings.</summary>
            public static readonly int Title = PxInt(21f);
            public const float TitleLeading = 24f / 21f;

            /// <summary>15/22 — narrative and interface copy.</summary>
            public static readonly int Body = PxInt(15f);
            public const float BodyLeading = 22f / 15f;

            /// <summary>13 — numbers and references.</summary>
            public static readonly int Mono = PxInt(13f);

            /// <summary>
            /// 12 — the design system's absolute floor, anywhere in the game. Nothing may be
            /// smaller than this, which is a rule worth stating because the scale this project
            /// started from had two steps below it.
            /// </summary>
            public static readonly int Minimum = PxInt(12f);
        }

        // ================================================================== space

        /// <summary>Base 4 spacing scale, converted. Screen gutter is <see cref="Gutter"/>.</summary>
        public static class Space
        {
            public static readonly float S4 = Px(4f);
            public static readonly float S8 = Px(8f);
            public static readonly float S12 = Px(12f);
            public static readonly float S16 = Px(16f);
            public static readonly float S20 = Px(20f);
            public static readonly float S24 = Px(24f);
            public static readonly float S32 = Px(32f);

            /// <summary>Screen gutter, left and right. The document specifies 20 to 22.</summary>
            public static readonly float Gutter = Px(21f);

            /// <summary>Extra clearance above the home indicator, on top of the safe area.</summary>
            public static readonly float SafeAreaBottom = Px(22f);

            /// <summary>Minimum touch target, both axes. 48 design points.</summary>
            public static readonly float TouchTarget = Px(48f);

            /// <summary>Clear space required between two touch targets.</summary>
            public static readonly float TouchGap = Px(8f);
        }

        /// <summary>
        /// Corner radii, converted. Buttons are never below <see cref="Md"/>, which is the
        /// document's rule and not a preference.
        /// </summary>
        public static class Radius
        {
            public static readonly float Sm = Px(8f);
            public static readonly float Md = Px(14f);
            public static readonly float Lg = Px(20f);

            /// <summary>Fully rounded. Resolved against the shorter side at the call site.</summary>
            public const float Pill = -1f;
        }

        // ================================================================== motion

        /// <summary>
        /// Durations and curves from section L. Every one of these is short: the design system's
        /// stated risk is a HUD that gets in the way of the scene, and slow motion is how that
        /// happens without anyone deciding it should.
        /// </summary>
        public static class Motion
        {
            /// <summary>Collecting a resource: item to HUD.</summary>
            public const float Collect = 0.20f;

            /// <summary>The counter and bar that follow a collection.</summary>
            public const float BarFill = 0.40f;

            /// <summary>Laying a block.</summary>
            public const float Place = 0.28f;

            /// <summary>Dust after a block lands.</summary>
            public const float Dust = 0.50f;

            /// <summary>A step of a mission completing.</summary>
            public const float StepComplete = 0.32f;

            /// <summary>Crossfade between two world transformation stages. Never a cut.</summary>
            public const float StageCrossfade = 1.20f;

            /// <summary>Reward card entrance. Scale 1 to 1.04 to 1, and no confetti.</summary>
            public const float Reward = 0.26f;

            /// <summary>Reward cards cascade rather than appearing together.</summary>
            public const float RewardStagger = 0.08f;

            public const float ToastIn = 0.22f;
            public const float ToastHold = 1.60f;
            public const float ToastOut = 0.20f;

            /// <summary>Approximation of cubic-bezier(.2,.8,.2,1), the document's placement curve.</summary>
            public static float EaseOutBack(float t)
            {
                t = Mathf.Clamp01(t);
                float inverted = 1f - t;
                return 1f - inverted * inverted * inverted;
            }

            /// <summary>
            /// Whether animation should be suppressed. The design system requires parallax, pulse
            /// and shake to stop under reduced motion while fades and bars keep running, so this
            /// gates the decorative half only.
            /// </summary>
            public static bool ReduceMotion { get; set; }
        }
    }
}
