using UnityEngine;

namespace SheepGate.World
{
    /// <summary>
    /// Keeps map chrome readable while its anchor travels through a zoomed content rect.
    ///
    /// The parent map scales and pans normally. This component applies the inverse local scale to
    /// its own RectTransform, so the card or location label stays at the design system's physical
    /// type size instead of becoming enormous as the player zooms in.
    /// </summary>
    public sealed class MapScaleLock : MonoBehaviour
    {
        RectTransform _mapContent;

        public void Configure(RectTransform mapContent)
        {
            _mapContent = mapContent;
            Apply();
        }

        void LateUpdate()
        {
            Apply();
        }

        void Apply()
        {
            if (_mapContent == null)
            {
                return;
            }

            float mapScale = Mathf.Max(0.01f, _mapContent.localScale.x);
            transform.localScale = Vector3.one / mapScale;
        }
    }
}
