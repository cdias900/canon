using System;
using System.Collections.Generic;
using UnityEngine;
using SheepGate.Art;
using SheepGate.Core;

namespace SheepGate.Player
{
    /// <summary>
    /// Five stacked <see cref="SpriteRenderer"/>s (body, legs, top, accessory, hair) on a single
    /// GameObject, driven by one animation clock written in code. There is no Animator asset and
    /// no animation clip: sprites are procedural and come from <see cref="ArtLibrary"/>, so frames
    /// are advanced here and looked up by key.
    ///
    /// NPCs use the very same component; a palette swap is a <see cref="Tint"/> assignment.
    /// </summary>
    public class CharacterAppearance : MonoBehaviour
    {
        public const string AnimationIdle = "idle";
        public const string AnimationWalk = "walk";
        public const string AnimationWork = "work";

        /// <summary>Layer draw order, back to front. Index doubles as the sorting order offset.</summary>
        private const int LayerBody = 0;
        private const int LayerLegs = 1;
        private const int LayerTop = 2;
        private const int LayerAccessory = 3;

        /// <summary>Hair draws last so it sits over the head the body painted.</summary>
        private const int LayerHair = 4;
        private const int LayerCount = 5;

        /// <summary>Child object name per layer, in draw order. Indexed by the Layer* constants.</summary>
        private static readonly string[] LayerNames = { "Body", "Legs", "Top", "Accessory", "Hair" };

        private static readonly string[] LayerPrefixes = { "body", "legs", "top", "acc", "hair" };

        /// <summary>Highest variant index accepted per layer, matching the ArtLibrary key list.</summary>
        private static readonly int[] LayerMaxVariant = { 7, 3, 3, 3, 3 };

        /// <summary>
        /// Frames requested per layer, in layer order. Only the body is animated in the art
        /// library: the cosmetic layers vary by facing but not by frame, so asking them for a
        /// second frame would generate a duplicate sprite for nothing. The body caps at two
        /// because the key parser clamps the frame token to 0..1.
        /// </summary>
        private static readonly int[] LayerFrameCount = { 2, 1, 1, 1, 1 };

        private const float FramesPerSecondIdle = 2.5f;
        private const float FramesPerSecondWalk = 8f;
        private const float FramesPerSecondWork = 6f;

        // Shared across every character in the scene: procedural sprites are cached by ArtLibrary,
        // and this avoids rebuilding the candidate ladder for every NPC.
        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite[]> FrameCache = new Dictionary<string, Sprite[]>();
        private static readonly HashSet<string> WarnedKeys = new HashSet<string>();
        private static readonly Sprite[] EmptyFrames = new Sprite[0];

        private readonly SpriteRenderer[] _renderers = new SpriteRenderer[LayerCount];
        private readonly Sprite[][] _frames = new Sprite[LayerCount][];
        private readonly int[] _variants = new int[LayerCount];

        /// <summary>
        /// The build handed to <see cref="Apply"/>, kept by reference so <see cref="Refresh"/> can
        /// re-read it. Null until a build has been applied, which is the state an NPC composed
        /// straight from the art library stays in.
        /// </summary>
        private AppearanceState _appliedState;

        private FacingDirection _direction = FacingDirection.Down;
        private string _animation = AnimationIdle;
        private Color _tint = Color.white;
        private int _sortingOrderBase = 100;
        private string _sortingLayerName = "Default";

        private float _clock;
        private int _step;
        private int _appliedStep = -1;
        private bool _initialized;

        // The art library may not be built yet when the first character is composed. Empty
        // resolutions are therefore never cached, and the layers are re-probed a bounded number
        // of times so a late library still reaches the screen.
        private const float ResolveRetryInterval = 0.5f;
        private const int MaxResolveRetries = 12;
        private int _resolveRetries;
        private float _nextResolveRetry;
        private bool _hasEmptyLayer;

        /// <summary>Current facing. Change it through <see cref="SetDirection"/>.</summary>
        public FacingDirection Direction { get { return _direction; } }

        /// <summary>Current animation name: idle, walk or work.</summary>
        public string Animation { get { return _animation; } }

