using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The trees of this terrain (its tree instances, drawn by the terrain itself) are <see cref="Obstacle"/>s:
    /// heroes walk around their trunks, hooks fly through them.
    /// </summary>
    [RequireComponent(typeof(Terrain))]
    public class TerrainTreeObstacles : MonoBehaviour
    {
        [Tooltip("Trunk radius heroes cannot walk into, metres, whatever the tree's scale.")]
        public float radius = 0.5f;

        void OnEnable()
        {
            var terrain = GetComponent<Terrain>();
            var data = terrain.terrainData;
            if (data == null) return;
            var trees = data.treeInstances;
            var circles = new ObstacleCircle[trees.Length];
            var origin = terrain.transform.position;
            for (int i = 0; i < trees.Length; i++)
                circles[i] = new ObstacleCircle(origin + Vector3.Scale(trees[i].position, data.size), radius);
            Obstacle.Register(this, circles);
        }

        void OnDisable() => Obstacle.Unregister(this);
    }
}
