using System;
using System.Collections;
using System.Collections.Generic;
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
        readonly List<Stack<IEnumerator>> routines = new();
        readonly Queue<(string title, Func<Action<float, string>, IEnumerator> run, float weight)> preparations = new();
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
            Instance.StartCoroutine(Instance.Run(Instance.LoadLocal(scene)));
        }

        public static void Observe(string scene, Func<AsyncOperation> operation, Func<bool> busy) =>
            Instance.StartCoroutine(Instance.Run(Instance.TrackExternal(scene, operation, busy)));

        // Dispose the entire iterator chain if a preparation throws or the loader is stopped.
        // This keeps input and progress ownership from being stranded by a nested coroutine failure.
        IEnumerator Run(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            routines.Add(stack);
            try
            {
                while (stack.Count > 0)
                {
                    var step = stack.Peek();
                    if (!step.MoveNext())
                    {
                        stack.Pop();
                        (step as IDisposable)?.Dispose();
                    }
                    else if (step.Current is IEnumerator nested) stack.Push(nested);
                    else yield return step.Current;
                }
            }
            finally
            {
                routines.Remove(stack);
                Dispose(stack);
            }
        }

        /// <summary>Scene Awake/Start callbacks can join the current load instead of blocking activation.</summary>
        public static bool DeferPreparation(string title, Func<Action<float, string>, IEnumerator> run, float weight = 1f)
        {
            if (!IsLoading) return false;
            instance.preparations.Enqueue((title, run, Mathf.Max(0.01f, weight)));
            return true;
        }

        public static bool DeferPreparation(string title, Action run) =>
            DeferPreparation(title, _ => RunAction(run));

        static IEnumerator RunAction(Action run) { run(); yield break; }

        IEnumerator PrepareScene(LoadingProgress.Operation progress)
        {
            yield return progress.Present(0.45f, "Запуск объектов сцены");
            float total = 0f, completed = 0f;
            foreach (var item in preparations) total += item.weight;
            while (preparations.Count > 0)
            {
                var item = preparations.Dequeue();
                float from = completed;
                void Report(float value, string description) => progress.Report(
                    Mathf.Lerp(0.45f, 0.97f, (from + Mathf.Clamp01(value) * item.weight) / total), description);
                Report(0f, item.title);
                yield return progress.Present(progress.Progress, item.title);
                yield return item.run(Report);
                completed += item.weight;
                Report(1f, item.title);
                // A service may register additional work during its own startup.
                total = completed;
                foreach (var remaining in preparations) total += remaining.weight;
            }
            yield return progress.Present(0.98f, "Подготовка первого кадра");
            yield return progress.Complete();
        }

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
            AsyncOperation operation = null;
            try
            {
                yield return progress.Present(0f, "Загрузка ресурсов сцены");
                operation = SceneManager.LoadSceneAsync(scene);
                if (operation == null) throw new InvalidOperationException($"Could not load scene: {scene}");
                operation.allowSceneActivation = false;
                while (operation.progress < 0.9f)
                {
                    progress.Report(operation.progress / 0.9f * 0.4f, "Загрузка ресурсов сцены");
                    yield return null;
                }
                yield return progress.Present(0.4f, "Открытие сцены");
                operation.allowSceneActivation = true;
                while (!operation.isDone) yield return null;
                yield return PrepareScene(progress);
            }
            finally
            {
                if (operation != null && !operation.isDone) operation.allowSceneActivation = true;
                preparations.Clear();
                active--;
            }
        }

        IEnumerator TrackExternal(string scene, Func<AsyncOperation> source, Func<bool> busy)
        {
            using var progress = LoadingProgress.Begin(Title(scene));
            active++;
            try
            {
                // Mirror invokes its scene callbacks before creating the AsyncOperation.
                progress.Report(0f, "Ожидание перехода сцены");
                yield return null;
                while (true)
                {
                    var operation = source();
                    if (!busy() && (operation == null || operation.isDone)) break;
                    float value = operation != null ? Mathf.Min(operation.progress / 0.9f, 1f) : 0f;
                    progress.Report(value * 0.4f, value >= 1f ? "Открытие сцены" : "Загрузка ресурсов сцены");
                    yield return null;
                }
                yield return PrepareScene(progress);
            }
            finally { preparations.Clear(); active--; }
        }

        static void Dispose(Stack<IEnumerator> stack)
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            StopAllCoroutines();
            // Unity can stop a coroutine on destruction without disposing its nested iterators.
            // Release scene activation and every progress operation even in that case.
            foreach (var stack in routines.ToArray()) Dispose(stack);
            routines.Clear();
        }
    }
}
