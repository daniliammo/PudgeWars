using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Drives the imported Animator Controller, whose states are named after the original clips.
    /// </summary>
    public class PudgeAnimation : MonoBehaviour
    {
        public const string Idle = "Idle";
        public const string Walk = "Walk";
        public const string Run = "Pudge_Run";
        public const string HookStart = "StartHookPudge";
        public const string HookEnd = "EndHookPudge";
        public const string Hooked = "PopadaniePudge";
        public const string Death = "Death_HookPudge_pudge1_End_HookPudge";
        public const string Spawn = "Spawn_1";

        [Tooltip("Above this speed the run cycle is used instead of the walk cycle, m/s.")]
        public float runThreshold = 2.4f;

        Animator animator;
        string current;
        float lockedUntil;

        void Awake() => animator = GetComponentInChildren<Animator>();

        public void Play(string state, float fade = 0.12f)
        {
            if (animator == null || state == current) return;
            animator.CrossFadeInFixedTime(state, fade);
            current = state;
            lockedUntil = 0f;
        }

        /// <summary>Plays a one-shot clip that locomotion will not override while standing still.</summary>
        public void PlayAction(string state, float lockSeconds, float fade = 0.1f)
        {
            if (animator == null) return;
            animator.CrossFadeInFixedTime(state, fade, 0, 0f);
            current = state;
            lockedUntil = Time.time + lockSeconds;
        }

        public void Locomotion(float speed)
        {
            bool moving = speed > 0.05f;
            if (!moving && Time.time < lockedUntil) return;
            Play(!moving ? Idle : speed > runThreshold ? Run : Walk);
        }
    }
}
