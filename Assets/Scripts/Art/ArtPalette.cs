using UnityEngine;

namespace SheepGate.Art
{
    /// <summary>
    /// The complete color vocabulary of the game: three base families plus a neutral ramp.
    /// Nothing outside this file may invent a color.
    ///
    /// Base 1 - stone: dusty, bleached limestone. The wall, the ground, the tools.
    /// Base 2 - clay:  warm terracotta. Brick, timber, garments, skin.
    /// Base 3 - teal:  muted, desaturated water. Water, shade, a few garments.
    ///
    /// Skin and hair are derived from the clay and neutral ramps on purpose, so the whole
    /// game still reads as three base colors plus neutrals.
    ///
    /// Deliberately absent: any warm ceremonial glow, any symbolic iconography.
    /// The intended read is a ruined city at work, not a religious illustration.
    /// </summary>
    public static class ArtPalette
    {
        public static readonly Color32 Transparent = new Color32(0, 0, 0, 0);

        // ---- Neutrals -------------------------------------------------------
        public static readonly Color32 Ink     = new Color32(30, 28, 26, 255);
        public static readonly Color32 Shadow  = new Color32(58, 54, 47, 255);
        public static readonly Color32 Neutral = new Color32(124, 118, 104, 255);
        public static readonly Color32 Light   = new Color32(217, 211, 196, 255);
        public static readonly Color32 Paper   = new Color32(239, 233, 218, 255);

        /// <summary>Translucent ink, for shading an already painted area.</summary>
        public static readonly Color32 SoftShade = new Color32(30, 28, 26, 64);

        /// <summary>Translucent light, for a subtle highlight pass.</summary>
        public static readonly Color32 SoftLight = new Color32(239, 233, 218, 56);

        // ---- Base 1: dusty stone --------------------------------------------
        public static readonly Color32 StoneDeep  = new Color32(74, 70, 57, 255);
        public static readonly Color32 StoneDark  = new Color32(110, 104, 87, 255);
        public static readonly Color32 StoneMid   = new Color32(146, 138, 115, 255);
        public static readonly Color32 StoneLight = new Color32(183, 175, 151, 255);
        public static readonly Color32 StonePale  = new Color32(210, 203, 180, 255);

        // ---- Base 2: clay ----------------------------------------------------
        public static readonly Color32 ClayDeep  = new Color32(90, 47, 34, 255);
        public static readonly Color32 ClayDark  = new Color32(138, 74, 49, 255);
        public static readonly Color32 ClayMid   = new Color32(176, 106, 69, 255);
        public static readonly Color32 ClayLight = new Color32(206, 143, 99, 255);
        public static readonly Color32 ClayPale  = new Color32(227, 177, 137, 255);

        // ---- Base 3: muted teal ----------------------------------------------
        public static readonly Color32 TealDeep  = new Color32(31, 58, 56, 255);
        public static readonly Color32 TealDark  = new Color32(51, 96, 90, 255);
        public static readonly Color32 TealMid   = new Color32(76, 133, 124, 255);
        public static readonly Color32 TealLight = new Color32(111, 167, 155, 255);
        public static readonly Color32 TealPale  = new Color32(154, 198, 185, 255);

        // ---- Ramps, dark to light --------------------------------------------
        public static readonly Color32[] StoneRamp = { StoneDeep, StoneDark, StoneMid, StoneLight, StonePale };
        public static readonly Color32[] ClayRamp  = { ClayDeep, ClayDark, ClayMid, ClayLight, ClayPale };
        public static readonly Color32[] TealRamp  = { TealDeep, TealDark, TealMid, TealLight, TealPale };

        /// <summary>Ground speckle: earth with a little clay in it.</summary>
        public static readonly Color32[] GroundRamp = { StoneDark, StoneMid, StoneMid, StoneLight, ClayDark };

        // ---- Derived skin and hair --------------------------------------------
        public static readonly Color32 SkinALight = new Color32(240, 199, 162, 255);
        public static readonly Color32 SkinABase  = ClayPale;
        public static readonly Color32 SkinAShade = ClayLight;
        public static readonly Color32 SkinADeep  = ClayDark;
        public static readonly Color32 HairA      = new Color32(78, 52, 36, 255);
        public static readonly Color32 HairAShade = new Color32(54, 36, 26, 255);

        public static readonly Color32 SkinBLight = new Color32(196, 128, 84, 255);
        public static readonly Color32 SkinBBase  = new Color32(169, 103, 63, 255);
        public static readonly Color32 SkinBShade = new Color32(126, 74, 45, 255);
        public static readonly Color32 SkinBDeep  = ClayDeep;
        public static readonly Color32 HairB      = new Color32(46, 42, 36, 255);
        public static readonly Color32 HairBShade = new Color32(32, 29, 25, 255);

        /// <summary>One skin tone: base, highlight, shade and the deepest line.</summary>
        public struct SkinTone
        {
            public Color32 Base;
            public Color32 Light;
            public Color32 Shade;
            public Color32 Deep;

            public SkinTone(Color32 baseColour, Color32 light, Color32 shade, Color32 deep)
            {
                Base = baseColour; Light = light; Shade = shade; Deep = deep;
            }
        }

        /// <summary>
        /// Four tones, light to deep. The first two are the pair the game shipped with, kept so
        /// existing saves keep the face they chose; the other two extend the range downward.
        /// </summary>
        public static readonly SkinTone[] SkinTones =
        {
            new SkinTone(SkinABase, SkinALight, SkinAShade, SkinADeep),
            new SkinTone(SkinBBase, SkinBLight, SkinBShade, SkinBDeep),
            new SkinTone(new Color32(138, 84, 52, 255), new Color32(163, 103, 66, 255),
                         new Color32(104, 61, 38, 255), new Color32(74, 42, 26, 255)),
            new SkinTone(new Color32(96, 58, 38, 255), new Color32(120, 75, 50, 255),
                         new Color32(70, 41, 26, 255), new Color32(48, 28, 18, 255))
        };

        /// <summary>Hair, as a colour pair. Index is the player's hair choice.</summary>
        public static readonly Color32[] HairColours =
        {
            HairA,
            HairB,
            new Color32(122, 84, 44, 255),      // lighter brown
            new Color32(88, 88, 92, 255)        // grey
        };

        public static readonly Color32[] HairShades =
        {
            HairAShade,
            HairBShade,
            new Color32(88, 58, 30, 255),
            new Color32(62, 62, 66, 255)
        };

        /// <summary>Debug-only marker for an unknown art key. Never part of shipped art.</summary>
        public static readonly Color32 Missing = new Color32(255, 0, 200, 255);

        public static Color32 Lerp(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32(
                (byte)Mathf.RoundToInt(a.r + (b.r - a.r) * t),
                (byte)Mathf.RoundToInt(a.g + (b.g - a.g) * t),
                (byte)Mathf.RoundToInt(a.b + (b.b - a.b) * t),
                (byte)Mathf.RoundToInt(a.a + (b.a - a.a) * t));
        }

        /// <summary>Component-wise multiply, used by the NPC palette swap.</summary>
        public static Color32 Multiply(Color32 source, Color tint)
        {
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(source.r * tint.r), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(source.g * tint.g), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(source.b * tint.b), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(source.a * tint.a), 0, 255));
        }
    }
}
