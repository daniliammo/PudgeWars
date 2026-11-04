using System;
using System.Collections.Generic;
using System.Linq;
using PudgeWars.Gameplay;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PudgeWars.EditorTools
{
    /// <summary>Export render references without rebuilding scenes or entering their gameplay lifecycle.</summary>
    public sealed class GraphicsWarmupLibraryBuilder : IPreprocessBuildWithReport
    {
        const string AssetPath = "Assets/Resources/GraphicsWarmupLibrary.asset";
        static readonly string[] ScenePaths = { "Assets/Scenes/Menu.unity", "Assets/Scenes/Arena.unity" };
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!ServerSceneProcessor.IsServerBuild(report)) Build();
        }

        [MenuItem("Pudge Wars/Build Graphics Warmup Library")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build the graphics library outside Play Mode.");
            var library = AssetDatabase.LoadAssetAtPath<GraphicsWarmupLibrary>(AssetPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<GraphicsWarmupLibrary>();
                AssetDatabase.CreateAsset(library, AssetPath);
            }
            var dependencies = AssetDatabase.GetDependencies(ScenePaths.Concat(new[] { "Assets/Resources/NetPrefabs.asset" }).ToArray(), true);
            library.scenes = new[] { "Menu", "Arena" };
            library.models = dependencies.Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal).Select(AssetDatabase.LoadAssetAtPath<GameObject>).ToArray();
            library.materials = dependencies.Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal).Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
            var terrains = new HashSet<TerrainData>();
            var meshes = new List<GraphicsWarmupLibrary.MeshSample>();
            foreach (var path in ScenePaths)
            {
                var preview = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    foreach (var root in preview.GetRootGameObjects())
                    {
                        foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
                        {
                            if (terrain.terrainData != null) terrains.Add(terrain.terrainData);
                            library.terrainMaterial = terrain.materialTemplate;
                        }
                        foreach (var landscape in root.GetComponentsInChildren<MenuLandscape>(true))
                        {
                            library.radiant = landscape.radiant;
                            library.dire = landscape.dire;
                            library.cliff = landscape.cliff;
                        }
                        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            var filter = renderer.GetComponent<MeshFilter>();
                            if (filter != null && filter.sharedMesh != null)
                                meshes.Add(new GraphicsWarmupLibrary.MeshSample { mesh = filter.sharedMesh, materials = renderer.sharedMaterials });
                        }
                    }
                }
                finally { EditorSceneManager.ClosePreviewScene(preview); }
            }
            library.terrains = terrains.OrderBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal).ToArray();
            library.meshes = meshes.ToArray();
            library.Validate();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GraphicsWarmupLibrary] Menu + Arena: {library.models.Length} models, " +
                $"{library.materials.Length} materials, {library.terrains.Length} terrains, both menu sides.");
        }
    }
}
