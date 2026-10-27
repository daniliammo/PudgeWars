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
    /// A ring around the hero shows the reach of whatever is picked: the hook's tip at full range, or an item's.
    /// Items: Z X C V B N (as in Dota) or a click (tap) on a slot of the shop bar picks a usable item (Tango, Iron
    /// Branch, Quelling Blade); a click on a tree (Tango, Quelling Blade) or on the ground (Iron Branch)
    /// uses it — out of reach the hero first walks up to the target, as in Dota (any move order cancels that).
    /// </summary>
    public class PlayerInputController : MonoBehaviour
    {
        public const int NoAbility = -1;
        public const int HookSlot = 0;
        public const int NoItem = -1;
        [Tooltip("A click this close to a tree's foot picks the tree, metres.")]
        public float treePickRadius = 1.5f;
        [Tooltip("The reach ring around the hero (the hook's or an item's).")]
        public Color rangeColor = new(0.55f, 0.95f, 0.45f, 0.8f);

        public Camera viewCamera;
        public HookAimIndicator aimIndicator;

        /// <summary>Ability waiting for a target (Dota-style quick cast is off), or NoAbility.</summary>
        public int SelectedAbility { get; private set; } = NoAbility;
        /// <summary>Inventory slot whose item waits for a target, or NoItem.</summary>
        public int SelectedItem { get; private set; } = NoItem;
        /// <summary>The player asked for the hook while it is on cooldown (the hero says "not yet").</summary>
        public event System.Action AbilityNotReady;

        PudgeMotor motor;
        HookAbility hook;
        Pudge self;
        TopDownCamera follow;
        ShopUI shop;
        HeroInventory inventory;
        LineRenderer rangeRing;
        // an item ordered on a target out of reach: the hero walks up to it first
        int pendingSlot = NoItem, pendingTree = HeroInventory.NoTree;
        ItemDef pendingItem;
        Vector3 pendingPoint;
        static readonly Key[] ItemKeys = { Key.Z, Key.X, Key.C, Key.V, Key.B, Key.N };

        void Awake()
        {
            motor = GetComponent<PudgeMotor>();
            hook = GetComponent<HookAbility>();
            self = GetComponent<Pudge>();
            inventory = GetComponent<HeroInventory>();
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
            ShopUI.ItemSlotClicked += OnItemSlotClicked;
        }

        void OnDisable()
        {
            TouchControls.HookPressed -= OnHookStickPressed;
            TouchControls.HookReleased -= OnHookStickReleased;
            ShopUI.ItemSlotClicked -= OnItemSlotClicked;
            Deselect();
            DeselectItem();
            CancelPendingUse();
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
            DeselectItem();
            CancelPendingUse();
            SelectedAbility = slot;
            return true;
        }

        /// <summary>Picks the usable item in an inventory slot for targeting (again: drops it).</summary>
        public bool SelectItem(int slot)
        {
            if (inventory == null || slot < 0 || slot >= inventory.Items.Count || !inventory.Items[slot].IsUsable) return false;
            if (SelectedItem == slot)
            {
                DeselectItem();
                return false;
            }
            if (!inventory.CanUse(slot))
            {
                if (inventory.CooldownLeft(slot) > 0f) AbilityNotReady?.Invoke();
                return false;
            }
            Deselect();
            SelectedItem = slot;
            return true;
        }

        public void DeselectItem()
        {
            SelectedItem = NoItem;
            if (SelectedAbility == NoAbility) HideRange();
        }

        void HideRange()
        {
            if (rangeRing != null) rangeRing.enabled = false;
        }

        void CancelPendingUse()
        {
            pendingSlot = NoItem;
            pendingItem = null;
        }

        void OnItemSlotClicked(int slot)
        {
            if (!OverlayInput.Captured) SelectItem(slot);
        }

        public void Deselect()
        {
            SelectedAbility = NoAbility;
            if (aimIndicator != null) aimIndicator.Hide();
            if (SelectedItem == NoItem) HideRange();
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
            if (move != Vector2.zero) CancelPendingUse(); // a move order replaces walking up to an item's target
            motor.SetMoveInput(pendingSlot != NoItem ? WalkToPendingTarget() : CameraRelative(move));
            motor.SetLookDirection(firstPerson ? follow.MoveForward : null);

            if (kb != null && kb.qKey.wasPressedThisFrame) SelectAbility(HookSlot);
            for (int i = 0; kb != null && i < ItemKeys.Length; i++)
                if (kb[ItemKeys[i]].wasPressedThisFrame) SelectItem(i);
            bool stickAiming = touch != null && touch.hookStick != null && touch.hookStick.IsHeld;
            if (stickAiming && SelectedAbility == NoAbility && hook.IsReady) SelectAbility(HookSlot); // held through the cooldown
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (SelectedItem != NoItem)
            {
                TargetItem(kb, mouse, firstPerson, overUi || (touch != null && touch.AnyHeld));
                return;
            }
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
            // as far as the aim lines and at their height (flat on the ground it looks shorter under the tilted camera)
            ShowRange(hook.AimLength, hook.AimStart.y - transform.position.y);
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

        /// <summary>
        /// An item waits for its target: the ring shows its reach; a click on a tree or the ground orders the use
        /// (walking up first if out of reach); right button, Escape, or the hero caught drops it.
        /// </summary>
        void TargetItem(Keyboard kb, Mouse mouse, bool firstPerson, bool overUi)
        {
            int slot = SelectedItem;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame)
                || (self != null && !self.IsControllable) || inventory == null || slot >= inventory.Items.Count
                || !inventory.Items[slot].IsUsable)
            {
                DeselectItem();
                return;
            }
            var item = inventory.Items[slot];
            ShowRange(item.castRange + (self != null ? self.radius : 0f) + (TerrainTrees.Instance != null ? TerrainTrees.Instance.radius : 0f));
            var pointer = Pointer.current;
            bool click = firstPerson
                ? mouse != null && mouse.leftButton.wasPressedThisFrame && follow.CursorCaptured && !follow.ClickConsumed
                : pointer != null && pointer.press.wasPressedThisFrame && !overUi;
            if (!click) return;
            var screen = firstPerson || pointer == null
                ? new Vector2(Screen.width / 2f, Screen.height / 2f) // the crosshair
                : pointer.position.ReadValue();
            if (!TryGetGroundPoint(screen, out var point)) return;
            int tree = HeroInventory.NoTree;
            var trees = TerrainTrees.Instance;
            if (item.TargetsTree)
            {
                tree = trees != null ? trees.Nearest(point, treePickRadius) : HeroInventory.NoTree;
                if (tree < 0) return; // not on a tree: still waiting for one
                point = trees.Position(tree);
            }
            DeselectItem();
            if (!inventory.TryUse(slot, point, tree))
            {
                // out of reach (or blocked for now): walk up and try there
                pendingSlot = slot;
                pendingItem = item;
                pendingPoint = point;
                pendingTree = tree;
            }
        }

        /// <summary>Walking up to an ordered item's target; uses the item once within reach.</summary>
        Vector3 WalkToPendingTarget()
        {
            var trees = TerrainTrees.Instance;
            bool gone = inventory == null || pendingSlot >= inventory.Items.Count || inventory.Items[pendingSlot] != pendingItem
                        || trees == null || (pendingItem.TargetsTree && !trees.IsStanding(pendingTree))
                        || (self != null && !self.IsControllable);
            if (gone)
            {
                CancelPendingUse();
                return Vector3.zero;
            }
            if (inventory.InReach(pendingPoint, trees.radius, pendingItem))
            {
                inventory.TryUse(pendingSlot, pendingPoint, pendingTree);
                CancelPendingUse(); // used, or cannot be (a hero on the spot): either way the order is done
                return Vector3.zero;
            }
            var to = pendingPoint - transform.position;
            to.y = 0f;
            return to.normalized;
        }

        /// <summary>The reach ring this frame (its radius from the hero's centre), or null while nothing is picked.</summary>
        public LineRenderer RangeRing => rangeRing != null && rangeRing.enabled ? rangeRing : null;

        /// <summary>
        /// A thin ring around the hero, <paramref name="radius"/> from its centre at <paramref name="height"/> over its
        /// feet (just over the ground by default): the reach of what is picked.
        /// </summary>
        void ShowRange(float radius, float height = 0.08f)
        {
            if (rangeRing == null)
            {
                rangeRing = new GameObject("RangeRing").AddComponent<LineRenderer>();
                rangeRing.transform.SetParent(transform, false);
                rangeRing.useWorldSpace = true; // whatever the hero's scale
                rangeRing.loop = true;
                rangeRing.widthMultiplier = 0.06f;
                rangeRing.positionCount = 96;
                var source = aimIndicator != null ? aimIndicator.left : null;
                rangeRing.sharedMaterial = source != null ? source.sharedMaterial : new Material(Shader.Find("Sprites/Default"));
                rangeRing.startColor = rangeRing.endColor = rangeColor;
            }
            var centre = transform.position;
            for (int i = 0; i < rangeRing.positionCount; i++)
            {
                float a = i * Mathf.PI * 2f / rangeRing.positionCount;
                rangeRing.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius));
            }
            rangeRing.enabled = true;
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
