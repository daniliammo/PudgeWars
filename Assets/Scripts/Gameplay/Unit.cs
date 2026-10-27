using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Anything on the arena with a side, a name and health: the base of <see cref="Pudge"/> and of whatever other
    /// creatures come later. Health regenerates while alive and runs out from <see cref="Damage"/>, which ends in
    /// <see cref="OnHealthDepleted"/>. On a network client (<see cref="Simulation.IsProxy"/>) health is only shown as
    /// the server sends it.
    /// </summary>
    public abstract class Unit : MonoBehaviour
    {
        static readonly List<Unit> allUnits = new();
        /// <summary>Every unit on the arena, heroes included.</summary>
        public static IReadOnlyList<Unit> AllUnits => allUnits;

        public Team team;
        [Tooltip("Name shown over the head and in announcements; the object name if empty.")]
        public string nickname;
        [Tooltip("Dota 2 Pudge at level 1: about 670.")]
        public float maxHealth = 670f;
        [Tooltip("Health regenerated per second while alive (Dota 2 Pudge at level 1: about 2.5).")]
        public float healthRegen = 2.5f;
        [Tooltip("Collision radius for hook hits, metres (capsule radius 61 cm in the original).")]
        public float radius = 0.6f;

        public const string BotPrefix = "Бот ";

        public string Nickname => string.IsNullOrEmpty(nickname) ? name : nickname;
        /// <summary>True for a unit the computer plays (it has a <see cref="BotBrain"/>, on every machine).</summary>
        public bool IsBot { get; private set; }
        /// <summary>Name shown over the head: bots are marked, "Бот Мясник".</summary>
        public string DisplayName => IsBot ? BotPrefix + Nickname : Nickname;
        /// <summary>Unit level; a stub until levels exist: always 1.</summary>
        public virtual int Level => 1;
        public float Health { get; private set; }
        public bool IsAlive { get; protected set; } = true;

        protected virtual void Awake()
        {
            IsBot = GetComponent<BotBrain>() != null;
            Health = maxHealth;
        }

        protected virtual void OnEnable() => allUnits.Add(this);
        protected virtual void OnDisable() => allUnits.Remove(this);

        protected virtual void Update()
        {
            if (IsAlive && !Simulation.IsProxy && healthRegen > 0f)
                Health = Mathf.Min(maxHealth, Health + healthRegen * Time.deltaTime);
        }

        /// <summary>
        /// Takes <paramref name="amount"/> of health; at zero the unit dies by <paramref name="source"/> (null — by
        /// nobody). Server / offline only.
        /// </summary>
        public void Damage(float amount, Unit source)
        {
            if (!IsAlive || amount <= 0f || Simulation.IsProxy) return;
            Health = Mathf.Max(0f, Health - amount);
            if (Health <= 0f) OnHealthDepleted(source);
        }

        /// <summary>Health ran out from <see cref="Damage"/>: the unit must die (and set <see cref="IsAlive"/>).</summary>
        protected abstract void OnHealthDepleted(Unit source);

        /// <summary>Back to full health (a respawn).</summary>
        protected void RestoreHealth() => Health = maxHealth;

        /// <summary>Death without damage (falling into the pit): health shows as zero.</summary>
        protected void ClearHealth() => Health = 0f;

        /// <summary>Network client: the health the server keeps.</summary>
        public void ApplyRemoteHealth(float health) => Health = Mathf.Clamp(health, 0f, maxHealth);
    }
}
