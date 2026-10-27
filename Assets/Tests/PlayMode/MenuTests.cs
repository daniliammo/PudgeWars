using System.Collections;
using System.Linq;
using Mirror;
using NUnit.Framework;
using PudgeWars.Gameplay;
using PudgeWars.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PudgeWars.Tests
{
    /// <summary>What the main menu sets: the graphics preset, the lobby of the game it creates and its settings.</summary>
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

        static bool IsNetworkHero(Pudge h) => h != null && h.GetComponent<NetHero>() != null;

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
        public IEnumerator MenuLobbyCountsPlayersAndStartsTheMatchWithItsSettings()
        {
            if (!Application.CanStreamedLevelBeLoaded(PudgeNetworkManager.MenuScene))
                Assert.Ignore("the Menu scene is not in the build: Pudge Wars → Build Main Menu");
            SceneManager.LoadScene(PudgeNetworkManager.MenuScene);
            yield return null;
            yield return null;
            var manager = PudgeNetworkManager.Instance;
            Assert.IsNotNull(manager, "no network manager was created at start-up");
            var (team, size, bots, difficulty) = (manager.hostTeam, manager.teamSize, manager.fillWithBots, manager.botDifficulty);
            manager.nickname = "Хост";
            manager.hostTeam = Team.Player;
            manager.teamSize = 2;
            manager.fillWithBots = true;
            manager.botDifficulty = 2f;
            manager.StartHost();
            try
            {
                float t = 0f;
                while (manager.Lobby.players == null && t < 5f)
                {
                    t += Time.deltaTime;
                    yield return null;
                }
                Assert.IsNotNull(manager.Lobby.players, "the host got no lobby");
                Assert.IsTrue(PudgeNetworkManager.InLobby, "the host must wait in the lobby, not start the match");
                Assert.AreEqual(1, manager.Lobby.players.Length);
                Assert.AreEqual("Хост", manager.Lobby.players[0].nickname);
                Assert.AreEqual(Team.Player, manager.Lobby.players[0].team, "the host plays for the team chosen in the menu");
                Assert.AreEqual(0, Pudge.All.Count, "no heroes before the match");
                yield return null;
                var texts = Object.FindObjectsByType<Text>().Select(x => x.text).ToList();
                Assert.IsTrue(texts.Contains("Игроков в лобби: 1 из 4"), "the menu does not show how many players are in: " + string.Join(" | ", texts));

                // the host changes the settings in the lobby: the players get them
                manager.teamSize = 3;
                manager.UpdateLobby();
                yield return null;
                yield return null;
                Assert.AreEqual(3, manager.Lobby.teamSize);
                manager.teamSize = 2;
                manager.UpdateLobby();

                manager.StartMatch();
                t = 0f;
                while (!IsNetworkHero(LocalPlayer.Hero) && t < 10f)
                {
                    t += Time.deltaTime;
                    yield return null;
                }
                Assert.IsTrue(IsNetworkHero(LocalPlayer.Hero), "the match did not start in the arena");
                yield return null;

                Assert.AreEqual(PudgeNetworkManager.ArenaScene, SceneManager.GetActiveScene().name);
                Assert.AreEqual(Team.Player, LocalPlayer.Hero.team);
                Assert.AreEqual("Хост", LocalPlayer.Hero.Nickname);
                Assert.IsTrue(Pudge.All.All(IsNetworkHero), "an offline hero is left");
                Assert.AreEqual(2, Pudge.All.Count(h => h.team == Team.Player));
                Assert.AreEqual(2, Pudge.All.Count(h => h.team == Team.Bots));
                var brains = Object.FindObjectsByType<BotBrain>();
                Assert.AreEqual(3, brains.Length);
                Assert.IsTrue(brains.All(b => Mathf.Approximately(b.difficulty, 2f)), "every bot gets the menu's difficulty");
            }
            finally
            {
                manager.StopHost();
                (manager.hostTeam, manager.teamSize, manager.fillWithBots, manager.botDifficulty) = (team, size, bots, difficulty);
            }
            // leaving goes back to the menu
            float wait = 0f;
            while (SceneManager.GetActiveScene().name != PudgeNetworkManager.MenuScene && wait < 5f)
            {
                wait += Time.deltaTime;
                yield return null;
            }
            Assert.AreEqual(PudgeNetworkManager.MenuScene, SceneManager.GetActiveScene().name, "leaving the game did not return to the menu");
            Assert.IsFalse(NetworkServer.active || NetworkClient.active);
        }
    }
}
