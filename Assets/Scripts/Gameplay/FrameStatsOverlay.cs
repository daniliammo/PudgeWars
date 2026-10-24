using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// FPS counter in the top-left corner plus an extended debug panel toggled by F11 (graphics API, GPU,
    /// draw calls, vertices, triangles, memory, frame timings). Spawns itself on startup in the editor and in
    /// builds, and caps the frame rate at the display's refresh rate.
    /// </summary>
    public class FrameStatsOverlay : MonoBehaviour
    {
        [Tooltip("How often the text is rebuilt, seconds.")] public float refreshInterval = 0.5f;
        public bool showDetails;

        const int HistorySize = 240;
        readonly float[] frameTimes = new float[HistorySize];
        int frameIndex, frameCount;
        float accumulated, sinceRefresh;
        int framesSinceRefresh;

        ProfilerRecorder drawCalls, batches, setPass, triangles, vertices, shadowCasters;
        ProfilerRecorder gcAlloc, gcUsed, systemUsed, totalUsed;
        readonly FrameTiming[] timings = new FrameTiming[1];

        readonly StringBuilder sb = new(1024);
        string fpsText = "", detailsText = "";
        GUIStyle fpsStyle, detailsStyle;
        Texture2D background;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            if (FindAnyObjectByType<FrameStatsOverlay>() != null) return;
            var go = new GameObject("FrameStatsOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<FrameStatsOverlay>();
        }

        /// <summary>
        /// Display refresh rate: the compositor's value when known (the editor always reports 60 Hz). On mobile the
        /// highest supported mode — phones keep the display at 60 Hz until the app asks for more via targetFrameRate.
        /// </summary>
        static float RefreshRate
        {
            get
            {
                if (DisplayScaling.NativeRefreshRate > 0f) return DisplayScaling.NativeRefreshRate;
                float hz = (float)Screen.currentResolution.refreshRateRatio.value;
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
                foreach (var mode in Screen.resolutions) hz = Mathf.Max(hz, (float)mode.refreshRateRatio.value);
#endif
                return hz;
            }
        }

        /// <summary>Frame cap = display refresh rate; VSync off so the cap is what limits the frame rate.</summary>
        public static int ApplyFrameCap()
        {
            int hz = Mathf.RoundToInt(RefreshRate);
            if (hz <= 0) hz = 60;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = hz;
            return hz;
        }

        void OnEnable()
        {
            ApplyFrameCap();
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            vertices = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            gcUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
            systemUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            totalUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
        }

        void OnDisable()
        {
            drawCalls.Dispose(); batches.Dispose(); setPass.Dispose(); triangles.Dispose(); vertices.Dispose();
            shadowCasters.Dispose(); gcAlloc.Dispose(); gcUsed.Dispose(); systemUsed.Dispose(); totalUsed.Dispose();
        }

        void OnDestroy()
        {
            if (background != null) Destroy(background);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f11Key.wasPressedThisFrame)
            {
                showDetails = !showDetails;
                sinceRefresh = refreshInterval;
            }

            float dt = Time.unscaledDeltaTime;
            frameTimes[frameIndex] = dt;
            frameIndex = (frameIndex + 1) % HistorySize;
            if (frameCount < HistorySize) frameCount++;
            FrameTimingManager.CaptureFrameTimings();

            accumulated += dt;
            framesSinceRefresh++;
            sinceRefresh += dt;
            if (sinceRefresh < refreshInterval) return;

            float fps = framesSinceRefresh / Mathf.Max(accumulated, 1e-5f);
            fpsText = $"{fps:0} FPS  ({1000f / Mathf.Max(fps, 1e-3f):0.0} мс)";
            accumulated = 0f;
            framesSinceRefresh = 0;
            sinceRefresh = 0f;
            if (showDetails) BuildDetails();
        }

        void BuildDetails()
        {
            float min = float.MaxValue, max = 0f, sum = 0f;
            for (int i = 0; i < frameCount; i++)
            {
                float t = frameTimes[i];
                sum += t;
                if (t < min) min = t;
                if (t > max) max = t;
            }
            float avg = frameCount > 0 ? sum / frameCount : 0f;

            sb.Clear();
            sb.Append("F11 — скрыть\n\n");
            sb.Append("Графический API: ").Append(SystemInfo.graphicsDeviceType).Append('\n');
            sb.Append("  ").Append(SystemInfo.graphicsDeviceVersion).Append('\n');
            sb.Append("GPU: ").Append(SystemInfo.graphicsDeviceName).Append(" (").Append(SystemInfo.graphicsMemorySize).Append(" МБ)\n");
            sb.Append("CPU: ").Append(SystemInfo.processorType).Append(", ").Append(SystemInfo.processorCount).Append(" потоков\n");
            sb.Append("ОЗУ: ").Append(SystemInfo.systemMemorySize).Append(" МБ, ОС: ").Append(SystemInfo.operatingSystem).Append('\n');
            sb.Append("Рендер: ").Append(GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "Built-in")
              .Append(", качество: ").Append(QualitySettings.names[QualitySettings.GetQualityLevel()]).Append('\n');
            sb.Append("Многопоточный рендер: ").Append(SystemInfo.renderingThreadingMode).Append('\n');
            sb.Append("Сборка: ").Append(Application.isEditor ? "редактор" : Debug.isDebugBuild ? "development" : "release")
              .Append(", Unity ").Append(Application.unityVersion).Append("\n\n");

            var res = Screen.currentResolution;
            sb.Append("Экран: ").Append(Screen.width).Append('x').Append(Screen.height)
              .Append(" @ ").Append(RefreshRate.ToString("0.##")).Append(" Гц, ").Append(Screen.fullScreenMode).Append('\n');
            sb.Append("Рабочий стол: ").Append(res.width).Append('x').Append(res.height).Append(", монитор: ");
            if (DisplayScaling.NativeSize != Vector2Int.zero) sb.Append(DisplayScaling.NativeSize.x).Append('x').Append(DisplayScaling.NativeSize.y).Append('\n');
            else sb.Append("неизвестно\n");
            sb.Append("Лимит кадров: ").Append(Application.targetFrameRate).Append(", VSync: ").Append(QualitySettings.vSyncCount).Append('\n');
            sb.Append("Кадр (").Append(frameCount).Append("): ср ").Append((avg * 1000f).ToString("0.0"))
              .Append(" / мин ").Append((min * 1000f).ToString("0.0"))
              .Append(" / макс ").Append((max * 1000f).ToString("0.0")).Append(" мс\n");
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                sb.Append("CPU ").Append(timings[0].cpuFrameTime.ToString("0.0"))
                  .Append(" мс (main ").Append(timings[0].cpuMainThreadFrameTime.ToString("0.0"))
                  .Append(", render ").Append(timings[0].cpuRenderThreadFrameTime.ToString("0.0"))
                  .Append("), GPU ").Append(timings[0].gpuFrameTime.ToString("0.0")).Append(" мс\n");
            else
                sb.Append("CPU/GPU время: нет (Player Settings → Frame Timing Stats)\n");
            sb.Append('\n');

            AppendCounter("Draw calls", drawCalls);
            AppendCounter("Batches", batches);
            AppendCounter("SetPass calls", setPass);
            AppendCounter("Треугольники", triangles);
            AppendCounter("Вершины", vertices);
            AppendCounter("Shadow casters", shadowCasters);
            sb.Append('\n');
            AppendMemory("GC за кадр", gcAlloc, 1024f, "КБ");
            AppendMemory("GC heap", gcUsed, 1024f * 1024f, "МБ");
            AppendMemory("Память Unity", totalUsed, 1024f * 1024f, "МБ");
            AppendMemory("Память процесса", systemUsed, 1024f * 1024f, "МБ");
            detailsText = sb.ToString();
        }

        void AppendCounter(string label, ProfilerRecorder recorder)
        {
            sb.Append(label).Append(": ");
            if (recorder.Valid) sb.Append(recorder.LastValue.ToString("N0"));
            else sb.Append("нет (только Development Build)");
            sb.Append('\n');
        }

        void AppendMemory(string label, ProfilerRecorder recorder, float unit, string unitName)
        {
            sb.Append(label).Append(": ");
            if (recorder.Valid) sb.Append((recorder.LastValue / unit).ToString("0.0")).Append(' ').Append(unitName);
            else sb.Append("нет");
            sb.Append('\n');
        }

        void OnGUI()
        {
            if (fpsStyle == null)
            {
                background = new Texture2D(1, 1);
                background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
                background.Apply();
                fpsStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, padding = new RectOffset(8, 8, 4, 4) };
                fpsStyle.normal.background = background;
                detailsStyle = new GUIStyle(fpsStyle) { fontSize = 14, fontStyle = FontStyle.Normal, wordWrap = false };
            }

            float scale = Mathf.Max(1f, Screen.height / 1080f);
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            fpsStyle.normal.textColor = FpsColor();
            var fpsContent = new GUIContent(fpsText);
            var fpsSize = fpsStyle.CalcSize(fpsContent);
            GUI.Label(new Rect(8f, 8f, fpsSize.x, fpsSize.y), fpsContent, fpsStyle);

            if (showDetails && detailsText.Length > 0)
            {
                var content = new GUIContent(detailsText);
                var size = detailsStyle.CalcSize(content);
                GUI.Label(new Rect(8f, 12f + fpsSize.y, size.x, size.y), content, detailsStyle);
            }
            GUI.matrix = matrix;
        }

        Color FpsColor()
        {
            float target = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60f;
            float fps = 1f / Mathf.Max(Time.smoothDeltaTime, 1e-5f);
            if (fps >= target * 0.9f) return new Color(0.4f, 1f, 0.4f);
            if (fps >= target * 0.5f) return new Color(1f, 0.85f, 0.3f);
            return new Color(1f, 0.4f, 0.4f);
        }
    }
}
