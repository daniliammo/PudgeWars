using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// One-shot sounds at a point of the arena (a tree falling, an item eaten): a short-lived source heard around it
    /// and fading with distance, as the heroes' sounds are. Clips come from Resources and are kept once loaded.
    /// </summary>
    public static class WorldAudio
    {
        static readonly System.Collections.Generic.Dictionary<string, AudioClip> clips = new();

        /// <summary>How many sounds were started (for tests).</summary>
        public static int Played { get; private set; }

        /// <summary>The clip at Resources/<paramref name="path"/> (null if there is none).</summary>
        public static AudioClip Clip(string path)
        {
            if (!clips.TryGetValue(path, out var clip) || clip == null) clips[path] = clip = Resources.Load<AudioClip>(path);
            return clip;
        }

        /// <summary>Plays Resources/<paramref name="path"/> at <paramref name="position"/>; false if there is no such clip.</summary>
        public static bool PlayAt(string path, Vector3 position, float volume = 1f) => PlayAt(Clip(path), position, volume);

        public static bool PlayAt(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip == null) return false;
            var go = new GameObject("Sound_" + clip.name);
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.spatialBlend = 0.8f;
            source.minDistance = 8f;
            source.maxDistance = 45f;
            source.Play();
            Object.Destroy(go, clip.length + 0.1f);
            Played++;
            return true;
        }
    }
}
