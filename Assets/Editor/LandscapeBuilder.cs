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
    /// along its side edges: Radiant oaks and pines on the player's side, Dire trees on the bots' side, one mango tree.
    /// Heroes walk around the trees (<see cref="Obstacle"/>); hooks fly through them.
    /// Trees come from Tools/Landscape/build_trees.py, the ground textures from make_ground_textures.py. Rebuilt as a
    /// whole (the "Landscape" object and the terrain asset) by its menu item and by Build Test Arena; the old box
    /// geometry of the arena (ground blocks, walls, pit) is removed.
    /// </summary>
    public static class LandscapeBuilder
    {
        const string TreeDir = "Assets/Art/Landscape/Trees";
        const string TerrainDir = "Assets/Art/Landscape/Terrain";
        const string RootName = "Landscape";
        static readonly string[] RadiantTrees = { "tree_oak_01", "tree_pine_01", "tree_oak_02", "tree_pine_02", "tree_oak_01b", "tree_pine_03b" };
        static readonly string[] DireTrees = { "dire_tree004", "dire_tree007", "dire_tree004b", "dire_tree008" };
        const string MangoTree = "mango_tree";
        static readonly string[] TwoSidedMaterials = { "dire_tree007_moss" }; // doubleSided in the glTF
        const float LeafCutoff = 0.5f;

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
            EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);
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
            PlaceTrees(arena, terrain, root, rng);
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

            // Radiant's grass on the player's side (z < 0) and Dire's ground on the bots' side with a ragged border
            // at z = 0, rock on the steep slopes, black in the depth of the pit
            data.terrainLayers = new[]
            {
                Layer("Grass_Radiant", 6f), Layer("Ground_Dire", 6f), Layer("Cliff", 5f), Layer("Abyss", 5f),
            };
            int aRes = data.alphamapResolution;
            var alpha = new float[aRes, aRes, 4];
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
                alpha[z, x, 0] = ground * (1f - dire);
                alpha[z, x, 1] = ground * dire;
                alpha[z, x, 2] = (1f - abyss) * rock;
                alpha[z, x, 3] = abyss;
            }
            data.SetAlphamaps(0, 0, alpha);

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
            terrain.drawTreesAndFoliage = false;
            return terrain;
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

        static TerrainLayer Layer(string name, float tile)
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

        static Material TerrainMaterial()
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
        static void PlaceTrees(Arena arena, Terrain terrain, Transform parent, System.Random rng)
        {
            var root = new GameObject("Trees").transform;
            root.SetParent(parent, false);
            float zNear = arena.pitHalfDepth + 2.8f, zFar = arena.pitHalfDepth + arena.sideDepth - TreeInset;
            foreach (var side in new[] { -1f, 1f })
            {
                var kinds = side < 0f ? RadiantTrees : DireTrees;
                int n = 0;
                foreach (var edge in new[] { -1f, 1f })
                    for (int i = 0; i < TreesPerEdge; i++)
                    {
                        float z = Mathf.Lerp(zNear, zFar, i / (TreesPerEdge - 1f)) + Range(rng, -0.4f, 0.4f);
                        float x = edge * (arena.halfWidth - TreeInset + Range(rng, -0.25f, 0.25f));
                        bool mango = side < 0f && edge < 0f && i == TreesPerEdge / 2;
                        var model = mango ? MangoTree : kinds[n++ % kinds.Length];
                        Tree(model, new Vector2(x, side * z), mango ? 1f : Range(rng, 0.85f, 1.1f), terrain, root, rng);
                    }
            }
        }

        static void Tree(string model, Vector2 at, float scale, Terrain terrain, Transform parent, System.Random rng)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{TreeDir}/{model}.fbx");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            var position = new Vector3(at.x, 0f, at.y);
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
            // on top of the model's own root rotation and scale (the FBX's axis conversion may sit there)
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f) * asset.transform.localRotation);
            go.transform.localScale = asset.transform.localScale * scale;
            go.AddComponent<Obstacle>().radius = TreeRadius;
            foreach (var t in go.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic);
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        /// <summary>Import settings of the tree models and textures, and their URP materials (masked leaves).</summary>
        static void SetupTreeAssets()
        {
            var materials = new Dictionary<string, Material>();
            foreach (var colorPath in Directory.GetFiles($"{TreeDir}/Textures", "*_color.png"))
            {
                var file = Path.GetFileName(colorPath);
                var name = file.Substring(0, file.Length - "_color.png".Length);
                var color = TreeTexture(colorPath.Replace('\\', '/'), normal: false);
                var normal = TreeTexture($"{TreeDir}/Textures/{name}_normal.png", normal: true);
                materials[name] = TreeMaterial(name, color, normal);
            }
            AssetDatabase.SaveAssets();

            foreach (var model in RadiantTrees.Concat(DireTrees).Append(MangoTree))
            {
                var path = $"{TreeDir}/{model}.fbx";
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
                mi.isReadable = false;
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

        static Material TreeMaterial(string name, Texture2D color, Texture2D normal)
        {
            Directory.CreateDirectory($"{TreeDir}/Materials");
            var path = $"{TreeDir}/Materials/{name}.mat";
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
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", LeafCutoff);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.SetFloat("_Cull", TwoSidedMaterials.Contains(name) ? 0f : 2f);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
