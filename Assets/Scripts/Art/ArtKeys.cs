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

        public const string PropRubble = "prop_rubble";
        public const string PropWell = "prop_well";

        public const string UiPanel = "ui_panel";
        public const string UiBubble = "ui_bubble";
        public const string UiButton = "ui_button";

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

        public static string Accessory(int variant)
        {
            return AccessoryPrefix + "_" + variant.ToString(CultureInfo.InvariantCulture);
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
