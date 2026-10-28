using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// One item line of the shop panel; under the mouse it tells how much gold the item still lacks. The price is what
    /// the buyer pays: for an assembled item only its parts still missing.
    /// </summary>
    public class ShopItemRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public ItemDef item;
        public Image icon;
        public Text title;
        public Text description;
        public Text price;
        public Button buyButton;
        public Text buyLabel;
        [Tooltip("Hint over the buy button: \"Не хватает: N золота\" (created at runtime if not set).")]
        public Text shortageLabel;
        public string shortageFormat = "Не хватает: {0} золота";
        public Color shortageColor = new(1f, 0.35f, 0.3f);
        /// <summary>The shop that shows the item's full description while the row is hovered (set by it).</summary>
        [System.NonSerialized] public ShopUI shop;

        HeroInventory buyer;
        bool hovered;

        /// <summary>Gold the buyer still needs for the item; 0 when it is affordable or already owned.</summary>
        public int Shortage => item == null || buyer == null || (item.unique && buyer.Owns(item)) ? 0 : Mathf.Max(0, buyer.PriceOf(item) - buyer.Gold);
        public bool ShortageShown => shortageLabel != null && shortageLabel.transform.parent.gameObject.activeSelf;

        void Awake()
        {
            if (shortageLabel == null) shortageLabel = CreateShortageLabel();
            ShowShortage();
        }

        void OnDisable()
        {
            // the shop closed under the mouse: no exit event comes, the hint must not wait for the next opening
            hovered = false;
            ShowShortage();
            if (shop != null) shop.HideTooltip();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            ShowShortage();
            if (shop != null) shop.ShowTooltip(item, -1, (RectTransform)transform);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // moving onto a child (the buy button) also sends an exit here: still over the row then
            hovered = eventData.pointerCurrentRaycast.gameObject != null && eventData.pointerCurrentRaycast.gameObject.transform.IsChildOf(transform);
            ShowShortage();
            if (!hovered && shop != null) shop.HideTooltip();
        }

        public void Refresh(HeroInventory buyer)
        {
            this.buyer = buyer;
            if (item == null) return;
            if (icon != null) icon.sprite = item.icon;
            if (title != null) title.text = item.displayName;
            if (description != null) description.text = item.description;
            if (price != null) price.text = (buyer != null ? buyer.PriceOf(item) : item.cost).ToString();
            bool owned = buyer != null && item.unique && buyer.Owns(item);
            if (buyButton != null) buyButton.interactable = buyer != null && buyer.CanBuy(item);
            if (buyLabel != null) buyLabel.text = owned ? "Куплено" : "Купить";
            ShowShortage();
        }

        void ShowShortage()
        {
            if (shortageLabel == null) return;
            int lack = Shortage;
            shortageLabel.transform.parent.gameObject.SetActive(hovered && lack > 0);
            if (lack > 0) shortageLabel.text = string.Format(shortageFormat, lack);
        }

        /// <summary>Dark plate just above the buy button with the hint in red.</summary>
        Text CreateShortageLabel()
        {
            var plate = new GameObject("ShortageHint", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)plate.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-10f, 66f);
            rt.sizeDelta = new Vector2(330f, 42f);
            var bg = plate.GetComponent<Image>();
            bg.color = new Color(0.04f, 0.04f, 0.05f, 0.95f);
            bg.raycastTarget = false; // must not take the hover away from the row and the button

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var text = textGo.GetComponent<Text>();
            text.font = description != null ? description.font : price != null ? price.font : null;
            text.fontSize = 24;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = shortageColor;
            text.raycastTarget = false;
            plate.SetActive(false);
            return text;
        }
    }
}
