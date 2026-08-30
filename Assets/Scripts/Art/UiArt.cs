using UnityEngine;

namespace SheepGate.Art
{
    /// <summary>
    /// Every nine-slice frame, bar and status icon the interface is built out of.
    ///
    /// ------------------------------------------------------------------------------------------
    /// WHY EVERYTHING HERE IS PAINTED WHITE
    /// ------------------------------------------------------------------------------------------
    /// uGUI multiplies a sprite by <c>Image.color</c>, so what reaches the screen is
    /// <c>sprite x tint</c>. The frames used to be painted in <see cref="ArtPalette"/> stone and
    /// clay, which meant a panel asking for <c>Surface.Panel</c> got <c>stone x Surface.Panel</c>
    /// and came out far darker than the token said. No screen could hit a design system colour,
    /// however carefully it asked.
    ///
    /// So the frames carry no colour at all. Their bodies are pure white at full alpha — the
    /// identity of that multiply — and every bevel, border and rounded corner lives in the ALPHA
    /// channel instead. <c>image.color = DesignTokens.Surface.Panel</c> now yields exactly
    /// <c>Surface.Panel</c>, and the same sprite serves a clay button, a gold call to action and
    /// a pergaminho card.
    ///
    /// The cost of that trade is that nothing here can be BRIGHTER than its tint: a multiply has
    /// no way to add light. A highlight is therefore impossible and is not attempted. What the
    /// frames carry instead is a rim of reduced alpha, which lets a little of the surface behind
    /// show through — over this game's dark background that reads as the darker border the design
    /// system asks for. The one exception is the pergaminho card's elevation shadow, which has to
    /// carry its own dark pixels for the same reason and is documented where it is drawn.
    ///
    /// ------------------------------------------------------------------------------------------
    /// WHY THE NEW FRAMES ARE BIG, AND WHAT ONE PIXEL IS WORTH
    /// ------------------------------------------------------------------------------------------
    /// A sliced <c>Image</c> renders its sprite border at <c>border / (sprite.pixelsPerUnit /
    /// canvas.referencePixelsPerUnit)</c> canvas units. This project never sets
    /// <c>referencePixelsPerUnit</c>, so it is Unity's default 100, and the legacy 32-pixel frames
    /// at <see cref="ArtLibrary.PixelsPerUnit"/> = 32 render their 8-pixel corner at 25 units —
    /// close to <c>Radius.Sm</c> by accident, and wrong for everything else.
    ///
    /// The Sistema Vale frames fix that by construction: they are built at
    /// <see cref="PixelsPerUnit"/> = 100, matching the canvas, so ONE SPRITE PIXEL IS ONE CANVAS
    /// REFERENCE UNIT. <see cref="RadiusSm"/>/<see cref="RadiusMd"/>/<see cref="RadiusLg"/> are
    /// therefore literally <c>DesignTokens.Radius.Sm/Md/Lg</c> rounded to whole pixels, the corner
    /// comes out at the radius the design document asks for, and the corner slice renders 1:1 with
    /// no resampling at all.
    ///
    /// This holds only while the canvas is at the default 100 reference pixels per unit and no
    /// <c>Image.pixelsPerUnitMultiplier</c> is set. Both are true today; if either changes, every
    /// radius here scales with it.
    ///
    /// The textures are a few tens of kilobytes each and are generated once, so their size is not
    /// a cost worth optimising against a radius that is visibly wrong.
    ///
    /// ------------------------------------------------------------------------------------------
    /// THE LEGACY THREE
    /// ------------------------------------------------------------------------------------------
    /// <c>ui_panel</c>, <c>ui_bubble</c> and <c>ui_button</c> keep their 32-pixel geometry, their
    /// borders and their 32 pixels per unit, because fifteen screens are laid out against the
    /// sizes those produce and moving them would move every one of those layouts at once. They are
    /// repainted neutral like everything else — that is the fix, and it applies to them too — but
    /// they are drawn with hard edges rather than antialiased ones. At 32 pixels per unit they are
    /// magnified more than three times with <see cref="FilterMode.Point"/>, and a one-pixel alpha
    /// ramp magnified that far is not a soft edge, it is a band of translucent fringe.
    ///
    /// Border order matches UnityEngine.Sprite: (left, bottom, right, top).
    /// </summary>
    public static class UiArt
    {
        /// <summary>
        /// Not a colour: the identity element of the tint multiply. A pixel painted with this
        /// leaves the renderer as exactly whatever <c>Image.color</c> asked for.
        ///
        /// Deliberately not added to <see cref="ArtPalette"/>. Nothing in the world may be painted
        /// white; this exists so a UI frame can be painted with nothing at all.
        /// </summary>
        public static readonly Color32 Tintable = new Color32(255, 255, 255, 255);

