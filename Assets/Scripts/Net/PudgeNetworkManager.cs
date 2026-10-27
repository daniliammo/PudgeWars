using System;
using System.Collections.Generic;
using System.Linq;
using kcp2k;
using Mirror;
using Mirror.Discovery;
using PudgeWars.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PudgeWars.Net
{
    /// <summary>A client asks the server for a place in the game (a hero in the arena, a line in the lobby).</summary>
    public struct JoinMessage : NetworkMessage
    {
        public string nickname;
    }

    /// <summary>A player in the lobby asks to play for the other team (granted while it has a free place).</summary>
    public struct TeamChangeMessage : NetworkMessage
    {
        public Team team;
    }

    /// <summary>A player of the game: their nickname and team.</summary>
    public struct LobbyPlayer
    {
        public string nickname;
        public Team team;
    }

    /// <summary>The server's lobby, sent to every client whenever it changes: who is in and the game's settings.</summary>
    public struct LobbyMessage : NetworkMessage
    {
        public LobbyPlayer[] players;
        /// <summary>Index of the receiving player in <see cref="players"/> (-1 if not among them yet).</summary>
        public int you;
        public int teamSize;
        public bool fillWithBots;
        public float botDifficulty;
    }

    /// <summary>
    /// Mirror network manager, created at start-up (no scene setup needed): KCP over UDP, LAN discovery, the
    /// <see cref="NetMenu"/>. A game started in the main menu opens a lobby there: players join it and see who is in
    /// and the settings (<see cref="Lobby"/>), the host may still change them, and <see cref="StartMatch"/> takes
    /// everyone to the arena. A game started in the arena itself (or joined later) goes on there at once: the arena's
    /// own heroes give way to network ones. Each player joins the team with fewer players and in the lobby may go
    /// over to the other one while it has a free place (<see cref="RequestTeam"/>) — so friends can play on one side
    /// against bots; bots fill the teams up to <see cref="teamSize"/>. The server simulates everything, clients send input and show the result
    /// (<see cref="Simulation.IsProxy"/>). Leaving the network game reloads the arena offline, or returns to the main
    /// menu if the game was started from it.
    /// A build started with -batchmode or -server runs a dedicated server in the arena (port: -port N, default 7777).
    /// </summary>
    public class PudgeNetworkManager : NetworkManager
    {
        public const uint PlayerAssetId = 0x50570001, BotAssetId = 0x50570002, MatchAssetId = 0x50570003;
        public const ushort DefaultPort = 7777;
        public const string ArenaScene = "Arena", MenuScene = "Menu";
        const long DiscoveryHandshake = 0x5057_4E45_5457_4F52; // only Pudge Wars servers answer

        [Tooltip("Heroes per team in a network game; bots take the places players leave free.")]
        public int teamSize = 3;
        public bool fillWithBots = true;
        [Tooltip("The host's own team (Dire by default); the other players join the team with fewer players.")]
        public Team hostTeam = Team.Bots;
        [Tooltip("Difficulty multiplier of every bot the server spawns (BotBrain.difficulty, 1 = default).")]
        [Min(0.05f)] public float botDifficulty = 1f;
        public NetPrefabs prefabs;
        [Tooltip("Sent to the server when joining.")]
        public string nickname = "Игрок";

        public static PudgeNetworkManager Instance => singleton as PudgeNetworkManager;
        public KcpTransport Kcp => transport as KcpTransport;
        /// <summary>A game in the local network answered the search (<see cref="SearchLan"/>).</summary>
        public event Action<ServerResponse> ServerFound;
        /// <summary>The lobby as the server last sent it (on a client, the host included); players are null before that.</summary>
        public LobbyMessage Lobby { get; private set; }
        public event Action LobbyChanged;
        /// <summary>The server waits in the main menu's lobby: the match has not started.</summary>
        public static bool InLobby => NetworkServer.active && !NetworkServer.isLoadingScene && Arena.Instance == null;

        /// <summary>
        /// LAN discovery, made when first needed: on its own it starts advertising in headless mode, which is how the
        /// tests run.
        /// </summary>
        NetworkDiscovery Discovery
        {
            get
            {
                if (discovery != null) return discovery;
                discovery = gameObject.AddComponent<NetworkDiscovery>();
                discovery.transport = transport;
                discovery.secretHandshake = DiscoveryHandshake;
                discovery.OnServerFound ??= new ServerFoundUnityEvent<ServerResponse>();
                discovery.OnServerFound.AddListener(response => ServerFound?.Invoke(response));
                return discovery;
            }
        }

        /// <summary>A player as the server knows them, from their join message on; in the order they joined.</summary>
        class Player
        {
            public NetworkConnectionToClient conn;
            public string nickname;
            public Team team;
        }

        readonly List<NetHero> bots = new();
        readonly List<Player> players = new();
        NetworkDiscovery discovery;
        bool reloadArena, returnToMenu;
        int nextBotName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateOnStartup()
        {
            if (singleton != null) return;
            var prefabs = NetPrefabs.Load();
            if (prefabs == null) return;

            // assembled while inactive, so Awake sees the transport and the settings
            var go = new GameObject("Network");
            go.SetActive(false);
            var kcp = go.AddComponent<KcpTransport>();
            var manager = go.AddComponent<PudgeNetworkManager>();
            manager.transport = kcp;
            manager.prefabs = prefabs;
            manager.autoCreatePlayer = false; // heroes are made by OnJoin, not from playerPrefab
            // 60 a second: a client shows the match about two send intervals behind (snapshot interpolation), so a
            // higher rate is a shorter delay; the state of 6 heroes is small even for a phone's connection
            manager.sendRate = 60;
            manager.maxConnections = 16;
            manager.headlessStartMode = HeadlessStartOptions.DoNothing; // see Start: tests run headless too
            go.AddComponent<NetMenu>();
            FrameStatsOverlay.NetworkStats = NetworkStatsText;
            go.SetActive(true);
        }

        /// <summary>
        /// Lines for the F11 panel: on a client the round trip to the server (ping), how much it jitters and how far
        /// behind the server the client shows the match; on the host every client's ping.
        /// </summary>
        static string NetworkStatsText()
        {
            var sb = new System.Text.StringBuilder();
            int rate = singleton != null ? singleton.sendRate : 0;
            if (NetworkServer.active)
            {
                sb.Append(NetworkClient.active ? "Сеть: хост" : "Сеть: сервер").Append(", отправка ").Append(rate).Append(" Гц\n");
                foreach (var conn in NetworkServer.connections.Values)
                {
                    if (conn is LocalConnectionToClient) continue;
                    var hero = conn.identity != null ? conn.identity.GetComponent<Pudge>() : null;
                    sb.Append("  ").Append(hero != null ? hero.Nickname : conn.address)
                      .Append(": пинг ").Append((conn.rtt * 1000).ToString("0")).Append(" мс\n");
                }
                return sb.ToString();
            }
            if (!NetworkClient.isConnected) return "Сеть: нет (без сети)\n";
            double jitter = Math.Sqrt(Math.Max(0, NetworkTime.rttVariance));
            sb.Append("Пинг: ").Append((NetworkTime.rtt * 1000).ToString("0")).Append(" мс (±").Append((jitter * 1000).ToString("0")).Append(")\n");
            sb.Append("Буфер сглаживания: ").Append((NetworkClient.bufferTime * 1000).ToString("0"))
              .Append(" мс (×").Append(NetworkClient.bufferTimeMultiplier.ToString("0.0")).Append("), отправка ").Append(rate).Append(" Гц\n");
            // what the player sees lags the server by half the round trip, one send interval and the buffer
            double behind = NetworkTime.rtt / 2 + NetworkServer.sendInterval + NetworkClient.bufferTime;
            sb.Append("Отставание картинки: ~").Append((behind * 1000).ToString("0")).Append(" мс\n");
            return sb.ToString();
        }

        public override void Start()
        {
            base.Start();
            var args = Environment.GetCommandLineArgs();
            int portArg = Array.IndexOf(args, "-port");
            if (portArg >= 0 && portArg + 1 < args.Length && ushort.TryParse(args[portArg + 1], out var port)) Kcp.port = port;
            // a player build in batch mode (or with -server) is a dedicated server; the editor never starts one by itself.
            // Nobody would start its match from the lobby: it goes to the arena at once
            if (args.Contains("-server") || (Application.isBatchMode && !Application.isEditor))
            {
                StartServer();
                if (InLobby) StartMatch();
            }
        }

        // a dedicated server ticks at the send rate; the editor (tests run headless) keeps its frame rate
        public override void ConfigureHeadlessFrameRate()
        {
            if (!Application.isEditor) base.ConfigureHeadlessFrameRate();
        }

        public override void Update()
        {
            base.Update();
            if (!reloadArena || NetworkServer.active || NetworkClient.active) return;
            reloadArena = false;
            bool toMenu = returnToMenu && Application.CanStreamedLevelBeLoaded(MenuScene);
            returnToMenu = false;
            // the lobby closed in the main menu: it simply shows the new game again
            if (SceneManager.GetActiveScene().name == MenuScene) return;
            if (toMenu) SceneManager.LoadScene(MenuScene);
            else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>A game started (or joined) in the main menu returns there when it ends.</summary>
        void RememberWhereStarted()
        {
            if (SceneManager.GetActiveScene().name == MenuScene) returnToMenu = true;
        }

        /// <summary>Joins a game by "host", "host:port" or a kcp:// address found in the local network.</summary>
        public void Join(string target)
        {
            target = target.Trim();
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme == "kcp")
            {
                networkAddress = uri.Host;
                StartClient(uri);
                return;
            }
            int colon = target.LastIndexOf(':');
            if (colon > 0 && ushort.TryParse(target.Substring(colon + 1), out var port))
            {
                Kcp.port = port;
                target = target.Substring(0, colon);
            }
            networkAddress = target;
            StartClient();
        }

        // server //////////////////////////////////////////////////////////////

        public override void OnStartServer()
        {
            NetworkServer.RegisterHandler<JoinMessage>(OnJoin);
            NetworkServer.RegisterHandler<TeamChangeMessage>(OnTeamChange);
            RememberWhereStarted();
            players.Clear();
            if (Arena.Instance != null)
            {
                // players joining later load the arena first (Mirror sends them the scene when they connect)
                networkSceneName = SceneManager.GetActiveScene().name;
                StartArenaGame();
            }
            try { Discovery.AdvertiseServer(); }
            catch (Exception e) { Debug.LogWarning($"LAN discovery is off: {e.Message}"); }
        }

        /// <summary>From the lobby: every player goes to the arena, their heroes appear as they get there.</summary>
        public void StartMatch()
        {
            if (!InLobby) return;
            ServerChangeScene(ArenaScene);
        }

        public override void OnServerSceneChanged(string sceneName)
        {
            if (Arena.Instance != null) StartArenaGame();
        }

        /// <summary>The arena's own heroes leave, the match object and the bots come; the players' heroes come when ready.</summary>
        void StartArenaGame()
        {
            LeaveOfflineGame();
            var match = Build("Match", go => go.AddComponent<NetMatch>());
            NetworkServer.Spawn(match, MatchAssetId);
            FillWithBots();
            foreach (var player in players) SpawnHero(player);
        }

        /// <summary>Asks the local network for games; answers come as <see cref="ServerFound"/>.</summary>
        public void SearchLan() => Discovery.StartDiscovery();

        public void StopSearch()
        {
            if (discovery != null) discovery.StopDiscovery();
        }

        public override void OnStopServer()
        {
            StopSearch();
            bots.Clear();
            players.Clear();
            reloadArena = true;
        }

        void OnJoin(NetworkConnectionToClient conn, JoinMessage msg)
        {
            string nick = string.IsNullOrWhiteSpace(msg.nickname) ? $"Игрок {conn.connectionId}" : msg.nickname.Trim();
            if (nick.Length > 20) nick = nick.Substring(0, 20);
            var player = players.Find(p => p.conn == conn);
            if (player == null)
            {
                player = new Player { conn = conn, team = TeamForNewPlayer(conn) };
                players.Add(player);
            }
            player.nickname = nick;
            if (InLobby) UpdateLobby();
            else
            {
                SpawnHero(player);
                FillWithBots();
            }
        }

        /// <summary>In the lobby: the player goes over to the asked team if it still has a free place.</summary>
        void OnTeamChange(NetworkConnectionToClient conn, TeamChangeMessage msg)
        {
            var player = players.Find(p => p.conn == conn);
            if (!InLobby || player == null || player.team == msg.team || Players(msg.team) >= teamSize) return;
            player.team = msg.team;
            if (conn is LocalConnectionToClient) hostTeam = msg.team;
            UpdateLobby();
        }

        public override void OnServerReady(NetworkConnectionToClient conn)
        {
            base.OnServerReady(conn);
            var player = players.Find(p => p.conn == conn);
            if (player == null) return;
            SpawnHero(player);
            FillWithBots();
        }

        /// <summary>The player's hero, once they are in the arena (ready) and still have none.</summary>
        void SpawnHero(Player player)
        {
            var conn = player.conn;
            if (Arena.Instance == null || NetworkServer.isLoadingScene || !conn.isReady || conn.identity != null) return;
            var hero = SpawnPoint(player.team, out var rotation, out var position) ? BuildHero(false, position, rotation) : null;
            if (hero == null) return;
            hero.GetComponent<NetHero>().Setup(player.team, player.nickname);
            NetworkServer.AddPlayerForConnection(conn, hero, PlayerAssetId);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            players.RemoveAll(p => p.conn == conn);
            base.OnServerDisconnect(conn); // destroys the player's hero
            if (InLobby) UpdateLobby();
            else FillWithBots();
        }

        int Players(Team team) => players.Count(p => p.team == team);

        /// <summary>
        /// The host plays for <see cref="hostTeam"/> (Dire by default), the others join the team with fewer players,
        /// the host's opponents when even (so the first client plays against the host).
        /// </summary>
        Team TeamForNewPlayer(NetworkConnectionToClient conn)
        {
            if (conn is LocalConnectionToClient) return hostTeam;
            var other = hostTeam == Team.Bots ? Team.Player : Team.Bots;
            return Players(hostTeam) < Players(other) ? hostTeam : other;
        }

        /// <summary>
        /// In the lobby: every player gets the lobby, with their own place in it marked. The players keep their teams
        /// (chosen at joining or changed since); the host's follows <see cref="hostTeam"/>. The host calls it after
        /// changing the settings.
        /// </summary>
        public void UpdateLobby()
        {
            if (!InLobby) return;
            var host = players.Find(p => p.conn is LocalConnectionToClient);
            if (host != null) host.team = hostTeam;
            var lobby = new LobbyMessage
            {
                players = players.Select(p => new LobbyPlayer { nickname = p.nickname, team = p.team }).ToArray(),
                teamSize = teamSize,
                fillWithBots = fillWithBots,
                botDifficulty = botDifficulty,
            };
            for (int i = 0; i < players.Count; i++)
            {
                lobby.you = i;
                players[i].conn.Send(lobby);
            }
        }

        /// <summary>Keeps every team at <see cref="teamSize"/> heroes with bots (or removes the bots when off).</summary>
        public void FillWithBots()
        {
            if (!NetworkServer.active || Arena.Instance == null) return;
            bots.RemoveAll(b => b == null);
            foreach (var team in new[] { Team.Player, Team.Bots })
            {
                int wanted = fillWithBots ? Mathf.Max(0, teamSize - Players(team)) : 0;
                var mine = bots.Where(b => b.Team == team).ToList();
                for (int i = mine.Count; i < wanted; i++) SpawnBot(team);
                for (int i = wanted; i < mine.Count; i++)
                {
                    bots.Remove(mine[i]);
                    NetworkServer.Destroy(mine[i].gameObject);
                }
            }
        }

        void SpawnBot(Team team)
        {
            if (!SpawnPoint(team, out var rotation, out var position)) return;
            var hero = BuildHero(true, position, rotation);
            var names = prefabs.botNames;
            string nick = names.Length > 0 ? names[nextBotName++ % names.Length] : $"Бот {nextBotName++}";
            if (hero.TryGetComponent<BotBrain>(out var brain)) brain.difficulty = botDifficulty;
            var net = hero.GetComponent<NetHero>();
            net.Setup(team, nick);
            NetworkServer.Spawn(hero, BotAssetId);
            bots.Add(net);
        }

        static bool SpawnPoint(Team team, out Quaternion rotation, out Vector3 position)
        {
            var arena = Arena.Instance;
            position = arena != null ? arena.RandomPointOnSide(team, 1.5f) : Vector3.zero;
            rotation = Quaternion.LookRotation(team == Team.Bots ? Vector3.back : Vector3.forward);
            return arena != null;
        }

        // client //////////////////////////////////////////////////////////////

        public override void OnStartClient()
        {
            // custom spawning: the heroes are built from the gameplay prefabs exactly as on the server
            NetworkClient.RegisterSpawnHandler(PlayerAssetId, msg => BuildHero(false, msg.position, msg.rotation), Unspawn);
            NetworkClient.RegisterSpawnHandler(BotAssetId, msg => BuildHero(true, msg.position, msg.rotation), Unspawn);
            NetworkClient.RegisterSpawnHandler(MatchAssetId, msg => Build("Match", go => go.AddComponent<NetMatch>()), Unspawn);
            NetworkClient.RegisterHandler<LobbyMessage>(OnLobby);
            if (NetworkServer.active) return; // host: the server part has set everything up
            RememberWhereStarted();
            Simulation.IsProxy = true;
            LeaveOfflineGame();
        }

        /// <summary>In the lobby: asks the server to play for <paramref name="team"/> (granted while it has a free place).</summary>
        public void RequestTeam(Team team)
        {
            if (NetworkClient.isConnected) NetworkClient.Send(new TeamChangeMessage { team = team });
        }

        void OnLobby(LobbyMessage msg)
        {
            Lobby = msg;
            LobbyChanged?.Invoke();
            // joined a lobby from the arena (F2): the lobby is shown in the main menu
            if (!NetworkServer.active && !NetworkClient.isLoadingScene && SceneManager.GetActiveScene().name != MenuScene
                && Application.CanStreamedLevelBeLoaded(MenuScene))
            {
                returnToMenu = true;
                SceneManager.LoadScene(MenuScene);
            }
        }

        /// <summary>The server took this client to the arena (the match started, or it was on already).</summary>
        public override void OnClientSceneChanged()
        {
            base.OnClientSceneChanged(); // ready
            if (!NetworkServer.active) LeaveOfflineGame();
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect(); // ready (no automatic player: see OnJoin)
            NetworkClient.Send(new JoinMessage { nickname = nickname });
        }

        public override void OnStopClient()
        {
            Simulation.IsProxy = false;
            Lobby = default;
            LobbyChanged?.Invoke();
            reloadArena = true;
        }

        static void Unspawn(GameObject spawned) => Destroy(spawned);

        // building ////////////////////////////////////////////////////////////

        /// <summary>
        /// The arena's own heroes (offline player and bots) leave: the network game brings its own.
        /// </summary>
        static void LeaveOfflineGame()
        {
            foreach (var hero in Pudge.All.ToArray())
                if (!hero.TryGetComponent<NetworkIdentity>(out _)) Destroy(hero.gameObject);
            if (Arena.Instance != null) Arena.Instance.ResetMatch();
        }

        /// <summary>
        /// A network hero from the gameplay prefab, built the same way on the server and on clients (the network
        /// components must match one to one). Assembled under an inactive parent so that nothing wakes up before
        /// it is complete; the input controller is left for the owner's own hero (NetHero.OnStartLocalPlayer).
        /// </summary>
        GameObject BuildHero(bool bot, Vector3 position, Quaternion rotation)
        {
            var holder = new GameObject("NetSpawn");
            holder.SetActive(false);
            var hero = Instantiate(bot ? prefabs.botHero : prefabs.playerHero, position, rotation, holder.transform);
            if (hero.TryGetComponent<PlayerInputController>(out var input)) DestroyImmediate(input);
            var sync = hero.AddComponent<NetworkTransformUnreliable>();
            sync.coordinateSpace = CoordinateSpace.World;
            sync.syncScale = false;
            hero.AddComponent<NetHero>().isBot = bot;
            hero.AddComponent<NetworkIdentity>();
            hero.transform.SetParent(null, true);
            Destroy(holder);
            return hero;
        }

        static GameObject Build(string name, Action<GameObject> addBehaviours)
        {
            var holder = new GameObject("NetSpawn");
            holder.SetActive(false);
            var go = new GameObject(name);
            go.transform.SetParent(holder.transform, false);
            addBehaviours(go);
            go.AddComponent<NetworkIdentity>();
            go.transform.SetParent(null, false);
            Destroy(holder);
            return go;
        }
    }
}
