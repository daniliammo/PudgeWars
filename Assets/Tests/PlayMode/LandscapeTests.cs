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
    /// <summary>The trees are the terrain's own, in the arena and in the main menu, they can be cut down, and the menu shows one side.</summary>
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
            Assert.IsNotNull(terrain.GetComponent<TerrainTrees>());
            Assert.AreEqual(data.treeInstanceCount, Obstacle.All.Count, "every tree of the terrain is an obstacle");

            var tree = terrain.transform.position + Vector3.Scale(data.GetTreeInstance(0).position, data.size);
            Assert.IsTrue(Obstacle.Blocks(new Vector3(tree.x + 0.3f, 0f, tree.z), 0.5f), "a hero fits into a tree's trunk");
            Assert.IsFalse(Obstacle.Blocks(new Vector3(tree.x + 3f, 0f, tree.z), 0.5f));
            // grass on Radiant's side (z < 0), none on Dire's scorched ground
            int res = data.detailResolution, radiant = 0, dire = 0;
            for (int i = 0; i < data.detailPrototypes.Length; i++)
            {
                var layer = data.GetDetailLayer(0, 0, res, res, i);
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    if (z < res / 2) radiant += layer[z, x];
                    else dire += layer[z, x];
            }
            Assert.Greater(radiant, 100, "no grass on Radiant's side: Pudge Wars → Build Test Arena");
            Assert.Less(dire, radiant / 10, "grass on Dire's scorched ground");
        }

        [UnityTest]
        public IEnumerator CutTreeLeavesItsStumpItsTopFallsAndItGrowsBack()
        {
            yield return Load(PudgeNetworkManager.ArenaScene);
            var trees = Object.FindAnyObjectByType<TerrainTrees>();
            Assert.IsNotNull(trees, "the arena's terrain has no TerrainTrees: Pudge Wars → Build Test Arena");
            var asset = trees.GetComponent<Terrain>().terrainData;
            Assert.AreNotEqual("ArenaTerrain", asset.name, "the trees must be cut on a copy of the terrain's data, not on the asset");
            Assert.IsTrue(trees.stumps.Any(s => s != null) && trees.fallingParts.Any(f => f != null), "no stumps or falling tops");

            // a tree with a stump and a top, cut from the arena's middle
            int index = Enumerable.Range(0, trees.Count).First(i => trees.stumps[asset.GetTreeInstance(i).prototypeIndex] != null);
            var at = trees.Position(index);
            var from = new Vector3(0f, 0f, at.z);
            trees.regrowTime = 4f;
            int obstacles = Obstacle.All.Count;
            Assert.IsTrue(trees.Fell(index, from));
            Assert.IsFalse(trees.IsStanding(index));
            Assert.IsFalse(trees.Fell(index, from), "a cut tree cannot be cut again");
            Assert.AreEqual(0f, asset.GetTreeInstance(index).heightScale, "the terrain still draws the cut tree");
            Assert.AreEqual(obstacles - 1, Obstacle.All.Count, "a cut tree still stops heroes");
            Assert.IsFalse(Obstacle.Blocks(at, 0.5f), "the stump stops heroes");
            Assert.IsNotNull(trees.transform.Find(trees.stumps[asset.GetTreeInstance(index).prototypeIndex].name + "(Clone)"), "no stump");

            // the top falls away from where it was cut from
            var top = trees.transform.Find("Falling tree");
            Assert.IsNotNull(top, "no falling top");
            yield return new WaitForSeconds(trees.fallTime + 0.4f);
            Assert.Greater(Vector3.Angle(Vector3.up, top.up), 70f, "the top did not fall");
            var away = at - from;
            Assert.Greater(Vector3.Dot(new Vector3(top.up.x, 0f, top.up.z), new Vector3(away.x, 0f, away.z)), 0f, "the top fell toward whoever cut it");
            yield return new WaitForSeconds(trees.lieTime + trees.sinkTime + 0.3f);
            Assert.IsTrue(top == null, "the fallen top did not sink into the ground");

            // and it grows back
            yield return new WaitForSeconds(4f - trees.fallTime - trees.lieTime - trees.sinkTime + 0.5f);
            Assert.IsTrue(trees.IsStanding(index), "the tree did not grow back");
            Assert.AreEqual(obstacles, Obstacle.All.Count);
            Assert.Greater(asset.GetTreeInstance(index).heightScale, 0f);
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
                    var pale = new[] { "tree_oak_03", "tree_oak_04", "tree_oak_05", "tree_oak_06", "tree_gnarly001", "tree_cine_02_low" };
                    Assert.IsFalse(models.Any(pale.Contains), $"{side}: white trees (untinted bark) in the menu: {string.Join(", ", models)}");
                    int res = data.detailResolution, grass = 0;
                    for (int i = 0; i < data.detailPrototypes.Length; i++)
                        foreach (int n in data.GetDetailLayer(0, 0, res, res, i)) grass += n;
                    if (side == MenuSide.Radiant) Assert.Greater(grass, 100, "Radiant's ground has no grass");
                    else Assert.AreEqual(0, grass, "grass on Dire's scorched ground");
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
