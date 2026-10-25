using System;
using System.Collections.Generic;
using System.Linq;
using Mirror.Discovery;
using PudgeWars.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Net
{
    /// <summary>
    /// Main menu over the Menu scene's landscape with Pudge in the middle: on the left the new game (nickname, team,
    /// heroes per team, bots on free places, bot difficulty as one multiplier for every bot) and "Создать игру", on
    /// the right the graphics preset (applied at once, <see cref="GraphicsQuality"/>) and joining a game by address or
    /// in the local network. The game itself runs in the arena (<see cref="PudgeNetworkManager.LaunchInArena"/>);
    /// leaving it returns here. The settings are remembered with the same keys as the arena's <see cref="NetMenu"/>.
    /// The uGUI is built here at start-up under this canvas (its <see cref="HudFonts"/> picks the fonts).
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        const string TeamKey = "menu.team", TeamSizeKey = "menu.teamSize", DifficultyKey = "menu.botDifficulty";
        const int MaxTeamSize = 5;
        const float MinDifficulty = 0.25f, MaxDifficulty = 3f, DifficultyStep = 0.25f;
        const int MaxFoundGames = 3;
        const float CaptionWidth = 250f;

        public Font font;
        public Color panelColor = new(0.06f, 0.07f, 0.09f, 0.82f);
        public Color fieldColor = new(0.14f, 0.15f, 0.18f, 1f);
        public Color buttonColor = new(0.20f, 0.45f, 0.22f, 1f);
        public Color secondaryButtonColor = new(0.22f, 0.24f, 0.28f, 1f);
        public Color titleColor = new(1f, 0.82f, 0.25f);

        static readonly string[] TeamNames = { "Radiant", "Dire" }; // Team.Player, Team.Bots

        string nickname, address;
        Team team;
        int teamSize;
        bool bots;
        float difficulty;

        Text graphicsDescription, status;
        RectTransform foundList;
        readonly Dictionary<string, ServerResponse> found = new();
        bool foundChanged, searching;
        PudgeNetworkManager manager;

        void Start()
        {
            nickname = PlayerPrefs.GetString(NetMenu.NicknameKey, "Игрок");
            address = PlayerPrefs.GetString(NetMenu.AddressKey, "127.0.0.1");
            bots = PlayerPrefs.GetInt(NetMenu.BotsKey, 1) != 0;
            team = PlayerPrefs.GetInt(TeamKey, (int)Team.Bots) == (int)Team.Player ? Team.Player : Team.Bots;
            teamSize = Mathf.Clamp(PlayerPrefs.GetInt(TeamSizeKey, 3), 1, MaxTeamSize);
            difficulty = Mathf.Clamp(PlayerPrefs.GetFloat(DifficultyKey, 1f), MinDifficulty, MaxDifficulty);

            manager = PudgeNetworkManager.Instance;
            if (manager != null) manager.ServerFound += OnServerFound;
            Build();
        }

        void OnDestroy()
        {
            if (manager == null) return;
            manager.ServerFound -= OnServerFound;
            if (searching) manager.StopSearch();
        }

        void OnServerFound(ServerResponse response)
        {
            found[response.uri.ToString()] = response;
            foundChanged = true;
        }

        void Update()
        {
            if (!foundChanged) return;
            foundChanged = false;
            foreach (Transform old in foundList) Destroy(old.gameObject);
            foreach (var uri in found.Keys.Take(MaxFoundGames))
                Button(foundList, "Войти: " + uri, secondaryButtonColor, 56f, () => Join(uri));
            if (found.Count > 0) status.text = "";
        }

        // actions /////////////////////////////////////////////////////////////

        void CreateGame()
        {
            if (manager == null) return;
            Remember();
            StopSearch();
            manager.nickname = nickname;
            manager.hostTeam = team;
            manager.teamSize = teamSize;
            manager.fillWithBots = bots;
            manager.botDifficulty = difficulty;
            manager.LaunchInArena(manager.StartHost);
        }

        void Join(string target)
        {
            if (manager == null || string.IsNullOrWhiteSpace(target)) return;
            Remember();
            StopSearch();
            manager.nickname = nickname;
            manager.LaunchInArena(() => manager.Join(target));
        }

        void Search()
        {
            if (manager == null) return;
            found.Clear();
            foundChanged = true;
            try
            {
                manager.SearchLan();
                searching = true;
                status.text = "Ищу игры в локальной сети…";
            }
            catch (Exception e)
            {
                status.text = "Поиск не удался: " + e.Message;
            }
        }

        void StopSearch()
        {
            if (!searching) return;
            searching = false;
            manager.StopSearch();
        }

        void Remember()
        {
            if (string.IsNullOrWhiteSpace(nickname)) nickname = "Игрок";
            nickname = nickname.Trim();
            PlayerPrefs.SetString(NetMenu.NicknameKey, nickname);
            PlayerPrefs.SetString(NetMenu.AddressKey, address);
            PlayerPrefs.SetInt(NetMenu.BotsKey, bots ? 1 : 0);
            PlayerPrefs.SetInt(TeamKey, (int)team);
            PlayerPrefs.SetInt(TeamSizeKey, teamSize);
            PlayerPrefs.SetFloat(DifficultyKey, difficulty);
            PlayerPrefs.Save();
        }

        // layout //////////////////////////////////////////////////////////////

        void Build()
        {
            var root = (RectTransform)transform;

            var title = Label(root, "PUDGE WARS", 92, TextAnchor.UpperCenter);
            title.color = titleColor;
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = Vector2.one;
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -36f);
            titleRt.sizeDelta = new Vector2(0f, 120f);

            // left: the new game
            var game = Panel(root, "NewGame", new Vector2(0f, 0.5f), new Vector2(40f, -30f));
            Label(game, "Новая игра", 44, TextAnchor.MiddleLeft, 60f).color = titleColor;
            Row(game, "Ник", row => Field(row, nickname, 20, v => nickname = v));
            Row(game, "Команда", row => Stepper(row, () => TeamNames[(int)team], _ => team = team == Team.Player ? Team.Bots : Team.Player));
            Row(game, "Героев в команде", row => Stepper(row, () => teamSize.ToString(), d => teamSize = Mathf.Clamp(teamSize + d, 1, MaxTeamSize)));
            Row(game, "Боты на свободных местах", row => Stepper(row, () => bots ? "Да" : "Нет", _ => bots = !bots));
            Row(game, "Сложность ботов", row => Stepper(row, () => $"×{difficulty:0.##}",
                d => difficulty = Mathf.Clamp(Mathf.Round((difficulty + d * DifficultyStep) / DifficultyStep) * DifficultyStep, MinDifficulty, MaxDifficulty)));
            Spacer(game, 8f);
            Button(game, "Создать игру", buttonColor, 84f, CreateGame).fontSize = 40;

            // right: graphics, then joining
            var graphics = Panel(root, "Graphics", new Vector2(1f, 1f), new Vector2(-40f, -190f));
            Label(graphics, "Графика", 44, TextAnchor.MiddleLeft, 60f).color = titleColor;
            Row(graphics, "Пресет", row => Stepper(row, () => GraphicsQuality.Names[(int)GraphicsQuality.Current], d =>
            {
                int count = GraphicsQuality.Names.Length;
                GraphicsQuality.Set((GraphicsPreset)(((int)GraphicsQuality.Current + d + count) % count));
                graphicsDescription.text = GraphicsQuality.Describe(GraphicsQuality.Current);
            }));
            graphicsDescription = Label(graphics, GraphicsQuality.Describe(GraphicsQuality.Current), 24, TextAnchor.UpperLeft, 64f);
            graphicsDescription.color = new Color(0.8f, 0.82f, 0.85f);

            var join = Panel(root, "Join", new Vector2(1f, 0f), new Vector2(-40f, 40f));
            Label(join, "Подключиться", 44, TextAnchor.MiddleLeft, 60f).color = titleColor;
            Row(join, "Адрес", row => Field(row, address, 64, v => address = v));
            var buttons = Horizontal(join, 64f);
            Button(buttons, "По адресу", secondaryButtonColor, 64f, () => Join(address));
            Button(buttons, "В локальной сети", secondaryButtonColor, 64f, Search);
            status = Label(join, "", 24, TextAnchor.MiddleLeft, 34f);
            foundList = Vertical(join, "Found");

#if !UNITY_WEBGL
            var quit = Button(root, "Выход", secondaryButtonColor, 60f, Application.Quit);
            var quitRt = (RectTransform)quit.transform.parent;
            quitRt.anchorMin = quitRt.anchorMax = quitRt.pivot = new Vector2(0.5f, 0f);
            quitRt.anchoredPosition = new Vector2(0f, 30f);
            quitRt.sizeDelta = new Vector2(220f, 60f);
#endif
        }

        /// <summary>A dark panel stacking its children from the top, as tall as they are, 560 wide.</summary>
        RectTransform Panel(RectTransform parent, string name, Vector2 anchor, Vector2 position)
        {
            var rt = Rect(parent, name);
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(560f, 0f);
            rt.gameObject.AddComponent<Image>().color = panelColor;
            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 16, 24);
            layout.spacing = 10f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            rt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        RectTransform Vertical(RectTransform parent, string name)
        {
            var rt = Rect(parent, name);
            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rt;
        }

        RectTransform Horizontal(RectTransform parent, float height)
        {
            var rt = Rect(parent, "Row");
            Height(rt.gameObject, height);
            var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return rt;
        }

        /// <summary>A caption on the left (shrunk to fit), the control in the rest of the row.</summary>
        void Row(RectTransform panel, string caption, Action<RectTransform> control)
        {
            var row = Horizontal(panel, 56f);
            var label = Label(row, caption, 28, TextAnchor.MiddleLeft);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 18;
            label.resizeTextMaxSize = 28;
            Width(label.gameObject, CaptionWidth);
            control(row);
            var element = Element(row.GetChild(row.childCount - 1).gameObject);
            element.preferredWidth = 0f;
            element.flexibleWidth = 1f;
        }

        void Spacer(RectTransform parent, float height) => Height(Rect(parent, "Space").gameObject, height);

        /// <summary>"&lt; value &gt;": the buttons call <paramref name="step"/> with -1 or +1, then the value is shown again.</summary>
        Text Stepper(RectTransform row, Func<string> value, Action<int> step)
        {
            var box = Rect(row, "Stepper");
            var layout = box.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            Text shown = null;
            void Click(int d)
            {
                step(d);
                shown.text = value();
            }
            Width(Button(box, "<", fieldColor, 56f, () => Click(-1)).transform.parent.gameObject, 56f);
            shown = Label(box, value(), 28, TextAnchor.MiddleCenter);
            shown.GetComponent<LayoutElement>().flexibleWidth = 1f;
            Width(Button(box, ">", fieldColor, 56f, () => Click(1)).transform.parent.gameObject, 56f);
            return shown;
        }

        void Field(RectTransform row, string value, int limit, Action<string> changed)
        {
            var rt = Rect(row, "Field");
            rt.gameObject.AddComponent<Image>().color = fieldColor;
            var text = Label(rt, "", 28, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 4f);
            textRt.offsetMax = new Vector2(-12f, -4f);
            var input = rt.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.characterLimit = limit;
            input.text = value;
            input.onValueChanged.AddListener(v => changed(v));
        }

        Text Button(RectTransform parent, string caption, Color color, float height, Action click)
        {
            var rt = Rect(parent, "Button");
            Height(rt.gameObject, height);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => click());
            var text = Label(rt, caption, 30, TextAnchor.MiddleCenter);
            var textRt = text.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = textRt.offsetMax = Vector2.zero;
            return text;
        }

        Text Label(RectTransform parent, string caption, int size, TextAnchor anchor, float height = 0f)
        {
            var rt = Rect(parent, "Text");
            var text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.text = caption;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var element = Element(rt.gameObject);
            element.flexibleWidth = 1f;
            if (height > 0f) element.preferredHeight = element.minHeight = height;
            return text;
        }

        static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static LayoutElement Element(GameObject go) => go.TryGetComponent<LayoutElement>(out var e) ? e : go.AddComponent<LayoutElement>();

        static void Height(GameObject go, float height)
        {
            var element = Element(go);
            element.preferredHeight = element.minHeight = height;
        }

        static void Width(GameObject go, float width)
        {
            var element = Element(go);
            element.preferredWidth = element.minWidth = width;
            element.flexibleWidth = 0f;
        }
    }
}
