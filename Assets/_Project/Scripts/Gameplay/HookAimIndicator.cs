using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Two lines on the ground marking the edges of the strip the hook will sweep: from the launch point
    /// to the full range, as wide as the hook head.
    /// </summary>
    public class HookAimIndicator : MonoBehaviour
    {
        public LineRenderer left;
        public LineRenderer right;
        [Tooltip("Lift above the ground so the lines do not z-fight with it, metres.")]
        public float groundOffset = 0.05f;

        void Awake() => Hide();

        public void Show(Vector3 start, Vector3 direction, float length, float width)
        {
            var side = Vector3.Cross(Vector3.up, direction).normalized * (width / 2f);
            start.y = groundOffset;
            Set(left, start - side, direction, length);
            Set(right, start + side, direction, length);
        }

        public void Hide()
        {
            if (left != null) left.enabled = false;
            if (right != null) right.enabled = false;
        }

        static void Set(LineRenderer line, Vector3 from, Vector3 direction, float length)
        {
            if (line == null) return;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, from + direction * length);
            line.enabled = true;
        }
    }
}
