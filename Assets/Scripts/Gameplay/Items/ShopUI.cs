using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    /// <see cref="PlayerInputController"/>), a right click (on a phone, holding a finger on it) sells it for half its
    /// cost, and hovering a slot or a row of the shop shows the item's full description beside it
    /// (<see cref="ShowTooltip"/>); a slot of a usable item shows its key (Z X C V B N; passive items keep to the
    /// far slots and show none) and the item's charges in the corners,
    /// a dark radial shade with the seconds left over its cooldown (as on the ability bar) and is outlined while its
    /// item waits for a target. An item of the player's own bar can be dragged onto another slot (<see cref="Move"/>):
    /// its picture follows the pointer, and an item already in that slot takes the dragged one's place.
    /// Dota's shop sounds play for the player: General.Buy on a purchase, Item.PickupShop as an item is picked from
    /// its slot and Item.DropShop as it is put into one — rings, Gem of True Sight and recipes with their own
    /// (<see cref="ItemDef.sound"/>; Resources/Audio/Shop).
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
        public Color slotCooldownColor = new(0f, 0f, 0f, 0.82f);
        [Tooltip("Remaining cooldown over a slot.")] public string cooldownFormat = "0.0";
        [Range(0f, 1f)] public float soundVolume = 0.8f;
        [Tooltip("The open panel keeps this far below the screen's top edge, canvas units (a long shop is cut and scrolls).")]
        public float panelTopMargin = 16f;
        [Header("Item description")]
        public float tooltipWidth = 500f;
        public Color tooltipColor = new(0.03f, 0.035f, 0.045f, 0.98f);
        public Color tooltipRimColor = new(0.55f, 0.5f, 0.38f, 1f);
        [Tooltip("Frame of an active ability (used on a key).")] public Color activeFrameColor = new(0.3f, 0.55f, 1f);
        public Color activeFillColor = new(0.05f, 0.09f, 0.18f, 1f);
        public Color activeTitleColor = new(0.6f, 0.78f, 1f);
        [Tooltip("Frame of a passive ability (works by itself).")] public Color passiveFrameColor = new(0.5f, 0.52f, 0.56f);
        public Color passiveFillColor = new(0.1f, 0.105f, 0.115f, 1f);
        public Color passiveTitleColor = new(0.92f, 0.92f, 0.94f);

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
        float builtHeight; // the panel's height as built (all the rows), before FitPanel cuts it
        // made at runtime (BuildSlotParts); never restored by a script reload, where they would come back as empty
        // arrays (play mode entered without a domain or scene reload)
        [NonSerialized] Text[] chargeLabels, cooldownLabels, keyLabels;
        [NonSerialized] Image[] cooldownShades, slotFrames;
        Color slotFrameColor;
        RectTransform tooltip, tooltipAbilities;
        Text tooltipName, tooltipPrice, tooltipBonuses, tooltipDetails, tooltipLore, tooltipFooter;
        Font tooltipFont;
        readonly List<AbilityBox> abilityBoxes = new();
        ItemDef tooltipItem;
        int tooltipSlot = -1;
        RectTransform tooltipOver;
        AudioSource sounds;
        AudioClip buySound;
        AudioClip[] pickupSounds, dropSounds; // by ItemSound
        int dragSlot = -1; // the slot whose item is being dragged
        RectTransform dragGhost;
        Image dragGhostImage;

        /// <summary>The slot whose item is being dragged, or -1.</summary>
        public int DraggedSlot => dragSlot;
        /// <summary>How many shop sounds were played (for tests).</summary>
        public int SoundsPlayed { get; private set; }

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
            LoadSounds();
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
                r.shop = this;
                r.buyButton.onClick.AddListener(() => Buy(r.item));
            }
            BuildTooltip();
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
            FitPanel();
            Refresh();
        }

        /// <summary>
        /// A long shop is cut to the screen's height (a short screen, many items): its top stays below the screen's top
        /// edge and the rows scroll inside.
        /// </summary>
        void FitPanel()
        {
            if (panelRect == null || !(panelRect.parent is RectTransform canvas)) return;
            if (builtHeight <= 0f) builtHeight = panelRect.sizeDelta.y;
            float bottom = panelRect.anchoredPosition.y - panelRect.pivot.y * panelRect.sizeDelta.y + canvas.rect.height * panelRect.anchorMin.y;
            float room = canvas.rect.height - bottom - panelTopMargin;
            panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, Mathf.Clamp(builtHeight, 200f, Mathf.Max(200f, room)));
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

        public bool Buy(ItemDef item)
        {
            if (buyer == null || !buyer.TryBuy(item)) return false;
            Play(buySound);
            return true;
        }

        /// <summary>
        /// Moves the player's own item from one slot to another (swapping with an item there), with the sound of
        /// putting it down; not on an inspected hero's bar.
        /// </summary>
        public bool Move(int from, int to)
        {
            if (inspecting || buyer == null) return false;
            var item = buyer.ItemIn(from);
            if (!buyer.TryMove(from, to)) return false;
            PlayDrop(item);
            return true;
        }

        void LoadSounds()
        {
            sounds = gameObject.AddComponent<AudioSource>();
            sounds.playOnAwake = false;
            sounds.spatialBlend = 0f; // a UI sound
            AudioClip Clip(string name) => Resources.Load<AudioClip>("Audio/Shop/" + name);
            buySound = Clip("buy");
            // in the order of ItemSound: Default, Ring, Gem, Recipe
            pickupSounds = new[] { Clip("item_pickup_shop"), Clip("item_pickup_ring_shop"), Clip("item_pickup_gem_shop"), Clip("item_recipe_pickup_shop") };
            dropSounds = new[] { Clip("item_drop_shop"), Clip("item_drop_ring_shop"), Clip("item_drop_gem_shop"), Clip("item_recipe_drop_shop") };
        }

        static AudioClip ByKind(AudioClip[] clips, ItemDef item) =>
            clips == null || item == null ? null : clips[Mathf.Clamp((int)item.sound, 0, clips.Length - 1)] ?? clips[0];

        void PlayPickup(ItemDef item) => Play(ByKind(pickupSounds, item));

        void PlayDrop(ItemDef item) => Play(ByKind(dropSounds, item));

        void Play(AudioClip clip)
        {
            if (clip == null || sounds == null) return;
            sounds.PlayOneShot(clip, soundVolume);
            SoundsPlayed++;
        }

        /// <summary>A drag began on a slot: an own item is picked, its picture goes with the pointer.</summary>
        void BeginDrag(int slot, Vector2 screen)
        {
            var item = !inspecting && buyer != null ? buyer.ItemIn(slot) : null;
            if (item == null) return;
            dragSlot = slot;
            HideTooltip();
            if (dragGhost == null) BuildDragGhost();
            if (dragGhost != null)
            {
                var size = ((RectTransform)slots[slot].transform).rect.size;
                dragGhost.sizeDelta = size;
                dragGhostImage.sprite = item.icon;
                dragGhost.gameObject.SetActive(true);
                dragGhost.SetAsLastSibling();
                FollowPointer(screen);
            }
            slots[slot].color = new Color(1f, 1f, 1f, 0.35f); // the slot shows where the item came from
            PlayPickup(item);
        }

        void FollowPointer(Vector2 screen)
        {
            if (dragSlot < 0 || dragGhost == null) return;
            var canvas = dragGhost.parent as RectTransform;
            var cam = goldLabel != null && goldLabel.canvas.renderMode != RenderMode.ScreenSpaceOverlay ? goldLabel.canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, cam, out var local))
                dragGhost.localPosition = local;
        }

        /// <summary>Let go on a slot: the item moves there (or swaps).</summary>
        void DropOn(int from, int to)
        {
            if (from != dragSlot) return;
            dragSlot = -1;
            if (!Move(from, to)) PlayDrop(buyer != null ? buyer.ItemIn(from) : null);
            EndDragView();
        }

        /// <summary>The drag is over; let go anywhere but a slot, the item stays where it was.</summary>
        void EndDrag(int slot)
        {
            if (dragSlot != slot) return;
            dragSlot = -1;
            PlayDrop(buyer != null ? buyer.ItemIn(slot) : null);
            EndDragView();
        }

        void EndDragView()
        {
            if (dragGhost != null) dragGhost.gameObject.SetActive(false);
            Refresh();
        }

        void BuildDragGhost()
        {
            var canvas = goldLabel != null ? goldLabel.canvas : GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var old = canvas.transform.Find("DraggedItem");
            if (old != null) Destroy(old.gameObject); // left from an earlier start
            dragGhost = new GameObject("DraggedItem", typeof(RectTransform)).GetComponent<RectTransform>();
            dragGhost.SetParent(canvas.transform, false);
            dragGhost.anchorMin = dragGhost.anchorMax = dragGhost.pivot = new Vector2(0.5f, 0.5f);
            dragGhostImage = dragGhost.gameObject.AddComponent<Image>();
            dragGhostImage.color = new Color(1f, 1f, 1f, 0.85f);
            dragGhostImage.preserveAspect = true;
            dragGhostImage.raycastTarget = false; // the slot under it must get the drop
            dragGhost.gameObject.SetActive(false);
        }

        /// <summary>Sells the player's own item in a slot for half its cost (not an inspected hero's).</summary>
        public bool Sell(int slot) => !inspecting && buyer != null && buyer.TrySell(slot);

        /// <summary>All the text of the description shown now, line by line (null while hidden).</summary>
        public string TooltipText
        {
            get
            {
                if (tooltip == null || !tooltip.gameObject.activeSelf) return null;
                return string.Join("\n", tooltip.GetComponentsInChildren<Text>(false).Select(t => t.text));
            }
        }

        /// <summary>The frames of the abilities in the description shown now: their title and whether it is an active one.</summary>
        public IEnumerable<(string title, bool active)> TooltipAbilities =>
            abilityBoxes.Where(b => b.root.gameObject.activeSelf).Select(b => (b.title.text, b.active));

        /// <summary>
        /// The item's description beside <paramref name="over"/>: above an inventory slot (<paramref name="slot"/>,
        /// with its charges, key and sale price), left of a shop row (slot -1). From the top: the name with the price
        /// beside it, the bonuses, more about it, its abilities each in a frame (blue: used on a key, grey: works by
        /// itself), the lore in italics, and for an own slot the key and the sale price.
        /// </summary>
        public void ShowTooltip(ItemDef item, int slot, RectTransform over)
        {
            if (tooltip == null || item == null || over == null) return;
            tooltipItem = item;
            tooltipSlot = slot;
            tooltipOver = over;
            Fill(item, slot >= 0 ? Shown : null, slot);
            tooltip.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltip);
            var corners = new Vector3[4];
            over.GetWorldCorners(corners); // bottom-left, top-left, top-right, bottom-right
            float gap = 8f * tooltip.lossyScale.x;
            if (slot >= 0)
            {
                tooltip.pivot = new Vector2(1f, 0f); // over the slot, toward the screen's middle
                tooltip.position = corners[2] + Vector3.up * gap;
            }
            else
            {
                tooltip.pivot = new Vector2(1f, 0.5f); // left of the shop's row
                tooltip.position = (corners[0] + corners[1]) / 2f + Vector3.left * gap;
            }
            KeepOnScreen();
            tooltip.SetAsLastSibling();
        }

        /// <summary>A tall description is moved down (or up) to stay within the canvas.</summary>
        void KeepOnScreen()
        {
            if (!(tooltip.parent is RectTransform canvas)) return;
            var corners = new Vector3[4];
            tooltip.GetWorldCorners(corners);
            var area = new Vector3[4];
            canvas.GetWorldCorners(area);
            float margin = 8f * tooltip.lossyScale.y;
            if (corners[1].y > area[1].y - margin) tooltip.position += Vector3.down * (corners[1].y - (area[1].y - margin));
            else if (corners[0].y < area[0].y + margin) tooltip.position += Vector3.up * (area[0].y + margin - corners[0].y);
        }

        public void HideTooltip()
        {
            if (tooltip != null) tooltip.gameObject.SetActive(false);
            tooltipItem = null;
            tooltipSlot = -1;
        }

        /// <summary>The plain bonuses, one a line ("+250 к мане"); what an ability frame tells is left to it.</summary>
        static string Bonuses(ItemDef item, bool hasAbilities)
        {
            var ci = CultureInfo.InvariantCulture;
            var lines = new List<string>();
            if (item.ManaBonus > 0f) lines.Add($"+{item.ManaBonus.ToString("0", ci)} к мане");
            if (item.ManaRegenBonus > 0f) lines.Add($"+{item.ManaRegenBonus.ToString("0.##", ci)} к восстановлению маны");
            if (item.MoveSpeedBonus > 0f) lines.Add($"+{(item.MoveSpeedBonus * 100f).ToString("0", ci)} к скорости передвижения");
            if (item.AgilityBonus > 0f) lines.Add($"+{item.AgilityBonus.ToString("0", ci)} к ловкости");
            if (!hasAbilities) // an item without named abilities still tells what it does
            {
                if (item.CastRangeBonus > 0f) lines.Add($"+{(item.CastRangeBonus * 100f).ToString("0", ci)} к дальности способностей");
                if (item.HeroDamageBonus > 0f) lines.Add($"+{item.HeroDamageBonus.ToString("0", ci)} урона по героям");
                if (item.CreepDamageBonus > 0f) lines.Add($"+{item.CreepDamageBonus.ToString("0", ci)} урона по крипам");
                if (item.AuraManaRegen > 0f) lines.Add($"Аура: +{item.AuraManaRegen.ToString("0.##", ci)} маны в секунду союзникам в радиусе {(item.AuraRadius * 100f).ToString("0", ci)}");
                if (item.KillManaSteal > 0f) lines.Add($"Вампиризм маны: {(item.KillManaSteal * 100f).ToString("0.#", ci)} % маны убитого");
            }
            return string.Join("\n", lines);
        }

        /// <summary>The use's numbers under the active frame: reach, cooldown, charges.</summary>
        static string UseNumbers(ItemDef item, HeroInventory owner, int slot)
        {
            var use = item.Active;
            if (use == null) return "";
            var ci = CultureInfo.InvariantCulture;
            var parts = new List<string>();
            if (!item.IsInstant && use.castRange > 0f) parts.Add($"Дальность {(use.castRange * 100f).ToString("0", ci)}");
            if (use.cooldown > 0f) parts.Add($"Перезарядка {use.cooldown.ToString("0.#", ci)} с");
            if (item.charges > 0) parts.Add(owner != null ? $"Зарядов осталось: {owner.ChargesAt(slot)}" : $"Зарядов: {item.charges}");
            return string.Join("   ·   ", parts);
        }

        void Fill(ItemDef item, HeroInventory owner, int slot)
        {
            tooltipName.text = item.displayName;
            tooltipPrice.text = item.cost.ToString();
            var abilities = item.Abilities.ToList();
            SetText(tooltipBonuses, Bonuses(item, abilities.Count > 0));
            SetText(tooltipDetails, item.details);
            // the use's numbers go under the first active frame; an item using without one gets a frame of its own
            string numbers = UseNumbers(item, owner, slot);
            if (item.IsUsable && !abilities.Any(a => a.active))
                abilities.Insert(0, new ItemAbility { name = "", active = true, description = "" });
            bool numbersShown = false;
            for (int i = 0; i < Mathf.Max(abilities.Count, abilityBoxes.Count); i++)
            {
                if (i >= abilities.Count)
                {
                    abilityBoxes[i].root.gameObject.SetActive(false);
                    continue;
                }
                if (i >= abilityBoxes.Count) abilityBoxes.Add(BuildAbilityBox(tooltipAbilities));
                var box = abilityBoxes[i];
                var ability = abilities[i];
                box.active = ability.active;
                box.root.gameObject.SetActive(true);
                box.frame.color = ability.active ? activeFrameColor : passiveFrameColor;
                box.fill.color = ability.active ? activeFillColor : passiveFillColor;
                string kind = ability.active ? "Активная" : "Пассивная";
                box.title.text = string.IsNullOrEmpty(ability.name) ? kind : $"{kind}: {ability.name}";
                box.title.color = ability.active ? activeTitleColor : passiveTitleColor;
                string text = ability.description ?? "";
                if (ability.active && !numbersShown && numbers.Length > 0)
                {
                    text = text.Length > 0 ? $"{text}\n<color=#9FC4FF>{numbers}</color>" : $"<color=#9FC4FF>{numbers}</color>";
                    numbersShown = true;
                }
                SetText(box.text, text);
            }
            tooltipAbilities.gameObject.SetActive(abilities.Count > 0);
            SetText(tooltipLore, item.lore);
            var footer = new System.Text.StringBuilder();
            if (owner != null && item.IsUsable && slot < SlotKeys.Length)
                footer.Append(item.IsInstant
                    ? $"Клавиша <b>{SlotKeys[slot]}</b> или клик по слоту — применить"
                    : $"Клавиша <b>{SlotKeys[slot]}</b> или клик по слоту — выбрать, клик по цели — применить");
            if (owner != null && owner == LocalHeroInventory())
            {
                if (footer.Length > 0) footer.Append('\n');
                footer.Append($"<color=#FFB060><b>Продать за {owner.SellValue(slot)} золота</b></color> — правая кнопка мыши (на телефоне — удерживать слот)");
            }
            SetText(tooltipFooter, footer.ToString());
        }

        static void SetText(Text text, string value)
        {
            bool shown = !string.IsNullOrEmpty(value);
            text.gameObject.SetActive(shown);
            if (shown) text.text = value;
        }

        class AbilityBox
        {
            public RectTransform root;
            public Image frame, fill;
            public Text title, text;
            public bool active;
        }

        /// <summary>A dark opaque plate with a bright rim, laid out top to bottom; made at start, filled on hover.</summary>
        void BuildTooltip()
        {
            var canvas = goldLabel != null ? goldLabel.canvas : GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var old = canvas.transform.Find("ItemTooltip");
            if (old != null) Destroy(old.gameObject); // left from an earlier start
            tooltipFont = goldLabel != null ? goldLabel.font : null;
            abilityBoxes.Clear();

            // the rim: the plate's own image, the content on a darker one inside it
            tooltip = new GameObject("ItemTooltip", typeof(RectTransform)).GetComponent<RectTransform>();
            tooltip.SetParent(canvas.transform, false);
            tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 0.5f);
            tooltip.sizeDelta = new Vector2(tooltipWidth, 200f);
            var rim = tooltip.gameObject.AddComponent<Image>();
            rim.color = tooltipRimColor;
            rim.raycastTarget = false;
            var rimLayout = tooltip.gameObject.AddComponent<VerticalLayoutGroup>();
            rimLayout.padding = new RectOffset(2, 2, 2, 2);
            rimLayout.childControlWidth = rimLayout.childControlHeight = true;
            rimLayout.childForceExpandWidth = true;
            rimLayout.childForceExpandHeight = false;
            tooltip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var body = Box(tooltip, "Body", tooltipColor, 16, 12);
            var header = new GameObject("Header", typeof(RectTransform)).GetComponent<RectTransform>();
            header.SetParent(body, false);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            tooltipName = Label(header, "Name", 28, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            Stretch((RectTransform)tooltipName.transform, 0f, 110f);
            TextOutline.Add(tooltipName.gameObject, 1.2f);
            tooltipPrice = Label(header, "Price", 26, new Color(1f, 0.82f, 0.25f), TextAnchor.MiddleRight, FontStyle.Bold);
            Stretch((RectTransform)tooltipPrice.transform, 0f, 0f);
            TextOutline.Add(tooltipPrice.gameObject, 1.2f);
            var line = new GameObject("Line", typeof(RectTransform)).GetComponent<RectTransform>();
            line.SetParent(body, false);
            line.gameObject.AddComponent<LayoutElement>().preferredHeight = 2f;
            var lineImage = line.gameObject.AddComponent<Image>();
            lineImage.color = new Color(1f, 0.82f, 0.25f, 0.45f);
            lineImage.raycastTarget = false;

            tooltipBonuses = Label(body, "Bonuses", 22, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            tooltipDetails = Label(body, "Details", 19, new Color(0.82f, 0.84f, 0.88f), TextAnchor.UpperLeft, FontStyle.Normal);
            tooltipAbilities = new GameObject("Abilities", typeof(RectTransform)).GetComponent<RectTransform>();
            tooltipAbilities.SetParent(body, false);
            var list = tooltipAbilities.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 8f;
            list.childControlWidth = list.childControlHeight = true;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;
            tooltipLore = Label(body, "Lore", 19, new Color(0.74f, 0.7f, 0.62f), TextAnchor.UpperLeft, FontStyle.Italic);
            tooltipFooter = Label(body, "Footer", 18, new Color(0.78f, 0.8f, 0.84f), TextAnchor.UpperLeft, FontStyle.Normal);
            tooltip.gameObject.SetActive(false);
        }

        /// <summary>An ability's frame: a coloured rim, a dark fill, its title and its text.</summary>
        AbilityBox BuildAbilityBox(RectTransform parent)
        {
            var root = new GameObject("Ability", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            var frame = root.gameObject.AddComponent<Image>();
            frame.raycastTarget = false;
            var rimLayout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            rimLayout.padding = new RectOffset(2, 2, 2, 2);
            rimLayout.childControlWidth = rimLayout.childControlHeight = true;
            rimLayout.childForceExpandWidth = true;
            rimLayout.childForceExpandHeight = false;
            var fill = Box(root, "Fill", passiveFillColor, 10, 4);
            var title = Label(fill, "Title", 21, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
            var text = Label(fill, "Text", 19, new Color(0.9f, 0.91f, 0.93f), TextAnchor.UpperLeft, FontStyle.Normal);
            return new AbilityBox { root = root, frame = frame, fill = fill.GetComponent<Image>(), title = title, text = text };
        }

        /// <summary>A plate laying its children out top to bottom with <paramref name="padding"/> around them.</summary>
        static RectTransform Box(RectTransform parent, string name, Color color, int padding, float spacing)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding - 2, padding);
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rt;
        }

        Text Label(Transform parent, string name, int size, Color color, TextAnchor align, FontStyle style)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = tooltipFont;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = align;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Fills the parent, leaving <paramref name="right"/> free at its right side.</summary>
        static void Stretch(RectTransform rt, float left, float right)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(-right, 0f);
        }

        /// <summary>Each slot clickable, with its charges label, cooldown shade and frame for the selection.</summary>
        void BuildSlotParts()
        {
            chargeLabels = new Text[slots.Length];
            cooldownLabels = new Text[slots.Length];
            keyLabels = new Text[slots.Length];
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
                if (icon.TryGetComponent<ItemSlotPointer>(out var earlier)) Destroy(earlier);
                var pointer = icon.gameObject.AddComponent<ItemSlotPointer>();
                pointer.slot = slot;
                pointer.Clicked += s =>
                {
                    if (!inspecting) ItemSlotClicked?.Invoke(s);
                };
                pointer.Sold += s => Sell(s);
                pointer.Hovered += s =>
                {
                    var shown = Shown;
                    var item = shown != null ? shown.ItemIn(s) : null;
                    if (item != null && dragSlot < 0) ShowTooltip(item, s, (RectTransform)slots[s].transform);
                };
                pointer.Unhovered += HideTooltip;
                pointer.DragStarted += BeginDrag;
                pointer.Dragging += FollowPointer;
                pointer.DroppedOn += DropOn;
                pointer.DragEnded += EndDrag;
                icon.raycastTarget = true;

                // as on the ability bar: a radial shade masked by the item's own picture, running out clockwise from the top
                var shade = new GameObject("Cooldown", typeof(RectTransform)).GetComponent<RectTransform>();
                shade.SetParent(icon.transform, false);
                shade.anchorMin = Vector2.zero;
                shade.anchorMax = Vector2.one;
                shade.offsetMin = shade.offsetMax = Vector2.zero;
                var shadeImage = shade.gameObject.AddComponent<Image>();
                shadeImage.color = slotCooldownColor;
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
                TextOutline.Add(time.gameObject, 2f, bold: 0.7f);
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
                TextOutline.Add(key.gameObject, 1.4f, bold: 0.5f);
                keyRt.gameObject.SetActive(false); // shown over a usable item (Refresh)
                keyLabels[i] = key;

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
                TextOutline.Add(label.gameObject, 1.6f, bold: 0.6f);
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
                var item = shown != null ? shown.ItemIn(i) : null;
                slots[i].sprite = item != null ? item.icon : null;
                slots[i].color = item == null ? new Color(0f, 0f, 0f, 0.55f) : i == dragSlot ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
                // only a usable item has a key to press (and a slot to click)
                if (keyLabels != null && i < keyLabels.Length && keyLabels[i] != null)
                    keyLabels[i].gameObject.SetActive(item != null && item.IsUsable);
                if (chargeLabels != null && i < chargeLabels.Length && chargeLabels[i] != null)
                    chargeLabels[i].text = item != null && (item.charges > 1 || item.stacks) ? shown.ChargesAt(i).ToString() : "";
            }
            foreach (var row in rows) row.Refresh(buyer);
            // the hovered slot changed under the mouse (used, sold, a charge less): show it anew or hide
            if (tooltipSlot >= 0)
            {
                var item = shown != null ? shown.ItemIn(tooltipSlot) : null;
                if (item == null) HideTooltip();
                else ShowTooltip(item, tooltipSlot, tooltipOver);
            }
        }
    }
}
