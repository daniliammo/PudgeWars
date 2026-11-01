using UnityEngine;
using UnityEngine.Rendering;

namespace PudgeWars.Gameplay
{
    /// <summary>Five minutes of day, five of night, driven by the replicated match clock.</summary>
    public sealed class DayNightCycle : MonoBehaviour
    {
        public const float HalfCycleSeconds = 300f;
        public const float SecondsUntilFirstDawn = HalfCycleSeconds * 0.5f;
        Light sun;
        Arena arena;
        Camera view;
        CameraClearFlags previousClearFlags;
        bool initialized;
        Material sky, previousSky;
        Light previousSun;
        AmbientMode previousAmbientMode;
        Color previousSkyColor, previousEquator, previousGround, previousFog;
        bool previousFogEnabled;
        FogMode previousFogMode;
        float previousFogStart, previousFogEnd;
        Color lightColor;
        float lightIntensity;
        Quaternion lightRotation;

        /// <summary>Starts at midnight; dawn is at 2:30. Each full day and night lasts five minutes.</summary>
        public static float SolarHeight(float seconds) => Mathf.Sin((seconds - SecondsUntilFirstDawn) * Mathf.PI / HalfCycleSeconds);

        void Start()
        {
            arena = GetComponent<Arena>();
            sun = RenderSettings.sun;
            if (sun == null)
                foreach (var light in FindObjectsByType<Light>())
                    if (light.type == LightType.Directional) { sun = light; break; }
            if (sun == null) { enabled = false; return; }
            initialized = true;
            view = Camera.main;
            if (view != null)
            {
                previousClearFlags = view.clearFlags;
                view.clearFlags = CameraClearFlags.Skybox;
            }
            previousSky = RenderSettings.skybox;
            previousSun = RenderSettings.sun;
            previousAmbientMode = RenderSettings.ambientMode;
            previousSkyColor = RenderSettings.ambientSkyColor;
            previousEquator = RenderSettings.ambientEquatorColor;
            previousGround = RenderSettings.ambientGroundColor;
            previousFog = RenderSettings.fogColor;
            previousFogEnabled = RenderSettings.fog;
            previousFogMode = RenderSettings.fogMode;
            previousFogStart = RenderSettings.fogStartDistance;
            previousFogEnd = RenderSettings.fogEndDistance;
            lightColor = sun.color;
            lightIntensity = sun.intensity;
            lightRotation = sun.transform.rotation;
            var shader = Resources.Load<Shader>("Effects/DayNightSky");
            if (shader != null)
            {
                sky = new Material(shader);
                RenderSettings.skybox = sky;
            }
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 150f;
            ApplyTime(arena.MatchTime);
        }

        void LateUpdate()
        {
            if (sun != null && arena != null) ApplyTime(arena.MatchTime);
        }

        /// <summary>Also allows tests to inspect dawn, noon and midnight without waiting ten minutes.</summary>
        public void ApplyTime(float seconds)
        {
            if (sun == null) return;
            float height = SolarHeight(seconds);
            float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.14f, 0.24f, height));
            float twilight = 1f - Mathf.SmoothStep(0f, 0.32f, Mathf.Abs(height));
            float angle = Mathf.Repeat((seconds - SecondsUntilFirstDawn) / HalfCycleSeconds * 180f, 360f);
            // One shadow-casting light serves as the sun by day and the moon by night.
            sun.transform.rotation = Quaternion.Euler(height >= 0f ? angle : angle - 180f, -35f, 0f);
            sun.color = Color.Lerp(new Color(0.48f, 0.64f, 1f),
                Color.Lerp(new Color(1f, 0.94f, 0.80f), new Color(1f, 0.48f, 0.24f), twilight), day);
            sun.intensity = Mathf.Lerp(0.32f, 1.3f, day) * Mathf.Lerp(1f, 0.65f, twilight);
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.14f, 0.20f, 0.34f), new Color(0.55f, 0.63f, 0.76f), day);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.10f, 0.14f, 0.23f), new Color(0.42f, 0.43f, 0.40f), day);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.055f, 0.07f, 0.11f), new Color(0.20f, 0.18f, 0.16f), day);
            RenderSettings.fogColor = Color.Lerp(new Color(0.045f, 0.075f, 0.14f),
                Color.Lerp(new Color(0.53f, 0.65f, 0.77f), new Color(0.65f, 0.36f, 0.27f), twilight * 0.65f), day);
            if (sky == null) return;
            sky.SetFloat("_Day", day);
            sky.SetFloat("_Twilight", twilight);
            Vector3 direction = Quaternion.Euler(angle, -35f, 0f) * Vector3.back;
            sky.SetVector("_SunDirection", direction.normalized);
        }

        void OnDestroy()
        {
            if (!initialized) return;
            if (view != null) view.clearFlags = previousClearFlags;
            if (sun != null)
            {
                sun.color = lightColor;
                sun.intensity = lightIntensity;
                sun.transform.rotation = lightRotation;
            }
            RenderSettings.sun = previousSun;
            RenderSettings.skybox = previousSky;
            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientSkyColor = previousSkyColor;
            RenderSettings.ambientEquatorColor = previousEquator;
            RenderSettings.ambientGroundColor = previousGround;
            RenderSettings.fog = previousFogEnabled;
            RenderSettings.fogColor = previousFog;
            RenderSettings.fogMode = previousFogMode;
            RenderSettings.fogStartDistance = previousFogStart;
            RenderSettings.fogEndDistance = previousFogEnd;
            if (sky != null) Destroy(sky);
        }
    }
}
