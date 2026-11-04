using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    public sealed partial class StartupGraphicsWarmup
    {
        [SerializeField] GraphicsWarmupLibrary library;
        [SerializeField] List<Material> retainedMaterials = new();
        readonly List<Terrain> terrainSamples = new();
        readonly HashSet<string> meshSamples = new();
        readonly HashSet<Material> sampledMaterials = new();
        GameObject uiSample;
        Light sampleSun;

        public int SceneSampleCount { get; private set; }
        public int TerrainDrawCount { get; private set; }
        public int UiDrawCount { get; private set; }

        void BuildSceneSamples()
        {
            int before = samples.Count;
            foreach (var model in library.models) AddModelSamples(model);
            foreach (var sample in library.meshes) AddSceneMesh(sample.mesh, sample.materials);
            foreach (var data in library.terrains)
            {
                AddTerrainSample(data.name, data.terrainLayers, data.treePrototypes, data.detailPrototypes);
                foreach (var tree in data.treePrototypes) AddModelSamples(tree.prefab);
                foreach (var grass in data.detailPrototypes) AddModelSamples(grass.prototype);
            }
            AddMenuSamples("Menu Radiant", library.radiant);
            AddMenuSamples("Menu Dire", library.dire);
            // Procedural arena geometry also uses scene materials without a model in the saved scene.
            foreach (var material in library.materials)
                if (material != null && !sampledMaterials.Contains(material) &&
                    material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal) &&
                    material.shader.name != "Universal Render Pipeline/Terrain/Lit")
                    AddSceneMesh(Quad(true, true), new[] { material });
            SceneSampleCount = samples.Count - before;
            BuildUiSample();
        }

        void AddMenuSamples(string name, MenuLandscape.Look look)
        {
            var trees = look.trees.Select(t => t.prefab).Append(look.landmark.prefab).Where(p => p != null).Distinct().ToArray();
            foreach (var tree in trees) AddModelSamples(tree);
            foreach (var grass in look.grass) AddModelSamples(grass);
            AddTerrainSample(name, new[] { look.ground, look.patches, look.path, library.cliff },
                trees.Select(p => new TreePrototype { prefab = p }).ToArray(), look.grass.Select(TerrainPaint.Grass).ToArray());
        }

        void AddModelSamples(GameObject model)
        {
            if (model == null) return;
            // Inspect prefab assets; never instantiate gameplay scripts, colliders, audio or network identities.
            foreach (var source in model.GetComponentsInChildren<Renderer>(true))
            {
                if (source is SkinnedMeshRenderer skin)
                    AddSceneMesh(skin.sharedMesh, skin.sharedMaterials, skin);
                else if (source is MeshRenderer)
                    AddSceneMesh(source.GetComponent<MeshFilter>()?.sharedMesh, source.sharedMaterials);
                else if (source is LineRenderer line)
                    foreach (var material in source.sharedMaterials) AddSceneLine(material, line);
            }
        }

        void AddSceneMesh(Mesh mesh, Material[] materials, SkinnedMeshRenderer skin = null)
        {
            if (mesh == null || materials.Length == 0) return;
            foreach (var material in materials)
                if (material == null || !material.shader.isSupported)
                    throw new InvalidOperationException($"Unsupported or missing material on {mesh.name}.");
            string layout = string.Join(";", mesh.GetVertexAttributes().Select(a => $"{a.attribute}/{a.format}/{a.dimension}/{a.stream}"));
            string key = $"{skin != null}|{layout}|" + string.Join(",", materials.Select(m => m.GetEntityId()));
            if (!meshSamples.Add(key)) return;
            var child = Child(mesh.name);
            var bounds = skin != null ? skin.localBounds : mesh.bounds;
            float scale = 3f / Mathf.Max(0.01f, bounds.size.x, bounds.size.y, bounds.size.z);
            child.transform.localScale = Vector3.one * scale;
            child.transform.localPosition = -bounds.center * scale;
            Renderer renderer;
            if (skin != null)
            {
                var copy = child.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = mesh;
                copy.quality = skin.quality;
                copy.updateWhenOffscreen = true;
                copy.localBounds = bounds;
                copy.bones = skin.bones.Select(bone =>
                {
                    var boneCopy = new GameObject("Bone").transform;
                    boneCopy.SetParent(child.transform, false);
                    var matrix = bone != null ? skin.transform.worldToLocalMatrix * bone.localToWorldMatrix : Matrix4x4.identity;
                    boneCopy.localPosition = matrix.GetColumn(3);
                    boneCopy.localRotation = matrix.rotation;
                    boneCopy.localScale = matrix.lossyScale;
                    return boneCopy;
                }).ToArray();
                copy.rootBone = child.transform;
                for (int i = 0; i < mesh.blendShapeCount; i++) copy.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                renderer = copy;
            }
            else
            {
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                renderer = child.AddComponent<MeshRenderer>();
            }
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.enabled = false;
            samples.Add(renderer);
            foreach (var material in materials) sampledMaterials.Add(material);
        }

        void AddSceneLine(Material material, LineRenderer source)
        {
            if (material == null) throw new InvalidOperationException("Missing line material.");
            if (!meshSamples.Add($"line|{source.generateLightingData}|{material.GetEntityId()}")) return;
            sampledMaterials.Add(material);
            var line = Child(material.name).AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.generateLightingData = source.generateLightingData;
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPositions(new[] { Vector3.left, Vector3.right });
            line.widthMultiplier = 0.25f;
            line.enabled = false;
            samples.Add(line);
        }

        void AddTerrainSample(string name, TerrainLayer[] layers, TreePrototype[] trees, DetailPrototype[] details)
        {
            var data = new TerrainData
            {
                name = "Warmup " + name, heightmapResolution = 33, alphamapResolution = 16,
                baseMapResolution = 16, size = new Vector3(8f, 1f, 8f),
            };
            temporary.Add(data);
            data.terrainLayers = layers.Where(l => l != null).ToArray();
            int count = data.terrainLayers.Length;
            if (count == 0) throw new InvalidOperationException($"No terrain layers for {name}.");
            var alpha = new float[16, 16, count];
            for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
            for (int layer = 0; layer < count; layer++) alpha[z, x, layer] = 1f / count;
            data.SetAlphamaps(0, 0, alpha);
            data.treePrototypes = trees;
            data.treeInstances = trees.Select((_, index) => new TreeInstance
            {
                prototypeIndex = index, position = new Vector3(0.15f + index % 5 * 0.15f, 0f, 0.15f + index / 5 * 0.1f),
                widthScale = 0.2f, heightScale = 0.2f, color = Color.white, lightmapColor = Color.white,
            }).ToArray();
            data.SetDetailResolution(16, 8);
            data.detailPrototypes = details;
            var density = new int[16, 16];
            for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++) density[z, x] = 1;
            for (int i = 0; i < details.Length; i++) data.SetDetailLayer(0, 0, i, density);
            var child = Child(name);
            child.SetActive(false);
            child.transform.localPosition = new Vector3(-4f, -1f, -4f);
            var terrain = child.AddComponent<Terrain>();
            terrain.terrainData = data;
            terrain.materialTemplate = library.terrainMaterial;
            terrain.drawInstanced = true;
            terrain.basemapDistance = 1000f;
            terrain.treeDistance = 1000f;
            terrain.treeBillboardDistance = 1000f;
            terrain.detailObjectDistance = 100f;
            terrain.detailObjectDensity = 1f;
            terrain.shadowCastingMode = ShadowCastingMode.On;
            terrainSamples.Add(terrain);
        }

        void BuildUiSample()
        {
            uiSample = new GameObject("Warmup UI", typeof(RectTransform), typeof(Canvas));
            uiSample.layer = WarmupLayer;
            uiSample.transform.SetParent(stage.transform, false);
            var canvas = uiSample.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = sampleCamera;
            canvas.planeDistance = 1f;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal |
                AdditionalCanvasShaderChannels.Tangent;
            var fonts = Resources.Load<UiFontLibrary>("Text/UiFonts");
            if (fonts == null) throw new InvalidOperationException("UI font library is missing.");
            for (int mask = 0; mask < 4; mask++)
            {
                var panel = UiRect("Mask " + mask, uiSample.transform);
                if ((mask & 1) != 0) { panel.AddComponent<Image>(); panel.AddComponent<Mask>().showMaskGraphic = false; }
                if ((mask & 2) != 0) panel.AddComponent<RectMask2D>();
                UiRect("Image", panel.transform).AddComponent<Image>();
                foreach (var font in new[] { fonts.latin, fonts.cyrillic })
                foreach (bool outlined in new[] { false, true })
                {
                    var label = UiRect("Text", panel.transform).AddComponent<UiText>();
                    label.font = font;
                    label.fontSize = 18;
                    label.text = "HP 123 Мана";
                    if (outlined) TextOutline.Add(label.gameObject, 1.2f, Color.black, 0.5f);
                    label.ForceMeshUpdate();
                }
            }
            Canvas.ForceUpdateCanvases();
            uiSample.SetActive(false);
        }

        GameObject UiRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = WarmupLayer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return go;
        }

        void RetainUiVariants()
        {
            if (retainedMaterials.Count > 0) return;
            foreach (var graphic in uiSample.GetComponentsInChildren<Graphic>())
            {
                var material = graphic.materialForRendering;
                if (material != null) retainedMaterials.Add(new Material(material) { hideFlags = HideFlags.HideAndDontSave });
            }
        }
    }
}
