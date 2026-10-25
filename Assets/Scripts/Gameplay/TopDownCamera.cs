using UnityEngine;
using UnityEngine.InputSystem;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Player camera with views switched by V: from above behind the player's side looking slightly ahead
    /// toward the enemy, a view from behind the enemy side's back wall across the pit at the player (it follows the
    /// player only sideways, so every enemy stays in front of it however close the player comes), and first person with
    /// shooter controls (mouse look with a locked cursor and a crosshair; the cursor is freed while the shop is open).
    /// The cursor is captured on entering first person and released on leaving it; when something else frees it
    /// (Escape in the editor, another window), a click or the window regaining focus captures it again.
    /// With the phone controls on (<see cref="TouchControls"/>) the views switch by their camera button and in
    /// first person a finger dragging over the empty screen turns the view instead of the mouse.
    /// Follows the local player's hero (<see cref="LocalPlayer"/>); for a hero of the far team the views are mirrored,
    /// so the own side is always at the bottom of the screen. <see cref="Shake"/> jolts the view (a hook's hit, see
    /// <see cref="CameraFeedback"/>, which this camera brings along).
    /// </summary>
    [DefaultExecutionOrder(-10)] // before PlayerInputController: a click that recaptures the cursor is not a cast
    public class TopDownCamera : MonoBehaviour
    {
        public enum View { OwnSide, EnemySide, FirstPerson }

        public Transform target;
        public Vector3 offset = new(0f, 11f, -10f);
        public Vector3 lookAhead = new(0f, 0f, 4.5f);
        public float smoothTime = 0.15f;
        [Tooltip("Enemy-side view: how far behind the enemy side's back wall the camera stands, metres.")]
        public float enemyViewBehind = 5f;

        [Header("First person")]
        [Tooltip("Eye position above the hero's feet, metres.")] public float eyeHeight = 1.9f;
        [Tooltip("Eye position in front of the hero's centre, so the head does not block the view, metres.")]
        public float eyeForward = 0.5f;
        [Tooltip("Initial look down from the horizon, degrees.")] public float firstPersonPitch = 12f;
        public Vector2 pitchLimits = new(-60f, 80f);
        [Tooltip("Degrees per pixel of mouse movement.")] public float mouseSensitivity = 0.12f;
        [Tooltip("Degrees per pixel of a finger turning the view, on a 1080 px high screen (scaled to others).")]
        public float touchSensitivity = 0.18f;
        public float firstPersonFov = 75f;
        [Tooltip("Shown only in first person.")] public GameObject crosshair;

        [Header("Shake")]
        [Tooltip("Sideways and vertical jolt at full shake, metres (half of it in first person).")]
        public float shakeDistance = 0.35f;
        [Tooltip("Roll, pitch and yaw jolt at full shake, degrees.")] public float shakeAngle = 2.2f;
        [Tooltip("How fast the shake wobbles, per second.")] public float shakeFrequency = 18f;
        [Tooltip("Shake lost per second: a full shake dies down in 1 / this seconds.")] public float shakeRecovery = 1.5f;

        public View Mode { get; private set; }
        public bool IsFirstPerson => Mode == View.FirstPerson;
        /// <summary>Mouse look is on: first person with the shop closed (the cursor should be locked).</summary>
        public bool WantsCursorCaptured => IsFirstPerson && !CursorNeeded && !TouchLook;
        /// <summary>The phone controls are on: the view turns with a finger (TouchControls), there is no cursor to lock.</summary>
        static bool TouchLook => TouchControls.Active != null;
        /// <summary>The shop or an overlay window is open: the cursor must be free.</summary>
        bool CursorNeeded => (shop != null && shop.IsOpen) || OverlayInput.Captured;
        /// <summary>The cursor is locked for mouse look.</summary>
        public bool CursorCaptured => Cursor.lockState == CursorLockMode.Locked;
        /// <summary>This frame's left click only recaptured the cursor and must not act in the game.</summary>
        public bool ClickConsumed { get; private set; }
        /// <summary>Shake left, 0..1; the jolt goes with its square, so it starts hard and fades out softly.</summary>
        public float Trauma { get; private set; }

        /// <summary>Horizontal direction WASD's "forward" maps to: the screen's up in the top views, the look
        /// direction in first person.</summary>
        public Vector3 MoveForward => Flat(IsFirstPerson ? Quaternion.Euler(0f, yaw, 0f) * Vector3.forward : transform.forward);

        Camera cam;
        ShopUI shop;
        float baseFov, yaw, pitch;
        bool shopWasOpen;
        Vector3 velocity;
        Vector3 basePosition; // the followed position without the shake
        float shakeTime;

        void Awake()
        {
            cam = GetComponent<Camera>();
            baseFov = cam != null ? cam.fieldOfView : 60f;
            if (crosshair != null) crosshair.SetActive(false);
            basePosition = transform.position;
            if (!TryGetComponent<CameraFeedback>(out _)) gameObject.AddComponent<CameraFeedback>();
        }

        void Start()
        {
            shop = FindAnyObjectByType<ShopUI>();
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

            if (TouchLook)
            {
                if (CursorCaptured) UnlockCursor();
                AddLook(TouchControls.Active.TakeLook() * (touchSensitivity * 1080f / Mathf.Max(Screen.height, 1)));
                if (crosshair != null) crosshair.SetActive(!CursorNeeded);
                return;
            }

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
            basePosition = IsFirstPerson
                ? position
                : Vector3.SmoothDamp(basePosition, position, ref velocity, smoothTime);
            transform.SetPositionAndRotation(basePosition, rotation);
            ApplyShake();
        }

        /// <summary>Adds a jolt, 0..1 (a hit on the own hero about 0.7, the own hook's catch about 0.45); jolts add up.</summary>
        public void Shake(float amount) => Trauma = Mathf.Clamp01(Trauma + amount);

        /// <summary>
        /// Shifts and tilts the view by smooth noise (each axis its own), scaled by the square of the trauma left,
        /// which runs down at a steady rate: a sharp jolt that settles, not a periodic rattle.
        /// </summary>
        void ApplyShake()
        {
            if (Trauma <= 0f) return;
            Trauma = Mathf.Max(0f, Trauma - shakeRecovery * Time.deltaTime);
            shakeTime += Time.deltaTime * shakeFrequency;
            float s = Trauma * Trauma;
            float distance = shakeDistance * (IsFirstPerson ? 0.5f : 1f) * s, angle = shakeAngle * s;
            var offset = new Vector3(ShakeNoise(0), ShakeNoise(1), 0f) * distance;
            transform.SetPositionAndRotation(
                transform.position + transform.rotation * offset,
                transform.rotation * Quaternion.Euler(ShakeNoise(2) * angle * 0.5f, ShakeNoise(3) * angle * 0.5f, ShakeNoise(4) * angle));
        }

        float ShakeNoise(int axis) => Mathf.PerlinNoise(axis * 31.7f, shakeTime) * 2f - 1f;

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
            if (IsFirstPerson && !TouchLook) CaptureCursor();
            else UnlockCursor();
            if (crosshair != null) crosshair.SetActive(IsFirstPerson);
            SnapToTarget();
        }

        public void SnapToTarget()
        {
            if (target == null) return;
            Pose(out var position, out var rotation);
            transform.SetPositionAndRotation(position, rotation);
            basePosition = position;
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
                // behind the enemy side's back wall, looking across the pit at the player: only the sideways
                // position follows the player. Mirroring the own view instead pushed the camera onto the enemy side
                // as the player came up to the pit, leaving the enemies at the back wall under and behind it.
                arm = Quaternion.Euler(0f, 180f, 0f) * arm;
                var arena = Arena.Instance;
                float mid = arena != null ? arena.transform.position.z : 0f;
                float depth = arena != null ? arena.pitHalfDepth + arena.sideDepth : 14f;
                float enemySide = LocalPlayer.Team == Team.Bots ? -1f : 1f;
                position = new Vector3(focus.x + arm.x, focus.y + arm.y, mid + enemySide * (depth + enemyViewBehind));
                // tilted halfway between a hero at the back wall below and the player (aiming at the bodies, a metre
                // up): both ends of the arena stay in the frame
                const float body = 1f;
                float height = arm.y - body;
                float toWall = Mathf.Atan2(height, enemyViewBehind + 0.6f);
                float toPlayer = Mathf.Atan2(height, Mathf.Max(Mathf.Abs(position.z - target.position.z), 0.1f));
                rotation = Quaternion.LookRotation(new Vector3(0f, 0f, -enemySide))
                           * Quaternion.Euler(0.5f * (toWall + toPlayer) * Mathf.Rad2Deg, 0f, 0f);
                return;
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