        // ============================================================ the legacy 32 pixel frames

        public const int Size = 32;

        public static readonly Vector4 PanelBorder = new Vector4(8f, 8f, 8f, 8f);
        public static readonly Vector4 BubbleBorder = new Vector4(9f, 9f, 9f, 9f);
        public static readonly Vector4 ButtonBorder = new Vector4(8f, 8f, 8f, 8f);

        /// <summary>
        /// Alpha of the one pixel rim on the legacy frames. Low enough to read as an edge over the
        /// background, high enough that the frame does not look like it is dissolving.
        /// </summary>
        const byte LegacyRimAlpha = 120;

        /// <summary>
        /// HUD blocks, the end of day panel, the contest frame. Neutral: the body is the tint
        /// exactly, the rim and the bottom bevel are alpha.
        /// </summary>
        public static PixelCanvas Panel()
        {
            PixelCanvas canvas = new PixelCanvas(Size, Size);
            LegacyFrame(canvas, 7, 6);
            // A bevel along the bottom, applied as an alpha ramp rather than a drawn line, so it
            // follows the rounded corners and stays continuous at any stretched width.
            ShadeEdge(canvas, 2, 0.70f, false);
            return canvas;
        }

        /// <summary>The dialogue bubble and the chapter reader page. Same neutral body, softer rim.</summary>
        public static PixelCanvas Bubble()
        {
            PixelCanvas canvas = new PixelCanvas(Size, Size);
            LegacyFrame(canvas, 8, 7);
            return canvas;
        }

        /// <summary>
        /// Clay button. The lip along the bottom is what gives it a raised read, and it survives
        /// the neutral repaint because a lip is a place where less of the tint reaches the screen
        /// — the one kind of shading a multiply can express.
        /// </summary>
        public static PixelCanvas Button()
        {
            PixelCanvas canvas = new PixelCanvas(Size, Size);
            LegacyFrame(canvas, 6, 5);
            ShadeEdge(canvas, 4, 0.62f, false);
            return canvas;
        }

        /// <summary>
        /// A rounded rectangle filling the canvas at full alpha with a one pixel rim of reduced
        /// alpha around it. Two stacked fills rather than an outline call, so the corner of the
        /// rim follows the corner of the body exactly.
        /// </summary>
        static void LegacyFrame(PixelCanvas canvas, int outerRadius, int innerRadius)
        {
            canvas.FillRoundedRect(0, 0, Size, Size, outerRadius, Alpha(LegacyRimAlpha));
            canvas.FillRoundedRect(1, 1, Size - 2, Size - 2, innerRadius, Tintable);
        }