        /// <summary>Multiplicative palette swap applied to every layer. White leaves art untouched.</summary>
        public Color Tint
        {
            get { return _tint; }
            set
            {
                _tint = value;
                ApplyTint();
            }
        }

        /// <summary>Sorting order of the bottom layer. Layers above it get +1, +2, +3.</summary>
        public int SortingOrderBase
        {
            get { return _sortingOrderBase; }
            set
            {
                _sortingOrderBase = value;
                ApplySorting();
            }
        }

        public string SortingLayerName
        {
            get { return _sortingLayerName; }
            set
            {
                _sortingLayerName = string.IsNullOrEmpty(value) ? "Default" : value;
                ApplySorting();
            }
        }

        /// <summary>Drops every memoised sprite lookup. Call after the art library is rebuilt.</summary>
        public static void ClearArtCache()
        {
            SpriteCache.Clear();
            FrameCache.Clear();
            WarnedKeys.Clear();
        }

        /// <summary>Adds the component to a host GameObject, creating the renderers immediately.</summary>
        public static CharacterAppearance CreateOn(GameObject host)
        {
            if (host == null) return null;
            var appearance = host.GetComponent<CharacterAppearance>();
            if (appearance == null) appearance = host.AddComponent<CharacterAppearance>();
            return appearance;
        }

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // SpriteRenderer is [DisallowMultipleComponent], so the four layers cannot share one
            // GameObject: the second AddComponent onwards returns null and those layers silently
            // stop rendering. Each layer therefore gets its own child, reused when Initialize runs
            // again on an object that was already built.
            for (int i = 0; i < LayerCount; i++)
            {
                Transform child = transform.Find(LayerNames[i]);
                if (child == null)
                {
                    var host = new GameObject(LayerNames[i]);
                    child = host.transform;
                    child.SetParent(transform, false);
                    child.localPosition = Vector3.zero;
                    child.localRotation = Quaternion.identity;
                    child.localScale = Vector3.one;
                }

                var renderer = child.GetComponent<SpriteRenderer>();
                if (renderer == null)
                {
                    renderer = child.gameObject.AddComponent<SpriteRenderer>();
                }

                _renderers[i] = renderer;
                _frames[i] = null;
            }

            ApplySorting();
            ApplyTint();
            RebuildFrames();
        }

        /// <summary>
        /// Applies a saved character build. Null resets every layer to variant 0.
        ///
        /// The state is kept <b>by reference</b>, which is what makes <see cref="Refresh"/> useful:
        /// whoever changes the build writes into the same object, and this component re-reads it.
        /// </summary>
        public void Apply(AppearanceState state)
        {
            Initialize();
            _appliedState = state;
            CopyVariants(state);
            RebuildFrames();
        }

        /// <summary>
        /// Re-reads the <see cref="AppearanceState"/> last handed to <see cref="Apply"/> and
        /// repaints the five layers where they stand.
        ///
        /// This is how a character changes clothes without the scene being rebuilt: the wardrobe
        /// writes the six ints of that very object and something calls this. Nothing is reloaded,
        /// nothing is respawned, and the character keeps their position, facing and animation —
        /// only the sprites under them change.
        ///
        /// Two deliberate refusals, and both are the same rule (never take something away):
        /// <list type="bullet">
        ///   <item>A component that was never given a build does nothing at all. It does not fall
        ///   back to <c>Apply(null)</c>, which would blank an NPC composed straight from the art
        ///   library into variant 0 on every layer.</item>
        ///   <item>A build whose six ints are unchanged does nothing either. The wardrobe raises
        ///   its change event for badge state as well as for clothes, and rebuilding frames resets
        ///   the animation step, so a badge being spent would otherwise stutter every character
        ///   listening.</item>
        /// </list>
        ///
        /// <b>The name is deliberately not <c>Refresh</c>.</b> GameScene composes this component by
        /// probing a list of method names — Apply, SetAppearance, Set, Rebuild, Refresh, Build —
        /// first with an argument and then without, and warns when nothing matched. A public
        /// zero-argument <c>Refresh</c> would satisfy that second probe every time, so the warning
        /// could never fire again and a genuinely broken compose would land silently. This method
        /// stays outside the probe list so that tripwire keeps working.
        /// </summary>
        public void Reapply()
        {
            if (_appliedState == null) return;

            Initialize();
            if (!CopyVariants(_appliedState)) return;

            RebuildFrames();
        }

