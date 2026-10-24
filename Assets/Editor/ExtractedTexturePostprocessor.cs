using UnityEditor;

namespace PudgeWars.EditorTools
{
    /// <summary>
    /// First-import settings for textures dumped from the original game into Art/Extracted
    /// (Tools/AssetExtraction, TEXTURES_ONLY): UI art becomes sprites, *_normal / *_N become normal maps.
    /// Only applied when the texture has no .meta yet, so later manual changes stay.
    /// </summary>
    public class ExtractedTexturePostprocessor : AssetPostprocessor
    {
        const string Root = "Assets/Art/Extracted/";

        // 3D art (meshes, terrain, FX, menu pedestal); everything else in Extracted is interface
        static readonly string[] WorldFolders =
        {
            "Assets/FX/", "Assets/Landscape/", "Assets/Mirana/", "Assets/Treant/", "Map/", "Menu/Pudge_Menu/Pedestal/",
        };

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root) || !assetImporter.importSettingsMissing) return;
            var ti = (TextureImporter)assetImporter;
            var rel = assetPath.Substring(Root.Length);
            var name = System.IO.Path.GetFileNameWithoutExtension(rel);

            if (name.ToLowerInvariant().Contains("normal") || name.EndsWith("_N"))
            {
                ti.textureType = TextureImporterType.NormalMap;
                return;
            }
            foreach (var folder in WorldFolders)
                if (rel.StartsWith(folder)) return;

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
        }
    }
}