        /// <summary>
        /// Fades a band of rows at one edge of the canvas by scaling the alpha already there.
        ///
        /// Painting a translucent white over a white body would do nothing — the antialiasing
        /// routines combine same-coloured shapes by maximum coverage, on purpose, so that
        /// overlapping icon strokes do not fatten their joins. Shading therefore has to reach for
        /// the alpha channel directly. Scaling what is there also means the ramp follows the
        /// rounded corners for free, which a drawn line does not.
        /// </summary>
        /// <param name="rows">Depth of the band, in pixels.</param>
        /// <param name="deepest">Alpha multiplier at the outermost row; 1 leaves it untouched.</param>
        /// <param name="fromTop">True to fade the top edge, false for the bottom.</param>
        static void ShadeEdge(PixelCanvas canvas, int rows, float deepest, bool fromTop)
        {
            if (canvas == null || rows <= 0) return;

            for (int i = 0; i < rows; i++)
            {
                int y = fromTop ? i : canvas.Height - 1 - i;
                if (y < 0 || y >= canvas.Height) continue;

                float factor = Mathf.Lerp(deepest, 1f, i / (float)rows);
                for (int x = 0; x < canvas.Width; x++)
                {
                    Color32 existing = canvas.Get(x, y);
                    if (existing.a == 0) continue;

                    byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(existing.a * factor), 0, 255);
                    canvas.Set(x, y, new Color32(existing.r, existing.g, existing.b, alpha));
                }
            }
        }

        static Color32 Alpha(byte alpha)
        {
            return new Color32(Tintable.r, Tintable.g, Tintable.b, alpha);
        }

        // ============================================================ the Sistema Vale frames

        /// <summary>
        /// Pixels per unit these sprites are created at. Equal to the canvas default
        /// <c>referencePixelsPerUnit</c>, which is what makes one sprite pixel one canvas
        /// reference unit and lets every constant below be read straight off
        /// <c>DesignTokens.Radius</c>.
        /// </summary>
        public const float PixelsPerUnit = 100f;

        /// <summary>
        /// <c>DesignTokens.Radius.Sm</c> (8 design points x 2.769) in reference units, rounded.
        /// The tokens are not referenced directly because SheepGate.Art must not depend on
        /// SheepGate.UI — the art module is the lower layer and the dependency runs one way.
        /// </summary>
        public const int RadiusSm = 22;

        /// <summary><c>DesignTokens.Radius.Md</c> (14 design points) in reference units.</summary>
        public const int RadiusMd = 39;

        /// <summary><c>DesignTokens.Radius.Lg</c> (20 design points) in reference units.</summary>
        public const int RadiusLg = 55;

        /// <summary>
        /// Width of the perimeter alpha fade on the Sistema Vale frames, in units. Roughly one
        /// design point: enough to separate a card from the panel under it, not enough to read as
        /// a drawn border.
        /// </summary>
        const float FrameRim = 3f;

        /// <summary>Alpha the rim fades to at the very edge.</summary>
        const byte FrameRimAlpha = 150;

        /// <summary>
        /// The stretchable middle of a nine-slice. Four units is enough for the slice to exist and
        /// small enough that the sprite is almost entirely corner, which is where its quality is.
        /// </summary>
        const int Middle = 4;

        /// <summary>
        /// How far the corner slice extends past the corner arc. The arc has to finish inside the
        /// corner slice or the nine-slice shows a seam where the corner meets the stretched edge;
        /// two units covers the arc plus its antialiased fringe.
        /// </summary>
        const int CornerMargin = 2;

        public static readonly Vector4 FrameSmBorder = UniformBorder(RadiusSm + CornerMargin);
        public static readonly Vector4 FrameMdBorder = UniformBorder(RadiusMd + CornerMargin);
        public static readonly Vector4 FrameLgBorder = UniformBorder(RadiusLg + CornerMargin);

        /// <summary>Radius Sm frame. Chips, tags, the small inset surfaces.</summary>
        public static PixelCanvas FrameSm()
        {
            return Frame(RadiusSm);
        }

        /// <summary>Radius Md frame. Buttons and inputs — the design system's floor for a button.</summary>
        public static PixelCanvas FrameMd()
        {
            return Frame(RadiusMd);
        }

        /// <summary>Radius Lg frame. Cards, panels, sheets.</summary>
        public static PixelCanvas FrameLg()
        {
            return Frame(RadiusLg);
        }

        static PixelCanvas Frame(int radius)
        {
            int size = (radius + CornerMargin) * 2 + Middle;
            PixelCanvas canvas = new PixelCanvas(size, size);
            canvas.FillRoundedRectAA(0f, 0f, size, size, radius, Tintable, FrameRim, FrameRimAlpha);
            return Done(canvas);
        }

