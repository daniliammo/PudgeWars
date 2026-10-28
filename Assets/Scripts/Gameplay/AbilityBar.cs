using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Ability list at the bottom centre of the HUD. Slot 0 is the hook: its icon dims while the hook is out
    /// or on cooldown, the remaining cooldown is written over the icon, the frame lights up while it is
    /// selected for targeting. Clicking a slot selects it like its key. As in Dota, the mana cost stands in blue at the
    /// slot's bottom right and the icon turns blue while the hero lacks the mana. In first person the bar rises by
    /// <see cref="firstPersonRise"/>: the own health and mana bar takes the bottom edge under it (<see cref="HealthBars"/>).
    /// </summary>
    public class AbilityBar : MonoBehaviour
    {
        public PlayerInputController input;
        public AbilitySlotView[] slots = new AbilitySlotView[0];
        [Tooltip("Remaining cooldown: seconds and milliseconds.")]
        public string cooldownFormat = "0.000";
        public Color frameColor = new(0.16f, 0.17f, 0.20f, 1f);
        public Color selectedFrameColor = new(0.25f, 0.95f, 0.35f, 1f);
        public Color manaCostColor = new(0.45f, 0.7f, 1f);
        [Tooltip("The icon's tint while the hero lacks the mana for the ability.")]
        public Color noManaTint = new(0.35f, 0.5f, 1f);
        [Tooltip("In first person the bar stands this much higher, canvas units (the own health bar goes under it).")]
        public float firstPersonRise = 86f;

        HookAbility hook;
        Text manaCostLabel;
        RectTransform panel;
        float panelY;
        TopDownCamera follow;

        /// <summary>The hook's mana cost over its slot (made at start).</summary>
        public Text ManaCostLabel => manaCostLabel;
        /// <summary>The bar is raised over the own health bar (first person).</summary>
        public bool Raised => panel != null && !Mathf.Approximately(panel.anchoredPosition.y, panelY);

        void Start()
        {
            if (input == null) input = LocalPlayer.Input;
            if (input != null) hook = input.GetComponent<HookAbility>();
            LocalPlayer.Changed += OnLocalPlayerChanged;
            if (slots.Length > 0 && slots[PlayerInputController.HookSlot] != null)
            {
                var slot = (RectTransform)slots[PlayerInputController.HookSlot].transform;
                panel = slot.parent as RectTransform;
                if (panel != null) panelY = panel.anchoredPosition.y;
                manaCostLabel = BuildManaCost(slot);
            }
            for (int i = 0; i < slots.Length; i++)
            {
                int slot = i;
                if (slots[i] != null && slots[i].button != null)
                    slots[i].button.onClick.AddListener(() => input?.SelectAbility(slot));
            }
        }

        void OnDestroy() => LocalPlayer.Changed -= OnLocalPlayerChanged;

        /// <summary>A network game spawned (or removed) the local hero.</summary>
        void OnLocalPlayerChanged()
        {
            input = LocalPlayer.Input;
            hook = input != null ? input.GetComponent<HookAbility>() : null;
        }

        /// <summary>The mana cost in blue at the slot's bottom right.</summary>
        Text BuildManaCost(RectTransform slot)
        {
            var old = slot.Find("ManaCost");
            if (old != null) Destroy(old.gameObject); // left from an earlier start
            var rt = new GameObject("ManaCost", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(slot, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-6f, 4f);
            rt.sizeDelta = new Vector2(80f, 30f);
            var text = rt.gameObject.AddComponent<Text>();
            var key = slots[PlayerInputController.HookSlot].keyLabel;
            text.font = key != null ? key.font : null;
            text.fontSize = 24;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.LowerRight;
            text.color = manaCostColor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            TextOutline.Add(text.gameObject, 1.6f, bold: 0.6f);
            return text;
        }

        bool IsFirstPerson
        {
            get
            {
                var cam = Camera.main;
                if (follow == null || follow.gameObject != (cam != null ? cam.gameObject : null))
                    follow = cam != null ? cam.GetComponent<TopDownCamera>() : null;
                return follow != null && follow.IsFirstPerson;
            }
        }

        void Update()
        {
            if (slots.Length == 0 || slots[PlayerInputController.HookSlot] == null) return;
            var view = slots[PlayerInputController.HookSlot];
            if (panel != null)
                panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, panelY + (IsFirstPerson && LocalPlayer.Hero != null ? firstPersonRise : 0f));
            if (manaCostLabel != null)
            {
                int cost = hook != null ? Mathf.CeilToInt(hook.manaCost) : 0;
                manaCostLabel.text = cost > 0 ? cost.ToString() : "";
            }
            if (view.icon != null) view.icon.color = hook != null && !hook.HasMana ? noManaTint : Color.white;
            float remaining = hook != null ? hook.CooldownRemaining : 0f;
            bool busy = hook != null && hook.IsBusy;
            bool cooling = !busy && remaining > 0f;

            if (view.cooldownOverlay != null)
            {
                view.cooldownOverlay.enabled = busy || cooling;
                view.cooldownOverlay.fillAmount = busy ? 1f : hook != null && hook.cooldown > 0f ? remaining / hook.cooldown : 0f;
            }
            if (view.cooldownLabel != null) view.cooldownLabel.text = cooling ? remaining.ToString(cooldownFormat, CultureInfo.InvariantCulture) : "";
            if (view.frame != null)
                view.frame.color = input != null && input.SelectedAbility == PlayerInputController.HookSlot ? selectedFrameColor : frameColor;
        }
    }
}
