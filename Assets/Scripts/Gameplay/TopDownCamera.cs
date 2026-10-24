using UnityEngine;
using UnityEngine.InputSystem;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Player camera with views switched by V: from above behind the player's side looking slightly ahead
    /// toward the enemy, the same view from the enemy side (mirrored across the pit), and first person with
    /// shooter controls (mouse look with a locked cursor and a crosshair; the cursor is freed while the shop is open).
    /// The cursor is captured on entering first person and released on leaving it; when something else frees it
    /// (Escape in the editor, another window), a click or the window regaining focus captures it again.
    /// Follows the local player's hero (<see cref="LocalPlayer"/>); for a hero of the far team the views are mirrored,
    /// so the own side is always at the bottom of the screen.
    /// </summary>
    [DefaultExecutionOrder(-10)] // before PlayerInputController: a click that recaptures the cursor is not a cast
    public class TopDownCamera : MonoBehaviour
    {
        public enum View { OwnSide, EnemySide, FirstPerson }

        public Transform target;
        public Vector3 offset = new(0f, 11f, -10f);
        public Vector3 lookAhead = new(0f, 0f, 4.5f);
        public float smoothTime = 0.15f;

        [Header("First person")]
        [Tooltip("Eye position above the hero's feet, metres.")] public float eyeHeight = 1.9f;
        [Tooltip("Eye position in front of the hero's centre, so the head does not block the view, metres.")]
        public float eyeForward = 0.5f;
        [Tooltip("Initial look down from the horizon, degrees.")] public float firstPersonPitch = 12f;
        public Vector2 pitchLimits = new(-60f, 80f);
        [Tooltip("Degrees per pixel of mouse movement.")] public float mouseSensitivity = 0.12f;
        public float firstPersonFov = 75f;
        [Tooltip("Shown only in first person.")] public GameObject crosshair;

        public View Mode { get; private set; }
        public bool IsFirstPerson => Mode == View.FirstPerson;
        /// <summary>Mouse look is on: first person with the shop closed (the cursor should be locked).</summary>
        public bool WantsCursorCaptured => IsFirstPerson && !CursorNeeded;
        /// <summary>The shop or an overlay window is open: the cursor must be free.</summary>
        bool CursorNeeded => (shop != null && shop.IsOpen) || OverlayInput.Captured;
        /// <summary>The cursor is locked for mouse look.</summary>
        public bool CursorCaptured => Cursor.lockState == CursorLockMode.Locked;
        /// <summary>This frame's left click only recaptured the cursor and must not act in the game.</summary>
        public bool ClickConsumed { get; private set; }

        /// <summary>Horizontal direction WASD's "forward" maps to: the screen's up in the top views, the look
        /// direction in first person.</summary>
        public Vector3 MoveForward => Flat(IsFirstPerson ? Quaternion.Euler(0f, yaw, 0f) * Vector3.forward : transform.forward);

        Camera cam;
        ShopUI shop;
        float baseFov, yaw, pitch;
        bool shopWasOpen;
        Vector3 velocity;

        void Awake()
        {
            cam = GetComponent<Camera>();
            baseFov = cam != null ? cam.fieldOfView : 60f;
            if (crosshair != null) crosshair.SetActive(false);
        }

        void Start()
        {
            shop = FindFirstObjectByType<ShopUI>();
            LocalPlayer.Changed += OnLocalPlayerChanged;
        }

        void OnDestroy() => LocalPlayer.Changed -= OnLocalPlayerChanged;

        void OnDisable() => UnlockCursor();

        /// <summary>A network game spawned (or removed) the local hero: look at it from its own side.</summary>
        void OnLocalPlayerChanged()
        {
            if (LocalPlayer.Hero == null) return;
            target = LocalPlayer.Hero.transform;
            SetView(View.OwnSide);
        }

        void Update()
        {
            ClickConsumed = false;
            var kb = Keyboard.current;
            if (kb != null && kb.vKey.wasPressedThisFrame && !OverlayInput.Captured) NextView();
            if (!IsFirstPerson) return;

            var mouse = Mouse.current;
            bool shopOpen = CursorNeeded;
            if (shopOpen) UnlockCursor();
            else if (!CursorCaptured && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                CaptureCursor();
                ClickConsumed = true;
            }
            else if (shopWasOpen) CaptureCursor(); // closing the shop returns to mouse look
            shopWasOpen = shopOpen;

            if (CursorCaptured && mouse != null) AddLook(mouse.delta.ReadValue() * mouseSensitivity);
            if (crosshair != null) crosshair.SetActive(!shopOpen);
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused && WantsCursorCaptured) CaptureCursor();
        }

        /// <summary>Turns the first-person view: x = yaw to the right, y = pitch up, degrees.</summary>
        public void AddLook(Vector2 degrees)
        {
            yaw += degrees.x;
            pitch = Mathf.Clamp(pitch - degrees.y, pitchLimits.x, pitchLimits.y);
        }

        void LateUpdate()
        {
            if (target == null) return;
            Pose(out var position, out var rotation);
            transform.position = IsFirstPerson
                ? position
                : Vector3.SmoothDamp(transform.position, position, ref velocity, smoothTime);
            transform.rotation = rotation;
        }

        public void NextView() => SetView((View)(((int)Mode + 1) % 3));

        public void SetView(View view)
        {
            Mode = view;
            if (cam != null) cam.fieldOfView = IsFirstPerson ? firstPersonFov : baseFov;
            if (IsFirstPerson && target != null)
            {
                yaw = Quaternion.LookRotation(Flat(target.forward)).eulerAngles.y;
                pitch = firstPersonPitch;
            }
            if (IsFirstPerson) CaptureCursor();
            else UnlockCursor();
            if (crosshair != null) crosshair.SetActive(IsFirstPerson);
            SnapToTarget();
        }

        public void SnapToTarget()
        {
            if (target == null) return;
            Pose(out var position, out var rotation);
            transform.SetPositionAndRotation(position, rotation);
            velocity = Vector3.zero;
        }

        void Pose(out Vector3 position, out Quaternion rotation)
        {
            if (IsFirstPerson)
            {
                var heading = Quaternion.Euler(0f, yaw, 0f);
                position = target.position + Vector3.up * eyeHeight + heading * Vector3.forward * eyeForward;
                rotation = heading * Quaternion.Euler(pitch, 0f, 0f);
                return;
            }
            // the own view stands behind the own side and looks across the pit at the enemy: for the far team
            // (z > 0) the camera arm and the look-ahead are turned around
            var turn = Quaternion.Euler(0f, LocalPlayer.Team == Team.Bots ? 180f : 0f, 0f);
            var focus = target.position + turn * lookAhead;
            var arm = turn * offset;
            if (Mode == View.EnemySide)
            {
                // the own view mirrored across the middle of the pit: the player is where the enemies are
                // in the own view, the enemies are in front of the camera
                float mid = Arena.Instance != null ? Arena.Instance.transform.position.z : 0f;
                focus.z = 2f * mid - focus.z;
                arm = Quaternion.Euler(0f, 180f, 0f) * arm;
            }
            position = focus + arm;
            rotation = Quaternion.LookRotation(-arm);
        }

        static void CaptureCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }
    }
}
