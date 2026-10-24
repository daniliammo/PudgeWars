using System;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>Hook sounds as wired in the original Player_PW Blueprint.</summary>
    [Serializable]
    public class HookSounds
    {
        [Tooltip("Hook_Chain: Sounds/Chain/Hook")] public AudioClip cast;
        [Tooltip("Hook_ChainReturn: Sounds/Chain/ReturnHook")] public AudioClip retract;
        [Tooltip("BOTPopadanieSound: Sounds/Popal/PopalSound")] public AudioClip impact;
        [Tooltip("Popal (SoundCue Hook_Popodanie, random): Pud_1, Pud_2, Pud_3, TargetHook")] public AudioClip[] hitVoice = Array.Empty<AudioClip>();
        [Tooltip("NePopal (SoundCue Returne_Sound, random): Pud_anger, Pud_1")] public AudioClip[] missVoice = Array.Empty<AudioClip>();
    }

    /// <summary>
    /// Pudge's hook: turn to the target (Dota 2 facing rule), wind-up, flight to max range, catch the first
    /// enemy on the way and pull it back. A target dragged over the pit is released and falls in.
    /// </summary>
    public class HookAbility : MonoBehaviour
    {
        enum State { Ready, Turning, Windup, Flying, Returning }

        [Header("Defaults of the original Player_PW Blueprint (cm -> m)")]
        [Tooltip("DistanceHook = 1300")] public float range = 13f;
        [Tooltip("HookStartSpeed = 1500")] public float flySpeed = 15f;
        [Tooltip("SpeedHookPudge = 1200 (multiplayer Player_Pudge)")] public float pullSpeed = 12f;
        public float returnSpeed = 15f;
        [Tooltip("StartHook_DelayAnimations = 0.29 s")] public float castDelay = 0.29f;
        public float cooldown = 1f;
        [Tooltip("A cast that catches a target leaves the hook ready right after it comes back.")]
        public bool resetCooldownOnHit = true;
        [Tooltip("Hook head radius used for hits, metres.")] public float hitRadius = 0.35f;
        [Tooltip("Share of the pull speed a released target keeps while falling.")] public float dropCarry = 0.35f;

        [Header("References (the head and the chain are unparented at runtime)")]
        public Transform hookHead;
        public LineRenderer chain;
        [Tooltip("Where the chain starts, e.g. the right hand bone.")] public Transform chainOrigin;
        public Vector3 fallbackOriginOffset = new(0.4f, 1.1f, 0.5f);
        public AudioSource audioSource;
        public HookSounds sounds = new();

        public event Action<Hookable> TargetCaught;
        public event Action<Hookable> TargetDropped;

        /// <summary>Range including item bonuses (Aether Lens).</summary>
        public float EffectiveRange => range + (inventory != null ? inventory.CastRangeBonus : 0f);
        public bool IsBusy => state != State.Ready;
        public bool IsReady => state == State.Ready && Time.time >= readyAt;
        public float CooldownRemaining => state == State.Ready ? Mathf.Max(0f, readyAt - Time.time) : cooldown;

        State state;
        float timer, traveled, readyAt;
        bool caughtThisCast;
        Vector3 direction;
        Hookable target, self;
        PudgeAnimation anim;
        PudgeMotor motor;
        HeroInventory inventory;

        void Awake()
        {
            self = GetComponent<Hookable>();
            anim = GetComponent<PudgeAnimation>();
            motor = GetComponent<PudgeMotor>();
            inventory = GetComponent<HeroInventory>();
            // the prefab keeps the head and the chain as children; in flight they must not follow the owner
            if (hookHead != null) hookHead.SetParent(null, true);
            if (chain != null) chain.transform.SetParent(null, true);
            SetHookVisible(false);
        }

        void OnDestroy()
        {
            if (hookHead != null) Destroy(hookHead.gameObject);
            if (chain != null) Destroy(chain.gameObject);
        }

        /// <summary>True for the hook head, the chain and their children.</summary>
        public bool Owns(Transform t) =>
            (hookHead != null && t.IsChildOf(hookHead)) || (chain != null && t.IsChildOf(chain.transform));

        Vector3 Origin => chainOrigin != null ? chainOrigin.position : transform.TransformPoint(fallbackOriginOffset);

        /// <summary>Throws the hook toward a world point (only the horizontal direction matters).</summary>
        public bool TryCast(Vector3 worldPoint)
        {
            if (!IsReady || (self != null && !self.IsControllable)) return false;
            var dir = worldPoint - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
            direction = dir.normalized;
            state = State.Turning;
            caughtThisCast = false;
            return true;
        }

        void BeginWindup()
        {
            anim?.PlayAction(PudgeAnimation.HookStart, 10f, 0.05f);
            state = State.Windup;
            timer = castDelay;
        }

        void Update()
        {
            switch (state)
            {
                case State.Turning:
                    // Dota 2: a point-target cast starts once the target is within 11.5° in front
                    float left = motor != null ? motor.TurnToward(direction, Time.deltaTime) : 0f;
                    if (left <= PudgeMotor.FacingTolerance) BeginWindup();
                    break;
                case State.Windup:
                    timer -= Time.deltaTime;
                    if (timer <= 0f) Launch();
                    break;
                case State.Flying:
                    Fly();
                    break;
                case State.Returning:
                    Return();
                    break;
            }
            if (state is State.Flying or State.Returning) UpdateChain();
        }

        void Launch()
        {
            var start = Origin;
            hookHead.SetPositionAndRotation(new Vector3(start.x, start.y, start.z), Quaternion.LookRotation(direction));
            traveled = 0f;
            state = State.Flying;
            SetHookVisible(true);
            Play(sounds.cast);
        }

        void Fly()
        {
            float step = Mathf.Min(flySpeed * Time.deltaTime, EffectiveRange - traveled);
            var from = hookHead.position;
            hookHead.position += direction * step;
            traveled += step;
            // swept test over this frame's path: a long frame must not let the hook pass through a target
            var hit = FindTarget(from, hookHead.position, out var contact);
            if (hit != null)
            {
                hookHead.position = contact;
                target = hit;
                caughtThisCast = true;
                target.AttachTo(this);
                TargetCaught?.Invoke(target);
                Play(sounds.impact);
                Play(Pick(sounds.hitVoice));
                state = State.Returning;
            }
            else if (traveled >= EffectiveRange)
            {
                Play(Pick(sounds.missVoice));
                state = State.Returning;
            }
            if (state == State.Returning) Play(sounds.retract);
        }

        void Return()
        {
            var origin = Origin;
            var toOrigin = origin - hookHead.position;
            float dist = toOrigin.magnitude;
            float step = (target != null ? pullSpeed : returnSpeed) * Time.deltaTime;
            var flatDir = new Vector3(toOrigin.x, 0f, toOrigin.z).normalized;
            if (dist <= step + 0.05f)
            {
                Finish();
                return;
            }
            hookHead.position += toOrigin / dist * step;
            if (flatDir.sqrMagnitude > 0f) hookHead.rotation = Quaternion.LookRotation(-flatDir);

            if (target == null) return;
            target.DragTo(hookHead.position, flatDir);
            var arena = Arena.Instance;
            if (arena != null && arena.IsOverPit(target.transform.position))
            {
                var dropped = target;
                target = null;
                dropped.StartFalling(flatDir * pullSpeed * dropCarry);
                TargetDropped?.Invoke(dropped);
            }
        }

        void Finish()
        {
            if (target != null)
            {
                // Pulled all the way without crossing the pit (not possible on the test arena): just let go.
                target.StartFalling(Vector3.zero);
                target = null;
            }
            SetHookVisible(false);
            state = State.Ready;
            readyAt = Time.time + (caughtThisCast && resetCooldownOnHit ? 0f : cooldown);
            anim?.PlayAction(PudgeAnimation.HookEnd, 0.5f, 0.1f);
        }

        /// <summary>
        /// Breaks the hook off at once (its owner was caught or is falling): the caught target is let go
        /// and the normal cooldown applies.
        /// </summary>
        public void Cancel()
        {
            if (state == State.Ready) return;
            if (target != null)
            {
                target.Release();
                target = null;
            }
            SetHookVisible(false);
            state = State.Ready;
            readyAt = Time.time + cooldown;
        }

        /// <summary>First enemy whose radius the hook head touches on the segment from → to (horizontal plane).</summary>
        Hookable FindTarget(Vector3 from, Vector3 to, out Vector3 contact)
        {
            Hookable best = null;
            float bestT = float.MaxValue;
            contact = to;
            var seg = to - from;
            seg.y = 0f;
            float len2 = seg.sqrMagnitude;
            foreach (var h in Hookable.All)
            {
                if (h == self || !h.CanBeHooked || (self != null && h.team == self.team)) continue;
                var rel = h.transform.position - from;
                rel.y = 0f;
                float t = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(rel, seg) / len2) : 0f;
                float r = hitRadius + h.radius;
                if ((rel - seg * t).sqrMagnitude <= r * r && t < bestT)
                {
                    bestT = t;
                    best = h;
                }
            }
            if (best != null) contact = from + (to - from) * bestT;
            return best;
        }

        void UpdateChain()
        {
            if (chain == null) return;
            chain.SetPosition(0, Origin);
            chain.SetPosition(1, hookHead.position);
        }

        void SetHookVisible(bool visible)
        {
            if (hookHead != null) hookHead.gameObject.SetActive(visible);
            if (chain != null)
            {
                chain.enabled = visible;
                chain.positionCount = 2;
            }
        }

        void Play(AudioClip clip)
        {
            if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
        }

        static AudioClip Pick(AudioClip[] clips) =>
            clips == null || clips.Length == 0 ? null : clips[UnityEngine.Random.Range(0, clips.Length)];
    }
}