        // ------------------------------------------------------------ the pergaminho card

        /// <summary>
        /// How far the soft edge reaches inside the Image's rect, in reference units.
        ///
        /// The scroll frame's visible body is inset by this on every side, because the shadow has
        /// to live inside the sprite. A caller sizing a scroll card should add it to whatever
        /// padding the content needs, or the text will sit two design points closer to the visible
        /// edge than it looks.
        /// </summary>
        public const float ScrollHalo = 6f;

        /// <summary>
        /// Alpha of the ambient shadow at its darkest, right against the card. Low on purpose:
        /// past roughly this value it stops reading as elevation and starts reading as a border,
        /// which is precisely what variation 1b's card is not supposed to have.
        /// </summary>
        const byte ScrollHaloAlpha = 38;

        public static readonly Vector4 FrameScrollBorder =
            UniformBorder(RadiusLg + Mathf.CeilToInt(ScrollHalo) + CornerMargin);

        /// <summary>
        /// The pergaminho card of variation 1b, which section 06 of the design document applies to
        /// every screen: warm, radius Lg, and elevated by a soft edge rather than by a hard border.
        ///
        /// The body is neutral like every other frame, so the warmth comes from the tint — in
        /// practice <c>DesignTokens.Surface.Scroll</c>. The halo around it is the one place the UI
        /// art paints an actual colour, and it has to: a multiply can never produce a pixel darker
        /// than its tint, so a shadow under a near-white card cannot be neutral. It is
        /// <see cref="ArtPalette.Ink"/> at low alpha, which survives being multiplied by any tint
        /// that is not itself black.
        /// </summary>
        public static PixelCanvas FrameScroll()
        {
            int halo = Mathf.CeilToInt(ScrollHalo);
            int size = (RadiusLg + halo + CornerMargin) * 2 + Middle;
            float body = size - halo * 2;

            PixelCanvas canvas = new PixelCanvas(size, size);
            canvas.SoftShadowRoundedRect(halo, halo, body, body, RadiusLg, ScrollHalo,
                new Color32(ArtPalette.Ink.r, ArtPalette.Ink.g, ArtPalette.Ink.b, ScrollHaloAlpha));
            canvas.FillRoundedRectAA(halo, halo, body, body, RadiusLg, Tintable, FrameRim, FrameRimAlpha);
            return Done(canvas);
        }

        // ------------------------------------------------------------ the focus ring

        /// <summary>
        /// Stroke of the focus outline, in reference units. The design system asks for 2 design
        /// points; 2 x 2.769 rounds to 6.
        /// </summary>
        public const float FocusStroke = 6f;

        /// <summary>
        /// Clear space between the control's edge and the ring's inner edge. The design system's
        /// 2 point offset, converted and rounded the same way the stroke is.
        /// </summary>
        public const float FocusGap = 6f;

        /// <summary>
        /// How far the ring's OUTER edge sits outside the control it belongs to: the gap plus the
        /// stroke, in reference units.
        ///
        /// This is the placement contract. A caller stretches the ring to the control's rect
        /// expanded by this on every side — <c>offsetMin = -FocusOutset</c>,
        /// <c>offsetMax = +FocusOutset</c> — and the gap, the stroke weight and the corner all
        /// come out right without the caller knowing any of the sprite's geometry.
        /// </summary>
        public const float FocusOutset = FocusGap + FocusStroke;

        /// <summary>
        /// Corner radius of the ring, which is a button's radius plus the outset. The design
        /// system's floor for a button is Md, and one ring serves every variant, so Md is what it
        /// is drawn against: on an Lg control the ring reads very slightly tighter than the corner
        /// it traces, which is invisible next to the alternative of a second sprite per radius.
        /// </summary>
        public const int FocusRadius = RadiusMd + (int)FocusOutset;

        public static readonly Vector4 FocusRingBorder = UniformBorder(FocusRadius + CornerMargin);

