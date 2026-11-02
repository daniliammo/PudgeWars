using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PudgeWars.Gameplay
{
    /// <summary>Async local scene loads and observation of Mirror's own operations without taking over its lifecycle.</summary>
    public sealed class SceneLoading : MonoBehaviour
    {
        static SceneLoading instance;
        int active;
        public static bool IsLoading => instance != null && instance.active > 0;

        static SceneLoading Instance
        {
            get
            {
                if (instance != null) return instance;
                var go = new GameObject("SceneLoading");
                DontDestroyOnLoad(go);
                return instance = go.AddComponent<SceneLoading>();
            }
        }

        public static void Load(string scene)
        {
            if (!Application.CanStreamedLevelBeLoaded(scene)) throw new ArgumentException($"Scene is not in the build: {scene}");
            Instance.StartCoroutine(Instance.LoadLocal(scene));
        }

        public static void Observe(string scene, Func<AsyncOperation> operation, Func<bool> busy) =>
            Instance.StartCoroutine(Instance.TrackExternal(scene, operation, busy));

        static string Title(string scene) => Path.GetFileNameWithoutExtension(scene) switch
        {
            "Arena" => "Загрузка арены",
            "Menu" => "Загрузка главного меню",
            _ => "Загрузка сцены",
        };

        IEnumerator LoadLocal(string scene)
        {
            using var progress = LoadingProgress.Begin(Title(scene));
            active++;
            try
            {
                yield return null; // Present the bar before starting any loading work.
                var operation = SceneManager.LoadSceneAsync(scene);
                if (operation == null) throw new InvalidOperationException($"Could not load scene: {scene}");
                while (!operation.isDone)
                {
                    progress.Report(Mathf.Min(operation.progress, 0.9f), "Загрузка ресурсов сцены");
                    yield return null;
                }
                progress.Report(0.95f, "Подготовка сцены");
                yield return null; // New scene Start methods run before completion is displayed.
                progress.Report(1f, "Готово");
                yield return null;
            }
            finally { active--; }
        }

        IEnumerator TrackExternal(string scene, Func<AsyncOperation> source, Func<bool> busy)
        {
            using var progress = LoadingProgress.Begin(Title(scene));
            active++;
            try
            {
                // Mirror invokes its scene callbacks before creating the AsyncOperation.
                yield return null;
                while (true)
                {
                    var operation = source();
                    if (!busy() && (operation == null || operation.isDone)) break;
                    progress.Report(operation != null ? Mathf.Min(operation.progress, 0.9f) : 0f, "Загрузка ресурсов сцены");
                    yield return null;
                }
                progress.Report(0.95f, "Подготовка сцены");
                yield return null;
                progress.Report(1f, "Готово");
                yield return null;
            }
            finally { active--; }
        }

        void OnDestroy() { if (instance == this) instance = null; }
    }
}
