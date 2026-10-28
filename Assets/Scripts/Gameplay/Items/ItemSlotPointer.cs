using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The pointer over an item slot of the shop bar (<see cref="ShopUI"/> adds it): a left click (a tap) picks the
    /// item, a right click sells it — on a touchscreen holding a finger on the slot for <see cref="holdToSell"/> does —
    /// hovering shows the item's description, and dragging the item with the left button (a finger) onto another slot
    /// moves it there (<see cref="DragStarted"/>, <see cref="Dragging"/>, <see cref="DroppedOn"/>, <see cref="DragEnded"/>).
    /// </summary>
    public class ItemSlotPointer : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        public int slot;
        [Tooltip("Seconds a finger holds the slot to sell its item.")]
        public float holdToSell = 0.8f;

        public event Action<int> Clicked, Sold, Hovered;
        public event Action Unhovered;
        /// <summary>A drag began on this slot (its index, the pointer's screen position).</summary>
        public event Action<int, Vector2> DragStarted;
        /// <summary>The drag that began here goes on (the pointer's screen position).</summary>
        public event Action<Vector2> Dragging;
        /// <summary>The drag that began here is over, dropped on a slot or anywhere.</summary>
        public event Action<int> DragEnded;
        /// <summary>A drag from another slot was let go on this one (the slot it came from, this slot).</summary>
        public event Action<int, int> DroppedOn;

        float downAt = -1f;
        bool soldByHold;

        void Update()
        {
            if (downAt < 0f || Time.unscaledTime - downAt < holdToSell) return;
            downAt = -1f;
            soldByHold = true;
            Sold?.Invoke(slot);
        }

        void OnDisable() => downAt = -1f;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.pointerId >= 0) downAt = Time.unscaledTime; // a finger (the mouse's ids are negative)
        }

        public void OnPointerUp(PointerEventData eventData) => downAt = -1f;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (soldByHold)
            {
                soldByHold = false; // the finger let go after the sale: not a pick
                return;
            }
            if (eventData.button == PointerEventData.InputButton.Right) Sold?.Invoke(slot);
            else if (eventData.button == PointerEventData.InputButton.Left) Clicked?.Invoke(slot);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            downAt = -1f; // a finger that drags does not sell
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                eventData.pointerDrag = null; // only the left button (a finger) moves items
                return;
            }
            DragStarted?.Invoke(slot, eventData.position);
        }

        public void OnDrag(PointerEventData eventData) => Dragging?.Invoke(eventData.position);

        public void OnEndDrag(PointerEventData eventData) => DragEnded?.Invoke(slot);

        public void OnDrop(PointerEventData eventData)
        {
            if (eventData.pointerDrag != null && eventData.pointerDrag.TryGetComponent<ItemSlotPointer>(out var from) && from != this)
                DroppedOn?.Invoke(from.slot, slot);
        }

        public void OnPointerEnter(PointerEventData eventData) => Hovered?.Invoke(slot);

        public void OnPointerExit(PointerEventData eventData) => Unhovered?.Invoke();
    }
}
