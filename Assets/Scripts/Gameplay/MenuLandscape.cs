using System;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    public enum MenuSide { Radiant, Dire }

    /// <summary>
    /// The main menu's landscape is either Radiant's (grass, oaks and pines, a clear sky) or Dire's (scorched ground,
    /// Dire's trees, a dusky sky), half and half: the side is drawn once when the game starts and stays the same
    /// until it is restarted. Each side is a terrain data of its own (ground layers and the terrain's trees) on the
    /// same terrain, with its sky, sun, ambient light and fog.
    /// </summary>
    public class MenuLandscape : MonoBehaviour
    {
        [Serializable]
        public class Look
        {
            public TerrainData terrain;
            public Material sky;
            public Color sun = Color.white;
            public float sunIntensity = 1f;
            public Color ambientSky = Color.gray, ambientEquator = Color.gray, ambientGround = Color.black;
            public Color fog = Color.gray;
        }

        public Terrain terrain;
        public Light sun;
        public Look radiant = new(), dire = new();

        static MenuSide? drawn;

        /// <summary>The side drawn for this run of the game (drawn on first use).</summary>
        public static MenuSide Side => drawn ??= UnityEngine.Random.value < 0.5f ? MenuSide.Radiant : MenuSide.Dire;

        /// <summary>The side shown now, by <see cref="Side"/> unless set otherwise (<see cref="Show"/>).</summary>
        public MenuSide Shown { get; private set; }

        // play mode may start without a domain reload: a new run draws anew
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetDraw() => drawn = null;

        void Awake() => Show(Side);

        public void Show(MenuSide side)
        {
            Shown = side;
            var look = side == MenuSide.Dire ? dire : radiant;
            if (terrain != null && look.terrain != null) terrain.terrainData = look.terrain;
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
    }
}
