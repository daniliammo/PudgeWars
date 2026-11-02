using UnityEngine;
using UnityEngine.Rendering;

namespace PudgeWars.Gameplay
{
    /// <summary>A soft team-coloured ring under living units at night; their models keep their own materials.</summary>
    public sealed class NightUnitGlow : MonoBehaviour
    {
        Unit unit;
        GameObject root;
        Material material;
        Mesh mesh;
        MeshRenderer ring;
        public float Strength { get; private set; }
        public Color Tint { get; private set; }
        static readonly int GlowColor = Shader.PropertyToID("_GlowColor");

        void Awake() => unit = GetComponent<Unit>();

        void Build()
        {
            var shader = Resources.Load<Shader>("Effects/NightUnitGlow");
            if (shader == null) return;
            material = new Material(shader);
            root = new GameObject("NightGlow");
            root.transform.SetParent(transform, false);
            mesh = new Mesh { name = "NightGroundRing" };
            mesh.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) };
            mesh.uv = new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            ring = root.AddComponent<MeshRenderer>();
            ring.sharedMaterial = material;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.lightProbeUsage = LightProbeUsage.Off;
            ring.reflectionProbeUsage = ReflectionProbeUsage.Off;
            ring.enabled = false;
        }

        void LateUpdate()
        {
            var arena = Arena.Instance;
            bool grounded = !(unit is Pudge hero) || !hero.IsFalling;
            Strength = unit != null && unit.IsAlive && grounded && arena != null && GraphicsQuality.Current >= GraphicsPreset.High
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, -0.24f, DayNightCycle.SolarHeight(arena.MatchTime))) : 0f;
            Tint = unit != null && unit.team == LocalPlayer.Team ? new Color(0.18f, 1f, 0.32f) : new Color(1f, 0.12f, 0.07f);
            if (root == null && Strength > 0f) Build();
            if (root == null) return;
            ring.enabled = Strength > 0f;
            root.transform.SetPositionAndRotation(transform.position + Vector3.up * 0.045f, Quaternion.identity);
            float radius = Mathf.Max(0.7f, unit.radius * 2.2f);
            var scale = transform.lossyScale;
            root.transform.localScale = new Vector3(radius / scale.x, 1f / scale.y, radius / scale.z);
            material.SetColor(GlowColor, Tint * (Strength * 0.8f));
        }

        void OnDisable()
        {
            Strength = 0f;
            if (ring != null) ring.enabled = false;
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root);
            if (material != null) Destroy(material);
            if (mesh != null) Destroy(mesh);
        }
    }
}
