using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Test bot: wanders between random points on its side with short pauses and throws its hook at the
    /// nearest enemy in range. Each throw randomly either leads the target or aims at where it stands now,
    /// so the player cannot dodge by always reacting the same way.
    /// </summary>
    public class BotBrain : MonoBehaviour
    {
        public Vector2 pauseRange = new(0.5f, 2f);
        [Tooltip("Random delay between hook throws, seconds.")]
        public Vector2 throwInterval = new(1.5f, 4f);
        [Tooltip("Chance that a throw leads the target; otherwise the bot aims at its current position.")]
        [Range(0f, 1f)] public float leadChance = 0.5f;
        [Tooltip("Maximum aiming error, degrees: lets the player dodge.")]
        public float aimError = 7f;
        [Tooltip("Throw only at targets closer than this share of the hook range.")]
        public float rangeFraction = 0.95f;

        PudgeMotor motor;
        Hookable self;
        HookAbility hook;
        Vector3 destination;
        float pause, nextThrowAt;

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
            var arena = Arena.Instance;
            if (arena == null || !self.IsControllable)
            {
                motor.SetMoveInput(Vector3.zero);
                pause = Random.Range(pauseRange.x, pauseRange.y);
                destination = transform.position;
                return;
            }
            if (TryThrow()) return;
            Wander(arena);
        }

        bool TryThrow()
        {
            if (hook == null || !hook.IsReady || Time.time < nextThrowAt) return false;
            var target = FindTarget();
            if (target == null) return false;

            var aim = AimOffset(target, Random.value < leadChance);
            aim = Quaternion.Euler(0f, Random.Range(-aimError, aimError), 0f) * aim;

            nextThrowAt = Time.time + Random.Range(throwInterval.x, throwInterval.y);
            motor.SetMoveInput(Vector3.zero);
            return hook.TryCast(transform.position + aim);
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

        Hookable FindTarget()
        {
            Hookable best = null;
            float bestDist = hook.range * rangeFraction;
            foreach (var h in Hookable.All)
            {
                if (h.team == self.team || !h.CanBeHooked) continue;
                var d = h.transform.position - transform.position;
                d.y = 0f;
                if (d.magnitude < bestDist)
                {
                    bestDist = d.magnitude;
                    best = h;
                }
            }
            return best;
        }

        void Wander(Arena arena)
        {
            if (pause > 0f)
            {
                pause -= Time.deltaTime;
                motor.SetMoveInput(Vector3.zero);
                if (pause <= 0f) destination = arena.RandomPointOnSide(self.team, 1.5f);
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
    }
}
