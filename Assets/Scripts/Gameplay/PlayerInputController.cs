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
    /// On a phone (<see cref="TouchControls"/>) the left stick runs at full speed; a finger on the right stick picks
    /// the hook and aims it, letting go throws it at full range; a tap without dragging throws it straight ahead,
    /// dragging back to the centre before letting go cancels. A tap on a hero inspects it like a click.
    /// In first person on a phone three fingers play: the left stick runs relative to the look, the right stick aims
    /// the hook relative to it (a tap throws at the crosshair), and a finger dragging over the empty screen turns the
    /// view (TopDownCamera); the camera button in the top-left corner switches the views.
    /// </summary>
    public class PlayerInputController : MonoBehaviour
    {
        public const int NoAbility = -1;
        public const int HookSlot = 0;

        public Camera viewCamera;
        public HookAimIndicator aimIndicator;

        /// <summary>Ability waiting for a target (Dota-style quick cast is off), or NoAbility.</summary>
        public int SelectedAbility { get; private set; } = NoAbility;
        /// <summary>The player asked for the hook while it is on cooldown (the hero says "not yet").</summary>
        public event System.Action AbilityNotReady;

        PudgeMotor motor;
        HookAbility hook;
        Pudge self;
        TopDownCamera follow;
        ShopUI shop;

        void Awake()
        {
            motor = GetComponent<PudgeMotor>();
            hook = GetComponent<HookAbility>();
            self = GetComponent<Pudge>();
            if (aimIndicator == null) aimIndicator = GetComponentInChildren<HookAimIndicator>();
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera != null) follow = viewCamera.GetComponent<TopDownCamera>();
            LocalPlayer.Register(this);
        }

        void OnDestroy() => LocalPlayer.Unregister(this);

        void OnEnable()
        {
            TouchControls.HookPressed += OnHookStickPressed;
            TouchControls.HookReleased += OnHookStickReleased;
        }

        void OnDisable()
        {
            TouchControls.HookPressed -= OnHookStickPressed;
            TouchControls.HookReleased -= OnHookStickReleased;
            Deselect();
            motor.SetLookDirection(null);
        }

        /// <summary>Picks an ability for targeting; one on cooldown or a caught hero cannot be picked.</summary>
        public bool SelectAbility(int slot)
        {
            if (slot != HookSlot || (self != null && !self.IsControllable)) return false;
            if (!hook.IsReady)
            {
                if (SelectedAbility == NoAbility) AbilityNotReady?.Invoke();
                return false;
            }
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
            var touch = TouchControls.Active;
            var move = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
            }
            if (touch != null && touch.Move != Vector2.zero) move += touch.Move.normalized; // Dota heroes have no walk
            bool firstPerson = follow != null && follow.IsFirstPerson;
            motor.SetMoveInput(CameraRelative(move));
            motor.SetLookDirection(firstPerson ? follow.MoveForward : null);

            if (kb != null && kb.qKey.wasPressedThisFrame) SelectAbility(HookSlot);
            bool stickAiming = touch != null && touch.hookStick != null && touch.hookStick.IsHeld;
            if (stickAiming && SelectedAbility == NoAbility && hook.IsReady) SelectAbility(HookSlot); // held through the cooldown
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (SelectedAbility == NoAbility)
            {
                Inspect(firstPerson, overUi || (touch != null && touch.AnyHeld));
                return;
            }
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame)
                || (self != null && !self.IsControllable))
            {
                Deselect();
                return;
            }
            if (stickAiming) // thrown when the finger lets go (OnHookStickReleased)
            {
                var stickDir = hook.AimDirection(StickAimPoint(touch.hookStick.Value));
                if (aimIndicator != null) aimIndicator.Show(hook.AimStart, stickDir, hook.AimLength, hook.HitWidth);
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

        void OnHookStickPressed()
        {
            if (!OverlayInput.Captured) SelectAbility(HookSlot);
        }

        void OnHookStickReleased(Vector2 stick, bool dragged)
        {
            if (SelectedAbility != HookSlot || OverlayInput.Captured) return;
            bool cancelled = dragged && stick == Vector2.zero; // aimed, then brought back to the centre
            if (!cancelled) hook.TryCast(StickAimPoint(stick));
            Deselect();
        }

        /// <summary>Where the hook stick points on the ground: along the stick seen from the camera, or ahead when centred.</summary>
        Vector3 StickAimPoint(Vector2 stick)
        {
            // a tap throws ahead: at the crosshair in first person, where the hero faces in the top views
            var ahead = follow != null && follow.IsFirstPerson ? follow.MoveForward : transform.forward;
            var dir = stick == Vector2.zero ? ahead : CameraRelative(stick.normalized);
            return transform.position + dir * hook.EffectiveRange;
        }

        /// <summary>Not aiming: a left click (or a tap) picks a hero to look at, or nobody (the player's own data again).</summary>
        void Inspect(bool firstPerson, bool overUi)
        {
            var pointer = Pointer.current; // the mouse, or the first finger on a touchscreen
            if (firstPerson || overUi || pointer == null || !pointer.press.wasPressedThisFrame) return;
            if (shop == null) shop = FindAnyObjectByType<ShopUI>();
            if (shop == null) return;
            var hero = HeroPicker.PickAt(pointer.position.ReadValue(), viewCamera);
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
