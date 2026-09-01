using UnityEngine;

namespace SheepGate.Art
{
    /// <summary>Facing of a character part. Left is drawn as a mirrored Right.</summary>
    public enum ArtFacing
    {
        Down = 0,
        Up = 1,
        Left = 2,
        Right = 3
    }

    /// <summary>Animation clip of a character part. Every clip is two frames.</summary>
    public enum ArtAnim
    {
        Idle = 0,
        Walk = 1,
        Work = 2
    }

    /// <summary>
    /// The layered 32x48 character: a body plus three cosmetic overlays that stack in the
    /// order body, legs, top, accessory.
    ///
    /// THE RIG INVARIANT, which the whole layering scheme depends on:
    /// the head, torso and thigh rectangles below are IDENTICAL for every direction,
    /// animation and frame. Direction changes face pixels and shading only; it never moves
    /// or resizes a silhouette an overlay draws on. That is what lets a single static
    /// `top_2` sprite sit correctly on all 48 body sprites.
    ///
    /// Only three regions animate:
    ///   - the arms, in columns x7..9 and x22..24, over rows y8..y31. Not y17 and down: the
    ///     work swing lifts them to y8, so "above ArmY" is not a safe place to put anything.
    ///   - the boots, below the lowest trouser hem
    ///   - the work tool
    /// An overlay stays out of all three, and anything added here must respect that split or the
    /// overlays will drift.
    ///
    /// IN PRACTICE that gives an overlay two hard edges, and both are one pixel tighter than
    /// they look, because <see cref="PixelCanvas.OutlineOpaque"/> puts an ink border AROUND
    /// whatever is drawn:
    ///   - opaque pixels within x11..20, so the border stays within x10..21 and off the arms;
    ///   - the topmost opaque row at y15 or below, so the border lands on y14, the neck, and
    ///     never on y13, which is the jaw.
    ///
    /// ONE OVERLAY IS EXEMPT, and the exemption is what proves the rule: the wrist beads
    /// (accessory variant 4) sit ON an arm, because the catalogue anchors them to `wrist_r` and a
    /// wrist is nowhere else. They are allowed there only because they do not assume a row — they
    /// read the same <see cref="HandTop"/> the arm is drawn from, so they move with it instead of
    /// drifting off it. Any future overlay that wants an animated region owes the same proof.
    /// </summary>
    public static class CharacterArt
    {
        public const int Width = 32;
        public const int Height = 48;

        /// <summary>
        /// Build times skin tone. The two are packed into one variant because they share a sprite:
        /// variant / SkinCount is the build, variant % SkinCount is the tone.
        /// </summary>
        public const int BodyVariants = 8;

        public const int SkinCount = 4;

        /// <summary>
        /// One variant per catalogue item, deliberately: two pieces with different names that draw
        /// the same shape read as a bug the moment a wardrobe puts them side by side.
        /// </summary>
        public const int HairVariants = 7;

        public const int TopVariants = 4;
        public const int LegsVariants = 4;
        public const int AccessoryVariants = 6;

        // ---- Rig, in top-left origin coordinates. Every rectangle is symmetric about x 15.5,
        //      so MirrorHorizontal() maps the rig onto itself exactly.
        public const int HeadX = 10, HeadY = 2, HeadW = 12, HeadH = 12;      // y 2..13
        public const int NeckX = 14, NeckY = 14, NeckW = 4, NeckH = 2;       // y 14..15
        public const int TorsoX = 10, TorsoY = 16, TorsoW = 12, TorsoH = 15; // y 16..30
        public const int ArmLX = 7, ArmRX = 22, ArmW = 3, ArmY = 17, ArmH = 13;
        public const int LegLX = 11, LegRX = 17, LegW = 4, LegY = 31, LegH = 11; // y 31..41
        public const int BootLX = 10, BootRX = 16, BootW = 6, BootY = 42, BootH = 5;

        /// <summary>Lowest hem any trouser variant reaches. Boots start below it.</summary>
        public const int LowestHem = LegY + LegH - 1;

        // ------------------------------------------------------------------ body

        public static PixelCanvas Body(int variant, ArtFacing facing, ArtAnim anim, int frame)
        {
            variant = Mathf.Clamp(variant, 0, BodyVariants - 1);
            frame = Mathf.Clamp(frame, 0, 1);

            int build = variant / SkinCount;
            ArtPalette.SkinTone tone = ArtPalette.SkinTones[variant % SkinCount];

            bool mirrored = facing == ArtFacing.Left;
            ArtFacing drawn = mirrored ? ArtFacing.Right : facing;

            Color32 skin = tone.Base;
            Color32 skinLight = tone.Light;
            Color32 skinShade = tone.Shade;
            Color32 skinDeep = tone.Deep;

            PixelCanvas canvas = new PixelCanvas(Width, Height);

            // Thighs and shins. Static: the trouser overlay covers them.
            canvas.FillRect(LegLX, LegY, LegW, LegH, skinShade);
            canvas.FillRect(LegRX, LegY, LegW, LegH, skin);
            canvas.VLine(LegLX + LegW - 1, LegY, LegH, skinDeep);
            canvas.VLine(LegRX, LegY, LegH, skinShade);

            // Boots. The only animated part of the lower body.
            int bootLeftX, bootLeftY, bootRightX, bootRightY;
            BootOffsets(drawn, anim, frame, out bootLeftX, out bootLeftY, out bootRightX, out bootRightY);
            DrawBoot(canvas, BootLX + bootLeftX, BootY + bootLeftY);
            DrawBoot(canvas, BootRX + bootRightX, BootY + bootRightY);

            // Torso. Static: the top overlay covers it. Build 1 is a narrower frame: one pixel
            // off each shoulder, which is as much as a 12-pixel torso can show without the two
            // silhouettes reading as the same person drawn badly.
            int torsoX = build == 1 ? TorsoX + 1 : TorsoX;
            int torsoW = build == 1 ? TorsoW - 2 : TorsoW;
            canvas.FillRect(torsoX, TorsoY, torsoW, TorsoH, skin);
            if (build == 1)
            {
                // The shoulders taper in rather than ending square.
                canvas.Set(torsoX - 1, TorsoY + 2, skinShade);
                canvas.Set(torsoX + torsoW, TorsoY + 2, skinShade);
                canvas.FillRect(torsoX, TorsoY + TorsoH - 4, torsoW, 4, skin);
            }
            canvas.ShadeRect(torsoX, TorsoY + TorsoH - 2, torsoW, 2, ArtPalette.SoftShade);
            if (drawn == ArtFacing.Right) canvas.ShadeRect(torsoX, TorsoY, 5, TorsoH, ArtPalette.SoftShade);
            else if (drawn == ArtFacing.Up) canvas.ShadeRect(torsoX, TorsoY, torsoW, 3, ArtPalette.SoftShade);
            else canvas.HLine(torsoX + 2, TorsoY + 1, torsoW - 4, skinShade);

            canvas.FillRect(NeckX, NeckY, NeckW, NeckH, skinShade);

            // Arms, then the tool they hold.
            int armTop, armHeight, leftOffset, rightOffset;
            bool handsUp;
            ArmPose(anim, frame, out armTop, out armHeight, out leftOffset, out rightOffset, out handsUp);
            Color32 farArm = drawn == ArtFacing.Right ? skinShade : skin;
            // Arms follow the shoulder line, or the narrower build's hang off nothing.
            int armLeftX = build == 1 ? ArmLX + 1 : ArmLX;
            int armRightX = build == 1 ? ArmRX - 1 : ArmRX;

            DrawArm(canvas, armLeftX, armTop + leftOffset, armHeight, farArm, skinLight, skinDeep, true, handsUp);
            DrawArm(canvas, armRightX, armTop + rightOffset, armHeight, skin, skinLight, skinDeep, false, handsUp);
            if (anim == ArtAnim.Work) DrawTool(canvas, frame);

            DrawHead(canvas, drawn, skin, skinLight, skinShade, skinDeep);

            canvas.OutlineOpaque(ArtPalette.Ink);
            if (mirrored) canvas.MirrorHorizontal();
            return canvas;
        }

