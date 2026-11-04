using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>Horizontal gestures move the recipe; vertical gestures and the mouse wheel keep scrolling the article.</summary>
    public sealed class CraftingGraphScroll : ScrollRect
    {
        ScrollRect article;
        bool verticalDrag;

        ScrollRect Article => article != null ? article : article = transform.parent.GetComponentInParent<ScrollRect>();

        public override void OnInitializePotentialDrag(PointerEventData eventData)
        {
            base.OnInitializePotentialDrag(eventData);
            if (Article != null) ExecuteEvents.Execute(Article.gameObject, eventData, ExecuteEvents.initializePotentialDrag);
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            Vector2 delta = eventData.position - eventData.pressPosition;
            verticalDrag = Mathf.Abs(delta.y) > Mathf.Abs(delta.x) && Article != null;
            if (verticalDrag) ExecuteEvents.Execute(Article.gameObject, eventData, ExecuteEvents.beginDragHandler);
            else base.OnBeginDrag(eventData);
        }

        public override void OnDrag(PointerEventData eventData)
        {
            if (verticalDrag) ExecuteEvents.Execute(Article.gameObject, eventData, ExecuteEvents.dragHandler);
            else base.OnDrag(eventData);
        }

        public override void OnEndDrag(PointerEventData eventData)
        {
            if (verticalDrag) ExecuteEvents.Execute(Article.gameObject, eventData, ExecuteEvents.endDragHandler);
            else base.OnEndDrag(eventData);
        }

        public override void OnScroll(PointerEventData eventData)
        {
            if (Mathf.Abs(eventData.scrollDelta.y) >= Mathf.Abs(eventData.scrollDelta.x) && Article != null)
                ExecuteEvents.Execute(Article.gameObject, eventData, ExecuteEvents.scrollHandler);
            else base.OnScroll(eventData);
        }
    }
}
