using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>Follows the player from above at a fixed angle, looking slightly ahead toward the enemy side.</summary>
    public class TopDownCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new(0f, 15f, -10f);
        public Vector3 lookAhead = new(0f, 0f, 6.5f);
        public float smoothTime = 0.15f;

        Vector3 velocity;

        void LateUpdate()
        {
            if (target == null) return;
            var focus = target.position + lookAhead;
            transform.position = Vector3.SmoothDamp(transform.position, focus + offset, ref velocity, smoothTime);
            transform.rotation = Quaternion.LookRotation(-offset);
        }

        public void SnapToTarget()
        {
            if (target == null) return;
            transform.position = target.position + lookAhead + offset;
            transform.rotation = Quaternion.LookRotation(-offset);
        }
    }
}
