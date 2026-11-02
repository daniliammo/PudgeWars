using System.Collections;
using System.Linq;
using NUnit.Framework;
using PudgeWars.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PudgeWars.Tests
{
    public class GraphicsWarmupTests
    {
        [UnitySetUp]
        public IEnumerator LoadMenu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Graphics warmup needs a GPU; run without -nographics.");
            if (!Application.CanStreamedLevelBeLoaded("Menu"))
                Assert.Ignore("Build the menu first: Pudge Wars → Build Main Menu.");
            var previous = Object.FindAnyObjectByType<StartupGraphicsWarmup>();
            if (previous != null) Object.Destroy(previous.gameObject);
            SceneManager.LoadScene("Menu");
            yield return null;
            yield return null;
            OverlayInput.Captured = false;
        }

        [UnityTest]
        public IEnumerator WarmupDrawsEffectsSilentlyAndRestoresTheScene()
        {
            int sounds = WorldAudio.Played, links = ClarityLink.ActiveCount, bursts = ManaBurst.ActiveCount;
            int units = Unit.AllUnits.Count;
            int cameraCount = Object.FindObjectsByType<Camera>().Length;
            var pipeline = QualitySettings.renderPipeline;
            var preset = GraphicsQuality.Current;
            bool fog = RenderSettings.fog;
            var fogMode = RenderSettings.fogMode;
            var sky = RenderSettings.skybox;
            var preparation = StartupGraphicsWarmup.Begin();
            Assert.IsNotNull(preparation);
            try
            {
                Assert.IsTrue(OverlayInput.Captured, "Input must stay blocked while graphics are prepared.");
                float deadline = Time.realtimeSinceStartup + 60f;
                float previousProgress = 0f;
                while (StartupGraphicsWarmup.IsPreparing && Time.realtimeSinceStartup < deadline)
                {
                    Assert.GreaterOrEqual(preparation.Progress, previousProgress, "Displayed warmup progress must not move backwards.");
                    Assert.That(preparation.Progress, Is.InRange(0f, 1f));
                    previousProgress = preparation.Progress;
                    yield return null;
                }
                Assert.IsFalse(StartupGraphicsWarmup.IsPreparing, "Preparation must finish without trapping the loading screen.");
                Assert.IsTrue(preparation.Succeeded);
                Assert.AreEqual(1f, preparation.Progress, "Successful preparation must reach 100%.");
                Assert.GreaterOrEqual(preparation.DrawCount, 16, "All eight effect/layout samples need real URP draws with and without fog.");
                yield return null; // Destroy releases the temporary camera and renderers at the end of the frame.
                Assert.IsFalse(OverlayInput.Captured);
                Assert.AreSame(pipeline, QualitySettings.renderPipeline);
                Assert.AreEqual(preset, GraphicsQuality.Current);
                Assert.AreEqual(fog, RenderSettings.fog);
                Assert.AreEqual(fogMode, RenderSettings.fogMode);
                Assert.AreSame(sky, RenderSettings.skybox);
                Assert.AreEqual(cameraCount, Object.FindObjectsByType<Camera>().Length);
                Assert.AreEqual(units, Unit.AllUnits.Count, "Preparation must not spawn gameplay heroes.");
                Assert.AreEqual(sounds, WorldAudio.Played, "Preparation must remain silent.");
                Assert.AreEqual(links, ClarityLink.ActiveCount);
                Assert.AreEqual(bursts, ManaBurst.ActiveCount);
                Assert.AreEqual(0, BloodSplatter.GroundStainCount);
                Assert.AreEqual(0, BloodSplatter.BodyStainCount);
                Assert.IsTrue(Resources.FindObjectsOfTypeAll<Texture2D>().Any(t => t.name == "BloodStains"),
                    "The expensive blood atlas must already exist before the first collision.");
            }
            finally { if (preparation != null) Object.Destroy(preparation.gameObject); }
        }

        [UnityTest]
        public IEnumerator AbortingPreparationReleasesInputAndTheLoadingCanvas()
        {
            var preparation = StartupGraphicsWarmup.Begin();
            Assert.IsNotNull(preparation);
            Assert.IsTrue(StartupGraphicsWarmup.IsPreparing);
            yield return null;
            Object.Destroy(preparation.gameObject);
            yield return null;
            Assert.IsFalse(StartupGraphicsWarmup.IsPreparing);
            Assert.IsFalse(OverlayInput.Captured);
            Assert.IsFalse(LoadingProgress.IsVisible);
        }
    }
}
