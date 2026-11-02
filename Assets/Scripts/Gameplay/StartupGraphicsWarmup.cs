using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Android's loading-time preparation: preload effects, draw their real vertex layouts through URP, then
    /// trace actual gameplay PSOs for the next launch on this GPU/driver/build. Does not simulate item use.
    /// </summary>
    public sealed class StartupGraphicsWarmup : MonoBehaviour
    {
        const int WarmupLayer = 31;
        const string CacheName = "graphics-states-v1";
        static StartupGraphicsWarmup instance;
        static readonly string[] AudioFolders = { "Audio/Items", "Audio/Hook", "Audio/Trees", "Audio/Shop" };
        readonly List<UnityEngine.Object> temporary = new();
        readonly List<Renderer> samples = new();
        readonly List<AudioClip> sounds = new();
        Shader[] shaders; // Keep Resources assets resident until the application exits.
        GraphicsStateCollection states;
        JobHandle warmupJob;
        GameObject stage;
        LoadingProgress.Operation loading;
        Camera sampleCamera;
        RenderTexture target;
        bool finished, tracing;
        string cachePath, cacheKey;
        int savedCount = -1;

        public static bool IsPreparing => instance != null && !instance.finished;
        public bool Succeeded { get; private set; }
        public int DrawCount { get; private set; }
        public float Progress => loading?.Progress ?? 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoStart()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Begin();
#endif
        }

        /// <summary>Also callable explicitly in Play Mode; the editor never reads or writes the device cache.</summary>
        public static StartupGraphicsWarmup Begin()
        {
            if (instance != null) return instance;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            var go = new GameObject("StartupGraphicsWarmup");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<StartupGraphicsWarmup>();
            instance.ShowLoading();
            return instance;
        }

        IEnumerator Start()
        {
            // BeforeSceneLoad initializers have unspecified order. Wait for GraphicsQuality and the first scene.
            yield return null;
            var prepare = Prepare();
            try
            {
                while (true)
                {
                    bool more;
                    try { more = prepare.MoveNext(); }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[GraphicsWarmup] Preparation skipped: {e.Message}");
                        break;
                    }
                    if (!more) { Succeeded = true; break; }
                    yield return prepare.Current;
                }
            }
            finally
            {
                (prepare as IDisposable)?.Dispose();
                CleanStage();
                CloseLoading();
                finished = true;
            }
            Debug.Log($"[GraphicsWarmup] Ready: {DrawCount} offscreen draws, {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}");
        }

        IEnumerator Prepare()
        {
            var request = Resources.LoadAsync<Shader>("Effects/Blood");
            while (!request.isDone)
            {
                loading.Report(request.progress * 0.04f, "Загрузка эффектов");
                yield return null;
            }
            shaders = Resources.LoadAll<Shader>("Effects");
            loading.Report(0.06f, "Загрузка звуков эффектов");
            int audioFoldersLoaded = 0;
            foreach (var folder in AudioFolders)
            {
                var clips = Resources.LoadAll<AudioClip>(folder);
                sounds.AddRange(clips);
                foreach (var clip in clips)
                    if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                loading.Report(0.06f + 0.14f * ++audioFoldersLoaded / AudioFolders.Length, "Загрузка звуков эффектов");
                yield return null;
            }
            // These used to be generated on the first item use or body collision (the atlas is CPU-heavy).
            ManaBurst.EnsureAssets();
            AuraRange.EnsureAssets();
            ClarityLink.PrepareMaterial();
            loading.Report(0.22f, "Подготовка эффектов");
            yield return null;
            var blood = BloodSplatter.PrepareTextures();
            loading.Report(0.25f, "Подготовка текстур");
            yield return null;

            // Warm against the real scene's lighting/render state, after the tiny boot scene has left.
            while (StartupScene.IsActive || SceneLoading.IsLoading)
            {
                loading.Report(0.25f, "Ожидание загрузки сцены");
                yield return null;
            }

            OpenCache();
            loading.Report(0.28f, "Подготовка шейдеров");
            if (states != null && states.totalGraphicsStateCount > 0)
            {
                while (!states.isWarmedUp)
                {
                    int before = states.completedWarmupCount;
                    warmupJob = states.WarmUpProgressively(16);
                    while (!warmupJob.IsCompleted) yield return null;
                    warmupJob.Complete();
                    loading.Report(0.28f + 0.37f * states.completedWarmupCount / states.totalGraphicsStateCount,
                        $"Прогрев шейдеров: {states.completedWarmupCount} из {states.totalGraphicsStateCount}");
                    // An incompatible/unsupported collection must not trap the loading screen.
                    if (states.completedWarmupCount <= before && !states.isWarmedUp) break;
                    yield return null;
                }
                Debug.Log($"[GraphicsWarmup] Cached GPU states: {states.completedWarmupCount}/{states.totalGraphicsStateCount}");
            }
            StartTrace();
            loading.Report(0.65f, "Подготовка отрисовки эффектов");

            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null)
            {
                BuildStage(pipeline, blood.stain, blood.drop);
                var render = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (RenderPipeline.SupportsRenderRequest(sampleCamera, render))
                {
                    foreach (var sample in samples)
                    {
                        sample.enabled = true;
                        // Both fog states are used by scene/effect cameras; restore globals before yielding.
                        Draw(render, false);
                        ReportDrawProgress();
                        yield return null;
                        Draw(render, true);
                        ReportDrawProgress();
                        sample.enabled = false;
                        yield return null;
                    }
                    if (SystemInfo.supportsAsyncGPUReadback)
                    {
                        loading.Report(0.96f, "Завершение подготовки графики");
                        var fence = AsyncGPUReadback.Request(target);
                        while (!fence.done) yield return null;
                    }
                }
            }
            loading.Report(0.97f, "Подготовка звуков");
            foreach (var clip in sounds)
                while (clip != null && clip.loadState == AudioDataLoadState.Loading) yield return null;
            SaveCache();
            loading.Report(1f, "Готово");
            yield return null;
        }

        void ReportDrawProgress() => loading.Report(0.65f + 0.3f * DrawCount / (samples.Count * 2),
            $"Прогрев эффектов: {DrawCount} из {samples.Count * 2}");

        void Draw(UniversalRenderPipeline.SingleCameraRequest request, bool fog)
        {
            bool oldFog = RenderSettings.fog;
            var oldMode = RenderSettings.fogMode;
            try
            {
                RenderSettings.fog = fog;
                RenderSettings.fogMode = FogMode.Linear;
                RenderPipeline.SubmitRenderRequest(sampleCamera, request);
                DrawCount++;
            }
            finally { RenderSettings.fog = oldFog; RenderSettings.fogMode = oldMode; }
        }

        void BuildStage(UniversalRenderPipelineAsset pipeline, Texture2D stain, Texture2D drop)
        {
            stage = new GameObject("GraphicsWarmupStage") { hideFlags = HideFlags.HideAndDontSave };
            stage.transform.SetParent(transform, false);
            stage.transform.position = Vector3.one * 10000f;
            var cameraObject = Child("Camera");
            sampleCamera = cameraObject.AddComponent<Camera>();
            sampleCamera.enabled = false;
            sampleCamera.orthographic = true;
            sampleCamera.orthographicSize = 2f;
            sampleCamera.nearClipPlane = 0.1f;
            sampleCamera.farClipPlane = 20f;
            sampleCamera.cullingMask = 1 << WarmupLayer;
            sampleCamera.allowHDR = pipeline.supportsHDR;
            sampleCamera.allowMSAA = true;
            sampleCamera.useOcclusionCulling = false;
            cameraObject.transform.localPosition = new Vector3(0f, 2f, -4f);
            cameraObject.transform.LookAt(stage.transform.position);
            var data = sampleCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;

            // Follow URP 17.6's HDR format selection, including its 32-bit blend-support check.
            var color = SystemInfo.GetGraphicsFormat(pipeline.supportsHDR ? DefaultFormat.HDR : DefaultFormat.LDR);
            if (pipeline.supportsHDR && pipeline.hdrColorBufferPrecision == HDRColorBufferPrecision._32Bits &&
                SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormatUsage.Blend))
                color = GraphicsFormat.B10G11R11_UFloatPack32;
            var descriptor = new RenderTextureDescriptor(64, 64)
            {
                graphicsFormat = color,
                depthStencilFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                msaaSamples = pipeline.msaaSampleCount,
            };
            descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
            target = new RenderTexture(descriptor) { name = "GraphicsWarmupTarget" };
            target.Create();
            temporary.Add(target);

            var plain = Quad(false, false);
            var normal = Quad(true, false);
            var colored = Quad(true, true);
            AddMesh("ManaBurst", plain);
            AddMesh("AuraRange", plain);
            AddMesh("NightUnitGlow", plain);
            var splat = AddMesh("Blood", colored);
            if (splat != null) splat.mainTexture = stain;
            var drops = AddMesh("Blood", normal);
            if (drops != null) { drops.mainTexture = drop; drops.SetFloat("_DepthOffset", 0f); }
            AddLine("ClarityLink");
            AddLine("SelectionRing");
            AddLine("AimGlow");

            // Give URP a real shadow caster and an additional light, so blood's lighting variants are exercised.
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var material = new Material(lit) { hideFlags = HideFlags.HideAndDontSave };
                temporary.Add(material);
                var caster = Child("ShadowCaster");
                caster.AddComponent<MeshFilter>().sharedMesh = normal;
                var renderer = caster.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
            var lamp = Child("PointLight").AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.cullingMask = 1 << WarmupLayer;
            lamp.range = 10f;
            lamp.shadows = LightShadows.None;
            lamp.transform.localPosition = Vector3.up * 2f;

            var sky = MaterialFor("DayNightSky");
            if (sky != null) cameraObject.AddComponent<Skybox>().material = sky;
            sampleCamera.clearFlags = sky != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        }

        GameObject Child(string name)
        {
            var child = new GameObject(name) { layer = WarmupLayer, hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(stage.transform, false);
            return child;
        }

        Material MaterialFor(string name)
        {
            var shader = Array.Find(shaders, s => s.name == "PudgeWars/" + name);
            if (shader == null || !shader.isSupported) return null;
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            temporary.Add(material);
            if (material.HasProperty("_Progress")) material.SetFloat("_Progress", 0.25f);
            return material;
        }

        Material AddMesh(string name, Mesh mesh)
        {
            var material = MaterialFor(name);
            if (material == null) return null;
            var child = Child(name);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (name == "ManaBurst" || name == "AuraRange")
            {
                var properties = new MaterialPropertyBlock();
                properties.SetFloat(name == "ManaBurst" ? "_Progress" : "_Fade", 0.25f);
                renderer.SetPropertyBlock(properties);
            }
            renderer.enabled = false;
            samples.Add(renderer);
            return material;
        }

        void AddLine(string name)
        {
            var material = MaterialFor(name);
            if (material == null) return;
            var line = Child(name).AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.positionCount = 3;
            line.SetPositions(new[] { Vector3.left, Vector3.up, Vector3.right });
            line.widthMultiplier = 0.3f;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (name == "ClarityLink")
            {
                var properties = new MaterialPropertyBlock();
                properties.SetFloat("_Progress", 0.25f);
                line.SetPropertyBlock(properties);
            }
            line.enabled = false;
            samples.Add(line);
        }

        Mesh Quad(bool normals, bool colors)
        {
            var mesh = new Mesh { name = "GraphicsWarmupQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) };
            if (normals) mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            if (colors) mesh.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            temporary.Add(mesh);
            return mesh;
        }

        void OpenCache()
        {
            if (Application.isEditor || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan) return;
            cachePath = Path.Combine(Application.persistentDataPath, CacheName + ".graphicsstate");
            cacheKey = Hash128.Compute($"{Application.buildGUID}|{Application.unityVersion}|{SystemInfo.graphicsDeviceType}|" +
                $"{SystemInfo.graphicsDeviceName}|{SystemInfo.graphicsDeviceVersion}|{SystemInfo.operatingSystem}").ToString();
            states = new GraphicsStateCollection();
            try
            {
                if (File.Exists(cachePath) && File.Exists(cachePath + ".key") && File.ReadAllText(cachePath + ".key") == cacheKey)
                    if (!states.LoadFromFile(cachePath)) states.ClearVariants();
            }
            catch (Exception e) { Debug.LogWarning($"[GraphicsWarmup] Cache ignored: {e.Message}"); states.ClearVariants(); }
        }

        void StartTrace()
        {
            if (states != null) tracing = states.BeginTrace();
        }

        void SaveCache()
        {
            if (states == null || states.totalGraphicsStateCount == 0 || states.totalGraphicsStateCount == savedCount) return;
            bool resume = tracing;
            try
            {
                if (tracing) { states.EndTrace(); tracing = false; }
                string staging = cachePath + ".tmp.graphicsstate";
                if (states.SaveToFile(staging))
                {
                    File.Copy(staging, cachePath, true);
                    File.WriteAllText(cachePath + ".key", cacheKey);
                    File.Delete(staging);
                    savedCount = states.totalGraphicsStateCount;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[GraphicsWarmup] Cache not saved: {e.Message}"); }
            finally { if (resume) StartTrace(); }
        }

        void OnApplicationPause(bool paused) { if (paused && finished) SaveCache(); }
        void OnApplicationQuit() { if (finished) SaveCache(); }

        void ShowLoading() => loading = LoadingProgress.Begin("Прогрев шейдеров");

        void CloseLoading() => loading?.Dispose();

        void CleanStage()
        {
            warmupJob.Complete();
            if (stage != null) { stage.SetActive(false); Destroy(stage); }
            if (target != null) target.Release();
            foreach (var asset in temporary) if (asset != null) Destroy(asset);
            temporary.Clear(); samples.Clear();
        }

        void OnDestroy()
        {
            CleanStage();
            CloseLoading();
            if (tracing) states.EndTrace();
            if (states != null) Destroy(states);
            if (instance == this) instance = null;
        }
    }
}
