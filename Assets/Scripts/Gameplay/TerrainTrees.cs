using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The trees of this terrain (its tree instances, drawn by the terrain itself): heroes walk around their trunks
    /// (<see cref="Obstacle"/>s), hooks fly through them. A tree can be cut down as in Dota (<see cref="Fell"/>): it
    /// leaves its stump, its top falls away from whoever cut it, lies a moment and sinks into the ground; the stump does
    /// not stop anyone. The tree grows back after <see cref="regrowTime"/> once nobody stands in its place.
    /// The parts come from Dota's models: the stump and the cut-off top (*_destruction) of each prototype; a tree
    /// without them falls whole and leaves nothing. The terrain plays on a copy of its data: the asset stays as built.
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

        const float FallAngle = 84f;    // lying on its branches, not quite flat
        const float BounceAngle = 7f;   // it bounces back this much as it hits the ground
        const float BounceTime = 0.3f;

        public static TerrainTrees Instance { get; private set; }

        /// <summary>A tree was cut down (its index, where it was cut from); the server's trees tell the clients.</summary>
        public event Action<int, Vector3> Felled;
        /// <summary>A cut tree grew back.</summary>
        public event Action<int> Regrown;

        Terrain terrain;
        TerrainData data;
        TreeInstance[] trees;
        bool[] down;
        GameObject[] stumpsUp;
        Coroutine[] regrowing;

        public int Count => trees.Length;
        public bool IsStanding(int index) => !down[index];

        void Awake()
        {
            terrain = GetComponent<Terrain>();
            // a cut tree changes the terrain's data: a copy of it, not the asset
            var asset = terrain.terrainData;
            data = Instantiate(asset);
            CopyGround(asset, data);
            terrain.terrainData = data;
            if (TryGetComponent<TerrainCollider>(out var ground)) ground.terrainData = data;
            trees = data.treeInstances;
            down = new bool[trees.Length];
            stumpsUp = new GameObject[trees.Length];
            regrowing = new Coroutine[trees.Length];
        }

        /// <summary>
        /// What a cloned terrain data may come without: how its ground layers blend (else all of it is the first
        /// layer, Radiant's grass on Dire's side too) and its grass. The heights and the trees come with the clone.
        /// </summary>
        static void CopyGround(TerrainData from, TerrainData to)
        {
            to.terrainLayers = from.terrainLayers;
            to.alphamapResolution = from.alphamapResolution;
            to.baseMapResolution = from.baseMapResolution;
            int res = from.alphamapResolution;
            to.SetAlphamaps(0, 0, from.GetAlphamaps(0, 0, res, res));

            to.detailPrototypes = from.detailPrototypes;
            to.SetDetailScatterMode(from.detailScatterMode);
            to.SetDetailResolution(from.detailResolution, from.detailResolutionPerPatch);
            int details = from.detailResolution;
            for (int i = 0; i < from.detailPrototypes.Length; i++)
                to.SetDetailLayer(0, 0, i, from.GetDetailLayer(0, 0, details, details, i));
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

        /// <summary>World position of a tree's foot.</summary>
        public Vector3 Position(int index) => terrain.transform.position + Vector3.Scale(trees[index].position, data.size);

        /// <summary>The standing tree nearest to <paramref name="p"/> on the ground plane within <paramref name="within"/>, or -1.</summary>
        public int Nearest(Vector3 p, float within)
        {
            int best = -1;
            float bestSqr = within * within;
            for (int i = 0; i < trees.Length; i++)
            {
                if (down[i]) continue;
                var d = Position(i) - p;
                d.y = 0f;
                if (d.sqrMagnitude >= bestSqr) continue;
                bestSqr = d.sqrMagnitude;
                best = i;
            }
            return best;
        }

        /// <summary>
        /// Cuts a standing tree down: it leaves its stump and its top falls away from <paramref name="from"/> (at once,
        /// without the fall, when <paramref name="animate"/> is off: a client joining a game in progress).
        /// </summary>
        public bool Fell(int index, Vector3 from, bool animate = true)
        {
            if (index < 0 || index >= trees.Length || down[index]) return false;
            down[index] = true;
            var hidden = trees[index];
            hidden.widthScale = hidden.heightScale = 0f;
            data.SetTreeInstance(index, hidden);
            UpdateObstacles();

            var tree = trees[index];
            var stump = Part(stumps, tree.prototypeIndex);
            if (stump != null) stumpsUp[index] = Place(stump, index, transform);
            if (animate) StartCoroutine(Fall(index, from));
            // a client's trees grow back when the server's do
            if (!Simulation.IsProxy) regrowing[index] = StartCoroutine(RegrowLater(index));
            Felled?.Invoke(index, from);
            return true;
        }

        /// <summary>A cut tree stands again; its stump goes.</summary>
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
            foreach (var h in Hookable.All)
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
        IEnumerator Fall(int index, Vector3 from)
        {
            var tree = trees[index];
            var top = Part(fallingParts, tree.prototypeIndex);
            var stump = Part(stumps, tree.prototypeIndex);
            if (top == null) top = data.treePrototypes[tree.prototypeIndex].prefab; // falls whole
            if (top == null) yield break;
            float cut = MeshBounds(top).min.y;
            float foot = stump != null ? Mathf.Min(cut, MeshBounds(stump).max.y) : cut;

            var at = Position(index);
            var pivot = new GameObject("Falling tree").transform;
            pivot.SetParent(transform, false);
            pivot.position = at + Vector3.up * (foot * tree.heightScale);
            var piece = Place(top, index, pivot);
            piece.transform.position += Vector3.up * ((foot - cut) * tree.heightScale);

            var away = at - from;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
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

        /// <summary>A part of a tree where the tree stands, turned and scaled as it.</summary>
        GameObject Place(GameObject prefab, int index, Transform parent)
        {
            var tree = trees[index];
            var part = Instantiate(prefab, parent);
            part.transform.SetPositionAndRotation(Position(index), Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f));
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
