using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SheepGate.Dialogue
{
    /// <summary>
    /// The full-screen target that hears a tap during a conversation.
    ///
    /// It reports two gestures, and the difference between them is time on the glass. A tap
    /// advances the line, which is what it has always done. A press held past
    /// <see cref="LongPressSeconds"/> asks for the lines already read, and it fires while the
    /// finger is still down — the moment it becomes a long press, not when it is let go — so the
    /// gesture confirms itself instead of leaving the player holding and hoping.
    ///
    /// A press that turned into a long press never also advances: the click that follows it on
    /// release is swallowed. Without that, asking to look back would cost the player the line they
    /// were looking back at.
    /// </summary>
    public class DialogueTapCatcher : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
    {
        /// <summary>
        /// How long a press has to be held to count as a long press.
        ///
        /// Half a second, which is iOS's own long-press default. Shorter and a slow tap opens a
        /// panel nobody asked for; longer and the gesture stops feeling connected to the finger.
        /// </summary>
        public const float LongPressSeconds = 0.5f;

        /// <summary>Raised on every tap or click that lands on the catcher.</summary>
        public event Action Clicked;

        /// <summary>Raised once when a press is held past <see cref="LongPressSeconds"/>.</summary>
        public event Action LongPressed;

        bool _down;
        bool _fired;
        float _downAt;

        public void OnPointerDown(PointerEventData eventData)
        {
            _down = true;
            _fired = false;
            _downAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _down = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // The click that ends a long press is not a tap. Unity raises it anyway, because from
            // its point of view a press and a release on the same target is a click.
            if (_fired)
            {
                _fired = false;
                return;
            }

            Action handler = Clicked;
            if (handler != null)
            {
                handler();
            }
        }

        void Update()
        {
            if (!_down || _fired)
            {
                return;
            }

            if (Time.unscaledTime - _downAt < LongPressSeconds)
            {
                return;
            }

            _fired = true;

            Action handler = LongPressed;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
