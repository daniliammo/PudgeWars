using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// A round obstacle on the ground (a tree's trunk): heroes walk around it as around each other, while a hook
    /// flies through it and a caught hero is dragged through it, as Meat Hook in Dota.
    /// </summary>
    public class Obstacle : MonoBehaviour
    {
        static readonly List<Obstacle> all = new();
        public static IReadOnlyList<Obstacle> All => all;

        [Tooltip("Radius heroes cannot walk into, metres (from the obstacle's position, on the ground plane).")]
        public float radius = 0.5f;

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        /// <summary>True if a hero of <paramref name="heroRadius"/> standing at <paramref name="p"/> would overlap an obstacle.</summary>
        public static bool Blocks(Vector3 p, float heroRadius)
        {
            foreach (var o in all)
            {
                var d = o.transform.position - p;
                d.y = 0f;
                float r = o.radius + heroRadius;
                if (d.sqrMagnitude < r * r) return true;
            }
            return false;
        }
    }
}
