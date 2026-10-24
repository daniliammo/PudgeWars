using Mirror;
using PudgeWars.Gameplay;
using UnityEngine;

namespace PudgeWars.Net
{
    /// <summary>
    /// Match-wide state of a network game, one per server: the score of both teams (rescues included), the
    /// kill-series announcements and rescues, which the server's <see cref="Arena"/> makes and clients' arenas only show.
    /// </summary>
    public class NetMatch : NetworkBehaviour
    {
        [SyncVar] int playerKills;
        [SyncVar] int botsKills;
        [SyncVar] int playerDeaths;
        [SyncVar] int botsDeaths;
        [SyncVar] int playerRescues;
        [SyncVar] int botsRescues;

        Arena arena;
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
        }

        public override void OnStopServer()
        {
            if (arena == null) return;
            arena.ScoreChanged -= PublishScore;
            arena.KillAnnounced -= OnKillAnnounced;
            arena.SeriesEnded -= OnSeriesEnded;
            arena.Rescued -= OnRescued;
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
