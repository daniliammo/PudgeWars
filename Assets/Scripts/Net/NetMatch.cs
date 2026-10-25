using Mirror;
using PudgeWars.Gameplay;
using UnityEngine;

namespace PudgeWars.Net
{
    /// <summary>A tree cut down in the match: its index on the arena's terrain and where it was cut from.</summary>
    public struct FelledTree
    {
        public int index;
        public Vector3 from;
    }

    /// <summary>
    /// Match-wide state of a network game, one per server: the score of both teams (rescues included), the
    /// kill-series announcements and rescues, which the server's <see cref="Arena"/> makes and clients' arenas only show,
    /// and the trees cut down (<see cref="TerrainTrees"/>: they fall on the clients too, a joining client finds them cut).
    /// </summary>
    public class NetMatch : NetworkBehaviour
    {
        readonly SyncList<FelledTree> felledTrees = new();

        [SyncVar] int playerKills;
        [SyncVar] int botsKills;
        [SyncVar] int playerDeaths;
        [SyncVar] int botsDeaths;
        [SyncVar] int playerRescues;
        [SyncVar] int botsRescues;

        Arena arena;
        TerrainTrees trees;
        (int, int, int, int, int, int) shown = (-1, -1, -1, -1, -1, -1);

        public override void OnStartServer()
        {
            arena = Arena.Instance;
            if (arena == null) return;
            arena.ScoreChanged += PublishScore;
            arena.KillAnnounced += OnKillAnnounced;
            arena.SeriesEnded += OnSeriesEnded;
            arena.Rescued += OnRescued;
            PublishScore(arena.Score, arena.Deaths);
            trees = TerrainTrees.Instance;
            if (trees == null) return;
            trees.Felled += OnTreeFelled;
            trees.Regrown += OnTreeRegrown;
        }

        public override void OnStopServer()
        {
            if (trees != null)
            {
                trees.Felled -= OnTreeFelled;
                trees.Regrown -= OnTreeRegrown;
            }
            if (arena == null) return;
            arena.ScoreChanged -= PublishScore;
            arena.KillAnnounced -= OnKillAnnounced;
            arena.SeriesEnded -= OnSeriesEnded;
            arena.Rescued -= OnRescued;
        }

        void OnTreeFelled(int index, Vector3 from) => felledTrees.Add(new FelledTree { index = index, from = from });

        void OnTreeRegrown(int index)
        {
            int i = felledTrees.FindIndex(t => t.index == index);
            if (i >= 0) felledTrees.RemoveAt(i);
        }

        public override void OnStartClient()
        {
            if (isServer) return; // the host's trees are the server's
            felledTrees.OnAdd += i =>
            {
                var tree = felledTrees[i];
                if (TerrainTrees.Instance != null) TerrainTrees.Instance.Fell(tree.index, tree.from);
            };
            felledTrees.OnRemove += (_, tree) =>
            {
                if (TerrainTrees.Instance != null) TerrainTrees.Instance.Regrow(tree.index);
            };
            // cut before this client joined: already lying, not falling now
            if (TerrainTrees.Instance != null)
                foreach (var tree in felledTrees) TerrainTrees.Instance.Fell(tree.index, tree.from, animate: false);
        }

        void PublishScore(int score, int deaths)
        {
            playerKills = arena.KillsOf(Team.Player);
            botsKills = arena.KillsOf(Team.Bots);
            playerDeaths = arena.DeathsOf(Team.Player);
            botsDeaths = arena.DeathsOf(Team.Bots);
            playerRescues = arena.RescuesOf(Team.Player);
            botsRescues = arena.RescuesOf(Team.Bots);
        }

        void OnKillAnnounced(Hookable killer, KillAnnouncement kind) =>
            RpcKill(killer != null ? killer.gameObject : null, (byte)kind, killer != null ? arena.RampageCount(killer) : 0);

        void OnSeriesEnded(Hookable hero, SeriesEnd reason, Hookable killer) =>
            RpcSeriesEnded(hero != null ? hero.gameObject : null, (byte)reason, killer != null ? killer.gameObject : null);

        [ClientRpc]
        void RpcKill(GameObject killer, byte kind, int rampages)
        {
            if (isServer || Arena.Instance == null) return;
            Arena.Instance.ApplyRemoteKill(HeroOf(killer), (KillAnnouncement)kind, rampages);
        }

        void OnRescued(Hookable rescued, Hookable rescuer) =>
            RpcRescued(rescued != null ? rescued.gameObject : null, rescuer != null ? rescuer.gameObject : null);

        [ClientRpc]
        void RpcRescued(GameObject rescued, GameObject rescuer)
        {
            var h = HeroOf(rescued);
            if (isServer || h == null || Arena.Instance == null) return;
            Arena.Instance.ApplyRemoteRescue(h, HeroOf(rescuer));
        }

        [ClientRpc]
        void RpcSeriesEnded(GameObject hero, byte reason, GameObject killer)
        {
            var h = HeroOf(hero);
            if (isServer || h == null || Arena.Instance == null) return;
            Arena.Instance.ApplyRemoteSeriesEnd(h, (SeriesEnd)reason, HeroOf(killer));
        }

        static Hookable HeroOf(GameObject go) => go != null ? go.GetComponent<Hookable>() : null;

        void Update()
        {
            if (isServer || !isClient || Arena.Instance == null) return;
            var score = (playerKills, botsKills, playerDeaths, botsDeaths, playerRescues, botsRescues);
            if (score == shown) return;
            shown = score;
            Arena.Instance.ApplyRemoteScore(playerKills, botsKills, playerDeaths, botsDeaths, playerRescues, botsRescues);
        }
    }
}