        /// <summary>
        /// Reads one build into the five layer variants. Returns true when at least one of them
        /// actually moved.
        ///
        /// The one place the packing lives: build and skin share the body sprite, so they arrive as
        /// a single art variant, and <see cref="Apply"/> and <see cref="Refresh"/> both come through
        /// here rather than each spelling that out. Two copies of it is two chances to fix one and
        /// leave the other drawing the wrong skin tone.
        /// </summary>
        private bool CopyVariants(AppearanceState state)
        {
            bool changed = false;
            changed |= SetVariant(LayerBody, state != null ? state.BodyArtVariant : 0);
            changed |= SetVariant(LayerLegs, state != null ? state.legs : 0);
            changed |= SetVariant(LayerTop, state != null ? state.top : 0);
            changed |= SetVariant(LayerAccessory, state != null ? state.accessory : 0);
            changed |= SetVariant(LayerHair, state != null ? state.hair : 0);
            return changed;
        }

        private bool SetVariant(int layer, int value)
        {
            int clamped = ClampVariant(layer, value);
            if (_variants[layer] == clamped) return false;
            _variants[layer] = clamped;
            return true;
        }

        public void SetDirection(FacingDirection direction)
        {
            Initialize();
            if (_direction == direction) return;
            _direction = direction;
            RebuildFrames();
        }

        /// <summary>Convenience for movement code: picks the facing from a delta vector.</summary>
        public void SetDirectionFromDelta(Vector2 delta)
        {
            SetDirection(FacingDirectionExtensions.FromDelta(delta, _direction));
        }

        /// <summary>Accepts idle, walk or work. Anything else falls back to idle.</summary>
        public void SetAnimation(string animation)
        {
            Initialize();

            string normalized = Normalize(animation);
            if (_animation == normalized) return;

            _animation = normalized;
            _clock = 0f;
            _step = 0;
            _appliedStep = -1;
            RebuildFrames();
        }

