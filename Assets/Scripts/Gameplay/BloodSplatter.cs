using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Blood. A splash (<see cref="Splash"/>: a body hitting the ground, <see cref="Corpse"/>) leaves a stain where it
    /// hit and throws drops that stain whatever they land on: the ground (stains follow the terrain's shape, down the
    /// pit's walls too), bodies (stuck to the part they hit and moving with it), standing heroes (stuck to the bone
    /// nearest the hit) and tree trunks. A pool spreads under a body at rest (<see cref="Pool"/>). Stains fade after
    /// a while, the oldest go first when there are too many. Only for show: each machine of a network game splashes
    /// its own. Made on first use in the active scene and gone with it; the look is Resources/Effects/Blood.shader
    /// with textures drawn here.
    /// </summary>
    public class BloodSplatter : MonoBehaviour
    {
        [Tooltip("Stains on the ground and trees at once; the oldest go first.")] public int maxGroundStains = 400;
        [Tooltip("Stains on bodies and heroes at once; the oldest go first.")] public int maxBodyStains = 120;
        [Tooltip("Seconds a ground stain stays, its fading included.")] public float groundLifetime = 45f;
        [Tooltip("Seconds a stain on a body or a hero stays, its fading included.")] public float bodyLifetime = 20f;
        [Tooltip("Seconds of fading at the end of a stain's life.")] public float fadeTime = 8f;
        [Tooltip("Drops in the air at once.")] public int maxDrops = 400;
        [Tooltip("Gravity of the drops, m/s² (heroes fall with 20).")] public float dropGravity = 16f;
        public Color stainColor = new(0.3f, 0.0f, 0.01f);
        public Color dropColor = new(0.42f, 0.01f, 0.02f);

        const int Grid = 4;                  // a ground stain is a 4 × 4 grid laid onto the terrain
        const int GridVerts = Grid * Grid;
        const float TerrainLift = 0.04f;     // over the terrain: its drawn surface is a little off the height samples
        const float SurfaceLift = 0.015f;
        const float HeroHeight = 1.8f;
        const float TrunkHeight = 3f;
        const float FadeStep = 0.1f;         // seconds between updates of the fading stains' alpha

        static BloodSplatter instance;

        struct Drop
        {
            public Vector3 position, velocity;
            public float size, age;
        }

        struct GroundStain
        {
            public Vector3 centre, normal, right, forward;
            public Vector2 uv;
            public float size, born, grow;
            public Terrain terrain;
            public byte shade;
            public bool alive, grown;
        }

        class BodyStain
        {
            public GameObject go;
            public MeshRenderer renderer;
            public Mesh mesh;
            public Color32[] colors;
            public Hookable owner; // a standing hero: its stains hide when it is gone, and stay hidden
            public float born;
            public bool hidden;
        }

        readonly List<Drop> drops = new();
        readonly List<BodyStain> bodyStains = new();
        readonly Dictionary<Hookable, Transform[]> heroBones = new();
        GroundStain[] ground;
        int nextGround;
        Vector3[] groundVerts, groundNormals;
        Vector2[] groundUvs;
        Color32[] groundColors;
        bool groundDirty;
        float nextFade;
        Mesh groundMesh, dropMesh;
        Material stainMaterial, dropMaterial;
        Texture2D stainTexture, dropTexture;
        readonly List<Vector3> dropVerts = new();
        readonly List<Vector2> dropUvs = new();
        readonly List<int> dropTris = new();
        bool ready;

        /// <summary>Stains on the ground and trees now.</summary>
        public static int GroundStainCount
        {
            get
            {
                int n = 0;
                if (instance != null && instance.ground != null)
                    foreach (var s in instance.ground) if (s.alive) n++;
                return n;
            }
        }

        /// <summary>Stains on bodies and heroes now.</summary>
        public static int BodyStainCount => instance != null ? instance.bodyStains.Count : 0;

        static BloodSplatter Instance
        {
            get
            {
                if (instance == null) instance = new GameObject("Blood").AddComponent<BloodSplatter>();
                return instance;
            }
        }

        /// <summary>
        /// Blood bursts from <paramref name="point"/> on a surface facing <paramref name="normal"/> (toward where the
        /// blood comes from): a stain there and drops thrown off it, more and further with <paramref name="strength"/>
        /// (0..1), carried along with <paramref name="carry"/> (the body's sliding speed).
        /// </summary>
        public static void Splash(Vector3 point, Vector3 normal, float strength, Vector3 carry, Collider surface = null)
        {
            var blood = Instance;
            if (!blood.ready) return;
            strength = Mathf.Clamp01(strength);
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            if (surface != null) blood.Stain(point, normal, Mathf.Lerp(0.45f, 1.4f, strength), surface, 0.15f);
            blood.Emit(point, normal, strength, carry);
        }

        /// <summary>A pool of <paramref name="size"/> metres slowly spreads on the ground under <paramref name="above"/>.</summary>
        public static void Pool(Vector3 above, float size)
        {
            var blood = Instance;
            if (!blood.ready) return;
            RaycastHit? nearest = null;
            foreach (var hit in Physics.RaycastAll(above + Vector3.up * 0.5f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider is TerrainCollider && (nearest == null || hit.distance < nearest.Value.distance)) nearest = hit;
            if (nearest is { } ground && ground.collider.TryGetComponent<Terrain>(out var terrain))
                blood.AddGroundStain(ground.point, ground.normal, size, 1.5f, terrain);
        }

        void Awake()
        {
            var shader = Resources.Load<Shader>("Effects/Blood");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("[BloodSplatter] Resources/Effects/Blood shader is missing or unsupported: no blood");
                return;
            }
            stainTexture = StainAtlas();
            dropTexture = DropTexture();
            stainMaterial = new Material(shader) { name = "BloodStain", mainTexture = stainTexture };
            stainMaterial.SetColor("_BaseColor", stainColor);
            dropMaterial = new Material(shader) { name = "BloodDrop", mainTexture = dropTexture };
            dropMaterial.SetColor("_BaseColor", dropColor);
            dropMaterial.SetFloat("_NormalUp", 1f);
            dropMaterial.SetFloat("_Gloss", 0.6f);
            dropMaterial.SetFloat("_DepthOffset", 0f);

            ground = new GroundStain[Mathf.Max(1, maxGroundStains)];
            int verts = ground.Length * GridVerts;
            groundVerts = new Vector3[verts];
            groundNormals = new Vector3[verts];
            groundUvs = new Vector2[verts];
            groundColors = new Color32[verts];
            var tris = new int[ground.Length * (Grid - 1) * (Grid - 1) * 6];
            for (int s = 0, t = 0; s < ground.Length; s++)
            for (int y = 0; y < Grid - 1; y++)
            for (int x = 0; x < Grid - 1; x++)
            {
                int v = s * GridVerts + y * Grid + x;
                tris[t++] = v; tris[t++] = v + Grid; tris[t++] = v + 1;
                tris[t++] = v + 1; tris[t++] = v + Grid; tris[t++] = v + Grid + 1;
            }
            groundMesh = new Mesh { name = "BloodStains" };
            groundMesh.MarkDynamic();
            groundMesh.SetVertices(groundVerts);
            groundMesh.SetNormals(groundNormals);
            groundMesh.SetUVs(0, groundUvs);
            groundMesh.SetColors(groundColors);
            groundMesh.SetTriangles(tris, 0);
            Renderer("Stains", groundMesh, stainMaterial);

            dropMesh = new Mesh { name = "BloodDrops" };
            dropMesh.MarkDynamic();
            Renderer("Drops", dropMesh, dropMaterial);
            ready = true;
        }

        void Renderer(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            foreach (var s in bodyStains) Destroy(s.mesh);
            Destroy(groundMesh);
            Destroy(dropMesh);
            Destroy(stainMaterial);
            Destroy(dropMaterial);
            Destroy(stainTexture);
            Destroy(dropTexture);
        }

        void LateUpdate()
        {
            if (!ready) return;
            float now = Time.time;
            bool fadeTick = now >= nextFade;
            if (fadeTick) nextFade = now + FadeStep;
            if (Time.deltaTime > 0f) MoveDrops(Time.deltaTime);
            DrawDrops();
            UpdateGround(now, fadeTick);
            UpdateBodyStains(now, fadeTick);
        }

        // drops ///////////////////////////////////////////////////////////////

        void Emit(Vector3 point, Vector3 normal, float strength, Vector3 carry)
        {
            int count = Mathf.RoundToInt(Mathf.Lerp(8f, 45f, strength));
            float speed = Mathf.Lerp(2.5f, 8.5f, strength);
            for (int i = 0; i < count && drops.Count < maxDrops; i++)
            {
                var dir = normal * Random.Range(0.3f, 1f) + Random.insideUnitSphere * 0.9f;
                if (Vector3.Dot(dir, normal) < 0.15f) dir += normal * 0.6f; // off the surface, not into it
                drops.Add(new Drop
                {
                    position = point + normal * 0.08f,
                    velocity = dir.normalized * speed * Random.Range(0.35f, 1f) + carry * Random.Range(0.1f, 0.5f),
                    size = Random.Range(0.025f, 0.06f) * Mathf.Lerp(1f, 1.8f, strength),
                });
            }
        }

        void MoveDrops(float dt)
        {
            for (int i = drops.Count - 1; i >= 0; i--)
            {
                var d = drops[i];
                var from = d.position;
                d.velocity += Vector3.down * (dropGravity * dt);
                d.position += d.velocity * dt;
                d.age += dt;
                if (d.age > 5f || Land(from, d))
                {
                    drops[i] = drops[^1]; // the last one was moved already
                    drops.RemoveAt(drops.Count - 1);
                }
                else drops[i] = d;
            }
        }

        /// <summary>True if the drop hit something on its way this frame (and stained it).</summary>
        bool Land(Vector3 from, Drop d)
        {
            var to = d.position;
            float size = d.size * Random.Range(4f, 8f);
            if (Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore))
            {
                Stain(hit.point, hit.normal, size, hit.collider, 0.1f);
                return true;
            }
            // standing heroes and trees have no colliders: their shapes are checked here
            foreach (var hero in Hookable.All)
            {
                if (!hero.IsAlive || hero.IsFalling) continue;
                var feet = hero.transform.position;
                float height = to.y - feet.y;
                var off = new Vector3(to.x - feet.x, 0f, to.z - feet.z);
                float r = hero.radius * 0.8f;
                if (height < 0.1f || height > HeroHeight || off.sqrMagnitude > r * r) continue;
                var normal = off.sqrMagnitude > 1e-6f ? off.normalized : -new Vector3(d.velocity.x, 0f, d.velocity.z).normalized;
                var point = new Vector3(feet.x, to.y, feet.z) + normal * r;
                AddBodyStain(NearestBone(hero, point), point, normal, size * 0.5f, hero);
                return true;
            }
            foreach (var tree in Obstacle.All)
            {
                var off = new Vector3(to.x - tree.center.x, 0f, to.z - tree.center.z);
                float height = to.y - tree.center.y;
                if (height < 0f || height > TrunkHeight || off.sqrMagnitude > tree.radius * tree.radius) continue;
                var normal = off.sqrMagnitude > 1e-6f ? off.normalized : Vector3.forward;
                var point = new Vector3(tree.center.x, to.y, tree.center.z) + normal * (tree.radius * 0.75f);
                AddGroundStain(point, normal, size * 0.6f, 0.1f, null);
                return true;
            }
            return false;
        }

        /// <summary>Drops as small streaks along their flight, facing the camera.</summary>
        void DrawDrops()
        {
            dropVerts.Clear();
            dropUvs.Clear();
            dropTris.Clear();
            var cam = Camera.main;
            if (cam != null)
            {
                var eye = cam.transform.position;
                foreach (var d in drops)
                {
                    var along = d.velocity * 0.035f;
                    if (along.sqrMagnitude < d.size * d.size) along = along.sqrMagnitude > 1e-8f ? along.normalized * d.size : cam.transform.up * d.size;
                    var side = Vector3.Cross(along, eye - d.position);
                    side = side.sqrMagnitude > 1e-8f ? side.normalized * (d.size * 0.5f) : cam.transform.right * (d.size * 0.5f);
                    int v = dropVerts.Count;
                    dropVerts.Add(d.position - along - side);
                    dropVerts.Add(d.position - along + side);
                    dropVerts.Add(d.position + side);
                    dropVerts.Add(d.position - side);
                    dropUvs.Add(new Vector2(0f, 0f));
                    dropUvs.Add(new Vector2(1f, 0f));
                    dropUvs.Add(new Vector2(1f, 1f));
                    dropUvs.Add(new Vector2(0f, 1f));
                    dropTris.Add(v); dropTris.Add(v + 1); dropTris.Add(v + 2);
                    dropTris.Add(v); dropTris.Add(v + 2); dropTris.Add(v + 3);
                }
            }
            dropMesh.Clear();
            if (dropVerts.Count == 0) return;
            dropMesh.SetVertices(dropVerts);
            dropMesh.SetUVs(0, dropUvs);
            dropMesh.SetTriangles(dropTris, 0);
            dropMesh.RecalculateNormals();
        }

        // stains //////////////////////////////////////////////////////////////

        void Stain(Vector3 point, Vector3 normal, float size, Collider surface, float grow)
        {
            if (surface is TerrainCollider && surface.TryGetComponent<Terrain>(out var terrain))
                AddGroundStain(point, normal, size, grow, terrain);
            else if (surface.attachedRigidbody != null) // a body's part
                AddBodyStain(surface.transform, point, normal, size * 0.5f, null);
            else
                AddGroundStain(point, normal, size, grow, null);
        }

        /// <summary>A stain growing to <paramref name="size"/> over <paramref name="grow"/> seconds; laid onto the terrain if given.</summary>
        void AddGroundStain(Vector3 point, Vector3 normal, float size, float grow, Terrain terrain)
        {
            int i = nextGround;
            nextGround = (nextGround + 1) % ground.Length;
            var right = Vector3.Cross(normal, Random.onUnitSphere);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.Cross(normal, Vector3.right);
            right.Normalize();
            int variant = Random.Range(0, 4);
            ground[i] = new GroundStain
            {
                centre = point, normal = normal, right = right, forward = Vector3.Cross(right, normal),
                uv = new Vector2(variant % 2 * 0.5f, variant / 2 * 0.5f),
                size = size, born = Time.time, grow = grow, terrain = terrain,
                shade = (byte)Random.Range(190, 256), alive = true,
            };
            PlaceGroundStain(i, grow > 0f ? 0.05f : 1f);
            SetGroundAlpha(i, 255);
        }

        void PlaceGroundStain(int i, float scale)
        {
            var s = ground[i];
            float half = s.size * scale * 0.5f;
            for (int y = 0, k = i * GridVerts; y < Grid; y++)
            for (int x = 0; x < Grid; x++, k++)
            {
                float fx = x / (Grid - 1f), fy = y / (Grid - 1f);
                var p = s.centre + (s.right * (fx * 2f - 1f) + s.forward * (fy * 2f - 1f)) * half;
                var n = s.normal;
                float lift = SurfaceLift;
                if (s.terrain != null)
                {
                    // straight down onto the terrain: the stain bends over bumps, rims and the pit's walls
                    var origin = s.terrain.transform.position;
                    var size = s.terrain.terrainData.size;
                    p.y = s.terrain.SampleHeight(p) + origin.y;
                    n = s.terrain.terrainData.GetInterpolatedNormal((p.x - origin.x) / size.x, (p.z - origin.z) / size.z);
                    lift = TerrainLift;
                }
                groundVerts[k] = p + n * lift;
                groundNormals[k] = n;
                groundUvs[k] = s.uv + new Vector2(fx, fy) * 0.5f;
            }
            groundDirty = true;
        }

        void SetGroundAlpha(int i, byte alpha)
        {
            var c = new Color32(ground[i].shade, ground[i].shade, ground[i].shade, alpha);
            for (int k = i * GridVerts; k < (i + 1) * GridVerts; k++) groundColors[k] = c;
            groundDirty = true;
        }

        void UpdateGround(float now, bool fadeTick)
        {
            for (int i = 0; i < ground.Length; i++)
            {
                ref var s = ref ground[i];
                if (!s.alive) continue;
                float age = now - s.born;
                if (age >= groundLifetime)
                {
                    s.alive = false;
                    for (int k = i * GridVerts; k < (i + 1) * GridVerts; k++) groundVerts[k] = Vector3.zero;
                    SetGroundAlpha(i, 0);
                    continue;
                }
                if (!s.grown)
                {
                    float t = s.grow > 0f ? Mathf.Clamp01(age / s.grow) : 1f;
                    s.grown = t >= 1f;
                    PlaceGroundStain(i, Mathf.Max(0.05f, 1f - Mathf.Pow(1f - t, 3f)));
                }
                if (fadeTick && age > groundLifetime - fadeTime)
                    SetGroundAlpha(i, (byte)(255f * Mathf.Clamp01((groundLifetime - age) / fadeTime)));
            }
            if (!groundDirty) return;
            groundDirty = false;
            groundMesh.SetVertices(groundVerts);
            groundMesh.SetNormals(groundNormals);
            groundMesh.SetUVs(0, groundUvs);
            groundMesh.SetColors(groundColors);
            groundMesh.RecalculateBounds();
        }

        /// <summary>A stain stuck to <paramref name="to"/> (a body's part or a hero's bone), moving with it.</summary>
        void AddBodyStain(Transform to, Vector3 point, Vector3 normal, float size, Hookable owner)
        {
            if (bodyStains.Count >= Mathf.Max(1, maxBodyStains)) RemoveBodyStain(0);
            int variant = Random.Range(0, 4);
            var uv = new Vector2(variant % 2 * 0.5f, variant / 2 * 0.5f);
            byte shade = (byte)Random.Range(190, 256);
            var colors = new Color32[4];
            for (int k = 0; k < 4; k++) colors[k] = new Color32(shade, shade, shade, 255);
            var mesh = new Mesh { name = "BloodStain" };
            mesh.SetVertices(new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) });
            mesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.SetUVs(0, new[] { uv, uv + new Vector2(0f, 0.5f), uv + new Vector2(0.5f, 0.5f), uv + new Vector2(0.5f, 0f) });
            mesh.SetColors(colors);
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);

            var go = new GameObject("BloodStain");
            go.transform.SetPositionAndRotation(point + normal * SurfaceLift,
                Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * Quaternion.FromToRotation(Vector3.up, normal));
            go.transform.SetParent(to, true);
            var parentScale = to.lossyScale;
            go.transform.localScale = Vector3.one * (size / Mathf.Max(Mathf.Abs(parentScale.x), Mathf.Abs(parentScale.y), Mathf.Abs(parentScale.z), 1e-6f));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = stainMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            bodyStains.Add(new BodyStain { go = go, renderer = r, mesh = mesh, colors = colors, owner = owner, born = Time.time });
        }

        void RemoveBodyStain(int i)
        {
            var s = bodyStains[i];
            if (s.go != null) Destroy(s.go);
            Destroy(s.mesh);
            bodyStains.RemoveAt(i);
        }

        void UpdateBodyStains(float now, bool fadeTick)
        {
            for (int i = bodyStains.Count - 1; i >= 0; i--)
            {
                var s = bodyStains[i];
                float age = now - s.born;
                // a body's stains go with the body; copies of a hero's ones stay on its body, using the same mesh
                if (s.go == null || age >= bodyLifetime)
                {
                    RemoveBodyStain(i);
                    continue;
                }
                if (s.owner != null && !s.hidden && (!s.owner.IsAlive || s.owner.IsFalling))
                {
                    s.hidden = true; // the hero is gone (its body took a copy of the stain): it comes back clean
                    s.renderer.enabled = false;
                }
                if (fadeTick && age > bodyLifetime - fadeTime)
                {
                    byte alpha = (byte)(255f * Mathf.Clamp01((bodyLifetime - age) / fadeTime));
                    for (int k = 0; k < 4; k++) s.colors[k].a = alpha;
                    s.mesh.SetColors(s.colors);
                }
            }
        }

        Transform NearestBone(Hookable hero, Vector3 point)
        {
            if (!heroBones.TryGetValue(hero, out var bones))
            {
                var animator = hero.GetComponentInChildren<Animator>();
                bones = animator != null ? animator.GetComponentsInChildren<Transform>() : new[] { hero.transform };
                heroBones[hero] = bones;
            }
            Transform nearest = hero.transform;
            float best = float.MaxValue;
            foreach (var bone in bones)
            {
                if (bone == null) continue;
                float d = (bone.position - point).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = bone;
                }
            }
            return nearest;
        }

        // textures ////////////////////////////////////////////////////////////

        /// <summary>
        /// Four stains in a 2 × 2 atlas: a blob with a ragged edge, spikes thrown out of it and drops around; white
        /// (tinted by the material), a little darker at the edge where blood dries first.
        /// </summary>
        static Texture2D StainAtlas()
        {
            const int cell = 128, size = cell * 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "BloodStains" };
            var pixels = new Color32[size * size];
            var rnd = new System.Random(20261025);
            float Next(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            for (int variant = 0; variant < 4; variant++)
            {
                int ox = variant % 2 * cell, oy = variant / 2 * cell;
                float radius = Next(0.3f, 0.4f);
                var waves = new Vector3[6]; // amplitude, frequency, phase
                for (int k = 0; k < waves.Length; k++) waves[k] = new Vector3(Next(0.02f, 0.09f) / (1f + k * 0.25f), 3 + k * 2 + rnd.Next(2), Next(0f, Mathf.PI * 2f));
                var dots = new Vector3[8 + rnd.Next(10)]; // x, y, radius
                for (int k = 0; k < dots.Length; k++)
                {
                    float a = Next(0f, Mathf.PI * 2f), d = radius * Next(1.15f, 2.2f);
                    dots[k] = new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, Next(0.015f, 0.05f) * (1.4f - d));
                }
                var spikes = new Vector4[4 + rnd.Next(4)]; // direction x, y, length, width
                for (int k = 0; k < spikes.Length; k++)
                {
                    float a = Next(0f, Mathf.PI * 2f);
                    spikes[k] = new Vector4(Mathf.Cos(a), Mathf.Sin(a), radius * Next(1.3f, 2f), Next(0.025f, 0.05f));
                }
                for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    float u = (x + 0.5f) / cell * 2f - 1f, v = (y + 0.5f) / cell * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v), angle = Mathf.Atan2(v, u);
                    float edge = radius;
                    foreach (var w in waves) edge += radius * w.x * Mathf.Sin(w.y * angle + w.z);
                    float alpha = Mathf.Clamp01((edge - d) / 0.03f + 0.5f);
                    foreach (var dot in dots)
                        alpha = Mathf.Max(alpha, Mathf.Clamp01((dot.z - Vector2.Distance(new Vector2(u, v), dot)) / 0.02f + 0.5f));
                    foreach (var s in spikes)
                    {
                        float along = u * s.x + v * s.y, across = Mathf.Abs(-u * s.y + v * s.x);
                        if (along <= 0f || along > s.z) continue;
                        float width = s.w * (1f - along / s.z * 0.8f);
                        alpha = Mathf.Max(alpha, Mathf.Clamp01((width - across) / 0.015f + 0.5f));
                    }
                    alpha *= Mathf.Clamp01((0.97f - Mathf.Max(Mathf.Abs(u), Mathf.Abs(v))) / 0.05f); // clear border: no bleeding between cells
                    float shade = Mathf.Lerp(1f, 0.7f, Mathf.Clamp01((d - edge * 0.75f) / (edge * 0.25f)));
                    byte c = (byte)(255f * shade);
                    pixels[(oy + y) * size + ox + x] = new Color32(c, c, c, (byte)(255f * alpha));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>A round soft-edged drop.</summary>
        static Texture2D DropTexture()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "BloodDrop" };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01((1f - Mathf.Sqrt(u * u + v * v)) / 0.35f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * a));
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }
    }
}
