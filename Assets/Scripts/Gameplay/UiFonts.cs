using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace PudgeWars.Gameplay
{
    /// <summary>Shared dynamic SDF atlases and outline materials; glyphs stay sharp without duplicated geometry.</summary>
    public static class UiFonts
    {
        static UiFontLibrary library;
        static readonly Dictionary<Font, TMP_FontAsset> fonts = new();
        static readonly Dictionary<(TMP_FontAsset, Color32, int, int), Material> materials = new();
        static UiFontLibrary Library => library != null ? library : library = Resources.Load<UiFontLibrary>("Text/UiFonts");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            Application.quitting -= Clear;
            Application.quitting += Clear;
            ResetLoadedLabels();
        }

        /// <summary>Rebind surviving scene objects when entering/leaving Play Mode without a scene/domain reload.</summary>
        public static void ResetLoadedLabels()
        {
            Clear();
            foreach (var label in Object.FindObjectsByType<UiText>(FindObjectsInactive.Include))
                label.font = label.font;
        }

        public static TMP_FontAsset For(Font source)
        {
            var settings = Library;
            if (settings == null) return TMP_Settings.defaultFontAsset;
            // The boot screen used the built-in raster font; use the bundled game face for its SDF rendering.
            if (source == null || source.name == "LegacyRuntime" || source.name == "Arial") source = settings.cyrillic;
            if (source == null) return TMP_Settings.defaultFontAsset;
            if (fonts.TryGetValue(source, out var existing) && existing != null)
            {
                if (existing.material != null && existing.atlasTextures.Length > 0 && existing.atlasTextures[0] != null)
                    return existing;
                // An editor transition can destroy a temporary atlas while the managed font cache survives.
                fonts.Remove(source);
                Destroy(existing);
            }
            var asset = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            if (asset == null) return TMP_Settings.defaultFontAsset;
            asset.name = source.name + " SDF (runtime)";
            asset.hideFlags = HideFlags.HideAndDontSave;
            asset.material.hideFlags = HideFlags.HideAndDontSave;
            foreach (var atlas in asset.atlasTextures) atlas.hideFlags = HideFlags.HideAndDontSave;
            fonts[source] = asset;
            asset.fallbackFontAssetTable = new List<TMP_FontAsset>();
            if (source == settings.latin && settings.cyrillic != source)
                asset.fallbackFontAssetTable.Add(For(settings.cyrillic));
            if (TMP_Settings.defaultFontAsset != null) asset.fallbackFontAssetTable.Add(TMP_Settings.defaultFontAsset);
            return asset;
        }

        public static Material Style(TMP_FontAsset font, float fontSize, Color outline, float width, float bold)
        {
            if (font == null) return null;
            float scale = Mathf.Max(1f, fontSize) / font.faceInfo.pointSize;
            float gradient = font.atlasPadding + 1f;
            // SDF shader widths are fractions of atlas padding, not canvas pixels.
            int edge = Mathf.RoundToInt(Mathf.Clamp01(width / (gradient * scale)) * 1000f);
            int weight = Mathf.RoundToInt(Mathf.Clamp(bold / (gradient * scale), 0f, 0.5f) * 1000f);
            edge = Mathf.Min(edge, 1000 - weight);
            var key = (font, (Color32)outline, edge, weight);
            if (materials.TryGetValue(key, out var existing) && existing != null) return existing;
            var material = new Material(font.material) { name = font.name + " UI style", hideFlags = HideFlags.HideAndDontSave };
            material.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
            material.SetColor(ShaderUtilities.ID_OutlineColor, outline);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, edge / 1000f);
            // TMP centers the outline on the glyph contour. Expand the face by the same amount so
            // the black half of the outline cannot eat the colored strokes, especially at small sizes.
            material.SetFloat(ShaderUtilities.ID_FaceDilate, (edge + weight) / 1000f);
            ShaderUtilities.UpdateShaderRatios(material);
            if (edge > 0) material.EnableKeyword("OUTLINE_ON");
            else material.DisableKeyword("OUTLINE_ON");
            materials[key] = material;
            return material;
        }

        /// <summary>Prepare the common alphabet while the loading screen is up, rather than during a first popup.</summary>
        internal static void PrepareCommonGlyphs()
        {
            var settings = Library;
            if (settings == null) return;
            const string characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 +-/%.,:!?()[]×–—…«»" +
                "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя";
            For(settings.cyrillic)?.TryAddCharacters(characters);
            For(settings.latin)?.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 +-/%.,:");
        }

        public static void Clear()
        {
            foreach (var material in materials.Values) Destroy(material);
            materials.Clear();
            foreach (var font in fonts.Values)
            {
                if (font == null) continue;
                // TMP_FontAsset.OnDestroy owns its atlas textures and base material.
                Destroy(font);
            }
            fonts.Clear();
        }

        static void Destroy(Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Object.Destroy(asset);
            else Object.DestroyImmediate(asset);
        }
    }
}
