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
    /// rolling hills with Dota 2's trees behind him (Radiant grass and trees on the left, Dire's on the right, as on
    /// the arena), sky and fog, a slowly drifting camera, and the canvas of <see cref="MainMenu"/> (it builds its
    /// widgets at runtime). The trees are decor only: no obstacles. Rebuilt as a whole by its menu item.
    /// </summary>
    public static class MenuSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Menu.unity";
        const string PudgePrefab = "Assets/Art/Characters/Pudge/Pudge.prefab";
        const string LatinFont = "Assets/Art/UI/Fonts/TrajanPro-Bold.otf";
        const string CyrillicFont = "Assets/Art/UI/Fonts/Кириллица_FrizQuadrata.ttf";

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
            var rng = new System.Random(Seed);
            var border = new Vector2(rng.Next(1000), rng.Next(1000)); // of Radiant's and Dire's ground, for the trees too
            var root = new GameObject("Landscape").transform;
            var terrain = BuildTerrain(root, rng, border);
            PlaceTrees(terrain, root, rng, border);
            BuildLighting();
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

        static Terrain BuildTerrain(Transform parent, System.Random rng, Vector2 border)
        {
            var data = new TerrainData { heightmapResolution = HeightmapResolution }; // resolution first: it resets the size
            data.size = new Vector3(TerrainWidth, TerrainHeight, TerrainLength);
            data.alphamapResolution = 256;
            data.baseMapResolution = 512;
            var origin = new Vector3(-TerrainWidth / 2f, TerrainBottom, TerrainFront - TerrainLength);
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
            data.SetHeights(0, 0, heights);

            // Radiant's grass on the left (x < 0), Dire's ground on the right with a ragged border, rock on the slopes
            data.terrainLayers = new[]
            {
                LandscapeBuilder.Layer("Grass_Radiant", 6f), LandscapeBuilder.Layer("Ground_Dire", 6f), LandscapeBuilder.Layer("Cliff", 5f),
            };
            int aRes = data.alphamapResolution;
            var alpha = new float[aRes, aRes, 3];
            for (int z = 0; z < aRes; z++)
            for (int x = 0; x < aRes; x++)
            {
                float u = (x + 0.5f) / aRes, v = (z + 0.5f) / aRes;
                float wx = origin.x + u * TerrainWidth, wz = origin.z + v * TerrainLength;
                float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 45f, data.GetSteepness(u, v)));
                float dire = DireShare(wx, wz, border);
                alpha[z, x, 0] = (1f - rock) * (1f - dire);
                alpha[z, x, 1] = (1f - rock) * dire;
                alpha[z, x, 2] = rock;
            }
            data.SetAlphamaps(0, 0, alpha);

            const string dataPath = LandscapeBuilder.TerrainDir + "/MenuTerrain.asset";
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = LandscapeBuilder.TerrainMaterial();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 150f;
            terrain.shadowCastingMode = ShadowCastingMode.On;
            terrain.drawTreesAndFoliage = false;
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

        /// <summary>0 on Radiant's left, 1 on Dire's right, with a ragged border through the middle.</summary>
        static float DireShare(float x, float z, Vector2 border)
        {
            float ragged = (Mathf.PerlinNoise(border.x + x * 0.08f, border.y + z * 0.08f) - 0.5f) * 10f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2f, 2f, x + ragged));
        }

        /// <summary>
        /// Trees scattered over the land behind and beside Pudge, kept apart; none right behind him close up (his
        /// outline stays clean) and none in front of the camera. The mango tree stands near him on the left.
        /// </summary>
        static void PlaceTrees(Terrain terrain, Transform parent, System.Random rng, Vector2 border)
        {
            var root = new GameObject("Trees").transform;
            root.SetParent(parent, false);
            var placed = new List<Vector2> { new(-3f, -10f) };
            LandscapeBuilder.Tree(LandscapeBuilder.MangoTree, placed[0], 1.1f, terrain, root, rng, obstacle: false);

            int radiant = 0, dire = 0;
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
                bool isDire = DireShare(at.x, at.y, border) > 0.5f;
                var kinds = isDire ? LandscapeBuilder.DireTrees : LandscapeBuilder.RadiantTrees;
                var model = kinds[(isDire ? dire++ : radiant++) % kinds.Length];
                // farther trees a little bigger: the line of the forest reads against the sky
                float scale = LandscapeBuilder.Range(rng, 0.9f, 1.15f) * Mathf.Lerp(1f, 1.35f, t);
                LandscapeBuilder.Tree(model, at, scale, terrain, root, rng, obstacle: false);
            }
        }

        static void BuildLighting()
        {
            // from behind the camera and a little to the right: Pudge is lit from the front, his shadow falls back
            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.35f;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(35f, 210f, 0f);
            RenderSettings.sun = sun;

            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.63f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.43f, 0.40f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.18f, 0.16f);
            // the distant hills fade into the sky's haze
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 30f;
            RenderSettings.fogEndDistance = 160f;
            RenderSettings.fogColor = new Color(0.68f, 0.74f, 0.82f);
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
