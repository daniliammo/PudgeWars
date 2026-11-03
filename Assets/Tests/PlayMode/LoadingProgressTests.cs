using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PudgeWars.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Text = PudgeWars.Gameplay.UiText;

namespace PudgeWars.Tests
{
    public class LoadingProgressTests
    {
        [UnityTest]
        public IEnumerator ProgressBarsAreIndependentMonotonicAndBlockInput()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("UI needs a GPU.");
            bool captured = OverlayInput.Captured;
            OverlayInput.Captured = false;
            var graphics = LoadingProgress.Begin("Прогрев шейдеров");
            var scene = LoadingProgress.Begin("Загрузка арены");
            try
            {
                graphics.Report(0.42f, "Прогрев эффектов");
                scene.Report(0.7f);
                graphics.Report(0.1f);
                graphics.Report(float.NaN);
                Assert.AreEqual(0.42f, graphics.Progress);
                Assert.AreEqual(0f, graphics.DisplayedProgress, "Reporting a target must not jump the displayed bar.");
                Assert.IsTrue(OverlayInput.Captured);
                var bars = Object.FindObjectsByType<Slider>().Where(s => s.name == "ProgressBar").ToArray();
                Assert.AreEqual(2, bars.Length);
                Assert.IsTrue(bars.All(s => !s.interactable), "Loading progress is read-only.");
                float deadline = Time.realtimeSinceStartup + 5f;
                float previous = 0f;
                while (scene.DisplayedProgress < 0.7f && Time.realtimeSinceStartup < deadline)
                {
                    Assert.That(scene.DisplayedProgress, Is.InRange(previous, scene.Progress));
                    previous = scene.DisplayedProgress;
                    yield return null;
                }
                Assert.IsTrue(bars.Any(s => Mathf.Approximately(s.value, 0.42f)));
                Assert.IsTrue(bars.Any(s => Mathf.Approximately(s.value, 0.7f)));
                Assert.IsTrue(Object.FindObjectsByType<Text>().Any(t => t.name == "Percent" && t.text == "42 %"));
                graphics.Dispose();
                Assert.IsTrue(LoadingProgress.IsVisible, "One operation cannot hide another operation's progress.");
                Assert.IsTrue(OverlayInput.Captured);
                yield return scene.Complete();
                Assert.IsTrue(Object.FindObjectsByType<Text>().Any(t => t.name == "Percent" && t.text == "100 %"));
            }
            finally { graphics.Dispose(); scene.Dispose(); OverlayInput.Captured = captured; }
            Assert.IsFalse(LoadingProgress.IsVisible);
        }

        [UnityTest]
        public IEnumerator SmoothingAndCompletionWorkWhileTimeIsPaused()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("UI needs a GPU.");
            float scale = Time.timeScale;
            using var operation = LoadingProgress.Begin("Проверка загрузки");
            try
            {
                Time.timeScale = 0f;
                operation.Report(1f);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (operation.DisplayedProgress < 1f && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(1f, operation.DisplayedProgress, "Loading must animate independently of game time.");
                yield return operation.Complete();
                Assert.IsTrue(LoadingProgress.IsVisible, "Completion stays visible until the owner closes it.");
            }
            finally { Time.timeScale = scale; }
        }

