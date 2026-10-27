using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The pointer over an item slot of the shop bar (<see cref="ShopUI"/> adds it): a left click (a tap) picks the
    /// item, a right click sells it — on a touchscreen holding a finger on the slot for <see cref="holdToSell"/> does —
    /// and hovering shows the item's description.
    /// </summary>
    public class ItemSlotPointer : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        public int slot;
        [Tooltip("Seconds a finger holds the slot to sell its item.")]
        public float holdToSell = 0.8f;

        public event Action<int> Clicked, Sold, Hovered;
        public event Action Unhovered;

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

        public void OnPointerEnter(PointerEventData eventData) => Hovered?.Invoke(slot);

        public void OnPointerExit(PointerEventData eventData) => Unhovered?.Invoke();
    }
}
