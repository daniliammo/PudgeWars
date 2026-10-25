using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Test bot: wanders between random points on its side with short pauses, often right up to the pit edge
    /// (its motor keeps it from falling in), and throws its hook at the
    /// nearest enemy in range. Each throw randomly either leads the target or aims at where it stands now,
    /// so the player cannot dodge by always reacting the same way. The hook catches allies too, so the bot does
    /// not throw while an ally stands in its path; and when an enemy hooks an ally, the bot sometimes throws its
    /// hook ahead of the dragged ally to hold it (a tug of war), if the head can reach the ally before the pit: it
    /// counts the turn, the wind-up, the head's flight speed and how wide the head catches. An enemy hook still on
    /// its way to an ally (turning, winding up, flying) the bot sometimes beats: after a somewhat longer reaction it
    /// pulls the ally out of its way, if its own head gets there first (a rescue too).
    /// </summary>
    public class BotBrain : MonoBehaviour
    {
        public Vector2 pauseRange = new(0.5f, 2f);
        [Tooltip("Chance that the next stop is at the very pit edge.")]
        [Range(0f, 1f)] public float edgeChance = 0.35f;
        [Tooltip("Random delay between hook throws, seconds.")]
        public Vector2 throwInterval = new(1.5f, 4f);
        [Tooltip("Chance that a throw leads the target; otherwise the bot aims at its current position.")]
        [Range(0f, 1f)] public float leadChance = 0.5f;
        [Tooltip("Maximum aiming error, degrees: lets the player dodge.")]
        public float aimError = 7f;
        [Tooltip("Throw only at targets closer than this share of the hook range.")]
        public float rangeFraction = 0.95f;
        [Tooltip("Extra clearance around allies on the hook's path, metres: the bot does not throw through them.")]
        public float allyClearance = 0.3f;
        [Tooltip("Chance that the bot tries to pull an ally out of an enemy's hook.")]
        [Range(0f, 1f)] public float rescueChance = 0.55f;
        [Tooltip("Reaction time before a rescue throw, seconds.")]
        public Vector2 rescueReaction = new(0.15f, 0.35f);
        [Tooltip("Reaction time before pulling an ally out of the way of an enemy hook on its way to it, seconds (the same chance).")]
        public Vector2 pullOutReaction = new(0.3f, 0.55f);
        [Tooltip("A bot that gets no closer to its stop for this long (another hero is in the way) picks a new one, seconds.")]
        public float stuckTime = 0.75f;
        [Tooltip("Difficulty multiplier (1 = the values above): divides the aiming error, the time between throws and the reactions, multiplies the rescue chance.")]
        [Min(0.05f)] public float difficulty = 1f;

        float Harder(float value) => value * difficulty;
        float Easier(float value) => value / Mathf.Max(0.05f, difficulty);
        float RandomEasier(Vector2 range) => Easier(Random.Range(range.x, range.y));
        float RescueChance => Mathf.Clamp01(Harder(rescueChance));

        PudgeMotor motor;
        Hookable self;
        HookAbility hook;
        Vector3 destination;
        float pause, nextThrowAt;
        float closestToStop, stuckFor;
        readonly List<Hookable> targets = new();
        // the enemy hook seen pulling an ally: a rescue is decided once per catch
        HookAbility rescueFrom;
        bool rescueWanted;
        float rescueAt;
        // the enemy hook seen on its way to an ally: pulling the ally out of its way is decided once per throw
        HookAbility pullOutFrom;
        bool pullOutWanted;
        float pullOutAt;

        void Awake()
        {
            motor = GetComponent<PudgeMotor>();
            self = GetComponent<Hookable>();
            hook = GetComponent<HookAbility>();
        }

        void OnEnable()
        {
            destination = transform.position;
            pause = Random.Range(pauseRange.x, pauseRange.y);
            nextThrowAt = Time.time + RandomEasier(throwInterval);
        }

        void OnDisable() => motor.SetMoveInput(Vector3.zero);

        void Update()
        {
            if (Simulation.IsProxy) return; // the server's copy of the bot thinks
            var arena = Arena.Instance;
            if (arena == null || !self.IsControllable)
            {
                motor.SetMoveInput(Vector3.zero);
                pause = Random.Range(pauseRange.x, pauseRange.y);
                destination = transform.position;
                return;
            }
            if (TryRescue(arena, out bool waiting) || TryPullOut(arena, out bool pullingOut)) return;
            if (!waiting && !pullingOut && TryThrow()) return;
            Wander(arena);
        }

        bool TryThrow()
        {
            if (hook == null || !hook.IsReady || Time.time < nextThrowAt) return false;
            FindTargets();
            // the nearest enemy that can be hooked without catching an ally on the way
            foreach (var target in targets)
            {
                var aim = AimOffset(target, Random.value < leadChance);
                aim = Quaternion.Euler(0f, Easier(Random.Range(-aimError, aimError)), 0f) * aim;
                if (!PathClear(aim, target)) continue;

                nextThrowAt = Time.time + RandomEasier(throwInterval);
                motor.SetMoveInput(Vector3.zero);
                return hook.TryCast(transform.position + aim);
            }
            return false;
        }

        /// <summary>
        /// An enemy's hook pulls an ally: with <see cref="rescueChance"/>, after a reaction delay, the bot hooks the
        /// ally back, but only if its hook reaches the ally before the ally is over the pit.
        /// <paramref name="waiting"/> is true while a rescue is planned: the bot keeps its hook for it.
        /// </summary>
        bool TryRescue(Arena arena, out bool waiting)
        {
            waiting = false;
            var ally = FindHookedAlly();
            if (ally == null)
            {
                rescueFrom = null;
                return false;
            }
            if (ally.HookedBy != rescueFrom)
            {
                rescueFrom = ally.HookedBy;
                rescueWanted = Random.value < RescueChance;
                rescueAt = Time.time + RandomEasier(rescueReaction);
            }
            if (!rescueWanted) return false;
            waiting = true;
            if (Time.time < rescueAt || hook == null || !hook.IsReady) return false;
            // the enemy's hook drags the ally straight to its owner (the measured velocity jumps on the catch frame),
            // held at its head: on the catch frame the ally still stands where it was caught, a step away from it
            var enemyHook = ally.HookedBy;
            var dragged = enemyHook.HeadVisible ? enemyHook.hookHead.position : ally.transform.position;
            dragged.y = ally.transform.position.y;
            var pull = enemyHook.Owner.transform.position - dragged;
            pull.y = 0f;
            var velocity = pull.normalized * enemyHook.pullSpeed;
            if (!Intercept(dragged, ally.radius, velocity, out var aim, out _)) return false;
            // the head catches the ally this far before its centre, at most at full range
            if (aim.magnitude > (hook.EffectiveRange + hook.hitRadius + ally.radius) * rangeFraction) return false;
            // too late: the ally is over the pit (or already past it) by the time the hook gets there
            var meet = transform.position + aim;
            if (arena.IsOverPit(meet) || Mathf.Sign(meet.z) != Mathf.Sign(dragged.z)) return false;
            if (!PathClear(aim, ally)) return false;

            rescueWanted = false; // one try per catch
            waiting = false;
            motor.SetMoveInput(Vector3.zero);
            return hook.TryCast(meet);
        }

        /// <summary>
        /// An enemy's hook on its way to an ally that it would catch first: with <see cref="rescueChance"/>, after
        /// <see cref="pullOutReaction"/>, the bot hooks the ally out of its way, but only if its own head reaches the
        /// ally before the enemy's does (and in range, and not through another ally).
        /// <paramref name="waiting"/> is true while it is planned: the bot keeps its hook for it.
        /// </summary>
        bool TryPullOut(Arena arena, out bool waiting)
        {
            waiting = false;
            var ally = FindThreatenedAlly(out var enemyHook, out var velocity, out float enemyCatch);
            if (ally == null)
            {
                // the same throw may stop looking dangerous for a moment (the ally runs): decided once all the same
                if (pullOutFrom != null && !pullOutFrom.IsOnItsWayOut) pullOutFrom = null;
                return false;
            }
            if (enemyHook != pullOutFrom)
            {
                pullOutFrom = enemyHook;
                pullOutWanted = Random.value < RescueChance;
                pullOutAt = Time.time + RandomEasier(pullOutReaction);
            }
            if (!pullOutWanted) return false;
            waiting = true;
            if (Time.time < pullOutAt || hook == null || !hook.IsReady) return false;
            if (!Intercept(ally, velocity, out var aim, out float ownCatch)) return false;
            if (ownCatch >= enemyCatch - 0.05f) return false; // the enemy's head gets there first
            if (aim.magnitude > (hook.EffectiveRange + hook.hitRadius + ally.radius) * rangeFraction) return false;
            var meet = transform.position + aim;
            if (arena.IsOverPit(meet) || Mathf.Sign(meet.z) != Mathf.Sign(ally.transform.position.z)) return false;
            if (!PathClear(aim, ally)) return false;

            pullOutWanted = false; // one try per throw
            waiting = false;
            motor.SetMoveInput(Vector3.zero);
            return hook.TryCast(meet);
        }

        /// <summary>
        /// The ally an enemy's hook on its way out would catch soonest (each ally moving on as it moves now, standing
        /// still while casting), with that hook, the ally's velocity and the seconds until the catch.
        /// </summary>
        Hookable FindThreatenedAlly(out HookAbility enemyHook, out Vector3 velocity, out float catchTime)
        {
            Hookable threatened = null;
            enemyHook = null;
            velocity = Vector3.zero;
            catchTime = float.PositiveInfinity;
            foreach (var enemy in Hookable.All)
            {
                if (enemy.team == self.team || !enemy.TryGetComponent<HookAbility>(out var enemyHookHere) || !enemyHookHere.IsOnItsWayOut) continue;
                foreach (var ally in Hookable.All)
                {
                    if (ally == self || ally.team != self.team) continue;
                    var v = ally.TryGetComponent<HookAbility>(out var allyHook) && allyHook.IsBusy ? Vector3.zero : ally.PlanarVelocity;
                    float when = enemyHookHere.CatchTime(ally, v);
                    if (when >= catchTime) continue;
                    (threatened, enemyHook, velocity, catchTime) = (ally, enemyHookHere, v, when);
                }
            }
            return threatened;
        }

        Hookable FindHookedAlly()
        {
            foreach (var h in Hookable.All)
                if (h != self && h.team == self.team && h.IsAlive && !h.IsFalling && h.HookedBy != null && h.HeldBy == null
                    && h.HookedBy.Owner != null && h.HookedBy.Owner.team != self.team)
                    return h;
            return null;
        }

        /// <summary>
        /// Aim offset where the hook head meets a target moving at a constant <paramref name="v"/> (a hooked ally
        /// is dragged in a straight line); false if the head cannot catch up. Counts the turn and the wind-up before
        /// the throw, the head's start at the hand, its flight speed and that it catches a hero hitRadius + radius
        /// away from its centre.
        /// </summary>
        public bool Intercept(Hookable target, Vector3 v, out Vector3 aim) => Intercept(target, v, out aim, out _);

        /// <param name="catchTime">Seconds from now until the head catches the target (the turn and wind-up included).</param>
        public bool Intercept(Hookable target, Vector3 v, out Vector3 aim, out float catchTime) =>
            Intercept(target.transform.position, target.radius, v, out aim, out catchTime);

        /// <summary>The same for a hero of <paramref name="radius"/> at <paramref name="position"/> now.</summary>
        public bool Intercept(Vector3 position, float radius, Vector3 v, out Vector3 aim, out float catchTime)
        {
            catchTime = float.PositiveInfinity;
            var to = position - transform.position;
            to.y = 0f;
            float delay = (motor != null ? motor.TimeToFace(to) : 0f) + hook.castDelay;
            var start = to + v * delay;
            float reach = hook.hitRadius + radius + hook.LaunchOffset;
            float s = hook.flySpeed;
            if (start.sqrMagnitude <= reach * reach)
            {
                aim = start;
                catchTime = delay;
                return true; // the head catches it right at the launch
            }
            // |start + v·t| = flySpeed·t + reach
            float a = v.sqrMagnitude - s * s;
            float b = 2f * (Vector3.Dot(start, v) - s * reach);
            float c = start.sqrMagnitude - reach * reach;
            float t;
            if (Mathf.Abs(a) < 1e-4f) t = b < 0f ? -c / b : -1f;
            else
            {
                float disc = b * b - 4f * a * c;
                if (disc < 0f)
                {
                    aim = to;
                    return false;
                }
                float sq = Mathf.Sqrt(disc);
                float t1 = (-b - sq) / (2f * a), t2 = (-b + sq) / (2f * a);
                // the earliest meeting that is still ahead
                t = Mathf.Min(t1, t2) >= 0f ? Mathf.Min(t1, t2) : Mathf.Max(t1, t2);
            }
            aim = start + v * Mathf.Max(t, 0f);
            aim.y = 0f;
            if (t >= 0f) catchTime = delay + t;
            return t >= 0f;
        }

        /// <summary>
        /// True if no ally is in the way of a hook thrown along <paramref name="aim"/> to full range (a miss flies
        /// on): each ally is checked where it will be when the head passes it.
        /// </summary>
        public bool PathClear(Vector3 aim, Hookable target)
        {
            aim.y = 0f;
            if (aim.sqrMagnitude < 1e-6f) return true;
            var dir = aim.normalized;
            float delay = (motor != null ? motor.TimeToFace(aim) : 0f) + hook.castDelay;
            foreach (var h in Hookable.All)
            {
                if (h == self || h == target || h.team != self.team || !h.IsAlive || h.IsFalling) continue;
                var rel = h.transform.position - transform.position;
                rel.y = 0f;
                float along = Mathf.Clamp(Vector3.Dot(rel, dir), 0f, hook.EffectiveRange);
                rel += h.PlanarVelocity * (delay + along / hook.flySpeed);
                rel.y = 0f;
                along = Vector3.Dot(rel, dir);
                if (along < -h.radius || along > hook.EffectiveRange + h.radius) continue;
                float clearance = hook.hitRadius + h.radius + allyClearance;
                if ((rel - dir * Mathf.Clamp(along, 0f, hook.EffectiveRange)).sqrMagnitude <= clearance * clearance) return false;
            }
            return true;
        }

        /// <summary>
        /// Horizontal offset from the bot to its aim point: the target's current position, or with
        /// <paramref name="lead"/> where it will be when the hook gets there.
        /// </summary>
        public Vector3 AimOffset(Hookable target, bool lead)
        {
            var to = target.transform.position - transform.position;
            to.y = 0f;
            if (!lead) return to;
            float turn = motor != null ? motor.TimeToFace(to) : 0f;
            float eta = turn + hook.castDelay + to.magnitude / hook.flySpeed;
            var aim = to + target.PlanarVelocity * eta;
            aim.y = 0f;
            return aim;
        }

        /// <summary>Enemies within range, nearest first.</summary>
        void FindTargets()
        {
            targets.Clear();
            float maxDist = hook.EffectiveRange * rangeFraction;
            foreach (var h in Hookable.All)
            {
                if (h.team == self.team || !h.CanBeHooked || PlanarDistance(h) >= maxDist) continue;
                targets.Add(h);
            }
            targets.Sort((x, y) => PlanarDistance(x).CompareTo(PlanarDistance(y)));
        }

        float PlanarDistance(Hookable h)
        {
            var d = h.transform.position - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        void Wander(Arena arena)
        {
            if (pause > 0f)
            {
                pause -= Time.deltaTime;
                motor.SetMoveInput(Vector3.zero);
                if (pause <= 0f) SetStop(NextStop(arena));
                return;
            }
            var to = destination - transform.position;
            to.y = 0f;
            if (to.magnitude < 0.3f)
            {
                pause = Random.Range(pauseRange.x, pauseRange.y);
                motor.SetMoveInput(Vector3.zero);
                return;
            }
            // heroes do not walk through each other: a bot pressed against one goes elsewhere
            if (to.magnitude < closestToStop - 0.05f)
            {
                closestToStop = to.magnitude;
                stuckFor = 0f;
            }
            else if ((stuckFor += Time.deltaTime) > stuckTime)
            {
                SetStop(NextStop(arena));
                return;
            }
            motor.SetMoveInput(to.normalized);
        }

        void SetStop(Vector3 stop)
        {
            destination = stop;
            closestToStop = float.MaxValue;
            stuckFor = 0f;
        }

        Vector3 NextStop(Arena arena)
        {
            var p = arena.RandomPointOnSide(self.team, 1.5f, 0.3f);
            // a stop at the edge lies a little over it: the motor halts the bot right at the brink
            if (Random.value < edgeChance) p.z = Mathf.Sign(p.z) * (arena.pitHalfDepth - 0.1f);
            return p;
        }
    }
}
