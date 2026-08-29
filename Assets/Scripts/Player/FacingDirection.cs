using UnityEngine;

namespace SheepGate.Player
{
    /// <summary>
    /// The four cardinal facings used by character art and by movement. The order matches the
    /// art library's own facing order, so the two stay castable if they ever meet; sprite keys
    /// are built from <see cref="FacingDirectionExtensions.ToKey"/> names, never from the index.
    /// </summary>
    public enum FacingDirection
    {
        Down = 0,
        Up = 1,
        Left = 2,
        Right = 3
    }

    public static class FacingDirectionExtensions
    {
        /// <summary>Lowercase name used when building sprite keys.</summary>
        public static string ToKey(this FacingDirection direction)
        {
            switch (direction)
            {
                case FacingDirection.Up: return "up";
                case FacingDirection.Left: return "left";
                case FacingDirection.Right: return "right";
                default: return "down";
            }
        }

        /// <summary>
        /// Dominant-axis facing for a movement delta. Vertical wins ties so that a diagonal
        /// approach reads as walking up or down rather than flickering between axes.
        /// </summary>
        public static FacingDirection FromDelta(Vector2 delta, FacingDirection fallback)
        {
            if (delta.sqrMagnitude < 0.000001f) return fallback;
            if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
            {
                return delta.x > 0f ? FacingDirection.Right : FacingDirection.Left;
            }
            return delta.y > 0f ? FacingDirection.Up : FacingDirection.Down;
        }
    }
}
