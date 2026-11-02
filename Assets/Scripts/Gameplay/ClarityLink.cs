using UnityEngine;
using UnityEngine.Rendering;

namespace PudgeWars.Gameplay
{
    /// <summary>Clarity's translucent mana ribbon: a link to an ally or a rising spiral on self use, including network events.</summary>
    public class ClarityLink : MonoBehaviour
    {
        const float Duration = 0.85f;
        const int Segments = 32;
        static ClarityLink listener;
        static Material material;
        LineRenderer ribbon;
        Transform giver, recipient;
        MaterialPropertyBlock properties;
        float started;
        bool counted, selfUse;
        float Lifetime => selfUse ? 1.2f : Duration;

        public static int ActiveCount { get; private set; }
        public static Transform LastGiver { get; private set; }
        public static Transform LastRecipient { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            if (listener != null) return;
            var go = new GameObject("ClarityLinks");
            DontDestroyOnLoad(go);
            listener = go.AddComponent<ClarityLink>();
        }

        void OnEnable()
        {
            if (listener == null || this == listener) HeroInventory.AnyUsed += OnUsed;
        }
        void OnDisable() => HeroInventory.AnyUsed -= OnUsed;

        void OnUsed(HeroInventory owner, ItemDef item, Unit target)
        {
            // Only the listener subscribes in practice; individual ribbons ignore all use events.
            if (this != listener || owner == null || target == null || item == null ||
                item.Active?.action != ItemAction.Replenish) return;
            if (material == null)
            {
                var shader = Resources.Load<Shader>("Effects/ClarityLink");
                if (shader == null) return;
                material = new Material(shader) { name = "ClarityLink", hideFlags = HideFlags.HideAndDontSave };
            }
            bool selfUse = target.gameObject == owner.gameObject;
            var go = new GameObject(selfUse ? "ClaritySelf" : "ClarityLink");
            var link = go.AddComponent<ClarityLink>();
            link.selfUse = selfUse;
            link.giver = owner.transform;
            link.recipient = target.transform;
            link.started = Time.time;
            link.properties = new MaterialPropertyBlock();
            link.ribbon = go.AddComponent<LineRenderer>();
            link.ribbon.sharedMaterial = material;
            link.ribbon.useWorldSpace = true;
            link.ribbon.positionCount = Segments + 1;
            link.ribbon.widthMultiplier = selfUse ? 0.38f : 0.28f;
            link.ribbon.alignment = LineAlignment.View;
            link.ribbon.textureMode = LineTextureMode.Stretch;
            link.ribbon.shadowCastingMode = ShadowCastingMode.Off;
            link.ribbon.receiveShadows = false;
            link.ribbon.lightProbeUsage = LightProbeUsage.Off;
            link.ribbon.reflectionProbeUsage = ReflectionProbeUsage.Off;
            LastGiver = link.giver;
            LastRecipient = link.recipient;
            link.counted = true;
            ActiveCount++;
            link.UpdateRibbon();
        }

        void Update()
        {
            if (this == listener) return;
            if (giver == null || recipient == null || Time.time - started >= Lifetime)
            {
                Destroy(gameObject);
                return;
            }
            UpdateRibbon();
        }

        void UpdateRibbon()
        {
            float progress = Mathf.Clamp01((Time.time - started) / Lifetime);
            var from = giver.position + Vector3.up * 1.1f;
            var to = recipient.position + Vector3.up * 1.1f;
            var across = Vector3.Cross((to - from).normalized, Vector3.up);
            for (int i = 0; i <= Segments; i++)
            {
                float t = (float)i / Segments;
                if (selfUse)
                {
                    // Two turns rise from the feet, closing gently towards the body as the magic fades.
                    float angle = t * Mathf.PI * 4f - progress * Mathf.PI * 2f;
                    float radius = Mathf.Lerp(0.85f, 0.55f, t) * Mathf.Lerp(1f, 0.8f, progress);
                    var spiral = new Vector3(Mathf.Cos(angle) * radius, 0.12f + t * 2.1f + progress * 0.3f,
                        Mathf.Sin(angle) * radius);
                    ribbon.SetPosition(i, recipient.position + spiral);
                    continue;
                }
                float envelope = Mathf.Sin(t * Mathf.PI);
                var at = Vector3.Lerp(from, to, t) + Vector3.up * (envelope * 0.35f);
                at += across * (Mathf.Sin(t * 15f - progress * 9f) * envelope * 0.045f);
                ribbon.SetPosition(i, at);
            }
            properties.SetFloat("_Progress", progress);
            ribbon.SetPropertyBlock(properties);
        }

        void OnDestroy()
        {
            if (counted) ActiveCount--;
        }
    }
}
