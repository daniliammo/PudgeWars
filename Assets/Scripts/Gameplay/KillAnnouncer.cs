using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Russian kill-series announcements at the top of the screen, queued and animated in turn. Bot messages
    /// are half size except First Blood, and always show the bot's name, including allied bots. Player-side
    /// series are gold, enemy-side series red. Repeated Rampages carry a count. Series endings explain the
    /// reason, and a rescued local player sees who saved them.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class KillAnnouncer : MonoBehaviour
    {
        public Font font;
        public int fontSize = 84;
        public int nameFontSize = 36;
        [Tooltip("Indexed by KillAnnouncement.")]
        public string[] texts = { "ПЕРВАЯ КРОВЬ", "ДВОЙНОЕ УБИЙСТВО", "ТРОЙНОЕ УБИЙСТВО", "ЧЕТВЕРНОЕ УБИЙСТВО", "БУЙСТВО", "БОГОПОДОБИЕ" };
        [Tooltip("Text of a Rampage after the first one in a row; {0} = Rampage text, {1} = how many in a row.")]
        public string rampageRepeatFormat = "{0} ×{1}";
        [Tooltip("Announcer voice, indexed by KillAnnouncement; empty slots are silent.")]
        public AudioClip[] clips = new AudioClip[6];
        public Color playerColor = new(1f, 0.78f, 0.2f);
        public Color enemyColor = new(1f, 0.3f, 0.22f);
        public string seriesEndedText = "СЕРИЯ ПРЕРВАНА";
        public Color seriesEndedColor = new(0.82f, 0.86f, 0.92f);
        [Tooltip("Reasons under \"СЕРИЯ ПРЕРВАНА\" for the player's series; {0} = the killer.")]
        public string ownTimeOut = "Время вышло", ownKilled = "Тебя сбил {0}", ownDied = "Ты сорвался в яму";
        [Tooltip("Reasons for an enemy's series; {0} = the enemy.")]
        public string enemyTimeOut = "{0}: время вышло", enemyKilled = "Ты сбил {0}", enemyDied = "{0} сорвался в яму";
        public string rescuedText = "СПАСЁН";
        [Tooltip("Under \"СПАСЁН\"; {0} = the ally who pulled the player out.")]
        public string rescuedBy = "Тебя спас {0}";
        public Color rescuedColor = new(0.4f, 0.95f, 0.45f);
        public float duration = 2.2f;
        [Tooltip("Share of the duration after which the text starts to dissolve.")] [Range(0f, 1f)] public float fadeFrom = 0.65f;

        struct Message
        {
            public string text, subtitle;
            public Color color;
            public AudioClip clip;
            public bool compact;
        }

        readonly Queue<Message> queue = new();
        Arena arena;
        RectTransform root;
        Text label, heroName;
        AudioSource voice;
        float age, messageScale = 1f;

        public float MessageScale => messageScale;

        public bool IsShowing => root != null && root.gameObject.activeSelf;
        public int Queued => queue.Count;
        public string LastText { get; private set; }
        public string LastSubtitle { get; private set; }

        void Awake()
        {
            // Existing scenes serialize the old English labels: translate them without rebuilding the scene.
            for (int i = 0; i < texts.Length; i++)
                texts[i] = texts[i] switch
                {
                    "FIRST BLOOD" => "ПЕРВАЯ КРОВЬ",
                    "DOUBLE KILL" => "ДВОЙНОЕ УБИЙСТВО",
                    "TRIPLE KILL" => "ТРОЙНОЕ УБИЙСТВО",
                    "ULTRA KILL" => "ЧЕТВЕРНОЕ УБИЙСТВО",
                    "RAMPAGE" => "БУЙСТВО",
                    "GODLIKE" => "БОГОПОДОБИЕ",
                    _ => texts[i],
                };
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
            if (arena == null) return;
            arena.KillAnnounced += Enqueue;
            arena.SeriesEnded += EnqueueSeriesEnd;
            arena.Rescued += EnqueueRescue;
        }

        void OnDestroy()
        {
            if (arena == null) return;
            arena.KillAnnounced -= Enqueue;
            arena.SeriesEnded -= EnqueueSeriesEnd;
            arena.Rescued -= EnqueueRescue;
        }

        public void Enqueue(Pudge hero, KillAnnouncement kind)
        {
            bool own = hero == null || hero.team == LocalPlayer.Team;
            int rampages = kind == KillAnnouncement.Rampage && hero != null && arena != null ? arena.RampageCount(hero) : 0;
            Enqueue(new Message
            {
                text = rampages > 1 ? string.Format(rampageRepeatFormat, texts[(int)kind], rampages) : texts[(int)kind],
                subtitle = hero != null && (hero.IsBot || !own) ? hero.DisplayName : "",
                compact = hero != null && hero.IsBot && kind != KillAnnouncement.FirstBlood,
                color = own ? playerColor : enemyColor,
                clip = (int)kind < clips.Length ? clips[(int)kind] : null,
            });
        }

        public void EnqueueSeriesEnd(Pudge hero, SeriesEnd reason, Pudge killer)
        {
            bool own = hero.team == LocalPlayer.Team;
            string format = reason == SeriesEnd.Time ? (own ? ownTimeOut : enemyTimeOut)
                : killer == null ? (own ? ownDied : enemyDied)
                : own ? ownKilled : enemyKilled;
            string subtitle = string.Format(format, own ? killer?.DisplayName : hero.DisplayName);
            if (hero.IsBot)
                subtitle = reason == SeriesEnd.Time ? string.Format(enemyTimeOut, hero.DisplayName)
                    : killer == null ? string.Format(enemyDied, hero.DisplayName)
                    : $"{hero.DisplayName}: серию прервал {killer.DisplayName}";
            Enqueue(new Message
            {
                text = seriesEndedText,
                subtitle = subtitle,
                compact = hero.IsBot,
                color = seriesEndedColor,
            });
        }

        /// <summary>Shown only to the rescued player.</summary>
        public void EnqueueRescue(Pudge rescued, Pudge rescuer)
        {
            if (rescued == null || rescued != LocalPlayer.Hero) return;
            Enqueue(new Message
            {
                text = rescuedText,
                subtitle = string.Format(rescuedBy, rescuer != null ? rescuer.DisplayName : "союзник"),
                color = rescuedColor,
            });
        }

        void Enqueue(Message message)
        {
            queue.Enqueue(message);
            if (!IsShowing) ShowNext();
        }

        void ShowNext()
        {
            var m = queue.Dequeue();
            label.text = LastText = m.text;
            heroName.text = LastSubtitle = m.subtitle;
            label.color = heroName.color = m.color;
            messageScale = m.compact ? 0.5f : 1f;
            age = 0f;
            root.gameObject.SetActive(true);
            Animate();
            if (m.clip != null) voice.PlayOneShot(m.clip);
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
            root.localScale = Vector3.one * messageScale * (age < 0.1f
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
