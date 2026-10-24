using UnityEngine;
using UnityEngine.InputSystem;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Player camera with views switched by V: from above behind the player's side looking slightly ahead
    /// toward the enemy, the same view from the enemy side (mirrored across the pit), and first person.
    /// </summary>
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
        [Tooltip("Looking down from the horizon, degrees.")] public float firstPersonPitch = 12f;
        public float firstPersonFov = 75f;

        public View Mode { get; private set; }

        /// <summary>Horizontal direction WASD's "forward" maps to: the screen's up in the top views, toward the
        /// enemy side in first person (relative to the hero's own facing, turning would never stop).</summary>
        public Vector3 MoveForward => Flat(Mode == View.FirstPerson ? lookAhead : transform.forward);

        Camera cam;
        float baseFov;
        Vector3 velocity;

        void Awake()
        {
            cam = GetComponent<Camera>();
            baseFov = cam != null ? cam.fieldOfView : 60f;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.vKey.wasPressedThisFrame) NextView();
        }

        void LateUpdate()
        {
            if (target == null) return;
            Pose(out var position, out var rotation);
            transform.position = Mode == View.FirstPerson
                ? position
                : Vector3.SmoothDamp(transform.position, position, ref velocity, smoothTime);
            transform.rotation = rotation;
        }

        public void NextView() => SetView((View)(((int)Mode + 1) % 3));

        public void SetView(View view)
        {
            Mode = view;
            if (cam != null) cam.fieldOfView = view == View.FirstPerson ? firstPersonFov : baseFov;
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
            if (Mode == View.FirstPerson)
            {
                var forward = Flat(target.forward);
                position = target.position + Vector3.up * eyeHeight + forward * eyeForward;
                rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(firstPersonPitch, 0f, 0f);
                return;
            }
            var focus = target.position + lookAhead;
            var arm = offset;
            if (Mode == View.EnemySide)
            {
                // the own view mirrored across the middle of the pit: the player is where the enemies are
                // in the own view, the enemies are in front of the camera
                float mid = Arena.Instance != null ? Arena.Instance.transform.position.z : 0f;
                focus.z = 2f * mid - focus.z;
                arm = Quaternion.Euler(0f, 180f, 0f) * offset;
            }
            position = focus + arm;
            rotation = Quaternion.LookRotation(-arm);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }
    }
}
