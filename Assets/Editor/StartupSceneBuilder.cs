using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using PudgeWars.Gameplay;

namespace PudgeWars.EditorTools
{
    /// <summary>Save Boot's startup component; its Awake creates a lightweight UI before preparing game resources.</summary>
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
            new GameObject("StartupScene", typeof(StartupScene));
            ConfigureSplash();
            EditorSceneManager.SaveScene(scene, ScenePath);
            ArenaSceneBuilder.AddToBuildSettings(ScenePath, first: true);
            Debug.Log("[StartupSceneBuilder] saved " + ScenePath);
        }

        internal static void ConfigureSplash()
        {
            var background = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Startup/EngineStartup.png");
            if (background == null) throw new BuildFailedException("Import Assets/Art/UI/Startup/EngineStartup.png before building.");
            PlayerSettings.SplashScreen.background = background;
            PlayerSettings.SplashScreen.backgroundColor = new Color(0.025f, 0.03f, 0.04f);
            PlayerSettings.SplashScreen.blurBackgroundImage = false;
            PlayerSettings.SplashScreen.overlayOpacity = 0f;
            PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Static;
        }
    }

    // Apply through the API too: an open editor may still hold the previous PlayerSettings in memory.
    public sealed class StartupSplashBuildSettings : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report) => StartupSceneBuilder.ConfigureSplash();
    }
}
