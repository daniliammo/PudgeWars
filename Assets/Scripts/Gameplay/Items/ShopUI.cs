using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// In-match shop (MagazinGame in the original): a panel toggled with B or the HUD button, plus an
    /// always visible bar with the player's gold and item slots.
    /// </summary>
    public class ShopUI : MonoBehaviour
    {
        public HeroInventory buyer;
        public GameObject panel;
        public Button toggleButton;
        public Button closeButton;
        public Text goldLabel;
        public Image[] slots = new Image[HeroInventory.SlotCount];
        public ShopItemRow[] rows = new ShopItemRow[0];

        public bool IsOpen => panel != null && panel.activeSelf;

        void Start()
        {
            if (buyer == null)
            {
                var input = FindFirstObjectByType<PlayerInputController>();
                if (input != null) buyer = input.GetComponent<HeroInventory>();
            }
            if (buyer != null) buyer.Changed += Refresh;
            if (toggleButton != null) toggleButton.onClick.AddListener(Toggle);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            foreach (var row in rows)
            {
                var r = row;
                r.buyButton.onClick.AddListener(() => Buy(r.item));
            }
            if (panel != null) panel.SetActive(false);
            Refresh();
        }

        void OnDestroy()
        {
            if (buyer != null) buyer.Changed -= Refresh;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.bKey.wasPressedThisFrame) Toggle();
            else if (kb.escapeKey.wasPressedThisFrame) Close();
        }

        public void Toggle()
        {
            if (panel == null) return;
            panel.SetActive(!panel.activeSelf);
            Refresh();
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
        }

        public bool Buy(ItemDef item) => buyer != null && buyer.TryBuy(item);

        void Refresh()
        {
            if (goldLabel != null) goldLabel.text = buyer != null ? buyer.Gold.ToString() : "0";
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                var item = buyer != null && i < buyer.Items.Count ? buyer.Items[i] : null;
                slots[i].sprite = item != null ? item.icon : null;
                slots[i].color = item != null ? Color.white : new Color(0f, 0f, 0f, 0.55f);
            }
            foreach (var row in rows) row.Refresh(buyer);
        }
    }
}
