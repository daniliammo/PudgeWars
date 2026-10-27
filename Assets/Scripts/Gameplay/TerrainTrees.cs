using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The trees of this terrain (its tree instances, drawn by the terrain itself): heroes walk around their trunks
    /// (<see cref="Obstacle"/>s), hooks fly through them. A tree can be cut down as in Dota (<see cref="Fell"/>): it
    /// leaves its stump, its top falls away from whoever cut it, lies a moment and sinks into the ground; the stump does
    /// not stop anyone. The tree grows back after <see cref="regrowTime"/> once nobody stands in its place.
    /// The parts come from Dota's models: the stump and the cut-off top (*_destruction) of each prototype; a tree
    /// without them falls whole and leaves nothing. The falling crown breaks up: the trunk tips over alone, and the
    /// crown flies apart in several pieces that drift slowly down through the air around the tree, lie a moment and
    /// sink as the trunk does (the pieces are cut from the mesh by sectors around the trunk, once per model).
    /// The terrain plays on a copy of its data: the asset stays as built.
    /// A tree can also be planted (Iron Branch, <see cref="Plant(Vector3, float)"/>): it is a model of its own, not a
    /// terrain instance, and stands for good (or a while, if given a lifetime); cut down it falls whole and never grows
    /// back. Trees are named by a handle: a terrain tree by its index, a planted one by <see cref="PlantedBase"/> + its
    /// number.
    /// </summary>
    [RequireComponent(typeof(Terrain))]
    public class TerrainTrees : MonoBehaviour
    {
        [Tooltip("Trunk radius heroes cannot walk into, metres, whatever the tree's scale.")]
        public float radius = 0.5f;
        [Tooltip("Stump of each tree prototype (by index), none if the model has no stump.")]
        public GameObject[] stumps = Array.Empty<GameObject>();
        [Tooltip("Cut-off top of each tree prototype (by index): it falls; without one the whole tree falls.")]
        public GameObject[] fallingParts = Array.Empty<GameObject>();
        [Tooltip("Seconds the top takes to fall to the ground.")]
        public float fallTime = 1.1f;
        [Tooltip("Seconds it lies on the ground before sinking into it.")]
        public float lieTime = 1.5f;
        [Tooltip("Seconds it takes to sink into the ground.")]
        public float sinkTime = 1.5f;
        [Tooltip("Seconds until a cut tree grows back (Dota: 5 minutes).")]
        public float regrowTime = 300f;

        [Header("Crown breaking up")]
        [Tooltip("The crown breaks into about this many pieces (sectors around the trunk, upper and lower).")]
        [Min(1)] public int crownPieces = 8;
        [Tooltip("Pieces drift outward at a speed in this range, m/s; the air slows them.")]
        public Vector2 crownDrift = new(0.7f, 1.8f);
        [Tooltip("They first rise at a speed in this range, m/s.")]
        public Vector2 crownLift = new(0.4f, 1.2f);
        [Tooltip("How fast a piece sinks through the air at most, m/s: leaves fall slowly.")]
        public float crownFallSpeed = 1.5f;
        [Tooltip("How fast the pieces reach that speed, m/s².")]
        public float crownGravity = 3.5f;
        [Tooltip("How fast the air takes the sideways speed away, per second.")]
        public float crownDrag = 0.7f;
        [Tooltip("Pieces tumble at a speed in this range, degrees per second.")]
        public Vector2 crownSpin = new(20f, 70f);

        const float FallAngle = 84f;    // lying on its branches, not quite flat
        const float BounceAngle = 7f;   // it bounces back this much as it hits the ground
        const float BounceTime = 0.3f;

        /// <summary>Handles of planted trees start here (terrain trees are their indices below it).</summary>
        public const int PlantedBase = 1 << 20;

        public static TerrainTrees Instance { get; private set; }
        public static bool IsPlanted(int tree) => tree >= PlantedBase;

        [Tooltip("A planted tree is this tall relative to the tree it takes after (the nearest one).")]
        public float plantedScale = 0.75f;

        /// <summary>A tree was cut down (its handle, where it was cut from); the server's trees tell the clients.</summary>
        public event Action<int, Vector3> Felled;
        /// <summary>A cut tree grew back.</summary>
        public event Action<int> Regrown;
        /// <summary>A tree was planted: its handle, foot, prototype, rotation (radians) and scale.</summary>
        public event Action<int, Vector3, int, float, float> Planted;
        /// <summary>A planted tree is gone: its time ran out or it was cut down (after <see cref="Felled"/>).</summary>
        public event Action<int> Unplanted;

        /// <summary>Where a tree stands and how it looks (a terrain instance or a planted tree).</summary>
        struct Spot
        {
            public Vector3 position;
            public int prototype;
            public float rotation, widthScale, heightScale;
        }

        class PlantedTree
        {
            public Spot spot;
            public GameObject model;
            public Coroutine expiry;
        }

        /// <summary>A falling part's mesh split up: the trunk and the crown's pieces (each centred on itself).</summary>
        class CrownSplit
        {
            public Mesh trunk;
            public Mesh[] pieces;
            public Vector3[] centres; // of the pieces, in the part's mesh space
        }

        static readonly Dictionary<(Mesh, int), CrownSplit> splits = new();

        Terrain terrain;
        TerrainData data;
        TreeInstance[] trees;
        bool[] down;
        GameObject[] stumpsUp;
        Coroutine[] regrowing;
        readonly Dictionary<int, PlantedTree> planted = new();
        int nextPlanted;

        /// <summary>Trees of the terrain (planted ones not counted).</summary>
        public int Count => trees.Length;
        public bool IsStanding(int tree) =>
            IsPlanted(tree) ? planted.ContainsKey(tree) : tree >= 0 && tree < trees.Length && !down[tree];
        /// <summary>Handles of the planted trees standing now.</summary>
        public IEnumerable<int> PlantedTrees => planted.Keys;

        void Awake()
        {
            terrain = GetComponent<Terrain>();
            // a cut tree changes the terrain's data: a copy of it, not the asset
            data = Instantiate(terrain.terrainData);
            terrain.terrainData = data;
            if (TryGetComponent<TerrainCollider>(out var ground)) ground.terrainData = data;
            trees = data.treeInstances;
            down = new bool[trees.Length];
            stumpsUp = new GameObject[trees.Length];
            regrowing = new Coroutine[trees.Length];
        }

        void OnEnable()
        {
            Instance = this;
            UpdateObstacles();
        }

        void OnDisable()
        {
            Obstacle.Unregister(this);
            if (Instance == this) Instance = null;
        }

        void OnDestroy()
        {
            if (data != null) Destroy(data);
        }

        /// <summary>World position of a tree's foot (a terrain tree's or a planted one's).</summary>
        public Vector3 Position(int tree) =>
            IsPlanted(tree) ? planted.TryGetValue(tree, out var p) ? p.spot.position : Vector3.zero
                : terrain.transform.position + Vector3.Scale(trees[tree].position, data.size);

        Spot SpotOf(int index)
        {
            var tree = trees[index];
            return new Spot
            {
                position = Position(index), prototype = tree.prototypeIndex, rotation = tree.rotation,
                widthScale = tree.widthScale, heightScale = tree.heightScale,
            };
        }

        /// <summary>The standing tree (terrain or planted) nearest to <paramref name="p"/> on the ground plane within <paramref name="within"/>, or -1.</summary>
        public int Nearest(Vector3 p, float within)
        {
            int best = -1;
            float bestSqr = within * within;
            void Consider(int tree, Vector3 at)
            {
                var d = at - p;
                d.y = 0f;
                if (d.sqrMagnitude >= bestSqr) return;
                bestSqr = d.sqrMagnitude;
                best = tree;
            }
            for (int i = 0; i < trees.Length; i++)
                if (!down[i]) Consider(i, Position(i));
            foreach (var (handle, tree) in planted) Consider(handle, tree.spot.position);
            return best;
        }

        /// <summary>
        /// A tree may be planted here: on the arena's ground (not over the pit), clear of the trees and the heroes.
        /// </summary>
        public bool CanPlant(Vector3 p)
        {
            var arena = Arena.Instance;
            if (arena != null && arena.IsOverPit(p)) return false;
            if (Obstacle.Blocks(p, radius)) return false;
            foreach (var h in Pudge.All)
            {
                var d = h.transform.position - p;
                d.y = 0f;
                float r = h.radius + radius;
                if (h.IsAlive && !h.IsFalling && d.sqrMagnitude < r * r) return false;
            }
            return true;
        }

        /// <summary>
        /// Server / offline: plants a tree at <paramref name="position"/> that stands <paramref name="lifetime"/>
        /// seconds (for good if not positive); it takes after the nearest terrain tree (the side's kind), a little
        /// smaller. Its handle, or -1.
        /// </summary>
        public int Plant(Vector3 position, float lifetime)
        {
            if (!CanPlant(position)) return -1;
            int prototype = 0;
            float scale = plantedScale, bestSqr = float.MaxValue;
            for (int i = 0; i < trees.Length; i++)
            {
                var d = Position(i) - position;
                d.y = 0f;
                if (d.sqrMagnitude >= bestSqr) continue;
                bestSqr = d.sqrMagnitude;
                prototype = trees[i].prototypeIndex;
                scale = trees[i].heightScale * plantedScale;
            }
            int handle = PlantedBase + nextPlanted++;
            Plant(handle, position, prototype, UnityEngine.Random.Range(0f, Mathf.PI * 2f), scale);
            if (lifetime > 0f && planted.TryGetValue(handle, out var tree)) tree.expiry = StartCoroutine(ExpireLater(handle, lifetime));
            return handle;
        }

        /// <summary>A planted tree as the server made it (a client's copy); false if it stands already.</summary>
        public bool Plant(int handle, Vector3 position, int prototype, float rotation, float scale)
        {
            if (!IsPlanted(handle) || planted.ContainsKey(handle)) return false;
            if (data.treePrototypes.Length == 0) return false;
            prototype = Mathf.Clamp(prototype, 0, data.treePrototypes.Length - 1);
            var spot = new Spot
            {
                position = new Vector3(position.x, terrain.SampleHeight(position) + terrain.transform.position.y, position.z),
                prototype = prototype, rotation = rotation, widthScale = scale, heightScale = scale,
            };
            var prefab = data.treePrototypes[prototype].prefab;
            var tree = new PlantedTree { spot = spot, model = prefab != null ? Place(prefab, spot, transform) : null };
            planted[handle] = tree;
            nextPlanted = Mathf.Max(nextPlanted, handle - PlantedBase + 1);
            UpdateObstacles();
            Planted?.Invoke(handle, spot.position, prototype, rotation, scale);
            return true;
        }

        /// <summary>A planted tree goes without falling (its time ran out); false if there is none.</summary>
        public bool Unplant(int handle)
        {
            if (!planted.Remove(handle, out var tree)) return false;
            if (tree.expiry != null) StopCoroutine(tree.expiry);
            if (tree.model != null) Destroy(tree.model);
            UpdateObstacles();
            Unplanted?.Invoke(handle);
            return true;
        }

        IEnumerator ExpireLater(int handle, float lifetime)
        {
            yield return new WaitForSeconds(lifetime);
            if (planted.TryGetValue(handle, out var tree)) tree.expiry = null;
            Unplant(handle);
        }

        /// <summary>
        /// Cuts a standing tree down: it leaves its stump and its top falls away from <paramref name="from"/> (at once,
        /// without the fall, when <paramref name="animate"/> is off: a client joining a game in progress).
        /// </summary>
        public bool Fell(int index, Vector3 from, bool animate = true)
        {
            if (IsPlanted(index)) return FellPlanted(index, from, animate);
            if (index < 0 || index >= trees.Length || down[index]) return false;
            down[index] = true;
            var hidden = trees[index];
            hidden.widthScale = hidden.heightScale = 0f;
            data.SetTreeInstance(index, hidden);
            UpdateObstacles();

            var spot = SpotOf(index);
            var stump = Part(stumps, spot.prototype);
            if (stump != null) stumpsUp[index] = Place(stump, spot, transform);
            if (animate) StartCoroutine(Fall(spot, from, whole: false));
            // a client's trees grow back when the server's do
            if (!Simulation.IsProxy) regrowing[index] = StartCoroutine(RegrowLater(index));
            Felled?.Invoke(index, from);
            return true;
        }

        /// <summary>A planted tree is cut down: it falls whole, leaves no stump and is gone for good.</summary>
        bool FellPlanted(int handle, Vector3 from, bool animate)
        {
            if (!planted.TryGetValue(handle, out var tree)) return false;
            if (tree.model != null) tree.model.SetActive(false); // the falling copy takes its place
            if (animate) StartCoroutine(Fall(tree.spot, from, whole: true));
            Felled?.Invoke(handle, from);
            Unplant(handle);
            return true;
        }

        /// <summary>A cut tree stands again; its stump goes. Planted trees do not grow back.</summary>
        public bool Regrow(int index)
        {
            if (index < 0 || index >= trees.Length || !down[index]) return false;
            down[index] = false;
            if (regrowing[index] != null) StopCoroutine(regrowing[index]);
            regrowing[index] = null;
            data.SetTreeInstance(index, trees[index]);
            if (stumpsUp[index] != null) Destroy(stumpsUp[index]);
            stumpsUp[index] = null;
            UpdateObstacles();
            Regrown?.Invoke(index);
            return true;
        }

        IEnumerator RegrowLater(int index)
        {
            yield return new WaitForSeconds(regrowTime);
            // as in Dota, not on top of a hero
            while (HeroAt(index)) yield return new WaitForSeconds(1f);
            regrowing[index] = null;
            Regrow(index);
        }

        bool HeroAt(int index)
        {
            var p = Position(index);
            foreach (var h in Pudge.All)
            {
                var d = h.transform.position - p;
                d.y = 0f;
                float r = h.radius + radius;
                if (h.IsAlive && d.sqrMagnitude < r * r) return true;
            }
            return false;
        }

        /// <summary>
        /// The top tips over about the foot of its trunk, faster and faster, bounces on the ground, lies there and sinks.
        /// It is set down on the stump (the files leave a gap between them).
        /// </summary>
        IEnumerator Fall(Spot tree, Vector3 from, bool whole)
        {
            var top = whole ? null : Part(fallingParts, tree.prototype);
            var stump = whole ? null : Part(stumps, tree.prototype);
            if (top == null) top = data.treePrototypes[tree.prototype].prefab; // falls whole
            if (top == null) yield break;
            float cut = MeshBounds(top).min.y;
            float foot = stump != null ? Mathf.Min(cut, MeshBounds(stump).max.y) : cut;

            var at = tree.position;
            var pivot = new GameObject("Falling tree").transform;
            pivot.SetParent(transform, false);
            pivot.position = at + Vector3.up * (foot * tree.heightScale);
            var piece = Place(top, tree, pivot);
            piece.transform.position += Vector3.up * ((foot - cut) * tree.heightScale);

            var away = at - from;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
            BreakCrown(piece, at, away.normalized);
            var axis = Vector3.Cross(Vector3.up, away.normalized); // tips the top toward away
            for (float t = 0f; t < fallTime; t += Time.deltaTime)
            {
                float k = t / fallTime;
                pivot.rotation = Quaternion.AngleAxis(FallAngle * k * k, axis); // like a falling pole: slow, then fast
                yield return null;
            }
            for (float t = 0f; t < BounceTime; t += Time.deltaTime)
            {
                pivot.rotation = Quaternion.AngleAxis(FallAngle - BounceAngle * Mathf.Sin(Mathf.PI * t / BounceTime), axis);
                yield return null;
            }
            pivot.rotation = Quaternion.AngleAxis(FallAngle, axis);
            yield return new WaitForSeconds(lieTime);

            var start = pivot.position;
            float depth = RendererBounds(piece).size.y + 0.3f;
            for (float t = 0f; t < sinkTime; t += Time.deltaTime)
            {
                pivot.position = start + Vector3.down * (depth * t / sinkTime);
                yield return null;
            }
            Destroy(pivot.gameObject);
        }

        /// <summary>
        /// The falling part keeps only its trunk; the crown's pieces fly off it outward (and a little away from whoever
        /// cut the tree), each on its own.
        /// </summary>
        void BreakCrown(GameObject part, Vector3 foot, Vector3 away)
        {
            var filter = part.GetComponentInChildren<MeshFilter>();
            var renderer = filter != null ? filter.GetComponent<MeshRenderer>() : null;
            if (renderer == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return;
            var split = Split(filter.sharedMesh, crownPieces);
            if (split == null) return;
            if (split.trunk != null) filter.sharedMesh = split.trunk;
            else renderer.enabled = false;
            var space = filter.transform;
            for (int i = 0; i < split.pieces.Length; i++)
            {
                var go = new GameObject("Crown piece");
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation(space.TransformPoint(split.centres[i]), space.rotation);
                go.transform.localScale = space.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = split.pieces[i];
                var pieceRenderer = go.AddComponent<MeshRenderer>();
                pieceRenderer.sharedMaterials = renderer.sharedMaterials;
                pieceRenderer.shadowCastingMode = renderer.shadowCastingMode;

                var outward = go.transform.position - foot;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : away;
                var velocity = (outward + away * 0.4f).normalized * UnityEngine.Random.Range(crownDrift.x, crownDrift.y)
                               + Vector3.up * UnityEngine.Random.Range(crownLift.x, crownLift.y);
                StartCoroutine(Drift(go.transform, pieceRenderer, velocity, UnityEngine.Random.onUnitSphere,
                    UnityEngine.Random.Range(crownSpin.x, crownSpin.y)));
            }
        }

        /// <summary>A crown piece drifts down slowly, tumbling, until it touches the ground; lies there, then sinks.</summary>
        IEnumerator Drift(Transform piece, Renderer shown, Vector3 velocity, Vector3 spinAxis, float spin)
        {
            for (float t = 0f; t < 20f; t += Time.deltaTime) // never forever, whatever the ground
            {
                float dt = Time.deltaTime;
                velocity.y = Mathf.MoveTowards(velocity.y, -crownFallSpeed, crownGravity * dt);
                float keep = Mathf.Exp(-crownDrag * dt);
                velocity.x *= keep;
                velocity.z *= keep;
                piece.position += velocity * dt;
                piece.Rotate(spinAxis, spin * dt, Space.World);
                float ground = terrain.SampleHeight(piece.position) + terrain.transform.position.y;
                if (shown.bounds.min.y <= ground + 0.05f) break;
                yield return null;
            }
            yield return new WaitForSeconds(lieTime);
            var start = piece.position;
            float depth = shown.bounds.size.y + 0.3f;
            for (float t = 0f; t < sinkTime; t += Time.deltaTime)
            {
                piece.position = start + Vector3.down * (depth * t / sinkTime);
                yield return null;
            }
            Destroy(piece.gameObject);
        }

        /// <summary>
        /// Cuts a falling part's mesh into its trunk (thin and low: what is near the axis below the crown) and the
        /// crown's pieces: sectors around the axis, an upper and a lower one each. Made once per model; null if the
        /// mesh has no crown to break.
        /// </summary>
        static CrownSplit Split(Mesh mesh, int pieceCount)
        {
            if (splits.TryGetValue((mesh, pieceCount), out var cached)) return cached;
            var vertices = mesh.vertices;
            var bounds = mesh.bounds;
            var axis = new Vector2(bounds.center.x, bounds.center.z);
            float maxR = 0f;
            foreach (var v in vertices) maxR = Mathf.Max(maxR, (new Vector2(v.x, v.z) - axis).magnitude);
            float trunkR = maxR * 0.18f;

            // where the crown begins: low among the triangles well off the trunk
            var submeshes = new int[mesh.subMeshCount][];
            for (int s = 0; s < submeshes.Length; s++) submeshes[s] = mesh.GetTriangles(s);
            var wide = new List<float>();
            foreach (var tris in submeshes)
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var c = (vertices[tris[i]] + vertices[tris[i + 1]] + vertices[tris[i + 2]]) / 3f;
                    if ((new Vector2(c.x, c.z) - axis).magnitude > trunkR * 2f) wide.Add(c.y);
                }
            if (wide.Count == 0)
            {
                splits[(mesh, pieceCount)] = null;
                return null;
            }
            wide.Sort();
            float crownBottom = wide[wide.Count / 10];
            float crownMiddle = wide[wide.Count / 2];

            int sectors = Mathf.Max(1, (pieceCount + 1) / 2), bands = pieceCount > 1 ? 2 : 1;
            int groups = sectors * bands;
            // per group (the trunk last), per submesh: the triangles' vertex indices
            var grouped = new List<int>[groups + 1, submeshes.Length];
            for (int g = 0; g <= groups; g++)
                for (int s = 0; s < submeshes.Length; s++) grouped[g, s] = new List<int>();
            for (int s = 0; s < submeshes.Length; s++)
            {
                var tris = submeshes[s];
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var c = (vertices[tris[i]] + vertices[tris[i + 1]] + vertices[tris[i + 2]]) / 3f;
                    var flat = new Vector2(c.x, c.z) - axis;
                    int g;
                    if (flat.magnitude < trunkR && c.y < crownBottom) g = groups;
                    else
                    {
                        float angle = Mathf.Atan2(flat.y, flat.x) + Mathf.PI; // 0..2π
                        int sector = Mathf.Min(sectors - 1, (int)(angle / (Mathf.PI * 2f) * sectors));
                        int band = bands > 1 && c.y >= crownMiddle ? 1 : 0;
                        g = band * sectors + sector;
                    }
                    grouped[g, s].Add(tris[i]);
                    grouped[g, s].Add(tris[i + 1]);
                    grouped[g, s].Add(tris[i + 2]);
                }
            }

            var split = new CrownSplit();
            var pieces = new List<Mesh>();
            var centres = new List<Vector3>();
            for (int g = 0; g <= groups; g++)
            {
                bool trunk = g == groups;
                var piece = Extract(mesh, grouped, g, centred: !trunk, out var centre);
                if (piece == null) continue;
                if (trunk) split.trunk = piece;
                else
                {
                    pieces.Add(piece);
                    centres.Add(centre);
                }
            }
            split.pieces = pieces.ToArray();
            split.centres = centres.ToArray();
            if (split.pieces.Length == 0) split = null;
            splits[(mesh, pieceCount)] = split;
            return split;
        }

        /// <summary>A mesh of one group's triangles (all submeshes kept), centred on its vertices' middle if asked.</summary>
        static Mesh Extract(Mesh source, List<int>[,] grouped, int group, bool centred, out Vector3 centre)
        {
            centre = Vector3.zero;
            int submeshes = grouped.GetLength(1);
            var remap = new Dictionary<int, int>();
            var order = new List<int>();
            for (int s = 0; s < submeshes; s++)
                foreach (int v in grouped[group, s])
                    if (!remap.ContainsKey(v))
                    {
                        remap[v] = order.Count;
                        order.Add(v);
                    }
            if (order.Count == 0) return null;

            var positions = source.vertices;
            var normals = source.normals;
            var tangents = source.tangents;
            var colors = source.colors;
            var uv0 = new List<Vector4>();
            var uv1 = new List<Vector4>();
            source.GetUVs(0, uv0);
            source.GetUVs(1, uv1);
            var p = new Vector3[order.Count];
            for (int i = 0; i < order.Count; i++) p[i] = positions[order[i]];
            if (centred)
            {
                foreach (var v in p) centre += v;
                centre /= p.Length;
                for (int i = 0; i < p.Length; i++) p[i] -= centre;
            }
            var mesh = new Mesh { name = source.name + (centred ? "_piece" : "_trunk") };
            if (order.Count > ushort.MaxValue) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = p;
            if (normals.Length == positions.Length) mesh.normals = order.Select(i => normals[i]).ToArray();
            if (tangents.Length == positions.Length) mesh.tangents = order.Select(i => tangents[i]).ToArray();
            if (colors.Length == positions.Length) mesh.colors = order.Select(i => colors[i]).ToArray();
            if (uv0.Count == positions.Length) mesh.SetUVs(0, order.Select(i => uv0[i]).ToList());
            if (uv1.Count == positions.Length) mesh.SetUVs(1, order.Select(i => uv1[i]).ToList());
            mesh.subMeshCount = submeshes;
            for (int s = 0; s < submeshes; s++)
                mesh.SetTriangles(grouped[group, s].Select(v => remap[v]).ToList(), s, calculateBounds: false);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A part of a tree where the tree stands, turned and scaled as it.</summary>
        static GameObject Place(GameObject prefab, Spot tree, Transform parent)
        {
            var part = Instantiate(prefab, parent);
            part.transform.SetPositionAndRotation(tree.position, Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f));
            part.transform.localScale = new Vector3(tree.widthScale, tree.heightScale, tree.widthScale);
            return part;
        }

        static GameObject Part(GameObject[] parts, int prototype) => prototype >= 0 && prototype < parts.Length ? parts[prototype] : null;

        /// <summary>Bounds of a part's mesh in its own space (the meshes sit at the prefab's origin).</summary>
        static Bounds MeshBounds(GameObject prefab)
        {
            var filter = prefab.GetComponentInChildren<MeshFilter>();
            return filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds();
        }

        static Bounds RendererBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        void UpdateObstacles()
        {
            if (trees == null) return;
            var circles = new List<ObstacleCircle>(trees.Length);
            for (int i = 0; i < trees.Length; i++)
                if (!down[i]) circles.Add(new ObstacleCircle(Position(i), radius));
            foreach (var tree in planted.Values) circles.Add(new ObstacleCircle(tree.spot.position, radius));
            Obstacle.Register(this, circles.ToArray());
        }

        // in the editor's play mode: the component's menu cuts down the tree nearest to the player
        [ContextMenu("Срубить дерево рядом с игроком")]
        void FellNearPlayer()
        {
            var hero = LocalPlayer.Hero;
            if (hero == null) return;
            int index = Nearest(hero.transform.position, float.MaxValue);
            if (index >= 0) Fell(index, hero.transform.position);
        }
    }
}
