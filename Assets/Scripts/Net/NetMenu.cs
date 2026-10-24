using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Mirror;
using Mirror.Discovery;
using PudgeWars.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PudgeWars.Net
{
    /// <summary>
    /// Network menu (F2 or the "Сеть" button in the top right corner): nickname, host a game (with or without bots),
    /// join by IP or pick a game found in the local network, leave the game. Drawn with IMGUI over the HUD; while it
    /// is open it takes the keyboard and the mouse from the game (<see cref="OverlayInput"/>).
    /// </summary>
    public class NetMenu : MonoBehaviour
    {
        const string NicknameKey = "net.nickname", AddressKey = "net.address", BotsKey = "net.bots";
        const float ReferenceHeight = 1080f;

        public bool IsOpen { get; private set; }

        PudgeNetworkManager manager;
        string nickname, address;
        bool bots;
        readonly Dictionary<string, ServerResponse> found = new();
        bool searching;
        GUIStyle label, button, field, title, toggle;
        float styleScale;
        Font font;
        string localAddresses;

        void Awake()
        {
            manager = GetComponent<PudgeNetworkManager>();
            nickname = PlayerPrefs.GetString(NicknameKey, "Игрок");
            address = PlayerPrefs.GetString(AddressKey, "127.0.0.1");
            bots = PlayerPrefs.GetInt(BotsKey, 1) != 0;
            manager.ServerFound += response => found[response.uri.ToString()] = response;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame) SetOpen(!IsOpen);
        }

        void SetOpen(bool open)
        {
            IsOpen = open;
            OverlayInput.Captured = open;
            if (!open) StopSearch();
        }

        void OnDisable() => OverlayInput.Captured = false;

        void OnGUI()
        {
            float scale = Mathf.Max(0.5f, Screen.height / ReferenceHeight);
            MakeStyles(scale);
            float width = Screen.width;

            if (GUI.Button(new Rect(width - 230f * scale, 10f * scale, 220f * scale, 52f * scale), IsOpen ? "Закрыть (F2)" : "Сеть (F2)", button))
                SetOpen(!IsOpen);
            if (!IsOpen) return;

            var area = new Rect(width - 640f * scale, 70f * scale, 630f * scale, Mathf.Min(Screen.height - 80f * scale, 700f * scale));
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 16f * scale, area.y + 12f * scale, area.width - 32f * scale, area.height - 24f * scale));
            GUILayout.Label("Сетевая игра", title);
            if (NetworkServer.active || NetworkClient.active) DrawSession();
            else DrawOffline();
            GUILayout.EndArea();
        }

        void DrawOffline()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Ник:", label, GUILayout.Width(110f * styleScale));
            nickname = GUILayout.TextField(nickname, 20, field);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f * styleScale);
            bots = GUILayout.Toggle(bots, $" Боты занимают свободные места (по {manager.teamSize} в команде)", toggle);
            if (GUILayout.Button("Создать игру (хост)", button)) Host();

            GUILayout.Space(12f * styleScale);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Адрес:", label, GUILayout.Width(110f * styleScale));
            address = GUILayout.TextField(address, 64, field);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Подключиться по IP", button)) Join(address);

            GUILayout.Space(12f * styleScale);
            if (GUILayout.Button(searching ? "Ищу игры в локальной сети…" : "Найти игры в локальной сети", button)) Search();
            foreach (var (uri, _) in found.ToList())
                if (GUILayout.Button("Войти: " + uri, button)) Join(uri);
        }

        void DrawSession()
        {
            if (NetworkServer.active && NetworkClient.active)
            {
                GUILayout.Label($"Ты хост. Игроков: {NetworkServer.connections.Count}", label);
                GUILayout.Label($"Друзья подключаются по адресу: {LocalAddresses()} (порт {manager.Kcp.port}/UDP)", label);
            }
            else if (NetworkServer.active)
                GUILayout.Label($"Сервер. Игроков: {NetworkServer.connections.Count}", label);
            else if (NetworkClient.isConnected)
                GUILayout.Label($"В игре: {manager.networkAddress}", label);
            else
                GUILayout.Label($"Подключение к {manager.networkAddress}…", label);

            if (GUILayout.Button(NetworkClient.isConnected || NetworkServer.active ? "Выйти из игры" : "Отмена", button)) Leave();
        }

        void Host()
        {
            Remember();
            StopSearch();
            manager.fillWithBots = bots;
            manager.nickname = nickname;
            manager.StartHost();
            SetOpen(false);
        }

        void Join(string target)
        {
            Remember();
            StopSearch();
            manager.nickname = nickname;
            target = target.Trim();
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme == "kcp")
            {
                manager.networkAddress = uri.Host;
                manager.StartClient(uri);
            }
            else
            {
                // "host" or "host:port"
                int colon = target.LastIndexOf(':');
                if (colon > 0 && ushort.TryParse(target.Substring(colon + 1), out var port))
                {
                    manager.Kcp.port = port;
                    target = target.Substring(0, colon);
                }
                manager.networkAddress = target;
                manager.StartClient();
            }
        }

        void Leave()
        {
            if (NetworkServer.active && NetworkClient.active) manager.StopHost();
            else if (NetworkServer.active) manager.StopServer();
            else manager.StopClient();
            SetOpen(false);
        }

        void Search()
        {
            found.Clear();
            try
            {
                manager.SearchLan();
                searching = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"LAN search failed: {e.Message}");
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
            PlayerPrefs.SetString(NicknameKey, nickname);
            PlayerPrefs.SetString(AddressKey, address);
            PlayerPrefs.SetInt(BotsKey, bots ? 1 : 0);
        }

        /// <summary>This machine's IPv4 addresses in the local networks.</summary>
        string LocalAddresses()
        {
            if (localAddresses != null) return localAddresses;
            try
            {
                var list = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .ToList();
                localAddresses = list.Count > 0 ? string.Join(", ", list) : "127.0.0.1";
            }
            catch (Exception)
            {
                localAddresses = "IP этого компьютера";
            }
            return localAddresses;
        }

        /// <summary>Styles in the HUD's Cyrillic font (the default IMGUI font may lack Cyrillic on Linux).</summary>
        void MakeStyles(float scale)
        {
            if (font == null)
            {
                var hud = FindFirstObjectByType<HudFonts>();
                if (hud != null) font = hud.cyrillic;
            }
            if (label != null && Mathf.Approximately(scale, styleScale) && label.font == font) return;
            styleScale = scale;
            int size = Mathf.RoundToInt(26f * scale);
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, wordWrap = true };
            title = new GUIStyle(label) { fontSize = Mathf.RoundToInt(34f * scale), fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = size, fixedHeight = 48f * scale };
            field = new GUIStyle(GUI.skin.textField) { font = font, fontSize = size, fixedHeight = 44f * scale };
            toggle = new GUIStyle(GUI.skin.toggle) { font = font, fontSize = size };
        }
    }
}