        private void Update()
        {
            if (!_initialized) return;

            if (_hasEmptyLayer && _resolveRetries < MaxResolveRetries && Time.unscaledTime >= _nextResolveRetry)
            {
                _resolveRetries++;
                _nextResolveRetry = Time.unscaledTime + ResolveRetryInterval;
                RebuildFrames();
            }

            _clock += Time.deltaTime * FramesPerSecond(_animation);
            while (_clock >= 1f)
            {
                _clock -= 1f;
                _step++;
                if (_step >= 1000000) _step = 0;
            }

            if (_step == _appliedStep) return;
            _appliedStep = _step;
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            for (int i = 0; i < LayerCount; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null) continue;

                var frames = _frames[i];
                if (frames == null || frames.Length == 0)
                {
                    renderer.enabled = false;
                    continue;
                }

                renderer.enabled = true;
                renderer.sprite = frames[_step % frames.Length];
            }
        }

        private void RebuildFrames()
        {
            _hasEmptyLayer = false;
            for (int i = 0; i < LayerCount; i++)
            {
                Sprite[] resolved = ResolveFrames(LayerPrefixes[i], _variants[i], _animation, _direction, LayerFrameCount[i]);
                _frames[i] = resolved;
                // The body is the only layer that must exist; cosmetic slots are legitimately empty
                // when the art library has no sprite for that variant.
                if (i == LayerBody && resolved.Length == 0) _hasEmptyLayer = true;
            }
            _appliedStep = -1;
            _nextResolveRetry = Time.unscaledTime + ResolveRetryInterval;
            ApplyFrame();
        }

        private void ApplySorting()
        {
            for (int i = 0; i < LayerCount; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null) continue;
                renderer.sortingLayerName = _sortingLayerName;
                renderer.sortingOrder = _sortingOrderBase + i;
            }
        }

        private void ApplyTint()
        {
            for (int i = 0; i < LayerCount; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null) continue;
                renderer.color = _tint;
            }
        }

        private static int ClampVariant(int layer, int value)
        {
            return Mathf.Clamp(value, 0, LayerMaxVariant[layer]);
        }

        private static string Normalize(string animation)
        {
            if (string.IsNullOrEmpty(animation)) return AnimationIdle;
            string lowered = animation.ToLowerInvariant();
            if (lowered == AnimationWalk) return AnimationWalk;
            if (lowered == AnimationWork) return AnimationWork;
            return AnimationIdle;
        }

        private static float FramesPerSecond(string animation)
        {
            if (animation == AnimationWalk) return FramesPerSecondWalk;
            if (animation == AnimationWork) return FramesPerSecondWork;
            return FramesPerSecondIdle;
        }

        /// <summary>
        /// Builds a character sprite key in the art library's canonical form,
        /// <c>prefix_variant_direction_animation_frame</c>. Public because NPC code driving this
        /// component needs the same key, and because it is the one string in the module that has
        /// to match another module exactly.
        /// </summary>
        public static string SpriteKey(string layerPrefix, int variant, FacingDirection direction,
            string animation, int frame)
        {
            return string.Concat(
                layerPrefix, "_",
                variant.ToString(), "_",
                direction.ToKey(), "_",
                Normalize(animation), "_",
                frame.ToString());
        }

        /// <summary>
        /// Resolves the frame list for one layer.
        ///
        /// The art library builds character keys as
        /// <c>prefix_variant_direction_animation_frame</c> (for example <c>body_0_left_walk_1</c>)
        /// and it never returns null: an unrecognised key yields a visible fallback sprite rather
        /// than nothing. That makes the exact key the only thing standing between the player and
        /// a wall of placeholders, so the canonical form is asked for first and the short
        /// <c>prefix_variant</c> form is kept only as a rescue for a library that does return null.
        /// </summary>
        private static Sprite[] ResolveFrames(string prefix, int variant, string animation,
            FacingDirection direction, int frameCount)
        {
            string directionKey = direction.ToKey();
            string cacheKey = string.Concat(prefix, "|", variant.ToString(), "|", animation, "|", directionKey);

            Sprite[] cached;
            if (FrameCache.TryGetValue(cacheKey, out cached)) return cached;

            var frames = new List<Sprite>(frameCount);

            for (int frame = 0; frame < frameCount; frame++)
            {
                Sprite sprite = TryGet(SpriteKey(prefix, variant, direction, animation, frame));
                if (sprite == null) sprite = TryGet(string.Concat(prefix, "_", variant.ToString()));
                if (sprite == null) break;
                frames.Add(sprite);
            }

            // A library that hands back one shared sprite for every frame should read as a still,
            // not as an animation that never visibly changes.
            if (frames.Count > 1)
            {
                bool allEqual = true;
                for (int i = 1; i < frames.Count; i++)
                {
                    if (frames[i] != frames[0]) { allEqual = false; break; }
                }
                if (allEqual) frames.RemoveRange(1, frames.Count - 1);
            }

            if (frames.Count == 0)
            {
                // Not cached: a miss here may only mean the art library was not built yet.
                WarnOnce(cacheKey, prefix, variant);
                return EmptyFrames;
            }

            Sprite[] resolved = frames.ToArray();
            FrameCache[cacheKey] = resolved;
            return resolved;
        }

        /// <summary>
        /// Single point of contact with the art library. A key that resolves to null is memoised
        /// so it is probed at most once, while a key whose lookup throws is not: throwing proves
        /// nothing about the key, only that the library was not ready. Either way the failure
        /// never escapes into the animation loop.
        /// </summary>
        private static Sprite TryGet(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            Sprite cached;
            if (SpriteCache.TryGetValue(key, out cached)) return cached;

            Sprite sprite = null;
            try
            {
                sprite = ArtLibrary.Get(key);
            }
            catch (Exception)
            {
                // A throwing lookup is not proof the key is unknown, so it is not memoised.
                return null;
            }

            SpriteCache[key] = sprite;
            return sprite;
        }

        private static void WarnOnce(string cacheKey, string prefix, int variant)
        {
            if (WarnedKeys.Contains(cacheKey)) return;
            WarnedKeys.Add(cacheKey);
            Debug.LogWarning("CharacterAppearance: no sprite resolved for layer '" + prefix +
                             "' variant " + variant.ToString() + " (" + cacheKey + "). Layer hidden.");
        }
    }
}