        static void BootOffsets(ArtFacing facing, ArtAnim anim, int frame,
            out int leftX, out int leftY, out int rightX, out int rightY)
        {
            leftX = 0;
            leftY = 0;
            rightX = 0;
            rightY = 0;

            switch (anim)
            {
                case ArtAnim.Walk:
                    if (facing == ArtFacing.Down || facing == ArtFacing.Up)
                    {
                        // Seen head on, a step reads as one foot lifted off the ground.
                        if (frame == 0) leftY = -1; else rightY = -1;
                    }
                    else
                    {
                        // Seen from the side, one foot leads and the other trails.
                        if (frame == 0) { rightX = 2; leftX = -1; }
                        else { leftX = 2; rightX = -1; }
                    }
                    break;

                case ArtAnim.Work:
                    // A braced stance, identical on both frames.
                    leftX = -1;
                    rightX = 1;
                    break;
            }
        }

        static void DrawBoot(PixelCanvas canvas, int x, int y)
        {
            canvas.FillRect(x, y, BootW, BootH, ArtPalette.StoneDark);
            canvas.HLine(x, y, BootW, ArtPalette.StoneMid);
            canvas.HLine(x, y + BootH - 1, BootW, ArtPalette.StoneDeep);
        }

        static void ArmPose(ArtAnim anim, int frame,
            out int armTop, out int armHeight, out int leftOffset, out int rightOffset, out bool handsUp)
        {
            armTop = ArmY;
            armHeight = ArmH;
            leftOffset = 0;
            rightOffset = 0;
            handsUp = false;

            switch (anim)
            {
                case ArtAnim.Idle:
                    if (frame == 1) { leftOffset = 1; rightOffset = 1; }
                    break;

                case ArtAnim.Walk:
                    if (frame == 0) { leftOffset = 2; rightOffset = -2; }
                    else { leftOffset = -2; rightOffset = 2; }
                    break;

                case ArtAnim.Work:
                    if (frame == 0)
                    {
                        armTop = 8;
                        armHeight = 12;
                        handsUp = true;
                    }
                    else
                    {
                        armTop = 20;
                        armHeight = 11;
                    }
                    break;
            }
        }

        /// <summary>
        /// Top row of one hand, for the pose the body is drawing.
        ///
        /// Public because an overlay that has to meet the hand — the wrist beads — must not carry
        /// its own copy of these numbers: both it and the arm read the same
        /// <see cref="ArmPose"/>, so they cannot drift apart when a pose is retimed.
        ///
        /// <paramref name="leftArm"/> picks which of the two, and it is not cosmetic: a walk
        /// swings the arms in opposite directions, so asking for the wrong one is four rows out.
        /// </summary>
        public static int HandTop(ArtAnim anim, int frame, bool leftArm)
        {
            int armTop, armHeight, leftOffset, rightOffset;
            bool handsUp;
            ArmPose(anim, Mathf.Clamp(frame, 0, 1),
                    out armTop, out armHeight, out leftOffset, out rightOffset, out handsUp);

            int top = armTop + (leftArm ? leftOffset : rightOffset);
            return handsUp ? top : top + armHeight - 3;
        }

        /// <summary>
        /// True when the pose lifts the hands above the shoulders, as the work swing does. The
        /// wrist is below the hand when an arm hangs and above it when the arm is raised, and
        /// nothing else in the rig exposes which of the two is happening.
        /// </summary>
        public static bool HandsRaised(ArtAnim anim, int frame)
        {
            int armTop, armHeight, leftOffset, rightOffset;
            bool handsUp;
            ArmPose(anim, Mathf.Clamp(frame, 0, 1),
                    out armTop, out armHeight, out leftOffset, out rightOffset, out handsUp);
            return handsUp;
        }

        /// <summary>
        /// One arm. The column facing the torso is darkened, because the body outline only
        /// wraps the outer silhouette: without that seam the arms and the chest read as a
        /// single barrel, and a torso garment cannot fix it since it stops at the torso.
        /// </summary>
        static void DrawArm(PixelCanvas canvas, int x, int top, int height,
            Color32 skin, Color32 skinLight, Color32 seam, bool seamOnRight, bool handsUp)
        {
            canvas.FillRect(x, top, ArmW, height, skin);
            int handTop = handsUp ? top : top + height - 3;
            canvas.FillRect(x, handTop, ArmW, 3, skinLight);
            canvas.VLine(seamOnRight ? x + ArmW - 1 : x, top, height, seam);
        }

        /// <summary>A stone headed mallet, swung down between the two work frames.</summary>
        static void DrawTool(PixelCanvas canvas, int frame)
        {
            int headTop = frame == 0 ? 3 : 32;
            int handleTop = frame == 0 ? 6 : 22;

            canvas.VLine(25, handleTop, 11, ArtPalette.ClayDark);
            canvas.VLine(26, handleTop, 11, ArtPalette.ClayDeep);
            canvas.FillRect(23, headTop, 6, 4, ArtPalette.StoneMid);
            canvas.HLine(23, headTop, 6, ArtPalette.StoneLight);
            canvas.HLine(23, headTop + 3, 6, ArtPalette.StoneDark);
        }

