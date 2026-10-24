using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Test bot: wanders between random points on its side with short pauses, often right up to the pit edge
    /// (its motor keeps it from falling in), and throws its hook at the
    /// nearest enemy in range. Each throw randomly either leads the target or aims at where it stands now,
    /// so the player cannot dodge by always reacting the same way. The hook catches allies too, so the bot does
    /// not throw while an ally stands in its path; and when an enemy hooks an ally, the bot sometimes tries to
    /// pull the ally out, if its hook can reach the ally before the pit.
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
        [Range(0f, 1f)] public float rescueChance = 0.5f;
        [Tooltip("Reaction time before a rescue throw, seconds.")]
        public Vector2 rescueReaction = new(0.15f, 0.35f);

        PudgeMotor motor;
        Hookable self;
        HookAbility hook;
        Vector3 destination;
        float pause, nextThrowAt;
        readonly List<Hookable> targets = new();
        // the enemy hook seen pulling an ally: a rescue is decided once per catch
        HookAbility rescueFrom;
        bool rescueWanted;
        float rescueAt;

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
            nextThrowAt = Time.time + Random.Range(throwInterval.x, throwInterval.y);
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
            if (TryRescue(arena, out bool waiting) || (!waiting && TryThrow())) return;
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
                aim = Quaternion.Euler(0f, Random.Range(-aimError, aimError), 0f) * aim;
                if (!PathClear(aim, target)) continue;

                nextThrowAt = Time.time + Random.Range(throwInterval.x, throwInterval.y);
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
                rescueWanted = Random.value < rescueChance;
                rescueAt = Time.time + Random.Range(rescueReaction.x, rescueReaction.y);
            }
            if (!rescueWanted) return false;
            waiting = true;
            if (Time.time < rescueAt || hook == null || !hook.IsReady) return false;
            // the enemy's hook drags the ally straight to its owner (the measured velocity jumps on the catch frame)
            var pull = ally.HookedBy.Owner.transform.position - ally.transform.position;
            pull.y = 0f;
            var velocity = pull.normalized * ally.HookedBy.pullSpeed;
            if (!Intercept(ally, velocity, out var aim) || aim.magnitude > hook.EffectiveRange * rangeFraction) return false;
            // too late: the ally is over the pit (or already past it) by the time the hook gets there
            var meet = transform.position + aim;
            if (arena.IsOverPit(meet) || Mathf.Sign(meet.z) != Mathf.Sign(ally.transform.position.z)) return false;
            if (!PathClear(aim, ally)) return false;

            rescueWanted = false; // one try per catch
            waiting = false;
            motor.SetMoveInput(Vector3.zero);
            return hook.TryCast(meet);
        }

        Hookable FindHookedAlly()
        {
            foreach (var h in Hookable.All)
                if (h != self && h.team == self.team && h.IsAlive && !h.IsFalling && h.HookedBy != null
                    && h.HookedBy.Owner != null && h.HookedBy.Owner.team != self.team)
                    return h;
            return null;
        }

        /// <summary>
        /// Aim offset where the hook head meets a target moving at a constant <paramref name="v"/> (a hooked ally
        /// is dragged in a straight line); false if the head cannot catch up.
        /// </summary>
        public bool Intercept(Hookable target, Vector3 v, out Vector3 aim)
        {
            var to = target.transform.position - transform.position;
            to.y = 0f;
            float delay = (motor != null ? motor.TimeToFace(to) : 0f) + hook.castDelay;
            var start = to + v * delay;
            // |start + v·t| = flySpeed·t
            float a = v.sqrMagnitude - hook.flySpeed * hook.flySpeed;
            float b = 2f * Vector3.Dot(start, v);
            float c = start.sqrMagnitude;
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
                if (pause <= 0f) destination = NextStop(arena);
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
            motor.SetMoveInput(to.normalized);
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
