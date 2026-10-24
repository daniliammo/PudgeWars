using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PudgeWars.EditorTools
{
    /// <summary>
    /// Configures assets extracted from the original Pudge Wars (UE 5.3) build:
    /// texture import settings, URP materials, material remaps, Generic rigs, animation clips,
    /// Animator Controllers and character prefabs. Driven by Tools/AssetExtraction/unity_manifest.json.
    /// </summary>
    public static class PudgeAssetImporter
    {
        const string DefaultManifest = "Tools/AssetExtraction/unity_manifest.json";

        [Serializable] class TextureEntry { public string path; public bool normal; }
        [Serializable] class MaterialEntry
        {
            public string path, source, blend, shading, baseMap, normalMap;
            public bool twoSided;
        }
        [Serializable] class Remap { public string slot, material; }
        [Serializable] class ModelEntry
        {
            public string path, type, avatar, clip;
            public bool loop;
            public List<Remap> remaps;
        }
        [Serializable] class Manifest
        {
            public List<TextureEntry> textures;
            public List<MaterialEntry> materials;
            public List<ModelEntry> models;
        }

        [MenuItem("Pudge Wars/Setup Imported Assets")]
        public static void SetupFromMenu() => Setup(DefaultManifest);

        // Unity -batchmode -projectPath . -executeMethod PudgeWars.EditorTools.PudgeAssetImporter.RunBatch -quit
        public static void RunBatch() => Setup(DefaultManifest);

        static void Setup(string manifestPath)
        {
            var m = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            AssetDatabase.Refresh();

            foreach (var t in m.textures)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(t.path);
                ti.textureType = t.normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !t.normal;
                ti.alphaIsTransparency = false;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }

            foreach (var e in m.materials)
                CreateMaterial(e);
            AssetDatabase.SaveAssets();

            foreach (var e in m.models.Where(x => x.type == "model"))
                SetupModel(e);
            foreach (var e in m.models.Where(x => x.type == "anim"))
                SetupAnimation(e);

            foreach (var group in m.models.Where(x => x.type == "anim").GroupBy(x => x.avatar))
                CreateCharacter(group.Key, group.ToList());

            AssetDatabase.SaveAssets();
            Debug.Log($"[PudgeAssetImporter] textures {m.textures.Count}, materials {m.materials.Count}, models {m.models.Count}");
        }

        static void CreateMaterial(MaterialEntry e)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(e.path)!);
            var additive = e.blend == "BLEND_Additive";
            var shader = Shader.Find(additive ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(e.path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, e.path);
            }
            mat.shader = shader;
            var baseMap = e.baseMap != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(e.baseMap) : null;
            mat.SetTexture("_BaseMap", baseMap);
            if (additive)
            {
                // Lotar (invisibility) look: unlit additive, colour * 0.5 as in the UE material
                mat.SetColor("_BaseColor", new Color(0.5f, 0.5f, 0.5f, 1f));
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 2f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Smoothness", 0f);
                var normal = e.normalMap != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(e.normalMap) : null;
                mat.SetTexture("_BumpMap", normal);
                if (normal != null) mat.EnableKeyword("_NORMALMAP");
                else mat.DisableKeyword("_NORMALMAP");
            }
            mat.SetFloat("_Cull", e.twoSided ? 0f : 2f);
            EditorUtility.SetDirty(mat);
        }

        static void SetupModel(ModelEntry e)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(e.path);
            var skinned = e.path.Contains("/Characters/");
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.preserveHierarchy = true;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.importAnimation = false;
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.animationType = skinned ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            if (skinned) mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var r in e.remaps)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), r.slot), AssetDatabase.LoadAssetAtPath<Material>(r.material));
            mi.SaveAndReimport();
        }

        static void SetupAnimation(ModelEntry e)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(e.path);
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.preserveHierarchy = true; // animation files hold only the armature; keep paths identical to the model
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            mi.sourceAvatar = AssetDatabase.LoadAllAssetsAtPath(e.avatar).OfType<Avatar>().First();
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
            mi.SaveAndReimport();
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = e.clip;
                c.loopTime = e.loop;
                c.loopPose = false;
            }
            mi.clipAnimations = clips.Take(1).ToArray();
            mi.SaveAndReimport();
        }

        static AnimationClip LoadClip(string fbx) =>
            AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));

        // Animator Controller with one state per original clip (default: Idle / Walk) and a prefab using it.
        // Transitions are left to gameplay code: the original AnimBP logic is not part of this asset pass.
        static void CreateCharacter(string modelPath, List<ModelEntry> anims)
        {
            var dir = Path.GetDirectoryName(modelPath)!.Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(modelPath);
            var ctrlPath = $"{dir}/{name}.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath) != null)
                AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var sm = ctrl.layers[0].stateMachine;
            var pos = 0;
            foreach (var a in anims)
            {
                var st = sm.AddState(a.clip, new Vector3(300 + 220 * (pos % 3), 60 * (pos / 3), 0));
                st.motion = LoadClip(a.path);
                if (a.clip == "Idle" || (sm.defaultState == null && a.clip == "Walk")) sm.defaultState = st;
                pos++;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = name;
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.runtimeAnimatorController = ctrl;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            PrefabUtility.SaveAsPrefabAsset(go, $"{dir}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
