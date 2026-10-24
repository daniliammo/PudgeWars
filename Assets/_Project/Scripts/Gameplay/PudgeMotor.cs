using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Kinematic ground movement limited to the character's side of the arena, with Dota 2 turning rules:
    /// the hero turns at its turn rate and only starts moving (or casting at a point) once the target
    /// direction is within 11.5° in front of it. With a look direction set (first-person controls) the hero
    /// turns to it instead and moves in any direction without turning, like in a shooter.
    /// </summary>
    public class PudgeMotor : MonoBehaviour
    {
        /// <summary>Dota 2: a unit moves or casts only when the target is within this angle in front of it.</summary>
        public const float FacingTolerance = 11.5f;

        [Tooltip("Dota 2 Pudge: 280 units/s; 1 Dota unit = 1 cm (the original's hook range 1300 = Meat Hook's 1300).")]
        public float moveSpeed = 2.8f;
        [Tooltip("Dota 2 turn rate in radians per 0.03 s (Pudge: 0.7, i.e. a 180° turn in about 0.13 s).")]
        public float turnRate = 0.7f;

        Vector3 input;
        Vector3? look;
        Hookable self;
        HookAbility hook;
        PudgeAnimation anim;

        public float TurnSpeedDegrees => turnRate / 0.03f * Mathf.Rad2Deg;

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
            if (self != null && !self.IsControllable) return;
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
            if (hook == null || !hook.IsBusy) anim?.Locomotion(speed);
        }

        /// <summary>Moves by velocity / moveSpeed for this frame within the own side; returns the actual speed.</summary>
        float Step(Vector3 direction)
        {
            var pos = transform.position + direction * (moveSpeed * Time.deltaTime);
            var arena = Arena.Instance;
            if (arena != null && self != null) pos = arena.ClampToSide(pos, self.team, self.radius);
            float speed = (pos - transform.position).magnitude / Mathf.Max(Time.deltaTime, 1e-5f);
            transform.position = pos;
            return speed;
        }
    }
}
