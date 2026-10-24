using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Two lines marking the edges of the strip the hook will sweep, drawn at the head's flight height (on the
    /// ground they would look shorter than the flight under the tilted camera): from the hero's centre to the
    /// tip of the hook at full range, as wide as the hook head.
    /// </summary>
    public class HookAimIndicator : MonoBehaviour
    {
        public LineRenderer left;
        public LineRenderer right;

        void Awake() => Hide();

        public void Show(Vector3 start, Vector3 direction, float length, float width)
        {
            var side = Vector3.Cross(Vector3.up, direction).normalized * (width / 2f);
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
