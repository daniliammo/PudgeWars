using UnityEngine;
using UnityEngine.Rendering;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The selection on the ground under a picked unit or tree: a ring that sways (its edge runs in slow waves and it
    /// breathes) and thin rings rippling out of it and fading away, one after another. Red under enemies, green under
    /// the local player's side (the own hero too), white under trees; all see-through (Resources/Effects/SelectionRing).
    /// Follows a moving unit, hides while it is dead or falling, goes when a picked tree is cut down.
    /// </summary>
    public class SelectionMarker : MonoBehaviour
    {
        public Color enemyColor = new(1f, 0.22f, 0.16f, 0.55f);
        public Color allyColor = new(0.35f, 1f, 0.4f, 0.55f);
        public Color treeColor = new(1f, 1f, 1f, 0.5f);
        [Tooltip("The ring's radius relative to a unit's (or a trunk's) radius.")]
        public float radiusScale = 1.5f;
        public float lineWidth = 0.07f;
        [Tooltip("How far the ring's edge sways in and out, relative to its radius.")]
        public float sway = 0.05f;
        [Tooltip("Waves around the ring's edge.")]
        public int swayWaves = 3;
        [Tooltip("How fast the waves run around, radians per second.")]
        public float swaySpeed = 3f;
        [Tooltip("The whole ring breathes by this much of its radius.")]
        public float breath = 0.06f;
        public float breathSpeed = 4f;
        [Tooltip("Rings rippling out at once.")]
        [Range(1, 5)] public int ripples = 3;
        [Tooltip("Seconds a ripple takes to grow out and fade.")]
        public float rippleTime = 1.2f;
        [Tooltip("How far a ripple grows past the ring, relative to its radius.")]
        public float rippleGrowth = 0.9f;
        [Tooltip("A ripple starts this see-through relative to the ring.")]
        [Range(0f, 1f)] public float rippleAlpha = 0.8f;

        const int Points = 72;

        static SelectionMarker instance;

        /// <summary>The marker exists (reading <see cref="Instance"/> would make one).</summary>
        public static bool HasInstance => instance != null;

        /// <summary>The marker of the scene, made when first needed.</summary>
        public static SelectionMarker Instance
        {
            get
            {
                if (instance == null) instance = new GameObject("SelectionMarker").AddComponent<SelectionMarker>();
                return instance;
            }
        }

        /// <summary>The picked unit, or null.</summary>
        public Unit SelectedUnit { get; private set; }
        /// <summary>The picked tree (a <see cref="TerrainTrees"/> handle), or -1.</summary>
        public int SelectedTree { get; private set; } = -1;
        /// <summary>The ring is drawn now.</summary>
        public bool IsShown => ring != null && ring.enabled;
        /// <summary>Its colour now (the side's or a tree's).</summary>
        public Color CurrentColor { get; private set; }
        public LineRenderer Ring => ring;
        public LineRenderer[] Ripples => rippleLines;

        LineRenderer ring;
        LineRenderer[] rippleLines;
        Material material;

        void Awake()
        {
            if (instance == null) instance = this;
            var shader = Resources.Load<Shader>("Effects/SelectionRing");
            material = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            ring = Line("Ring");
            rippleLines = new LineRenderer[ripples];
            for (int i = 0; i < ripples; i++) rippleLines[i] = Line("Ripple");
            Hide();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            if (material != null) Destroy(material);
        }

        public void Select(Unit unit)
        {
            SelectedUnit = unit;
            SelectedTree = -1;
        }

        public void SelectTree(int tree)
        {
            SelectedUnit = null;
            SelectedTree = tree;
        }

        public void Clear()
        {
            SelectedUnit = null;
            SelectedTree = -1;
            Hide();
        }

        void LateUpdate()
        {
            if (!Target(out var centre, out float radius, out var color))
            {
                Hide();
                return;
            }
            CurrentColor = color;
            float t = Time.time;
            float r = radius * (1f + breath * Mathf.Sin(t * breathSpeed));
            Draw(ring, centre, r, color, sway, t);
            for (int i = 0; i < rippleLines.Length; i++)
            {
                float phase = Mathf.Repeat(t / rippleTime + (float)i / rippleLines.Length, 1f);
                var c = color;
                c.a *= rippleAlpha * (1f - phase) * (1f - phase); // fades faster as it grows
                Draw(rippleLines[i], centre, r * (1f + rippleGrowth * phase), c, sway * 0.5f, t);
            }
        }

        /// <summary>Where the ring goes, how big and in what colour; false when there is nothing to show.</summary>
        bool Target(out Vector3 centre, out float radius, out Color color)
        {
            centre = default;
            radius = 0f;
            color = treeColor;
            if (SelectedUnit != null)
            {
                if (!SelectedUnit.IsAlive || (SelectedUnit is Pudge hero && hero.IsFalling)) return false;
                centre = SelectedUnit.transform.position;
                radius = SelectedUnit.radius * radiusScale;
                color = SelectedUnit.team == LocalPlayer.Team ? allyColor : enemyColor;
                return true;
            }
            if (SelectedTree < 0) return false;
            var trees = TerrainTrees.Instance;
            if (trees == null || !trees.IsStanding(SelectedTree))
            {
                SelectedTree = -1; // cut down: the selection goes with it
                return false;
            }
            centre = trees.Position(SelectedTree);
            radius = trees.radius * radiusScale * 1.2f;
            return true;
        }

        /// <summary>A ring of <paramref name="radius"/> around <paramref name="centre"/>, its edge swaying in waves.</summary>
        void Draw(LineRenderer line, Vector3 centre, float radius, Color color, float swayAmount, float t)
        {
            line.startColor = line.endColor = color;
            for (int i = 0; i < Points; i++)
            {
                float a = i * Mathf.PI * 2f / Points;
                float r = radius * (1f + swayAmount * Mathf.Sin(swayWaves * a + t * swaySpeed));
                line.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * r, 0.05f, Mathf.Sin(a) * r));
            }
            line.enabled = true;
        }

        void Hide()
        {
            if (ring != null) ring.enabled = false;
            if (rippleLines != null)
                foreach (var line in rippleLines) line.enabled = false;
        }

        /// <summary>A flat closed line lying on the ground.</summary>
        LineRenderer Line(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // TransformZ alignment: flat, facing up
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = Points;
            line.widthMultiplier = lineWidth;
            line.alignment = LineAlignment.TransformZ;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }
    }
}
