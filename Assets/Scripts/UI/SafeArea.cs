using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// Keeps a RectTransform inside <see cref="Screen.safeArea"/>.
    ///
    /// Every canvas in this project anchors to the raw screen rectangle, which is correct on a
    /// device whose screen is a rectangle and wrong on every phone sold since. On the target
    /// hardware the top strip sits under the camera housing and the bottom row sits under the home
    /// indicator, so a readout gets a black pill through it and a button ends up under the gesture
    /// bar that swallows the tap.
    ///
    /// The fix is one inset rect that every screen builds inside, rather than a margin guessed per
    /// screen: guessed margins are wrong on the next device, and they are wrong again in landscape.
    ///
    /// Anything that must cover the whole screen — a fade, a scrim — stays outside this and uses
    /// <see cref="SafeAreaBleed"/> instead.
    ///
    /// <b>This is the hardware inset and nothing else.</b> It knows where the camera housing and
    /// the home indicator are; it does not know what a comfortable margin looks like. Those are
    /// design decisions with tokens of their own, and a screen applies them on top of this rect
    /// rather than expecting this to have applied them: <c>DesignTokens.Space.Gutter</c> for the
    /// left and right edges, and <c>DesignTokens.Space.SafeAreaBottom</c> for the clearance a
    /// control needs above the gesture bar. Folding either of them in here would mean every screen
    /// in the game silently lost the ability to reach its own edges.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        RectTransform _rect;
        Rect _appliedArea;
        int _appliedWidth;
        int _appliedHeight;

        void Awake()
        {
            _rect = (RectTransform)transform;
            Apply();
        }

        void OnEnable()
        {
            Apply();
        }

        void Update()
        {
            // Polled rather than driven by an event: Unity raises nothing reliable for a safe-area
            // change, and the comparison below costs four floats per frame.
            Apply();
        }

        void Apply()
        {
            if (_rect == null)
            {
                _rect = (RectTransform)transform;
            }

            Rect safe = Screen.safeArea;
            int width = Screen.width;
            int height = Screen.height;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            if (safe == _appliedArea && width == _appliedWidth && height == _appliedHeight)
            {
                return;
            }

            _appliedArea = safe;
            _appliedWidth = width;
            _appliedHeight = height;

            Vector2 min = new Vector2(safe.xMin / width, safe.yMin / height);
            Vector2 max = new Vector2(safe.xMax / width, safe.yMax / height);

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// Keeps the scaler's match in step with the window's shape, so a window that is resized
    /// through the portrait/landscape boundary is re-scaled instead of staying on the match it
    /// happened to be created under.
    ///
    /// The canvas is built once, usually before anyone has resized anything, and
    /// <see cref="UIKit.CanvasMatch"/> is read at that moment. On a phone that is the end of it.
    /// On a desktop a window is a thing people drag, and the drag that matters is the one that
    /// crosses square: past it every layout in the project needs the other match, and nothing was
    /// watching for it.
    ///
    /// Polled for the same reason <see cref="SafeAreaFitter"/> polls — Unity raises nothing
    /// reliable — and it costs one comparison per frame, only writing when the answer changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CanvasMatchFitter : MonoBehaviour
    {
        CanvasScaler _scaler;
        float _applied = -1f;

        void Awake()
        {
            _scaler = GetComponent<CanvasScaler>();
            Apply();
        }

        void OnEnable()
        {
            Apply();
        }

        void Update()
        {
            Apply();
        }

        void Apply()
        {
            if (_scaler == null)
            {
                _scaler = GetComponent<CanvasScaler>();
                if (_scaler == null)
                {
                    return;
                }
            }

            float match = UIKit.CanvasMatch;
            if (Mathf.Approximately(match, _applied))
            {
                return;
            }

            _applied = match;
            _scaler.matchWidthOrHeight = match;
        }
    }

    /// <summary>
    /// Caps how wide the band of interface gets, and centres it.
    ///
    /// Matching the canvas on height in a landscape window (see
    /// <see cref="UIKit.LandscapeCanvasMatch"/>) buys back the 1920 units of height every layout
    /// is written against, and pays for it in width: a 16:9 window reports about 3413 units
    /// across. Left alone, every card that sizes itself off its parent would stretch to that,
    /// and a dialogue card three and a half thousand units wide is not a wide card — it is
    /// unreadable, because a line of text that long has no return sweep for the eye.
    ///
    /// So the content keeps the measure it was designed at and sits in the middle, and the extra
    /// width shows what is behind it: the village, the wall, the map. That is the honest shape of
    /// a portrait game in a wide window, and it is what a phone game does on a tablet.
    ///
    /// <b>Only ever narrows.</b> On a phone the safe area is already narrower than the cap and
    /// this does nothing at all, which is the property that makes it safe to put on every canvas.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ContentWidthCap : MonoBehaviour
    {
        RectTransform _rect;
        float _appliedWidth = -1f;
        float _appliedCap = -1f;

        /// <summary>
        /// How far in each side was pulled, in canvas units, or zero when the cap is not biting.
        ///
        /// Read by <see cref="SafeAreaBleed"/>, which has to cancel this as well as the safe area:
        /// a scrim that stops at the capped band leaves the window lit down both sides, which is
        /// the exact failure that component was written to prevent.
        /// </summary>
        public float AppliedInset { get; private set; }

        void Awake()
        {
            _rect = (RectTransform)transform;
        }

        void OnEnable()
        {
            _appliedWidth = -1f;
        }

        void LateUpdate()
        {
            Apply();
        }

        /// <summary>
        /// Runs after <see cref="SafeAreaFitter"/> has had its say — hence LateUpdate — because
        /// the two write the same anchors and the safe area is the outer constraint of the pair.
        /// </summary>
        void Apply()
        {
            if (_rect == null)
            {
                _rect = (RectTransform)transform;
            }

            var parent = _rect.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            float available = parent.rect.width;
            if (available <= 0f)
            {
                return;
            }

            float cap = UIKit.ReferenceWidth;
            if (available <= cap)
            {
                // Narrower than the cap: the safe area's own anchors stand, untouched. This is
                // every phone, and the branch that keeps this component invisible in portrait.
                if (_appliedWidth >= 0f)
                {
                    _appliedWidth = -1f;
                    AppliedInset = 0f;
                    _rect.offsetMin = new Vector2(0f, _rect.offsetMin.y);
                    _rect.offsetMax = new Vector2(0f, _rect.offsetMax.y);
                    Rebuild();
                }

                return;
            }

            if (Mathf.Approximately(available, _appliedWidth) && Mathf.Approximately(cap, _appliedCap))
            {
                return;
            }

            _appliedWidth = available;
            _appliedCap = cap;

            float inset = (available - cap) * 0.5f;
            AppliedInset = inset;
            _rect.offsetMin = new Vector2(inset, _rect.offsetMin.y);
            _rect.offsetMax = new Vector2(-inset, _rect.offsetMax.y);
            Rebuild();
        }

        /// <summary>
        /// Tells everything below to lay out again, because this just changed the width they were
        /// laid out against.
        ///
        /// Without it the cap is a trap rather than a fix. The screens are built before this runs
        /// — a canvas is composed, its children measure the parent, and only then does LateUpdate
        /// narrow that parent — so every row, every scroll viewport and every chip keeps positions
        /// computed for the uncapped width. Nothing looks obviously broken: the panels are drawn
        /// where they were told to be, and a tap lands on the background behind them, which is
        /// exactly how the e2e reported it — "Chip_hair_short_crop is covered by SafeArea/
        /// Background". A layout that is silently one width behind is worse than one that is
        /// visibly wrong.
        /// </summary>
        void Rebuild()
        {
            LayoutRebuilder.MarkLayoutForRebuild(_rect);
        }
    }

    /// <summary>
    /// Cancels the inset of a <see cref="SafeAreaFitter"/> above it, so a graphic parented inside
    /// the safe area still covers the entire screen.
    ///
    /// A scrim that stops at the safe area is worse than no scrim: it leaves a lit strip along the
    /// top and bottom of a dimmed screen and reads as a rendering fault rather than a modal. The
    /// same holds for the fade between cutscene beats — anything drawn in <c>Surface.Scrim</c> or
    /// in black is claiming to cover the screen, and the claim has to be true at the corners.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SafeAreaBleed : MonoBehaviour
    {
        RectTransform _rect;
        Rect _appliedArea;
        int _appliedWidth;
        int _appliedHeight;
        float _appliedScale = -1f;
        float _appliedCapInset = -1f;

        void Awake()
        {
            _rect = (RectTransform)transform;
        }

        void OnEnable()
        {
            Apply();
        }

        void Update()
        {
            Apply();
        }

        void Apply()
        {
            if (_rect == null)
            {
                _rect = (RectTransform)transform;
            }

            Rect safe = Screen.safeArea;
            int width = Screen.width;
            int height = Screen.height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            Canvas canvas = _rect.GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            // The content cap is the second thing standing between this graphic and the screen
            // edge, and it moves independently of the safe area — a window resized wider changes
            // this and nothing else, so it belongs in the comparison as well as in the arithmetic.
            ContentWidthCap cap = _rect.GetComponentInParent<ContentWidthCap>();
            float capInset = cap != null ? cap.AppliedInset : 0f;

            if (safe == _appliedArea && width == _appliedWidth && height == _appliedHeight &&
                Mathf.Approximately(scale, _appliedScale) &&
                Mathf.Approximately(capInset, _appliedCapInset))
            {
                return;
            }

            _appliedArea = safe;
            _appliedWidth = width;
            _appliedHeight = height;
            _appliedScale = scale;
            _appliedCapInset = capInset;

            // Insets are in device pixels; the rect lives in canvas units, so they divide by the
            // scaler's factor before they can cancel anything.
            float left = safe.xMin / scale;
            float bottom = safe.yMin / scale;
            float right = (width - safe.xMax) / scale;
            float top = (height - safe.yMax) / scale;

            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.offsetMin = new Vector2(-left - capInset, -bottom);
            _rect.offsetMax = new Vector2(right + capInset, top);
        }
    }
}
