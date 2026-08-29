using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SheepGate.Dialogue
{
    /// <summary>
    /// Full-screen click/tap receiver used to advance a line. It sits on a transparent graphic that
    /// only exists while a dialogue is on screen: when nothing is playing the whole dialogue root is
    /// deactivated, so taps meant for the world are never swallowed.
    /// </summary>
    public class DialogueTapCatcher : MonoBehaviour, IPointerClickHandler
    {
        /// <summary>Raised on every tap or click that lands on the catcher.</summary>
        public event Action Clicked;

        public void OnPointerClick(PointerEventData eventData)
        {
            Action handler = Clicked;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
