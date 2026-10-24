using System.IO;
using System.Linq;
using PudgeWars.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PudgeWars.EditorTools
{
    /// <summary>
    /// Builds Assets/_Project/Scenes/Arena.unity: the player on one side, bots on the other, a pit in between.
    /// Whoever gets hooked is dragged over the pit, falls in and dies; bots throw their hooks at the player too.
    /// </summary>
    public static class ArenaSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Arena.unity";
        const string PudgePrefab = "Assets/_Project/Art/Characters/Pudge/Pudge.prefab";
        const string HookModel = "Assets/_Project/Art/Hooks/Standart/Hook_Standart.fbx";
        const string ChainTexture = "Assets/_Project/Art/FX/Chain/Textures/chain_standart.png";
        const string MaterialDir = "Assets/_Project/Art/Arena/Materials";
        const string AudioDir = "Assets/_Project/Audio/Pudge";
        const string PlayerPrefabPath = "Assets/_Project/Prefabs/Characters/Pudge_Player.prefab";
        const string BotPrefabPath = "Assets/_Project/Prefabs/Characters/Pudge_Bot.prefab";
        const int BotCount = 4;

        [MenuItem("Pudge Wars/Build Test Arena")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var arenaGo = new GameObject("Arena");
            var arena = arenaGo.AddComponent<Arena>();
            BuildGeometry(arena, arenaGo.transform);
            BuildLighting();

            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cam.AddComponent<Camera>();
            camera.fieldOfView = 55f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            cam.AddComponent<AudioListener>();
            var follow = cam.AddComponent<TopDownCamera>();

            var (playerPrefab, botPrefab) = BuildCharacterPrefabs();
            var player = Place(playerPrefab, "Player", new Vector3(0f, 0f, -8f), Vector3.forward);
            for (int i = 0; i < BotCount; i++)
            {
                float x = Mathf.Lerp(-arena.halfWidth + 3.5f, arena.halfWidth - 3.5f, i / (BotCount - 1f));
                Place(botPrefab, $"Bot_{i + 1}", new Vector3(x, 0f, arena.pitHalfDepth + 3f + (i % 2) * 3f), Vector3.back);
            }

            follow.target = player.transform;
            follow.SnapToTarget();
            BuildHud();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Debug.Log("[ArenaSceneBuilder] saved " + ScenePath);
        }

        static void BuildGeometry(Arena arena, Transform parent)
        {
            float hw = arena.halfWidth, pit = arena.pitHalfDepth, side = arena.sideDepth;
            var ground = Mat("Ground", new Color(0.30f, 0.38f, 0.22f));
            var cliff = Mat("PitWall", new Color(0.22f, 0.17f, 0.13f));
            var abyss = Mat("PitFloor", new Color(0.03f, 0.02f, 0.02f));
            var border = Mat("Border", new Color(0.35f, 0.33f, 0.30f));

            foreach (var sign in new[] { -1f, 1f })
            {
                var name = sign < 0 ? "Ground_Player" : "Ground_Bots";
                // thick blocks: their inner faces are the pit walls
                Box(name, parent, new Vector3(0f, -3f, sign * (pit + side / 2f)), new Vector3(2f * hw, 6f, side), ground);
                Box(name + "_BackWall", parent, new Vector3(0f, 0.3f, sign * (pit + side + 0.25f)), new Vector3(2f * hw + 1f, 0.6f, 0.5f), border);
                Box(name + "_LeftWall", parent, new Vector3(-hw - 0.25f, 0.3f, sign * (pit + side / 2f)), new Vector3(0.5f, 0.6f, side), border);
                Box(name + "_RightWall", parent, new Vector3(hw + 0.25f, 0.3f, sign * (pit + side / 2f)), new Vector3(0.5f, 0.6f, side), border);
                Box("Pit_EndWall_" + (sign < 0 ? "L" : "R"), parent, new Vector3(sign * (hw + 0.25f), -3f, 0f), new Vector3(0.5f, 6.6f, 2f * pit), cliff);
            }
            Box("Pit_Floor", parent, new Vector3(0f, -6.5f, 0f), new Vector3(2f * hw, 1f, 2f * pit), abyss);
            // thin slabs in front of the pit faces of the ground blocks give the pit walls their own colour
            Box("Pit_Wall_Player", parent, new Vector3(0f, -3f, -pit + 0.02f), new Vector3(2f * hw, 6f, 0.04f), cliff);
            Box("Pit_Wall_Bots", parent, new Vector3(0f, -3f, pit - 0.02f), new Vector3(2f * hw, 6f, 0.04f), cliff);
        }

        static void BuildLighting()
        {
            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.65f);
            RenderSettings.ambientEquatorColor = new Color(0.40f, 0.40f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.18f, 0.16f);
        }

        /// <summary>
        /// Pudge_Player (input) and Pudge_Bot (BotBrain) prefabs: model, team marker, hook head and chain inside.
        /// Movement and turning use Dota 2 values (PudgeMotor defaults).
        /// </summary>
        public static (GameObject player, GameObject bot) BuildCharacterPrefabs()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlayerPrefabPath)!);

            var player = CreateCharacter("Pudge_Player", Team.Player);
            player.AddComponent<PlayerInputController>(); // camera: Camera.main at runtime
            SetupHook(player, spatialAudio: false);
            var playerPrefab = PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            Object.DestroyImmediate(player);

            var bot = CreateCharacter("Pudge_Bot", Team.Bots);
            SetupHook(bot, spatialAudio: true);
            bot.AddComponent<BotBrain>();
            var botPrefab = PrefabUtility.SaveAsPrefabAsset(bot, BotPrefabPath);
            Object.DestroyImmediate(bot);
            return (playerPrefab, botPrefab);
        }

        static GameObject Place(GameObject prefab, string name, Vector3 position, Vector3 forward)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = name;
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            return go;
        }

        static GameObject CreateCharacter(string name, Team team)
        {
            var root = new GameObject(name);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PudgePrefab), root.transform);
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            root.AddComponent<PudgeAnimation>();
            var hookable = root.AddComponent<Hookable>();
            hookable.team = team;
            root.AddComponent<PudgeMotor>();
            root.AddComponent<HeroInventory>();

            // team marker under the feet (the original uses Target_M / Target_M_Red)
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "TeamMarker";
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            marker.transform.localScale = new Vector3(1.5f, 0.01f, 1.5f);
            var r = marker.GetComponent<MeshRenderer>();
            r.sharedMaterial = UnlitMat(team == Team.Player ? "Marker_Player" : "Marker_Bots",
                team == Team.Player ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.95f, 0.15f, 0.1f));
            r.shadowCastingMode = ShadowCastingMode.Off;
            return root;
        }

        static void SetupHook(GameObject owner, bool spatialAudio)
        {
            var hook = owner.AddComponent<HookAbility>();

            var head = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HookModel), owner.transform);
            head.name = "HookHead";
            hook.hookHead = head.transform;

            var chainGo = new GameObject("Chain");
            chainGo.transform.SetParent(owner.transform, false);
            var line = chainGo.AddComponent<LineRenderer>();
            line.sharedMaterial = ChainMaterial();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = 0.22f;
            line.textureMode = LineTextureMode.Tile;
            line.textureScale = new Vector2(1f / 0.44f, 1f); // one 2-link tile (2:1 texture) per 2 x width
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.enabled = false;
            hook.chain = line;

            hook.chainOrigin = owner.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "weapon_rt_1");

            var audio = owner.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = spatialAudio ? 1f : 0f;
            audio.minDistance = 5f;
            audio.maxDistance = 40f;
            hook.audioSource = audio;
            hook.sounds = LoadHookSounds();
        }

        /// <summary>Clips decoded from the original Sounds/ folder, named after their UE paths.</summary>
        public static HookSounds LoadHookSounds()
        {
            AudioClip Clip(string name) =>
                AssetDatabase.FindAssets(name + " t:AudioClip", new[] { AudioDir }).Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => Path.GetFileNameWithoutExtension(p) == name).Select(AssetDatabase.LoadAssetAtPath<AudioClip>).FirstOrDefault();
            AudioClip[] Clips(params string[] names) => names.Select(Clip).Where(c => c != null).ToArray();
            if (!AssetDatabase.IsValidFolder(AudioDir)) return new HookSounds();
            return new HookSounds
            {
                cast = Clip("Chain_Hook"),
                retract = Clip("Chain_ReturnHook"),
                impact = Clip("Popal_PopalSound"),
                hitVoice = Clips("Popal_Pud_1", "Popal_Pud_2", "Popal_Pud_3", "Popal_TargetHook"),
                missVoice = Clips("Return_Pud_anger", "Return_Pud_1"),
            };
        }

        /// <summary>Re-assigns hook sounds on the character prefabs (after the clips are imported).</summary>
        [MenuItem("Pudge Wars/Assign Hook Sounds")]
        public static void AssignHookSounds()
        {
            var sounds = LoadHookSounds();
            foreach (var path in new[] { PlayerPrefabPath, BotPrefabPath })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                root.GetComponent<HookAbility>().sounds = sounds;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void BuildHud()
        {
            var canvasGo = new GameObject("HUD");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var score = Label(canvasGo.transform, "Score", font, 56, new Vector2(40f, -30f), TextAnchor.UpperLeft);
            canvasGo.AddComponent<ScoreHud>().label = score;
            var hint = Label(canvasGo.transform, "Hint", font, 30, new Vector2(40f, -100f), TextAnchor.UpperLeft);
            hint.text = "WASD — движение, ЛКМ / Пробел — хук в сторону курсора, B — магазин";
            hint.color = new Color(1f, 1f, 1f, 0.7f);

            ShopUiBuilder.Build(canvasGo.transform, font, new[] { ShopUiBuilder.AetherLens() });
        }

        static Text Label(Transform parent, string name, Font font, int size, Vector2 pos, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(1400f, size * 1.5f);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            go.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
            return text;
        }

        static void Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
        }

        static Material Mat(string name, Color color) => SaveMat(name, "Universal Render Pipeline/Lit", m =>
        {
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.1f);
        });

        static Material UnlitMat(string name, Color color) => SaveMat(name, "Universal Render Pipeline/Unlit", m => m.SetColor("_BaseColor", color));

        static Material ChainMaterial()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(ChainTexture);
            if (ti != null && (ti.wrapMode != TextureWrapMode.Repeat || !ti.alphaIsTransparency))
            {
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }
            // M_Chain in the original: unlit, masked
            return SaveMat("M_Chain", "Universal Render Pipeline/Unlit", m =>
            {
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ChainTexture));
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f);
                m.renderQueue = (int)RenderQueue.AlphaTest;
            });
        }

        static Material SaveMat(string name, string shader, System.Action<Material> setup)
        {
            Directory.CreateDirectory(MaterialDir);
            var path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find(shader);
            setup(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