        static void DrawHead(PixelCanvas canvas, ArtFacing facing,
            Color32 skin, Color32 skinLight, Color32 skinShade, Color32 skinDeep)
        {
            canvas.FillRect(HeadX, HeadY, HeadW, HeadH, skin);
            canvas.HLine(HeadX + 1, HeadY + 1, HeadW - 2, skinLight);
            canvas.HLine(HeadX, HeadY + HeadH - 1, HeadW, skinShade);

            switch (facing)
            {
                case ArtFacing.Down:
                    canvas.Set(13, 8, ArtPalette.Ink);
                    canvas.Set(18, 8, ArtPalette.Ink);
                    canvas.Set(13, 9, skinShade);
                    canvas.Set(18, 9, skinShade);
                    canvas.HLine(15, 11, 2, skinDeep);
                    break;

                case ArtFacing.Up:
                    canvas.HLine(HeadX + 2, HeadY + 9, HeadW - 4, skinShade);
                    break;

                default: // Right; Left is this canvas mirrored.
                    canvas.Set(19, 8, ArtPalette.Ink);
                    canvas.Set(19, 9, skinShade);
                    canvas.Set(21, 9, skinShade);
                    canvas.Set(16, 9, skinShade);
                    canvas.HLine(19, 11, 2, skinDeep);
                    break;
            }
        }

        /// <summary>
        /// Hair, lifted out of the head so it can be chosen apart from skin. Seven looks, each
        /// drawn onto the same head rectangle the body paints.
        ///
        /// Every look is a shared crown plus one distinguishing MASS, and that is the whole method
        /// here: at 32x48 a strand is one pixel and reads as dirt, so what separates two variants
        /// is always the silhouette — a braid past the jaw, a bun clearing the crown, a band of
        /// cloth — and never a difference in texture. The test the design system sets is that a
        /// piece which disappears at 30 pixels is not a piece.
        ///
        /// Two variants are load bearing and their indices must not move:
        ///   - 3 is the head cloth. Every hooded outfit and every art_hooded rule in the catalogue
        ///     forces hair 3, so renumbering it silently unhoods six items.
        ///   - 0 is the short crop, which is what the catalogue's free hair and Adar's default
        ///     both point at.
        /// </summary>
        public static PixelCanvas Hair(int variant, ArtFacing facing)
        {
            variant = Mathf.Clamp(variant, 0, HairVariants - 1);
            bool mirrored = facing == ArtFacing.Left;
            ArtFacing drawn = mirrored ? ArtFacing.Right : facing;

            Color32 hair = ArtPalette.HairColours[variant];
            Color32 shade = ArtPalette.HairShades[variant];

            PixelCanvas canvas = new PixelCanvas(Width, Height);

            switch (variant)
            {
                // The two covered looks draw INSTEAD of the crown rather than over it: a crown
                // underneath would only be repainted, and for the shaved band it would put hair
                // back on the temples the razor took off.
                case 3: DrawHeadCloth(canvas, drawn, hair, shade); break;
                case 4: DrawShavedBand(canvas, drawn, hair, shade); break;

                case 1: DrawCrown(canvas, drawn, hair, shade); DrawSideBraid(canvas, drawn, hair, shade); break;
                case 2: DrawCrown(canvas, drawn, hair, shade); DrawLooseWaves(canvas, drawn, hair, shade); break;
                case 5: DrawCrown(canvas, drawn, hair, shade); DrawWorkBun(canvas, drawn, hair, shade); break;
                case 6: DrawCrown(canvas, drawn, hair, shade); DrawTiedBack(canvas, drawn, hair, shade); break;

                default: DrawCrown(canvas, drawn, hair, shade); break;  // 0, the short crop itself
            }

            if (mirrored) canvas.MirrorHorizontal();
            return canvas;
        }

        /// <summary>
        /// hair_short_crop on its own, and the base every uncovered look is built on: cut close all
        /// round, nothing hanging below the brow. The crown ADDS nothing past the brow line so that
        /// a variant wanting length can add it, rather than every long variant having to erase a
        /// side the crown drew — which is how the old numbering ended up with the dense cut mapped
        /// to the cropped name.
        /// </summary>
        static void DrawCrown(PixelCanvas canvas, ArtFacing facing, Color32 hair, Color32 shade)
        {
            switch (facing)
            {
                case ArtFacing.Up:
                    // From behind there is no face to stop at, so the crown is the whole skull.
                    canvas.FillRect(HeadX, HeadY, HeadW, 7, hair);
                    canvas.HLine(HeadX, HeadY + 6, HeadW, shade);
                    break;

                case ArtFacing.Right:
                    canvas.FillRect(HeadX, HeadY, HeadW, 4, hair);
                    canvas.FillRect(HeadX, HeadY, 4, 5, hair);          // the back of the skull
                    canvas.HLine(HeadX, HeadY + 4, 4, shade);
                    break;

                default: // Down
                    canvas.FillRect(HeadX, HeadY, HeadW, 4, hair);
                    canvas.HLine(HeadX + 1, HeadY + 3, HeadW - 2, shade); // the brow it stops at
                    break;
            }
        }

        /// <summary>
        /// hair_side_braid: one thick plait on the character's right. Drawn as a two pixel column
        /// with a tie every third row, because at this size a plait can only be read from its
        /// rhythm — weave it pixel by pixel and it turns to noise.
        ///
        /// It ends at y15, above ArmY, so no frame of any arm animation runs into it. The side it
        /// falls on flips between Left and Right because Hair() mirrors the whole canvas, which is
        /// what the rest of the rig already does.
        /// </summary>
        static void DrawSideBraid(PixelCanvas canvas, ArtFacing facing, Color32 hair, Color32 shade)
        {
            // Seen from behind, the character's right side is on the viewer's left.
            int x = facing == ArtFacing.Up ? HeadX - 1 : HeadX + HeadW - 1;
            canvas.FillRect(x, HeadY + 4, 2, 10, hair);
            for (int y = HeadY + 6; y <= HeadY + 12; y += 3) canvas.HLine(x, y, 2, shade);
        }

        /// <summary>
        /// hair_loose_waves: down past the jaw on both sides. The two pixels pushed outward at y9
        /// and y12 are the whole difference between a wave and a curtain — without them the fall
        /// is a dead straight edge and reads as the cropped cut grown long.
        /// </summary>
        static void DrawLooseWaves(PixelCanvas canvas, ArtFacing facing, Color32 hair, Color32 shade)
        {
            switch (facing)
            {
                case ArtFacing.Up:
                    // Starts at the crown's last row rather than at the length itself: a fill that
                    // began where the extra length begins would float clear of the head.
                    canvas.FillRect(HeadX + 2, HeadY + 7, HeadW - 4, 5, hair);
                    canvas.HLine(HeadX + 2, HeadY + 11, HeadW - 4, shade);
                    break;

                case ArtFacing.Right:
                    canvas.FillRect(HeadX, HeadY + 4, 4, 9, hair);
                    canvas.HLine(HeadX, HeadY + 12, 4, shade);
                    break;

                default: // Down
                    canvas.VLine(HeadX, HeadY, 12, hair);
                    canvas.VLine(HeadX + HeadW - 1, HeadY, 12, hair);
                    canvas.VLine(HeadX + 1, HeadY + 7, 5, shade);
                    canvas.VLine(HeadX + HeadW - 2, HeadY + 7, 5, shade);
                    canvas.Set(HeadX - 1, 9, hair);
                    canvas.Set(HeadX - 1, 12, hair);
                    canvas.Set(HeadX + HeadW, 9, hair);
                    canvas.Set(HeadX + HeadW, 12, hair);
                    break;
            }
        }

