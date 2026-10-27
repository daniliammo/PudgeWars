using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Mirror.Discovery;
using PudgeWars.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Net
{
    /// <summary>
    /// Main menu over the Menu scene's landscape with Pudge in the middle: on the left the new game (nickname, team,
    /// heroes per team, bots on free places, bot difficulty as one multiplier for every bot) and "Создать лобби", on
    /// the right the graphics preset (applied at once, <see cref="GraphicsQuality"/>) and joining a game by address or
    /// in the local network. A created or joined game waits in the lobby, which takes the new game's place: how many
    /// players are in and who plays for which team (with the bots on the free places), "Перейти за …" for every player
    /// while the other team has a free place (friends may play on one side against bots), the settings — the host may
    /// still change them — and the host's "Начать игру", which takes everyone to the arena
    /// (<see cref="PudgeNetworkManager.StartMatch"/>); a game already on takes a joining player there at once.
    /// Leaving the game returns here. The settings are remembered with the same keys as the arena's <see cref="NetMenu"/>.
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
        static readonly string[] TeamColors = { "#7FD36B", "#E8584A" };

        enum State { Idle, Connecting, Lobby }

        string nickname, address;
        Team team;
        int teamSize;
        bool bots;
        float difficulty;

        Text graphicsDescription, status;
        RectTransform newGame, lobby, join, foundList;
        readonly Dictionary<string, ServerResponse> found = new();
        bool foundChanged, searching, lobbyChanged;
        PudgeNetworkManager manager;
        State shown;
        string joining;  // the address being joined
        bool connected;  // to it, at some point
        bool leaving;    // by this player's own choice

        void Start()
        {
            nickname = PlayerPrefs.GetString(NetMenu.NicknameKey, "Игрок");
            address = PlayerPrefs.GetString(NetMenu.AddressKey, "127.0.0.1");
            bots = PlayerPrefs.GetInt(NetMenu.BotsKey, 1) != 0;
            team = PlayerPrefs.GetInt(TeamKey, (int)Team.Bots) == (int)Team.Player ? Team.Player : Team.Bots;
            teamSize = Mathf.Clamp(PlayerPrefs.GetInt(TeamSizeKey, 3), 1, MaxTeamSize);
            difficulty = Mathf.Clamp(PlayerPrefs.GetFloat(DifficultyKey, 1f), MinDifficulty, MaxDifficulty);

            manager = PudgeNetworkManager.Instance;
            if (manager != null)
            {
                manager.ServerFound += OnServerFound;
                manager.LobbyChanged += OnLobbyChanged;
            }
            Build();
            shown = Current();
            ShowState();
        }

        void OnDestroy()
        {
            if (manager == null) return;
            manager.ServerFound -= OnServerFound;
            manager.LobbyChanged -= OnLobbyChanged;
            if (searching) manager.StopSearch();
        }

        void OnLobbyChanged() => lobbyChanged = true;

        static State Current() =>
            NetworkServer.active || NetworkClient.isConnected ? State.Lobby : NetworkClient.active ? State.Connecting : State.Idle;

        void OnServerFound(ServerResponse response)
        {
            found[response.uri.ToString()] = response;
            foundChanged = true;
        }

        void Update()
        {
            if (NetworkClient.isConnected) connected = true;
            var state = Current();
            if (state != shown)
            {
                // back from a game that ended here: say why, unless the player left it
                if (state == State.Idle && joining != null && !leaving)
                    status.text = connected ? "Хост закрыл лобби" : $"Не удалось подключиться к {joining}";
                shown = state;
                ShowState();
            }
            else if (lobbyChanged && shown != State.Idle) RebuildLobby();
            lobbyChanged = false;

            if (!foundChanged) return;
            foundChanged = false;
            foreach (Transform old in foundList) Destroy(old.gameObject);
            foreach (var uri in found.Keys.Take(MaxFoundGames))
                Button(foundList, "Войти: " + uri, secondaryButtonColor, 56f, () => Join(uri));
            if (found.Count > 0) status.text = "";
        }

        // actions /////////////////////////////////////////////////////////////

        /// <summary>A host in this menu: its lobby opens here.</summary>
        void CreateGame()
        {
            if (manager == null) return;
            Remember();
            StopSearch();
            manager.nickname = nickname;
            ApplySettings();
            joining = null;
            leaving = false;
            status.text = "";
            manager.StartHost();
        }

        void ApplySettings(bool withTeam = true)
        {
            if (withTeam) manager.hostTeam = team;
            manager.teamSize = teamSize;
            manager.fillWithBots = bots;
            manager.botDifficulty = difficulty;
        }

        /// <summary>The host changed the settings in the lobby: the players see them at once (its team changes as everyone's does).</summary>
        void LobbySettingsChanged()
        {
            Remember();
            ApplySettings(withTeam: false);
            manager.UpdateLobby();
        }

        /// <summary>This player goes over to the other team (the server grants it while that team has a free place).</summary>
        void SwitchTeam(Team to) => manager.RequestTeam(to);

        void Join(string target)
        {
            if (manager == null || string.IsNullOrWhiteSpace(target)) return;
            Remember();
            StopSearch();
            manager.nickname = nickname;
            joining = target.Trim();
            connected = leaving = false;
            status.text = "";
            manager.Join(target);
        }

        void StartMatch()
        {
            if (manager != null) manager.StartMatch();
        }

        void Leave()
        {
            if (manager == null) return;
            leaving = true;
            if (NetworkServer.active && NetworkClient.active) manager.StopHost();
            else if (NetworkServer.active) manager.StopServer();
            else manager.StopClient();
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

            // left: the new game, and in its place the lobby once the game is created or joined
            newGame = Panel(root, "NewGame", new Vector2(0f, 0.5f), new Vector2(40f, -30f));
            Label(newGame, "Новая игра", 44, TextAnchor.MiddleLeft, 60f).color = titleColor;
            Row(newGame, "Ник", row => Field(row, nickname, 20, v => nickname = v));
            Settings(newGame, null);
            Spacer(newGame, 8f);
            Button(newGame, "Создать лобби", buttonColor, 84f, CreateGame).fontSize = 40;
            lobby = Panel(root, "Lobby", new Vector2(0f, 0.5f), new Vector2(40f, -30f));

            // right: graphics, then joining
            var graphics = Panel(root, "Graphics", new Vector2(1f, 1f), new Vector2(-40f, -190f));
            Label(graphics, "Графика", 44, TextAnchor.MiddleLeft, 60f).color = titleColor;
            Row(graphics, "Пресет", row => Stepper(row, () => GraphicsQuality.Names[(int)GraphicsQuality.Current], d =>
            {
                int count = GraphicsQuality.Names.Length;
                GraphicsQuality.Set((GraphicsPreset)(((int)GraphicsQuality.Current + d + count) % count));
                graphicsDescription.text = GraphicsQuality.Describe(GraphicsQuality.Current);
            }));
            Row(graphics, "Сглаживание", row => Checkbox(row, GraphicsQuality.Antialiasing, on =>
            {
                GraphicsQuality.SetAntialiasing(on);
                graphicsDescription.text = GraphicsQuality.Describe(GraphicsQuality.Current);
            }));
            graphicsDescription = Label(graphics, GraphicsQuality.Describe(GraphicsQuality.Current), 24, TextAnchor.UpperLeft, 64f);
            graphicsDescription.color = new Color(0.8f, 0.82f, 0.85f);

            join = Panel(root, "Join", new Vector2(1f, 0f), new Vector2(-40f, 40f));
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

        /// <summary>
        /// The settings of the game; <paramref name="changed"/> is called after each change (the lobby's host, where
        /// the team is not among them: it is changed by going over, as every player does).
        /// </summary>
        void Settings(RectTransform panel, Action changed, bool withTeam = true)
        {
            void Step(Action change)
            {
                change();
                changed?.Invoke();
            }
            if (withTeam) Row(panel, "Команда", row => Stepper(row, () => TeamNames[(int)team], _ => Step(() => team = team == Team.Player ? Team.Bots : Team.Player)));
            Row(panel, "Героев в команде", row => Stepper(row, () => teamSize.ToString(), d => Step(() => teamSize = Mathf.Clamp(teamSize + d, 1, MaxTeamSize))));
            Row(panel, "Боты на свободных местах", row => Stepper(row, () => bots ? "Да" : "Нет", _ => Step(() => bots = !bots)));
            Row(panel, "Сложность ботов", row => Stepper(row, () => $"×{difficulty:0.##}", d => Step(() =>
                difficulty = Mathf.Clamp(Mathf.Round((difficulty + d * DifficultyStep) / DifficultyStep) * DifficultyStep, MinDifficulty, MaxDifficulty))));
        }

        void ShowState()
        {
            newGame.gameObject.SetActive(shown == State.Idle);
            join.gameObject.SetActive(shown == State.Idle);
            lobby.gameObject.SetActive(shown != State.Idle);
            if (shown != State.Idle) RebuildLobby();
        }

        /// <summary>The lobby panel anew from the lobby the server sent last.</summary>
        void RebuildLobby()
        {
            foreach (Transform old in lobby)
            {
                old.gameObject.SetActive(false); // out of the layout now, destroyed at the end of the frame
                Destroy(old.gameObject);
            }
            Label(lobby, "Лобби", 44, TextAnchor.MiddleLeft, 60f).color = titleColor;
            bool host = NetworkServer.active;
            var info = manager.Lobby;
            if (info.players == null)
            {
                Label(lobby, host ? "Открываю лобби…" : $"Подключение к {joining}…", 28, TextAnchor.MiddleLeft, 44f);
                Button(lobby, "Отмена", secondaryButtonColor, 64f, Leave);
                return;
            }

            Label(lobby, $"Игроков в лобби: {info.players.Length} из {info.teamSize * 2}", 34, TextAnchor.MiddleLeft, 50f);
            foreach (var side in new[] { Team.Player, Team.Bots })
                Label(lobby, TeamLine(side, info), 26, TextAnchor.UpperLeft, 34f);
            if (info.you >= 0 && info.you < info.players.Length)
            {
                var own = info.players[info.you].team;
                if (host && team != own)
                {
                    team = own; // the host's side is remembered for the next game
                    Remember();
                }
                var other = own == Team.Player ? Team.Bots : Team.Player;
                if (info.players.Count(p => p.team == other) < info.teamSize)
                    Button(lobby, $"Перейти за {TeamNames[(int)other]}", secondaryButtonColor, 56f, () => SwitchTeam(other));
                else
                    Label(lobby, $"В {TeamNames[(int)other]} нет мест", 24, TextAnchor.MiddleLeft, 40f).color = new Color(0.8f, 0.82f, 0.85f);
            }
            Spacer(lobby, 4f);
            if (host) Settings(lobby, LobbySettingsChanged, withTeam: false);
            else
            {
                Row(lobby, "Героев в команде", row => Label(row, info.teamSize.ToString(), 28, TextAnchor.MiddleCenter));
                Row(lobby, "Боты на свободных местах", row => Label(row, info.fillWithBots ? "Да" : "Нет", 28, TextAnchor.MiddleCenter));
                Row(lobby, "Сложность ботов", row => Label(row, $"×{info.botDifficulty:0.##}", 28, TextAnchor.MiddleCenter));
            }
            Spacer(lobby, 8f);
            if (host)
            {
                var hint = Label(lobby, $"Друзья подключаются по адресу {NetMenu.LocalAddresses()} (порт {manager.Kcp.port}/UDP)", 22, TextAnchor.UpperLeft, 30f);
                hint.color = new Color(0.8f, 0.82f, 0.85f);
                Button(lobby, "Начать игру", buttonColor, 84f, StartMatch).fontSize = 40;
                Button(lobby, "Распустить лобби", secondaryButtonColor, 60f, Leave);
            }
            else
            {
                Label(lobby, "Ждём, когда хост начнёт игру…", 26, TextAnchor.MiddleLeft, 40f).color = new Color(0.8f, 0.82f, 0.85f);
                Button(lobby, "Покинуть лобби", secondaryButtonColor, 60f, Leave);
            }
        }

        /// <summary>"Radiant: Хост, Вася + 2 бота" in the team's colour.</summary>
        static string TeamLine(Team side, LobbyMessage info)
        {
            var names = info.players.Where(p => p.team == side).Select(p => p.nickname).ToList();
            int free = info.fillWithBots ? Mathf.Max(0, info.teamSize - names.Count) : 0;
            string line = names.Count > 0 ? string.Join(", ", names) : "никого";
            if (free > 0) line += $" + {free} {Bots(free)}";
            return $"<color={TeamColors[(int)side]}>{TeamNames[(int)side]}</color>: {line}";
        }

        static string Bots(int n) => (n % 100 is >= 11 and <= 14) || n % 10 is 0 or >= 5 ? "ботов" : n % 10 == 1 ? "бот" : "бота";

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

        /// <summary>A square box with a check mark inside while on; the click flips it.</summary>
        void Checkbox(RectTransform row, bool value, Action<bool> changed)
        {
            var holder = Rect(row, "Checkbox");
            var box = Rect(holder, "Box");
            box.anchorMin = box.anchorMax = new Vector2(0f, 0.5f);
            box.pivot = new Vector2(0f, 0.5f);
            box.sizeDelta = new Vector2(48f, 48f);
            var image = box.gameObject.AddComponent<Image>();
            image.color = fieldColor;
            var mark = Rect(box, "Mark");
            mark.anchorMin = Vector2.zero;
            mark.anchorMax = Vector2.one;
            mark.offsetMin = new Vector2(10f, 10f);
            mark.offsetMax = new Vector2(-10f, -10f);
            mark.gameObject.AddComponent<Image>().color = titleColor;
            mark.gameObject.SetActive(value);
            var button = box.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                value = !value;
                mark.gameObject.SetActive(value);
                changed(value);
            });
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
