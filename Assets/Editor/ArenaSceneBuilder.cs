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
    /// Builds Assets/Scenes/Arena.unity: the player on one side, bots on the other, a pit in between.
    /// Whoever gets hooked is dragged over the pit, falls in and dies; bots throw their hooks at the player too.
    /// </summary>
    public static class ArenaSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Arena.unity";
        const string PudgePrefab = "Assets/Art/Characters/Pudge/Pudge.prefab";
        const string HookModel = "Assets/Art/Hooks/Standart/Hook_Standart.fbx";
        const string ChainTexture = "Assets/Art/FX/Chain/Textures/chain_standart.png";
        const string MaterialDir = "Assets/Art/Arena/Materials";
        const string AudioDir = "Assets/Audio/Pudge";
        const string AnnouncerDir = "Assets/Audio/Announcer";
        const string CoinSound = "Assets/Audio/coinsondesk.mp3";
        const string PlayerPrefabPath = "Assets/Prefabs/Characters/Pudge_Player.prefab";
        const string BotPrefabPath = "Assets/Prefabs/Characters/Pudge_Bot.prefab";
        const string LatinFont = "Assets/Art/UI/Fonts/TrajanPro-Bold.otf";
        const string CyrillicFont = "Assets/Art/UI/Fonts/Кириллица_FrizQuadrata.ttf";
        const string CoinIcon = "Assets/Art/UI/Gold.png";
        const int BotCount = 4;
        const string PlayerNickname = "Игрок";
        static readonly string[] BotNicknames = { "Мясник", "Крюкодел", "Тухляк", "Свежее Мясо" };

        [MenuItem("Pudge Wars/Build Test Arena")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return; // the open scene's changes are not lost
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var arenaGo = new GameObject("Arena");
            var arena = arenaGo.AddComponent<Arena>();
            LandscapeBuilder.Build(arena); // the ground with the pit, the hills around and the trees
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
            var player = Place(playerPrefab, "Player", PlayerNickname, new Vector3(0f, 0f, -8f), Vector3.forward);
            for (int i = 0; i < BotCount; i++)
            {
                float x = Mathf.Lerp(-arena.halfWidth + 3.5f, arena.halfWidth - 3.5f, i / (BotCount - 1f));
                Place(botPrefab, $"Bot_{i + 1}", BotNicknames[i % BotNicknames.Length], new Vector3(x, 0f, arena.pitHalfDepth + 3f + (i % 2) * 3f), Vector3.back);
            }

            follow.target = player.transform;
            follow.SnapToTarget();
            follow.crosshair = BuildHud();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath, first: false);
            Debug.Log("[ArenaSceneBuilder] saved " + ScenePath);
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
            var input = player.AddComponent<PlayerInputController>(); // camera: Camera.main at runtime
            SetupHook(player, spatialAudio: false);
            input.aimIndicator = AimIndicator(player.transform);
            var playerPrefab = PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefabPath);
            Object.DestroyImmediate(player);

            var bot = CreateCharacter("Pudge_Bot", Team.Bots);
            SetupHook(bot, spatialAudio: true);
            bot.AddComponent<BotBrain>();
            bot.GetComponent<PudgeMotor>().keepOffPit = true;
            bot.AddComponent<BotShopper>().catalog = new[] { ShopUiBuilder.AetherLens() };
            var botPrefab = PrefabUtility.SaveAsPrefabAsset(bot, BotPrefabPath);
            Object.DestroyImmediate(bot);
            return (playerPrefab, botPrefab);
        }

        static GameObject Place(GameObject prefab, string name, string nickname, Vector3 position, Vector3 forward)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = name;
            go.GetComponent<Pudge>().nickname = nickname;
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            return go;
        }

        static GameObject CreateCharacter(string name, Team team)
        {
            var root = new GameObject(name);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PudgePrefab), root.transform);
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            root.AddComponent<PudgeAnimation>();
            var hookable = root.AddComponent<Pudge>();
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

        /// <summary>Green edges of the hook's path, shown while the hook is selected (Q).</summary>
        static HookAimIndicator AimIndicator(Transform owner)
        {
            var go = new GameObject("HookAim");
            go.transform.SetParent(owner, false);
            var indicator = go.AddComponent<HookAimIndicator>();
            var mat = UnlitMat("AimLine", new Color(0.2f, 1f, 0.3f));
            LineRenderer Edge(string name)
            {
                var edge = new GameObject(name);
                // lines face up: TransformZ alignment with Z pointing down lays them flat on the ground
                edge.transform.SetParent(go.transform, false);
                edge.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var line = edge.AddComponent<LineRenderer>();
                line.sharedMaterial = mat;
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.widthMultiplier = 0.06f;
                line.alignment = LineAlignment.TransformZ;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                return line;
            }
            indicator.left = Edge("Left");
            indicator.right = Edge("Right");
            return indicator;
        }

        /// <summary>Clips decoded from the original Sounds/ folder, named after their UE paths.</summary>
        public static HookSounds LoadHookSounds()
        {
            AudioClip[] Clips(params string[] names) => names.Select(n => Clip(n)).Where(c => c != null).ToArray();
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

        /// <summary>Dota announcer lines, indexed by KillAnnouncement (no Godlike line yet).</summary>
        static AudioClip[] LoadKillSounds() =>
            new[] { "firstblood", "announcer_kill_double_01", "announcer_kill_triple_01", "announcer_kill_ultra_01", "announcer_kill_rampage_01", null }
                .Select(n => n != null && AssetDatabase.IsValidFolder(AnnouncerDir) ? Clip(n, AnnouncerDir) : null).ToArray();

        static AudioClip Clip(string name, string dir = AudioDir) =>
            AssetDatabase.FindAssets(name + " t:AudioClip", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p) == name).Select(AssetDatabase.LoadAssetAtPath<AudioClip>).FirstOrDefault();

        /// <summary>
        /// Re-assigns the hook's sounds on the character prefabs without rebuilding the arena (after the clips are
        /// imported). The revive sound needs nothing: the camera loads it from Resources.
        /// </summary>
        [MenuItem("Pudge Wars/Assign Sounds")]
        public static void AssignSounds()
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

        /// <summary>HUD canvas; returns the first-person crosshair (hidden until that view is on).</summary>
        static GameObject BuildHud()
        {
            var canvasGo = new GameObject("HUD");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>(); // without it the EventSystem sees no UI under the mouse
            // HudFonts picks the font per text at runtime; the builder sets the Cyrillic one as a start
            var fonts = canvasGo.AddComponent<HudFonts>();
            fonts.latin = AssetDatabase.LoadAssetAtPath<Font>(LatinFont);
            fonts.cyrillic = AssetDatabase.LoadAssetAtPath<Font>(CyrillicFont);
            var font = fonts.cyrillic != null ? fonts.cyrillic : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var score = Label(canvasGo.transform, "Score", font, 56, new Vector2(40f, -30f), TextAnchor.UpperLeft);
            var scoreHud = canvasGo.AddComponent<ScoreHud>();
            scoreHud.label = score;
            scoreHud.goldIcon = AbilityBarBuilder.Sprite(CoinIcon);
            TouchControlsBuilder.Build(canvasGo.transform); // before the shop: its open panel covers the sticks
            ShopUiBuilder.Build(canvasGo.transform, font, ShopUiBuilder.Catalog());
            AbilityBarBuilder.Build(canvasGo.transform, font);
            GoldPopupLayer(canvasGo.transform, font);
            HealthBarLayer(canvasGo.transform, font);
            KillAnnouncerLayer(canvasGo.transform, font);
            return Crosshair(canvasGo.transform);
        }

        /// <summary>Full-screen layer for the Dota-style health bars and nicknames over the heroes, under everything.</summary>
        static void HealthBarLayer(Transform canvas, Font font)
        {
            var go = new GameObject("HealthBars", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            go.transform.SetSiblingIndex(0);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.AddComponent<HealthBars>().font = font;
        }

        /// <summary>Full-screen layer for the "+N монет" popups, under the shop and the bars.</summary>
        static void GoldPopupLayer(Transform canvas, Font font)
        {
            var go = new GameObject("GoldPopups", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            go.transform.SetSiblingIndex(0);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var popups = go.AddComponent<GoldPopups>();
            popups.font = font;
            popups.coinSprite = AbilityBarBuilder.Sprite(CoinIcon);
            popups.coinSound = AssetDatabase.LoadAssetAtPath<AudioClip>(CoinSound);
        }

        /// <summary>Double / Triple / Ultra Kill, Rampage, Godlike in the upper middle of the screen.</summary>
        static void KillAnnouncerLayer(Transform canvas, Font font)
        {
            var rt = ShopUiBuilder.Rect(canvas, "KillAnnouncer", new Vector2(0.5f, 1f), new Vector2(0f, -230f), Vector2.zero);
            rt.SetSiblingIndex(2); // over the health bars and gold popups, under the shop and the ability bar
            var announcer = rt.gameObject.AddComponent<KillAnnouncer>();
            announcer.font = font;
            announcer.clips = LoadKillSounds();
        }

        static GameObject Crosshair(Transform canvas)
        {
            var root = ShopUiBuilder.Rect(canvas, "Crosshair", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36f, 36f));
            foreach (var size in new[] { new Vector2(36f, 4f), new Vector2(4f, 36f) })
            {
                var bar = ShopUiBuilder.Img(ShopUiBuilder.Rect(root, "Bar", new Vector2(0.5f, 0.5f), Vector2.zero, size), Color.white);
                bar.raycastTarget = false;
                bar.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            }
            root.gameObject.SetActive(false);
            return root.gameObject;
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

        /// <summary>The main menu opens the game (first); the arena comes right after it, or first without it.</summary>
        internal static void AddToBuildSettings(string path, bool first)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            int index = first ? 0 : scenes.FindIndex(s => s.path == MenuSceneBuilder.ScenePath) + 1;
            scenes.Insert(index, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
