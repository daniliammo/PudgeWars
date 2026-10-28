using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The bright blue burst of Arcane Boots' Replenish Mana: a glowing wave runs out over the ground to the use's
    /// radius, and a column of light with spiralling streaks and sparks rises over every ally the mana reached (the
    /// owner too), following it. Drawn by Resources/Effects/ManaBurst.shader; plays wherever an item that gives mana is
    /// used (<see cref="HeroInventory.AnyUsed"/>), on a network client too.
    /// </summary>
    public class ManaBurst : MonoBehaviour
    {
        [Tooltip("Seconds the wave takes to run out and fade.")] public float waveDuration = 0.9f;
        [Tooltip("Seconds a column rises and fades.")] public float columnDuration = 1.1f;
        [Tooltip("Columns start this much later than the wave: they light up as it reaches them, seconds per metre.")]
        public float columnDelayPerMetre = 0.035f;
        public float columnHeight = 3.2f;
        public float columnRadius = 0.85f;

        static ManaBurst instance;
        static Material material;
        static Mesh quad, cylinder;
        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        static readonly int ShapeId = Shader.PropertyToID("_Shape");
        static readonly int SeedId = Shader.PropertyToID("_Seed");

        class Piece
        {
            public Transform transform;
            public MeshRenderer renderer;
            public float start, duration;
        }

        readonly List<Piece> pieces = new();
        MaterialPropertyBlock block;

        /// <summary>Parts of bursts still playing (for tests).</summary>
        public static int ActiveCount => instance != null ? instance.pieces.Count : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            if (instance != null) return;
            var go = new GameObject("ManaBurst");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ManaBurst>();
        }

        void OnEnable() => HeroInventory.AnyUsed += OnUsed;

        void OnDisable() => HeroInventory.AnyUsed -= OnUsed;

        void OnUsed(HeroInventory owner, ItemDef item)
        {
            var use = item != null ? item.Active : null;
            if (owner == null || use == null || use.action != ItemAction.RestoreAllyMana) return;
            Play(owner.GetComponent<Unit>(), use.castRange);
        }

        /// <summary>A burst around <paramref name="owner"/>, reaching its allies within <paramref name="radius"/> metres.</summary>
        public void Play(Unit owner, float radius)
        {
            if (owner == null || !EnsureAssets()) return;
            var centre = owner.transform.position;
            float seed = Random.value * 10f;
            Add(null, centre + Vector3.up * 0.06f, Vector3.one * radius, quad, 0f, waveDuration, seed);
            foreach (var unit in Unit.AllUnits)
            {
                if (unit.team != owner.team || !unit.IsAlive) continue;
                var d = unit.transform.position - centre;
                d.y = 0f;
                if (d.magnitude > radius) continue;
                Add(unit.transform, Vector3.zero, new Vector3(columnRadius, columnHeight, columnRadius), cylinder,
                    d.magnitude * columnDelayPerMetre, columnDuration, seed + Random.value * 10f);
            }
        }

        void Add(Transform follow, Vector3 position, Vector3 scale, Mesh mesh, float delay, float duration, float seed)
        {
            var go = new GameObject(mesh == quad ? "ManaWave" : "ManaColumn");
            go.transform.SetParent(follow != null ? follow : transform, false);
            if (follow == null) go.transform.position = position;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            block ??= new MaterialPropertyBlock();
            block.SetFloat(ShapeId, mesh == quad ? 0f : 1f);
            block.SetFloat(SeedId, seed);
            block.SetFloat(ProgressId, 0f);
            renderer.SetPropertyBlock(block);
            renderer.enabled = delay <= 0f;
            pieces.Add(new Piece { transform = go.transform, renderer = renderer, start = Time.time + delay, duration = duration });
        }

        void Update()
        {
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                var piece = pieces[i];
                if (piece.transform == null) // its hero is gone
                {
                    pieces.RemoveAt(i);
                    continue;
                }
                float t = (Time.time - piece.start) / piece.duration;
                if (t >= 1f)
                {
                    Destroy(piece.transform.gameObject);
                    pieces.RemoveAt(i);
                    continue;
                }
                piece.renderer.enabled = t >= 0f;
                if (t < 0f) continue;
                piece.renderer.GetPropertyBlock(block);
                block.SetFloat(ProgressId, t);
                piece.renderer.SetPropertyBlock(block);
            }
        }

        static bool EnsureAssets()
        {
            if (material == null)
            {
                var shader = Resources.Load<Shader>("Effects/ManaBurst");
                if (shader == null) return false;
                material = new Material(shader) { name = "ManaBurst" };
            }
            if (quad == null) quad = Quad();
            if (cylinder == null) cylinder = Cylinder(32);
            return true;
        }

        /// <summary>A flat 2 × 2 square facing up, uv −1..1 from its centre (scaled by the radius).</summary>
        static Mesh Quad()
        {
            var mesh = new Mesh { name = "ManaWave" };
            mesh.vertices = new[] { new Vector3(-1f, 0f, -1f), new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, -1f) };
            mesh.uv = new[] { new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>An open cylinder of radius 1 from the ground up to 1, uv.x around it and uv.y up.</summary>
        static Mesh Cylinder(int sides)
        {
            var vertices = new Vector3[(sides + 1) * 2];
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                var ring = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices[i * 2] = ring;
                vertices[i * 2 + 1] = ring + Vector3.up;
                uv[i * 2] = new Vector2(i / (float)sides, 0f);
                uv[i * 2 + 1] = new Vector2(i / (float)sides, 1f);
            }
            var triangles = new int[sides * 6];
            for (int i = 0; i < sides; i++)
            {
                int v = i * 2;
                triangles[i * 6] = v;
                triangles[i * 6 + 1] = v + 1;
                triangles[i * 6 + 2] = v + 3;
                triangles[i * 6 + 3] = v;
                triangles[i * 6 + 4] = v + 3;
                triangles[i * 6 + 5] = v + 2;
            }
            var mesh = new Mesh { name = "ManaColumn", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
