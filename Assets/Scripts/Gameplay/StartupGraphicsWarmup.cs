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
    /// Prepare Menu and Arena rendering before input is released; retain resources and trace real gameplay PSOs.
    /// </summary>
    public sealed partial class StartupGraphicsWarmup : MonoBehaviour
    {
        const int WarmupLayer = 31;
        const string CacheName = "graphics-states-v2";
        static StartupGraphicsWarmup instance;
        static readonly string[] AudioFolders = { "Audio/Items", "Audio/Hook", "Audio/Trees", "Audio/Shop" };
        readonly List<UnityEngine.Object> temporary = new();
        readonly List<Renderer> samples = new();
        readonly List<AudioClip> sounds = new();
        readonly Stack<IEnumerator> work = new();
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
        int configuration, expectedDrawCount;
        bool cacheComplete;
        Light[] sceneLights;

        public static bool IsPreparing => instance != null && !instance.finished;
        public bool Succeeded { get; private set; }
        public int DrawCount { get; private set; }
        public float Progress => loading?.Progress ?? 0f;
        public string Failure { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (!StartupScene.IsActive) BeginAutomatically();
        }

        public static void BeginAutomatically()
        {
#if !UNITY_EDITOR
            Begin();
#endif
        }

        /// <summary>Also callable explicitly in Play Mode; the editor never reads or writes the device cache.</summary>
        public static StartupGraphicsWarmup Begin(GraphicsWarmupLibrary resources = null)
        {
            if (ServerRuntime.IsDedicated) return null;
            if (instance != null)
            {
                if (instance.finished && (!instance.Succeeded || instance.configuration != Configuration())) instance.Restart();
                return instance;
            }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            Debug.Log($"[GraphicsWarmup] Begin at {Time.realtimeSinceStartup:F2}s: " +
                $"{SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}, " +
                $"screen={Screen.width}x{Screen.height}, quality={QualitySettings.names[QualitySettings.GetQualityLevel()]}, " +
                $"preset={GraphicsQuality.Current}, renderScale={GraphicsQuality.RenderScale:F2}");
            var go = new GameObject("StartupGraphicsWarmup");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<StartupGraphicsWarmup>();
            instance.library = resources;
            instance.Restart();
            return instance;
        }

        static int Configuration()
        {
            var pipeline = (QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline)
                as UniversalRenderPipelineAsset;
            return HashCode.Combine(pipeline != null ? pipeline.GetEntityId().GetHashCode() : 0,
                pipeline != null && pipeline.supportsHDR, pipeline != null ? pipeline.msaaSampleCount : 1,
                pipeline != null ? pipeline.renderScale : 1f, GraphicsQuality.Current, QualitySettings.GetQualityLevel());
        }

        void Update()
        {
#if !UNITY_EDITOR
            if (finished && configuration != Configuration()) Restart();
#endif
        }

        void Restart()
        {
            finished = Succeeded = false;
            Failure = null;
            DrawCount = SceneSampleCount = TerrainDrawCount = UiDrawCount = 0;
            cacheComplete = true;
            configuration = Configuration();
            ShowLoading();
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            // Let the loading UI render before preparing any effects.
            yield return null;
            work.Push(Prepare());
            try
            {
                while (work.Count > 0)
                {
                    var step = work.Peek();
                    bool more;
                    try { more = step.MoveNext(); }
                    catch (Exception e)
                    {
                        Failure = e.ToString();
                        Debug.LogWarning($"[GraphicsWarmup] Incomplete: {Failure}");
                        break;
                    }
                    if (!more) { work.Pop(); (step as IDisposable)?.Dispose(); }
                    else if (step.Current is IEnumerator nested) work.Push(nested);
                    else yield return step.Current;
                }
                Succeeded = Failure == null && DrawCount == expectedDrawCount && DrawCount > 0;
            }
            finally
            {
                DisposeWork();
                CleanStage();
                CloseLoading();
                finished = true;
            }
            if (Succeeded) Debug.Log($"[GraphicsWarmup] Ready: Menu + Arena, {DrawCount}/{expectedDrawCount} draws, " +
                $"{SceneSampleCount} model/material samples, {TerrainDrawCount} terrain draws, {UiDrawCount} UI draws, " +
                $"{SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}");
        }

        IEnumerator Prepare()
        {
            yield return loading.Present(0f, "Загрузка графики меню и арены");
            if (library == null)
            {
                var assets = Resources.LoadAsync<GraphicsWarmupLibrary>(GraphicsWarmupLibrary.ResourcePath);
                while (!assets.isDone) yield return null;
                library = assets.asset as GraphicsWarmupLibrary;
            }
            if (library == null) throw new InvalidOperationException("Graphics warmup library is missing. Rebuild the Player.");
            library.Validate();
            yield return loading.Present(0f, "Загрузка шейдеров эффектов");
            var request = Resources.LoadAsync<Shader>("Effects/Blood");
            while (!request.isDone)
            {
                loading.Report(request.progress * 0.04f, "Загрузка эффектов");
                yield return null;
            }
            yield return loading.Present(0.04f, "Загрузка шейдеров эффектов");
            shaders = Resources.LoadAll<Shader>("Effects");
            sounds.Clear();
            int audioFoldersLoaded = 0;
            foreach (var folder in AudioFolders)
            {
                string description = folder switch
                {
                    "Audio/Items" => "Загрузка звуков предметов",
                    "Audio/Hook" => "Загрузка звуков хука",
                    "Audio/Trees" => "Загрузка звуков деревьев",
                    _ => "Загрузка звуков магазина",
                };
                yield return loading.Present(0.06f + 0.14f * audioFoldersLoaded / AudioFolders.Length, description);
                var clips = Resources.LoadAll<AudioClip>(folder);
                sounds.AddRange(clips);
                foreach (var clip in clips)
                    if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                loading.Report(0.06f + 0.14f * ++audioFoldersLoaded / AudioFolders.Length, description);
                yield return null;
            }
            // These used to be generated on the first item use or body collision (the atlas is CPU-heavy).
            yield return loading.Present(0.20f, "Подготовка эффектов маны");
            ManaBurst.EnsureAssets();
            yield return loading.Present(0.21f, "Подготовка аур");
            AuraRange.EnsureAssets();
            yield return loading.Present(0.22f, "Подготовка эффекта Clarity");
            ClarityLink.PrepareMaterial();
            yield return loading.Present(0.23f, "Подготовка шрифтов");
            UiFonts.PrepareCommonGlyphs();
            yield return loading.Present(0.24f, "Создание текстур крови");
            foreach (var step in BloodSplatter.PrepareTexturesProgressively(
                         value => loading.Report(0.24f + 0.01f * value, "Создание текстур крови")))
                yield return step;
            var blood = BloodSplatter.PrepareTextures();
            loading.Report(0.25f, "Подготовка текстур");
            yield return null;

            // Warm against the real scene's lighting/render state, after the tiny boot scene has left.
            while (StartupScene.IsActive || SceneLoading.IsLoading)
            {
                loading.Report(0.25f, "Ожидание загрузки сцены");
                yield return null;
            }

            yield return loading.Present(0.25f, "Чтение кэша графики");
            configuration = Configuration();
            if (tracing) { states.EndTrace(); tracing = false; }
            OpenCache();
            yield return loading.Present(0.28f, "Подготовка шейдеров");
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
                    if (states.completedWarmupCount <= before && !states.isWarmedUp)
                    {
                        cacheComplete = false;
                        Debug.LogWarning("[GraphicsWarmup] Cached GPU warmup made no progress; continuing scene samples.");
                        break;
                    }
                    yield return null;
                }
                Debug.Log($"[GraphicsWarmup] Cached GPU states: {states.completedWarmupCount}/{states.totalGraphicsStateCount}");
            }
            StartTrace();
            yield return loading.Present(0.65f, "Подготовка отрисовки эффектов");

            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) throw new InvalidOperationException("Graphics warmup requires URP.");
            BuildStage(pipeline, blood.stain, blood.drop);
            BuildSceneSamples();
            expectedDrawCount = samples.Count * 4 + terrainSamples.Count * 16 + 8;
            var render = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(sampleCamera, render))
                throw new InvalidOperationException("URP does not support the graphics warmup render request.");
            foreach (var sample in samples)
            {
                sample.enabled = true;
                yield return null; // Let Unity update skinned meshes before submitting their draw.
                DrawPair(render, true);
                yield return null;
                DrawPair(render, false);
                sample.enabled = false;
                yield return null;
            }
            foreach (var terrain in terrainSamples)
            {
                terrain.gameObject.SetActive(true);
                foreach (bool instanced in new[] { true, false })
                foreach (float distance in new[] { 1000f, 0f })
                {
                    terrain.drawInstanced = instanced;
                    terrain.basemapDistance = distance;
                    terrain.Flush();
                    yield return null;
                    DrawPair(render, true); TerrainDrawCount += 2;
                    yield return null;
                    DrawPair(render, false); TerrainDrawCount += 2;
                }
                terrain.gameObject.SetActive(false);
            }
            var skybox = sampleCamera.GetComponent<Skybox>();
            var effectSky = skybox.material;
            foreach (var sky in new[] { library.radiant.sky, library.dire.sky, effectSky })
            {
                skybox.material = sky;
                DrawPair(render, true);
                yield return null;
            }
            skybox.material = effectSky;
            uiSample.SetActive(true);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Draw(render, false, true); UiDrawCount++;
            RetainUiVariants();
            // Screen-space overlay UI uses the final LDR attachment, independently of the scene's HDR/MSAA.
            var screenDescriptor = target.descriptor;
            screenDescriptor.graphicsFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);
            screenDescriptor.msaaSamples = 1;
            var screenTarget = new RenderTexture(screenDescriptor) { name = "GraphicsWarmupScreenTarget" };
            temporary.Add(screenTarget);
            if (!screenTarget.Create()) throw new InvalidOperationException("Could not create the UI warmup target.");
            sampleCamera.allowHDR = sampleCamera.allowMSAA = false;
            render.destination = screenTarget;
            Draw(render, false, true); UiDrawCount++;
            ReportDrawProgress();
            uiSample.SetActive(false);
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                loading.Report(0.96f, "Завершение подготовки графики");
                var fence = AsyncGPUReadback.Request(screenTarget);
                while (!fence.done) yield return null;
                if (fence.hasError) throw new InvalidOperationException("GPU warmup readback failed.");
            }
            yield return loading.Present(0.97f, "Ожидание готовности звуков");
            foreach (var clip in sounds)
                while (clip != null && clip.loadState == AudioDataLoadState.Loading) yield return null;
            yield return loading.Present(0.99f, "Сохранение кэша графики");
            SaveCache();
            if (!cacheComplete) throw new InvalidOperationException("The cached GPU collection was not fully warmed up.");
            yield return loading.Complete();
        }

        void ReportDrawProgress() => loading.Report(0.65f + 0.3f * DrawCount / expectedDrawCount,
            $"Графика меню и арены: {DrawCount} из {expectedDrawCount}");

        void DrawPair(UniversalRenderPipeline.SingleCameraRequest request, bool shadows)
        {
            Draw(request, false, shadows);
            Draw(request, true, shadows);
            ReportDrawProgress();
        }

        void Draw(UniversalRenderPipeline.SingleCameraRequest request, bool fog, bool shadows)
        {
            bool oldFog = RenderSettings.fog;
            var oldMode = RenderSettings.fogMode;
            var oldSun = RenderSettings.sun;
            var masks = new int[sceneLights.Length];
            try
            {
                for (int i = 0; i < sceneLights.Length; i++)
                    if (sceneLights[i] != null)
                    {
                        masks[i] = sceneLights[i].cullingMask;
                        sceneLights[i].cullingMask &= ~(1 << WarmupLayer);
                    }
                RenderSettings.sun = sampleSun;
                sampleSun.shadows = shadows ? GraphicsQuality.SunShadows : LightShadows.None;
                RenderSettings.fog = fog;
                RenderSettings.fogMode = FogMode.Linear;
                RenderPipeline.SubmitRenderRequest(sampleCamera, request);
                DrawCount++;
            }
            finally
            {
                RenderSettings.fog = oldFog; RenderSettings.fogMode = oldMode; RenderSettings.sun = oldSun;
                for (int i = 0; i < sceneLights.Length; i++)
                    if (sceneLights[i] != null) sceneLights[i].cullingMask = masks[i];
            }
        }

        void BuildStage(UniversalRenderPipelineAsset pipeline, Texture2D stain, Texture2D drop)
        {
            sceneLights = FindObjectsByType<Light>();
            stage = new GameObject("GraphicsWarmupStage") { hideFlags = HideFlags.HideAndDontSave };
            stage.transform.SetParent(transform, false);
            stage.transform.position = Vector3.one * 10000f;
            var cameraObject = Child("Camera");
            sampleCamera = cameraObject.AddComponent<Camera>();
            sampleCamera.enabled = false;
            sampleCamera.orthographic = true;
            sampleCamera.orthographicSize = 6f;
            sampleCamera.nearClipPlane = 0.1f;
            sampleCamera.farClipPlane = 40f;
            sampleCamera.cullingMask = 1 << WarmupLayer;
            sampleCamera.allowHDR = pipeline.supportsHDR;
            sampleCamera.allowMSAA = true;
            sampleCamera.useOcclusionCulling = false;
            cameraObject.transform.localPosition = new Vector3(0f, 9f, -10f);
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
            var descriptor = new RenderTextureDescriptor(128, 128)
            {
                graphicsFormat = color,
                depthStencilFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                msaaSamples = pipeline.msaaSampleCount,
            };
            descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
            target = new RenderTexture(descriptor) { name = "GraphicsWarmupTarget" };
            temporary.Add(target);
            if (!target.Create()) throw new InvalidOperationException("Could not create the scene warmup target.");

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
            sampleSun = Child("Sun").AddComponent<Light>();
            sampleSun.type = LightType.Directional;
            sampleSun.cullingMask = 1 << WarmupLayer;
            sampleSun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            sampleSun.intensity = 1.3f;

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
            if (shader == null || !shader.isSupported) throw new InvalidOperationException($"Missing or unsupported effect shader: {name}.");
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
            if (Application.isEditor || states != null) return;
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
            if (states != null)
            {
                tracing = states.BeginTrace();
                if (!tracing) Debug.LogWarning($"[GraphicsWarmup] GPU tracing unavailable on {SystemInfo.graphicsDeviceType}; scene draws still run.");
            }
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
            terrainSamples.Clear(); meshSamples.Clear(); sampledMaterials.Clear();
            stage = null; target = null; uiSample = null;
        }

        void DisposeWork()
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
        }

        void OnDestroy()
        {
            StopAllCoroutines();
            DisposeWork();
            CleanStage();
            CloseLoading();
            if (tracing) states.EndTrace();
            if (states != null) Destroy(states);
            foreach (var material in retainedMaterials) if (material != null) Destroy(material);
            retainedMaterials.Clear();
            if (instance == this) instance = null;
        }
    }
}
