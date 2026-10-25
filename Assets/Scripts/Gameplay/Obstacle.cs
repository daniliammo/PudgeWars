using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>A round obstacle on the ground plane: its centre (the height is ignored) and radius, metres.</summary>
    public readonly struct ObstacleCircle
    {
        public readonly Vector3 center;
        public readonly float radius;

        public ObstacleCircle(Vector3 center, float radius)
        {
            this.center = center;
            this.radius = radius;
        }
    }

    /// <summary>
    /// A round obstacle on the ground (a tree's trunk): heroes walk around it as around each other, while a hook
    /// flies through it and a caught hero is dragged through it, as Meat Hook in Dota. Besides these components,
    /// whole sets of obstacles are registered by their owners (the terrain's trees, <see cref="TerrainTreeObstacles"/>);
    /// <see cref="All"/> holds them all. Obstacles do not move.
    /// </summary>
    public class Obstacle : MonoBehaviour
    {
        static readonly Dictionary<Object, ObstacleCircle[]> sources = new();
        static readonly List<ObstacleCircle> all = new();
        public static IReadOnlyList<ObstacleCircle> All => all;

        [Tooltip("Radius heroes cannot walk into, metres (from the obstacle's position, on the ground plane).")]
        public float radius = 0.5f;

        void OnEnable() => Register(this, new[] { new ObstacleCircle(transform.position, radius) });
        void OnDisable() => Unregister(this);

        /// <summary>Sets the obstacles of <paramref name="owner"/>, replacing the ones it had.</summary>
        public static void Register(Object owner, ObstacleCircle[] circles)
        {
            sources[owner] = circles;
            Rebuild();
        }

        public static void Unregister(Object owner)
        {
            if (sources.Remove(owner)) Rebuild();
        }

        static void Rebuild()
        {
            all.Clear();
            foreach (var circles in sources.Values) all.AddRange(circles);
        }

        /// <summary>True if a hero of <paramref name="heroRadius"/> standing at <paramref name="p"/> would overlap an obstacle.</summary>
        public static bool Blocks(Vector3 p, float heroRadius)
        {
            foreach (var o in all)
            {
                var d = o.center - p;
                d.y = 0f;
                float r = o.radius + heroRadius;
                if (d.sqrMagnitude < r * r) return true;
            }
            return false;
        }
    }
}
