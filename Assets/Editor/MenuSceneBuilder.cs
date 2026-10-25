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
    /// half when the game starts, and a new land each time the menu opens (<see cref="MenuLandscape"/>,
    /// <see cref="MenuTerrain"/>): here only what each side is made of (ground layers, trees, grass, sky and light) and a
    /// land saved for the editor's view. Rebuilt as a whole by its menu item.
    /// </summary>
    public static class MenuSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Menu.unity";
        const string PudgePrefab = "Assets/Art/Characters/Pudge/Pudge.prefab";
        const string LatinFont = "Assets/Art/UI/Fonts/TrajanPro-Bold.otf";
        const string CyrillicFont = "Assets/Art/UI/Fonts/Кириллица_FrizQuadrata.ttf";
        const string SkyDir = "Assets/Art/Landscape/Sky";

        const string PreviewPath = LandscapeBuilder.TerrainDir + "/MenuTerrain_Preview.asset";
        const int PreviewSeed = 2600; // the land saved for the editor; the game draws a new one each time

        const float CameraFov = 35f;
        const float PudgeScreenShare = 0.55f; // of the screen height

        [MenuItem("Pudge Wars/Build Main Menu")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return; // the open scene's changes are not lost
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // every asset first: an import while a terrain data is filled loses what it holds (LandscapeBuilder.PrepareAssets)
            LandscapeBuilder.PrepareAssets();
            var radiantSky = Sky("Sky_Radiant", tint: new Color(0.5f, 0.5f, 0.5f), ground: new Color(0.37f, 0.35f, 0.35f), atmosphere: 1f, exposure: 1.3f);
            var direSky = Sky("Sky_Dire", tint: new Color(0.62f, 0.32f, 0.28f), ground: new Color(0.25f, 0.16f, 0.14f), atmosphere: 1.7f, exposure: 1.1f);
            foreach (var old in new[] { "MenuTerrain_Radiant", "MenuTerrain_Dire" }) // the lands saved before they were drawn at runtime
                AssetDatabase.DeleteAsset($"{LandscapeBuilder.TerrainDir}/{old}.asset");
            AssetDatabase.SaveAssets();

            float height = PlacePudge();
            var camera = BuildCamera(height); // before the land: its one-sided trees turn to the camera
            var root = new GameObject("Landscape");
            var landscape = root.AddComponent<MenuLandscape>();
            landscape.sun = BuildLighting();
            landscape.cliff = LandscapeBuilder.Layer("Cliff", 5f);
            landscape.radiant = RadiantLook(radiantSky);
            landscape.dire = DireLook(direSky);
            landscape.terrain = BuildTerrain(root.transform);

            // the editor's view: Radiant's land for one seed, saved first and filled after (a terrain data filled before
            // it is saved loses its alphamaps); at start-up the menu draws its side and a new land
            var preview = new TerrainData();
            LandscapeBuilder.SaveTerrainData(preview, PreviewPath);
            MenuTerrain.Fill(preview, landscape.radiant, landscape.cliff, PreviewSeed, camera.position);
            LandscapeBuilder.MarkTerrainDirty(preview);
            AssetDatabase.SaveAssets();
            landscape.terrain.terrainData = preview;
            landscape.ShowLight(MenuSide.Radiant);
            BuildUi();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            ArenaSceneBuilder.AddToBuildSettings(ScenePath, first: true);
            AssetDatabase.SaveAssets();
            Debug.Log("[MenuSceneBuilder] saved " + ScenePath);
        }

        // landscape ////////////////////////////////////////////////////////////

        /// <summary>The terrain at MenuTerrain.Origin; its data comes from MenuLandscape (a new land each time).</summary>
        static Terrain BuildTerrain(Transform parent)
        {
            var go = new GameObject("Terrain");
            go.transform.SetParent(parent, false);
            go.transform.position = MenuTerrain.Origin;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            var terrain = go.AddComponent<Terrain>(); // no collider: nothing walks here
            terrain.materialTemplate = LandscapeBuilder.TerrainMaterial();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 150f;
            terrain.shadowCastingMode = ShadowCastingMode.On;
            terrain.treeDistance = 200f;
            terrain.detailObjectDistance = MenuTerrain.GrassDistance;
            return terrain;
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

        /// <summary>Daylight over Radiant's grass with flowery patches and a stone path, its trees and green grass: a clear
        /// blue sky, warm sun, bluish haze.</summary>
        static MenuLandscape.Look RadiantLook(Material sky) => new()
        {
            ground = LandscapeBuilder.Layer("Radiant_Grass", 5f),
            patches = LandscapeBuilder.Layer("Radiant_Flowers", 5f),
            path = LandscapeBuilder.Layer("Radiant_Stones", 2.5f),
            landmark = LandscapeBuilder.MenuTreeOf(LandscapeBuilder.MangoTree),
            trees = LandscapeBuilder.RadiantTrees.Select(LandscapeBuilder.MenuTreeOf).ToArray(),
            grass = LandscapeBuilder.GrassPrefabs(LandscapeBuilder.Grass),
            sky = sky,
            sun = new Color(1f, 0.95f, 0.86f),
            sunIntensity = 1.35f,
            ambientSky = new Color(0.58f, 0.63f, 0.72f),
            ambientEquator = new Color(0.42f, 0.43f, 0.40f),
            ambientGround = new Color(0.20f, 0.18f, 0.16f),
            fog = new Color(0.68f, 0.74f, 0.82f),
        };

        /// <summary>Dusk over Dire's cracked ground with patchy dirt and a sand path, its trees, no grass: a red, smoky
        /// sky, a low orange sun, dark reddish haze.</summary>
        static MenuLandscape.Look DireLook(Material sky) => new()
        {
            ground = LandscapeBuilder.Layer("Dire_Ground", 6f),
            patches = LandscapeBuilder.Layer("Dire_Patches", 6f),
            path = LandscapeBuilder.Layer("Path_Sand", 3f),
            landmark = LandscapeBuilder.MenuTreeOf(LandscapeBuilder.DireTrees[0]),
            trees = LandscapeBuilder.DireTrees.Select(LandscapeBuilder.MenuTreeOf).ToArray(),
            sky = sky,
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

        /// <summary>How far in front of Pudge the camera stands: there he takes up PudgeScreenShare of the screen.</summary>
        static float CameraDistance(float pudgeHeight) => pudgeHeight / PudgeScreenShare / (2f * Mathf.Tan(CameraFov * 0.5f * Mathf.Deg2Rad));

        /// <summary>In front of Pudge, a little above his middle, at the distance where he takes up PudgeScreenShare.</summary>
        static Transform BuildCamera(float pudgeHeight)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = go.AddComponent<Camera>();
            camera.fieldOfView = CameraFov;
            camera.nearClipPlane = 0.2f;
            camera.farClipPlane = 400f;
            camera.clearFlags = CameraClearFlags.Skybox;
            go.AddComponent<AudioListener>();

            float distance = CameraDistance(pudgeHeight);
            var lookAt = new Vector3(0f, pudgeHeight * 0.5f, 0f);
            go.transform.position = new Vector3(0f, pudgeHeight * 0.62f, distance);
            go.transform.LookAt(lookAt);
            go.AddComponent<MenuCamera>().lookAt = lookAt;
            return go.transform;
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