        /// <summary>
        /// hair_headscarf, which is not a hairstyle at all: it is the worker's head cloth, and it
        /// is also the shape every hooded outfit forces. That is why it covers the skull outright
        /// and why its colour pair is bleached linen rather than a hair colour.
        /// </summary>
        static void DrawHeadCloth(PixelCanvas canvas, ArtFacing facing, Color32 cloth, Color32 shade)
        {
            switch (facing)
            {
                case ArtFacing.Up:
                    canvas.FillRect(HeadX - 1, HeadY, HeadW + 2, 9, cloth);
                    canvas.HLine(HeadX - 1, HeadY + 8, HeadW + 2, shade);
                    break;

                case ArtFacing.Right:
                    canvas.FillRect(HeadX - 1, HeadY, HeadW + 2, 6, cloth);
                    canvas.HLine(HeadX - 1, HeadY + 5, HeadW + 2, shade);
                    canvas.FillRect(HeadX - 1, HeadY, 5, 9, cloth);      // the drape at the nape
                    canvas.HLine(HeadX - 1, HeadY + 8, 5, shade);
                    break;

                default: // Down
                    canvas.FillRect(HeadX - 1, HeadY, HeadW + 2, 6, cloth);
                    canvas.HLine(HeadX - 1, HeadY + 5, HeadW + 2, shade);
                    canvas.VLine(HeadX - 1, HeadY, 9, cloth);
                    canvas.VLine(HeadX + HeadW, HeadY, 9, cloth);
                    break;
            }
        }

        /// <summary>
        /// hair_shaved_band: shaved at the sides, a strip of cloth holding what is left. The band
        /// is drawn from the stone ramp and not in a hair colour, because the words say cloth —
        /// the same reason Accessory() reaches into ArtPalette for a strap.
        ///
        /// There are no side columns at all, which is the point: past the crown it is bare skin,
        /// and that is what tells this look apart from the crop at a glance.
        /// </summary>
        static void DrawShavedBand(PixelCanvas canvas, ArtFacing facing, Color32 hair, Color32 shade)
        {
            // Widest from behind, narrowest face on: face on, the temples are what the band holds.
            int stripX, stripW;
            switch (facing)
            {
                case ArtFacing.Up:    stripX = HeadX + 2; stripW = HeadW - 4; break;
                case ArtFacing.Right: stripX = HeadX + 1; stripW = 6;         break;
                default:              stripX = HeadX + 3; stripW = HeadW - 6; break;
            }
            canvas.FillRect(stripX, HeadY, stripW, 3, hair);
            canvas.HLine(stripX, HeadY + 2, stripW, shade);

            canvas.HLine(HeadX - 1, HeadY + 3, HeadW + 2, ArtPalette.StoneLight);
            canvas.HLine(HeadX - 1, HeadY + 4, HeadW + 2, ArtPalette.StoneMid);

            // The knot tail. Above ArmY, and moved to the other side from behind so it stays on
            // the same side of the character's head whichever way they are looking.
            int tailX = facing == ArtFacing.Up ? HeadX + HeadW : HeadX - 2;
            canvas.FillRect(tailX, HeadY + 5, 2, 3, ArtPalette.StoneMid);
            canvas.HLine(tailX, HeadY + 5, 2, ArtPalette.StoneLight);
        }

        /// <summary>
        /// hair_work_bun: pinned high and tight, clear of rope and scaffold. A compact round mass
        /// held above the skull, which is the one thing that keeps it distinct from hair_tied_back
        /// — that one is a length that hangs, this one is a ball that sits.
        ///
        /// Face on, only two rows of it clear the crown, and that is enough: a bun on a 12 pixel
        /// head is read from the bump in the outline, not from the bun.
        /// </summary>
        static void DrawWorkBun(PixelCanvas canvas, ArtFacing facing, Color32 hair, Color32 shade)
        {
            switch (facing)
            {
                case ArtFacing.Up:
                    canvas.FillRect(HeadX + 4, HeadY + 4, 4, 4, hair);
                    canvas.Outline(HeadX + 3, HeadY + 3, 6, 6, shade);   // the tie, ringing it
                    break;

                case ArtFacing.Right:
                    canvas.FillRect(HeadX - 2, HeadY + 3, 3, 4, hair);
                    canvas.HLine(HeadX - 2, HeadY + 6, 3, shade);
                    break;

                default: // Down
                    canvas.FillRect(HeadX + 4, HeadY - 2, 4, 2, hair);
                    canvas.HLine(HeadX + 4, HeadY - 1, 4, shade);
                    break;
            }
        }

        /// <summary>
        /// hair_tied_back, and Neriah's default: a smooth crown with the length gathered at the
        /// nape into a low tail. Face on it is almost the crop — swept sides, then two pixels of
        /// tail past the jaw. That restraint is deliberate: the look belongs to the back and the
        /// side, and faking it from the front would only make it read as loose hair.
        /// </summary>
        static void DrawTiedBack(PixelCanvas canvas, ArtFacing facing, Color32 hair, Color32 shade)
        {
            switch (facing)
            {
                case ArtFacing.Up:
                    // Contiguous with the crown, then tapered to two pixels: a hanging length,
                    // where the bun is a mass.
                    canvas.FillRect(HeadX + 4, HeadY + 7, 4, 8, hair);
                    canvas.HLine(HeadX + 4, HeadY + 9, 4, shade);            // the tie
                    canvas.VLine(HeadX + 4, HeadY + 13, 2, ArtPalette.Transparent);
                    canvas.VLine(HeadX + 7, HeadY + 13, 2, ArtPalette.Transparent);
                    break;

                case ArtFacing.Right:
                    canvas.FillRect(HeadX - 1, HeadY + 5, 2, 9, hair);
                    canvas.HLine(HeadX - 1, HeadY + 7, 2, shade);            // the tie
                    break;

                default: // Down
                    // Swept, not loose: the sides come down a pixel INSIDE the crown line.
                    canvas.VLine(HeadX + 1, HeadY + 4, 2, hair);
                    canvas.VLine(HeadX + HeadW - 2, HeadY + 4, 2, hair);
                    canvas.VLine(HeadX, HeadY + 12, 2, hair);
                    canvas.VLine(HeadX + HeadW - 1, HeadY + 12, 2, hair);
                    break;
            }
        }

        // ------------------------------------------------------------------ overlays

