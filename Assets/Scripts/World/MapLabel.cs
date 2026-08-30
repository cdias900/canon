using SheepGate.Art;
using SheepGate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.World
{
    /// <summary>
    /// A name plate pinned to a place on the map.
    ///
    /// The plate is UI and the place is in the world, so the two are joined by anchoring the rect
    /// at the world point's viewport coordinates every frame. That costs one projection per label
    /// and survives the camera easing into its framing, which a position computed once at build
    /// time does not.
    ///
    /// Plates are how a map says what it is pointing at. Without them the markers are shapes and
    /// the player has to be told, in prose, what they are looking at.
    /// </summary>
    public sealed class MapLabel : MonoBehaviour
    {
        RectTransform _rect;
        Camera _camera;
        Vector3 _worldPoint;

        /// <summary>
        /// Builds a plate under <paramref name="parent"/> that follows <paramref name="worldPoint"/>.
        /// The offset is in world units and is applied before projection, so a plate sits the same
        /// distance under its marker whatever the zoom.
        /// </summary>
        public static MapLabel Create(RectTransform parent, string text, Vector3 worldPoint, float worldOffsetY,
            Color fill, Color ink)
        {
            RectTransform rect = UIKit.CreateRect("MapLabel", parent);
            rect.sizeDelta = new Vector2(10f, 10f);

            var plate = rect.gameObject.AddComponent<Image>();
            plate.sprite = ArtLibrary.Get(ArtKeys.UiBubble);
            plate.type = Image.Type.Sliced;
            plate.color = fill;
            plate.raycastTarget = false;

            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 6, 8);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text label = UIKit.CreateText(rect, "Text", text, UIKit.FontSize.Small, ink, TextAnchor.MiddleCenter);
            label.raycastTarget = false;

            var follower = rect.gameObject.AddComponent<MapLabel>();
            follower._rect = rect;
            follower._worldPoint = worldPoint + new Vector3(0f, worldOffsetY, 0f);
            follower._camera = Camera.main;
            follower.Follow();
            return follower;
        }

        /// <summary>The plate's graphics, so a fade can reach them.</summary>
        public Graphic[] Graphics
        {
            get { return GetComponentsInChildren<Graphic>(true); }
        }

        void LateUpdate()
        {
            Follow();
        }

        void Follow()
        {
            if (_rect == null)
            {
                return;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }

            Vector3 viewport = _camera.WorldToViewportPoint(_worldPoint);

            // Anchoring in viewport space rather than converting to canvas units keeps this correct
            // under any CanvasScaler setting, and there is exactly one of those settings to get
            // wrong otherwise.
            var anchor = new Vector2(viewport.x, viewport.y);
            _rect.anchorMin = anchor;
            _rect.anchorMax = anchor;
            _rect.anchoredPosition = Vector2.zero;
        }
    }
}
