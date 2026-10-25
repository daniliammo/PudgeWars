using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Pudge in the middle of the main menu: appears with the spawn clip, then stands idle and taunts now and then.
    /// Plays the imported controller's states directly, without the gameplay components.
    /// </summary>
    public class MenuPudge : MonoBehaviour
    {
        [Tooltip("Random time between taunts, seconds.")]
        public Vector2 tauntInterval = new(8f, 14f);

        Animator animator;
        string playing;
        float nextTauntAt;

        void Start()
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.applyRootMotion = false; // stays on its spot
            Play(PudgeAnimation.Spawn, 0f);
            nextTauntAt = Time.time + Random.Range(tauntInterval.x, tauntInterval.y);
        }

        void Update()
        {
            if (animator == null) return;
            if (playing != PudgeAnimation.Idle)
            {
                // a one-shot clip: back to idle near its end
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (!animator.IsInTransition(0) && state.IsName(playing) && state.normalizedTime >= 0.95f)
                    Play(PudgeAnimation.Idle, 0.25f);
            }
            else if (Time.time >= nextTauntAt)
            {
                Play(PudgeAnimation.Taunt, 0.2f);
                nextTauntAt = Time.time + Random.Range(tauntInterval.x, tauntInterval.y);
            }
        }

        void Play(string state, float fade)
        {
            animator.CrossFadeInFixedTime(state, fade, 0, 0f);
            playing = state;
        }
    }
}
