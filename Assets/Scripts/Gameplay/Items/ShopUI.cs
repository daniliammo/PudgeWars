using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// In-match shop (MagazinGame in the original): a panel toggled with F4 (as in Dota; B is an item key) or the HUD button, plus an
    /// always visible bar with the player's gold and item slots. The panel flies in from the right screen edge
    /// and flies back out when closed. A hero picked with a click (<see cref="Inspect"/>) takes the player's place
    /// on the bar, so its gold and items can be seen, a tab over the bar says whose they are ("Осмотр: Бот
    /// Мясник") and a panel over the tab shows its stats (<see cref="HeroStatsPanel"/>); purchases are still the
    /// player's own. A click (tap) on a slot of the player's own bar picks its item for use (<see cref="ItemSlotClicked"/>,
    /// <see cref="PlayerInputController"/>); a slot shows its key (Z X C V B N) and the item's charges in the corners,
    /// a dark radial shade with the seconds left over its cooldown (as on the ability bar) and is outlined while its
    /// item waits for a target.
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
        [Tooltip("Tab over the bar while another hero is inspected; made at runtime over the gold if empty.")]
        public Text inspectLabel;
        [Tooltip("{0} = the inspected hero's name.")]
        public string inspectFormat = "Осмотр: {0}";
        public Color inspectAllyColor = new(0.45f, 0.95f, 0.4f);
        public Color inspectEnemyColor = new(1f, 0.4f, 0.32f);
        [Tooltip("Stats of the inspected hero over the tab; made at runtime if empty.")]
        public HeroStatsPanel statsPanel;
        [Tooltip("Seconds the panel takes to fly in from beyond the screen edge (and back out).")]
        public float slideDuration = 0.25f;
        public Color selectedSlotColor = new(0.55f, 0.95f, 0.45f, 1f);
        public Color cooldownShadeColor = new(0f, 0f, 0f, 0.82f);
        [Tooltip("Remaining cooldown over a slot.")] public string cooldownFormat = "0.0";

        /// <summary>The item keys, slot by slot (PlayerInputController).</summary>
        public static readonly string[] SlotKeys = { "Z", "X", "C", "V", "B", "N" };

        /// <summary>A slot of the player's own bar was clicked (its index).</summary>
        public static event Action<int> ItemSlotClicked;
        /// <summary>True from the moment the shop is opened, also while the panel is still flying in.</summary>
        public bool IsOpen { get; private set; }
        public bool IsAnimating => panel != null && panel.activeSelf && slide != (IsOpen ? 1f : 0f);

        /// <summary>The hero whose gold and items the bar shows: the inspected one, otherwise the player.</summary>
        public HeroInventory Shown => inspected != null ? inspected : buyer;

        HeroInventory inspected;
        bool inspecting;
        GameObject inspectTab;
        RectTransform panelRect;
        Vector2 shownPosition, hiddenPosition;
        float slide; // 0 = beyond the screen edge, 1 = in place
        // made at runtime (BuildSlotParts); never restored by a script reload, where they would come back as empty
        // arrays (play mode entered without a domain or scene reload)
        [NonSerialized] Text[] chargeLabels, cooldownLabels;
        [NonSerialized] Image[] cooldownShades, slotFrames;
        Color slotFrameColor;

        void Start()
        {
            // the local player's wallet; in a network game the hero appears (and changes) later
            if (buyer == null) Bind(LocalHeroInventory());
            else buyer.Changed += Refresh;
            LocalPlayer.Changed += OnLocalPlayerChanged;
            if (inspectLabel == null) inspectLabel = BuildInspectLabel();
            else inspectTab = inspectLabel.gameObject;
            if (statsPanel == null && inspectTab != null)
            {
                var tab = (RectTransform)inspectTab.transform;
                float top = tab.anchoredPosition.y + (1f - tab.pivot.y) * tab.sizeDelta.y;
                statsPanel = HeroStatsPanel.Create(tab.parent, inspectLabel.font, new Vector2(tab.anchoredPosition.x, top + 6f), tab.sizeDelta.x);
            }
            BuildSlotParts();
            if (toggleButton != null)
            {
                toggleButton.onClick.AddListener(Toggle);
                var caption = toggleButton.GetComponentInChildren<Text>();
                if (caption != null) caption.text = "Магазин (F4)"; // a scene built before the move still says B
            }
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
            ShowSlotStates();
            if (inspecting && inspected == null) Inspect(null); // the hero left the game
            var kb = Keyboard.current;
            if (kb == null || OverlayInput.Captured) return;
            if (kb.f4Key.wasPressedThisFrame) Toggle();
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

        /// <summary>Each slot clickable, with its charges label, cooldown shade and frame for the selection.</summary>
        void BuildSlotParts()
        {
            chargeLabels = new Text[slots.Length];
            cooldownLabels = new Text[slots.Length];
            cooldownShades = new Image[slots.Length];
            slotFrames = new Image[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                var icon = slots[i];
                if (icon == null) continue;
                int slot = i;
                // parts left from an earlier start (play mode entered without reloading the scene) are made anew
                foreach (var part in new[] { "Cooldown", "CooldownTime", "Key", "Charges" })
                {
                    var old = icon.transform.Find(part);
                    if (old != null) Destroy(old.gameObject);
                }
                if (!icon.TryGetComponent<Button>(out var button)) button = icon.gameObject.AddComponent<Button>();
                button.onClick.RemoveAllListeners(); // runtime listeners only; the scene has none on the slots
                button.targetGraphic = icon;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() =>
                {
                    if (!inspecting) ItemSlotClicked?.Invoke(slot);
                });
                icon.raycastTarget = true;

                // as on the ability bar: a radial shade masked by the item's own picture, running out clockwise from the top
                var shade = new GameObject("Cooldown", typeof(RectTransform)).GetComponent<RectTransform>();
                shade.SetParent(icon.transform, false);
                shade.anchorMin = Vector2.zero;
                shade.anchorMax = Vector2.one;
                shade.offsetMin = shade.offsetMax = Vector2.zero;
                var shadeImage = shade.gameObject.AddComponent<Image>();
                shadeImage.color = cooldownShadeColor;
                shadeImage.raycastTarget = false;
                shadeImage.preserveAspect = true;
                shadeImage.type = Image.Type.Filled;
                shadeImage.fillMethod = Image.FillMethod.Radial360;
                shadeImage.fillOrigin = (int)Image.Origin360.Top;
                shadeImage.fillClockwise = false;
                shadeImage.enabled = false;
                cooldownShades[i] = shadeImage;

                var timeRt = new GameObject("CooldownTime", typeof(RectTransform)).GetComponent<RectTransform>();
                timeRt.SetParent(icon.transform, false);
                timeRt.anchorMin = Vector2.zero;
                timeRt.anchorMax = Vector2.one;
                timeRt.offsetMin = timeRt.offsetMax = Vector2.zero;
                var time = timeRt.gameObject.AddComponent<Text>();
                time.font = goldLabel != null ? goldLabel.font : null;
                time.fontSize = 26;
                time.fontStyle = FontStyle.Bold;
                time.alignment = TextAnchor.MiddleCenter;
                time.color = Color.white;
                time.raycastTarget = false;
                time.horizontalOverflow = HorizontalWrapMode.Overflow;
                var timeOutline = timeRt.gameObject.AddComponent<Outline>();
                timeOutline.effectColor = Color.black;
                timeOutline.effectDistance = new Vector2(2f, -2f);
                cooldownLabels[i] = time;

                var keyRt = new GameObject("Key", typeof(RectTransform)).GetComponent<RectTransform>();
                keyRt.SetParent(icon.transform, false);
                keyRt.anchorMin = keyRt.anchorMax = keyRt.pivot = new Vector2(0f, 1f);
                keyRt.anchoredPosition = new Vector2(3f, -1f);
                keyRt.sizeDelta = new Vector2(30f, 24f);
                var key = keyRt.gameObject.AddComponent<Text>();
                key.font = goldLabel != null ? goldLabel.font : null;
                key.fontSize = 18;
                key.fontStyle = FontStyle.Bold;
                key.alignment = TextAnchor.UpperLeft;
                key.color = new Color(1f, 1f, 1f, 0.85f);
                key.raycastTarget = false;
                key.text = i < SlotKeys.Length ? SlotKeys[i] : "";
                keyRt.gameObject.AddComponent<Outline>().effectColor = Color.black;

                var labelRt = new GameObject("Charges", typeof(RectTransform)).GetComponent<RectTransform>();
                labelRt.SetParent(icon.transform, false);
                labelRt.anchorMin = labelRt.anchorMax = labelRt.pivot = new Vector2(1f, 0f);
                labelRt.anchoredPosition = new Vector2(-3f, 1f);
                labelRt.sizeDelta = new Vector2(40f, 26f);
                var label = labelRt.gameObject.AddComponent<Text>();
                label.font = goldLabel != null ? goldLabel.font : null;
                label.fontSize = 22;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.LowerRight;
                label.color = Color.white;
                label.raycastTarget = false;
                var outline = labelRt.gameObject.AddComponent<Outline>();
                outline.effectColor = Color.black;
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                chargeLabels[i] = label;

                slotFrames[i] = icon.transform.parent != null ? icon.transform.parent.GetComponent<Image>() : null;
                if (slotFrames[i] != null) slotFrameColor = slotFrames[i].color;
            }
        }

        /// <summary>Cooldown shading (from the top down, as it runs out) and the frame of the item picked for use.</summary>
        void ShowSlotStates()
        {
            if (cooldownShades == null || cooldownShades.Length < slots.Length) return;
            var shown = Shown;
            int selected = !inspecting && LocalPlayer.Input != null ? LocalPlayer.Input.SelectedItem : PlayerInputController.NoItem;
            for (int i = 0; i < slots.Length; i++)
            {
                if (cooldownShades[i] != null)
                {
                    float total = shown != null ? shown.CooldownOf(i) : 0f;
                    float seconds = shown != null ? shown.CooldownLeft(i) : 0f;
                    bool cooling = total > 0f && seconds > 0f;
                    cooldownShades[i].enabled = cooling;
                    cooldownShades[i].sprite = slots[i].sprite; // the shade covers exactly the picture
                    cooldownShades[i].fillAmount = cooling ? Mathf.Clamp01(seconds / total) : 0f;
                    cooldownLabels[i].text = cooling ? seconds.ToString(cooldownFormat, CultureInfo.InvariantCulture) : "";
                }
                if (slotFrames[i] != null) slotFrames[i].color = i == selected ? selectedSlotColor : slotFrameColor;
            }
        }

        /// <summary>A dark tab sitting on the bar's top edge, over the gold.</summary>
        Text BuildInspectLabel()
        {
            if (goldLabel == null) return null;
            var bar = goldLabel.transform.parent;
            var tab = new GameObject("InspectTab", typeof(RectTransform)).GetComponent<RectTransform>();
            tab.SetParent(bar, false);
            tab.anchorMin = tab.anchorMax = tab.pivot = new Vector2(0f, 1f);
            tab.anchoredPosition = new Vector2(0f, 46f);
            tab.sizeDelta = new Vector2(420f, 42f);
            var bg = tab.gameObject.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.08f, 0.9f);
            bg.raycastTarget = false;
            var rt = new GameObject("Inspect", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(tab, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(16f, 0f);
            rt.offsetMax = new Vector2(-8f, 0f);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = goldLabel.font;
            text.fontSize = 26;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(2f, -2f);
            inspectTab = tab.gameObject;
            inspectTab.SetActive(false);
            return text;
        }

        /// <summary>The tab says whose gold and items the bar shows; hidden for the player's own.</summary>
        void ShowInspected()
        {
            if (inspectLabel == null) return;
            var hero = inspected != null ? inspected.GetComponent<Pudge>() : null;
            inspectTab.SetActive(hero != null);
            if (statsPanel != null) statsPanel.Show(hero);
            if (hero == null) return;
            inspectLabel.text = string.Format(inspectFormat, hero.DisplayName);
            inspectLabel.color = hero.team == LocalPlayer.Team ? inspectAllyColor : inspectEnemyColor;
        }

        void Refresh()
        {
            ShowInspected();
            var shown = Shown;
            if (goldLabel != null) goldLabel.text = shown != null ? shown.Gold.ToString() : "0";
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                var item = shown != null && i < shown.Items.Count ? shown.Items[i] : null;
                slots[i].sprite = item != null ? item.icon : null;
                slots[i].color = item != null ? Color.white : new Color(0f, 0f, 0f, 0.55f);
                if (chargeLabels != null && i < chargeLabels.Length && chargeLabels[i] != null)
                    chargeLabels[i].text = item != null && (item.charges > 1 || item.stacks) ? shown.ChargesAt(i).ToString() : "";
            }
            foreach (var row in rows) row.Refresh(buyer);
        }
    }
}
