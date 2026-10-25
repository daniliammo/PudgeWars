using System.Collections.Generic;
using System.IO;
using System.Linq;
using PudgeWars.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PudgeWars.EditorTools
{
    /// <summary>
    /// The test arena's ground as one terrain: the two sides on flat ground (Radiant grass on the player's side,
    /// Dire's scorched ground on the bots' side), the pit cut into it, steep hills instead of walls all around
    /// (rock on the slopes, black in the depth of the pit), and a few of Dota 2's standard trees inside the arena
    /// along its side edges: Radiant oaks and pines on the player's side, Dire trees on the bots' side, one mango tree;
    /// Dota's green grass on Radiant's ground (terrain details).
    /// The trees are the terrain's own (its tree prototypes and instances, <see cref="SetTrees"/>), not scene objects;
    /// heroes walk around them, hooks fly through them, and they can be cut down with Dota's stumps and falling tops
    /// (<see cref="TerrainTrees"/>).
    /// Trees come from Tools/Landscape/build_trees.py, the ground textures from make_ground_textures.py. Rebuilt as a
    /// whole (the "Landscape" object and the terrain asset) by its menu item and by Build Test Arena; the old box
    /// geometry of the arena (ground blocks, walls, pit) is removed.
    /// </summary>
    public static class LandscapeBuilder
    {
        const string TreeDir = "Assets/Art/Landscape/Trees";
        const string GrassDir = "Assets/Art/Landscape/Grass";
        internal const string TerrainDir = "Assets/Art/Landscape/Terrain";
        const string RootName = "Landscape";
        // the arena keeps Dota's standard map trees; the main menu's Radiant forest has all of Radiant's
        static readonly string[] ArenaRadiantTrees = { "tree_oak_01", "tree_pine_01", "tree_oak_02", "tree_pine_02", "tree_oak_01b", "tree_pine_03b" };
        internal static readonly string[] RadiantTrees =
        {
            "tree_oak_01", "tree_pine_01", "tree_bamboo_01", "tree_oak_02", "tree_pine_02", "tree_bamboo_02",
            "tree_oak_01b", "tree_pine_03b", "tree_bamboo_03", "tree_oak_00", "tree_oakstatic_01", "tree_bamboo_00", "tree_oakstatic_02",
        };
        // Radiant's trees with tintable bark (Dota tints it per map): white here, so imported but not planted
        static readonly string[] PaleTrees = { "tree_oak_03", "tree_oak_04", "tree_oak_05", "tree_oak_06", "tree_gnarly001", "tree_cine_02_low" };
        // Radiant's trees in snow: imported for a winter landscape, not planted yet
        internal static readonly string[] SnowTrees =
        {
            "tree_oak_snow_01", "tree_oak_snow_01b", "tree_oak_snow_02", "tree_pine_snow_01", "tree_pine_snow_02", "tree_pine_snow_03b",
            "tree_pine_01_heavysnow", "tree_pine_02_heavysnow", "tree_bamboo_snow_01", "tree_bamboo_snow_02", "tree_bamboo_snow_03",
        };
        internal static readonly string[] DireTrees = { "dire_tree004", "dire_tree007", "dire_tree004b", "dire_tree008" };
        internal const string MangoTree = "mango_tree";
        // Dota's grass clumps (Tools/Landscape/build_grass.py): green, and the tintable ones tinted with frost
        internal static readonly string[] Grass = { "grass_clump_00a", "grass_clump_00b", "grass_clump_00d", "grass_clump_00e", "grass_clump_00f" };
        internal static readonly string[] WinterGrass =
            { "grass_clump_winter_b", "grass_clump_winter_c", "grass_clump_winter_d", "grass_clump_winter_e", "grass_clump_winter_f" };
        static readonly string[] TwoSidedMaterials = { "dire_tree007_moss" }; // doubleSided in the glTF
        static readonly string[] OpaqueMaterials = { "tree_oak_03", "tree_desert002", "tree_gnarly001", "tree_cine_02_low" }; // the rest are masked
        // the parts of a tree that can be cut down (Tools/Landscape/build_trees.py): what stays, what falls
        const string StumpSuffix = "_stump", FallingSuffix = "_falling";
        const float LeafCutoff = 0.5f;
        const float TreeCullHeight = 0.01f; // of the screen: a terrain tree smaller than this is not drawn

        // 64 m with a 257 heightmap: 0.25 m cells, their edges on the pit's and the sides' whole metres
        const float TerrainSize = 64f;
        const int HeightmapResolution = 257;
        const float TerrainBottom = -7f, TerrainHeight = 24f;
        const float PitDepth = 6f;     // below the falling heroes' kill height (Arena.killHeight)
        // the pit walls go down over this much, rounded at the rim and at the bottom: a ravine, not a cut; a hero
        // walking off the edge (the slowest way in) still falls in front of them, not through them
        const float PitSlope = 1.8f;
        const float PitEndSlope = 0.75f; // the ends at the side hills are steeper: heroes can fall in right there
        const float PitRimJitter = 0.15f; // the rim wanders this much into the pit, never out of it (Arena.IsOverPit)
        const float PitLedges = 0.35f;   // bumps on the walls, metres
        const float CliffHeight = 7f;  // the hills rise this high within the first CliffRun metres: too steep to climb
        const float CliffRun = 3f;
        const float RidgeHeight = 6f;  // and then this much more toward the terrain's edge
        // behind the back edges the hills stay under this slope (rise per metre, plus a steeper one sideways): the
        // cameras stand above them and look over them at the heroes at the back edge
        const float BackSlope = 1.3f, BackSideSlope = 2.5f;

        // grass on Radiant's ground (the player's side and its hills), in patches; thin where heroes fight, so that the
        // heroes, their markers and the hooks stay in plain view
        const int GrassResolution = 128;  // detail cells: 0.5 m
        const float GrassPerCell = 0.45f; // clumps in a cell in the thick of a patch, outside the arena
        const float GrassInArena = 0.5f;  // of that inside it
        const float GrassDistance = 80f;
        const float RimSandWidth = 1.4f; // the sand band along the pit's rims, metres

        const float TreeInset = 1.3f;  // trees stand this far inside the side edges
        const int TreesPerEdge = 3;
        const float TreeRadius = 0.5f;
        const int Seed = 1300;

        [MenuItem("Pudge Wars/Build Landscape")]
        public static void BuildInOpenScene()
        {
            var arena = Object.FindAnyObjectByType<Arena>();
            if (arena == null)
            {
                Debug.LogError("[LandscapeBuilder] the open scene has no Arena");
                return;
            }
            Build(arena);
            // saved at once: another builder opening a new scene would drop the change without asking
            EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);
            EditorSceneManager.SaveScene(arena.gameObject.scene);
        }

        public static void Build(Arena arena)
        {
            SetupTreeAssets();
            foreach (var old in arena.gameObject.scene.GetRootGameObjects().Where(g => g.name == RootName))
                Object.DestroyImmediate(old);
            foreach (Transform box in arena.transform.Cast<Transform>().ToList())
                if (box.name.StartsWith("Ground_") || box.name.StartsWith("Pit_")) Object.DestroyImmediate(box.gameObject);

            var root = new GameObject(RootName).transform;
            var rng = new System.Random(Seed);
            var terrain = BuildTerrain(arena, root, rng);
            PlaceTrees(arena, terrain, rng);
            AssetDatabase.SaveAssets();
            Debug.Log("[LandscapeBuilder] landscape built");
        }

        // terrain //////////////////////////////////////////////////////////////

        static Terrain BuildTerrain(Arena arena, Transform parent, System.Random rng)
        {
            Directory.CreateDirectory(TerrainDir);
            var data = new TerrainData { heightmapResolution = HeightmapResolution }; // resolution first: it resets the size
            data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);
            data.alphamapResolution = 256;
            data.baseMapResolution = 512;
            float half = TerrainSize / 2f, cell = TerrainSize / (HeightmapResolution - 1);
            var hills = new Vector2(rng.Next(1000), rng.Next(1000));
            var bumps = new Vector2(rng.Next(1000), rng.Next(1000));
            var border = new Vector2(rng.Next(1000), rng.Next(1000));

            var heights = new float[HeightmapResolution, HeightmapResolution];
            for (int z = 0; z < HeightmapResolution; z++)
            for (int x = 0; x < HeightmapResolution; x++)
            {
                float y = GroundHeight(arena, -half + x * cell, -half + z * cell, hills, bumps);
                heights[z, x] = (y - TerrainBottom) / TerrainHeight;
            }
            data.SetHeights(0, 0, heights);

            // Dota's Radiant grass on the player's side (z < 0) and Dire's cracked ground on the bots' side with a
            // ragged border at z = 0, a band of sand along the pit's rims as on a river bank, rock on the steep slopes,
            // black in the depth of the pit (layers 0 and 1 are the sides' ground: PlaceGrass reads layer 0)
            data.terrainLayers = new[]
            {
                Layer("Radiant_Grass", 5f), Layer("Dire_Ground", 6f), Layer("Cliff", 5f), Layer("Abyss", 5f), Layer("Path_Sand", 3f),
            };
            int aRes = data.alphamapResolution;
            var alpha = new float[aRes, aRes, 5];
            for (int z = 0; z < aRes; z++)
            for (int x = 0; x < aRes; x++)
            {
                float u = (x + 0.5f) / aRes, v = (z + 0.5f) / aRes;
                float wx = -half + u * TerrainSize, wz = -half + v * TerrainSize;
                float y = data.GetInterpolatedHeight(u, v) + TerrainBottom;
                float abyss = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2.5f, -5.5f, y));
                float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(32f, 48f, data.GetSteepness(u, v)));
                float ragged = (Mathf.PerlinNoise(border.x + wx * 0.15f, border.y + wz * 0.15f) - 0.5f) * 5f;
                float dire = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1.5f, 1.5f, wz + ragged));
                float ground = (1f - abyss) * (1f - rock);
                float sand = RimSand(arena, wx, wz, border);
                alpha[z, x, 0] = ground * (1f - sand) * (1f - dire);
                alpha[z, x, 1] = ground * (1f - sand) * dire;
                alpha[z, x, 2] = (1f - abyss) * rock;
                alpha[z, x, 3] = abyss;
                alpha[z, x, 4] = ground * sand;
            }
            data.SetAlphamaps(0, 0, alpha);
            PlaceGrass(arena, data, alpha);

            const string dataPath = TerrainDir + "/ArenaTerrain.asset";
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(-half, TerrainBottom, -half);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = TerrainMaterial();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 100f;
            terrain.shadowCastingMode = ShadowCastingMode.On; // the pit walls and the hills shade what lies below them
            terrain.treeDistance = 120f;
            terrain.detailObjectDistance = GrassDistance;
            go.AddComponent<TerrainTrees>().radius = TreeRadius;
            return terrain;
        }

        /// <summary>Sand along the pit's rims on both sides, a ragged band about RimSandWidth wide, fading out at its edge.</summary>
        static float RimSand(Arena arena, float x, float z, Vector2 noise)
        {
            float fromRim = Mathf.Abs(z) - arena.pitHalfDepth;
            if (fromRim < -1f) return 0f;
            float width = RimSandWidth + (Mathf.PerlinNoise(noise.x + 50f + x * 0.35f, noise.y + Mathf.Sign(z) * 9f) - 0.5f) * 1.2f;
            float along = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(arena.halfWidth + 1f, arena.halfWidth - 1f, Mathf.Abs(x)));
            return along * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(width - 0.5f, width + 0.3f, fromRim)));
        }

        /// <summary>
        /// Green grass clumps as the terrain's details where Radiant's grass covers the ground (not on Dire's scorched
        /// ground, the rock or in the pit), in patches. Its own random numbers: the trees stay where they were.
        /// </summary>
        static void PlaceGrass(Arena arena, TerrainData data, float[,,] alpha)
        {
            var rng = new System.Random(Seed + 1);
            var prototypes = GrassPrototypes(Grass);
            data.detailPrototypes = prototypes;
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            data.SetDetailResolution(GrassResolution, 32);
            var layers = prototypes.Select(_ => new int[GrassResolution, GrassResolution]).ToArray();
            var patches = new Vector2(rng.Next(1000), rng.Next(1000));
            int aRes = alpha.GetLength(0);
            float half = TerrainSize / 2f;
            for (int z = 0; z < GrassResolution; z++)
            for (int x = 0; x < GrassResolution; x++)
            {
                float u = (x + 0.5f) / GrassResolution, v = (z + 0.5f) / GrassResolution;
                float radiant = alpha[(int)(v * aRes), (int)(u * aRes), 0];
                if (radiant < 0.6f) continue;
                float wx = -half + u * TerrainSize, wz = -half + v * TerrainSize;
                float patch = Mathf.InverseLerp(0.4f, 0.75f, Mathf.PerlinNoise(patches.x + wx * 0.15f, patches.y + wz * 0.15f));
                bool inArena = Mathf.Abs(wx) < arena.halfWidth && Mathf.Abs(wz) < arena.pitHalfDepth + arena.sideDepth;
                int count = Mathf.FloorToInt(patch * radiant * GrassPerCell * (inArena ? GrassInArena : 1f) + (float)rng.NextDouble());
                for (int i = 0; i < count; i++) layers[rng.Next(layers.Length)][z, x]++;
            }
            for (int i = 0; i < layers.Length; i++) data.SetDetailLayer(0, 0, i, layers[i]);
        }

        /// <summary>
        /// World height of the ground: 0 on the arena's sides, the pit below them (|z| &lt; pitHalfDepth), and steep
        /// hills outside the arena (|x| &gt; halfWidth, |z| &gt; pitHalfDepth + sideDepth: where heroes cannot go).
        /// </summary>
        static float GroundHeight(Arena arena, float x, float z, Vector2 hills, Vector2 bumps)
        {
            float depth = arena.pitHalfDepth + arena.sideDepth;
            float dx = Mathf.Max(0f, Mathf.Abs(x) - arena.halfWidth), dz = Mathf.Max(0f, Mathf.Abs(z) - depth);
            if (dx <= 0f && dz <= 0f)
            {
                // how far into the pit, measured in wall widths (the pit ends where the side hills begin)
                float rim = Mathf.PerlinNoise(bumps.x + x * 0.45f, bumps.y + Mathf.Sign(z) * 7f) * PitRimJitter;
                float s = Mathf.Min((arena.pitHalfDepth - Mathf.Abs(z) - rim) / PitSlope, (arena.halfWidth - Mathf.Abs(x)) / PitEndSlope);
                if (s <= 0f) return 0f;
                s = Mathf.Clamp01(s);
                float ledges = (Mathf.PerlinNoise(hills.x + x * 0.5f, hills.y + z * 0.5f) - 0.5f) * 2f * PitLedges * Mathf.Sin(s * Mathf.PI) * s; // none at the rim
                return Mathf.Min(0f, -PitDepth * Mathf.SmoothStep(0f, 1f, s) + ledges);
            }
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            float y = CliffHeight * Mathf.SmoothStep(0f, 1f, d / CliffRun)
                      + RidgeHeight * Mathf.SmoothStep(0f, 1f, (d - CliffRun) / 14f);
            float noise = (Mathf.PerlinNoise(hills.x + x * 0.07f, hills.y + z * 0.07f) - 0.5f) * 5f
                          + (Mathf.PerlinNoise(bumps.x + x * 0.3f, bumps.y + z * 0.3f) - 0.5f) * 0.8f;
            y = Mathf.Max(0f, y + noise * Mathf.Clamp01(d / CliffRun));
            if (dz > 0f) y = Mathf.Min(y, BackSlope * dz + BackSideSlope * dx);
            return y;
        }

        internal static TerrainLayer Layer(string name, float tile)
        {
            var texturePath = $"{TerrainDir}/{name}.png";
            var ti = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D; // a new image may come in as a cubemap
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.sRGBTexture = true;
            ti.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null) Debug.LogError("[LandscapeBuilder] no 2D texture in " + texturePath);

            var path = $"{TerrainDir}/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = texture;
            layer.tileSize = new Vector2(tile, tile);
            layer.metallic = 0f;
            // matte: by default the smoothness comes from the texture's alpha, which is 1 in these images (wet look)
            layer.smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly;
            layer.smoothness = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        internal static Material TerrainMaterial()
        {
            const string path = TerrainDir + "/Terrain.mat";
            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // trees ////////////////////////////////////////////////////////////////

        /// <summary>
        /// A few trees along both side edges of each side, clear of the pit; the mango tree in the middle of the
        /// player side's left edge.
        /// </summary>
        static void PlaceTrees(Arena arena, Terrain terrain, System.Random rng)
        {
            var trees = new List<TreeSpot>();
            float zNear = arena.pitHalfDepth + 2.8f, zFar = arena.pitHalfDepth + arena.sideDepth - TreeInset;
            foreach (var side in new[] { -1f, 1f })
            {
                var kinds = side < 0f ? ArenaRadiantTrees : DireTrees;
                int n = 0;
                foreach (var edge in new[] { -1f, 1f })
                    for (int i = 0; i < TreesPerEdge; i++)
                    {
                        float z = Mathf.Lerp(zNear, zFar, i / (TreesPerEdge - 1f)) + Range(rng, -0.4f, 0.4f);
                        float x = edge * (arena.halfWidth - TreeInset + Range(rng, -0.25f, 0.25f));
                        bool mango = side < 0f && edge < 0f && i == TreesPerEdge / 2;
                        var model = mango ? MangoTree : kinds[n++ % kinds.Length];
                        trees.Add(new TreeSpot(model, new Vector2(x, side * z), mango ? 1f : Range(rng, 0.85f, 1.1f), rng));
                    }
            }
            SetTrees(terrain, trees);
        }

        /// <summary>A tree to plant on the terrain: model, world position on the ground plane, scale, random turn.</summary>
        internal readonly struct TreeSpot
        {
            public readonly string model;
            public readonly Vector2 at;
            public readonly float scale, turn;

            public TreeSpot(string model, Vector2 at, float scale, System.Random rng)
            {
                this.model = model;
                this.at = at;
                this.scale = scale;
                turn = Range(rng, 0f, 2f * Mathf.PI);
            }
        }

        /// <summary>
        /// The trees as the terrain's own: a prototype for each model (<see cref="TreePrefab"/>) and an instance for
        /// each tree, on the ground of the heightmap. The terrain draws them itself. A terrain whose trees can be cut
        /// down (<see cref="TerrainTrees"/>) gets each prototype's stump and falling top.
        /// </summary>
        internal static void SetTrees(Terrain terrain, IReadOnlyList<TreeSpot> trees)
        {
            var data = terrain.terrainData;
            var models = trees.Select(t => t.model).Distinct().ToList();
            data.treePrototypes = models.Select(m => new TreePrototype { prefab = TreePrefab(m) }).ToArray();
            if (terrain.TryGetComponent<TerrainTrees>(out var cutting))
            {
                cutting.stumps = models.Select(m => PartPrefab(m + StumpSuffix)).ToArray();
                cutting.fallingParts = models.Select(m => PartPrefab(m + FallingSuffix)).ToArray();
                EditorUtility.SetDirty(cutting);
            }
            var origin = terrain.transform.position;
            data.SetTreeInstances(trees.Select(t => new TreeInstance
            {
                prototypeIndex = models.IndexOf(t.model),
                position = new Vector3((t.at.x - origin.x) / data.size.x, 0f, (t.at.y - origin.z) / data.size.z),
                widthScale = t.scale,
                heightScale = t.scale,
                rotation = t.turn,
                color = Color.white,
                lightmapColor = Color.white,
            }).ToArray(), snapToHeightmap: true);
            terrain.drawTreesAndFoliage = true;
            EditorUtility.SetDirty(data);
        }

        /// <summary>
        /// The terrain's tree prefab of a model (Trees/Prefabs): its baked mesh (<see cref="Bake"/>) under an LOD group
        /// (a terrain tree turns and culls by it).
        /// </summary>
        static GameObject TreePrefab(string model)
        {
            var (mesh, materials) = Bake(TreeDir, model);
            var go = new GameObject(model);
            var lods = go.AddComponent<LODGroup>();
            lods.SetLODs(new[] { new LOD(TreeCullHeight, new Renderer[] { MeshChild(go, "LOD0", mesh, materials) }) });
            lods.RecalculateBounds();
            return SavePrefab(go, TreeDir, model);
        }

        /// <summary>A part of a cut tree (its stump or falling top) as a plain prefab; null if the model has none.</summary>
        static GameObject PartPrefab(string part)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{TreeDir}/{part}.fbx") == null) return null;
            var (mesh, materials) = Bake(TreeDir, part);
            var go = new GameObject(part);
            MeshChild(go, "Mesh", mesh, materials);
            return SavePrefab(go, TreeDir, part);
        }

        /// <summary>
        /// The terrain's detail prototypes of grass clumps (Grass/Prefabs: the baked mesh on the prefab's root, as a
        /// detail mesh wants it), drawn instanced with their own materials, a little bigger or smaller at random.
        /// </summary>
        internal static DetailPrototype[] GrassPrototypes(IEnumerable<string> models)
        {
            SetupModels(GrassDir, models);
            return models.Select(model =>
            {
                var (mesh, materials) = Bake(GrassDir, model);
                var go = new GameObject(model);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                var grass = new DetailPrototype
                {
                    prototype = SavePrefab(go, GrassDir, model),
                    usePrototypeMesh = true,
                    useInstancing = true,
                    renderMode = DetailRenderMode.VertexLit,
                    minWidth = 0.8f, maxWidth = 1.25f, minHeight = 0.8f, maxHeight = 1.3f,
                    noiseSpread = 0.4f,
                    healthyColor = Color.white, dryColor = new Color(0.9f, 0.9f, 0.8f),
                };
                if (!grass.Validate(out var error)) Debug.LogError($"[LandscapeBuilder] {model}: {error}");
                return grass;
            }).ToArray();
        }

        static Renderer MeshChild(GameObject parent, string name, Mesh mesh, Material[] materials)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return renderer;
        }

        static GameObject SavePrefab(GameObject go, string dir, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{dir}/Prefabs/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>
        /// A model's meshes baked into one (&lt;dir&gt;/Prefabs/&lt;model&gt;_mesh), with the FBX root's axis conversion,
        /// rotation and scale, one submesh per material; the mesh asset is overwritten in place.
        /// </summary>
        static (Mesh mesh, Material[] materials) Bake(string dir, string model)
        {
            Directory.CreateDirectory($"{dir}/Prefabs");
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{model}.fbx");
            var parts = new Dictionary<Material, List<CombineInstance>>();
            int vertices = 0;
            foreach (var filter in asset.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !filter.TryGetComponent<MeshRenderer>(out var source) || source.sharedMaterials.Length == 0) continue;
                var materials = source.sharedMaterials;
                var toRoot = Matrix4x4.Translate(-asset.transform.position) * filter.transform.localToWorldMatrix;
                vertices += mesh.vertexCount;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    var material = materials[Mathf.Min(i, materials.Length - 1)];
                    if (material == null) continue;
                    if (!parts.TryGetValue(material, out var list)) parts[material] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = i, transform = toRoot });
                }
            }
            var format = vertices > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            var byMaterial = parts.Values.Select(list =>
            {
                var part = new Mesh { indexFormat = format };
                part.CombineMeshes(list.ToArray(), mergeSubMeshes: true, useMatrices: true);
                return part;
            }).ToList();
            var baked = new Mesh { name = model, indexFormat = format };
            baked.CombineMeshes(byMaterial.Select(m => new CombineInstance { mesh = m, transform = Matrix4x4.identity }).ToArray(), mergeSubMeshes: false, useMatrices: false);
            baked.RecalculateBounds();
            foreach (var part in byMaterial) Object.DestroyImmediate(part);

            // overwritten in place: the prefabs and the terrains keep their references
            var meshPath = $"{dir}/Prefabs/{model}_mesh.asset";
            var stored = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (stored == null) AssetDatabase.CreateAsset(baked, meshPath);
            else
            {
                EditorUtility.CopySerialized(baked, stored);
                Object.DestroyImmediate(baked);
                baked = stored;
            }
            return (baked, parts.Keys.ToArray());
        }

        internal static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        /// <summary>Import settings of the tree models and textures, and their URP materials (masked leaves).</summary>
        internal static void SetupTreeAssets() =>
            SetupModels(TreeDir, RadiantTrees.Concat(PaleTrees).Concat(SnowTrees).Concat(DireTrees).Append(MangoTree)
                .SelectMany(m => new[] { m, m + StumpSuffix, m + FallingSuffix }));

        /// <summary>Import settings of the models in <paramref name="dir"/> and their textures, and their URP materials.</summary>
        static void SetupModels(string dir, IEnumerable<string> models)
        {
            var materials = new Dictionary<string, Material>();
            foreach (var colorPath in Directory.GetFiles($"{dir}/Textures", "*_color.png"))
            {
                var file = Path.GetFileName(colorPath);
                var name = file.Substring(0, file.Length - "_color.png".Length);
                var color = TreeTexture(colorPath.Replace('\\', '/'), normal: false);
                var normal = TreeTexture($"{dir}/Textures/{name}_normal.png", normal: true);
                // grass clumps are flat cards, seen from both sides
                materials[name] = TreeMaterial(dir, name, color, normal, twoSided: dir == GrassDir || TwoSidedMaterials.Contains(name));
            }
            AssetDatabase.SaveAssets();

            foreach (var model in models)
            {
                var path = $"{dir}/{model}.fbx";
                if (!File.Exists(path)) continue; // a tree Dota cannot cut down has no stump or falling top
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                mi.globalScale = 1f;
                mi.useFileScale = true;
                mi.bakeAxisConversion = true;
                // as the other models (PudgeAssetImporter): the file's root stays a plain root and the mesh node
                // keeps its own rotation, which placing the tree must not overwrite
                mi.preserveHierarchy = true;
                mi.importNormals = ModelImporterNormals.Import;
                mi.importTangents = ModelImporterTangents.CalculateMikk;
                mi.importAnimation = false;
                mi.importBlendShapes = false;
                mi.importCameras = false;
                mi.importLights = false;
                mi.animationType = ModelImporterAnimationType.None;
                mi.addCollider = false;
                mi.isReadable = true; // baked into the terrain's prefabs (Bake); the FBX itself is not in the build
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                // the FBX material slots are named after the Dota materials, as the textures are
                foreach (var slot in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name).ToList())
                    if (materials.TryGetValue(slot, out var mat))
                        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), mat);
                mi.SaveAndReimport();
            }
        }

        static Texture2D TreeTexture(string path, bool normal)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) return null;
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D; // a new image may come in as a cubemap
            ti.sRGBTexture = !normal;
            ti.alphaIsTransparency = false;
            ti.mipmapEnabled = true;
            // masked leaves keep their coverage in the smaller mips instead of thinning out with distance
            ti.mipMapsPreserveCoverage = !normal;
            ti.alphaTestReferenceValue = LeafCutoff;
            ti.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) Debug.LogError("[LandscapeBuilder] no 2D texture in " + path);
            return texture;
        }

        static Material TreeMaterial(string dir, string name, Texture2D color, Texture2D normal, bool twoSided)
        {
            Directory.CreateDirectory($"{dir}/Materials");
            var path = $"{dir}/Materials/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", color);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", normal);
            if (normal != null) mat.EnableKeyword("_NORMALMAP");
            else mat.DisableKeyword("_NORMALMAP");
            if (OpaqueMaterials.Contains(name))
            {
                mat.SetFloat("_AlphaClip", 0f);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.renderQueue = (int)RenderQueue.Geometry;
            }
            else
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", LeafCutoff);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                mat.renderQueue = (int)RenderQueue.AlphaTest;
            }
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.SetFloat("_Cull", twoSided ? 0f : 2f);
            mat.enableInstancing = true; // the terrain draws its trees instanced
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
