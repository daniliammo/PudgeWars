using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// In-match shop (MagazinGame in the original): a panel toggled with B or the HUD button, plus an
    /// always visible bar with the player's gold and item slots. The panel flies in from the right screen edge
    /// and flies back out when closed. A hero picked with a click (<see cref="Inspect"/>) takes the player's place
    /// on the bar, so its gold and items can be seen; purchases are still the player's own.
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

        /// <summary>The hero whose gold and items the bar shows: the inspected one, otherwise the player.</summary>
        public HeroInventory Shown => inspected != null ? inspected : buyer;

        HeroInventory inspected;
        bool inspecting;
        RectTransform panelRect;
        Vector2 shownPosition, hiddenPosition;
        float slide; // 0 = beyond the screen edge, 1 = in place

        void Start()
        {
            // the local player's wallet; in a network game the hero appears (and changes) later
            if (buyer == null) Bind(LocalHeroInventory());
            else buyer.Changed += Refresh;
            LocalPlayer.Changed += OnLocalPlayerChanged;
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
            LocalPlayer.Changed -= OnLocalPlayerChanged;
            if (buyer != null) buyer.Changed -= Refresh;
            if (inspected != null) inspected.Changed -= Refresh;
        }

        /// <summary>Shows another hero's gold and items on the bar; null (or the player itself) shows the player's again.</summary>
        public void Inspect(HeroInventory hero)
        {
            if (hero == buyer) hero = null;
            if (inspected != null) inspected.Changed -= Refresh;
            inspected = hero;
            inspecting = hero != null;
            if (inspected != null) inspected.Changed += Refresh;
            Refresh();
        }

        static HeroInventory LocalHeroInventory() => LocalPlayer.Hero != null ? LocalPlayer.Hero.GetComponent<HeroInventory>() : null;

        void OnLocalPlayerChanged() => Bind(LocalHeroInventory());

        void Bind(HeroInventory wallet)
        {
            if (buyer != null) buyer.Changed -= Refresh;
            buyer = wallet;
            if (buyer != null) buyer.Changed += Refresh;
            Refresh();
        }

        void Update()
        {
            Slide();
            if (inspecting && inspected == null) Inspect(null); // the hero left the game
            var kb = Keyboard.current;
            if (kb == null || OverlayInput.Captured) return;
            if (kb.bKey.wasPressedThisFrame) Toggle();
            else if (kb.escapeKey.wasPressedThisFrame)
            {
                Close();
                Inspect(null);
            }
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
            var shown = Shown;
            if (goldLabel != null) goldLabel.text = shown != null ? shown.Gold.ToString() : "0";
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                var item = shown != null && i < shown.Items.Count ? shown.Items[i] : null;
                slots[i].sprite = item != null ? item.icon : null;
                slots[i].color = item != null ? Color.white : new Color(0f, 0f, 0f, 0.55f);
            }
            foreach (var row in rows) row.Refresh(buyer);
        }
    }
}
