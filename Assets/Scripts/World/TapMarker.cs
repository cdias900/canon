using System.Collections;
using SheepGate.UI;
using UnityEngine;

namespace SheepGate.World
{
    /// <summary>
    /// A ring on the ground where the player just tapped.
    ///
    /// ==================================================================================
    /// WHY
    /// ==================================================================================
    /// Tap-to-move gave no acknowledgement at all. The player's own finger covers the point they
    /// aimed at, the walk starts with the avatar somewhere else on screen, and a tap that landed a
    /// cell short of a doorway looks exactly like a tap the game never received. The ring is the
    /// receipt: it says the tap was heard and it says where the game thinks it landed, which is the
    /// half a player cannot otherwise check.
    ///
    /// It marks the goal that was actually accepted, not the raw touch point — the pathfinder
    /// slides an unwalkable tap to the nearest open cell, and showing the raw point would teach the
    /// player the wrong thing about where they can walk.
    ///
    /// ==================================================================================
    /// WHAT IT IS
    /// ==================================================================================
    /// The design system's focus ring, laid flat on the ground: a 2px ring with a transparent
    /// centre, drawn in clay, which is this game's colour for "the one you chose". No new art, no
    /// new colour, and nothing that outlives the walk it belongs to.
    ///
    /// Reduced motion takes the shrink and keeps the fade, which is the rule as written: parallax,
    /// pulse and shake stop; fades keep running, because something that vanishes between two frames
    /// reads as a glitch rather than as calm.
    /// </summary>
    public sealed class TapMarker : MonoBehaviour
    {
        /// <summary>How long the ring stays, in seconds. Shorter than the shortest useful walk.</summary>
        const float Lifetime = 0.45f;

        /// <summary>Ring size in world units. One cell is one unit; this sits inside a cell.</summary>
        const float Size = 0.7f;

        /// <summary>How much of its size the ring loses over its life, when motion is not reduced.</summary>
        const float Shrink = 0.25f;

        /// <summary>
        /// Above the tilemap and below anything standing on it.
        ///
        /// The world sorts characters by row, from <c>CutsceneSortingBase</c> upwards, so a marker
        /// under all of that is a marker no resident can be hidden behind.
        /// </summary>
        const int SortingOrder = -50;

        static TapMarker _current;

        SpriteRenderer _renderer;
        Coroutine _run;

        static TapMarker Ensure()
        {
            if (_current != null)
            {
                return _current;
            }

            var go = new GameObject("TapMarker");
            _current = go.AddComponent<TapMarker>();
            _current.Build();
            return _current;
        }

        /// <summary>Puts the ring on a world position and starts its life over.</summary>
        public static void ShowAt(Vector2 worldPosition)
        {
            TapMarker marker = Ensure();
            if (marker == null)
            {
                return;
            }

            marker.Place(worldPosition);
        }

        void Build()
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = UIKit.GetSprite(UiSpriteKeys.FocusRing);
            _renderer.color = new Color(DesignTokens.Brand.Primary.r,
                                        DesignTokens.Brand.Primary.g,
                                        DesignTokens.Brand.Primary.b, 0f);
            _renderer.sortingOrder = SortingOrder;

            // The ring sprite is authored at whatever pixel size the art module chose, so it is
            // scaled to a world size here rather than assumed to be one unit across.
            float pixelsPerUnit = _renderer.sprite != null ? _renderer.sprite.rect.width : 1f;
            if (pixelsPerUnit <= 0f)
            {
                pixelsPerUnit = 1f;
            }

            float unit = _renderer.sprite != null ? _renderer.sprite.pixelsPerUnit : 1f;
            float spriteWorldWidth = pixelsPerUnit / Mathf.Max(0.0001f, unit);
            transform.localScale = Vector3.one * (Size / Mathf.Max(0.0001f, spriteWorldWidth));
        }

        void Place(Vector2 worldPosition)
        {
            transform.position = new Vector3(worldPosition.x, worldPosition.y, 0f);

            if (_run != null)
            {
                StopCoroutine(_run);
            }

            _run = StartCoroutine(Life());
        }

        IEnumerator Life()
        {
            Vector3 full = transform.localScale;
            Vector3 spent = full * (1f - Shrink);
            bool animate = !DesignTokens.Motion.ReduceMotion;

            float elapsed = 0f;
            while (elapsed < Lifetime)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / Lifetime);

                Color colour = _renderer.color;
                colour.a = 1f - t;
                _renderer.color = colour;

                transform.localScale = animate ? Vector3.Lerp(full, spent, t) : full;
                yield return null;
            }

            Color done = _renderer.color;
            done.a = 0f;
            _renderer.color = done;
            transform.localScale = full;

            _run = null;
        }

        void OnDestroy()
        {
            if (_current == this)
            {
                _current = null;
            }
        }
    }
}
