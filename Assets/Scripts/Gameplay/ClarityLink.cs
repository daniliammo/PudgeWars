using UnityEngine;
using UnityEngine.Rendering;

namespace PudgeWars.Gameplay
{
    /// <summary>A short translucent mana ribbon from Clarity's giver to its recipient, including network item events.</summary>
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
        bool counted;

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
                item.Active?.action != ItemAction.Replenish || target.gameObject == owner.gameObject) return;
            if (material == null)
            {
                var shader = Resources.Load<Shader>("Effects/ClarityLink");
                if (shader == null) return;
                material = new Material(shader) { name = "ClarityLink", hideFlags = HideFlags.HideAndDontSave };
            }
            var go = new GameObject("ClarityLink");
            var link = go.AddComponent<ClarityLink>();
            link.giver = owner.transform;
            link.recipient = target.transform;
            link.started = Time.time;
            link.properties = new MaterialPropertyBlock();
            link.ribbon = go.AddComponent<LineRenderer>();
            link.ribbon.sharedMaterial = material;
            link.ribbon.useWorldSpace = true;
            link.ribbon.positionCount = Segments + 1;
            link.ribbon.widthMultiplier = 0.28f;
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
            if (giver == null || recipient == null || Time.time - started >= Duration)
            {
                Destroy(gameObject);
                return;
            }
            UpdateRibbon();
        }

        void UpdateRibbon()
        {
            float progress = Mathf.Clamp01((Time.time - started) / Duration);
            var from = giver.position + Vector3.up * 1.1f;
            var to = recipient.position + Vector3.up * 1.1f;
            var across = Vector3.Cross((to - from).normalized, Vector3.up);
            for (int i = 0; i <= Segments; i++)
            {
                float t = (float)i / Segments;
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
