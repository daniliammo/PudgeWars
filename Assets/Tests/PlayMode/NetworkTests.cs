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
    /// <summary>Network game in host mode (server and client in this process, KCP on the default port).</summary>
    public class NetworkTests
    {
        Arena arena;

        [UnitySetUp]
        public IEnumerator LoadArena()
        {
            AudioListener.volume = 0f;
            SceneManager.LoadScene("Arena");
            yield return null;
            yield return null;
            arena = Object.FindFirstObjectByType<Arena>();
            arena.passiveGold = 0;
        }

        static bool IsNetworkHero(Hookable h) => h != null && h.GetComponent<NetHero>() != null;

        [UnityTest]
        public IEnumerator HostGameSwapsArenaHeroesForNetworkOnesAndBackOnLeaving()
        {
            var manager = PudgeNetworkManager.Instance;
            Assert.IsNotNull(manager, "no network manager was created at start-up");
            manager.fillWithBots = true;
            manager.teamSize = 3;
            manager.nickname = "Хост";
            manager.StartHost();
            try
            {
                float t = 0f;
                while (!IsNetworkHero(LocalPlayer.Hero) && t < 5f)
                {
                    t += Time.deltaTime;
                    yield return null;
                }
                Assert.IsTrue(IsNetworkHero(LocalPlayer.Hero), "the host got no network hero");
                yield return null; // the arena's own heroes are gone by now

                var hero = LocalPlayer.Hero;
                Assert.IsFalse(Simulation.IsProxy, "the host simulates the match itself");
                Assert.AreEqual("Хост", hero.Nickname);
                Assert.AreEqual(Team.Player, hero.team);
                Assert.IsTrue(Hookable.All.All(h => h.GetComponent<NetworkIdentity>() != null), "an offline hero is left");
                Assert.AreEqual(3, Hookable.All.Count(h => h.team == Team.Player), "the host's team is not filled with bots");
                Assert.AreEqual(3, Hookable.All.Count(h => h.team == Team.Bots));
                Assert.IsNotNull(Object.FindFirstObjectByType<NetMatch>(), "no match object");
                Assert.AreEqual(hero.transform, Object.FindFirstObjectByType<TopDownCamera>().target, "the camera does not follow the host's hero");
                Assert.AreEqual(0, arena.Score);

                // the host plays as offline: its hook drags an enemy bot into the pit
                foreach (var brain in Object.FindObjectsByType<BotBrain>(FindObjectsSortMode.None)) brain.enabled = false;
                var enemies = Hookable.All.Where(h => h.team == Team.Bots).ToList();
                var bot = enemies[0];
                for (int i = 1; i < enemies.Count; i++) enemies[i].transform.position = new Vector3(12f, 0f, 12f - i);
                bot.transform.position = new Vector3(hero.transform.position.x, 0f, arena.pitHalfDepth + 1.5f);
                Assert.IsTrue(hero.GetComponent<HookAbility>().TryCast(bot.transform.position));
                t = 0f;
                while (arena.Score == 0 && t < 6f)
                {
                    t += Time.deltaTime;
                    yield return null;
                }
                Assert.AreEqual(1, arena.Score, "the hook did not kill the bot");
                Assert.AreEqual(arena.killGold, hero.GetComponent<HeroInventory>().Gold, "no gold for the kill");
            }
            finally
            {
                manager.StopHost();
            }

            // leaving reloads the arena offline
            float wait = 0f;
            while ((LocalPlayer.Hero == null || IsNetworkHero(LocalPlayer.Hero)) && wait < 5f)
            {
                wait += Time.deltaTime;
                yield return null;
            }
            Assert.IsNotNull(LocalPlayer.Hero, "the offline player did not come back");
            Assert.IsFalse(IsNetworkHero(LocalPlayer.Hero));
            Assert.IsFalse(NetworkServer.active || NetworkClient.active);
        }

        [UnityTest]
        public IEnumerator ClientProxyOnlyShowsTheServersState()
        {
            // what a pure client does with the state it receives, without a second process
            var player = LocalPlayer.Hero;
            var hook = player.GetComponent<HookAbility>();
            var inventory = player.GetComponent<HeroInventory>();
            Simulation.IsProxy = true;
            try
            {
                Vector3? cast = null;
                hook.CastRequested += p => cast = p;
                Assert.IsTrue(hook.TryCast(player.transform.position + Vector3.forward * 5f));
                Assert.IsTrue(cast.HasValue, "the cast was not sent to the server");
                Assert.IsTrue(hook.IsBusy, "the hook must wait for the server's answer");
                Assert.IsFalse(hook.HeadVisible, "a client must not throw the hook itself");

                ItemDef wanted = null;
                inventory.BuyRequested += item => wanted = item;
                var lens = Object.FindFirstObjectByType<ShopUI>().rows[0].item;
                inventory.ApplyRemoteState(lens.cost, Enumerable.Empty<ItemDef>());
                Assert.IsTrue(inventory.TryBuy(lens));
                Assert.AreEqual(lens, wanted, "the purchase was not sent to the server");
                Assert.AreEqual(lens.cost, inventory.Gold, "a client must not spend gold itself");

                // the server's word: dead (hidden), then alive again
                player.ApplyRemoteState(false, false, false);
                Assert.IsFalse(player.IsAlive);
                Assert.IsTrue(player.GetComponentsInChildren<Renderer>().Where(r => r.GetComponentInParent<HookAimIndicator>() == null).All(r => !r.enabled));
                yield return new WaitForSeconds(arena.respawnDelay + 0.5f);
                Assert.IsFalse(player.IsAlive, "a client must not respawn heroes itself");
                player.ApplyRemoteState(true, false, false);
                Assert.IsTrue(player.IsAlive);

                arena.ApplyRemoteScore(2, 1, 1, 0);
                Assert.AreEqual(2, arena.Score);
                Assert.AreEqual(1, arena.Deaths);
                Assert.AreEqual(1, arena.KillsOf(Team.Bots));
            }
            finally
            {
                Simulation.IsProxy = false;
            }
        }
    }
}
