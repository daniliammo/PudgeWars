using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>What the landscape code (the editor's builders and the main menu at runtime) puts on a terrain data.</summary>
    public static class TerrainPaint
    {
        /// <summary>
        /// How the ground layers blend ([z, x, layer]), set and written into the alphamap textures themselves, so that
        /// a saved terrain data keeps it too (the editor marks the textures dirty).
        /// </summary>
        public static void WriteAlphamaps(TerrainData data, float[,,] alpha)
        {
            data.SetAlphamaps(0, 0, alpha);
            int res = alpha.GetLength(0), layers = alpha.GetLength(2);
            var textures = data.alphamapTextures;
            for (int t = 0; t < textures.Length; t++)
            {
                var pixels = new Color[res * res];
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float Channel(int c) => t * 4 + c < layers ? alpha[z, x, t * 4 + c] : 0f;
                    pixels[z * res + x] = new Color(Channel(0), Channel(1), Channel(2), Channel(3));
                }
                textures[t].SetPixels(pixels);
                textures[t].Apply(true);
            }
            data.SetBaseMapDirty();
        }

        /// <summary>A grass clump as a terrain detail: its prefab's mesh drawn instanced with its material, sizes vary.</summary>
        public static DetailPrototype Grass(GameObject prefab) => new()
        {
            prototype = prefab,
            usePrototypeMesh = true,
            useInstancing = true,
            renderMode = DetailRenderMode.VertexLit,
            minWidth = 0.8f, maxWidth = 1.25f, minHeight = 0.8f, maxHeight = 1.3f,
            noiseSpread = 0.4f,
            healthyColor = Color.white, dryColor = new Color(0.9f, 0.9f, 0.8f),
        };
    }
}
