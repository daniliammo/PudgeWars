using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PudgeWars.EditorTools
{
    /// <summary>Build committed scenes as a game player or dedicated server, without scene regeneration.</summary>
    public static class CiBuild
    {
        public static void Build()
        {
            BuildPlayer(false);
        }

        public static void BuildLinuxServer()
        {
            BuildPlayer(true);
        }

        static void BuildPlayer(bool server)
        {
            string output = Argument("-ciOutputPath");
            if (string.IsNullOrWhiteSpace(output)) throw new BuildFailedException("Missing -ciOutputPath.");
            var target = EditorUserBuildSettings.activeBuildTarget;
            if (server && target != BuildTarget.StandaloneLinux64)
                throw new BuildFailedException("Linux server requires -buildTarget Linux64 -standaloneBuildSubtarget Server.");
            if (target != BuildTarget.Android && target != BuildTarget.StandaloneLinux64 && target != BuildTarget.StandaloneOSX)
                throw new BuildFailedException($"Unsupported CI target: {target}");
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                throw new BuildFailedException($"Install the Unity module for {target}.");

            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0 || scenes[0] != StartupSceneBuilder.ScenePath ||
                !scenes.Contains("Assets/Scenes/Menu.unity") || !scenes.Contains("Assets/Scenes/Arena.unity"))
                throw new BuildFailedException("CI requires enabled Boot → Menu → Arena scenes in Build Settings.");
            foreach (string scene in scenes)
                if (!File.Exists(scene)) throw new BuildFailedException($"Scene not found: {scene}");
            if (server) scenes = new[] { StartupSceneBuilder.ScenePath, "Assets/Scenes/Arena.unity" };

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            // Preserve the project's Mono backend, architectures, quality and committed assets.
            // Select the subtarget explicitly instead of inheriting a saved editor profile.
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                subtarget = (int)(server ? StandaloneBuildSubtarget.Server : StandaloneBuildSubtarget.Player),
                options = BuildOptions.CompressWithLz4HC,
            };
            if (target == BuildTarget.Android)
            {
                options.subtarget = 0;
                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            }
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[CiBuild] {target} {(server ? "Server" : "Player")}: {report.summary.result}, " +
                $"{report.summary.totalSize} bytes, {report.summary.totalTime}");
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"{target} build failed with {report.summary.totalErrors} errors.");
        }

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
