using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The main menu's landscape, made from a seed (<see cref="MenuLandscape"/> draws a new one each time the menu
    /// opens): a flat clearing for Pudge at the origin (he faces the camera, +Z), rolling hills rising behind him (-Z),
    /// the side's ground with patches, rock on the slopes, a road from the camera to a round place under Pudge, branching into winding forest trails
    /// behind him, the side's trees scattered over the land (none right behind him, none in front of the camera),
    /// and the side's grass in patches. The terrain stands at <see cref="Origin"/>.
    /// </summary>
    public static class MenuTerrain
    {
        public const float Width = 160f, Length = 160f, Front = 20f, Bottom = -2f, Height = 30f;
        public const float GrassDistance = 60f; // the terrain draws grass this far
        const int HeightmapResolution = 257;
        const int AlphamapResolution = 512;     // 0.31 m: the path keeps its edges
        const int GrassResolution = 256;        // detail cells: 0.625 m
        const float ClearingRadius = 7f;        // flat around Pudge
        const int TreeCount = 420;
        const float TreeSpacing = 2.4f, TreeNear = 6f, TreeFar = 75f;
        const float GrassNear = 2.5f, GrassFar = 45f, GrassPerCell = 2.2f;

        public static Vector3 Origin => new(-Width / 2f, Bottom, Front - Length);

        /// <summary>What a seed varies: the hills, the patches, the path's bends.</summary>
        struct Shape
        {
            public Vector2 hills, bumps, patches, grass;
            public float hillHeight, hillScale;
            public float bend, bendRate, bendPhase;
            public PathSegment[] paths;
        }

        struct PathSegment
        {
            public Vector2 from, to;
            public float halfWidth;
        }

        /// <summary>Fills <paramref name="data"/> (new, or a saved asset) with the side's landscape for <paramref name="seed"/>.</summary>
        public static void Fill(TerrainData data, MenuLandscape.Look look, TerrainLayer cliff, int seed, Vector3 camera)
        {
            var rng = new System.Random(seed);
            var shape = new Shape
            {
                hills = new Vector2(rng.Next(1000), rng.Next(1000)),
                bumps = new Vector2(rng.Next(1000), rng.Next(1000)),
                patches = new Vector2(rng.Next(1000), rng.Next(1000)),
                grass = new Vector2(rng.Next(1000), rng.Next(1000)),
                hillHeight = Range(rng, 11f, 20f),
                hillScale = Range(rng, 0.028f, 0.045f),
                bend = Range(rng, 2.5f, 5f) * (rng.Next(2) == 0 ? -1f : 1f),
                bendRate = Range(rng, 0.055f, 0.09f),
                bendPhase = Range(rng, 0f, Mathf.PI),
            };

            shape.paths = Paths(shape, rng);

            data.heightmapResolution = HeightmapResolution; // resolution first: it resets the size
            data.size = new Vector3(Width, Height, Length);
            data.alphamapResolution = AlphamapResolution;
            data.baseMapResolution = 512;
            data.SetHeights(0, 0, Heights(shape));
            data.terrainLayers = new[] { look.ground, look.patches, cliff, look.path };
            TerrainPaint.WriteAlphamaps(data, Alphamaps(data, shape));
            PlantTrees(data, look, shape, rng, camera);
            PlantGrass(data, look.grass, shape, rng, look.groundDetailDensity);
        }

        static float[,] Heights(Shape shape)
        {
            var origin = Origin;
            var heights = new float[HeightmapResolution, HeightmapResolution];
            for (int z = 0; z < HeightmapResolution; z++)
            for (int x = 0; x < HeightmapResolution; x++)
            {
                float wx = origin.x + x * Width / (HeightmapResolution - 1);
                float wz = origin.z + z * Length / (HeightmapResolution - 1);
                heights[z, x] = (GroundHeight(wx, wz, shape) - Bottom) / Height;
            }
            return heights;
        }

        /// <summary>Flat around Pudge, then rolling hills that rise toward the horizon and toward the sides.</summary>
        static float GroundHeight(float x, float z, Shape shape)
        {
            float d = new Vector2(x, z).magnitude;
            float open = Mathf.SmoothStep(0f, 1f, (d - ClearingRadius) / 12f); // 0 in the clearing
            if (z > 0f) open *= Mathf.Clamp01(1f - z / 8f); // and flat toward the camera
            float behind = Mathf.Clamp01(-z / Length);
            float rise = shape.hillHeight * Mathf.SmoothStep(0f, 1f, behind * 1.6f) + 4f * Mathf.SmoothStep(0f, 1f, Mathf.Abs(x) / 70f);
            float noise = (Mathf.PerlinNoise(shape.hills.x + x * shape.hillScale, shape.hills.y + z * shape.hillScale) - 0.5f) * 8f
                          + (Mathf.PerlinNoise(shape.bumps.x + x * 0.15f, shape.bumps.y + z * 0.15f) - 0.5f) * 1.2f;
            return Mathf.Max(Bottom + 0.5f, (rise + noise) * open);
        }

        /// <summary>Layers: 0 the side's ground, 1 its patches, 2 rock on the slopes, 3 the path.</summary>
        static float[,,] Alphamaps(TerrainData data, Shape shape)
        {
            var origin = Origin;
            var alpha = new float[AlphamapResolution, AlphamapResolution, 4];
            for (int z = 0; z < AlphamapResolution; z++)
            for (int x = 0; x < AlphamapResolution; x++)
            {
                float u = (x + 0.5f) / AlphamapResolution, v = (z + 0.5f) / AlphamapResolution;
                float wx = origin.x + u * Width, wz = origin.z + v * Length;
                float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 45f, data.GetSteepness(u, v)));
                float path = PathShare(wx, wz, shape);
                float patch = 0.8f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.75f,
                    Mathf.PerlinNoise(shape.patches.x + wx * 0.06f, shape.patches.y + wz * 0.06f)));
                float ground = (1f - rock) * (1f - path);
                alpha[z, x, 0] = ground * (1f - patch);
                alpha[z, x, 1] = ground * patch;
                alpha[z, x, 2] = rock;
                alpha[z, x, 3] = (1f - rock) * path;
            }
            return alpha;
        }

        static float MainPathX(float z, Shape shape) => z > 0f ? 0f
            : shape.bend * (Mathf.Sin(-z * shape.bendRate + shape.bendPhase) - Mathf.Sin(shape.bendPhase));

        /// <summary>A winding forest road with five narrower branches, all connected to it.</summary>
        static PathSegment[] Paths(Shape shape, System.Random rng)
        {
            var paths = new List<PathSegment>();
            Vector2 previous = new(0f, Front - 6f);
            for (int i = 1; i <= 20; i++)
            {
                float z = Mathf.Lerp(Front - 6f, -68f, i / 20f);
                var next = new Vector2(MainPathX(z, shape), z);
                paths.Add(new PathSegment { from = previous, to = next, halfWidth = Mathf.Lerp(1.05f, 0.7f, i / 20f) });
                previous = next;
            }
            float side = rng.Next(2) == 0 ? -1f : 1f;
            for (int branch = 0; branch < 5; branch++)
            {
                // Alternate sides, with junctions and bends varied by the landscape seed.
                float z = -Range(rng, 9f, 14f) - branch * 9f;
                // Start on the sampled road itself so there is no seam at the fork.
                var road = paths.Find(segment => segment.from.y >= z && segment.to.y <= z);
                Vector2 start = Vector2.Lerp(road.from, road.to, Mathf.InverseLerp(road.from.y, road.to.y, z));
                Vector2 end = start + new Vector2(side * Range(rng, 20f, 35f), -Range(rng, 12f, 24f));
                Vector2 control = start + new Vector2(side * Range(rng, 3f, 10f), -Range(rng, 10f, 20f));
                previous = start;
                float halfWidth = Range(rng, 0.65f, 0.85f);
                for (int step = 1; step <= 8; step++)
                {
                    float t = step / 8f;
                    Vector2 next = (1f - t) * (1f - t) * start + 2f * (1f - t) * t * control + t * t * end;
                    paths.Add(new PathSegment { from = previous, to = next, halfWidth = halfWidth });
                    previous = next;
                }
                side = -side;
            }
            return paths.ToArray();
        }

        /// <summary>Distance from the edge of the clearing or the nearest connected trail.</summary>
        static float PathEdge(float x, float z, Shape shape)
        {
            Vector2 at = new(x, z);
            float edge = at.magnitude - 2.3f;
            foreach (var segment in shape.paths)
            {
                Vector2 delta = segment.to - segment.from;
                float t = Mathf.Clamp01(Vector2.Dot(at - segment.from, delta) / Mathf.Max(0.001f, delta.sqrMagnitude));
                edge = Mathf.Min(edge, (at - segment.from - delta * t).magnitude - segment.halfWidth);
            }
            return edge;
        }

        static float PathShare(float x, float z, Shape shape) =>
            1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.35f, 0.35f, PathEdge(x, z, shape)));

        /// <summary>
        /// The side's trees kept apart, denser close to the clearing, farther ones a little bigger (the forest's line
        /// reads against the sky); its landmark tree (Radiant's mango) near Pudge on the left. A one-sided tree (a relief
        /// with nothing behind it) is turned to the <paramref name="camera"/>, which does not move: it always shows its front.
        /// </summary>
        static void PlantTrees(TerrainData data, MenuLandscape.Look look, Shape shape, System.Random rng, Vector3 camera)
        {
            var spots = new List<(MenuLandscape.MenuTree tree, Vector2 at, float scale)>();
            var placed = new List<Vector2>();
            if (look.landmark.prefab != null)
            {
                var near = new Vector2(Range(rng, -4.5f, -2.5f), Range(rng, -11f, -9f));
                while (PathEdge(near.x, near.y, shape) < 0.9f) near.x -= 1f;
                spots.Add((look.landmark, near, 1.1f));
                placed.Add(near);
            }
            int kind = rng.Next(Mathf.Max(1, look.trees.Length));
            for (int attempt = 0; attempt < TreeCount * 30 && placed.Count < TreeCount && look.trees.Length > 0; attempt++)
            {
                // the distance is skewed toward TreeNear
                float t = (float)rng.NextDouble();
                float distance = Mathf.Lerp(TreeNear, TreeFar, t * t);
                float angle = Range(rng, -100f, 100f) * Mathf.Deg2Rad; // 0 = straight behind Pudge
                var at = new Vector2(Mathf.Sin(angle) * distance, -Mathf.Cos(angle) * distance);
                if (at.y > 1f) continue;                           // in front of the camera
                if (Mathf.Abs(at.x) < 4f && at.y > -12f) continue; // right behind Pudge: his outline stays clean
                if (PathEdge(at.x, at.y, shape) < 0.9f) continue; // trunks stay clear of every trail
                if (placed.Any(p => (p - at).sqrMagnitude < TreeSpacing * TreeSpacing)) continue;
                placed.Add(at);
                spots.Add((look.trees[kind++ % look.trees.Length], at, Range(rng, 0.9f, 1.15f) * Mathf.Lerp(1f, 1.35f, t)));
            }

            var prefabs = spots.Select(s => s.tree.prefab).Distinct().ToList();
            data.treePrototypes = prefabs.Select(p => new TreePrototype { prefab = p }).ToArray();
            var origin = Origin;
            var viewer = new Vector2(camera.x, camera.z);
            data.SetTreeInstances(spots.Select(s =>
            {
                float turn = Range(rng, 0f, 2f * Mathf.PI);
                if (s.tree.oneSided)
                {
                    var to = viewer - s.at;
                    turn = Mathf.Repeat(Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg - s.tree.frontYaw, 360f) * Mathf.Deg2Rad;
                }
                return new TreeInstance
                {
                    prototypeIndex = prefabs.IndexOf(s.tree.prefab),
                    position = new Vector3((s.at.x - origin.x) / Width, 0f, (s.at.y - origin.z) / Length),
                    widthScale = s.scale,
                    heightScale = s.scale,
                    rotation = turn,
                    color = Color.white,
                    lightmapColor = Color.white,
                };
            }).ToArray(), snapToHeightmap: true);
        }

        /// <summary>The side's grass clumps in patches, off the slopes and the path, thinning out toward the hills.</summary>
        static void PlantGrass(TerrainData data, GameObject[] grass, Shape shape, System.Random rng, float density)
        {
            data.detailPrototypes = grass.Select(TerrainPaint.Grass).ToArray();
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            data.SetDetailResolution(GrassResolution, 32);
            if (grass.Length == 0) return;
            var layers = grass.Select(_ => new int[GrassResolution, GrassResolution]).ToArray();
            var origin = Origin;
            for (int z = 0; z < GrassResolution; z++)
            for (int x = 0; x < GrassResolution; x++)
            {
                float u = (x + 0.5f) / GrassResolution, v = (z + 0.5f) / GrassResolution;
                var at = new Vector2(origin.x + u * Width, origin.z + v * Length);
                float d = at.magnitude;
                if (d < GrassNear || d > GrassFar || at.y > 3f || data.GetSteepness(u, v) > 28f || PathShare(at.x, at.y, shape) > 0.2f) continue;
                float patch = Mathf.InverseLerp(0.35f, 0.75f, Mathf.PerlinNoise(shape.grass.x + at.x * 0.08f, shape.grass.y + at.y * 0.08f));
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GrassFar * 0.6f, GrassFar, d));
                int count = Mathf.FloorToInt(patch * fade * GrassPerCell * density + (float)rng.NextDouble());
                for (int i = 0; i < count; i++) layers[rng.Next(layers.Length)][z, x]++;
            }
            for (int i = 0; i < layers.Length; i++) data.SetDetailLayer(0, 0, i, layers[i]);
        }

        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
