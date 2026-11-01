using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PudgeWars.Gameplay
{
    /// <summary>Local team-coloured night lighting, independent of the unit's original materials.</summary>
    public sealed class NightUnitGlow : MonoBehaviour
    {
        struct Shell { public Renderer source, glow; }
        readonly List<Shell> shells = new();
        Unit unit;
        GameObject root;
        Material material;
        Light pool;
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
            // Outside the animated model: ragdoll copies must not inherit the glow.
            root.transform.SetParent(transform, false);
            var animator = GetComponentInChildren<Animator>(true);
            var sources = animator != null ? animator.GetComponentsInChildren<Renderer>(true) : GetComponentsInChildren<Renderer>(true);
            foreach (var source in sources)
            {
                if (source.transform.IsChildOf(root.transform) || source.name == "TeamMarker") continue;
                Renderer glow;
                Mesh mesh;
                var go = new GameObject(source.name + " Glow");
                go.layer = source.gameObject.layer;
                go.transform.SetParent(root.transform, false);
                if (source is SkinnedMeshRenderer skin && skin.sharedMesh != null)
                {
                    mesh = skin.sharedMesh;
                    var copy = go.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = mesh;
                    copy.bones = skin.bones;
                    copy.rootBone = skin.rootBone;
                    copy.localBounds = skin.localBounds;
                    copy.quality = skin.quality;
                    glow = copy;
                }
                else if (source is MeshRenderer && source.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    mesh = filter.sharedMesh;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    glow = go.AddComponent<MeshRenderer>();
                }
                else { Destroy(go); continue; }
                var materials = new Material[mesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                glow.sharedMaterials = materials;
                glow.shadowCastingMode = ShadowCastingMode.Off;
                glow.receiveShadows = false;
                glow.lightProbeUsage = LightProbeUsage.Off;
                glow.reflectionProbeUsage = ReflectionProbeUsage.Off;
                glow.enabled = false;
                shells.Add(new Shell { source = source, glow = glow });
            }
            pool = root.AddComponent<Light>();
            pool.type = LightType.Point;
            pool.range = 3.5f;
            pool.shadows = LightShadows.None;
            pool.enabled = false;
        }

        void LateUpdate()
        {
            var arena = Arena.Instance;
            Strength = unit != null && unit.IsAlive && arena != null && GraphicsQuality.Current >= GraphicsPreset.High
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, -0.24f, DayNightCycle.SolarHeight(arena.MatchTime))) : 0f;
            Tint = unit != null && unit.team == LocalPlayer.Team ? new Color(0.18f, 1f, 0.32f) : new Color(1f, 0.12f, 0.07f);
            if (root == null && Strength > 0f) Build();
            if (root == null) return;
            pool.enabled = Strength > 0f;
            pool.color = Tint;
            pool.intensity = Strength * 1.6f;
            pool.transform.localPosition = Vector3.up * 0.8f;
            // The light is on the root; shells follow the actual animated renderer in world space.
            material.SetColor(GlowColor, Tint * (Strength * 0.75f));
            foreach (var shell in shells)
            {
                if (shell.glow == null) continue;
                shell.glow.enabled = Strength > 0f && shell.source != null && shell.source.enabled && shell.source.gameObject.activeInHierarchy;
                if (!shell.glow.enabled) continue;
                var original = shell.source.transform;
                shell.glow.transform.SetPositionAndRotation(original.position, original.rotation);
                // Account for the unit root's scale (the shell itself is a child of it).
                var parentScale = root.transform.lossyScale;
                shell.glow.transform.localScale = new Vector3(original.lossyScale.x / parentScale.x,
                    original.lossyScale.y / parentScale.y, original.lossyScale.z / parentScale.z);
                if (shell.source is SkinnedMeshRenderer skin && shell.glow is SkinnedMeshRenderer copy)
                {
                    copy.localBounds = skin.localBounds;
                    for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++) copy.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                }
            }
        }

        void OnDisable()
        {
            Strength = 0f;
            if (pool != null) pool.enabled = false;
            foreach (var shell in shells) if (shell.glow != null) shell.glow.enabled = false;
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root);
            if (material != null) Destroy(material);
        }
    }
}
