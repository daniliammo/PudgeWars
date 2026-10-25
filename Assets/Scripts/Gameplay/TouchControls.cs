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
    /// Built at runtime next to them: a button in the top-left corner that switches the camera view like V, and in
    /// first person a zone over the rest of the screen where a third finger turns the view (<see cref="TakeLook"/>).
    /// </summary>
    public class TouchControls : MonoBehaviour
    {
        public VirtualJoystick moveStick;
        public VirtualJoystick hookStick;
        [Tooltip("Radial filled image over the hook stick's icon.")]
        public Image hookCooldown;
        [Tooltip("Show the sticks without a touchscreen (driven by the mouse).")]
        public bool showOnDesktop;
        [Tooltip("Camera view button: size and offset from the top-left corner, canvas units (under the score).")]
        public float cameraButtonSize = 120f;
        public Vector2 cameraButtonOffset = new(30f, -125f);

        const string CameraIconPath = "Touch/CameraSwitch";

        /// <summary>The controls on screen, or null where there are none (desktop) or they are hidden.</summary>
        public static TouchControls Active { get; private set; }

        /// <summary>A finger came down on the hook stick.</summary>
        public static event Action HookPressed;
        /// <summary>The hook stick was let go: the last stick value and whether it was dragged out of its dead zone.</summary>
        public static event Action<Vector2, bool> HookReleased;

        /// <summary>Run direction in stick space (x right, y up), length up to 1; zero when the stick is idle.</summary>
        public Vector2 Move => moveStick != null ? moveStick.Value : Vector2.zero;
        public bool AnyHeld => (moveStick != null && moveStick.IsHeld) || (hookStick != null && hookStick.IsHeld);

        /// <summary>Screen pixels the look finger has dragged since the last call (zero outside first person).</summary>
        public Vector2 TakeLook() => lookZone != null && lookZone.isActiveAndEnabled ? lookZone.TakeDelta() : Vector2.zero;

        /// <summary>The camera view button (null until Start).</summary>
        public Button CameraButton { get; private set; }
        /// <summary>The first-person look zone (null until Start; active only in first person).</summary>
        public TouchLookZone LookZone => lookZone;

        HookAbility hook;
        TopDownCamera view;
        TouchLookZone lookZone;

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
            var cam = Camera.main;
            view = cam != null ? cam.GetComponent<TopDownCamera>() : null;
            BuildLookZone();
            BuildCameraButton();
            LocalPlayer.Changed += OnLocalPlayerChanged;
            OnLocalPlayerChanged();
        }

        void OnDestroy()
        {
            LocalPlayer.Changed -= OnLocalPlayerChanged;
            if (Active == this) Active = null;
        }

        void OnLocalPlayerChanged() => hook = LocalPlayer.Hero != null ? LocalPlayer.Hero.GetComponent<HookAbility>() : null;

        /// <summary>Transparent zone over the whole screen, under the sticks (first sibling): a finger off them turns the view.</summary>
        void BuildLookZone()
        {
            var rt = new GameObject("LookZone", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.SetAsFirstSibling();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.gameObject.AddComponent<Image>().color = Color.clear; // invisible, but catches the touches
            lookZone = rt.gameObject.AddComponent<TouchLookZone>();
            rt.gameObject.SetActive(false);
        }

        /// <summary>The camera view button in the top-left corner (Resources/Touch/CameraSwitch: a camera in turning arrows).</summary>
        void BuildCameraButton()
        {
            var rt = new GameObject("CameraButton", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = cameraButtonOffset;
            rt.sizeDelta = Vector2.one * cameraButtonSize;
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = Resources.Load<Sprite>(CameraIconPath);
            if (image.sprite == null) image.color = new Color(0.08f, 0.08f, 0.1f, 0.9f);
            image.preserveAspect = true;
            CameraButton = rt.gameObject.AddComponent<Button>();
            CameraButton.targetGraphic = image;
            var colors = CameraButton.colors;
            colors.pressedColor = new Color(1f, 0.85f, 0.4f);
            CameraButton.colors = colors;
            CameraButton.onClick.AddListener(() =>
            {
                if (view != null && !OverlayInput.Captured) view.NextView();
            });
            rt.gameObject.SetActive(false);
        }

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
            bool canSwitch = visible && view != null;
            if (CameraButton != null && CameraButton.gameObject.activeSelf != canSwitch) CameraButton.gameObject.SetActive(canSwitch);
            bool look = canSwitch && view.IsFirstPerson;
            if (lookZone != null && lookZone.gameObject.activeSelf != look) lookZone.gameObject.SetActive(look);
        }
    }
}
