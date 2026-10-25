using System.Collections.Generic;
using System.IO;
using System.Linq;
using PudgeWars.Gameplay;
using PudgeWars.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PudgeWars.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/Menu.unity, the first scene of the game: Pudge in the middle of a clearing, a landscape of
    /// rolling hills with Dota 2's trees behind him, sky and fog, a slowly drifting camera, and the canvas of
    /// <see cref="MainMenu"/> (it builds its widgets at runtime). The landscape is Radiant's or Dire's, drawn half and
    /// half when the game starts (<see cref="MenuLandscape"/>): one terrain with a terrain data for each side, the
    /// same hills under Radiant's grass and trees or Dire's ground and trees, the trees being the terrain's own
    /// (<see cref="LandscapeBuilder.SetTrees"/>), decor only. Rebuilt as a whole by its menu item.
    /// </summary>
    public static class MenuSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Menu.unity";
        const string PudgePrefab = "Assets/Art/Characters/Pudge/Pudge.prefab";
        const string LatinFont = "Assets/Art/UI/Fonts/TrajanPro-Bold.otf";
        const string CyrillicFont = "Assets/Art/UI/Fonts/Кириллица_FrizQuadrata.ttf";
        const string SkyDir = "Assets/Art/Landscape/Sky";

        // terrain: Pudge stands at the origin facing the camera (+Z); the land stretches away behind him (-Z)
        const float TerrainWidth = 160f, TerrainLength = 160f, TerrainFront = 20f;
        const int HeightmapResolution = 257;
        const float TerrainBottom = -2f, TerrainHeight = 30f;
        const float ClearingRadius = 7f; // flat around Pudge
        const float HillHeight = 16f;    // the land rises this much toward the horizon

        const int TreeCount = 90;
        const float TreeSpacing = 3.2f;
        const float TreeNear = 6f, TreeFar = 70f;
        const int Seed = 2600;

        const float CameraFov = 35f;
        const float PudgeScreenShare = 0.55f; // of the screen height

        [MenuItem("Pudge Wars/Build Main Menu")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            LandscapeBuilder.SetupTreeAssets();
            var root = new GameObject("Landscape");
            var heights = Heights(new System.Random(Seed));
            var radiant = BuildTerrainData(MenuSide.Radiant, heights);
            var dire = BuildTerrainData(MenuSide.Dire, heights);
            var terrain = BuildTerrain(root.transform, radiant);
            PlaceTrees(terrain, radiant, MenuSide.Radiant);
            PlaceTrees(terrain, dire, MenuSide.Dire);
            var sun = BuildLighting();

            var landscape = root.AddComponent<MenuLandscape>();
            landscape.terrain = terrain;
            landscape.sun = sun;
            landscape.radiant = RadiantLook(radiant);
            landscape.dire = DireLook(dire);
            landscape.Show(MenuSide.Radiant); // as the scene is saved; at start-up it shows the side drawn for the run

            float height = PlacePudge();
            BuildCamera(height);
            BuildUi();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            ArenaSceneBuilder.AddToBuildSettings(ScenePath, first: true);
            AssetDatabase.SaveAssets();
            Debug.Log("[MenuSceneBuilder] saved " + ScenePath);
        }

        // landscape ////////////////////////////////////////////////////////////

        static Vector3 Origin => new(-TerrainWidth / 2f, TerrainBottom, TerrainFront - TerrainLength);

        /// <summary>The hills, the same for both sides.</summary>
        static float[,] Heights(System.Random rng)
        {
            var origin = Origin;
            var hills = new Vector2(rng.Next(1000), rng.Next(1000));
            var bumps = new Vector2(rng.Next(1000), rng.Next(1000));
            var heights = new float[HeightmapResolution, HeightmapResolution];
            for (int z = 0; z < HeightmapResolution; z++)
            for (int x = 0; x < HeightmapResolution; x++)
            {
                float wx = origin.x + x * TerrainWidth / (HeightmapResolution - 1);
                float wz = origin.z + z * TerrainLength / (HeightmapResolution - 1);
                heights[z, x] = (GroundHeight(wx, wz, hills, bumps) - TerrainBottom) / TerrainHeight;
            }
            return heights;
        }

        /// <summary>A side's terrain data (MenuTerrain_Radiant / _Dire): its ground, rock on the slopes.</summary>
        static TerrainData BuildTerrainData(MenuSide side, float[,] heights)
        {
            var data = new TerrainData { heightmapResolution = HeightmapResolution }; // resolution first: it resets the size
            data.size = new Vector3(TerrainWidth, TerrainHeight, TerrainLength);
            data.alphamapResolution = 256;
            data.baseMapResolution = 512;
            data.SetHeights(0, 0, heights);

            data.terrainLayers = new[]
            {
                side == MenuSide.Dire ? LandscapeBuilder.Layer("Ground_Dire", 6f) : LandscapeBuilder.Layer("Grass_Radiant", 6f),
                LandscapeBuilder.Layer("Cliff", 5f),
            };
            int aRes = data.alphamapResolution;
            var alpha = new float[aRes, aRes, 2];
            for (int z = 0; z < aRes; z++)
            for (int x = 0; x < aRes; x++)
            {
                float u = (x + 0.5f) / aRes, v = (z + 0.5f) / aRes;
                float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 45f, data.GetSteepness(u, v)));
                alpha[z, x, 0] = 1f - rock;
                alpha[z, x, 1] = rock;
            }
            data.SetAlphamaps(0, 0, alpha);

            var dataPath = $"{LandscapeBuilder.TerrainDir}/MenuTerrain_{side}.asset";
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);
            return data;
        }

        static Terrain BuildTerrain(Transform parent, TerrainData data)
        {
            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = Origin;
            Object.DestroyImmediate(go.GetComponent<TerrainCollider>()); // nothing walks here
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = LandscapeBuilder.TerrainMaterial();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 150f;
            terrain.shadowCastingMode = ShadowCastingMode.On;
            terrain.treeDistance = 200f;
            return terrain;
        }

        /// <summary>Flat around Pudge, then rolling hills that rise toward the horizon and toward the sides.</summary>
        static float GroundHeight(float x, float z, Vector2 hills, Vector2 bumps)
        {
            float d = new Vector2(x, z).magnitude;
            float open = Mathf.SmoothStep(0f, 1f, (d - ClearingRadius) / 12f); // 0 in the clearing
            if (z > 0f) open *= Mathf.Clamp01(1f - z / 8f); // and flat toward the camera
            float behind = Mathf.Clamp01(-z / TerrainLength);
            float rise = HillHeight * Mathf.SmoothStep(0f, 1f, behind * 1.6f) + 4f * Mathf.SmoothStep(0f, 1f, Mathf.Abs(x) / 70f);
            float noise = (Mathf.PerlinNoise(hills.x + x * 0.035f, hills.y + z * 0.035f) - 0.5f) * 8f
                          + (Mathf.PerlinNoise(bumps.x + x * 0.15f, bumps.y + z * 0.15f) - 0.5f) * 1.2f;
            return Mathf.Max(TerrainBottom + 0.5f, (rise + noise) * open);
        }

        /// <summary>
        /// A side's trees, scattered over the land behind and beside Pudge, kept apart; none right behind him close up
        /// (his outline stays clean) and none in front of the camera. Both sides plant them in the same places; on
        /// Radiant's the mango tree stands near him on the left.
        /// </summary>
        static void PlaceTrees(Terrain terrain, TerrainData data, MenuSide side)
        {
            var rng = new System.Random(Seed);
            var kinds = side == MenuSide.Dire ? LandscapeBuilder.DireTrees : LandscapeBuilder.RadiantTrees;
            int kind = 0;
            var near = new Vector2(-3f, -10f);
            var trees = new List<LandscapeBuilder.TreeSpot>
            {
                new(side == MenuSide.Dire ? kinds[kind++] : LandscapeBuilder.MangoTree, near, 1.1f, rng),
            };
            var placed = new List<Vector2> { near };
            for (int attempt = 0; attempt < TreeCount * 30 && placed.Count < TreeCount; attempt++)
            {
                // denser close to the clearing: the distance is skewed toward TreeNear
                float t = (float)rng.NextDouble();
                float distance = Mathf.Lerp(TreeNear, TreeFar, t * t);
                float angle = LandscapeBuilder.Range(rng, -100f, 100f) * Mathf.Deg2Rad; // 0 = straight behind Pudge
                var at = new Vector2(Mathf.Sin(angle) * distance, -Mathf.Cos(angle) * distance);
                if (at.y > 1f) continue;                                   // in front of the camera
                if (Mathf.Abs(at.x) < 4f && at.y > -12f) continue;         // right behind Pudge
                if (placed.Any(p => (p - at).sqrMagnitude < TreeSpacing * TreeSpacing)) continue;
                placed.Add(at);
                // farther trees a little bigger: the line of the forest reads against the sky
                float scale = LandscapeBuilder.Range(rng, 0.9f, 1.15f) * Mathf.Lerp(1f, 1.35f, t);
                trees.Add(new LandscapeBuilder.TreeSpot(kinds[kind++ % kinds.Length], at, scale, rng));
            }
            var shown = terrain.terrainData;
            terrain.terrainData = data;
            LandscapeBuilder.SetTrees(terrain, trees);
            terrain.terrainData = shown;
        }

        /// <summary>The sun and the fog; their colours, the ambient light and the sky come with the side's look.</summary>
        static Light BuildLighting()
        {
            // from behind the camera and a little to the right: Pudge is lit from the front, his shadow falls back
            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(35f, 210f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // the distant hills fade into the sky's haze
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 30f;
            RenderSettings.fogEndDistance = 160f;
            return sun;
        }

        /// <summary>Daylight: a clear blue sky, warm sun, bluish haze.</summary>
        static MenuLandscape.Look RadiantLook(TerrainData terrain) => new()
        {
            terrain = terrain,
            sky = Sky("Sky_Radiant", tint: new Color(0.5f, 0.5f, 0.5f), ground: new Color(0.37f, 0.35f, 0.35f), atmosphere: 1f, exposure: 1.3f),
            sun = new Color(1f, 0.95f, 0.86f),
            sunIntensity = 1.35f,
            ambientSky = new Color(0.58f, 0.63f, 0.72f),
            ambientEquator = new Color(0.42f, 0.43f, 0.40f),
            ambientGround = new Color(0.20f, 0.18f, 0.16f),
            fog = new Color(0.68f, 0.74f, 0.82f),
        };

        /// <summary>Dusk: a red, smoky sky, a low orange sun, dark reddish haze.</summary>
        static MenuLandscape.Look DireLook(TerrainData terrain) => new()
        {
            terrain = terrain,
            sky = Sky("Sky_Dire", tint: new Color(0.62f, 0.32f, 0.28f), ground: new Color(0.25f, 0.16f, 0.14f), atmosphere: 1.7f, exposure: 1.1f),
            sun = new Color(1f, 0.70f, 0.52f),
            sunIntensity = 1.15f,
            ambientSky = new Color(0.42f, 0.36f, 0.40f),
            ambientEquator = new Color(0.36f, 0.28f, 0.26f),
            ambientGround = new Color(0.15f, 0.11f, 0.10f),
            fog = new Color(0.45f, 0.34f, 0.33f),
        };

        /// <summary>A procedural sky material (Art/Landscape/Sky), updated in place if it exists.</summary>
        static Material Sky(string name, Color tint, Color ground, float atmosphere, float exposure)
        {
            Directory.CreateDirectory(SkyDir);
            var path = $"{SkyDir}/{name}.mat";
            var shader = Shader.Find("Skybox/Procedural");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_SkyTint", tint);
            mat.SetColor("_GroundColor", ground);
            mat.SetFloat("_AtmosphereThickness", atmosphere);
            mat.SetFloat("_Exposure", exposure);
            mat.SetFloat("_SunSize", 0.04f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Pudge at the origin facing the camera; returns his height.</summary>
        static float PlacePudge()
        {
            var root = new GameObject("Pudge");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PudgePrefab), root.transform);
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.rotation = Quaternion.Euler(0f, 8f, 0f); // turned a little toward the light
            root.AddComponent<MenuPudge>();
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 2f;
            var bounds = renderers.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            return Mathf.Max(1f, bounds.max.y);
        }

        /// <summary>In front of Pudge, a little above his middle, at the distance where he takes up PudgeScreenShare.</summary>
        static void BuildCamera(float pudgeHeight)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = go.AddComponent<Camera>();
            camera.fieldOfView = CameraFov;
            camera.nearClipPlane = 0.2f;
            camera.farClipPlane = 400f;
            camera.clearFlags = CameraClearFlags.Skybox;
            go.AddComponent<AudioListener>();

            float distance = pudgeHeight / PudgeScreenShare / (2f * Mathf.Tan(CameraFov * 0.5f * Mathf.Deg2Rad));
            var lookAt = new Vector3(0f, pudgeHeight * 0.5f, 0f);
            go.transform.position = new Vector3(0f, pudgeHeight * 0.62f, distance);
            go.transform.LookAt(lookAt);
            go.AddComponent<MenuCamera>().lookAt = lookAt;
        }

        static void BuildUi()
        {
            var canvasGo = new GameObject("Menu");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f; // the panels keep their height on wide phone screens
            canvasGo.AddComponent<GraphicRaycaster>();
            var fonts = canvasGo.AddComponent<HudFonts>();
            fonts.latin = AssetDatabase.LoadAssetAtPath<Font>(LatinFont);
            fonts.cyrillic = AssetDatabase.LoadAssetAtPath<Font>(CyrillicFont);
            canvasGo.AddComponent<MainMenu>().font = fonts.cyrillic != null ? fonts.cyrillic : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
