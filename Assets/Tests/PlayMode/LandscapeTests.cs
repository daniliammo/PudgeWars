using System.Collections;
using System.Linq;
using NUnit.Framework;
using PudgeWars.Gameplay;
using PudgeWars.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PudgeWars.Tests
{
    /// <summary>The trees are the terrain's own, in the arena and in the main menu, and the menu shows one side.</summary>
    public class LandscapeTests
    {
        static IEnumerator Load(string scene)
        {
            AudioListener.volume = 0f;
            SceneManager.LoadScene(scene);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ArenaTreesAreTheTerrainsAndHeroesWalkAroundThem()
        {
            yield return Load(PudgeNetworkManager.ArenaScene);
            var terrain = Terrain.activeTerrain;
            Assert.IsNotNull(terrain, "the arena has no terrain");
            var data = terrain.terrainData;
            Assert.Greater(data.treeInstanceCount, 0, "the arena's terrain has no trees: Pudge Wars → Build Landscape");
            Assert.IsTrue(terrain.drawTreesAndFoliage, "the terrain does not draw its trees");
            Assert.AreEqual(0, Object.FindObjectsByType<Obstacle>().Length, "trees must not be scene objects");
            Assert.IsNotNull(terrain.GetComponent<TerrainTreeObstacles>());
            Assert.AreEqual(data.treeInstanceCount, Obstacle.All.Count, "every tree of the terrain is an obstacle");

            var tree = terrain.transform.position + Vector3.Scale(data.GetTreeInstance(0).position, data.size);
            Assert.IsTrue(Obstacle.Blocks(new Vector3(tree.x + 0.3f, 0f, tree.z), 0.5f), "a hero fits into a tree's trunk");
            Assert.IsFalse(Obstacle.Blocks(new Vector3(tree.x + 3f, 0f, tree.z), 0.5f));
        }

        [UnityTest]
        public IEnumerator MenuShowsOneSidesLandscapeWithTheTerrainsTrees()
        {
            if (!Application.CanStreamedLevelBeLoaded(PudgeNetworkManager.MenuScene))
                Assert.Ignore("the Menu scene is not in the build: Pudge Wars → Build Main Menu");
            yield return Load(PudgeNetworkManager.MenuScene);
            var landscape = Object.FindAnyObjectByType<MenuLandscape>();
            Assert.IsNotNull(landscape, "the menu has no MenuLandscape");
            Assert.AreEqual(MenuLandscape.Side, landscape.Shown, "the menu does not show the side drawn for this run");
            var drawn = landscape.Shown;
            try
            {
                foreach (var side in new[] { MenuSide.Radiant, MenuSide.Dire })
                {
                    landscape.Show(side);
                    var data = landscape.terrain.terrainData;
                    var look = side == MenuSide.Dire ? landscape.dire : landscape.radiant;
                    Assert.AreEqual(look.terrain, data, $"{side}: the terrain shows the other side's data");
                    Assert.Greater(data.treeInstanceCount, 20, $"{side}: the terrain has no trees of its own");
                    var models = data.treePrototypes.Select(p => p.prefab.name).ToList();
                    Assert.IsTrue(models.All(m => m.StartsWith("dire_") == (side == MenuSide.Dire)), $"{side}: trees of the other side: {string.Join(", ", models)}");
                    Assert.AreEqual(look.sky, RenderSettings.skybox);
                    Assert.AreEqual(look.fog, RenderSettings.fogColor);
                }
                Assert.AreEqual(0, Object.FindObjectsByType<MeshRenderer>().Count(r => r.name.StartsWith("tree_") || r.name.StartsWith("dire_")),
                    "trees must not be scene objects");
            }
            finally
            {
                landscape.Show(drawn);
            }
        }
    }
}
