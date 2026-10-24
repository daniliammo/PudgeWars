using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Phone controls (PW_TouchInterface in the original): the left stick runs, the right one (JoystikHook) aims the
    /// hook and throws it when the finger lets go; <see cref="PlayerInputController"/> reads both. The hook stick
    /// darkens with a radial fill while the hook is out or on cooldown, like the ability icon. Shown on phones and
    /// on devices with a touchscreen; <see cref="showOnDesktop"/> shows them with the mouse too, for trying out in
    /// the editor. Hidden while an overlay window (the network menu) has the input.
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        public VirtualJoystick moveStick;
        public VirtualJoystick hookStick;
        [Tooltip("Radial filled image over the hook stick's icon.")]
        public Image hookCooldown;
        [Tooltip("Show the sticks without a touchscreen (driven by the mouse).")]
        public bool showOnDesktop;

        /// <summary>The controls on screen, or null where there are none (desktop) or they are hidden.</summary>
        public static TouchControls Active { get; private set; }

        /// <summary>A finger came down on the hook stick.</summary>
        public static event Action HookPressed;
        /// <summary>The hook stick was let go: the last stick value and whether it was dragged out of its dead zone.</summary>
        public static event Action<Vector2, bool> HookReleased;

        /// <summary>Run direction in stick space (x right, y up), length up to 1; zero when the stick is idle.</summary>
        public Vector2 Move => moveStick != null ? moveStick.Value : Vector2.zero;
        public bool AnyHeld => (moveStick != null && moveStick.IsHeld) || (hookStick != null && hookStick.IsHeld);

        HookAbility hook;

        static bool HasTouch => Application.isMobilePlatform || Touchscreen.current != null;

        /// <summary>The arena is laid out for a wide screen: phones stay in landscape.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LockLandscape()
        {
            if (!Application.isMobilePlatform) return;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
        }

        void Start()
        {
            if (hookStick != null)
            {
                hookStick.Pressed += () => HookPressed?.Invoke();
                hookStick.Released += value => HookReleased?.Invoke(value, hookStick.WasDragged);
            }
            LocalPlayer.Changed += OnLocalPlayerChanged;
            OnLocalPlayerChanged();
        }

        void OnDestroy()
        {
            LocalPlayer.Changed -= OnLocalPlayerChanged;
            if (Active == this) Active = null;
        }

        void OnLocalPlayerChanged() => hook = LocalPlayer.Hero != null ? LocalPlayer.Hero.GetComponent<HookAbility>() : null;

        void Update()
        {
            bool visible = (HasTouch || showOnDesktop) && !OverlayInput.Captured;
            SetVisible(visible);
            if (!visible || hookCooldown == null) return;

            float remaining = hook != null ? hook.CooldownRemaining : 0f;
            bool busy = hook != null && hook.IsBusy;
            hookCooldown.enabled = busy || remaining > 0f;
            hookCooldown.fillAmount = busy ? 1f : hook != null && hook.cooldown > 0f ? remaining / hook.cooldown : 0f;
        }

        void SetVisible(bool visible)
        {
            Active = visible ? this : Active == this ? null : Active;
            // switching a stick off mid-drag drops its finger without a release (no stray cast)
            if (moveStick != null && moveStick.gameObject.activeSelf != visible) moveStick.gameObject.SetActive(visible);
            if (hookStick != null && hookStick.gameObject.activeSelf != visible) hookStick.gameObject.SetActive(visible);
        }
    }
}
