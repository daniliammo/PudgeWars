using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>One item line of the shop panel.</summary>
    public class ShopItemRow : MonoBehaviour
    {
        public ItemDef item;
        public Image icon;
        public Text title;
        public Text description;
        public Text price;
        public Button buyButton;
        public Text buyLabel;

        public void Refresh(HeroInventory buyer)
        {
            if (item == null) return;
            if (icon != null) icon.sprite = item.icon;
            if (title != null) title.text = item.displayName;
            if (description != null) description.text = item.description;
            if (price != null) price.text = item.cost.ToString();
            bool owned = buyer != null && item.unique && buyer.Owns(item);
            if (buyButton != null) buyButton.interactable = buyer != null && buyer.CanBuy(item);
            if (buyLabel != null) buyLabel.text = owned ? "Куплено" : "Купить";
        }
    }
}
