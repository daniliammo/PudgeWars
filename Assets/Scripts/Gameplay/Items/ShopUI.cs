using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// In-match shop (MagazinGame in the original): a panel toggled with B or the HUD button, plus an
    /// always visible bar with the player's gold and item slots. The panel flies in from the right screen edge
    /// and flies back out when closed.
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
        [Tooltip("Seconds the panel takes to fly in from beyond the screen edge (and back out).")]
        public float slideDuration = 0.25f;

        /// <summary>True from the moment the shop is opened, also while the panel is still flying in.</summary>
        public bool IsOpen { get; private set; }
        public bool IsAnimating => panel != null && panel.activeSelf && slide != (IsOpen ? 1f : 0f);

        RectTransform panelRect;
        Vector2 shownPosition, hiddenPosition;
        float slide; // 0 = beyond the screen edge, 1 = in place

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
            if (panel != null)
            {
                panelRect = (RectTransform)panel.transform;
                shownPosition = panelRect.anchoredPosition;
                // anchored to the right screen edge: hidden, its left side lies just beyond that edge
                hiddenPosition = new Vector2(panelRect.pivot.x * panelRect.sizeDelta.x, shownPosition.y);
                panelRect.anchoredPosition = hiddenPosition;
                panel.SetActive(false);
            }
            Refresh();
        }

        void OnDestroy()
        {
            if (buyer != null) buyer.Changed -= Refresh;
        }

        void Update()
        {
            Slide();
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.bKey.wasPressedThisFrame) Toggle();
            else if (kb.escapeKey.wasPressedThisFrame) Close();
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (panel == null || IsOpen) return;
            IsOpen = true;
            panel.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
        }

        /// <summary>Flies the panel toward its place or beyond the screen edge; switches it off once out.</summary>
        void Slide()
        {
            if (panelRect == null || !panel.activeSelf) return;
            float step = slideDuration > 0f ? Time.unscaledDeltaTime / slideDuration : 1f;
            slide = Mathf.MoveTowards(slide, IsOpen ? 1f : 0f, step);
            float eased = 1f - (1f - slide) * (1f - slide) * (1f - slide); // ease-out: fast start, soft landing
            panelRect.anchoredPosition = Vector2.LerpUnclamped(hiddenPosition, shownPosition, eased);
            if (!IsOpen && slide <= 0f) panel.SetActive(false);
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
