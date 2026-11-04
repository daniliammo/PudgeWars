using System;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>Build-time references keep both scenes' render resources resident across scene changes.</summary>
    public sealed class GraphicsWarmupLibrary : ScriptableObject
    {
        public const string ResourcePath = "GraphicsWarmupLibrary";

        [Serializable]
        public sealed class MeshSample
        {
            public Mesh mesh;
            public Material[] materials = Array.Empty<Material>();
        }

        public string[] scenes = Array.Empty<string>();
        public GameObject[] models = Array.Empty<GameObject>();
        public MeshSample[] meshes = Array.Empty<MeshSample>();
        public Material[] materials = Array.Empty<Material>();
        public TerrainData[] terrains = Array.Empty<TerrainData>();
        public Material terrainMaterial;
        public TerrainLayer cliff;
        public MenuLandscape.Look radiant = new(), dire = new();

        public void Validate()
        {
            if (Array.IndexOf(scenes, "Menu") < 0 || Array.IndexOf(scenes, "Arena") < 0 ||
                models.Length == 0 || terrains.Length == 0 || terrainMaterial == null ||
                radiant.ground == null || dire.ground == null || radiant.sky == null || dire.sky == null)
                throw new InvalidOperationException("Incomplete graphics library. Run Pudge Wars → Build Graphics Warmup Library.");
        }
    }
}
