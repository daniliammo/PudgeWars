using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Announcer of kill series at the top of the screen, as in Dota: "FIRST BLOOD", "DOUBLE KILL", "TRIPLE KILL", "ULTRA KILL",
    /// "RAMPAGE", "БОГОПОДОБИЕ" pop in, hold and dissolve one after another (announcements coming together
    /// wait in a queue). The player's series are gold, the enemies' red with the hero's name below.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class KillAnnouncer : MonoBehaviour
    {
        public Font font;
        public int fontSize = 84;
        public int nameFontSize = 36;
        [Tooltip("Indexed by KillAnnouncement.")]
        public string[] texts = { "FIRST BLOOD", "DOUBLE KILL", "TRIPLE KILL", "ULTRA KILL", "RAMPAGE", "БОГОПОДОБИЕ" };
        [Tooltip("Announcer voice, indexed by KillAnnouncement; empty slots are silent.")]
        public AudioClip[] clips = new AudioClip[6];
        public Color playerColor = new(1f, 0.78f, 0.2f);
        public Color enemyColor = new(1f, 0.3f, 0.22f);
        public float duration = 2.2f;
        [Tooltip("Share of the duration after which the text starts to dissolve.")] [Range(0f, 1f)] public float fadeFrom = 0.65f;

        readonly Queue<(Hookable hero, KillAnnouncement kind)> queue = new();
        Arena arena;
        RectTransform root;
        Text label, heroName;
        AudioSource voice;
        float age;

        public bool IsShowing => root != null && root.gameObject.activeSelf;
        public int Queued => queue.Count;
        public string LastText { get; private set; }

        void Awake()
        {
            root = new GameObject("Announcement", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(transform, false);
            root.sizeDelta = Vector2.zero;
            label = MakeText("Text", fontSize, Vector2.zero);
            heroName = MakeText("Hero", nameFontSize, new Vector2(0f, -fontSize * 0.9f));
            root.gameObject.SetActive(false);
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
        }

        void Start()
        {
            arena = Arena.Instance;
            if (arena != null) arena.KillAnnounced += Enqueue;
        }

        void OnDestroy()
        {
            if (arena != null) arena.KillAnnounced -= Enqueue;
        }

        public void Enqueue(Hookable hero, KillAnnouncement kind)
        {
            queue.Enqueue((hero, kind));
            if (!IsShowing) ShowNext();
        }

        void ShowNext()
        {
            var (hero, kind) = queue.Dequeue();
            bool own = hero == null || hero.team == Team.Player;
            label.text = LastText = texts[(int)kind];
            label.color = own ? playerColor : enemyColor;
            heroName.text = own ? "" : hero.name;
            heroName.color = label.color;
            age = 0f;
            root.gameObject.SetActive(true);
            Animate();
            var clip = (int)kind < clips.Length ? clips[(int)kind] : null;
            if (clip != null) voice.PlayOneShot(clip);
        }

        void Update()
        {
            if (!IsShowing) return;
            age += Time.deltaTime;
            if (age < duration)
            {
                Animate();
                return;
            }
            root.gameObject.SetActive(false);
            if (queue.Count > 0) ShowNext();
        }

        void Animate()
        {
            // slam in from big (overshoot, settle), hold, then dissolve
            root.localScale = Vector3.one * (age < 0.1f
                ? Mathf.Lerp(2.2f, 0.92f, age / 0.1f)
                : Mathf.Lerp(0.92f, 1f, Mathf.SmoothStep(0f, 1f, (age - 0.1f) / 0.15f)));
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeFrom, 1f, age / duration));
            foreach (var t in new[] { label, heroName })
            {
                var c = t.color;
                c.a = alpha;
                t.color = c;
            }
        }

        Text MakeText(string name, int size, Vector2 position)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(root, false);
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(1400f, size * 1.5f);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(3f, -3f);
            return text;
        }
    }
}
