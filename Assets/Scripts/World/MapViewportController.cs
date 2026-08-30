using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SheepGate.World
{
    /// <summary>
    /// Bounded pan and zoom for the progression map.
    ///
    /// The map is deliberately wider than the portrait viewport: at minimum zoom it fills the
    /// viewport's height, so the road is explored section by section instead of being reduced to a
    /// thumbnail. Drag pans it, a pinch or mouse wheel zooms it, and the two public zoom methods are
    /// wired to ordinary 48-point buttons for players who cannot or do not use gestures.
    /// </summary>
    public sealed class MapViewportController : MonoBehaviour, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IScrollHandler, IPointerDownHandler, IPointerUpHandler
    {
        const float MaxZoomRatio = 2.25f;
        const float MinimumExplorationScale = 1.45f;
        const float ButtonZoomStep = 1.28f;
        const float WheelZoomStep = 1.12f;

        readonly Dictionary<int, Vector2> _pointers = new Dictionary<int, Vector2>();

        RectTransform _viewport;
        RectTransform _content;
        Canvas _canvas;
        Vector2 _focus = new Vector2(0.5f, 0.5f);
        Vector2 _lastViewportSize;
        Vector2 _lastContentSize;
        float _minimumScale = 1f;
        bool _needsLayout;

        public float CurrentScale
        {
            get { return _content != null ? _content.localScale.x : 1f; }
        }

        public float MinimumScale
        {
            get { return _minimumScale; }
        }

        public bool CanPanHorizontally
        {
            get
            {
                Vector2 range = PanRange(CurrentScale);
                return range.x > 0.5f;
            }
        }

        public bool CanPanVertically
        {
            get
            {
                Vector2 range = PanRange(CurrentScale);
                return range.y > 0.5f;
            }
        }

        public void Configure(RectTransform viewport, RectTransform content, Vector2 initialFocus)
        {
            _viewport = viewport;
            _content = content;
            _canvas = viewport != null ? viewport.GetComponentInParent<Canvas>() : null;
            _focus = new Vector2(Mathf.Clamp01(initialFocus.x), Mathf.Clamp01(initialFocus.y));
            _needsLayout = true;
        }

        void LateUpdate()
        {
            if (_viewport == null || _content == null)
            {
                return;
            }

            Vector2 viewportSize = _viewport.rect.size;
            Vector2 contentSize = _content.rect.size;
            if (viewportSize.x <= 0f || viewportSize.y <= 0f || contentSize.x <= 0f || contentSize.y <= 0f)
            {
                return;
            }

            if (!_needsLayout && Approximately(viewportSize, _lastViewportSize) &&
                Approximately(contentSize, _lastContentSize))
            {
                return;
            }

            _lastViewportSize = viewportSize;
            _lastContentSize = contentSize;
            _minimumScale = Mathf.Max(viewportSize.x / contentSize.x, viewportSize.y / contentSize.y) *
                MinimumExplorationScale;
            _content.localScale = Vector3.one * _minimumScale;
            FocusNormalized(_focus);
            _needsLayout = false;
        }

        public void ZoomIn()
        {
            SetScale(CurrentScale * ButtonZoomStep);
        }

        public void ZoomOut()
        {
            SetScale(CurrentScale / ButtonZoomStep);
        }

        public void FocusNormalized(Vector2 point)
        {
            if (_content == null)
            {
                return;
            }

            _focus = new Vector2(Mathf.Clamp01(point.x), Mathf.Clamp01(point.y));
            Vector2 size = _content.rect.size;
            Vector2 fromCentre = new Vector2(
                (_focus.x - 0.5f) * size.x,
                (_focus.y - 0.5f) * size.y);
            _content.anchoredPosition = -fromCentre * CurrentScale;
            ClampPosition();
        }

        public void PanBy(Vector2 canvasDelta)
        {
            if (_content == null)
            {
                return;
            }

            _content.anchoredPosition += canvasDelta;
            ClampPosition();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData != null)
            {
                _pointers[eventData.pointerId] = eventData.position;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData != null)
            {
                _pointers.Remove(eventData.pointerId);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            OnPointerDown(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData == null || _content == null)
            {
                return;
            }

            float before = PointerDistance();
            _pointers[eventData.pointerId] = eventData.position;
            float after = PointerDistance();

            if (_pointers.Count >= 2 && before > 1f && after > 1f)
            {
                SetScale(CurrentScale * (after / before));
                return;
            }

            float scaleFactor = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            PanBy(eventData.delta / scaleFactor);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData != null)
            {
                _pointers.Remove(eventData.pointerId);
            }
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (eventData == null || Mathf.Approximately(eventData.scrollDelta.y, 0f))
            {
                return;
            }

            SetScale(CurrentScale * (eventData.scrollDelta.y > 0f ? WheelZoomStep : 1f / WheelZoomStep));
        }

        void SetScale(float requested)
        {
            if (_content == null)
            {
                return;
            }

            float minimum = Mathf.Max(0.01f, _minimumScale);
            float scale = Mathf.Clamp(requested, minimum, minimum * MaxZoomRatio);
            _content.localScale = Vector3.one * scale;
            ClampPosition();
        }

        void ClampPosition()
        {
            if (_content == null || _viewport == null)
            {
                return;
            }

            Vector2 range = PanRange(CurrentScale);
            Vector2 position = _content.anchoredPosition;
            position.x = Mathf.Clamp(position.x, -range.x, range.x);
            position.y = Mathf.Clamp(position.y, -range.y, range.y);
            _content.anchoredPosition = position;
        }

        Vector2 PanRange(float scale)
        {
            if (_content == null || _viewport == null)
            {
                return Vector2.zero;
            }

            Vector2 contentSize = _content.rect.size * Mathf.Max(0f, scale);
            Vector2 viewportSize = _viewport.rect.size;
            return new Vector2(
                Mathf.Max(0f, (contentSize.x - viewportSize.x) * 0.5f),
                Mathf.Max(0f, (contentSize.y - viewportSize.y) * 0.5f));
        }

        float PointerDistance()
        {
            if (_pointers.Count < 2)
            {
                return 0f;
            }

            var positions = _pointers.Values.GetEnumerator();
            positions.MoveNext();
            Vector2 first = positions.Current;
            positions.MoveNext();
            return Vector2.Distance(first, positions.Current);
        }

        static bool Approximately(Vector2 a, Vector2 b)
        {
            return Mathf.Abs(a.x - b.x) < 0.1f && Mathf.Abs(a.y - b.y) < 0.1f;
        }
    }
}
