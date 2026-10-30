using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The reach of an aura drawn on the ground around each ally giving it (Resources/Effects/AuraRange.shader): shown
    /// while the aura's effect is hovered on the effect bar (<see cref="EffectBar"/>), following the givers, fading in
    /// and out.
    /// </summary>
    public class AuraRange : MonoBehaviour
    {
        [Tooltip("Seconds to fade in or out.")] public float fadeTime = 0.15f;
        [Tooltip("Height over the giver's feet, metres (just over the ground).")] public float height = 0.06f;

        static AuraRange instance;
        static Material material;
        static Mesh quad;
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int SeedId = Shader.PropertyToID("_Seed");

        class Circle
        {
            public Unit giver;
            public Transform transform;
            public MeshRenderer renderer;
        }

        readonly List<Circle> circles = new();
        readonly List<(Unit giver, float radius)> wanted = new();
        MaterialPropertyBlock block;
        float fade;
        bool shown;

        /// <summary>The circles drawn now (for tests).</summary>
        public static int Count => instance != null ? instance.circles.FindAll(c => c.renderer.enabled).Count : 0;

        static AuraRange Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("AuraRange");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<AuraRange>();
                }
                return instance;
            }
        }

        /// <summary>Shows the reach of every aura <paramref name="unit"/> stands in, around the allies giving them.</summary>
        public static void ShowFor(Unit unit)
        {
            var auras = new List<(Unit giver, ItemDef item)>();
            if (unit != null) unit.AurasFrom(auras);
            var self = Instance;
            self.wanted.Clear();
            // one circle per giver: the widest of its auras
            foreach (var (giver, item) in auras)
            {
                int i = self.wanted.FindIndex(w => w.giver == giver);
                if (i < 0) self.wanted.Add((giver, item.AuraRadius));
                else if (item.AuraRadius > self.wanted[i].radius) self.wanted[i] = (giver, item.AuraRadius);
            }
            self.shown = self.wanted.Count > 0;
            self.Apply();
        }

        public static void Hide()
        {
            if (instance != null) instance.shown = false;
        }

        void Apply()
        {
            if (!EnsureAssets()) return;
            block ??= new MaterialPropertyBlock();
            while (circles.Count < wanted.Count) circles.Add(Make());
            for (int i = 0; i < circles.Count; i++)
            {
                var c = circles[i];
                bool used = i < wanted.Count;
                c.giver = used ? wanted[i].giver : null;
                c.renderer.enabled = used;
                if (!used) continue;
                c.transform.localScale = Vector3.one * wanted[i].radius;
            }
            Place();
        }

        Circle Make()
        {
            var go = new GameObject("AuraCircle");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            block ??= new MaterialPropertyBlock();
            block.Clear();
            block.SetFloat(SeedId, Random.value * 10f);
            block.SetFloat(FadeId, 0f);
            renderer.SetPropertyBlock(block);
            return new Circle { transform = go.transform, renderer = renderer };
        }

        void Place()
        {
            foreach (var c in circles)
                if (c.giver != null) c.transform.position = c.giver.transform.position + Vector3.up * height;
        }

        void LateUpdate()
        {
            if (circles.Count == 0) return;
            fade = Mathf.MoveTowards(fade, shown ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime));
            Place();
            foreach (var c in circles)
            {
                if (c.giver == null || !c.giver.isActiveAndEnabled) c.renderer.enabled = false;
                if (!c.renderer.enabled) continue;
                if (fade <= 0f && !shown)
                {
                    c.renderer.enabled = false;
                    continue;
                }
                c.renderer.GetPropertyBlock(block);
                block.SetFloat(FadeId, fade);
                c.renderer.SetPropertyBlock(block);
            }
        }

        static bool EnsureAssets()
        {
            if (material == null)
            {
                var shader = Resources.Load<Shader>("Effects/AuraRange");
                if (shader == null) return false;
                material = new Material(shader) { name = "AuraRange" };
            }
            if (quad == null)
            {
                // a little past the radius, so the rim is not cut
                const float e = 1.03f;
                quad = new Mesh { name = "AuraRange" };
                quad.vertices = new[] { new Vector3(-e, 0f, -e), new Vector3(-e, 0f, e), new Vector3(e, 0f, e), new Vector3(e, 0f, -e) };
                quad.uv = new[] { new Vector2(-e, -e), new Vector2(-e, e), new Vector2(e, e), new Vector2(e, -e) };
                quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                quad.RecalculateBounds();
            }
            return true;
        }
    }
}