        /// <summary>
        /// The focus outline: a ring with a transparent centre, identical on every button variant
        /// including ghost, because the design system requires focus to look the same everywhere.
        ///
        /// Painted neutral like the frames, so the tint carries the gold. The centre is genuinely
        /// empty rather than filled with a background colour, which is what lets the ring be laid
        /// over a control without hiding it.
        ///
        /// The sprite's own corner slices come to roughly 106 units across, so a ring drawn around
        /// anything smaller than that has its borders scaled down by uGUI and loses a little of its
        /// stroke. Every control the design system allows is at least a 48 point touch target,
        /// which is 133 units before the outset is added, so this is a floor rather than a limit.
        /// </summary>
        public static PixelCanvas FocusRing()
        {
            int size = (FocusRadius + CornerMargin) * 2 + Middle;
            PixelCanvas canvas = new PixelCanvas(size, size);
            canvas.StrokeRoundedRectAA(0f, 0f, size, size, FocusRadius, FocusStroke, Tintable);
            return Done(canvas);
        }

        // ------------------------------------------------------------ progress bars

        /// <summary>
        /// Height the bar sprites are drawn at, in reference units — about 8.7 design points,
        /// comfortably over the design system's floor of 6.
        ///
        /// A bar drawn at this height gets exactly circular caps. It stays a pill at any other
        /// height, because the caps are the sprite's left and right slices and stretch with it,
        /// but a bar drawn much shorter or taller will have caps a little wider or narrower than a
        /// true semicircle. Sizing a track to this value is the way to avoid thinking about it.
        /// </summary>
        public const float BarHeight = 24f;

        const int BarPixels = (int)BarHeight;
        const int BarRadius = BarPixels / 2;
        const int BarWidth = BarPixels + Middle;

        /// <summary>
        /// Left and right borders only. The pill is nine-sliced as two caps and a stretchable
        /// middle with NO horizontal seam: a top and bottom border would pin the cap height to the
        /// sprite's, and the caps have to scale with the bar instead. Order is
        /// (left, bottom, right, top).
        /// </summary>
        public static readonly Vector4 BarBorder = new Vector4(BarRadius, 0f, BarRadius, 0f);

        /// <summary>
        /// Alpha multiplier at the shaded edge of a bar. The track is shaded along its top and the
        /// fill along its bottom, which is the same trick used twice: less tint reaches the screen
        /// there, so the track reads as sunk and the fill as lit from above, under any pair of
        /// token colours and without either of them being told what the other is.
        /// </summary>
        const float BarShade = 0.80f;

        /// <summary>The empty half of a progress bar. Pill ended, neutral, subtly recessed.</summary>
        public static PixelCanvas BarTrack()
        {
            PixelCanvas canvas = Pill();
            ShadeEdge(canvas, 4, BarShade, true);
            return canvas;
        }

        /// <summary>The filled half. Same pill, shaded along the bottom so it sits inside the track.</summary>
        public static PixelCanvas BarFill()
        {
            PixelCanvas canvas = Pill();
            ShadeEdge(canvas, 4, BarShade, false);
            return canvas;
        }

        static PixelCanvas Pill()
        {
            PixelCanvas canvas = new PixelCanvas(BarWidth, BarPixels);
            canvas.FillRoundedRectAA(0f, 0f, BarWidth, BarPixels, BarRadius, Tintable);
            return Done(canvas);
        }

        // ============================================================ the status icons

        /// <summary>
        /// The icon grid, drawn at three times the design system's 24 so the strokes land on whole
        /// pixels: a 2 point stroke on a 24 grid becomes a 6 pixel stroke on a 72 grid, and 2
        /// point corners become 6 pixel corners.
        ///
        /// These exist as sprites and not as text because NONE of the three bundled families
        /// carries the characters the mocks use for them — of the set, only <c>x</c>, an arrow, a
        /// middle dot and an em dash are covered anywhere, and substituting a lookalike character
        /// for a missing glyph is exactly what the design system's iconography note forbids.
        /// </summary>
        public const int IconGrid = 72;

