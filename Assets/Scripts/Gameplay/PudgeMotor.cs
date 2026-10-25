using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Kinematic ground movement on the character's side of the arena (walls hold it, the pit edge does not: a step
    /// over the edge drops the hero into the pit), with Dota 2 turning rules:
    /// the hero turns at its turn rate and only starts moving (or casting at a point) once the target
    /// direction is within 11.5° in front of it. With a look direction set (first-person controls) the hero
    /// turns to it instead and moves in any direction without turning, like in a shooter.
    /// Heroes do not walk through each other or through trees (obstacles): a step into one slides along it.
    /// </summary>
    public class PudgeMotor : MonoBehaviour
    {
        /// <summary>Dota 2: a unit moves or casts only when the target is within this angle in front of it.</summary>
        public const float FacingTolerance = 11.5f;

        [Tooltip("Dota 2 Pudge: 280 units/s; 1 Dota unit = 1 cm (the original's hook range 1300 = Meat Hook's 1300).")]
        public float moveSpeed = 2.8f;
        [Tooltip("Dota 2 turn rate in radians per 0.03 s (Pudge: 0.7, i.e. a 180° turn in about 0.13 s).")]
        public float turnRate = 0.7f;
        [Tooltip("Stop at the pit edge instead of stepping over it (bots).")]
        public bool keepOffPit;
        [Tooltip("With keepOffPit: how far from the edge the centre stops, metres (the body hangs over it).")]
        public float pitGap = 0.1f;

        Vector3 input;
        Vector3? look;
        Hookable self;
        HookAbility hook;
        PudgeAnimation anim;

        public float TurnSpeedDegrees => turnRate / 0.03f * Mathf.Rad2Deg;
        /// <summary>Movement input as last set (a network client sends it to the server, which moves the hero).</summary>
        public Vector3 MoveInput => input;
        public Vector3? LookDirection => look;

        void Awake()
        {
            self = GetComponent<Hookable>();
            hook = GetComponent<HookAbility>();
            anim = GetComponent<PudgeAnimation>();
        }

        /// <summary>Desired world-space movement direction; its length (clamped to 1) scales the speed.</summary>
        public void SetMoveInput(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            input = Vector3.ClampMagnitude(worldDirection, 1f);
        }

        /// <summary>Facing to keep while moving freely (strafing); null restores the Dota rules.</summary>
        public void SetLookDirection(Vector3? worldDirection)
        {
            var flat = worldDirection is { } d ? new Vector3(d.x, 0f, d.z) : Vector3.zero;
            look = flat.sqrMagnitude > 1e-6f ? flat : null;
        }

        public bool CanMove => (self == null || self.IsControllable) && (hook == null || !hook.IsBusy);

        /// <summary>Turns toward a horizontal direction at the turn rate; returns the angle still left, degrees.</summary>
        public float TurnToward(Vector3 direction, float deltaTime)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return 0f;
            var look = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, TurnSpeedDegrees * deltaTime);
            return Quaternion.Angle(transform.rotation, look);
        }

        /// <summary>Seconds needed to face a horizontal direction from the current facing.</summary>
        public float TimeToFace(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return 0f;
            float angle = Mathf.Max(0f, Vector3.Angle(transform.forward, direction) - FacingTolerance);
            return angle / TurnSpeedDegrees;
        }

        void Update()
        {
            if (Simulation.IsProxy || (self != null && !self.IsControllable)) return;
            float speed = 0f;
            if (CanMove && look is { } facing)
            {
                TurnToward(facing, Time.deltaTime);
                if (input.sqrMagnitude > 1e-4f) speed = Step(input);
            }
            else if (CanMove && input.sqrMagnitude > 1e-4f)
            {
                float angleLeft = TurnToward(input, Time.deltaTime);
                if (angleLeft <= FacingTolerance) speed = Step(transform.forward * input.magnitude);
            }
            if (self != null && !self.IsControllable) return; // stepped over the edge: the fall animation stays
            if (hook == null || !hook.IsBusy) anim?.Locomotion(speed);
        }

        /// <summary>Moves by velocity / moveSpeed for this frame within the own side; returns the actual speed.</summary>
        float Step(Vector3 direction)
        {
            var pos = transform.position + direction * (moveSpeed * Time.deltaTime);
            var arena = Arena.Instance;
            if (arena != null && self != null) pos = arena.ClampToSide(pos, self.team, self.radius, keepOffPit ? pitGap : null);
            if (self != null)
            {
                pos = PushOutOfHeroes(pos);
                if (arena != null) pos = arena.ClampToSide(pos, self.team, self.radius, keepOffPit ? pitGap : null);
            }
            var velocity = (pos - transform.position) / Mathf.Max(Time.deltaTime, 1e-5f);
            transform.position = pos;
            if (arena != null && self != null && arena.IsOverPit(pos)) self.StepIntoPit(velocity);
            return velocity.magnitude;
        }

        /// <summary>
        /// Keeps the step out of other heroes' bodies and of obstacles (circles of their radii): the part of the step
        /// into one is dropped, so the walker slides along it. Bodies that already overlap (a pulled ally let go next
        /// to Pudge, a hero dragged onto a tree) may only move apart, without being thrown out at once.
        /// </summary>
        Vector3 PushOutOfHeroes(Vector3 pos)
        {
            var from = transform.position;
            for (int pass = 0; pass < 2; pass++) // a second pass settles a step between two bodies
            {
                foreach (var other in Hookable.All)
                {
                    if (other == self || !other.IsAlive || other.IsFalling) continue;
                    pos = PushOut(pos, from, other.transform.position, other.radius);
                }
                foreach (var obstacle in Obstacle.All)
                    pos = PushOut(pos, from, obstacle.transform.position, obstacle.radius);
            }
            return pos;
        }

        Vector3 PushOut(Vector3 pos, Vector3 from, Vector3 center, float radius)
        {
            var d = new Vector3(pos.x - center.x, 0f, pos.z - center.z);
            var was = new Vector3(from.x - center.x, 0f, from.z - center.z);
            float allowed = Mathf.Min(self.radius + radius, was.magnitude);
            if (d.sqrMagnitude >= allowed * allowed) return pos;
            var away = d.sqrMagnitude > 1e-8f ? d.normalized : was.sqrMagnitude > 1e-8f ? was.normalized : -transform.forward;
            return new Vector3(center.x + away.x * allowed, pos.y, center.z + away.z * allowed);
        }
    }
}
