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
    /// Only three regions animate, and no overlay ever covers them:
    ///   - the arms, outside the torso columns
    ///   - the boots, below the lowest trouser hem
    ///   - the work tool
    /// Anything added here must respect that split or the overlays will drift.
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
        public const int HairVariants = 4;
        public const int TopVariants = 4;
        public const int LegsVariants = 4;
        public const int AccessoryVariants = 4;

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
        /// Hair, lifted out of the head so it can be chosen apart from skin. Four looks, each with
        /// its own colour, drawn to sit on the same head rectangle the body paints.
        /// </summary>
        public static PixelCanvas Hair(int variant, ArtFacing facing)
        {
            variant = Mathf.Clamp(variant, 0, HairVariants - 1);
            bool mirrored = facing == ArtFacing.Left;
            ArtFacing drawn = mirrored ? ArtFacing.Right : facing;

            Color32 hair = ArtPalette.HairColours[variant];
            Color32 shade = ArtPalette.HairShades[variant];

            PixelCanvas canvas = new PixelCanvas(Width, Height);

            switch (drawn)
            {
                case ArtFacing.Up:
                    // From behind, every style is a full back of the head.
                    canvas.FillRect(HeadX, HeadY, HeadW, 9, hair);
                    canvas.HLine(HeadX, HeadY + 8, HeadW, shade);
                    if (variant == 2) canvas.FillRect(HeadX + 2, HeadY + 9, HeadW - 4, 3, hair);
                    break;

                case ArtFacing.Right:
                    canvas.FillRect(HeadX, HeadY, HeadW, 4, hair);
                    canvas.FillRect(HeadX, HeadY, 6, 9, hair);
                    canvas.HLine(HeadX, HeadY + 8, 6, shade);
                    if (variant == 2) canvas.FillRect(HeadX, HeadY + 8, 4, 5, hair);       // longer
                    if (variant == 3) canvas.HLine(HeadX + 1, HeadY, HeadW - 2, shade);    // receding
                    break;

                default: // Down
                    canvas.FillRect(HeadX, HeadY, HeadW, 4, hair);
                    canvas.VLine(HeadX, HeadY, 7, hair);
                    canvas.VLine(HeadX + HeadW - 1, HeadY, 7, hair);
                    canvas.HLine(HeadX + 1, HeadY + 3, HeadW - 2, shade);

                    switch (variant)
                    {
                        case 1: // cropped: no sides below the brow
                            canvas.VLine(HeadX, HeadY + 4, 3, ArtPalette.Transparent);
                            canvas.VLine(HeadX + HeadW - 1, HeadY + 4, 3, ArtPalette.Transparent);
                            break;
                        case 2: // long: down past the jaw on both sides
                            canvas.VLine(HeadX, HeadY, 12, hair);
                            canvas.VLine(HeadX + HeadW - 1, HeadY, 12, hair);
                            canvas.VLine(HeadX + 1, HeadY + 7, 5, shade);
                            canvas.VLine(HeadX + HeadW - 2, HeadY + 7, 5, shade);
                            break;
                        case 3: // covered: a worker's head cloth rather than a style
                            canvas.FillRect(HeadX - 1, HeadY, HeadW + 2, 6, hair);
                            canvas.HLine(HeadX - 1, HeadY + 5, HeadW + 2, shade);
                            canvas.VLine(HeadX - 1, HeadY, 9, hair);
                            canvas.VLine(HeadX + HeadW, HeadY, 9, hair);
                            break;
                    }

                    break;
            }

            if (mirrored) canvas.MirrorHorizontal();
            return canvas;
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
        /// Accessory. Confined to the head crown and the waist, both static regions, and
        /// deliberately mundane: a cap, a band, a strap, a belt. Nothing ceremonial.
        /// </summary>
        public static PixelCanvas Accessory(int variant, ArtFacing facing)
        {
            variant = Mathf.Clamp(variant, 0, AccessoryVariants - 1);
            bool mirrored = facing == ArtFacing.Left;

            PixelCanvas canvas = new PixelCanvas(Width, Height);

            switch (variant)
            {
                case 1: // headband
                    canvas.FillRect(HeadX, HeadY + 3, HeadW, 2, ArtPalette.ClayDark);
                    canvas.HLine(HeadX, HeadY + 3, HeadW, ArtPalette.ClayMid);
                    canvas.FillRect(HeadX - 2, HeadY + 3, 2, 2, ArtPalette.ClayDark);
                    canvas.Set(HeadX - 2, HeadY + 5, ArtPalette.ClayDeep);
                    break;

                case 2: // carrying strap and hip pouch
                    canvas.Line(TorsoX + 1, TorsoY + 1, TorsoX + TorsoW - 3, TorsoY + 11, ArtPalette.ClayDark);
                    canvas.Line(TorsoX + 2, TorsoY + 1, TorsoX + TorsoW - 2, TorsoY + 11, ArtPalette.ClayDeep);
                    canvas.FillRect(17, 25, 5, 5, ArtPalette.ClayMid);
                    canvas.HLine(17, 25, 5, ArtPalette.ClayLight);
                    canvas.HLine(17, 29, 5, ArtPalette.ClayDeep);
                    break;

                case 3: // wide belt with a tool loop
                    canvas.FillRect(TorsoX - 1, 28, TorsoW + 2, 3, ArtPalette.ClayDark);
                    canvas.HLine(TorsoX - 1, 28, TorsoW + 2, ArtPalette.ClayMid);
                    canvas.FillRect(15, 28, 2, 3, ArtPalette.StoneLight);
                    canvas.FillRect(19, 31, 2, 4, ArtPalette.ClayDeep);
                    break;

                default: // flat work cap
                    canvas.FillRect(HeadX - 1, HeadY, HeadW + 2, 4, ArtPalette.StoneDark);
                    canvas.HLine(HeadX - 1, HeadY, HeadW + 2, ArtPalette.StoneMid);
                    canvas.HLine(HeadX - 2, HeadY + 4, HeadW + 4, ArtPalette.StoneDeep);
                    break;
            }

            canvas.OutlineOpaque(ArtPalette.Ink);
            if (mirrored) canvas.MirrorHorizontal();
            return canvas;
        }
    }
}
