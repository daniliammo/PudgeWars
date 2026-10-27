using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Pudge's Dota 2 voice (Russian, Resources/Audio/Voice) for the local player's hero, heard only by that player:
    /// spawn and "battle begins", hook hit and miss, a cast now and then, "not yet" for a hook on cooldown, caught by an
    /// enemy, kills (first blood, big multi-kills laughing), death, respawn, a rescue made ("denied") and received
    /// ("thanks"), a purchase and now and then a move after standing. One line at a time: a more important one cuts a
    /// lesser one off, one that matters no more than the line playing is dropped. Everything is read from the state the heroes
    /// show (score, alive, inventory) and from the hook's events, so a network client hears the same as the host.
    /// </summary>
    public class HeroVoice : MonoBehaviour
    {
        // what the lines are about, as the clip names start (pud_<topic>_NN)
        static readonly string[] SpawnLines = { "pud_spawn" };
        static readonly string[] BattleBeginsLines = { "pud_battlebegins" };
        static readonly string[] RespawnLines = { "pud_respawn" };
        static readonly string[] HookHitLines = { "pud_ability_hook" };
        static readonly string[] HookMissLines = { "pud_ability_hook_miss", "pud_anger" };
        static readonly string[] CastLines = { "pud_cast", "pud_attack" };
        static readonly string[] NotYetLines = { "pud_notyet" };
        static readonly string[] CaughtLines = { "pud_pain", "pud_getoff", "pud_underattack" };
        static readonly string[] KillLines = { "pud_kill", "pud_lasthit", "pud_nomnom", "pud_ability_devour", "pud_ability_heap" };
        static readonly string[] RareKillLines = { "pud_rare" };
        static readonly string[] FirstBloodLines = { "pud_firstblood" };
        static readonly string[] BigStreakLines = { "pud_laugh", "pud_happy", "pud_inthebag" };
        static readonly string[] DeathLines = { "pud_death" };
        static readonly string[] RescueLines = { "pud_deny" };
        static readonly string[] RescuedLines = { "pud_thanks" };
        static readonly string[] PurchaseLines = { "pud_purch" };
        static readonly string[] MoveLines = { "pud_move", "pud_acknow" };

        [Range(0f, 1f)] public float volume = 0.9f;
        [Tooltip("Chance of a line when the hook is thrown.")]
        [Range(0f, 1f)] public float castChance = 0.2f;
        [Tooltip("Chance of a rare kill line instead of a usual one.")]
        [Range(0f, 1f)] public float rareKillChance = 0.15f;
        [Tooltip("Chance of a line when the hero starts moving after standing.")]
        [Range(0f, 1f)] public float moveChance = 0.25f;
        [Tooltip("Standing this long before moving may bring a move line, seconds.")]
        public float moveIdle = 3f;
        [Tooltip("Lines of the minor kinds (cast, move, not yet) are this far apart at least, seconds.")]
        public float minorGap = 6f;
        [Tooltip("\"Battle begins\" comes this long after the hero first appears in a match, seconds.")]
        public float battleBeginsDelay = 4f;

        /// <summary>How much a line matters: a higher one cuts a lower one off.</summary>
        enum Weight { Minor, Normal, Major }

        static readonly Dictionary<string, AudioClip[]> topics = new();
        static readonly Dictionary<string, AudioClip> lastPicked = new();

        AudioSource source;
        Weight playingWeight;
        Hookable hero;
        HookAbility hook;
        PlayerInputController input;
        PudgeMotor motor;
        HeroInventory inventory;
        Arena arena; // the arena whose events are listened to
        bool wasAlive, spokeSpawn;
        HeroScore lastScore;
        int lastItems;
        float standingSince, nextMinorAt, battleBeginsAt = -1f;
        KillAnnouncement? announcement; // the last series announcement of this hero, for its next kill line
        float announcementAt = -10f;
        int pendingKills;
        float pendingKillAt;

        /// <summary>The topic of the last line started (for tests); null before any.</summary>
        public string LastTopic { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            if (FindAnyObjectByType<HeroVoice>() != null) return;
            var go = new GameObject("HeroVoice");
            DontDestroyOnLoad(go);
            go.AddComponent<HeroVoice>();
        }

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // the own hero's voice: the same wherever the camera is
            if (topics.Count == 0) LoadClips();
        }

        static void LoadClips()
        {
            var grouped = new Dictionary<string, List<AudioClip>>();
            foreach (var clip in Resources.LoadAll<AudioClip>("Audio/Voice"))
            {
                // pud_ability_hook_miss_03 -> pud_ability_hook_miss
                var name = clip.name;
                int cut = name.LastIndexOf('_');
                var topic = cut > 0 && int.TryParse(name[(cut + 1)..], out _) ? name[..cut] : name;
                if (!grouped.TryGetValue(topic, out var list)) grouped[topic] = list = new List<AudioClip>();
                list.Add(clip);
            }
            foreach (var pair in grouped) topics[pair.Key] = pair.Value.ToArray();
        }

        void OnEnable()
        {
            LocalPlayer.Changed += Follow;
            HookAbility.AnyTargetCaught += OnAnyCaught;
            Follow();
        }

        void OnDisable()
        {
            LocalPlayer.Changed -= Follow;
            HookAbility.AnyTargetCaught -= OnAnyCaught;
            Unfollow();
        }

        /// <summary>Starts listening to the local player's (new) hero.</summary>
        void Follow()
        {
            if (hero == LocalPlayer.Hero) return;
            Unfollow();
            hero = LocalPlayer.Hero;
            if (hero == null) return;
            hook = hero.GetComponent<HookAbility>();
            input = LocalPlayer.Input;
            motor = hero.GetComponent<PudgeMotor>();
            inventory = hero.GetComponent<HeroInventory>();
            if (hook != null)
            {
                hook.TargetCaught += OnCaught;
                hook.Missed += OnMissed;
                hook.Launched += OnLaunched;
            }
            if (input != null) input.AbilityNotReady += OnNotReady;
            ListenTo(Arena.Instance);
            wasAlive = hero.IsAlive;
            lastScore = arena != null ? arena.ScoreOf(hero) : default;
            lastItems = inventory != null ? inventory.Items.Count : 0;
            standingSince = Time.time;
            pendingKills = 0;
            announcement = null;
            // a hero of a new match (or the offline arena): it greets and the battle begins
            spokeSpawn = false;
            battleBeginsAt = Time.time + battleBeginsDelay;
        }

        void Unfollow()
        {
            if (hook != null)
            {
                hook.TargetCaught -= OnCaught;
                hook.Missed -= OnMissed;
                hook.Launched -= OnLaunched;
            }
            if (input != null) input.AbilityNotReady -= OnNotReady;
            ListenTo(null);
            hero = null;
            hook = null;
            input = null;
        }

        void ListenTo(Arena next)
        {
            if (next == arena) return;
            if (arena != null)
            {
                arena.KillAnnounced -= OnKillAnnounced;
                arena.Rescued -= OnRescued;
            }
            arena = next;
            if (arena != null)
            {
                arena.KillAnnounced += OnKillAnnounced;
                arena.Rescued += OnRescued;
                lastScore = hero != null ? arena.ScoreOf(hero) : default;
            }
        }

        void Update()
        {
            if (hero == null) return;
            ListenTo(Arena.Instance); // the hero may have registered before its arena woke up
            if (!spokeSpawn)
            {
                spokeSpawn = true;
                Say(SpawnLines, Weight.Minor); // greetings give way to the fight
            }
            if (battleBeginsAt >= 0f && Time.time >= battleBeginsAt && !source.isPlaying)
            {
                battleBeginsAt = -1f;
                Say(BattleBeginsLines, Weight.Minor);
            }

            if (wasAlive != hero.IsAlive)
            {
                wasAlive = hero.IsAlive;
                if (wasAlive) Say(RespawnLines, Weight.Normal);
                else Say(DeathLines, Weight.Major);
            }
            if (arena != null)
            {
                var score = arena.ScoreOf(hero);
                if (score.kills > lastScore.kills)
                {
                    // the series announcement may come a moment after the score on a network client
                    pendingKills += score.kills - lastScore.kills;
                    pendingKillAt = Time.time + 0.25f;
                }
                if (score.deaths < lastScore.deaths || score.kills < lastScore.kills) pendingKills = 0; // a new match
                lastScore = score;
            }
            if (pendingKills > 0 && Time.time >= pendingKillAt)
            {
                pendingKills = 0;
                SayKill();
            }

            if (inventory != null)
            {
                int items = inventory.Items.Count;
                if (items > lastItems) Say(PurchaseLines, Weight.Normal);
                lastItems = items;
            }

            if (motor != null)
            {
                bool moving = motor.MoveInput.sqrMagnitude > 0.01f;
                if (!moving) { if (standingSince < 0f) standingSince = Time.time; }
                else if (standingSince >= 0f)
                {
                    if (Time.time - standingSince >= moveIdle && hero.IsControllable && Random.value < moveChance) SayMinor(MoveLines);
                    standingSince = -1f;
                }
            }
        }

        void SayKill()
        {
            bool fresh = announcement != null && Time.time - announcementAt < 1.5f;
            var kind = fresh ? announcement : null;
            announcement = null;
            if (kind == KillAnnouncement.FirstBlood) Say(FirstBloodLines, Weight.Major);
            else if (kind is KillAnnouncement.UltraKill or KillAnnouncement.Rampage or KillAnnouncement.Godlike) Say(BigStreakLines, Weight.Major);
            else Say(Random.value < rareKillChance ? RareKillLines : KillLines, Weight.Major);
        }

        void OnKillAnnounced(Hookable killer, KillAnnouncement kind)
        {
            if (killer != hero) return;
            // first blood is a kill's first announcement; a multi-kill one may follow it in the same kill
            if (announcement == null || Time.time - announcementAt > 0.5f || kind != KillAnnouncement.FirstBlood) announcement = kind;
            announcementAt = Time.time;
        }

        void OnRescued(Hookable rescued, Hookable rescuer)
        {
            if (rescuer == hero) Say(RescueLines, Weight.Normal);
            else if (rescued == hero) Say(RescuedLines, Weight.Normal);
        }

        void OnCaught(Hookable target)
        {
            if (target != null && target.team != hero.team) Say(HookHitLines, Weight.Normal);
        }

        void OnAnyCaught(HookAbility by, Hookable target)
        {
            if (hero == null || target != hero || by == hook || by.Owner == null || by.Owner.team == hero.team) return;
            Say(CaughtLines, Weight.Normal);
        }

        void OnMissed() => Say(HookMissLines, Weight.Normal);

        void OnLaunched()
        {
            if (Random.value < castChance) SayMinor(CastLines);
        }

        void OnNotReady() => SayMinor(NotYetLines);

        void SayMinor(string[] lines)
        {
            if (Time.time < nextMinorAt) return;
            if (Say(lines, Weight.Minor)) nextMinorAt = Time.time + minorGap;
        }

        /// <summary>A random line of the topics (clip names without the number, e.g. "pud_laugh"); null if none is imported.</summary>
        public static AudioClip RandomLine(params string[] lines)
        {
            if (topics.Count == 0) LoadClips();
            return Pick(lines, out _);
        }

        /// <summary>Starts a random line of the topics, not the one said last time; false if it could not be said now.</summary>
        bool Say(string[] lines, Weight weight)
        {
            if (source.isPlaying && weight <= playingWeight && weight != Weight.Major) return false;
            var clip = Pick(lines, out var topic);
            if (clip == null) return false;
            source.Stop();
            source.clip = clip;
            source.volume = volume;
            source.Play();
            playingWeight = weight;
            LastTopic = topic;
            return true;
        }

        static AudioClip Pick(string[] lines, out string topic)
        {
            topic = null;
            int count = 0;
            foreach (var t in lines)
                if (topics.TryGetValue(t, out var clips)) count += clips.Length;
            if (count == 0) return null;
            var key = string.Join("|", lines);
            lastPicked.TryGetValue(key, out var last);
            for (int attempt = 0; ; attempt++)
            {
                int i = Random.Range(0, count);
                foreach (var t in lines)
                {
                    if (!topics.TryGetValue(t, out var clips)) continue;
                    if (i < clips.Length)
                    {
                        if (clips[i] == last && count > 1 && attempt < 4) break;
                        topic = t;
                        lastPicked[key] = clips[i];
                        return clips[i];
                    }
                    i -= clips.Length;
                }
            }
        }
    }
}
