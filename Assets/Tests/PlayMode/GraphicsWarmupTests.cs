using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
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
            var sun = RenderSettings.sun;
            var lights = Object.FindObjectsByType<Light>();
            var masks = lights.Select(light => light.cullingMask).ToArray();
            int terrainCount = Terrain.activeTerrains.Length;
            var preparation = StartupGraphicsWarmup.Begin();
            Assert.IsNotNull(preparation);
            try
            {
                Assert.IsTrue(OverlayInput.Captured, "Input must stay blocked while graphics are prepared.");
                float deadline = Time.realtimeSinceStartup + 180f;
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
                Assert.GreaterOrEqual(preparation.DrawCount, 32, "All effect/layout samples need URP draws with both fog and shadow states.");
                Assert.Greater(preparation.SceneSampleCount, 0, "Menu/Arena models and materials must be sampled too.");
                Assert.GreaterOrEqual(preparation.TerrainDrawCount, 64, "Both saved terrains and both menu sides need near/far, instanced/scalar, fog/shadow draws.");
                Assert.AreEqual(2, preparation.UiDrawCount, "Render masked/plain/outlined UI into scene and screen attachments.");
                yield return null; // Destroy releases the temporary camera and renderers at the end of the frame.
                Assert.IsFalse(OverlayInput.Captured);
                Assert.AreSame(pipeline, QualitySettings.renderPipeline);
                Assert.AreEqual(preset, GraphicsQuality.Current);
                Assert.AreEqual(fog, RenderSettings.fog);
                Assert.AreEqual(fogMode, RenderSettings.fogMode);
                Assert.AreSame(sky, RenderSettings.skybox);
                Assert.AreSame(sun, RenderSettings.sun);
                CollectionAssert.AreEqual(masks, lights.Select(light => light.cullingMask));
                Assert.AreEqual(terrainCount, Terrain.activeTerrains.Length);
                Assert.AreEqual(cameraCount, Object.FindObjectsByType<Camera>().Length);
                Assert.AreEqual(units, Unit.AllUnits.Count, "Preparation must not spawn gameplay heroes.");
                Assert.AreEqual(sounds, WorldAudio.Played, "Preparation must remain silent.");
                Assert.AreEqual(links, ClarityLink.ActiveCount);
                Assert.AreEqual(bursts, ManaBurst.ActiveCount);
                Assert.AreEqual(0, BloodSplatter.GroundStainCount);
                Assert.AreEqual(0, BloodSplatter.BodyStainCount);
                Assert.IsTrue(Resources.FindObjectsOfTypeAll<Texture2D>().Any(t => t.name == "BloodStains"),
                    "The expensive blood atlas must already exist before the first collision.");
                var library = Resources.Load<GraphicsWarmupLibrary>(GraphicsWarmupLibrary.ResourcePath);
                SceneManager.LoadScene("Menu");
                yield return null;
                yield return Resources.UnloadUnusedAssets();
                Assert.AreSame(preparation, StartupGraphicsWarmup.Begin(), "Changing scenes must reuse the prepared renderer resources.");
                Assert.IsTrue(library != null && library.models.All(model => model != null),
                    "The persistent warmup must retain both scenes' model/material references after unloading unused assets.");
                Assert.IsTrue(preparation.Succeeded);
                var replacement = Object.Instantiate(pipeline != null ? pipeline : GraphicsSettings.defaultRenderPipeline);
                try
                {
                    QualitySettings.renderPipeline = replacement;
                    Assert.AreSame(preparation, StartupGraphicsWarmup.Begin());
                    Assert.IsTrue(StartupGraphicsWarmup.IsPreparing, "A changed rendering pipeline needs another preparation.");
                    Assert.IsFalse(preparation.Succeeded, "The previous pass must not report success for the new configuration.");
                    Assert.AreEqual(0f, preparation.Progress);
                }
                finally { QualitySettings.renderPipeline = pipeline; Object.Destroy(replacement); }
            }
            finally { if (preparation != null) Object.Destroy(preparation.gameObject); }
        }

        [UnityTest]
        public IEnumerator AbortingPreparationReleasesInputAndTheLoadingCanvas()
        {
            var preparation = StartupGraphicsWarmup.Begin();
            Assert.IsNotNull(preparation);
            Assert.IsTrue(StartupGraphicsWarmup.IsPreparing);
            float deadline = Time.realtimeSinceStartup + 120f;
            while (preparation.SceneSampleCount == 0 && StartupGraphicsWarmup.IsPreparing && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.Greater(preparation.SceneSampleCount, 0, "Reach the render stage before testing its cleanup.");
            Object.Destroy(preparation.gameObject);
            yield return null;
            Assert.IsFalse(StartupGraphicsWarmup.IsPreparing);
            Assert.IsFalse(OverlayInput.Captured);
            Assert.IsFalse(LoadingProgress.IsVisible);
            Assert.IsFalse(Object.FindObjectsByType<Camera>().Any(camera => camera.name == "Camera" && camera.transform.parent?.name == "GraphicsWarmupStage"));
        }

        [UnityTest]
        public IEnumerator IncompleteLibraryDoesNotReportSuccessfulPreparation()
        {
            var invalid = ScriptableObject.CreateInstance<GraphicsWarmupLibrary>();
            LogAssert.Expect(LogType.Warning, new Regex(@"\[GraphicsWarmup\] Incomplete:.*Incomplete graphics library", RegexOptions.Singleline));
            var preparation = StartupGraphicsWarmup.Begin(invalid);
            try
            {
                float deadline = Time.realtimeSinceStartup + 10f;
                while (StartupGraphicsWarmup.IsPreparing && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsFalse(StartupGraphicsWarmup.IsPreparing);
                Assert.IsFalse(preparation.Succeeded);
                Assert.AreEqual(0, preparation.DrawCount);
                Assert.IsNotEmpty(preparation.Failure);
                Assert.Less(preparation.Progress, 1f);
                Assert.IsFalse(LoadingProgress.IsVisible);
                Assert.IsFalse(OverlayInput.Captured);
            }
            finally
            {
                if (preparation != null) Object.Destroy(preparation.gameObject);
                Object.Destroy(invalid);
            }
        }

        [Test]
        public void LibraryIncludesBothMenuSidesAndTheArena()
        {
            var library = Resources.Load<GraphicsWarmupLibrary>(GraphicsWarmupLibrary.ResourcePath);
            Assert.IsNotNull(library, "Pudge Wars → Build Graphics Warmup Library.");
            Assert.DoesNotThrow(library.Validate);
            Assert.IsTrue(library.terrains.Any(terrain => terrain.name == "ArenaTerrain"));
            Assert.IsTrue(library.models.Any(model => model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0));
            Assert.IsNotEmpty(library.radiant.trees);
            Assert.IsNotEmpty(library.dire.trees);
            Assert.IsNotEmpty(library.radiant.grass);
            Assert.IsNotEmpty(library.dire.grass);
        }

        [Test]
        public void EditorDoesNotStartWarmupAutomatically()
        {
            if (!Application.isEditor) Assert.Ignore("This check exercises editor-only startup behavior.");
            StartupGraphicsWarmup.BeginAutomatically();
            Assert.IsNull(Object.FindAnyObjectByType<StartupGraphicsWarmup>());
            Assert.IsFalse(StartupGraphicsWarmup.IsPreparing);
        }
    }
}
