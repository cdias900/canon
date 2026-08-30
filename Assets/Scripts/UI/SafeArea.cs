using UnityEngine;

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
    /// Cancels the inset of a <see cref="SafeAreaFitter"/> above it, so a graphic parented inside
    /// the safe area still covers the entire screen.
    ///
    /// A scrim that stops at the safe area is worse than no scrim: it leaves a lit strip along the
    /// top and bottom of a dimmed screen and reads as a rendering fault rather than a modal.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SafeAreaBleed : MonoBehaviour
    {
        RectTransform _rect;
        Rect _appliedArea;
        int _appliedWidth;
        int _appliedHeight;
        float _appliedScale = -1f;

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

            if (safe == _appliedArea && width == _appliedWidth && height == _appliedHeight &&
                Mathf.Approximately(scale, _appliedScale))
            {
                return;
            }

            _appliedArea = safe;
            _appliedWidth = width;
            _appliedHeight = height;
            _appliedScale = scale;

            // Insets are in device pixels; the rect lives in canvas units, so they divide by the
            // scaler's factor before they can cancel anything.
            float left = safe.xMin / scale;
            float bottom = safe.yMin / scale;
            float right = (width - safe.xMax) / scale;
            float top = (height - safe.yMax) / scale;

            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.offsetMin = new Vector2(-left, -bottom);
            _rect.offsetMax = new Vector2(right, top);
        }
    }
}
