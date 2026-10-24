using UnityEngine;
using UnityEngine.InputSystem;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Desktop controls for the test arena: WASD / arrows to move, left mouse button or Space to throw
    /// the hook toward the cursor.
    /// </summary>
    public class PlayerInputController : MonoBehaviour
    {
        public Camera viewCamera;

        PudgeMotor motor;
        HookAbility hook;

        void Awake()
        {
            motor = GetComponent<PudgeMotor>();
            hook = GetComponent<HookAbility>();
            if (viewCamera == null) viewCamera = Camera.main;
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

            bool cast = (mouse != null && mouse.leftButton.wasPressedThisFrame) || (kb != null && kb.spaceKey.wasPressedThisFrame);
            if (cast && mouse != null && TryGetGroundPoint(mouse.position.ReadValue(), out var point))
                hook.TryCast(point);
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
