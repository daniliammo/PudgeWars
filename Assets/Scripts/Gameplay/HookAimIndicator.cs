using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Two lines marking the edges of the strip the hook will sweep, drawn at the head's flight height (on the
    /// ground they would look shorter than the flight under the tilted camera): from the hero's centre to the
    /// tip of the hook at full range, as wide as the hook head.
    /// </summary>
    public class HookAimIndicator : MonoBehaviour
    {
        public LineRenderer left;
        public LineRenderer right;

        const int LightsPerEdge = 3;
        readonly Light[] lights = new Light[LightsPerEdge * 2];
        readonly LineRenderer[] halos = new LineRenderer[2];
        Material glowMaterial;
        bool built;

        void Awake() => Hide();

        void BuildGlow()
        {
            built = true;
            var shader = Resources.Load<Shader>("Effects/AimGlow");
            if (shader != null) glowMaterial = new Material(shader);
            for (int edge = 0; edge < 2; edge++)
            {
                if (glowMaterial != null)
                {
                    var go = new GameObject("AimHalo");
                    go.transform.SetParent(transform, false);
                    var halo = go.AddComponent<LineRenderer>();
                    halo.sharedMaterial = glowMaterial;
                    halo.useWorldSpace = true;
                    halo.alignment = LineAlignment.View;
                    halo.positionCount = 2;
                    halo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    halo.receiveShadows = false;
                    halo.enabled = false;
                    halos[edge] = halo;
                }
                for (int i = 0; i < LightsPerEdge; i++)
                {
                    var lamp = new GameObject("AimLight");
                    lamp.transform.SetParent(transform, false);
                    var light = lamp.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = new Color(0.2f, 1f, 0.3f);
                    light.shadows = LightShadows.None;
                    light.enabled = false;
                    lights[edge * LightsPerEdge + i] = light;
                }
            }
        }

        void LateUpdate() => UpdateGlow();

        void UpdateGlow()
        {
            var arena = Arena.Instance;
            float night = arena != null ? Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0f, -0.24f, DayNightCycle.SolarHeight(arena.MatchTime))) : 0f;
            bool visible = (left != null && left.enabled) || (right != null && right.enabled);
            if (!built && visible && night > 0f) BuildGlow();
            if (!built) return;
            for (int edge = 0; edge < 2; edge++)
            {
                var source = edge == 0 ? left : right;
                bool active = source != null && source.enabled && source.positionCount >= 2 && night > 0f;
                var halo = halos[edge];
                if (halo != null) halo.enabled = active;
                for (int i = 0; i < LightsPerEdge; i++) lights[edge * LightsPerEdge + i].enabled = active;
                if (!active) continue;
                Vector3 from = source.GetPosition(0), to = source.GetPosition(1);
                if (!source.useWorldSpace)
                {
                    from = source.transform.TransformPoint(from);
                    to = source.transform.TransformPoint(to);
                }
                if (halo != null)
                {
                    halo.SetPosition(0, from);
                    halo.SetPosition(1, to);
                    halo.startWidth = source.startWidth * 1.8f;
                    halo.endWidth = source.endWidth * 1.8f;
                    halo.startColor = halo.endColor = new Color(0.2f, 1f, 0.3f, night * 0.35f);
                }
                for (int i = 0; i < LightsPerEdge; i++)
                {
                    var light = lights[edge * LightsPerEdge + i];
                    light.transform.position = Vector3.Lerp(from, to, (i + 0.5f) / LightsPerEdge);
                    light.range = Mathf.Clamp(Vector3.Distance(from, to) / LightsPerEdge * 0.9f, 1.5f, 5f);
                    light.intensity = night * 0.45f;
                }
            }
        }

        void OnDisable() => Hide();

        void OnDestroy()
        {
            foreach (var halo in halos) if (halo != null) Destroy(halo.gameObject);
            foreach (var light in lights) if (light != null) Destroy(light.gameObject);
            if (glowMaterial != null) Destroy(glowMaterial);
        }

        public void Show(Vector3 start, Vector3 direction, float length, float width)
        {
            var side = Vector3.Cross(Vector3.up, direction).normalized * (width / 2f);
            Set(left, start - side, direction, length);
            Set(right, start + side, direction, length);
            UpdateGlow();
        }

        public void Hide()
        {
            if (left != null) left.enabled = false;
            if (right != null) right.enabled = false;
            foreach (var halo in halos) if (halo != null) halo.enabled = false;
            foreach (var light in lights) if (light != null) light.enabled = false;
        }

        static void Set(LineRenderer line, Vector3 from, Vector3 direction, float length)
        {
            if (line == null) return;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, from + direction * length);
            line.enabled = true;
        }
    }
}
