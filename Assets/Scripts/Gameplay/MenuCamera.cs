using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>The main menu's camera drifts slowly around where it stands, always looking at the same point.</summary>
    public class MenuCamera : MonoBehaviour
    {
        public Vector3 lookAt = new(0f, 1.2f, 0f);
        [Tooltip("How far it drifts sideways and up, metres.")]
        public Vector2 sway = new(0.35f, 0.12f);
        [Tooltip("Sway speed, radians per second (sideways, up).")]
        public Vector2 speed = new(0.13f, 0.21f);

        Vector3 home, right;

        void Start()
        {
            home = transform.position;
            right = transform.right; // fixed: the turning camera's own right would make the sway wander
        }

        void LateUpdate()
        {
            float t = Time.time;
            transform.position = home + right * (Mathf.Sin(t * speed.x) * sway.x) + Vector3.up * (Mathf.Sin(t * speed.y) * sway.y);
            transform.LookAt(lookAt);
        }
    }
}
