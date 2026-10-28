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
        [NonSerialized] Text[] chargeLabels, cooldownLabels, keyLabels;
        [NonSerialized] Image[] cooldownShades, slotFrames;
        Color slotFrameColor;
        RectTransform tooltip;
        Text tooltipText;
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

        /// <summary>The item description shown now (null while hidden).</summary>
        public Text Tooltip => tooltip != null && tooltip.gameObject.activeSelf ? tooltipText : null;

        /// <summary>
        /// The item's full description beside <paramref name="over"/>: above an inventory slot (<paramref name="slot"/>,
        /// with its charges, key and sale price), left of a shop row (slot -1).
        /// </summary>
        public void ShowTooltip(ItemDef item, int slot, RectTransform over)
        {
            if (tooltip == null || item == null || over == null) return;
            tooltipItem = item;
            tooltipSlot = slot;
            tooltipOver = over;
            tooltipText.text = Describe(item, slot >= 0 ? Shown : null, slot);
            float height = tooltipText.preferredHeight;
            tooltip.sizeDelta = new Vector2(tooltip.sizeDelta.x, height + 24f);
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
            tooltip.gameObject.SetActive(true);
            tooltip.SetAsLastSibling();
        }

        public void HideTooltip()
        {
            if (tooltip != null) tooltip.gameObject.SetActive(false);
            tooltipItem = null;
            tooltipSlot = -1;
        }

        /// <summary>Name, price, the short and the full description and the numbers; for an owned slot its charges, key and sale.</summary>
        static string Describe(ItemDef item, HeroInventory owner, int slot)
        {
            var ci = CultureInfo.InvariantCulture;
            var text = new System.Text.StringBuilder();
            text.Append($"<b><size=28>{item.displayName}</size></b>\n<color=#FFD040>{item.cost} золота</color>");
            if (!string.IsNullOrEmpty(item.description)) text.Append($"\n\n{item.description}");
            if (!string.IsNullOrEmpty(item.details)) text.Append($"\n\n<color=#D8DCE0>{item.details}</color>");
            text.Append("\n<color=#A8B4C0>");
            if (item.castRangeBonus > 0f) text.Append($"\nДальность способностей: +{(item.castRangeBonus * 100f).ToString("0", ci)}");
            if (item.heroDamageBonus > 0f) text.Append($"\nУрон по героям: +{item.heroDamageBonus.ToString("0", ci)} (у хука — к попаданию и к каждому метру)");
            if (item.creepDamageBonus > 0f) text.Append($"\nУрон по крипам: +{item.creepDamageBonus.ToString("0", ci)} (у хука — к попаданию и к каждому метру)");
            if (item.IsUsable) text.Append($"\nДальность применения: {(item.castRange * 100f).ToString("0", ci)}");
            if (item.cooldown > 0f) text.Append($"\nПерезарядка: {item.cooldown.ToString("0.#", ci)} с");
            if (item.charges > 0) text.Append(owner != null ? $"\nЗарядов осталось: {owner.ChargesAt(slot)}" : $"\nЗарядов: {item.charges}");
            text.Append("</color>");
            if (owner != null && item.IsUsable && slot < SlotKeys.Length)
                text.Append($"\n\nКлавиша <b>{SlotKeys[slot]}</b> или клик по слоту — выбрать, клик по цели — применить");
            if (owner != null && owner == LocalHeroInventory())
                text.Append($"\n<color=#E8A860>Продать за {owner.SellValue(slot)} золота: правая кнопка мыши (на телефоне — удерживать слот)</color>");
            return text.ToString();
        }

        /// <summary>A dark panel with the description's text, made at start and shown over a hovered item.</summary>
        void BuildTooltip()
        {
            var canvas = goldLabel != null ? goldLabel.canvas : GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var old = canvas.transform.Find("ItemTooltip");
            if (old != null) Destroy(old.gameObject); // left from an earlier start
            tooltip = new GameObject("ItemTooltip", typeof(RectTransform)).GetComponent<RectTransform>();
            tooltip.SetParent(canvas.transform, false);
            tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 0.5f);
            tooltip.sizeDelta = new Vector2(460f, 200f);
            var bg = tooltip.gameObject.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.05f, 0.07f, 0.95f);
            bg.raycastTarget = false;
            var textRt = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
            textRt.SetParent(tooltip, false);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(14f, 12f);
            textRt.offsetMax = new Vector2(-14f, -12f);
            tooltipText = textRt.gameObject.AddComponent<Text>();
            tooltipText.font = goldLabel != null ? goldLabel.font : null;
            tooltipText.fontSize = 20;
            tooltipText.supportRichText = true;
            tooltipText.alignment = TextAnchor.UpperLeft;
            tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipText.verticalOverflow = VerticalWrapMode.Overflow;
            tooltipText.color = Color.white;
            tooltipText.raycastTarget = false;
            TextOutline.Add(tooltipText.gameObject, 1.2f); // thin: on a dark plate (a long text gets fewer copies)
            tooltip.gameObject.SetActive(false);
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
