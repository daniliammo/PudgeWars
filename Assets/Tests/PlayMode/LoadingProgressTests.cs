using System.Collections;
using System.Linq;
using NUnit.Framework;
using PudgeWars.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
                Assert.IsTrue(OverlayInput.Captured);
                var bars = Object.FindObjectsByType<Slider>().Where(s => s.name == "ProgressBar").ToArray();
                Assert.AreEqual(2, bars.Length);
                Assert.IsTrue(bars.All(s => !s.interactable), "Loading progress is read-only.");
                Assert.IsTrue(bars.Any(s => Mathf.Approximately(s.value, 0.42f)));
                Assert.IsTrue(bars.Any(s => Mathf.Approximately(s.value, 0.7f)));
                Assert.IsTrue(Object.FindObjectsByType<Text>().Any(t => t.name == "Percent" && t.text == "42 %"));
                graphics.Dispose();
                Assert.IsTrue(LoadingProgress.IsVisible, "One operation cannot hide another operation's progress.");
                Assert.IsTrue(OverlayInput.Captured);
                scene.Report(1f);
                yield return null;
                Assert.IsTrue(Object.FindObjectsByType<Text>().Any(t => t.name == "Percent" && t.text == "100 %"));
            }
            finally { graphics.Dispose(); scene.Dispose(); OverlayInput.Captured = captured; }
            Assert.IsFalse(LoadingProgress.IsVisible);
        }

        [UnityTest]
        public IEnumerator LocalSceneTransitionShowsCompletionAndReleasesInput()
        {
            if (!Application.CanStreamedLevelBeLoaded("Menu"))
                Assert.Ignore("Build the menu first: Pudge Wars → Build Main Menu.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Menu rendering needs a GPU.");
            SceneLoading.Load("Menu");
            Assert.IsTrue(SceneLoading.IsLoading);
            Assert.IsTrue(LoadingProgress.IsVisible);
            Assert.IsTrue(OverlayInput.Captured);
            float deadline = Time.realtimeSinceStartup + 30f;
            bool sawComplete = false;
            while (SceneLoading.IsLoading && Time.realtimeSinceStartup < deadline)
            {
                sawComplete |= Object.FindObjectsByType<Text>().Any(t => t.name == "Percent" && t.text == "100 %");
                yield return null;
            }
            Assert.IsFalse(SceneLoading.IsLoading, "Scene loading must finish.");
            Assert.AreEqual("Menu", SceneManager.GetActiveScene().name);
            Assert.IsTrue(sawComplete, "100% must be displayed before the overlay disappears.");
            Assert.IsFalse(LoadingProgress.IsVisible);
        }
    }
}
