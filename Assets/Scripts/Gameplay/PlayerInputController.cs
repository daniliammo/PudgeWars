using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Desktop controls for the test arena: WASD / arrows to move relative to the camera view (V switches it,
    /// see TopDownCamera); Q selects the first ability (the hook) and shows where it will fly, left mouse
    /// button or Space casts it toward the cursor, right mouse button or Escape drops the selection. With no ability
    /// selected, a left click on a hero puts its gold and items on the shop bar in place of the player's
    /// (<see cref="ShopUI.Inspect"/>); a click on empty ground or Escape brings the player's back.
    /// In first person the controls are a shooter's: the hero faces where the camera looks, strafes with
    /// A / D and throws the hook at the crosshair.
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
        TopDownCamera follow;
        ShopUI shop;

        void Awake()
        {
            motor = GetComponent<PudgeMotor>();
            hook = GetComponent<HookAbility>();
            self = GetComponent<Hookable>();
            if (aimIndicator == null) aimIndicator = GetComponentInChildren<HookAimIndicator>();
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera != null) follow = viewCamera.GetComponent<TopDownCamera>();
            LocalPlayer.Register(this);
        }

        void OnDestroy() => LocalPlayer.Unregister(this);

        void OnDisable()
        {
            Deselect();
            motor.SetLookDirection(null);
        }

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
            if (OverlayInput.Captured)
            {
                motor.SetMoveInput(Vector3.zero);
                if (SelectedAbility != NoAbility) Deselect();
                return;
            }
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
            bool firstPerson = follow != null && follow.IsFirstPerson;
            motor.SetMoveInput(CameraRelative(move));
            motor.SetLookDirection(firstPerson ? follow.MoveForward : null);

            if (kb != null && kb.qKey.wasPressedThisFrame) SelectAbility(HookSlot);
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (SelectedAbility == NoAbility)
            {
                Inspect(mouse, firstPerson, overUi);
                return;
            }
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame)
                || (self != null && !self.IsControllable))
            {
                Deselect();
                return;
            }
            Vector3 point;
            if (firstPerson) point = transform.position + follow.MoveForward * hook.EffectiveRange; // the crosshair
            else if (mouse == null || !TryGetGroundPoint(mouse.position.ReadValue(), out point))
            {
                if (aimIndicator != null) aimIndicator.Hide();
                return;
            }

            var dir = hook.AimDirection(point);
            if (aimIndicator != null) aimIndicator.Show(hook.AimStart, dir, hook.AimLength, hook.HitWidth);

            bool click = mouse != null && mouse.leftButton.wasPressedThisFrame
                         && (firstPerson ? follow.CursorCaptured && !follow.ClickConsumed : !overUi);
            bool cast = click || (kb != null && kb.spaceKey.wasPressedThisFrame);
            if (cast && hook.TryCast(point)) Deselect();
        }

        /// <summary>Not aiming: a left click picks a hero to look at, or nobody (the player's own data again).</summary>
        void Inspect(Mouse mouse, bool firstPerson, bool overUi)
        {
            if (firstPerson || overUi || mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (shop == null) shop = FindAnyObjectByType<ShopUI>();
            if (shop == null) return;
            var hero = HeroPicker.PickAt(mouse.position.ReadValue(), viewCamera);
            shop.Inspect(hero != null ? hero.GetComponent<HeroInventory>() : null);
        }

        Vector3 CameraRelative(Vector2 move)
        {
            if (viewCamera == null) return new Vector3(move.x, 0f, move.y);
            var fwd = follow != null ? follow.MoveForward : viewCamera.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
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
