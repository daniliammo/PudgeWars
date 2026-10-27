using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>Finds the hero under a screen point: the one whose figure (feet to head) is nearest to it.</summary>
    public static class HeroPicker
    {
        /// <summary>A click this close to a hero's figure on screen picks it, pixels at 1080p.</summary>
        public const float PickRadius = 45f;
        /// <summary>Height of the hero's figure used for picking, metres.</summary>
        public const float FigureHeight = 2.2f;

        /// <summary>The living hero nearest to <paramref name="screen"/> within the pick radius, or null.</summary>
        public static Pudge PickAt(Vector2 screen, Camera cam)
        {
            if (cam == null) return null;
            Pudge best = null;
            float bestDist = PickRadius * Screen.height / 1080f;
            foreach (var hero in Pudge.All)
            {
                if (!hero.IsAlive) continue;
                var feet = cam.WorldToScreenPoint(hero.transform.position);
                var head = cam.WorldToScreenPoint(hero.transform.position + Vector3.up * FigureHeight);
                if (feet.z <= 0f || head.z <= 0f) continue;
                float d = DistanceToSegment(screen, feet, head);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = hero;
                }
            }
            return best;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
