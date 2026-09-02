using System.Globalization;
using UnityEngine;

namespace SheepGate.Art
{
    /// <summary>
    /// Every sprite key the game may ask ArtLibrary for, plus the builders and the parser
    /// that turn a key into the parameters of a drawing routine.
    ///
    /// Character part keys are parsed permissively on purpose, because the contract and the
    /// animation code describe them at different levels of detail. All of these are valid and
    /// resolve to the same family:
    ///     body_0                      -> variant 0, facing down, idle, frame 0
    ///     body_0_left                 -> variant 0, facing left, idle, frame 0
    ///     body_0_left_1               -> variant 0, facing left, idle, frame 1
    ///     body_1_right_walk_0         -> variant 1, facing right, walk, frame 0
    ///     top_2 / top_2_up / top_2_up_work_1
    /// </summary>
    public static class ArtKeys
    {
        public const string TileGround = "tile_ground";
        public const string TileRubble = "tile_rubble";
        public const string TileWater = "tile_water";
        public const string TileHouse = "tile_house";

        /// <summary>
        /// The wall lying where it fell: the terrain of the ruin band outside the city.
        ///
        /// Not <see cref="TileRubble"/>. Rubble is a cell the player walks onto and clears, and
        /// two things that mean opposite things must not draw the same pixels — this repository
        /// has been bitten by that before. See <see cref="Art.TileArt.FallenWall"/>.
        /// </summary>
        public const string TileFallenWall = "tile_fallen_wall";

        public const string PropRubble = "prop_rubble";
        public const string PropWell = "prop_well";
        public const string PropMat = "prop_mat";

        /// <summary>
        /// The three original frames, kept at their 32 pixel geometry because fifteen screens are
        /// laid out against the sizes they produce. Repainted neutral like everything else, so a
        /// tint now lands on them exactly; see <see cref="UiArt"/> for why that matters.
        /// </summary>
        public const string UiPanel = "ui_panel";
        public const string UiBubble = "ui_bubble";
        public const string UiButton = "ui_button";

        /// <summary>
        /// The Sistema Vale frames, one per design radius. Nine-sliced, painted neutral so
        /// <c>Image.color</c> yields the token colour exactly, and generated at the size they
        /// render at so a corner is a curve rather than a staircase.
        /// </summary>
        public const string UiFrameSm = "ui_frame_sm";
        public const string UiFrameMd = "ui_frame_md";
        public const string UiFrameLg = "ui_frame_lg";

        /// <summary>
        /// The pergaminho card of variation 1b: radius Lg with a soft elevated edge instead of a
        /// border. Its visible body is inset by <see cref="UiArt.ScrollHalo"/> on every side.
        /// </summary>
        public const string UiFrameScroll = "ui_frame_scroll";

        /// <summary>
        /// The focus outline, identical on every button variant including ghost. Stretch it to the
        /// control's rect expanded by <see cref="UiArt.FocusOutset"/> on each side.
        /// </summary>
        public const string UiFocusRing = "ui_focus_ring";

        /// <summary>Pill ended progress bar, in two halves. Draw them at <see cref="UiArt.BarHeight"/>.</summary>
        public const string UiBarTrack = "ui_bar_track";
        public const string UiBarFill = "ui_bar_fill";

        /// <summary>
        /// The six status icons.
        ///
        /// These are sprites and not text because none of the three bundled font families carries
        /// the characters the design mocks use for them. Substituting a lookalike character for a
        /// missing glyph is what the design system's iconography note forbids, so the icons are
        /// drawn: 2 point stroke, 2 point corners, on a 24 grid.
        /// </summary>
        public const string IconCheck = "ui_icon_check";
        public const string IconClose = "ui_icon_close";
        public const string IconDot = "ui_icon_dot";
        public const string IconArrow = "ui_icon_arrow";
        public const string IconMenu = "ui_icon_menu";
        public const string IconLock = "ui_icon_lock";
        public const string IconBag = "ui_icon_bag";
        public const string IconHelp = "ui_icon_help";
        public const string IconCoin = "ui_icon_coin";
        public const string IconCalendarCheck = "ui_icon_calendar_check";

        public const string WallPrefix = "wall_";
        public const string BodyPrefix = "body";
        public const string TopPrefix = "top";
        public const string LegsPrefix = "legs";
        public const string AccessoryPrefix = "acc";
        public const string HairPrefix = "hair";

        /// <summary>Wall stages 0 through 4 inclusive.</summary>
        public const int WallStageCount = 5;

        /// <summary>Direction tokens, in ArtFacing order.</summary>
        public static readonly string[] Directions = { "down", "up", "left", "right" };

        /// <summary>Animation tokens, in ArtAnim order.</summary>
        public static readonly string[] Animations = { "idle", "walk", "work" };

        /// <summary>
        /// How many ground tiles the library can produce. One tile repeated over a whole map
        /// lattices: the noise hides it only while the noise is loud enough to be camouflage, so
        /// the way out is several tiles, not a busier one.
        /// </summary>
        public const int GroundVariantCount = 6;

        /// <summary>Key for one of the ground variants. Variant 0 is the plain key.</summary>
        public static string GroundVariant(int variant)
        {
            int clamped = variant <= 0 ? 0 : variant % GroundVariantCount;
            return clamped == 0 ? TileGround : TileGround + "_" + clamped;
        }

