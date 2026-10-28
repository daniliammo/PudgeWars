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

    /// <summary>A hero's own score: kills, deaths and rescues (У/С/С as in the corner of the HUD), and how many times
    /// allies' hooks rescued it.</summary>
    public struct HeroScore
    {
        public int kills, deaths, rescues, rescued;
    }

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
        [Tooltip("Seconds from death to respawn: the 3rd second of the revive sound, which starts at the death (CameraFeedback.reviveLead).")]
        public float respawnDelay = 3f;
        [Tooltip("Kill reward for the hero whose hook dragged the victim into the pit: killGoldBase + killGoldPerLevel × the victim's level (133 at level 1). A rescue pays half of it.")]
        public int killGoldBase = 125;
        [Tooltip("Kill reward added per level of the victim.")]
        public int killGoldPerLevel = 8;
        [Tooltip("Breaking a kill series pays extra: half the victim's kill reward + this much per kill of its streak.")]
        public int streakBreakPerKill = 5;
        [Tooltip("Per Rampage kill instead, if the victim had Rampages in a row (RAMPAGE ×2 and on); the kills before still pay streakBreakPerKill.")]
        public int streakBreakPerKillRampage = 10;
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
        /// <summary>Allies a team's hooks pulled out of enemy hooks.</summary>
        public int RescuesOf(Team team) => rescues[(int)team];
        /// <summary>A hero's own kills, deaths, rescues and times rescued, as the server counted them on a client.</summary>
        public HeroScore ScoreOf(Pudge hero) => hero != null && heroScores.TryGetValue(hero, out var s) ? s : default;
        /// <summary>Gold for killing a hero: 125 + its level × 8, so 133 at level 1.</summary>
        public int KillGold(Pudge victim) => killGoldBase + killGoldPerLevel * (victim != null ? victim.Level : 1);
        /// <summary>
        /// Extra gold for killing a hero on a kill series: half its kill reward + its kills without dying × 5; if it had
        /// Rampages in a row (RAMPAGE ×2 and on), each Rampage kill pays × 10 instead (the kills before them stay × 5).
        /// </summary>
        public int StreakBreakBonus(Pudge victim, int streak, int rampagesInARow)
        {
            int rampageKills = rampagesInARow >= 2 ? Mathf.Min(rampagesInARow, streak) : 0;
            return KillGold(victim) / 2 + (streak - rampageKills) * streakBreakPerKill + rampageKills * streakBreakPerKillRampage;
        }
        /// <summary>(Score, Deaths) after any change of the counts.</summary>
        public event Action<int, int> ScoreChanged;
        /// <summary>First Blood, Double / Triple / Ultra Kill, Rampage, Godlike made by the hero.</summary>
        public event Action<Pudge, KillAnnouncement> KillAnnounced;
        /// <summary>The hero's series is over: by time (killer is null) or because the killer got him.</summary>
        public event Action<Pudge, SeriesEnd, Pudge> SeriesEnded;
        /// <summary>The first hero was pulled out of an enemy's hook by the second one's hook.</summary>
        public event Action<Pudge, Pudge> Rescued;

        public KillStreaks Streaks { get; private set; }
        /// <summary>Seconds since the match began (the arena loaded, or the network game started; on a client, the server's).</summary>
        public float MatchTime => Time.time - matchStartedAt;

        readonly int[] kills = new int[2], deaths = new int[2], rescues = new int[2];
        readonly Dictionary<Pudge, int> remoteRampages = new();
        readonly Dictionary<Pudge, int> remoteMultiKills = new();
        readonly Dictionary<Pudge, HeroScore> heroScores = new();
        float nextPassiveGold;
        float matchStartedAt;

        void Awake()
        {
            Instance = this;
            Streaks = new KillStreaks { multiKillWindow = multiKillWindow, godlikeStreak = godlikeStreak };
            nextPassiveGold = Time.time + passiveGoldInterval;
            matchStartedAt = Time.time;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (Simulation.IsProxy) return;
            foreach (var hero in Streaks.ExpireSeries(Time.time))
                SeriesEnded?.Invoke((Pudge)hero, SeriesEnd.Time, null);

            for (; Time.time >= nextPassiveGold; nextPassiveGold += passiveGoldInterval)
                foreach (var hero in Pudge.All)
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

        /// <summary>A random point on a team's side where a hero fits between the obstacles (trees) and the other heroes.</summary>
        /// <param name="pitMargin">Distance kept from the pit edge; <paramref name="margin"/> if negative.</param>
        public Vector3 RandomPointOnSide(Team team, float margin, float pitMargin = -1f)
        {
            if (pitMargin < 0f) pitMargin = margin;
            const float heroClearance = 0.8f; // a hero's radius and a little room
            for (int attempt = 0; ; attempt++)
            {
                float x = UnityEngine.Random.Range(-halfWidth + margin, halfWidth - margin);
                float z = UnityEngine.Random.Range(pitHalfDepth + pitMargin, pitHalfDepth + sideDepth - margin);
                var p = new Vector3(x, 0f, team == Team.Player ? -z : z);
                if (attempt >= 30 || (!Obstacle.Blocks(p, heroClearance) && !HeroAt(p, heroClearance))) return p;
            }
        }

        /// <summary>True if a hero of <paramref name="radius"/> at <paramref name="p"/> would overlap a living hero.</summary>
        static bool HeroAt(Vector3 p, float radius)
        {
            foreach (var h in Pudge.All)
            {
                if (!h.IsAlive || h.IsFalling) continue;
                var d = h.transform.position - p;
                d.y = 0f;
                float r = h.radius + radius;
                if (d.sqrMagnitude < r * r) return true;
            }
            return false;
        }

        public void ReportDeath(Pudge victim, Pudge killer)
        {
            if (killer != null && killer.team == victim.team) killer = null;
            int streak = Streaks.Streak(victim), rampages = Streaks.RampageCount(victim); // before the death clears them
            bool seriesBroken = Streaks.RegisterDeath(victim);
            if (seriesBroken)
                SeriesEnded?.Invoke(victim, SeriesEnd.Killed, killer);
            if (killer != null)
            {
                // the bonus for breaking a series comes as its own "+N", after the kill reward
                if (killer.TryGetComponent<HeroInventory>(out var wallet))
                {
                    wallet.AddGold(KillGold(victim));
                    if (seriesBroken) wallet.AddGold(StreakBreakBonus(victim, streak, rampages));
                }
                // Ring of Basilius: a share of the mana the victim had as it died
                if (killer.TryGetComponent<HeroInventory>(out var items) && items.KillManaSteal > 0f)
                    killer.RestoreMana(victim.ManaAtDeath * items.KillManaSteal);
                foreach (var announcement in Streaks.RegisterKill(killer, Time.time))
                    KillAnnounced?.Invoke(killer, announcement);
            }
            deaths[(int)victim.team]++;
            var victimScore = ScoreOf(victim);
            victimScore.deaths++;
            heroScores[victim] = victimScore;
            if (killer != null) // a hero that fell in by itself is nobody's kill
            {
                kills[(int)killer.team]++;
                var killerScore = ScoreOf(killer);
                killerScore.kills++;
                heroScores[killer] = killerScore;
            }
            ScoreChanged?.Invoke(Score, Deaths);
            StartCoroutine(Respawn(victim));
        }

        /// <summary>
        /// An ally's hook took <paramref name="rescued"/> out of an enemy's hook: the rescuer gets half the reward for
        /// killing the rescued hero.
        /// </summary>
        public void ReportRescue(Pudge rescued, Pudge rescuer)
        {
            rescues[(int)rescuer.team]++;
            var rescuerScore = ScoreOf(rescuer);
            rescuerScore.rescues++;
            heroScores[rescuer] = rescuerScore;
            var rescuedScore = ScoreOf(rescued);
            rescuedScore.rescued++;
            heroScores[rescued] = rescuedScore;
            if (rescuer.TryGetComponent<HeroInventory>(out var wallet)) wallet.AddGold(KillGold(rescued) / 2);
            Rescued?.Invoke(rescued, rescuer);
            ScoreChanged?.Invoke(Score, Deaths);
        }

        /// <summary>A network game starts here: the offline score, series and pending respawns are dropped.</summary>
        public void ResetMatch()
        {
            StopAllCoroutines();
            Array.Clear(kills, 0, kills.Length);
            Array.Clear(deaths, 0, deaths.Length);
            Array.Clear(rescues, 0, rescues.Length);
            remoteRampages.Clear();
            remoteMultiKills.Clear();
            heroScores.Clear();
            Streaks = new KillStreaks { multiKillWindow = multiKillWindow, godlikeStreak = godlikeStreak };
            matchStartedAt = Time.time;
            ScoreChanged?.Invoke(Score, Deaths);
        }

        /// <summary>Network client: the match has gone on this long on the server.</summary>
        public void ApplyRemoteMatchTime(float elapsed) => matchStartedAt = Time.time - elapsed;

        /// <summary>Rampages in a row of the hero's current multi-kill (as the server counted them on a client).</summary>
        public int RampageCount(Pudge hero) =>
            remoteRampages.TryGetValue(hero, out var n) ? n : Streaks.RampageCount(hero);

        /// <summary>Kills of the hero's current multi-kill, 0 once its window ran out (as the server counted them on a client).</summary>
        public int MultiKillOf(Pudge hero) =>
            remoteMultiKills.TryGetValue(hero, out var n) ? n : Streaks.MultiKillCount(hero);

        /// <summary>Network client: a hero's kills, deaths and rescues the server keeps.</summary>
        public void ApplyRemoteHeroScore(Pudge hero, HeroScore score) => heroScores[hero] = score;

        /// <summary>Network client: the hero's multi-kill count the server keeps.</summary>
        public void ApplyRemoteMultiKill(Pudge hero, int kills) => remoteMultiKills[hero] = kills;

        /// <summary>Network client: the score the server keeps.</summary>
        public void ApplyRemoteScore(int playerKills, int botsKills, int playerDeaths, int botsDeaths, int playerRescues = 0, int botsRescues = 0)
        {
            rescues[(int)Team.Player] = playerRescues;
            rescues[(int)Team.Bots] = botsRescues;
            kills[(int)Team.Player] = playerKills;
            kills[(int)Team.Bots] = botsKills;
            deaths[(int)Team.Player] = playerDeaths;
            deaths[(int)Team.Bots] = botsDeaths;
            ScoreChanged?.Invoke(Score, Deaths);
        }

        /// <summary>Network client: an announcement the server made.</summary>
        public void ApplyRemoteKill(Pudge killer, KillAnnouncement kind, int rampages)
        {
            if (killer != null) remoteRampages[killer] = rampages;
            KillAnnounced?.Invoke(killer, kind);
        }

        /// <summary>Network client: a rescue the server saw (the count comes with the score).</summary>
        public void ApplyRemoteRescue(Pudge rescued, Pudge rescuer) => Rescued?.Invoke(rescued, rescuer);

        /// <summary>Network client: a series the server saw end.</summary>
        public void ApplyRemoteSeriesEnd(Pudge hero, SeriesEnd reason, Pudge killer) =>
            SeriesEnded?.Invoke(hero, reason, killer);

        IEnumerator Respawn(Pudge who)
        {
            yield return new WaitForSeconds(respawnDelay);
            if (who == null) yield break; // left the game meanwhile
            var facing = who.team == Team.Bots ? Vector3.back : Vector3.forward;
            who.Respawn(RandomPointOnSide(who.team, 1.5f), Quaternion.LookRotation(facing));
        }
    }
}
