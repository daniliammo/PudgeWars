using PudgeWars.Gameplay;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace PudgeWars.EditorTools
{
    /// <summary>Strip presentation and offline heroes from build copies, preserving terrain and gameplay physics.</summary>
    public sealed class ServerSceneProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public static bool IsServerBuild(BuildReport report) => report != null &&
            BuildPipeline.GetBuildTargetGroup(report.summary.platform) == BuildTargetGroup.Standalone &&
            report.summary.GetSubtarget<StandaloneBuildSubtarget>() == StandaloneBuildSubtarget.Server;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (!IsServerBuild(report)) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                RemoveObjects<Pudge>(root);
                if (root == null) continue;
                RemoveObjects<Canvas>(root);
                if (root == null) continue;
                RemoveObjects<EventSystem>(root);
                if (root == null) continue;
                RemoveObjects<Camera>(root);
                if (root == null) continue;
                RemoveObjects<Light>(root);
                if (root == null) continue;
                foreach (var cycle in root.GetComponentsInChildren<DayNightCycle>(true))
                    Object.DestroyImmediate(cycle);
            }
            Debug.Log($"[ServerBuild] Removed presentation and offline heroes from {scene.path}.");
        }

        static void RemoveObjects<T>(GameObject root) where T : Component
        {
            foreach (var component in root.GetComponentsInChildren<T>(true))
                if (component != null) Object.DestroyImmediate(component.gameObject);
        }
    }
}