        /// <summary>Stroke weight, in icon pixels. Two design points at this grid's scale.</summary>
        const float IconStroke = 6f;

        /// <summary>
        /// Display size the icons are drawn for, in reference units: 24 design points. They are
        /// generated slightly larger than that and sampled down, which is why the UI sprites are
        /// bilinear rather than point filtered.
        /// </summary>
        public const float IconDisplaySize = 66f;

        /// <summary>Confirmation, a completed step, a met requirement.</summary>
        public static PixelCanvas IconCheck()
        {
            PixelCanvas canvas = new PixelCanvas(IconGrid, IconGrid);
            canvas.CapsuleAA(16f, 38f, 29f, 51f, IconStroke, Tintable);
            canvas.CapsuleAA(29f, 51f, 56f, 21f, IconStroke, Tintable);
            return Done(canvas);
        }

        /// <summary>Dismiss, and the negative half of a pair. Never a failure the player caused.</summary>
        public static PixelCanvas IconClose()
        {
            PixelCanvas canvas = new PixelCanvas(IconGrid, IconGrid);
            canvas.CapsuleAA(19f, 19f, 53f, 53f, IconStroke, Tintable);
            canvas.CapsuleAA(53f, 19f, 19f, 53f, IconStroke, Tintable);
            return Done(canvas);
        }

        /// <summary>A bullet, a step marker, a state that is neither done nor refused.</summary>
        public static PixelCanvas IconDot()
        {
            PixelCanvas canvas = new PixelCanvas(IconGrid, IconGrid);
            canvas.FillCircleAA(36f, 36f, 11f, Tintable);
            return Done(canvas);
        }

        /// <summary>
        /// Forward. Drawn pointing right; a caller wanting another direction rotates the
        /// RectTransform rather than asking for a second sprite.
        /// </summary>
        public static PixelCanvas IconArrow()
        {
            PixelCanvas canvas = new PixelCanvas(IconGrid, IconGrid);
            canvas.CapsuleAA(16f, 36f, 51f, 36f, IconStroke, Tintable);
            canvas.CapsuleAA(38f, 20f, 54f, 36f, IconStroke, Tintable);
            canvas.CapsuleAA(38f, 52f, 54f, 36f, IconStroke, Tintable);
            return Done(canvas);
        }

        /// <summary>Settings and overflow. Three bars, evenly spaced on the grid.</summary>
        public static PixelCanvas IconMenu()
        {
            PixelCanvas canvas = new PixelCanvas(IconGrid, IconGrid);
            canvas.CapsuleAA(14f, 22f, 58f, 22f, IconStroke, Tintable);
            canvas.CapsuleAA(14f, 36f, 58f, 36f, IconStroke, Tintable);
            canvas.CapsuleAA(14f, 50f, 58f, 50f, IconStroke, Tintable);
            return Done(canvas);
        }

        /// <summary>
        /// Not yet available. Note what this icon may never mean here: the game has no game over
        /// and takes nothing back, so a padlock marks something the player has not reached yet,
        /// never something they lost.
        /// </summary>
        public static PixelCanvas IconLock()
        {
            PixelCanvas canvas = new PixelCanvas(IconGrid, IconGrid);
            canvas.StrokeArcAA(36f, 31f, 13f, IconStroke, 0f, 180f, Tintable);
            canvas.FillRoundedRectAA(14f, 31f, 44f, 27f, 6f, Tintable);
            canvas.EraseCircleAA(36f, 44f, 4.5f);
            return Done(canvas);
        }

        /// <summary>
        /// Last pass on every Sistema Vale sprite: gives the empty texels the body's colour so a
        /// bilinear sample taken between a lit texel and an empty one does not drag a dark fringe
        /// in with it. It changes nothing that is drawn, only what is hiding under the zeroes.
        /// </summary>
        static PixelCanvas Done(PixelCanvas canvas)
        {
            canvas.SetTransparentColor(Tintable);
            return canvas;
        }

        static Vector4 UniformBorder(int border)
        {
            return new Vector4(border, border, border, border);
        }
    }
}
