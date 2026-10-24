using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// On-screen stick for touch controls (VirtualJoystick_Background / _Thumb in the original). The object itself is
    /// the touch zone (a transparent raycast image); the finger that pressed it drags the thumb within the
    /// background's radius, other fingers on the same zone are ignored. A floating stick moves its background under
    /// the finger on press and goes back to its place on release (its pivot must be the centre).
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform background;
        public RectTransform thumb;
        [Tooltip("Background jumps under the finger on press instead of staying put.")]
        public bool floating;
        [Tooltip("Share of the radius near the centre that reads as zero.")]
        [Range(0f, 0.9f)] public float deadZone = 0.2f;

        /// <summary>Thumb offset in background radii, length up to 1; zero inside the dead zone.</summary>
        public Vector2 Value { get; private set; }
        public bool IsHeld => pointerId != NoPointer;
        /// <summary>The thumb has left the dead zone since the finger came down (stays set until the next press).</summary>
        public bool WasDragged { get; private set; }

        /// <summary>A finger came down on the stick.</summary>
        public event Action Pressed;
        /// <summary>The finger went up; the argument is the last <see cref="Value"/> (zero for a tap).</summary>
        public event Action<Vector2> Released;

        const int NoPointer = int.MinValue;
        int pointerId = NoPointer;
        Vector2 restPosition;

        void Awake()
        {
            if (background != null) restPosition = background.anchoredPosition;
        }

        void OnDisable()
        {
            // hidden mid-drag (the zone switched off): drop the finger without reporting a release
            pointerId = NoPointer;
            Recenter();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsHeld || background == null) return;
            pointerId = eventData.pointerId;
            WasDragged = false;
            if (floating && RectTransformUtility.ScreenPointToWorldPointInRectangle(background, eventData.position, eventData.pressEventCamera, out var world))
                background.position = world; // the background's pivot is its centre
            Track(eventData);
            Pressed?.Invoke();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == pointerId) Track(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != pointerId) return;
            var last = Value;
            pointerId = NoPointer;
            Recenter();
            Released?.Invoke(last);
        }

        void Track(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(background, eventData.position, eventData.pressEventCamera, out var local))
                return;
            float radius = background.rect.width / 2f;
            var offset = Vector2.ClampMagnitude((local - background.rect.center) / radius, 1f);
            Value = offset.magnitude < deadZone ? Vector2.zero : offset;
            if (Value != Vector2.zero) WasDragged = true;
            if (thumb != null) thumb.anchoredPosition = offset * radius;
        }

        void Recenter()
        {
            Value = Vector2.zero;
            if (thumb != null) thumb.anchoredPosition = Vector2.zero;
            if (background != null) background.anchoredPosition = restPosition;
        }
    }
}
