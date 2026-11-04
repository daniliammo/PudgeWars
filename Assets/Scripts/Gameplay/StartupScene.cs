using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace PudgeWars.Gameplay
{
    /// <summary>A tiny first scene presents loading UI before the menu and its heavy resources are loaded.</summary>
    public sealed class StartupScene : MonoBehaviour
    {
        public const string SceneName = "Boot";
        public const string MenuScene = "Menu";
        static readonly List<(Action action, string title)> deferred = new();
        static readonly List<(Action action, string title)> visibleDeferred = new();
        StartupLoadingScreen screen;
        public static bool IsActive => SceneManager.GetActiveScene().name == SceneName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDeferredStartup() { deferred.Clear(); visibleDeferred.Clear(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
            Debug.Log($"[Startup] build={Application.buildGUID}, version={Application.version}, " +
                $"platform={Application.platform}, editor={Application.isEditor}, batch={Application.isBatchMode}, " +
                $"dataPath='{Application.dataPath}'");
            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                scenes.Add($"{i}: {SceneUtility.GetScenePathByBuildIndex(i)}");
            Debug.Log("[Startup] Build scenes: " + string.Join("; ", scenes));
        }

        static void OnQuitting() => Debug.Log($"[Startup] Quitting at {Time.realtimeSinceStartup:F2}s, " +
            $"scene='{SceneManager.GetActiveScene().path}', loading={SceneLoading.IsLoading}, " +
            $"graphicsPreparing={StartupGraphicsWarmup.IsPreparing}");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FirstSceneReady() => OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);

        /// <summary>Defer heavy startup services without changing direct Menu/Arena Play Mode entry.</summary>
        public static bool DeferUntilSceneReady(Action action, string title = "Подготовка игровых сервисов")
        {
            if (!IsActive) return false;
            if (!deferred.Exists(item => item.action == action)) deferred.Add((action, title));
            return true;
        }

        /// <summary>Run initial setup only after the lightweight Boot UI has had a chance to render.</summary>
        public static bool DeferUntilVisible(Action action, string title)
        {
            if (!IsActive) return false;
            if (!visibleDeferred.Exists(item => item.action == action)) visibleDeferred.Add((action, title));
            return true;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"[Startup] Scene loaded at {Time.realtimeSinceStartup:F2}s: '{scene.path}', " +
                $"index={scene.buildIndex}, mode={mode}");
            if (scene.name == SceneName)
            {
                if (FindAnyObjectByType<StartupScene>() == null)
                {
                    var go = new GameObject("StartupScene");
                    SceneManager.MoveGameObjectToScene(go, scene);
                    go.AddComponent<StartupScene>();
                }
                return;
            }
            var ready = deferred.ToArray();
            deferred.Clear();
            foreach (var item in ready)
                if (!SceneLoading.DeferPreparation(item.title, item.action)) item.action();
        }

        void Awake()
        {
            Debug.Log($"[Startup] Boot UI creation at {Time.realtimeSinceStartup:F2}s");
            screen = StartupLoadingScreen.Create(transform);
            if (Camera.main == null)
            {
                var cameraObject = new GameObject("StartupCamera");
                cameraObject.transform.SetParent(transform, false);
                cameraObject.tag = "MainCamera";
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.025f, 0.03f, 0.04f);
                camera.cullingMask = 0;
                camera.allowHDR = camera.allowMSAA = false;
                cameraObject.AddComponent<AudioListener>();
            }
        }

        IEnumerator Start()
        {
            yield return screen.Present(0f, "Подготовка ввода");
            if (EventSystem.current == null)
            {
                var events = new GameObject("StartupEvents", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
            // A test/editor transition into Boot can still be finishing its own progress operation.
            while (SceneLoading.IsLoading) yield return null;
            var setup = visibleDeferred.ToArray();
            visibleDeferred.Clear();
            for (int i = 0; i < setup.Length; i++)
            {
                yield return screen.Present(0.1f + 0.6f * i / setup.Length, setup[i].title);
                setup[i].action();
            }
            yield return screen.Present(0.7f, "Подготовка шрифтов интерфейса");
            UiFonts.PrepareCommonGlyphs();
            yield return screen.Present(0.9f, "Подготовка экрана загрузки");
            yield return screen.Complete();
            if (!Application.CanStreamedLevelBeLoaded(MenuScene))
                Debug.LogWarning("[Startup] Menu is missing from the build; falling back to Arena.");
            SceneLoading.Load(Application.CanStreamedLevelBeLoaded(MenuScene) ? MenuScene : "Arena");
            StartupGraphicsWarmup.BeginAutomatically();
            screen.Close();
        }
    }
}
