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
    /// Landscape of the test arena: a terrain that is also the arena's ground (a hole over the pit, Radiant grass on
    /// the player's side, Dire's scorched ground on the bots' side, flat around the walls and rising into hills that
    /// hide the empty space beyond it) and Dota 2's standard trees outside the walls: Radiant oaks and pines on the
    /// player's side, Dire trees on the bots' side, one mango tree. The arena's ground blocks are hidden under it.
    /// Trees come from Tools/Landscape/build_trees.py, the ground textures from make_ground_textures.py. Rebuilt as a
    /// whole (the "Landscape" object and the terrain asset) by its menu item and by Build Test Arena.
    /// </summary>
    public static class LandscapeBuilder
    {
        const string TreeDir = "Assets/Art/Landscape/Trees";
        const string TerrainDir = "Assets/Art/Landscape/Terrain";
        const string RootName = "Landscape";
        static readonly string[] RadiantTrees = { "tree_oak_01", "tree_oak_01b", "tree_oak_02", "tree_pine_01", "tree_pine_02", "tree_pine_03b" };
        static readonly string[] DireTrees = { "dire_tree004", "dire_tree004b", "dire_tree007", "dire_tree008" };
        const string MangoTree = "mango_tree";
        static readonly string[] TwoSidedMaterials = { "dire_tree007_moss" }; // doubleSided in the glTF
        const float LeafCutoff = 0.5f;

        // 128 m with a 129 heightmap: 1 m cells, their edges on whole metres like the pit's, so the hole fits it
        const float TerrainSize = 128f;
        const int HeightmapResolution = 129;
        const float TerrainHeight = 20f;
        const float FlatMargin = 10f; // flat ground this far around the walls, metres (the trees stand there)
        const float HillHeight = 11f;
        const float GroundTile = 6f; // metres per tile of the ground textures

        const int TreesPerSide = 22;
        const float TreeSpacing = 2.6f;
        const float TreeBand = 11f;   // trees stand up to this far from the walls
        const float SideGap = 1.8f;   // kept clear outside the side walls: crowns must not hang over the heroes
        const float BackGap = 3f;     // and behind the back walls: the cameras look over them at the heroes
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
            var root = new GameObject(RootName).transform;
            var rng = new System.Random(Seed);
            var terrain = BuildTerrain(arena, root, rng);
            PlaceTrees(arena, terrain, root, rng);
            // the terrain is the ground now; the blocks stay for their pit faces, which the wall slabs cover anyway
            foreach (var name in new[] { "Ground_Player", "Ground_Bots" })
                if (arena.transform.Find(name) is { } block && block.TryGetComponent<MeshRenderer>(out var r)) r.enabled = false;
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
            float rampLength = half - OuterHalf(arena).x - FlatMargin;
            for (int z = 0; z < HeightmapResolution; z++)
            for (int x = 0; x < HeightmapResolution; x++)
            {
                float wx = -half + x * cell, wz = -half + z * cell;
                float d = DistanceOutside(arena, wx, wz) - FlatMargin;
                if (d <= 0f) continue;
                float rise = Mathf.SmoothStep(0f, 1f, d / rampLength);
                float hill = Mathf.PerlinNoise(hills.x + wx * 0.05f, hills.y + wz * 0.05f);
                float bump = Mathf.PerlinNoise(bumps.x + wx * 0.2f, bumps.y + wz * 0.2f);
                float y = rise * HillHeight * (0.5f + 0.8f * hill) + Mathf.Clamp01(d / 4f) * bump * 0.8f;
                heights[z, x] = y / TerrainHeight;
            }
            data.SetHeights(0, 0, heights);

            // a hole over the pit, of whole cells inside it
            int holesRes = data.holesResolution;
            var holes = new bool[holesRes, holesRes]; // false: a hole
            for (int z = 0; z < holesRes; z++)
            for (int x = 0; x < holesRes; x++)
            {
                float x0 = -half + x * cell, z0 = -half + z * cell;
                holes[z, x] = !(x0 >= -arena.halfWidth && x0 + cell <= arena.halfWidth && z0 >= -arena.pitHalfDepth && z0 + cell <= arena.pitHalfDepth);
            }
            data.SetHoles(0, 0, holes);

            // Radiant's grass on the player's side (z < 0), Dire's ground on the bots' side, a ragged border at z = 0
            data.terrainLayers = new[] { Layer("Grass_Radiant"), Layer("Ground_Dire") };
            int aRes = data.alphamapResolution;
            var alpha = new float[aRes, aRes, 2];
            for (int z = 0; z < aRes; z++)
            for (int x = 0; x < aRes; x++)
            {
                float wx = -half + (x + 0.5f) * TerrainSize / aRes, wz = -half + (z + 0.5f) * TerrainSize / aRes;
                float ragged = (Mathf.PerlinNoise(border.x + wx * 0.15f, border.y + wz * 0.15f) - 0.5f) * 5f;
                float dire = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1.5f, 1.5f, wz + ragged));
                alpha[z, x, 0] = 1f - dire;
                alpha[z, x, 1] = dire;
            }
            data.SetAlphamaps(0, 0, alpha);

            const string dataPath = TerrainDir + "/ArenaTerrain.asset";
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(-half, 0f, -half); // the flat ground at y = 0, level with the arena
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = TerrainMaterial();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 200f;
            terrain.shadowCastingMode = ShadowCastingMode.Off; // gentle hills: nothing worth a shadow, cheaper on phones
            terrain.drawTreesAndFoliage = false;
            return terrain;
        }

        /// <summary>Half sizes of the arena with its walls, metres.</summary>
        static Vector2 OuterHalf(Arena arena) => new(arena.halfWidth + 0.5f, arena.pitHalfDepth + arena.sideDepth + 0.5f);

        /// <summary>Distance from the arena with its walls on the ground plane; 0 inside it.</summary>
        static float DistanceOutside(Arena arena, float x, float z)
        {
            var outer = OuterHalf(arena);
            float dx = Mathf.Max(0f, Mathf.Abs(x) - outer.x), dz = Mathf.Max(0f, Mathf.Abs(z) - outer.y);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static TerrainLayer Layer(string name)
        {
            var texturePath = $"{TerrainDir}/{name}.png";
            var ti = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.sRGBTexture = true;
            ti.SaveAndReimport();

            var path = $"{TerrainDir}/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            layer.tileSize = new Vector2(GroundTile, GroundTile);
            layer.metallic = 0f;
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

        static void PlaceTrees(Arena arena, Terrain terrain, Transform parent, System.Random rng)
        {
            var root = new GameObject("Trees").transform;
            root.SetParent(parent, false);
            var outer = OuterHalf(arena);
            var placed = new List<Vector2>();

            // the mango tree: on the player's side, left of the arena, in view of the own side's camera
            var mango = new Vector2(-(outer.x + SideGap + 1.5f), -(arena.pitHalfDepth + 4f));
            Tree(MangoTree, mango, 1f, terrain, root, rng);
            placed.Add(mango);

            foreach (var side in new[] { -1f, 1f })
            {
                var kinds = side < 0f ? RadiantTrees : DireTrees;
                int count = 0;
                for (int attempt = 0; attempt < 5000 && count < TreesPerSide; attempt++)
                {
                    var p = new Vector2(Range(rng, -outer.x - TreeBand, outer.x + TreeBand), side * Range(rng, 0.5f, outer.y + TreeBand));
                    if (Mathf.Abs(p.x) < outer.x + SideGap && Mathf.Abs(p.y) < outer.y + BackGap) continue; // the arena and its clear strip
                    if (DistanceOutside(arena, p.x, p.y) > TreeBand) continue;
                    if (placed.Any(q => (q - p).sqrMagnitude < TreeSpacing * TreeSpacing)) continue;
                    placed.Add(p);
                    Tree(kinds[count % kinds.Length], p, Range(rng, 0.85f, 1.15f), terrain, root, rng);
                    count++;
                }
            }
        }

        static void Tree(string model, Vector2 at, float scale, Terrain terrain, Transform parent, System.Random rng)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{TreeDir}/{model}.fbx");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            var position = new Vector3(at.x, 0f, at.y);
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f));
            go.transform.localScale = Vector3.one * scale;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic);
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
            ti.sRGBTexture = !normal;
            ti.alphaIsTransparency = false;
            ti.mipmapEnabled = true;
            // masked leaves keep their coverage in the smaller mips instead of thinning out with distance
            ti.mipMapsPreserveCoverage = !normal;
            ti.alphaTestReferenceValue = LeafCutoff;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
