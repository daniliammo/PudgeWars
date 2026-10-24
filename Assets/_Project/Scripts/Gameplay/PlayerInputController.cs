using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Desktop controls for the test arena: WASD / arrows to move; Q selects the first ability (the hook)
    /// and shows where it will fly, left mouse button or Space casts it toward the cursor, right mouse
    /// button or Escape drops the selection.
    /// </summary>
    public class PlayerInputController : MonoBehaviour
    {
        public const int NoAbility = -1;
        public const int HookSlot = 0;

        public Camera viewCamera;
        public HookAimIndicator aimIndicator;

        /// <summary>Ability waiting for a target (Dota-style quick cast is off), or NoAbility.</summary>
        public int SelectedAbility { get; private set; } = NoAbility;

        PudgeMotor motor;
        HookAbility hook;
        Hookable self;

        void Awake()
        {
            motor = GetComponent<PudgeMotor>();
            hook = GetComponent<HookAbility>();
            self = GetComponent<Hookable>();
            if (aimIndicator == null) aimIndicator = GetComponentInChildren<HookAimIndicator>();
            if (viewCamera == null) viewCamera = Camera.main;
        }

        void OnDisable() => Deselect();

        /// <summary>Picks an ability for targeting; one on cooldown or a caught hero cannot be picked.</summary>
        public bool SelectAbility(int slot)
        {
            if (slot != HookSlot || !hook.IsReady || (self != null && !self.IsControllable)) return false;
            SelectedAbility = slot;
            return true;
        }

        public void Deselect()
        {
            SelectedAbility = NoAbility;
            if (aimIndicator != null) aimIndicator.Hide();
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var move = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
            }
            motor.SetMoveInput(CameraRelative(move));

            if (kb != null && kb.qKey.wasPressedThisFrame) SelectAbility(HookSlot);
            if (SelectedAbility == NoAbility) return;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame)
                || (self != null && !self.IsControllable))
            {
                Deselect();
                return;
            }
            if (mouse == null || !TryGetGroundPoint(mouse.position.ReadValue(), out var point))
            {
                if (aimIndicator != null) aimIndicator.Hide();
                return;
            }

            var dir = hook.AimDirection(point);
            if (aimIndicator != null) aimIndicator.Show(hook.PredictLaunchPoint(dir), dir, hook.EffectiveRange, hook.HitWidth);

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool cast = (mouse.leftButton.wasPressedThisFrame && !overUi) || (kb != null && kb.spaceKey.wasPressedThisFrame);
            if (cast && hook.TryCast(point)) Deselect();
        }

        Vector3 CameraRelative(Vector2 move)
        {
            if (viewCamera == null) return new Vector3(move.x, 0f, move.y);
            var fwd = viewCamera.transform.forward; fwd.y = 0f; fwd.Normalize();
            var right = viewCamera.transform.right; right.y = 0f; right.Normalize();
            return fwd * move.y + right * move.x;
        }

        bool TryGetGroundPoint(Vector2 screen, out Vector3 point)
        {
            point = default;
            if (viewCamera == null) return false;
            var ray = viewCamera.ScreenPointToRay(screen);
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var t)) return false;
            point = ray.GetPoint(t);
            return true;
        }
    }
}
