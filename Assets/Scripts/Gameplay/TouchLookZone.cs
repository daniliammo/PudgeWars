using UnityEngine;
using UnityEngine.EventSystems;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Touch zone over the empty screen for turning the first-person view: one finger dragging over it (the third,
    /// besides the run and hook sticks) turns the camera; other fingers on the zone are ignored. The object itself
    /// is the zone (a transparent raycast image) and lies under the sticks, so they keep their own fingers.
    /// </summary>
    public class TouchLookZone : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public bool IsHeld => pointerId != NoPointer;

        const int NoPointer = int.MinValue;
        int pointerId = NoPointer;
        Vector2 delta;

        /// <summary>Screen pixels the finger has moved since the last call.</summary>
        public Vector2 TakeDelta()
        {
            var d = delta;
            delta = Vector2.zero;
            return d;
        }

        void OnDisable()
        {
            pointerId = NoPointer;
            delta = Vector2.zero;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsHeld) pointerId = eventData.pointerId;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == pointerId) delta += eventData.delta;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == pointerId) pointerId = NoPointer;
        }
    }
}
