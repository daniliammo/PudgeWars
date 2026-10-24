using System;
using System.Collections;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    public enum Team { Player, Bots }

    /// <summary>
    /// Test arena: two sides separated by a pit along the X axis (z = 0).
    /// The player side is z &lt; 0, the bot side is z &gt; 0. Keeps the score and respawns the dead.
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
        [Tooltip("Multi-kills: the next kill must come within this time of the previous one, seconds (18 in Dota 2).")]
        public float multiKillWindow = 18f;
        [Tooltip("Kills without dying for Godlike (9 in Dota 2).")]
        public int godlikeStreak = 9;

        /// <summary>Bots killed by the player.</summary>
        public int Score { get; private set; }
        /// <summary>Times the player was killed.</summary>
        public int Deaths { get; private set; }
        public event Action<int, int> ScoreChanged;
        /// <summary>First Blood, Double / Triple / Ultra Kill, Rampage, Godlike made by the hero.</summary>
        public event Action<Hookable, KillAnnouncement> KillAnnounced;

        public KillStreaks Streaks { get; private set; }

        void Awake()
        {
            Instance = this;
            Streaks = new KillStreaks { multiKillWindow = multiKillWindow, godlikeStreak = godlikeStreak };
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool IsOverPit(Vector3 p) => Mathf.Abs(p.z) < pitHalfDepth && Mathf.Abs(p.x) < halfWidth;

        public Vector3 ClampToSide(Vector3 p, Team team, float radius)
        {
            p.x = Mathf.Clamp(p.x, -halfWidth + radius, halfWidth - radius);
            float near = pitHalfDepth + radius, far = pitHalfDepth + sideDepth - radius;
            p.z = team == Team.Player ? Mathf.Clamp(p.z, -far, -near) : Mathf.Clamp(p.z, near, far);
            return p;
        }

        public Vector3 RandomPointOnSide(Team team, float margin)
        {
            float x = UnityEngine.Random.Range(-halfWidth + margin, halfWidth - margin);
            float z = UnityEngine.Random.Range(pitHalfDepth + margin, pitHalfDepth + sideDepth - margin);
            return new Vector3(x, 0f, team == Team.Player ? -z : z);
        }

        public void ReportDeath(Hookable victim, Hookable killer)
        {
            Streaks.RegisterDeath(victim);
            if (killer != null && killer.team != victim.team)
            {
                if (killer.TryGetComponent<HeroInventory>(out var wallet)) wallet.AddGold(killGold);
                foreach (var announcement in Streaks.RegisterKill(killer, Time.time))
                    KillAnnounced?.Invoke(killer, announcement);
            }
            if (victim.team == Team.Bots) Score++;
            else Deaths++;
            ScoreChanged?.Invoke(Score, Deaths);
            StartCoroutine(Respawn(victim));
        }

        IEnumerator Respawn(Hookable who)
        {
            yield return new WaitForSeconds(respawnDelay);
            var facing = who.team == Team.Bots ? Vector3.back : Vector3.forward;
            who.Respawn(RandomPointOnSide(who.team, 1.5f), Quaternion.LookRotation(facing));
        }
    }
}
