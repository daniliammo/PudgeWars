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
        static readonly List<Action> deferred = new();
        public static bool IsActive => SceneManager.GetActiveScene().name == SceneName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDeferredStartup() => deferred.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FirstSceneReady() => OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);

        /// <summary>Defer heavy startup services without changing direct Menu/Arena Play Mode entry.</summary>
        public static bool DeferUntilSceneReady(Action action)
        {
            if (!IsActive) return false;
            if (!deferred.Contains(action)) deferred.Add(action);
            return true;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
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
            foreach (var action in ready) action();
        }

        IEnumerator Start()
        {
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
            if (EventSystem.current == null)
            {
                var events = new GameObject("StartupEvents", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
            // A test/editor transition into Boot can still be finishing its own progress operation.
            while (SceneLoading.IsLoading) yield return null;
            SceneLoading.Load(Application.CanStreamedLevelBeLoaded(MenuScene) ? MenuScene : "Arena");
        }
    }
}
