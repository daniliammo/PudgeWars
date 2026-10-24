using System;
using System.Collections.Generic;

namespace PudgeWars.Gameplay
{
    public enum KillAnnouncement { DoubleKill, TripleKill, UltraKill, Rampage, Godlike }

    /// <summary>
    /// Kill series per hero, as in Dota 2. Multi-kills (Double → Triple → Ultra → Rampage): each kill must come
    /// within <see cref="multiKillWindow"/> of the previous one, otherwise the count starts over; after Rampage
    /// every further kill in the window is Rampage again. Godlike is a kill streak: <see cref="godlikeStreak"/>
    /// kills without dying (time does not matter), announced again for each kill beyond it that is not a multi-kill.
    /// </summary>
    public class KillStreaks
    {
        public float multiKillWindow = 18f;
        public int godlikeStreak = 9;

        class State
        {
            public int multi, streak;
            public float lastKillTime;
        }

        readonly Dictionary<object, State> states = new();

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
            if (s.multi >= 2) result.Add((KillAnnouncement)Math.Min(s.multi - 2, (int)KillAnnouncement.Rampage));
            if (s.streak == godlikeStreak || (s.streak > godlikeStreak && s.multi < 2)) result.Add(KillAnnouncement.Godlike);
            return result;
        }

        /// <summary>A death ends the kill streak; multi-kills run out only by time.</summary>
        public void RegisterDeath(object hero) => Of(hero).streak = 0;
    }
}
