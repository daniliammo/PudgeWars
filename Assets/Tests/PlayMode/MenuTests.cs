using System.Collections;
using System.Linq;
using Mirror;
using NUnit.Framework;
using PudgeWars.Gameplay;
using PudgeWars.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PudgeWars.Tests
{
    /// <summary>What the main menu sets: the graphics preset and the settings of the game it creates.</summary>
    public class MenuTests
    {
        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            AudioListener.volume = 0f;
            SceneManager.LoadScene("Arena");
            yield return null;
            yield return null;
            Object.FindAnyObjectByType<Arena>().passiveGold = 0;
        }

        static bool IsNetworkHero(Hookable h) => h != null && h.GetComponent<NetHero>() != null;

        [UnityTest]
        public IEnumerator GraphicsPresetTunesACopyOfThePipelineAndTheSun()
        {
            var before = GraphicsQuality.Current;
            var sun = Object.FindObjectsByType<Light>().First(l => l.type == LightType.Directional);
            try
            {
                GraphicsQuality.Set(GraphicsPreset.Low);
                yield return null;
                Assert.AreEqual(GraphicsPreset.Low, GraphicsQuality.Current);
                StringAssert.EndsWith("(preset)", QualitySettings.renderPipeline.name, "the preset must change a copy, not the project's URP asset");
                Assert.AreEqual(LightShadows.None, sun.shadows, "Low has no shadows");
                Assert.AreEqual(1, QualitySettings.globalTextureMipmapLimit, "Low halves the textures");

                GraphicsQuality.Set(GraphicsPreset.Ultra);
                yield return null;
                Assert.AreEqual(LightShadows.Soft, sun.shadows);
                Assert.AreEqual(0, QualitySettings.globalTextureMipmapLimit);
                Assert.AreEqual(1f, Terrain.activeTerrain.heightmapPixelError, 1e-3f, "Ultra draws the terrain in full detail");
            }
            finally
            {
                GraphicsQuality.Set(before);
            }
        }

        [UnityTest]
        public IEnumerator MenuGameStartsAHostWithItsSettings()
        {
            var manager = PudgeNetworkManager.Instance;
            Assert.IsNotNull(manager, "no network manager was created at start-up");
            var (team, size, bots, difficulty) = (manager.hostTeam, manager.teamSize, manager.fillWithBots, manager.botDifficulty);
            manager.nickname = "Хост";
            manager.hostTeam = Team.Player;
            manager.teamSize = 2;
            manager.fillWithBots = true;
            manager.botDifficulty = 2f;
            manager.LaunchInArena(manager.StartHost);
            try
            {
                float t = 0f;
                while (!IsNetworkHero(LocalPlayer.Hero) && t < 5f)
                {
                    t += Time.deltaTime;
                    yield return null;
                }
                Assert.IsTrue(IsNetworkHero(LocalPlayer.Hero), "the arena was loaded but the host did not start");
                yield return null;

                Assert.AreEqual(Team.Player, LocalPlayer.Hero.team, "the host plays for the team chosen in the menu");
                Assert.AreEqual(2, Hookable.All.Count(h => h.team == Team.Player));
                Assert.AreEqual(2, Hookable.All.Count(h => h.team == Team.Bots));
                var brains = Object.FindObjectsByType<BotBrain>();
                Assert.AreEqual(3, brains.Length);
                Assert.IsTrue(brains.All(b => Mathf.Approximately(b.difficulty, 2f)), "every bot gets the menu's difficulty");
            }
            finally
            {
                manager.StopHost();
                (manager.hostTeam, manager.teamSize, manager.fillWithBots, manager.botDifficulty) = (team, size, bots, difficulty);
            }
            // leaving goes back to the menu (or reloads the arena without it in the build)
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsFalse(NetworkServer.active || NetworkClient.active);
        }
    }
}
