using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>A short, unscaled fade from transparent to opaque whenever a hover description opens.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class TooltipFade : MonoBehaviour
    {
        public const float Duration = 0.14f;
        CanvasGroup group;
        float elapsed;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
        }

        void OnEnable() => Restart();

        public void Restart()
        {
            if (group == null) group = GetComponent<CanvasGroup>();
            elapsed = 0f;
            group.alpha = 0f;
        }

        void Update()
        {
            if (elapsed >= Duration) return;
            elapsed = Mathf.Min(Duration, elapsed + Time.unscaledDeltaTime);
            group.alpha = Mathf.SmoothStep(0f, 1f, elapsed / Duration);
        }
    }
}
