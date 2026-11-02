using UnityEngine;
using UnityEngine.EventSystems;

namespace PudgeWars.Gameplay
{
    /// <summary>Item previews on knowledge-base links; a tap still opens the persistent article.</summary>
    public class KnowledgeItemPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public ShopUI descriptions;
        public ItemDef item;

        public void OnPointerEnter(PointerEventData eventData) => descriptions.ShowTooltip(item, -1, (RectTransform)transform);
        public void OnPointerExit(PointerEventData eventData) => descriptions.HideTooltip();
        void OnDisable()
        {
            if (descriptions != null) descriptions.HideTooltip();
        }
    }
}