        /// <summary>Trousers. Drawn over the static thigh rectangles, hems vary by variant.</summary>
        public static PixelCanvas Legs(int variant, ArtFacing facing)
        {
            variant = Mathf.Clamp(variant, 0, LegsVariants - 1);
            bool mirrored = facing == ArtFacing.Left;

            Color32 main, dark, light;
            int hem;
            switch (variant)
            {
                case 1: main = ArtPalette.ClayDark; dark = ArtPalette.ClayDeep; light = ArtPalette.ClayMid; hem = 41; break;
                case 2: main = ArtPalette.TealDark; dark = ArtPalette.TealDeep; light = ArtPalette.TealMid; hem = 39; break;
                case 3: main = ArtPalette.StoneDark; dark = ArtPalette.StoneDeep; light = ArtPalette.StoneMid; hem = 41; break;
                default: main = ArtPalette.StoneMid; dark = ArtPalette.StoneDark; light = ArtPalette.StoneLight; hem = 36; break;
            }

            PixelCanvas canvas = new PixelCanvas(Width, Height);
            int height = hem - LegY + 1;
            canvas.FillRect(LegLX, LegY, LegW, height, main);
            canvas.FillRect(LegRX, LegY, LegW, height, main);
            canvas.FillRect(LegLX + LegW, LegY, LegRX - (LegLX + LegW), 5, main); // seat between the legs

            canvas.VLine(LegLX, LegY, height, dark);
            canvas.VLine(LegRX + LegW - 1, LegY, height, dark);
            canvas.HLine(LegLX, LegY, LegW, light);
            canvas.HLine(LegRX, LegY, LegW, light);
            canvas.HLine(LegLX, hem, LegW, dark);
            canvas.HLine(LegRX, hem, LegW, dark);

            switch (variant)
            {
                case 2: // a square knee patch
                    canvas.FillRect(LegRX + 1, 35, 3, 3, light);
                    canvas.Outline(LegRX + 1, 35, 3, 3, dark);
                    break;
                case 3: // turned up cuffs
                    canvas.HLine(LegLX, hem - 1, LegW, light);
                    canvas.HLine(LegRX, hem - 1, LegW, light);
                    break;
                case 1: // a side seam
                    canvas.VLine(LegLX + 1, LegY + 2, height - 3, light);
                    canvas.VLine(LegRX + 2, LegY + 2, height - 3, light);
                    break;
            }

            if (mirrored) canvas.MirrorHorizontal();
            return canvas;
        }

        /// <summary>
        /// Torso garment. Sleeveless by design: the arms belong to the body layer so they can
        /// swing without the overlay having to follow them.
        /// </summary>
        public static PixelCanvas Top(int variant, ArtFacing facing)
        {
            variant = Mathf.Clamp(variant, 0, TopVariants - 1);
            bool mirrored = facing == ArtFacing.Left;
            ArtFacing drawn = mirrored ? ArtFacing.Right : facing;

            PixelCanvas canvas = new PixelCanvas(Width, Height);

            switch (variant)
            {
                case 1: // work apron with shoulder straps
                    canvas.FillRect(TorsoX + 1, TorsoY + 3, TorsoW - 2, TorsoH - 3, ArtPalette.TealMid);
                    canvas.VLine(TorsoX + 2, TorsoY, 4, ArtPalette.TealDark);
                    canvas.VLine(TorsoX + TorsoW - 3, TorsoY, 4, ArtPalette.TealDark);
                    canvas.HLine(TorsoX + 1, TorsoY + 3, TorsoW - 2, ArtPalette.TealLight);
                    canvas.FillRect(TorsoX + 3, TorsoY + 8, 6, 4, ArtPalette.TealDark);
                    canvas.HLine(TorsoX + 1, TorsoY + TorsoH - 1, TorsoW - 2, ArtPalette.TealDeep);
                    break;

                case 2: // wrapped shirt with a clay belt
                    canvas.FillRect(TorsoX, TorsoY, TorsoW, TorsoH - 3, ArtPalette.StoneLight);
                    canvas.HLine(TorsoX, TorsoY, TorsoW, ArtPalette.StonePale);
                    canvas.Line(TorsoX + 1, TorsoY + 2, TorsoX + TorsoW - 2, TorsoY + 10, ArtPalette.StoneMid);
                    canvas.FillRect(TorsoX, TorsoY + TorsoH - 3, TorsoW, 3, ArtPalette.ClayDark);
                    canvas.HLine(TorsoX, TorsoY + TorsoH - 3, TorsoW, ArtPalette.ClayMid);
                    break;

                case 3: // padded jerkin
                    canvas.FillRect(TorsoX, TorsoY, TorsoW, TorsoH, ArtPalette.StoneDark);
                    canvas.FillRect(TorsoX, TorsoY, TorsoW, 2, ArtPalette.StoneMid);
                    for (int y = TorsoY + 3; y < TorsoY + TorsoH - 1; y += 3)
                        canvas.HLine(TorsoX + 1, y, TorsoW - 2, ArtPalette.StoneDeep);
                    canvas.HLine(TorsoX, TorsoY + TorsoH - 1, TorsoW, ArtPalette.StoneDeep);
                    break;

                default: // sleeveless clay vest, open at the front
                    canvas.FillRect(TorsoX, TorsoY + 1, TorsoW, TorsoH - 1, ArtPalette.ClayMid);
                    canvas.HLine(TorsoX, TorsoY + 1, TorsoW, ArtPalette.ClayLight);
                    canvas.VLine(TorsoX, TorsoY + 1, TorsoH - 1, ArtPalette.ClayDark);
                    canvas.VLine(TorsoX + TorsoW - 1, TorsoY + 1, TorsoH - 1, ArtPalette.ClayDark);
                    canvas.HLine(TorsoX, TorsoY + TorsoH - 1, TorsoW, ArtPalette.ClayDeep);
                    if (drawn != ArtFacing.Up)
                    {
                        canvas.VLine(15, TorsoY + 2, TorsoH - 3, ArtPalette.ClayDeep);
                        canvas.VLine(16, TorsoY + 2, TorsoH - 3, ArtPalette.ClayLight);
                    }
                    break;
            }

            if (mirrored) canvas.MirrorHorizontal();
            return canvas;
        }

