using System;
using System.Collections.Generic;

namespace PudgeWars.Gameplay
{
    public enum KillAnnouncement { FirstBlood, DoubleKill, TripleKill, UltraKill, Rampage, Godlike }

    /// <summary>Why a series ended: no kill in time, or the hero was killed.</summary>
    public enum SeriesEnd { Time, Killed }

    /// <summary>
    /// Kill series per hero, as in Dota 2. First Blood is the first kill of the match. Multi-kills (Double → Triple →
    /// Ultra → Rampage): each kill must come within <see cref="multiKillWindow"/> of the previous one, otherwise the
    /// count starts over; after Rampage every further kill in the window is Rampage again. Godlike is a kill streak: <see cref="godlikeStreak"/>
    /// kills without dying (time does not matter), announced again for each kill beyond it that is not a multi-kill.
    /// A series is a run that got announced; it ends when the hero dies, or when the multi-kill window runs out
    /// (a Godlike streak outlives the window and ends only with death).
    /// </summary>
    public class KillStreaks
    {
        public float multiKillWindow = 18f;
        public int godlikeStreak = 9;

        class State
        {
            public int multi, streak;
            public float lastKillTime;
            public bool series;
        }

        readonly Dictionary<object, State> states = new();
        readonly List<object> expired = new();
        bool firstBloodDone;

        State Of(object hero) => states.TryGetValue(hero, out var s) ? s : states[hero] = new State();

        public int MultiKillCount(object hero) => states.TryGetValue(hero, out var s) ? s.multi : 0;
        public int Streak(object hero) => states.TryGetValue(hero, out var s) ? s.streak : 0;

        /// <summary>Counts a kill made at <paramref name="time"/>; returns what to announce, in order.</summary>
        public List<KillAnnouncement> RegisterKill(object killer, float time)
        {
            var s = Of(killer);
            s.multi = s.multi > 0 && time - s.lastKillTime <= multiKillWindow ? s.multi + 1 : 1;
            s.lastKillTime = time;
            s.streak++;

            var result = new List<KillAnnouncement>();
            if (!firstBloodDone) result.Add(KillAnnouncement.FirstBlood);
            firstBloodDone = true;
            if (s.multi >= 2) result.Add((KillAnnouncement)Math.Min((int)KillAnnouncement.DoubleKill + s.multi - 2, (int)KillAnnouncement.Rampage));
            if (s.streak == godlikeStreak || (s.streak > godlikeStreak && s.multi < 2)) result.Add(KillAnnouncement.Godlike);
            s.series |= s.multi >= 2 || s.streak >= godlikeStreak;
            return result;
        }

        /// <summary>A death ends both the multi-kill and the kill streak; true if it broke a series.</summary>
        public bool RegisterDeath(object hero)
        {
            var s = Of(hero);
            bool broken = s.series;
            s.multi = s.streak = 0;
            s.series = false;
            return broken;
        }

        /// <summary>Heroes whose series ran out of time by <paramref name="time"/> (each reported once).</summary>
        public IReadOnlyList<object> ExpireSeries(float time)
        {
            expired.Clear();
            foreach (var (hero, s) in states)
            {
                if (s.multi == 0 || time - s.lastKillTime <= multiKillWindow) continue;
                s.multi = 0;
                if (!s.series || s.streak >= godlikeStreak) continue;
                s.series = false;
                expired.Add(hero);
            }
            return expired;
        }
    }
}
