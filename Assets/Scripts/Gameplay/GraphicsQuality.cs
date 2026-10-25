using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PudgeWars.Gameplay
{
    public enum GraphicsPreset { Low, Medium, High, Ultra }

    /// <summary>
    /// Graphics presets of the main menu, remembered in PlayerPrefs and applied at start-up and on every scene load.
    /// Each platform has only one quality level (Mobile or PC), so a preset tunes a runtime copy of that level's URP
    /// asset (render scale, MSAA, shadow distance, resolution and cascades, HDR) — the asset itself stays untouched —
    /// plus texture mips, anisotropic filtering, LOD bias, the sun's shadows and the terrain's detail.
    /// In the editor the project's quality settings are restored when play mode ends.
    /// </summary>
    public static class GraphicsQuality
    {
        const string PresetKey = "graphics.preset";
        const string AntialiasingKey = "graphics.antialiasing";

        public static readonly string[] Names = { "Низкая", "Средняя", "Высокая", "Ультра" };

        public static GraphicsPreset Current { get; private set; }

        /// <summary>Off turns the preset's MSAA off; remembered apart from the preset.</summary>
        public static bool Antialiasing { get; private set; } = true;

        /// <summary>Phones start on Medium, computers on High.</summary>
        public static GraphicsPreset Default => Application.isMobilePlatform ? GraphicsPreset.Medium : GraphicsPreset.High;

        struct Settings
        {
            public float renderScale;
            public int msaa;
            public bool shadows, softShadows;
            public float shadowDistance;
            public int shadowResolution, cascades;
            public bool hdr;
            public int textureMipLimit; // 1 = half-size textures
            public AnisotropicFiltering anisotropic;
            public float lodBias;
            public float terrainPixelError;
            public float grassDensity; // of the terrain's details
        }

        static Settings For(GraphicsPreset preset) => preset switch
        {
            GraphicsPreset.Low => new Settings
            {
                renderScale = 0.65f, msaa = 1, shadows = false, shadowDistance = 0f, shadowResolution = 512, cascades = 1,
                hdr = false, textureMipLimit = 1, anisotropic = AnisotropicFiltering.Disable, lodBias = 0.7f, terrainPixelError = 8f, grassDensity = 0.3f,
            },
            GraphicsPreset.Medium => new Settings
            {
                renderScale = 0.85f, msaa = 1, shadows = true, shadowDistance = 35f, shadowResolution = 1024, cascades = 1,
                hdr = true, anisotropic = AnisotropicFiltering.Enable, lodBias = 1f, terrainPixelError = 5f, grassDensity = 0.6f,
            },
            GraphicsPreset.High => new Settings
            {
                renderScale = 1f, msaa = 2, shadows = true, softShadows = true, shadowDistance = 50f, shadowResolution = 2048, cascades = 2,
                hdr = true, anisotropic = AnisotropicFiltering.Enable, lodBias = 1.5f, terrainPixelError = 3f, grassDensity = 0.85f,
            },
            _ => new Settings
            {
                renderScale = 1f, msaa = 4, shadows = true, softShadows = true, shadowDistance = 70f, shadowResolution = 4096, cascades = 4,
                hdr = true, anisotropic = AnisotropicFiltering.ForceEnable, lodBias = 2f, terrainPixelError = 1f, grassDensity = 1f,
            },
        };

        /// <summary>One line for the menu: what the preset sets.</summary>
        public static string Describe(GraphicsPreset preset)
        {
            var s = For(preset);
            string shadows = !s.shadows ? "без теней" : s.softShadows ? "мягкие тени" : "жёсткие тени";
            string aa = s.msaa > 1 && Antialiasing ? $"сглаживание {s.msaa}×" : "без сглаживания";
            string textures = s.textureMipLimit > 0 ? "текстуры вполовину" : "полные текстуры";
            return $"Разрешение {Mathf.RoundToInt(s.renderScale * 100f)} %, {shadows}, {aa}, {textures}";
        }

        static UniversalRenderPipelineAsset source, copy;
        static RenderPipelineAsset originalPipeline;
        static int originalMipLimit;
        static AnisotropicFiltering originalAnisotropic;
        static float originalLodBias;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            originalPipeline = QualitySettings.renderPipeline;
            originalMipLimit = QualitySettings.globalTextureMipmapLimit;
            originalAnisotropic = QualitySettings.anisotropicFiltering;
            originalLodBias = QualitySettings.lodBias;
            // the editor keeps statics between play sessions (no domain reload): subscribe once
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
#if UNITY_EDITOR
            Application.quitting -= Restore;
            Application.quitting += Restore;
#endif
            Antialiasing = PlayerPrefs.GetInt(AntialiasingKey, 1) != 0;
            int saved = PlayerPrefs.GetInt(PresetKey, (int)Default);
            Apply((GraphicsPreset)Mathf.Clamp(saved, 0, Names.Length - 1));
        }

        /// <summary>Applies and remembers a preset.</summary>
        public static void Set(GraphicsPreset preset)
        {
            PlayerPrefs.SetInt(PresetKey, (int)preset);
            PlayerPrefs.Save();
            Apply(preset);
        }

        /// <summary>Turns antialiasing on or off over the current preset and remembers it.</summary>
        public static void SetAntialiasing(bool on)
        {
            PlayerPrefs.SetInt(AntialiasingKey, on ? 1 : 0);
            PlayerPrefs.Save();
            Antialiasing = on;
            Apply(Current);
        }

        static void Apply(GraphicsPreset preset)
        {
            Current = preset;
            var s = For(preset);
            var pipeline = Pipeline();
            if (pipeline != null)
            {
                pipeline.renderScale = s.renderScale;
                pipeline.msaaSampleCount = Antialiasing ? s.msaa : 1;
                pipeline.shadowDistance = s.shadowDistance;
                pipeline.mainLightShadowmapResolution = s.shadowResolution;
                pipeline.shadowCascadeCount = s.cascades;
                pipeline.supportsHDR = s.hdr;
            }
            QualitySettings.globalTextureMipmapLimit = s.textureMipLimit;
            QualitySettings.anisotropicFiltering = s.anisotropic;
            QualitySettings.lodBias = s.lodBias;
            ApplyToScene();
        }

        /// <summary>
        /// The quality level's URP asset, copied once and put in its place, so the presets never change the asset
        /// on disk.
        /// </summary>
        static UniversalRenderPipelineAsset Pipeline()
        {
            if (copy != null) return copy;
            source = (QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            if (source == null) return null;
            copy = Object.Instantiate(source);
            copy.name = source.name + " (preset)";
            copy.hideFlags = HideFlags.DontSave;
            QualitySettings.renderPipeline = copy;
            return copy;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyToScene();

        /// <summary>The sun's shadows, the terrain's detail and how thick its grass is in the loaded scenes.</summary>
        static void ApplyToScene()
        {
            var s = For(Current);
            foreach (var light in Object.FindObjectsByType<Light>())
                if (light.type == LightType.Directional)
                    light.shadows = !s.shadows ? LightShadows.None : s.softShadows ? LightShadows.Soft : LightShadows.Hard;
            foreach (var terrain in Terrain.activeTerrains)
            {
                terrain.heightmapPixelError = s.terrainPixelError;
                terrain.detailObjectDensity = s.grassDensity;
            }
        }

#if UNITY_EDITOR
        static void Restore()
        {
            QualitySettings.renderPipeline = originalPipeline;
            QualitySettings.globalTextureMipmapLimit = originalMipLimit;
            QualitySettings.anisotropicFiltering = originalAnisotropic;
            QualitySettings.lodBias = originalLodBias;
            if (copy != null) Object.DestroyImmediate(copy);
            copy = null;
        }
#endif
    }
}