        /// <summary>
        /// Accessory: a rope coil, a map tube, a tool bag, a ring belt, a bead bracelet, an old
        /// stone seal. Deliberately mundane — the read is a crew at work, never a vestment.
        ///
        /// THE RULE THIS METHOD EXISTS TO KEEP: every variant sits where its catalogue anchor and
        /// its description say it sits, IN EVERY FACING. A wardrobe puts the words next to the
        /// picture, so an item called "no pulso direito" that drew on the left wrist read as a
        /// bug, and was one.
        ///
        /// WHY THIS METHOD DOES NOT MIRROR, and every other layer does. Left is drawn as a
        /// mirrored Right everywhere else because a body, a garment and a hairstyle are
        /// symmetric: reflecting them costs nothing. An accessory is not. Six of the six are
        /// anchored to one side or one face of the character — `shoulder_r`, `wrist_r`,
        /// `back_center`, `waist_front` — and reflecting a right shoulder produces a left
        /// shoulder. Mirroring made the rope coil change shoulders when the character turned
        /// round, which is the same defect as drawing it on the wrong side, only intermittent.
        /// So each Draw* below takes the TRUE facing and switches on all four.
        ///
        /// THE ENVELOPE every variant but the bracelet stays inside, and why:
        ///   - opaque pixels within x11..20, so the ink border OutlineOpaque adds lands within
        ///     x10..21 and never enters the arm columns (x7..9 and x22..24). Those columns
        ///     animate over y8..31 — the work swing lifts the arms to y8 — so an overlay pixel
        ///     there is a mark that stays put while the arm moves out from under it.
        ///   - top row at y15 or below, so the ink lands on y14, the neck row, and never on
        ///     y13, which is the jaw.
        ///
        /// THE BRACELET IS THE ONE EXEMPTION, and the exemption is what proves the rule: the
        /// catalogue anchors it to `wrist_r`, a wrist is on an arm, and across idle, walk and
        /// work the hand rows never once coincide, so no static row IS the wrist. It reads the
        /// live arm pose instead, which is the only reason this method takes an animation at all.
        ///
        /// WHICH PUTS A CONTRACT ON THE CALLER, and it is the one thing here that this file
        /// cannot keep on its own: <paramref name="anim"/> and <paramref name="frame"/> must be
        /// the pose the BODY is drawing on the same tick. Resolve the accessory layer once at
        /// frame 0 and then play the body's two frames under it and the beads land four rows off
        /// the wrist on every second walk frame, and beside an empty patch of sky on the second
        /// work frame — the drifting this overload exists to prevent, reintroduced one layer up.
        /// A caller that animates the body therefore has to ask this layer for both frames too,
        /// which for the runtime figure is what the per layer frame count in
        /// <c>CharacterAppearance</c> decides. The other five variants are identical across
        /// frames, so honouring the contract costs them nothing.
        /// </summary>
        public static PixelCanvas Accessory(int variant, ArtFacing facing, ArtAnim anim, int frame)
        {
            variant = Mathf.Clamp(variant, 0, AccessoryVariants - 1);
            frame = Mathf.Clamp(frame, 0, 1);

            PixelCanvas canvas = new PixelCanvas(Width, Height);

            switch (variant)
            {
                case 1: DrawMapTube(canvas, facing); break;
                case 2: DrawToolBag(canvas, facing); break;
                case 3: DrawRingBelt(canvas, facing); break;
                case 4: DrawBeadBracelet(canvas, facing, anim, frame); break;
                case 5: DrawOldSeal(canvas, facing); break;
                default: DrawRopeCoil(canvas, facing); break;
            }

            canvas.OutlineOpaque(ArtPalette.Ink);

            // Nothing in the accessory catalogue is worn on the head, so any pixel that reached
            // the head rectangle is spill — in practice the ink border of the bracelet when the
            // work swing lifts the wrist up beside the cheek. Clearing it here makes "never on
            // the face" structural instead of a thing each variant has to remember.
            for (int y = HeadY; y < HeadY + HeadH; y++)
                for (int x = HeadX; x < HeadX + HeadW; x++)
                    canvas.Set(x, y, ArtPalette.Transparent);

            return canvas;
        }

        /// <summary>
        /// Pose free overload. Only variant 4 differs between poses, so the other five are
        /// identical either way and this is not a second drawing path.
        ///
        /// It takes a facing and drops only the pose, because <c>ArtKeys.Accessory</c> carries a
        /// direction token: the four drawings above are reachable, and the creation screen's
        /// four-facing strip is what reaches them. What still resolves to Down is the wardrobe
        /// thumbnail and the backpack's stage figure, and there Down is a choice — a shop window
        /// is front-on, and each catalogue description has to be truest in that facing.
        /// </summary>
        public static PixelCanvas Accessory(int variant, ArtFacing facing)
        {
            return Accessory(variant, facing, ArtAnim.Idle, 0);
        }

        /// <summary>
        /// A solid band of <paramref name="width"/> whose left edge runs from (x0, y0) down to
        /// (x1, y1), stepped one row at a time.
        ///
        /// It exists because the obvious alternative is wrong: two parallel Bresenham lines a
        /// pixel apart do NOT tile a diagonal band. Wherever the two lines step sideways on
        /// different rows they leave a transparent hole between them, and OutlineOpaque then
        /// fills that hole with ink — which is why the map tube used to read as a dotted seam
        /// rather than a tube.
        /// </summary>
        static void DiagonalBand(PixelCanvas canvas, int x0, int y0, int x1, int y1,
            int width, Color32 near, Color32 far)
        {
            int rows = y1 - y0;
            for (int i = 0; i <= rows; i++)
            {
                // Integer division, truncating toward zero, so the shape is exactly reproducible.
                int x = rows == 0 ? x0 : x0 + (x1 - x0) * i / rows;
                for (int j = 0; j < width; j++) canvas.Set(x + j, y0 + i, j == 0 ? near : far);
            }
        }

        /// <summary>
        /// acc_rope_coil, catalogue anchor <c>shoulder_r</c>: "Enrolada no ombro direito." Adar's
        /// signature piece, and the one the eye should catch first at a distance.
        ///
        /// Which side of the sprite the character's right shoulder is on depends on where the
        /// viewer is standing, and getting that backwards is the defect this drawing replaces:
        /// face on, the character's right is the VIEWER'S LEFT; from behind, the viewer's right.
        /// In profile it is neither — it is the near shoulder or the far one, and that is a
        /// difference in how much of the coil you can see, not in where it sits.
        ///
        /// The mass is three hoops eight rows tall. It cannot break the silhouette sideways,
        /// because sideways is the arm columns; it breaks it upward instead, clearing the
        /// shoulder line at y16 by one row.
        /// </summary>
        static void DrawRopeCoil(PixelCanvas canvas, ArtFacing facing)
        {
            switch (facing)
            {
                case ArtFacing.Down:  // the right shoulder is the viewer's left
                    CoilStack(canvas, 11, 5);
                    DiagonalBand(canvas, 14, 23, 16, 26, 2, ArtPalette.ClayDark, ArtPalette.ClayDeep);
                    break;

                case ArtFacing.Up:    // the right shoulder is the viewer's right
                    CoilStack(canvas, 16, 5);
                    // Seen from behind, the loose end is not tucked away: it hangs down the back.
                    DiagonalBand(canvas, 16, 23, 14, 27, 2, ArtPalette.ClayDark, ArtPalette.ClayDeep);
                    break;

                case ArtFacing.Right: // the right shoulder is the near one: the coil is face on
                    CoilStack(canvas, 13, 6);
                    canvas.FillRect(18, 23, 2, 4, ArtPalette.ClayDark);
                    canvas.HLine(18, 23, 2, ArtPalette.ClayMid);
                    break;

                default:              // Left: the right shoulder is the far one, behind the body
                    // Only the bulk clears the shoulder line. That it nearly disappears here is
                    // the point — a coil on the shoulder you cannot see is a coil you cannot see.
                    canvas.FillRect(12, 15, 7, 4, ArtPalette.ClayMid);
                    canvas.HLine(12, 15, 7, ArtPalette.ClayPale);
                    canvas.HLine(12, 17, 7, ArtPalette.ClayDeep);   // one hoop edge, so it reads coiled
                    canvas.FillRect(18, 19, 2, 3, ArtPalette.ClayDark);  // a sliver past the back
                    break;
            }
        }

