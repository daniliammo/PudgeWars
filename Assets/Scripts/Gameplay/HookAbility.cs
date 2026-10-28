using System;
using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Hook sounds: the slots of the original Player_PW Blueprint, filled with Dota 2's Meat Hook sounds
    /// (Resources/Audio/Hook) where the prefab leaves them empty.
    /// </summary>
    [Serializable]
    public class HookSounds
    {
        [Tooltip("The chain flying out (Hero_Pudge.AttackHookExtend); Resources/Audio/Hook/hook_throw if empty.")] public AudioClip cast;
        [Tooltip("The chain coming back (Hero_Pudge.AttackHookRetract); goes on from the cast sound if it is the same clip. Resources/Audio/Hook/hook_throw if empty.")] public AudioClip retract;
        [Tooltip("The hook hits a hero (Hero_Pudge.AttackHookImpact); Resources/Audio/Hook/hook_impact if empty.")] public AudioClip impact;
        [Tooltip("The hook is back at Pudge (Hero_Pudge.AttackHookRetractStop); Resources/Audio/Hook/hook_retract_stop if empty.")] public AudioClip retractStop;
        [Tooltip("Popal (SoundCue Hook_Popodanie, random): Pud_1, Pud_2, Pud_3, TargetHook")] public AudioClip[] hitVoice = Array.Empty<AudioClip>();
        [Tooltip("NePopal (SoundCue Returne_Sound, random): Pud_anger, Pud_1")] public AudioClip[] missVoice = Array.Empty<AudioClip>();

        /// <summary>Fills the empty sound slots with the Dota clips from Resources/Audio/Hook.</summary>
        public void FillMissing()
        {
            if (cast == null) cast = Resources.Load<AudioClip>("Audio/Hook/hook_throw");
            if (retract == null) retract = Resources.Load<AudioClip>("Audio/Hook/hook_throw");
            if (impact == null) impact = Resources.Load<AudioClip>("Audio/Hook/hook_impact");
            if (retractStop == null) retractStop = Resources.Load<AudioClip>("Audio/Hook/hook_retract_stop");
        }
    }

    /// <summary>
    /// Pudge's hook: turn to the target (Dota 2 facing rule), wind-up, flight to max range, catch the first
    /// hero on the way and pull it back. A target dragged over the pit is released and falls in. As Meat Hook in Dota,
    /// it catches allies too: an ally is pulled to Pudge unharmed and let go there. An ally an enemy's hook is pulling
    /// is held instead (a tug of war): the enemy pulls three times slower and drags the rescuer after the ally; if the
    /// enemy's hook lets go — also when the enemy falls into the pit, which then breaks its chain instead of dragging the
    /// held ally after it — the rescuer pulls the ally home (a rescue); if the ally reaches the pit the rescuer's chain
    /// breaks. Pulling a free ally out of the way of an enemy hook that would surely catch it is a rescue too:
    /// the ally, moving on as it moves now (standing, casting, or running too late), would be the first one the
    /// enemy's head reaches.
    /// An owner caught by an ally's hook is only carried home: its own hook goes on as usual (flies, pulls its catch,
    /// comes back to it); only an enemy's hook, the pit or death breaks it off or leaves it on a taut chain.
    /// On a network client (<see cref="Simulation.IsProxy"/>) the server throws the hook: a cast becomes a
    /// <see cref="CastRequested"/> request, and the head and the chain only show the state the server sends —
    /// interpolated on the same delayed timeline as the heroes' positions (<see cref="ShowRemote"/>), so the head and
    /// the hero it drags move together and smoothly.
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
        [Tooltip("Mana a cast takes (Dota 2 Meat Hook: 110–140 by level); spent as the wind-up starts.")]
        public float manaCost = 120f;
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

        [Header("Damage (allies take it too)")]
        [Tooltip("Dealt at the catch, at level 1.")] public float hitDamage = 110f;
        [Tooltip("Added to the catch damage per owner level above the first (levels do not exist yet).")]
        public float hitDamagePerLevel = 0f;
        [Tooltip("Dealt per metre the caught hero is dragged on this hook: the longer the pull, the more it hurts.")]
        public float damagePerMeter = 10f;

        [Header("Tug of war: an ally's hook holds the target")]
        [Tooltip("While an ally of the target holds it on its own hook, the pull is this many times slower.")]
        [Min(1f)] public float heldPullSlowdown = 3f;

        [Header("References (the head and the chain are unparented at runtime)")]
        public Transform hookHead;
        public LineRenderer chain;
        [Tooltip("Where the chain starts, e.g. the right hand bone.")] public Transform chainOrigin;
        public Vector3 fallbackOriginOffset = new(0.4f, 1.1f, 0.5f);
        public AudioSource audioSource;
        public HookSounds sounds = new();

        public event Action<Pudge> TargetCaught;
        /// <summary>Any hero's hook caught someone (the hook, its catch); on a network client too.</summary>
        public static event Action<HookAbility, Pudge> AnyTargetCaught;
        /// <summary>The head has left the hand; on a network client when the server's head appears.</summary>
        public event Action Launched;
        /// <summary>The head reached its full range without catching anyone; on a network client too.</summary>
        public event Action Missed;
        public event Action<Pudge> TargetDropped;
        /// <summary>Network client: the player casts toward this point; the server throws the hook.</summary>
        public event Action<Vector3> CastRequested;

        /// <summary>The hero that throws this hook.</summary>
        public Pudge Owner => self;
        /// <summary>Damage dealt at the catch, by the owner's level.</summary>
        public float HitDamage => hitDamage + hitDamagePerLevel * ((self != null ? self.Level : 1) - 1);
        /// <summary>The catch's damage on <paramref name="target"/> with the owner's item bonuses (Quelling Blade).</summary>
        public float HitDamageAgainst(Unit target) => HitDamage + DamageBonusAgainst(target);
        /// <summary>Damage per metre <paramref name="target"/> is dragged, the item bonuses added to every metre too.</summary>
        public float DamagePerMeterAgainst(Unit target) => damagePerMeter + DamageBonusAgainst(target);

        float DamageBonusAgainst(Unit target) => inventory != null ? inventory.DamageBonusAgainst(target) : 0f;
        /// <summary>Range including item bonuses (Aether Lens).</summary>
        public float EffectiveRange => range + (inventory != null ? inventory.CastRangeBonus : 0f);
        public bool IsBusy => Simulation.IsProxy ? remoteBusy || Time.time < pendingCastUntil : state != State.Ready;
        public bool IsReady => !IsBusy && Time.time >= readyAt;
        /// <summary>The owner has the mana for a cast.</summary>
        public bool HasMana => self == null || self.Mana >= manaCost;
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
        bool remoteBusy;
        struct RemoteHead
        {
            public double time;
            public bool visible, flying;
            public Vector3 position, direction;
        }
        readonly List<RemoteHead> remoteHeads = new(); // the server's head over (server) time, oldest first
        const int RemoteHeadLimit = 32;
        float pendingCastUntil;
        Vector3 direction;
        Pudge target, self;
        Pudge held; // tug of war: the ally this hook holds on an enemy's hook
        PudgeAnimation anim;
        PudgeMotor motor;
        HeroInventory inventory;
        AudioSource chainAudio; // the chain's own sound, stopped when the hook is gone
        bool remoteFlying, remoteCaught; // network client: the shown head flies out / caught someone this cast

        void Awake()
        {
            self = GetComponent<Pudge>();
            anim = GetComponent<PudgeAnimation>();
            motor = GetComponent<PudgeMotor>();
            inventory = GetComponent<HeroInventory>();
            sounds ??= new HookSounds();
            sounds.FillMissing();
            if (audioSource != null)
            {
                chainAudio = gameObject.AddComponent<AudioSource>();
                chainAudio.playOnAwake = false;
                chainAudio.spatialBlend = audioSource.spatialBlend;
                chainAudio.rolloffMode = audioSource.rolloffMode;
                chainAudio.minDistance = audioSource.minDistance;
                chainAudio.maxDistance = audioSource.maxDistance;
                chainAudio.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
            }
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
            if (!IsReady || !HasMana || (self != null && !self.IsControllable)) return false;
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
            // the mana goes as the throw starts; spent meanwhile (turning) — no throw, and no cooldown either
            if (self != null && !self.SpendMana(manaCost))
            {
                state = State.Ready;
                return;
            }
            anim?.PlayAction(PudgeAnimation.HookStart, 10f, 0.05f);
            state = State.Windup;
            timer = castDelay;
        }

        void Update()
        {
            if (Simulation.IsProxy) return; // the network side shows the server's head (ShowRemote)
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
            PlayChain(sounds.cast, restart: true);
            Launched?.Invoke();
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
                if (self != null && !self.IsControllable) Cancel(); // a caught or carried owner cannot hold anyone
                else
                {
                    BeginHold(hit);
                    RaiseCaught(hit);
                    hit.Damage(HitDamageAgainst(hit), self); // dying of it breaks the hold (Hold)
                }
                return;
            }
            if (hit != null)
            {
                hookHead.position = contact;
                target = hit;
                caughtThisCast = IsEnemy(target); // rescuing an ally does not refresh the hook
                bool savedFromHook = !IsEnemy(target) && WouldSurelyBeCaught(target); // before it is on this hook
                target.AttachTo(this);
                if (savedFromHook) Arena.Instance?.ReportRescue(target, self);
                RaiseCaught(target);
                Play(sounds.impact);
                Play(Pick(sounds.hitVoice));
                target.Damage(HitDamageAgainst(target), self);
                if (!target.IsAlive) target = null; // killed by the blow: the hook comes back empty
                // the owner was caught while the hook flew: the catch hangs on a taut chain behind the owner
                if (target != null && OwnerCaught) BeginTether();
                else state = State.Returning;
            }
            else if (traveled >= EffectiveRange)
            {
                Play(Pick(sounds.missVoice));
                Missed?.Invoke();
                // a miss comes back to its owner wherever it is: caught meanwhile, or falling into the pit with it
                state = State.Returning;
            }
            if (state == State.Returning) PlayChain(sounds.retract);
        }

        void Return()
        {
            if (target != null && !target.IsAlive) target = null; // killed meanwhile: its body is not dragged
            var origin = Origin;
            var toOrigin = origin - hookHead.position;
            float dist = toOrigin.magnitude;
            float pull = target != null && target.HeldBy != null ? pullSpeed / heldPullSlowdown : pullSpeed;
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
            DragTarget(hookHead.position, flatDir);
            if (target == null) return;
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
            Play(sounds.retractStop);
            state = State.Ready;
            readyAt = Time.time + (caughtThisCast && resetCooldownOnHit ? 0f : cooldown);
            // an owner on someone's hook keeps its own animation
            if (self == null || self.IsControllable) anim?.PlayAction(PudgeAnimation.HookEnd, 0.5f, 0.1f);
        }

        /// <summary>
        /// The owner was caught or started falling. A hook that is already pulling a target stays on it as a
        /// taut chain (the target is dragged after the owner, into the pit if the owner falls) until the owner is free
        /// again and pulls it on; a hook in flight flies on and whatever it catches hangs on that chain; a missed hook
        /// on its way back comes back to the owner wherever a hook (an enemy's or an ally's) drags it — an item is to
        /// let it catch on the way back — even into the pit after a falling owner; a hook not yet thrown breaks off.
        /// </summary>
        public void OwnerLostControl()
        {
            if (state is State.Tethered or State.Flying) return;
            if (state == State.Returning && target == null) return;
            // an ally's hook only carries the owner home: a hook on its way back comes back to it as usual
            if (state == State.Returning && !OwnerCaught) return;
            if (state == State.Returning && target != null && !OwnerFallsFromRescue)
            {
                BeginTether();
                return;
            }
            Cancel();
        }

        /// <summary>
        /// The owner is falling into the pit (or dead) while an ally of the target holds it: the chain breaks instead
        /// of dragging the target after the owner, and the rescuer pulls it home.
        /// </summary>
        bool OwnerFallsFromRescue => target != null && target.HeldBy != null && self != null && (self.IsFalling || !self.IsAlive);

        /// <summary>
        /// The owner cannot pull its hook in: an enemy's hook has it, it is falling or dead. An ally's hook only carries
        /// it home, and the owner's own hook goes on meanwhile (returning to it wherever it is carried).
        /// </summary>
        bool OwnerCaught => self != null && (!self.IsAlive || self.IsFalling || (self.IsHooked && !CarriedByAlly));

        bool CarriedByAlly => self.HookedBy != null && self.HookedBy.Owner != null && !IsEnemy(self.HookedBy.Owner);

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
            if (target == null || !target.IsHooked || OwnerFallsFromRescue)
            {
                Cancel();
                return;
            }
            // the owner is free again (a teammate's hook delivered it, the chain that held it broke): the hook pulls
            // its catch on the usual way instead of keeping both of them in place
            if (!OwnerCaught)
            {
                state = State.Returning;
                return;
            }
            if (timer <= 0f)
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
            {
                DragTarget(target.transform.position + dir * (dist - ropeLength), dir);
                if (target == null)
                {
                    Cancel();
                    return;
                }
            }
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
        /// Drags the catch to <paramref name="position"/> and hurts it for the distance (<see cref="DamagePerMeterAgainst"/>:
        /// <see cref="damagePerMeter"/> and the owner's item bonuses, per metre);
        /// a catch that dies of it is let go (<see cref="target"/> becomes null), its body flies on.
        /// </summary>
        void DragTarget(Vector3 position, Vector3 pullDirection)
        {
            var before = target.transform.position;
            target.DragTo(position, pullDirection);
            var moved = target.transform.position - before;
            moved.y = 0f;
            target.Damage(moved.magnitude * DamagePerMeterAgainst(target), self);
            if (!target.IsAlive) target = null;
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
        public bool CanCatch(Pudge h)
        {
            if (h == self || !h.IsAlive || h.IsFalling) return false;
            if (IsEnemy(h)) return !h.IsHooked;
            return hitsAllies && (!h.IsHooked || IsRescue(h));
        }

        /// <summary>The hook is on its way out (turning to throw, winding up or flying) and may still catch someone.</summary>
        public bool IsOnItsWayOut => state is State.Turning or State.Windup or State.Flying;

        /// <summary>
        /// This hook, on its way out, would catch <paramref name="hero"/> moving on at <paramref name="velocity"/>
        /// before reaching anyone else (the others staying where they are). A hook in flight goes on from its head,
        /// one still turning or winding up starts once that is over (its direction is already set and its owner
        /// cannot move meanwhile). False for a hook that is not on its way out.
        /// </summary>
        public bool WouldCatch(Pudge hero, Vector3 velocity) => !float.IsPositiveInfinity(CatchTime(hero, velocity));

        /// <summary>
        /// Seconds from now until this hook catches <paramref name="hero"/> as in <see cref="WouldCatch"/> (the turn
        /// and the wind-up left included); infinity if it would not catch it first, or is not on its way out.
        /// </summary>
        public float CatchTime(Pudge hero, Vector3 velocity)
        {
            Vector3 start;
            float delay, length;
            switch (state)
            {
                case State.Flying:
                    start = hookHead.position;
                    delay = 0f;
                    length = EffectiveRange - traveled;
                    break;
                case State.Turning:
                    start = AimStart;
                    delay = (motor != null ? motor.TimeToFace(direction) : 0f) + castDelay;
                    length = EffectiveRange;
                    break;
                case State.Windup:
                    start = AimStart;
                    delay = timer;
                    length = EffectiveRange;
                    break;
                default:
                    return float.PositiveInfinity;
            }
            if (!CanCatch(hero)) return float.PositiveInfinity;
            float when = TimeToReach(hero, velocity, start, delay, length);
            if (float.IsPositiveInfinity(when)) return when;
            foreach (var other in Pudge.All)
                if (other != hero && CanCatch(other) && TimeToReach(other, Vector3.zero, start, delay, length) < when)
                    return float.PositiveInfinity; // someone else is in the way first
            return delay + when;
        }

        /// <summary>
        /// Seconds of flight until the head, starting at <paramref name="start"/> after <paramref name="delay"/> and
        /// flying <paramref name="length"/> along the cast direction, touches a hero moving at <paramref name="velocity"/>
        /// (horizontal plane); infinity if it never does.
        /// </summary>
        float TimeToReach(Pudge hero, Vector3 velocity, Vector3 start, float delay, float length)
        {
            velocity.y = 0f;
            var p = start - (hero.transform.position + velocity * delay);
            p.y = 0f;
            var q = direction * flySpeed - velocity;
            q.y = 0f;
            float r = hitRadius + hero.radius;
            float a = q.sqrMagnitude, b = 2f * Vector3.Dot(p, q), c = p.sqrMagnitude - r * r;
            if (c <= 0f) return 0f; // already touching
            float disc = b * b - 4f * a * c;
            if (a < 1e-8f || disc < 0f) return float.PositiveInfinity;
            float t = (-b - Mathf.Sqrt(disc)) / (2f * a);
            return t >= 0f && t * flySpeed <= length ? t : float.PositiveInfinity;
        }

        /// <summary>
        /// An enemy hook on its way would catch this ally for sure: moving on as it moves now (standing, casting its
        /// own hook, or starting to run too late), the ally is the first one that hook reaches.
        /// </summary>
        bool WouldSurelyBeCaught(Pudge ally)
        {
            var allyHook = ally.GetComponent<HookAbility>();
            var velocity = allyHook != null && allyHook.IsBusy ? Vector3.zero : ally.PlanarVelocity; // casting: rooted
            foreach (var hero in Pudge.All)
            {
                if (hero.team == ally.team || !hero.TryGetComponent<HookAbility>(out var enemyHook)) continue;
                if (enemyHook.WouldCatch(ally, velocity)) return true;
            }
            return false;
        }

        /// <summary>An ally on an enemy's hook that no other ally holds yet.</summary>
        bool IsRescue(Pudge h) =>
            h.IsHooked && h.HeldBy == null && h.HookedBy != null && h.HookedBy.Owner != null && IsEnemy(h.HookedBy.Owner);

        /// <summary>
        /// Tug of war: the ally stays on the enemy's hook, which now pulls slower, and the owner hangs on behind it
        /// on a chain of the length at the catch.
        /// </summary>
        void BeginHold(Pudge ally)
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
            if (held == null) // left the game (a disconnect, a bot taken out for a joining player): the chain breaks
            {
                Cancel();
                return;
            }
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
            if (held != null && held.HeldBy == this) held.HeldBy = null;
            held = null; // also a hero that left the game
        }

        /// <summary>First hero (enemy, or ally with hitsAllies) whose radius the hook head touches on the segment from → to (horizontal plane).</summary>
        Pudge FindTarget(Vector3 from, Vector3 to, out Vector3 contact)
        {
            Pudge best = null;
            float bestT = float.MaxValue;
            contact = to;
            var seg = to - from;
            seg.y = 0f;
            float len2 = seg.sqrMagnitude;
            foreach (var h in Pudge.All)
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

        /// <summary>Network client: the server's hook caught <paramref name="target"/> (for the camera's shake and the sounds).</summary>
        public void ApplyRemoteCatch(Pudge target)
        {
            remoteCaught = true;
            Play(sounds.impact);
            RaiseCaught(target);
        }

        /// <summary>Network client: whether the server's hook is busy and its cooldown (the UI and casts use them at once).</summary>
        /// <param name="cooldownLeft">Seconds until ready, counted from now (ignored while busy).</param>
        public void ApplyRemoteState(bool busy, float cooldownLeft)
        {
            if (busy != remoteBusy) pendingCastUntil = 0f;
            remoteBusy = busy;
            readyAt = Time.time + Mathf.Max(0f, cooldownLeft);
        }

        /// <summary>
        /// Network client: the server's head as it was at <paramref name="serverTime"/>; shown later, by
        /// <see cref="ShowRemote"/>, between the two snapshots around the client's (delayed) time.
        /// </summary>
        public void AddRemoteHead(double serverTime, bool visible, bool flying, Vector3 position, Vector3 direction)
        {
            var snapshot = new RemoteHead { time = serverTime, visible = visible, flying = flying, position = position, direction = direction };
            if (remoteHeads.Count > 0 && serverTime <= remoteHeads[^1].time) remoteHeads[^1] = snapshot; // same moment, newer data
            else remoteHeads.Add(snapshot);
            if (remoteHeads.Count > RemoteHeadLimit) remoteHeads.RemoveAt(0);
        }

        /// <summary>
        /// Network client: shows the head as the server had it at <paramref name="time"/> on the server's clock — the
        /// timeline the heroes' positions are shown on (Mirror's interpolated time), so a dragged hero stays at the head.
        /// Between two snapshots the head moves in a straight line; it appears and disappears when the server's did.
        /// </summary>
        public void ShowRemote(double time)
        {
            while (remoteHeads.Count > 1 && remoteHeads[1].time <= time) remoteHeads.RemoveAt(0);
            if (remoteHeads.Count == 0 || time < remoteHeads[0].time) return; // nothing that old yet: as it is
            var from = remoteHeads[0];
            var to = remoteHeads.Count > 1 ? remoteHeads[1] : from;
            if (from.visible != HeadVisible)
            {
                // the server's hook is gone near the hand: it came back (not broken off far away)
                bool back = !from.visible && Vector3.Distance(hookHead.position, Origin) < 1.5f;
                SetHookVisible(from.visible);
                spin = 0f;
                if (from.visible)
                {
                    remoteFlying = true;
                    remoteCaught = false;
                    PlayChain(sounds.cast, restart: true);
                    Launched?.Invoke();
                }
                else if (back) Play(sounds.retractStop);
            }
            if (!from.visible) return;
            if (remoteFlying && !from.flying)
            {
                remoteFlying = false;
                if (!remoteCaught) Missed?.Invoke();
                PlayChain(sounds.retract);
            }
            float t = to.time > from.time ? Mathf.Clamp01((float)((time - from.time) / (to.time - from.time))) : 0f;
            if (!to.visible) t = 0f; // it vanishes there: no sliding toward a stale position
            hookHead.position = Vector3.Lerp(from.position, to.position, t);
            var dir = Vector3.Lerp(from.direction, to.direction, t);
            if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
            if (from.flying) spin = Mathf.Repeat(spin + spinSpeed * Time.deltaTime, 360f);
            hookHead.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, from.flying ? spin : 0f);
            UpdateChain();
        }

        bool IsEnemy(Pudge h) => self == null || h.team != self.team;

        void UpdateChain()
        {
            if (chain == null) return;
            chain.SetPosition(0, Origin);
            chain.SetPosition(1, hookHead.position);
        }

        void SetHookVisible(bool visible)
        {
            if (!visible && chainAudio != null) chainAudio.Stop();
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

        /// <summary>The chain's sound; one already playing the same clip goes on unless <paramref name="restart"/>.</summary>
        void PlayChain(AudioClip clip, bool restart = false)
        {
            if (clip == null || chainAudio == null) return;
            if (!restart && chainAudio.isPlaying && chainAudio.clip == clip) return;
            chainAudio.clip = clip;
            chainAudio.Play();
        }

        void RaiseCaught(Pudge target)
        {
            TargetCaught?.Invoke(target);
            AnyTargetCaught?.Invoke(this, target);
        }

        static AudioClip Pick(AudioClip[] clips) =>
            clips == null || clips.Length == 0 ? null : clips[UnityEngine.Random.Range(0, clips.Length)];
    }
}
