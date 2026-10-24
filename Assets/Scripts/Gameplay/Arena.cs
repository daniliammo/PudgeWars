using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The two sides: <see cref="Player"/> is Radiant (the near side, z &lt; 0), <see cref="Bots"/> is Dire (the far
    /// side, z &gt; 0). Offline the player is Radiant against Dire bots; in a network game the host plays for Dire.
    /// </summary>
    public enum Team { Player, Bots }

    /// <summary>
    /// Test arena: two sides separated by a pit along the X axis (z = 0).
    /// The player side is z &lt; 0, the bot side is z &gt; 0 (in a network game these are simply the two teams).
    /// Keeps the score and respawns the dead; on a network client (<see cref="Simulation.IsProxy"/>) it only
    /// shows the score and the announcements the server sends.
    /// </summary>
    public class Arena : MonoBehaviour
    {
        public static Arena Instance { get; private set; }

        [Tooltip("Half of the arena width along X, metres.")]
        public float halfWidth = 14f;
        [Tooltip("Half of the pit depth along Z: |z| < pitHalfDepth is over the pit.")]
        public float pitHalfDepth = 3f;
        [Tooltip("Depth of each side from the pit edge to the back wall, metres.")]
        public float sideDepth = 11f;
        [Tooltip("A falling character dies below this height.")]
        public float killHeight = -4f;
        public float respawnDelay = 2.5f;
        [Tooltip("Gold for the hero whose hook dragged the victim into the pit.")]
        public int killGold = 250;
        [Tooltip("Gold every hero gets each passiveGoldInterval, without a popup.")]
        public int passiveGold = 2;
        [Tooltip("Seconds between passive gold ticks.")]
        public float passiveGoldInterval = 1f;
        [Tooltip("Multi-kills: the next kill must come within this time of the previous one, seconds (18 in Dota 2).")]
        public float multiKillWindow = 18f;
        [Tooltip("Kills without dying for Godlike (9 in Dota 2).")]
        public int godlikeStreak = 9;

        /// <summary>Bots killed by the player('s team).</summary>
        public int Score => kills[(int)Team.Player];
        /// <summary>Times the player('s team) was killed.</summary>
        public int Deaths => deaths[(int)Team.Player];
        /// <summary>Kills made by a team.</summary>
        public int KillsOf(Team team) => kills[(int)team];
        /// <summary>Deaths of a team's heroes, falls included.</summary>
        public int DeathsOf(Team team) => deaths[(int)team];
        /// <summary>(Score, Deaths) after any change of the counts.</summary>
        public event Action<int, int> ScoreChanged;
        /// <summary>First Blood, Double / Triple / Ultra Kill, Rampage, Godlike made by the hero.</summary>
        public event Action<Hookable, KillAnnouncement> KillAnnounced;
        /// <summary>The hero's series is over: by time (killer is null) or because the killer got him.</summary>
        public event Action<Hookable, SeriesEnd, Hookable> SeriesEnded;

        public KillStreaks Streaks { get; private set; }

        readonly int[] kills = new int[2], deaths = new int[2];
        readonly Dictionary<Hookable, int> remoteRampages = new();
        float nextPassiveGold;

        void Awake()
        {
            Instance = this;
            Streaks = new KillStreaks { multiKillWindow = multiKillWindow, godlikeStreak = godlikeStreak };
            nextPassiveGold = Time.time + passiveGoldInterval;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (Simulation.IsProxy) return;
            foreach (var hero in Streaks.ExpireSeries(Time.time))
                SeriesEnded?.Invoke((Hookable)hero, SeriesEnd.Time, null);

            for (; Time.time >= nextPassiveGold; nextPassiveGold += passiveGoldInterval)
                foreach (var hero in Hookable.All)
                    if (passiveGold > 0 && hero.TryGetComponent<HeroInventory>(out var wallet)) wallet.AddGold(passiveGold, popup: false);
        }

        public bool IsOverPit(Vector3 p) => Mathf.Abs(p.z) < pitHalfDepth && Mathf.Abs(p.x) < halfWidth;

        /// <summary>Keeps a point off the side and back walls; the pit edge is open, a careless step goes over it.</summary>
        /// <param name="pitGap">With a value, the pit edge holds too: the centre stays this far from it.</param>
        public Vector3 ClampToSide(Vector3 p, Team team, float radius, float? pitGap = null)
        {
            p.x = Mathf.Clamp(p.x, -halfWidth + radius, halfWidth - radius);
            // work on the bot side (z > 0) and mirror for the player
            float sign = team == Team.Player ? -1f : 1f;
            float depth = Mathf.Min(p.z * sign, pitHalfDepth + sideDepth - radius);
            if (pitGap is { } gap) depth = Mathf.Max(depth, pitHalfDepth + gap);
            p.z = depth * sign;
            return p;
        }

        /// <param name="pitMargin">Distance kept from the pit edge; <paramref name="margin"/> if negative.</param>
        public Vector3 RandomPointOnSide(Team team, float margin, float pitMargin = -1f)
        {
            if (pitMargin < 0f) pitMargin = margin;
            float x = UnityEngine.Random.Range(-halfWidth + margin, halfWidth - margin);
            float z = UnityEngine.Random.Range(pitHalfDepth + pitMargin, pitHalfDepth + sideDepth - margin);
            return new Vector3(x, 0f, team == Team.Player ? -z : z);
        }

        public void ReportDeath(Hookable victim, Hookable killer)
        {
            if (killer != null && killer.team == victim.team) killer = null;
            if (Streaks.RegisterDeath(victim))
                SeriesEnded?.Invoke(victim, SeriesEnd.Killed, killer);
            if (killer != null)
            {
                if (killer.TryGetComponent<HeroInventory>(out var wallet)) wallet.AddGold(killGold);
                foreach (var announcement in Streaks.RegisterKill(killer, Time.time))
                    KillAnnounced?.Invoke(killer, announcement);
            }
            deaths[(int)victim.team]++;
            if (killer != null) kills[(int)killer.team]++; // a hero that fell in by itself is nobody's kill
            ScoreChanged?.Invoke(Score, Deaths);
            StartCoroutine(Respawn(victim));
        }

        /// <summary>A network game starts here: the offline score, series and pending respawns are dropped.</summary>
        public void ResetMatch()
        {
            StopAllCoroutines();
            Array.Clear(kills, 0, kills.Length);
            Array.Clear(deaths, 0, deaths.Length);
            remoteRampages.Clear();
            Streaks = new KillStreaks { multiKillWindow = multiKillWindow, godlikeStreak = godlikeStreak };
            ScoreChanged?.Invoke(Score, Deaths);
        }

        /// <summary>Rampages in a row of the hero's current multi-kill (as the server counted them on a client).</summary>
        public int RampageCount(Hookable hero) =>
            remoteRampages.TryGetValue(hero, out var n) ? n : Streaks.RampageCount(hero);

        /// <summary>Network client: the score the server keeps.</summary>
        public void ApplyRemoteScore(int playerKills, int botsKills, int playerDeaths, int botsDeaths)
        {
            kills[(int)Team.Player] = playerKills;
            kills[(int)Team.Bots] = botsKills;
            deaths[(int)Team.Player] = playerDeaths;
            deaths[(int)Team.Bots] = botsDeaths;
            ScoreChanged?.Invoke(Score, Deaths);
        }

        /// <summary>Network client: an announcement the server made.</summary>
        public void ApplyRemoteKill(Hookable killer, KillAnnouncement kind, int rampages)
        {
            if (killer != null) remoteRampages[killer] = rampages;
            KillAnnounced?.Invoke(killer, kind);
        }

        /// <summary>Network client: a series the server saw end.</summary>
        public void ApplyRemoteSeriesEnd(Hookable hero, SeriesEnd reason, Hookable killer) =>
            SeriesEnded?.Invoke(hero, reason, killer);

        IEnumerator Respawn(Hookable who)
        {
            yield return new WaitForSeconds(respawnDelay);
            if (who == null) yield break; // left the game meanwhile
            var facing = who.team == Team.Bots ? Vector3.back : Vector3.forward;
            who.Respawn(RandomPointOnSide(who.team, 1.5f), Quaternion.LookRotation(facing));
        }
    }
}