        /// <summary>
        /// How many fallen-wall tiles the library can produce, and twice the ground's count for a
        /// reason: a ground tile carries nothing but noise, so a repeat is invisible, whereas
        /// these carry block silhouettes the eye can match from across the screen. The ruin band
        /// is around two hundred cells, so twelve keeps any one tile down to roughly sixteen
        /// appearances.
        /// </summary>
        public const int FallenWallVariantCount = 12;

        /// <summary>Key for one of the fallen-wall variants. Variant 0 is the plain key.</summary>
        public static string FallenWallVariant(int variant)
        {
            int clamped = variant <= 0 ? 0 : variant % FallenWallVariantCount;
            return clamped == 0 ? TileFallenWall : TileFallenWall + "_" + clamped;
        }

        public static string Wall(int stage)
        {
            return WallPrefix + Mathf.Clamp(stage, 0, WallStageCount - 1).ToString(CultureInfo.InvariantCulture);
        }

        public static string Body(int variant, ArtFacing facing, ArtAnim anim, int frame)
        {
            return Part(BodyPrefix, variant, facing, anim, frame);
        }

        public static string Top(int variant)
        {
            return TopPrefix + "_" + variant.ToString(CultureInfo.InvariantCulture);
        }

        public static string Legs(int variant)
        {
            return LegsPrefix + "_" + variant.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Hair layer key: hair_&lt;variant&gt;_&lt;direction&gt;.</summary>
        public static string Hair(int variant, ArtFacing facing)
        {
            return Part(HairPrefix, variant, facing, ArtAnim.Idle, 0);
        }

        /// <summary>
        /// Accessory layer key: acc_&lt;variant&gt;_&lt;direction&gt;_idle_0.
        ///
        /// The direction token is not decoration. Alone among the worn layers, the accessory is
        /// never mirrored — every variant is anchored to one side or one face of the character
        /// (<c>shoulder_r</c>, <c>wrist_r</c>, <c>back_center</c>, <c>waist_front</c>), so
        /// <see cref="CharacterArt.Accessory"/> draws all four facings by hand. A key without the
        /// token parses back as Down, which fed those four drawings one facing and threw the other
        /// three away — the map tube across the back rendered as the front view of the map tube.
        /// So this builder is shaped like <see cref="Hair"/> and takes the facing, rather than like
        /// <see cref="Top"/> and <see cref="Legs"/>, whose layers really are facing-free.
        /// </summary>
        public static string Accessory(int variant, ArtFacing facing)
        {
            return Part(AccessoryPrefix, variant, facing, ArtAnim.Idle, 0);
        }

        public static string Part(string prefix, int variant, ArtFacing facing, ArtAnim anim, int frame)
        {
            return prefix + "_"
                 + variant.ToString(CultureInfo.InvariantCulture) + "_"
                 + Directions[(int)facing] + "_"
                 + Animations[(int)anim] + "_"
                 + Mathf.Clamp(frame, 0, 1).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Splits a character part key. Returns false when the key does not belong to prefix.
        /// Missing trailing tokens fall back to down / idle / frame 0.
        /// </summary>
        public static bool TryParsePart(string key, string prefix, int variantCount,
            out int variant, out ArtFacing facing, out ArtAnim anim, out int frame)
        {
            variant = 0;
            facing = ArtFacing.Down;
            anim = ArtAnim.Idle;
            frame = 0;

            if (string.IsNullOrEmpty(key)) return false;
            if (!key.StartsWith(prefix + "_", System.StringComparison.Ordinal)) return false;

            string[] tokens = key.Split('_');
            if (tokens.Length < 2) return false;
            if (!TryParseInt(tokens[1], out variant)) return false;
            variant = Mathf.Clamp(variant, 0, Mathf.Max(0, variantCount - 1));

            int index = 2;
            if (index < tokens.Length && TryParseFacing(tokens[index], out facing)) index++;
            if (index < tokens.Length && TryParseAnim(tokens[index], out anim)) index++;
            if (index < tokens.Length)
            {
                int parsed;
                if (TryParseInt(tokens[index], out parsed)) frame = parsed;
            }
            frame = Mathf.Clamp(frame, 0, 1);
            return true;
        }

        public static bool TryParseFacing(string token, out ArtFacing facing)
        {
            facing = ArtFacing.Down;
            if (string.IsNullOrEmpty(token)) return false;
            for (int i = 0; i < Directions.Length; i++)
            {
                if (string.Equals(token, Directions[i], System.StringComparison.Ordinal))
                {
                    facing = (ArtFacing)i;
                    return true;
                }
            }
            return false;
        }

        public static bool TryParseAnim(string token, out ArtAnim anim)
        {
            anim = ArtAnim.Idle;
            if (string.IsNullOrEmpty(token)) return false;
            for (int i = 0; i < Animations.Length; i++)
            {
                if (string.Equals(token, Animations[i], System.StringComparison.Ordinal))
                {
                    anim = (ArtAnim)i;
                    return true;
                }
            }
            return false;
        }

        public static bool TryParseInt(string token, out int value)
        {
            return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
