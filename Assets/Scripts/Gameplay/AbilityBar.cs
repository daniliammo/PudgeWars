using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Ability list at the bottom centre of the HUD. Slot 0 is the hook: its icon dims while the hook is out
    /// or on cooldown, the remaining cooldown is written over the icon, the frame lights up while it is
    /// selected for targeting. Clicking a slot selects it like its key.
    /// </summary>
    public class AbilityBar : MonoBehaviour
    {
        public PlayerInputController input;
        public AbilitySlotView[] slots = new AbilitySlotView[0];
        [Tooltip("Remaining cooldown: seconds and milliseconds.")]
        public string cooldownFormat = "0.000";
        public Color frameColor = new(0.16f, 0.17f, 0.20f, 1f);
        public Color selectedFrameColor = new(0.25f, 0.95f, 0.35f, 1f);

        HookAbility hook;

        void Start()
        {
            if (input == null) input = LocalPlayer.Input;
            if (input != null) hook = input.GetComponent<HookAbility>();
            LocalPlayer.Changed += OnLocalPlayerChanged;
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

        void Update()
        {
            if (slots.Length == 0 || slots[PlayerInputController.HookSlot] == null) return;
            var view = slots[PlayerInputController.HookSlot];
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