        /// <summary>Three hoops with a shaded edge between them, the top row at y15.</summary>
        static void CoilStack(PixelCanvas canvas, int x, int width)
        {
            for (int band = 0; band < 3; band++)
            {
                int top = 15 + band * 3;
                canvas.FillRect(x, top, width, 2, ArtPalette.ClayMid);
                canvas.HLine(x, top, width, ArtPalette.ClayPale);
                if (band < 2) canvas.HLine(x, top + 2, width, ArtPalette.ClayDeep);
            }
        }

        /// <summary>
        /// acc_map_tube, catalogue anchor <c>back_center</c>: "Atravessado nas costas." Neriah's
        /// signature piece, carried on a strap over the right shoulder and down to the left hip.
        ///
        /// A thing on the back is the clearest case for four separate drawings: from behind it is
        /// the whole item; face on the body hides it and the STRAP is what you see, with the
        /// barrel clearing the shoulder; in profile the tube runs down whichever edge of the
        /// sprite the back happens to be on, which is screen left facing right and screen right
        /// facing left. It used to draw the barrel at head height in three of the four, over the
        /// jaw, which is both anatomically wrong and the one place this layer may not go.
        /// </summary>
        static void DrawMapTube(PixelCanvas canvas, ArtFacing facing)
        {
            switch (facing)
            {
                case ArtFacing.Up:    // the back faces the viewer: all tube
                    TubeCap(canvas, 16);
                    DiagonalBand(canvas, 16, 18, 11, 28, 4, ArtPalette.StoneLight, ArtPalette.StoneMid);
                    canvas.VLine(19, 15, 4, ArtPalette.ClayDark);   // the strap, over the shoulder
                    canvas.VLine(20, 15, 4, ArtPalette.ClayDeep);
                    break;

                case ArtFacing.Down:  // the tube is behind the body: the strap is what shows
                    TubeCap(canvas, 11);
                    canvas.FillRect(11, 18, 3, 2, ArtPalette.StoneDark);  // the barrel, before it goes behind
                    DiagonalBand(canvas, 13, 20, 18, 29, 2, ArtPalette.ClayDark, ArtPalette.ClayDeep);
                    break;

                case ArtFacing.Right: // the back is screen left
                    TubeCap(canvas, 11);
                    TubeBarrel(canvas, 11, ArtPalette.StoneLight, ArtPalette.StoneDark);
                    DiagonalBand(canvas, 14, 18, 17, 25, 2, ArtPalette.ClayDark, ArtPalette.ClayDeep);
                    break;

                default:              // Left: the back is screen right
                    TubeCap(canvas, 18);
                    TubeBarrel(canvas, 18, ArtPalette.StoneDark, ArtPalette.StoneLight);
                    DiagonalBand(canvas, 16, 18, 13, 25, 2, ArtPalette.ClayDark, ArtPalette.ClayDeep);
                    break;
            }
        }

        /// <summary>The tube's capped end, clearing the shoulder line without reaching the jaw.</summary>
        static void TubeCap(PixelCanvas canvas, int x)
        {
            canvas.FillRect(x, 15, 3, 2, ArtPalette.StonePale);
            canvas.HLine(x, 17, 3, ArtPalette.StoneMid);
        }

        /// <summary>
        /// The barrel down the back edge in profile. The lit column is the one facing the
        /// viewer's side of the body, which swaps between the two profiles.
        /// </summary>
        static void TubeBarrel(PixelCanvas canvas, int x, Color32 leftEdge, Color32 rightEdge)
        {
            canvas.FillRect(x, 17, 3, 11, ArtPalette.StoneMid);
            canvas.VLine(x, 17, 11, leftEdge);
            canvas.VLine(x + 2, 17, 11, rightEdge);
        }

        /// <summary>
        /// acc_tool_bag, catalogue anchor <c>waist_side</c>: "Na lateral da cintura." It hangs
        /// from the character's LEFT hip — the anchor names a side without saying which, and
        /// picking one and keeping it is what stops the bag swapping hips as the player turns.
        ///
        /// The cross body strap this shape used to carry is gone on purpose: that silhouette
        /// belongs to acc_map_tube, and a bag sitting on the belt does not hang from the opposite
        /// shoulder. It also used to sit at mid torso, which read as a chest pouch; the belt line
        /// is y28.
        /// </summary>
        static void DrawToolBag(PixelCanvas canvas, ArtFacing facing)
        {
            switch (facing)
            {
                case ArtFacing.Down:  // the left hip is the viewer's right
                    BagBody(canvas, 16, 5, false);
                    BagHanger(canvas, 17);
                    break;

                case ArtFacing.Up:    // the left hip is the viewer's left
                    BagBody(canvas, 11, 5, false);
                    BagHanger(canvas, 13);
                    break;

                case ArtFacing.Left:  // the left hip is the near one: the bag is face on, and widest
                    BagBody(canvas, 13, 7, true);
                    BagHanger(canvas, 16);
                    break;

                default:              // Right: the far hip, so only the back of the bag clears the body
                    canvas.FillRect(11, 29, 3, 5, ArtPalette.ClayDark);
                    canvas.HLine(11, 29, 3, ArtPalette.ClayMid);
                    canvas.HLine(11, 33, 3, ArtPalette.ClayDeep);
                    break;
            }
        }

        static void BagBody(PixelCanvas canvas, int x, int width, bool buckle)
        {
            canvas.FillRect(x, 28, width, 7, ArtPalette.ClayMid);
            canvas.HLine(x, 28, width, ArtPalette.ClayLight);
            canvas.HLine(x, 30, width, ArtPalette.ClayDark);   // the flap
            canvas.HLine(x, 34, width, ArtPalette.ClayDeep);
            if (!buckle) return;
            canvas.Set(x + width / 2, 32, ArtPalette.StoneLight);
            canvas.Set(x + width / 2 + 1, 32, ArtPalette.StoneMid);
        }

        /// <summary>The loop that ties the pouch up to the belt line.</summary>
        static void BagHanger(PixelCanvas canvas, int x)
        {
            canvas.FillRect(x, 26, 2, 2, ArtPalette.ClayDark);
        }

