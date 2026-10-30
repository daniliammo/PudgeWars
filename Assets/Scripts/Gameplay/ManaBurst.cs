using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The blue burst of Arcane Boots' Replenish Mana with Dota's sound (Resources/Audio/Items/arcane_boots): a faint
    /// wave runs out over the ground to the use's radius, and every ally the mana reached (the owner too) gets its own,
    /// brighter effect as the wave comes to it, following it: a rune circle turning under its feet and two strands of
    /// light winding up round it, narrowing into the hero. Drawn by Resources/Effects/ManaBurst.shader; plays wherever
    /// an item that gives mana is used (<see cref="HeroInventory.AnyUsed"/>), on a network client too.
    /// </summary>
    public class ManaBurst : MonoBehaviour
    {
        [Tooltip("Seconds the wave takes to run out and fade.")] public float waveDuration = 0.9f;
        [Tooltip("Brightness of the wave (the heroes' effects are the bright part).")] public float waveIntensity = 1.2f;
        [Tooltip("Seconds an ally's effect plays.")] public float allyDuration = 1.4f;
        [Tooltip("An ally's effect starts as the wave reaches it: seconds per metre from the owner.")]
        public float allyDelayPerMetre = 0.035f;
        public float allyIntensity = 2.6f;
        [Tooltip("Height of the winding strands, metres.")] public float helixHeight = 3f;
        [Tooltip("Radius the strands start at and the one they narrow to, metres.")] public Vector2 helixRadius = new(1.3f, 0.45f);
        [Tooltip("Radius of the rune circle under an ally, metres.")] public float runeRadius = 1.3f;
        [Range(0f, 1f)] public float soundVolume = 0.9f;
        [Header("Enchanted Mango")]
        [Tooltip("Seconds the mango's effect plays.")] public float mangoDuration = 1.8f;
        [Tooltip("Radius of the petals opening under the hero, metres.")] public float mangoRadius = 2.3f;
        public float mangoIntensity = 3.2f;
        [Tooltip("The strands wrapping the hero: the radius they start at, the one they close to (round the body), their height, metres.")]
        public Vector3 mangoWrap = new(1.4f, 0.75f, 3.6f);
        [Tooltip("The petals and the strands: mana blue, their middles, sparks and drops pale blue.")]
        public Color mangoColor = new(0.2f, 0.55f, 1f);
        public Color mangoCore = new(0.75f, 0.93f, 1f);

        static ManaBurst instance;
        static Material material;
        static Mesh quad, cylinder;
        static AudioClip sound;
        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        static readonly int ShapeId = Shader.PropertyToID("_Shape");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int CoreId = Shader.PropertyToID("_Core");

        enum Shape { Wave = 0, Helix = 1, Runes = 2, Petals = 3 }

        class Piece
        {
            public Transform transform;
            public MeshRenderer renderer;
            public Shape shape;
            public Vector3 size; // the scale it was made with: the helix narrows from it, the circle springs to it
            public float narrowTo; // the share of its radius a helix ends at
            public float start, duration;
        }

        readonly List<Piece> pieces = new();
        MaterialPropertyBlock block;

        /// <summary>Parts of bursts still playing (for tests).</summary>
        public static int ActiveCount => instance != null ? instance.pieces.Count : 0;
        /// <summary>How many times the sound was played (for tests).</summary>
        public static int SoundsPlayed { get; private set; }

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
            if (owner == null || use == null) return;
            if (use.action == ItemAction.RestoreAllyMana) Play(owner.GetComponent<Unit>(), use.castRange);
            else if (use.action == ItemAction.RestoreMana) PlayMango(owner.GetComponent<Unit>());
        }

        /// <summary>A burst around <paramref name="owner"/>, reaching its allies within <paramref name="radius"/> metres.</summary>
        public void Play(Unit owner, float radius)
        {
            if (owner == null || !EnsureAssets()) return;
            var centre = owner.transform.position;
            PlaySound(centre);
            Add(null, centre + Vector3.up * 0.05f, Vector3.one * radius, Shape.Wave, 0f, waveDuration, waveIntensity);
            foreach (var unit in Unit.AllUnits)
            {
                if (unit.team != owner.team || !unit.IsAlive) continue;
                var d = unit.transform.position - centre;
                d.y = 0f;
                if (d.magnitude > radius) continue;
                float delay = d.magnitude * allyDelayPerMetre;
                Add(unit.transform, Vector3.up * 0.07f, Vector3.one * runeRadius, Shape.Runes, delay, allyDuration, allyIntensity);
                Add(unit.transform, Vector3.zero, new Vector3(helixRadius.x, helixHeight, helixRadius.x), Shape.Helix, delay, allyDuration, allyIntensity);
            }
        }

        /// <summary>
        /// Enchanted Mango eaten: wide blue petals open and turn round the hero's feet with pale drops flying out, and two
        /// shells of winding light wrap the hero from the feet to over its head, closing round the body (not into a
        /// point) so the whole figure is seen wrapped in mana. Its sound is <see cref="ItemUseSounds"/>'.
        /// </summary>
        public void PlayMango(Unit eater)
        {
            if (eater == null || !EnsureAssets()) return;
            Add(eater.transform, Vector3.up * 0.07f, Vector3.one * mangoRadius, Shape.Petals, 0f, mangoDuration, mangoIntensity, mangoColor, mangoCore);
            float closeTo = mangoWrap.y / Mathf.Max(0.01f, mangoWrap.x);
            Add(eater.transform, Vector3.zero, new Vector3(mangoWrap.x, mangoWrap.z, mangoWrap.x), Shape.Helix,
                0.05f, mangoDuration, mangoIntensity, mangoColor, mangoCore, closeTo);
            // a second, narrower and lower shell: its strands cross the first one's
            Add(eater.transform, Vector3.zero, new Vector3(mangoWrap.x * 0.8f, mangoWrap.z * 0.82f, mangoWrap.x * 0.8f), Shape.Helix,
                0.15f, mangoDuration, mangoIntensity, mangoColor, mangoCore, closeTo);
        }

        /// <summary>Dota's Arcane Boots sound where they were used, heard around as the heroes' sounds are.</summary>
        void PlaySound(Vector3 at)
        {
            if (sound == null) return;
            var go = new GameObject("ArcaneBootsSound");
            go.transform.position = at;
            var source = go.AddComponent<AudioSource>();
            source.clip = sound;
            source.volume = soundVolume;
            source.spatialBlend = 0.8f;
            source.minDistance = 8f;
            source.maxDistance = 45f;
            source.Play();
            Destroy(go, sound.length + 0.1f);
            SoundsPlayed++;
        }

        void Add(Transform follow, Vector3 offset, Vector3 scale, Shape shape, float delay, float duration, float intensity,
            Color? color = null, Color? core = null, float narrowTo = -1f)
        {
            var go = new GameObject("Mana" + shape);
            go.transform.SetParent(follow != null ? follow : transform, false);
            if (follow == null) go.transform.position = offset;
            else go.transform.localPosition = offset;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = shape == Shape.Helix ? cylinder : quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            block ??= new MaterialPropertyBlock();
            block.Clear();
            block.SetFloat(ShapeId, (float)shape);
            block.SetFloat(SeedId, Random.value * 10f);
            block.SetFloat(IntensityId, intensity);
            if (color is { } c) block.SetColor(ColorId, c);
            if (core is { } k) block.SetColor(CoreId, k);
            block.SetFloat(ProgressId, 0f);
            renderer.SetPropertyBlock(block);
            renderer.enabled = delay <= 0f;
            pieces.Add(new Piece { transform = go.transform, renderer = renderer, shape = shape, size = scale,
                narrowTo = narrowTo >= 0f ? narrowTo : helixRadius.y / Mathf.Max(0.01f, helixRadius.x), start = Time.time + delay, duration = duration });
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
                float eased = 1f - (1f - t) * (1f - t);
                if (piece.shape == Shape.Helix)
                {
                    // the strands narrow into the hero
                    float r = piece.size.x * Mathf.Lerp(1f, piece.narrowTo, eased);
                    piece.transform.localScale = new Vector3(r, piece.size.y, r);
                }
                else if (piece.shape == Shape.Runes)
                {
                    // the circle springs open a little past its size and settles
                    float pop = 0.6f + 0.4f * Mathf.Min(1f, t * 5f) + 0.08f * Mathf.Sin(Mathf.Min(1f, t * 5f) * Mathf.PI);
                    piece.transform.localScale = piece.size * pop;
                }
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
            if (sound == null) sound = Resources.Load<AudioClip>("Audio/Items/arcane_boots");
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
