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
    /// hero on the way and pull it back. A target dragged over the pit is released and falls in. As Meat Hook in Dota,
    /// it catches allies too: an ally is pulled to Pudge unharmed and let go there. An ally an enemy's hook is pulling
    /// is held instead (a tug of war): the enemy pulls at half speed and drags the rescuer after the ally; if the
    /// enemy's hook lets go, the rescuer pulls the ally home (a rescue), if the ally reaches the pit the rescuer's
    /// chain breaks.
    /// On a network client (<see cref="Simulation.IsProxy"/>) the server throws the hook: a cast becomes a
    /// <see cref="CastRequested"/> request, and the head and the chain only show the state the server sends.
    /// </summary>
    public class HookAbility : MonoBehaviour
    {
        enum State { Ready, Turning, Windup, Flying, Returning, Tethered, Holding }

        [Header("Defaults of the original Player_PW Blueprint (cm -> m)")]
        [Tooltip("DistanceHook = 1300; measured from the hero's centre to the hook head, as cast range in Dota.")]
        public float range = 13f;
        [Tooltip("HookStartSpeed = 1500")] public float flySpeed = 15f;
        [Tooltip("SpeedHookPudge = 1200 (multiplayer Player_Pudge)")] public float pullSpeed = 12f;
        public float returnSpeed = 15f;
        [Tooltip("StartHook_DelayAnimations = 0.29 s")] public float castDelay = 0.29f;
        public float cooldown = 1f;
        [Tooltip("Dota's Meat Hook pulls allies too (without harm).")]
        public bool hitsAllies = true;
        [Tooltip("A cast that catches an enemy leaves the hook ready right after it comes back.")]
        public bool resetCooldownOnHit = true;
        [Tooltip("Hook head radius used for hits, metres. Dota 2 Meat Hook: hook_width 100 plus Pudge's hull 24 is about 1.24 m from the hero's centre; with our hero radius 0.6 that is 0.65.")]
        public float hitRadius = 0.65f;
        [Tooltip("Size of the hook head model relative to the imported one (in Dota the head is large).")]
        public float headScale = 1.8f;
        [Tooltip("The head spins around its flight direction while flying out, degrees per second.")]
        public float spinSpeed = 900f;
        [Tooltip("The head flies straight out of the hero's centre line at this height, metres (about the hand).")]
        public float flightHeight = 1.1f;
        [Tooltip("Share of the pull speed a released target keeps while falling.")] public float dropCarry = 0.35f;
        [Header("Tether: the owner is caught while pulling a target")]
        [Tooltip("How fast a falling/dead owner reels the chain in, dragging the target after it, m/s.")]
        public float tetherReelSpeed = 8f;
        [Tooltip("Safety limit: the target is let go after this many seconds on a tether.")]
        public float tetherTimeout = 4f;

        [Header("Tug of war: an ally's hook holds the target")]
        [Tooltip("Share of the pull speed left while an ally of the target holds it on its own hook.")]
        [Range(0f, 1f)] public float heldPullFactor = 0.5f;

        [Header("References (the head and the chain are unparented at runtime)")]
        public Transform hookHead;
        public LineRenderer chain;
        [Tooltip("Where the chain starts, e.g. the right hand bone.")] public Transform chainOrigin;
        public Vector3 fallbackOriginOffset = new(0.4f, 1.1f, 0.5f);
        public AudioSource audioSource;
        public HookSounds sounds = new();

        public event Action<Hookable> TargetCaught;
        public event Action<Hookable> TargetDropped;
        /// <summary>Network client: the player casts toward this point; the server throws the hook.</summary>
        public event Action<Vector3> CastRequested;

        /// <summary>The hero that throws this hook.</summary>
        public Hookable Owner => self;
        /// <summary>Range including item bonuses (Aether Lens).</summary>
        public float EffectiveRange => range + (inventory != null ? inventory.CastRangeBonus : 0f);
        public bool IsBusy => Simulation.IsProxy ? remoteBusy || Time.time < pendingCastUntil : state != State.Ready;
        public bool IsReady => !IsBusy && Time.time >= readyAt;
        /// <summary>Distance from the head's pivot (where hits and range are measured) to the tip of its model.</summary>
        public float TipLength { get; private set; }
        /// <summary>How far from the hero's centre the tip of the hook reaches.</summary>
        public float AimLength => EffectiveRange + TipLength;
        /// <summary>How far ahead of the hero's centre the head appears at launch (level with the hand).</summary>
        public float LaunchOffset
        {
            get
            {
                var hand = Origin - transform.position;
                return Mathf.Clamp(Vector3.Dot(new Vector3(hand.x, 0f, hand.z), transform.forward), 0f, EffectiveRange);
            }
        }
        /// <summary>Start of the hook's flight line: the hero's centre at flight height.</summary>
        public Vector3 AimStart => transform.position + Vector3.up * flightHeight;
        public float CooldownRemaining => !IsBusy ? Mathf.Max(0f, readyAt - Time.time) : cooldown;
        /// <summary>Time.time when the hook is ready again (meaningful while not busy).</summary>
        public float ReadyAt => readyAt;
        public bool HeadVisible => hookHead != null && hookHead.gameObject.activeSelf;
        public bool IsFlying => state == State.Flying;
        /// <summary>The owner was caught while pulling: the target hangs on a taut chain.</summary>
        public bool IsTethered => state == State.Tethered;
        /// <summary>Where the head points, without the spin.</summary>
        public Vector3 HeadDirection => state == State.Flying ? direction : hookHead != null ? hookHead.forward : transform.forward;

        State state;
        float timer, traveled, readyAt, spin;
        bool caughtThisCast;
        float ropeLength;
        // network client: what the server last sent
        bool remoteBusy, remoteFlying;
        Vector3 remoteHead, remoteDirection;
        float pendingCastUntil;
        Vector3 direction;
        Hookable target, self;
        Hookable held; // tug of war: the ally this hook holds on an enemy's hook
        PudgeAnimation anim;
        PudgeMotor motor;
        HeroInventory inventory;

        void Awake()
        {
            self = GetComponent<Hookable>();
            anim = GetComponent<PudgeAnimation>();
            motor = GetComponent<PudgeMotor>();
            inventory = GetComponent<HeroInventory>();
            if (hookHead != null) hookHead.localScale *= headScale;
            TipLength = MeasureTip();
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

        /// <summary>Width of the strip the hook head sweeps (an enemy is caught when its radius touches it).</summary>
        public float HitWidth => hitRadius * 2f;

        /// <summary>Same direction TryCast would take for a world point.</summary>
        public Vector3 AimDirection(Vector3 worldPoint)
        {
            var dir = worldPoint - transform.position;
            dir.y = 0f;
            return dir.sqrMagnitude < 1e-4f ? transform.forward : dir.normalized;
        }

        /// <summary>Farthest point of the head's meshes in front of its pivot.</summary>
        float MeasureTip()
        {
            if (hookHead == null) return 0f;
            float tip = 0f;
            foreach (var mf in hookHead.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    tip = Mathf.Max(tip, hookHead.InverseTransformPoint(mf.transform.TransformPoint(corner)).z);
                }
            }
            return tip;
        }

        /// <summary>Throws the hook toward a world point (only the horizontal direction matters).</summary>
        public bool TryCast(Vector3 worldPoint)
        {
            if (!IsReady || (self != null && !self.IsControllable)) return false;
            if (Simulation.IsProxy)
            {
                CastRequested?.Invoke(worldPoint);
                pendingCastUntil = Time.time + 0.5f; // busy until the server's answer arrives
                return true;
            }
            direction = AimDirection(worldPoint);
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
            if (Simulation.IsProxy)
            {
                ShowRemote();
                return;
            }
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
                case State.Tethered:
                    Tether();
                    break;
                case State.Holding:
                    Hold();
                    break;
            }
            if (state is State.Flying or State.Returning or State.Tethered or State.Holding) UpdateChain();
        }

        void Launch()
        {
            // the head appears level with the hand but on the centre line, so its path and range do not depend
            // on where the animation has put the hand: exactly what the aim lines show
            var hand = Origin - transform.position;
            traveled = Mathf.Clamp(Vector3.Dot(new Vector3(hand.x, 0f, hand.z), direction), 0f, EffectiveRange);
            spin = 0f;
            hookHead.SetPositionAndRotation(AimStart + direction * traveled, Quaternion.LookRotation(direction));
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
            spin = Mathf.Repeat(spin + spinSpeed * Time.deltaTime, 360f);
            hookHead.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0f, 0f, spin);
            // swept test over this frame's path: a long frame must not let the hook pass through a target
            var hit = FindTarget(from, hookHead.position, out var contact);
            if (hit != null && IsRescue(hit))
            {
                hookHead.position = contact;
                Play(sounds.impact);
                if (OwnerCaught) Cancel(); // a caught owner cannot hold anyone
                else
                {
                    BeginHold(hit);
                    TargetCaught?.Invoke(hit);
                }
                return;
            }
            if (hit != null)
            {
                hookHead.position = contact;
                target = hit;
                caughtThisCast = IsEnemy(target); // rescuing an ally does not refresh the hook
                target.AttachTo(this);
                TargetCaught?.Invoke(target);
                Play(sounds.impact);
                Play(Pick(sounds.hitVoice));
                // the owner was caught while the hook flew: the catch hangs on a taut chain behind the owner
                if (OwnerCaught) BeginTether();
                else state = State.Returning;
            }
            else if (traveled >= EffectiveRange)
            {
                Play(Pick(sounds.missVoice));
                if (OwnerCaught) Cancel(); // nobody to reel an empty hook in
                else state = State.Returning;
            }
            if (state == State.Returning) Play(sounds.retract);
        }

        void Return()
        {
            var origin = Origin;
            var toOrigin = origin - hookHead.position;
            float dist = toOrigin.magnitude;
            float pull = target != null && target.HeldBy != null ? pullSpeed * heldPullFactor : pullSpeed;
            float step = (target != null ? pull : returnSpeed) * Time.deltaTime;
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
                // an ally is delivered to Pudge and let go; an enemy pulled all the way without crossing the pit
                // (not possible on the test arena) dies all the same
                if (IsEnemy(target)) target.StartFalling(Vector3.zero);
                else target.Release();
                target = null;
            }
            SetHookVisible(false);
            state = State.Ready;
            readyAt = Time.time + (caughtThisCast && resetCooldownOnHit ? 0f : cooldown);
            anim?.PlayAction(PudgeAnimation.HookEnd, 0.5f, 0.1f);
        }

        /// <summary>
        /// The owner was caught or started falling. A hook that is already pulling a target stays on it as a
        /// taut chain (the target is dragged after the owner, into the pit if the owner falls); a hook in flight
        /// flies on and whatever it catches hangs on that chain; a hook not yet thrown breaks off.
        /// </summary>
        public void OwnerLostControl()
        {
            if (state is State.Tethered or State.Flying) return;
            if (state == State.Returning && target != null)
            {
                BeginTether();
                return;
            }
            Cancel();
        }

        bool OwnerCaught => self != null && !self.IsControllable;

        void BeginTether()
        {
            var d = target.transform.position - transform.position;
            d.y = 0f;
            ropeLength = d.magnitude;
            timer = tetherTimeout;
            state = State.Tethered;
        }

        void Tether()
        {
            timer -= Time.deltaTime;
            if (target == null || !target.IsHooked || timer <= 0f)
            {
                Cancel();
                return;
            }
            // a falling or dead owner hangs on the chain and reels it in; an owner held by a taut chain too (two
            // Pudges hooked each other) is pulled by nobody, so both chains reel in and drag them together
            var anchor = transform.position;
            var toAnchor = anchor - target.transform.position;
            toAnchor.y = 0f;
            float dist = toAnchor.magnitude;
            if (self != null && (self.IsFalling || !self.IsAlive))
                ropeLength = Mathf.Max(0f, ropeLength - tetherReelSpeed * Time.deltaTime);
            else if (self != null && self.HookedBy != null && self.HookedBy.IsTethered)
                // slack taken up first: the other chain has already pulled this frame, both must pull alike
                ropeLength = Mathf.Max(0f, Mathf.Min(ropeLength, dist) - pullSpeed * Time.deltaTime);
            var dir = dist > 1e-4f ? toAnchor / dist : Vector3.zero;
            if (dist > ropeLength)
                target.DragTo(target.transform.position + dir * (dist - ropeLength), dir);
            var head = target.transform.position;
            hookHead.position = new Vector3(head.x, hookHead.position.y, head.z);
            if (dir.sqrMagnitude > 0f) hookHead.rotation = Quaternion.LookRotation(-dir);

            var arena = Arena.Instance;
            if (arena != null && arena.IsOverPit(target.transform.position))
            {
                var dropped = target;
                target = null;
                dropped.StartFalling(dir * tetherReelSpeed * dropCarry);
                TargetDropped?.Invoke(dropped);
                SetHookVisible(false);
                state = State.Ready;
                readyAt = Time.time + cooldown;
            }
        }

        /// <summary>
        /// Breaks the hook off at once (its owner was caught or is falling): the caught target is let go
        /// and the normal cooldown applies.
        /// </summary>
        public void Cancel()
        {
            if (state == State.Ready) return;
            ReleaseHeld();
            if (target != null)
            {
                target.Release();
                target = null;
            }
            SetHookVisible(false);
            state = State.Ready;
            readyAt = Time.time + cooldown;
        }

        /// <summary>
        /// A free enemy, or with hitsAllies a free ally or one an enemy's hook is pulling and nobody holds yet (a
        /// rescue). A hero already on a hook of this team or falling cannot be caught.
        /// </summary>
        public bool CanCatch(Hookable h)
        {
            if (h == self || !h.IsAlive || h.IsFalling) return false;
            if (IsEnemy(h)) return !h.IsHooked;
            return hitsAllies && (!h.IsHooked || IsRescue(h));
        }

        /// <summary>An ally on an enemy's hook that no other ally holds yet.</summary>
        bool IsRescue(Hookable h) =>
            h.IsHooked && h.HeldBy == null && h.HookedBy != null && h.HookedBy.Owner != null && IsEnemy(h.HookedBy.Owner);

        /// <summary>
        /// Tug of war: the ally stays on the enemy's hook, which now pulls slower, and the owner hangs on behind it
        /// on a chain of the length at the catch.
        /// </summary>
        void BeginHold(Hookable ally)
        {
            held = ally;
            ally.HeldBy = this;
            var d = ally.transform.position - transform.position;
            d.y = 0f;
            ropeLength = d.magnitude;
            state = State.Holding;
        }

        void Hold()
        {
            var enemyHook = held.HookedBy;
            if (!held.IsAlive || held.IsFalling || enemyHook == null)
            {
                var ally = held;
                bool saved = ally.IsAlive && !ally.IsFalling;
                Cancel(); // fell into the pit (or died): the chain breaks
                if (!saved) return;
                // the enemy's hook let go: the ally is pulled home, which is the rescue
                direction = AimDirection(ally.transform.position);
                target = ally;
                ally.AttachTo(this);
                SetHookVisible(true);
                state = State.Returning;
                Arena.Instance?.ReportRescue(ally, self);
                return;
            }
            if (enemyHook.Owner == null || !IsEnemy(enemyHook.Owner))
            {
                Cancel();
                return;
            }

            // the owner is dragged after the ally on the taut chain, but not over the pit edge: there it breaks
            var toAlly = held.transform.position - transform.position;
            toAlly.y = 0f;
            float dist = toAlly.magnitude;
            var dir = dist > 1e-4f ? toAlly / dist : transform.forward;
            if (dist > ropeLength && self != null)
            {
                var next = transform.position + dir * (dist - ropeLength);
                var arena = Arena.Instance;
                if (arena != null && arena.IsOverPit(next))
                {
                    Cancel();
                    return;
                }
                self.DragTo(next, dir);
            }
            var head = held.transform.position;
            hookHead.position = new Vector3(head.x, hookHead.position.y, head.z);
            hookHead.rotation = Quaternion.LookRotation(-dir);
        }

        void ReleaseHeld()
        {
            if (held == null) return;
            if (held.HeldBy == this) held.HeldBy = null;
            held = null;
        }

        /// <summary>First hero (enemy, or ally with hitsAllies) whose radius the hook head touches on the segment from → to (horizontal plane).</summary>
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
                if (!CanCatch(h)) continue;
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

        /// <summary>Network client: the hook's state on the server.</summary>
        /// <param name="cooldownLeft">Seconds until ready, counted from now (ignored while busy).</param>
        public void ApplyRemoteState(bool busy, float cooldownLeft, bool visible, bool flying, Vector3 head, Vector3 headDirection)
        {
            if (busy != remoteBusy) pendingCastUntil = 0f;
            remoteBusy = busy;
            readyAt = Time.time + Mathf.Max(0f, cooldownLeft);
            remoteFlying = flying;
            remoteHead = head;
            remoteDirection = headDirection;
            if (visible == HeadVisible) return;
            SetHookVisible(visible);
            if (!visible) return;
            spin = 0f;
            hookHead.position = head; // appears where the server has it, then follows smoothly
        }

        /// <summary>Network client: moves the head toward the server's position (it comes ~30 times a second).</summary>
        void ShowRemote()
        {
            if (!HeadVisible) return;
            float speed = Mathf.Max(flySpeed, pullSpeed, returnSpeed, tetherReelSpeed) * 1.5f;
            hookHead.position = Vector3.MoveTowards(hookHead.position, remoteHead, speed * Time.deltaTime);
            var dir = remoteDirection.sqrMagnitude > 1e-6f ? remoteDirection : transform.forward;
            if (remoteFlying) spin = Mathf.Repeat(spin + spinSpeed * Time.deltaTime, 360f);
            hookHead.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, remoteFlying ? spin : 0f);
            UpdateChain();
        }

        bool IsEnemy(Hookable h) => self == null || h.team != self.team;

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
