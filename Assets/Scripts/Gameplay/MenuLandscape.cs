using System;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    public enum MenuSide { Radiant, Dire }

    /// <summary>
    /// The main menu's landscape is either Radiant's (grass, oaks and pines, a clear sky) or Dire's (scorched ground,
    /// Dire's trees, a dusky sky), half and half: the side is drawn once when the game starts and stays the same
    /// until it is restarted. The land itself is new each time the menu opens: hills, patches, the path, the trees and
    /// the grass come from a random seed (<see cref="MenuTerrain"/>) on a terrain data made at runtime; the scene keeps
    /// a saved one only for the editor. Each side has its sky, sun, ambient light and fog.
    /// </summary>
    public class MenuLandscape : MonoBehaviour
    {
        [Serializable]
        public class MenuTree
        {
            public GameObject prefab;
            [Tooltip("A relief with nothing behind it: turned to the camera instead of at random.")]
            public bool oneSided;
            [Tooltip("Which way its front looks in the prefab, yaw in degrees (0 = +Z).")]
            public float frontYaw;
        }

        [Serializable]
        public class Look
        {
            public TerrainLayer ground, patches, path;
            [Tooltip("Stands near Pudge on the left (Radiant's mango tree).")]
            public MenuTree landmark = new();
            public MenuTree[] trees = Array.Empty<MenuTree>();
            public GameObject[] grass = Array.Empty<GameObject>();
            public Material sky;
            public Color sun = Color.white;
            public float sunIntensity = 1f;
            public Color ambientSky = Color.gray, ambientEquator = Color.gray, ambientGround = Color.black;
            public Color fog = Color.gray;
        }

        public Terrain terrain;
        public Light sun;
        public TerrainLayer cliff;
        public Look radiant = new(), dire = new();

        static MenuSide? drawn;

        /// <summary>The side drawn for this run of the game (drawn on first use).</summary>
        public static MenuSide Side => drawn ??= UnityEngine.Random.value < 0.5f ? MenuSide.Radiant : MenuSide.Dire;

        /// <summary>The side shown now, by <see cref="Side"/> unless set otherwise (<see cref="Show"/>).</summary>
        public MenuSide Shown { get; private set; }
        /// <summary>The seed of the land shown now.</summary>
        public int Seed { get; private set; }

        TerrainData made; // at runtime, destroyed with the menu

        // play mode may start without a domain reload: a new run draws anew
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDraw() => drawn = null;

        void Awake() => Show(Side, UnityEngine.Random.Range(1, int.MaxValue));

        void OnDestroy()
        {
            if (made != null) Destroy(made);
        }

        public Look LookOf(MenuSide side) => side == MenuSide.Dire ? dire : radiant;

        /// <summary>The side's land for <paramref name="seed"/> on a new terrain data, and its sky and light.</summary>
        public void Show(MenuSide side, int seed)
        {
            var data = new TerrainData();
            MenuTerrain.Fill(data, LookOf(side), cliff, seed, CameraPosition);
            if (terrain != null) terrain.terrainData = data;
            if (made != null) Destroy(made);
            made = data;
            Seed = seed;
            ShowLight(side);
        }

        /// <summary>The side's sky, sun, ambient light and fog (the builder also shows them in the saved scene).</summary>
        public void ShowLight(MenuSide side)
        {
            Shown = side;
            var look = LookOf(side);
            if (look.sky != null) RenderSettings.skybox = look.sky;
            if (sun != null)
            {
                sun.color = look.sun;
                sun.intensity = look.sunIntensity;
            }
            RenderSettings.ambientSkyColor = look.ambientSky;
            RenderSettings.ambientEquatorColor = look.ambientEquator;
            RenderSettings.ambientGroundColor = look.ambientGround;
            RenderSettings.fogColor = look.fog;
        }

        static Vector3 CameraPosition
        {
            get
            {
                var cam = Camera.main;
                return cam != null ? cam.transform.position : new Vector3(0f, 1f, 6f);
            }
        }
    }
}
