using UnityEngine;
using UnityEngine.EventSystems;

namespace PudgeWars.Gameplay
{
    /// <summary>The pointer over an effect's icon: its description shows while hovered.</summary>
    public class EffectIconPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public EffectBar bar;
        public int index;

        public void OnPointerEnter(PointerEventData eventData) => bar.Hover(index, true);

        public void OnPointerExit(PointerEventData eventData) => bar.Hover(index, false);
    }
}