        /// <summary>
        /// acc_ring_belt, catalogue anchor <c>waist_front</c>: "Argolas de ferro na frente da
        /// cintura." The band goes all the way round, so it is the only accessory whose main
        /// shape is the same in every facing — but the RINGS are on the front, and the front is
        /// somewhere different in each of the four. It used to draw them dead centre in profile,
        /// where the belly is, and identically front and side.
        ///
        /// The band is trimmed to x11..20 rather than the full torso width, which is a fix and
        /// not a restyle: at the full width its ink border ran a pixel into each arm, breaking
        /// the one rule the layering scheme rests on.
        /// </summary>
        static void DrawRingBelt(PixelCanvas canvas, ArtFacing facing)
        {
            canvas.FillRect(11, 28, 10, 3, ArtPalette.ClayDark);
            canvas.HLine(11, 28, 10, ArtPalette.ClayMid);

            switch (facing)
            {
                case ArtFacing.Down:
                    DrawBeltRing(canvas, 13);
                    DrawBeltRing(canvas, 17);
                    break;

                case ArtFacing.Up:    // the rings are at the front: from behind, the knot is what shows
                    canvas.FillRect(15, 27, 2, 5, ArtPalette.ClayDark);
                    canvas.HLine(15, 27, 2, ArtPalette.ClayMid);
                    break;

                case ArtFacing.Right: // the front is screen right, and one ring occludes the other
                    DrawBeltRing(canvas, 18);
                    canvas.FillRect(18, 31, 2, 3, ArtPalette.ClayDark);   // the strap tongue
                    break;

                default:              // Left: the front is screen left
                    DrawBeltRing(canvas, 12);
                    canvas.FillRect(12, 31, 2, 3, ArtPalette.ClayDark);
                    break;
            }
        }

        static void DrawBeltRing(PixelCanvas canvas, int x)
        {
            canvas.FillRect(x, 28, 2, 2, ArtPalette.StoneLight);
            canvas.Set(x + 1, 29, ArtPalette.StoneDeep);   // the hole through it
        }

        /// <summary>
        /// acc_bead_bracelet, catalogue anchor <c>wrist_r</c>: "Contas de barro num cordão, no
        /// pulso direito." The one accessory that cannot be a still, and the one allowed into an
        /// arm column.
        ///
        /// The wrist is immediately beside the hand, the hand moves in every pose, and the hand
        /// rows across idle, walk and work share no row at all — so there is no pixel that is the
        /// wrist in general. The beads take their rows from the same <see cref="ArmPose"/> the
        /// body draws from: below the hand when the arm hangs, above it when the work swing
        /// raises it, which is where a wrist actually is in each case.
        ///
        /// WHICH COLUMN carries the character's right arm is the part that was wrong, and the
        /// table below is not symmetric, so it is worth reading rather than guessing:
        ///
        ///   Down   x7..9    the character's right is the viewer's left
        ///   Up     x22..24  seen from behind, it is the viewer's right
        ///   Right  x22..24  the near arm, since the character faces screen right
        ///   Left   x22..24  the FAR arm — the body mirrors, so the arm the pose offsets as the
        ///                   left one is the arm that lands on x22..24
        ///
        /// and the <c>leftArm</c> flag follows the column rather than the facing, because it
        /// selects which of ArmPose's two offsets that column was drawn with. Getting it wrong is
        /// four rows out on every walk frame.
        ///
        /// KNOWN AND ACCEPTED: build 1 pulls both arms a pixel inward and this layer does not
        /// know the build, so on the narrower frame the band overhangs the forearm by one pixel.
        /// The alternatives were a key format change or packing accessory against build the way
        /// the body packs build against tone, and neither buys enough to be worth it.
        /// </summary>
        static void DrawBeadBracelet(PixelCanvas canvas, ArtFacing facing, ArtAnim anim, int frame)
        {
            int x;
            bool leftArm;
            if (facing == ArtFacing.Down) { x = ArmLX; leftArm = true; }
            else if (facing == ArtFacing.Left) { x = ArmRX; leftArm = true; }
            else { x = ArmRX; leftArm = false; }

            // Facing left, the beads are on the arm the body itself draws in shade, so they are
            // shaded too. It is free differentiation, and it is also just correct.
            bool far = facing == ArtFacing.Left;
            Color32 bead = far ? ArtPalette.ClayDark : ArtPalette.ClayMid;
            Color32 lit = far ? ArtPalette.ClayMid : ArtPalette.ClayLight;

            int handTop = HandTop(anim, frame, leftArm);
            int wristTop = HandsRaised(anim, frame) ? handTop + 3 : handTop - 2;

            for (int i = 0; i < ArmW; i++)
            {
                canvas.Set(x + i, wristTop, i % 2 == 0 ? bead : lit);
                canvas.Set(x + i, wristTop + 1, ArtPalette.ClayDeep);
            }

            // The cord is knotted at the back of the wrist, so the knot is visible from behind
            // and from nowhere else.
            if (facing == ArtFacing.Up) canvas.Set(x + 1, wristTop - 1, ArtPalette.ClayDeep);
        }

        /// <summary>
        /// acc_old_seal, catalogue anchor <c>neck</c>: a small stone seal on a cord, turned up in
        /// the foundation. Worn smooth — the single dark pixel on the disc is an engraving nobody
        /// can read any more, and that is both why the piece is interesting and why it carries no
        /// symbol of any kind.
        ///
        /// The cord is the constant; the disc is what moves. It hangs on the chest, so in profile
        /// it swings to whichever edge of the sprite the chest is on, and from behind it is out of
        /// sight altogether and the knot at the nape is all there is.
        /// </summary>
        static void DrawOldSeal(PixelCanvas canvas, ArtFacing facing)
        {
            switch (facing)
            {
                case ArtFacing.Up:    // the disc is against the chest; only the knot shows
                    canvas.FillRect(15, 15, 2, 2, ArtPalette.ClayDeep);
                    canvas.Line(12, 18, 15, 16, ArtPalette.ClayDeep);
                    canvas.Line(19, 18, 16, 16, ArtPalette.ClayDeep);
                    break;

                case ArtFacing.Down:  // the cord sits on the collarbones, the disc mid chest
                    canvas.Line(12, 17, 15, 21, ArtPalette.ClayDeep);
                    canvas.Line(15, 21, 19, 17, ArtPalette.ClayDeep);
                    SealDisc(canvas, 14, 21);
                    break;

                case ArtFacing.Right: // the chest is screen right
                    canvas.Line(15, 16, 18, 20, ArtPalette.ClayDeep);
                    canvas.Line(15, 17, 17, 20, ArtPalette.ClayDeep);
                    SealDisc(canvas, 17, 20);
                    break;

                default:              // Left: the chest is screen left
                    canvas.Line(16, 16, 13, 20, ArtPalette.ClayDeep);
                    canvas.Line(16, 17, 14, 20, ArtPalette.ClayDeep);
                    SealDisc(canvas, 12, 20);
                    break;
            }
        }

        static void SealDisc(PixelCanvas canvas, int x, int y)
        {
            canvas.FillRect(x, y, 3, 3, ArtPalette.StoneMid);
            canvas.HLine(x, y, 3, ArtPalette.StoneLight);
            canvas.Set(x + 1, y + 1, ArtPalette.StoneDeep);
        }
    }
}