        [UnityTest]
        public IEnumerator PreparationFailureReleasesTheOverlayAndInput()
        {
            if (!Application.CanStreamedLevelBeLoaded("Menu"))
                Assert.Ignore("Build the menu: Pudge Wars → Build Main Menu.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("UI needs a GPU.");
            void Observe(Scene scene, LoadSceneMode mode)
            {
                if (scene.name == "Menu")
                    SceneLoading.DeferPreparation("Проверка ошибки", () =>
                        throw new System.InvalidOperationException("Loading preparation failure"));
            }
            SceneManager.sceneLoaded += Observe;
            try
            {
                LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Loading preparation failure"));
                SceneLoading.Load("Menu");
                float deadline = Time.realtimeSinceStartup + 30f;
                while (SceneLoading.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsFalse(SceneLoading.IsLoading);
                Assert.IsFalse(LoadingProgress.IsVisible, "A nested preparation failure must close its operation.");
                Assert.IsFalse(OverlayInput.Captured, "A failed preparation must not strand the input blocker.");
            }
            finally { SceneManager.sceneLoaded -= Observe; }
        }

        [UnityTest]
        public IEnumerator StartupSceneLoadsMenuWithAVisibleProgressBar()
        {
            if (!Application.CanStreamedLevelBeLoaded(StartupScene.SceneName) || !Application.CanStreamedLevelBeLoaded("Menu"))
                Assert.Ignore("Build startup and menu: Pudge Wars → Build Startup Scene / Build Main Menu.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Loading UI needs a GPU.");
            bool deferred = false, invoked = false, sawBar = false;
            bool earlyDeferred = false, earlyInvoked = false, sawEarlyStage = false;
            void Observe(Scene scene, LoadSceneMode mode)
            {
                if (scene.name == StartupScene.SceneName)
                {
                    deferred = StartupScene.DeferUntilSceneReady(() => invoked = true);
                    earlyDeferred = StartupScene.DeferUntilVisible(() =>
                    {
                        Assert.IsTrue(sawEarlyStage, "Present Boot's lightweight screen before initial setup.");
                        Assert.IsTrue(StartupLoadingScreen.IsVisible);
                        Assert.IsTrue(OverlayInput.Captured);
                        Assert.IsFalse(SceneLoading.IsLoading, "Initial setup must run before loading Menu.");
                        earlyInvoked = true;
                    }, "Проверка раннего запуска");
                }
            }
            SceneManager.sceneLoaded += Observe;
            try
            {
                var boot = SceneManager.LoadSceneAsync(StartupScene.SceneName);
                float deadline = Time.realtimeSinceStartup + 30f;
                while ((!boot.isDone || StartupScene.IsActive || SceneLoading.IsLoading) && Time.realtimeSinceStartup < deadline)
                {
                    sawBar |= Object.FindObjectsByType<Text>().Any(t => t.text == "Загрузка главного меню");
                    sawEarlyStage |= Object.FindObjectsByType<UnityEngine.UI.Text>()
                        .Any(t => t.name == "StartupStage" && t.text == "Проверка раннего запуска");
                    yield return null;
                }
                Assert.AreEqual("Menu", SceneManager.GetActiveScene().name);
                Assert.IsFalse(SceneLoading.IsLoading);
                Assert.IsTrue(sawBar, "The initial menu load needs its own progress bar.");
                Assert.IsTrue(deferred && invoked, "Heavy startup callbacks must wait until the real scene is loaded.");
                Assert.IsTrue(earlyDeferred && earlyInvoked);
                Assert.IsFalse(StartupLoadingScreen.IsVisible, "The first UI must hand over to scene progress.");
                Assert.IsNull(Object.FindAnyObjectByType<StartupScene>(), "The temporary startup scene must leave after loading the menu.");
            }
            finally { SceneManager.sceneLoaded -= Observe; }
        }

        [UnityTest]
        public IEnumerator LocalSceneTransitionShowsCompletionAndReleasesInput()
        {
            if (!Application.CanStreamedLevelBeLoaded("Menu"))
                Assert.Ignore("Build the menu first: Pudge Wars → Build Main Menu.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Menu rendering needs a GPU.");
            var stages = new HashSet<string>();
            bool prepared = false, queued = false;
            void Observe(Scene scene, LoadSceneMode mode)
            {
                if (scene.name != "Menu") return;
                queued = SceneLoading.DeferPreparation("Проверка подготовки", () =>
                {
                    Assert.IsTrue(stages.Contains("Проверка подготовки"), "Present a stage before starting its work.");
                    Assert.IsTrue(OverlayInput.Captured);
                    prepared = true;
                });
                Assert.IsFalse(prepared, "Scene callbacks must queue heavy work instead of doing it during activation.");
            }
            SceneManager.sceneLoaded += Observe;
            try
            {
                SceneLoading.Load("Menu");
                Assert.IsTrue(SceneLoading.IsLoading);
                Assert.IsTrue(LoadingProgress.IsVisible);
                Assert.IsTrue(OverlayInput.Captured);
                float deadline = Time.realtimeSinceStartup + 30f;
                bool sawComplete = false;
                while (SceneLoading.IsLoading && Time.realtimeSinceStartup < deadline)
                {
                    foreach (var label in Object.FindObjectsByType<Text>().Where(t => t.name == "Stage")) stages.Add(label.text);
                    sawComplete |= Object.FindObjectsByType<Text>().Any(t => t.name == "Percent" && t.text == "100 %");
                    yield return null;
                }
                Assert.IsFalse(SceneLoading.IsLoading, "Scene loading must finish.");
                Assert.AreEqual("Menu", SceneManager.GetActiveScene().name);
                Assert.IsTrue(sawComplete, "100% must be displayed before the overlay disappears.");
                Assert.IsTrue(queued && prepared);
                Assert.IsTrue(stages.Contains("Создание рельефа меню"), "Rebuild the menu: Pudge Wars → Build Main Menu.");
                Assert.IsTrue(stages.Contains("Смешивание слоёв земли"));
                Assert.IsTrue(stages.Contains("Посадка деревьев"));
                Assert.IsTrue(stages.Contains("Посадка травы"));
                Assert.IsTrue(stages.Contains("Подготовка интерфейса меню"));
                Assert.IsFalse(LoadingProgress.IsVisible);
            }
            finally { SceneManager.sceneLoaded -= Observe; }
        }
    }
}
