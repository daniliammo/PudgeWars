using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PudgeWars.EditorTools
{
    /// <summary>Build the minimal first scene; StartupScene creates its camera/UI and loads the menu at runtime.</summary>
    public static class StartupSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Boot.unity";

        [MenuItem("Pudge Wars/Build Startup Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientLight = Color.black;
            EditorSceneManager.SaveScene(scene, ScenePath);
            ArenaSceneBuilder.AddToBuildSettings(ScenePath, first: true);
            Debug.Log("[StartupSceneBuilder] saved " + ScenePath);
        }
    }
}
