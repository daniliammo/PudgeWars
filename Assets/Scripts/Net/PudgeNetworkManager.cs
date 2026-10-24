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
    /// <summary>A client asks the server for its hero.</summary>
    public struct JoinMessage : NetworkMessage
    {
        public string nickname;
    }

    /// <summary>
    /// Mirror network manager of the arena, created at start-up (no scene setup needed): KCP over UDP, LAN discovery,
    /// the <see cref="NetMenu"/>. Offline the arena plays as before; starting a host, a server or joining one swaps
    /// the arena's own heroes for network ones: each player gets a hero on the team with fewer players, bots fill the
    /// teams up to <see cref="teamSize"/>. The server simulates everything, clients send input and show the result
    /// (<see cref="Simulation.IsProxy"/>). Leaving the network game reloads the arena offline.
    /// A build started with -batchmode or -server runs a dedicated server (port: -port N, default 7777).
    /// </summary>
    public class PudgeNetworkManager : NetworkManager
    {
        public const uint PlayerAssetId = 0x50570001, BotAssetId = 0x50570002, MatchAssetId = 0x50570003;
        public const ushort DefaultPort = 7777;
        const long DiscoveryHandshake = 0x5057_4E45_5457_4F52; // only Pudge Wars servers answer

        [Tooltip("Heroes per team in a network game; bots take the places players leave free.")]
        public int teamSize = 3;
        public bool fillWithBots = true;
        public NetPrefabs prefabs;
        [Tooltip("Sent to the server when joining.")]
        public string nickname = "Игрок";

        public static PudgeNetworkManager Instance => singleton as PudgeNetworkManager;
        public KcpTransport Kcp => transport as KcpTransport;
        /// <summary>A game in the local network answered the search (<see cref="SearchLan"/>).</summary>
        public event Action<ServerResponse> ServerFound;

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

        readonly List<NetHero> bots = new();
        NetworkDiscovery discovery;
        bool reloadArena;
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
            manager.sendRate = 30;
            manager.maxConnections = 16;
            manager.headlessStartMode = HeadlessStartOptions.DoNothing; // see Start: tests run headless too
            go.AddComponent<NetMenu>();
            go.SetActive(true);
        }

        public override void Start()
        {
            base.Start();
            var args = Environment.GetCommandLineArgs();
            int portArg = Array.IndexOf(args, "-port");
            if (portArg >= 0 && portArg + 1 < args.Length && ushort.TryParse(args[portArg + 1], out var port)) Kcp.port = port;
            // a player build in batch mode (or with -server) is a dedicated server; the editor never starts one by itself
            if (args.Contains("-server") || (Application.isBatchMode && !Application.isEditor)) StartServer();
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
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // server //////////////////////////////////////////////////////////////

        public override void OnStartServer()
        {
            NetworkServer.RegisterHandler<JoinMessage>(OnJoin);
            LeaveOfflineGame();
            var match = Build("Match", go => go.AddComponent<NetMatch>());
            NetworkServer.Spawn(match, MatchAssetId);
            FillWithBots();
            try { Discovery.AdvertiseServer(); }
            catch (Exception e) { Debug.LogWarning($"LAN discovery is off: {e.Message}"); }
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
            reloadArena = true;
        }

        void OnJoin(NetworkConnectionToClient conn, JoinMessage msg)
        {
            if (conn.identity != null) return;
            var team = TeamForNewPlayer();
            string nick = string.IsNullOrWhiteSpace(msg.nickname) ? $"Игрок {conn.connectionId}" : msg.nickname.Trim();
            if (nick.Length > 20) nick = nick.Substring(0, 20);
            var hero = SpawnPoint(team, out var rotation, out var position) ? BuildHero(false, position, rotation) : null;
            if (hero == null) return;
            hero.GetComponent<NetHero>().Setup(team, nick);
            NetworkServer.AddPlayerForConnection(conn, hero, PlayerAssetId);
            FillWithBots();
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            base.OnServerDisconnect(conn); // destroys the player's hero
            FillWithBots();
        }

        static int Players(Team team) => NetHero.OnServer.Count(h => !h.isBot && h.Team == team);

        /// <summary>The team with fewer players; the near (player) side when even.</summary>
        static Team TeamForNewPlayer() => Players(Team.Bots) < Players(Team.Player) ? Team.Bots : Team.Player;

        /// <summary>Keeps every team at <see cref="teamSize"/> heroes with bots (or removes the bots when off).</summary>
        public void FillWithBots()
        {
            if (!NetworkServer.active) return;
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
            if (NetworkServer.active) return; // host: the server part has set everything up
            Simulation.IsProxy = true;
            LeaveOfflineGame();
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect(); // ready (no automatic player: see OnJoin)
            NetworkClient.Send(new JoinMessage { nickname = nickname });
        }

        public override void OnStopClient()
        {
            Simulation.IsProxy = false;
            reloadArena = true;
        }

        static void Unspawn(GameObject spawned) => Destroy(spawned);

        // building ////////////////////////////////////////////////////////////

        /// <summary>
        /// The arena's own heroes (offline player and bots) leave: the network game brings its own.
        /// </summary>
        static void LeaveOfflineGame()
        {
            foreach (var hero in Hookable.All.ToArray())
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
