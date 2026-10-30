using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// An effect on a unit, shown as a round icon over the ability bar (<see cref="EffectBar"/>): a timed one from an
    /// item's use (Tango's Eat Tree) or an aura from a nearby ally's item (Basilius Aura).
    /// </summary>
    public readonly struct UnitEffect
    {
        public readonly ItemDef item;
        public readonly string name, description;
        /// <summary>Time.time it ends at, and how long it lasts in all; both 0 for one that lasts while it holds (an aura).</summary>
        public readonly float endsAt, duration;
        /// <summary>The unit giving it (the aura's owner); null for the unit's own.</summary>
        public readonly Unit source;

        public UnitEffect(ItemDef item, string name, string description, float endsAt, float duration, Unit source)
        {
            this.item = item;
            this.name = name;
            this.description = description;
            this.endsAt = endsAt;
            this.duration = duration;
            this.source = source;
        }

        public bool IsTimed => duration > 0f;
        public float Left => IsTimed ? Mathf.Max(0f, endsAt - Time.time) : 0f;
    }

    /// <summary>
    /// Anything on the arena with a side, a name, health and mana: the base of <see cref="Pudge"/> and of whatever other
    /// creatures come later. Mana regenerates while alive (<see cref="ManaRegen"/>) up to <see cref="MaxMana"/>; items
    /// (<see cref="HeroInventory"/>) add to both: what an item adds to the maximum comes into the current mana at once
    /// (a lower maximum only cuts the mana down to it).
    /// Health regenerates while alive (<see cref="HealthRegen"/>: the unit's own and what items add
    /// for a while, <see cref="AddRegen"/>) and runs out from <see cref="Damage"/>, which ends in
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
        [Tooltip("Mana without items (Dota 2 Pudge at level 1: 75 + 16 intelligence × 12 = 267).")]
        public float maxMana = 267f;
        [Tooltip("Mana regenerated per second without items (Dota 2 Pudge at level 1: 16 intelligence × 0.05 = 0.8).")]
        public float manaRegen = 0.8f;
        [Tooltip("Collision radius for hook hits, metres (capsule radius 61 cm in the original).")]
        public float radius = 0.6f;

        public const string BotPrefix = "Бот ";

        public string Nickname => string.IsNullOrEmpty(nickname) ? name : nickname;
        /// <summary>True for a unit the computer plays (it has a <see cref="BotBrain"/>, on every machine).</summary>
        public bool IsBot { get; private set; }
        /// <summary>Name shown over the head: bots are marked, "Бот Мясник".</summary>
        public string DisplayName => IsBot ? BotPrefix + Nickname : Nickname;
        /// <summary>A hero (Pudge), not a creep or another creature: some bonuses tell them apart (Quelling Blade).</summary>
        public virtual bool IsHero => false;
        /// <summary>Unit level; a stub until levels exist: always 1.</summary>
        public virtual int Level => 1;
        public float Health { get; private set; }
        public bool IsAlive { get; protected set; } = true;
        public float Mana { get; private set; }

        /// <summary>Maximum mana: the unit's own and what its items add.</summary>
        public float MaxMana => Simulation.IsProxy && remoteMaxMana >= 0f ? remoteMaxMana : maxMana + (inventory != null ? inventory.ManaBonus : 0f);

        /// <summary>Mana regenerated per second: the unit's own and what its items add.</summary>
        public float ManaRegen => Simulation.IsProxy && remoteManaRegen >= 0f
            ? remoteManaRegen
            : manaRegen + (inventory != null ? inventory.ManaRegenBonus : 0f) + AuraManaRegen;

        /// <summary>
        /// Basilius Aura on this unit: of every living ally (itself included) whose items carry it and who stands within
        /// its radius; all of them add up — two heroes with Ring of Basilius give +2, and so does one hero with two.
        /// </summary>
        public float AuraManaRegen
        {
            get
            {
                float sum = 0f;
                foreach (var ally in allUnits)
                {
                    if (ally.team != team || !ally.IsAlive || ally.inventory == null) continue;
                    sum += ally.inventory.AuraManaRegenWithin((ally.transform.position - transform.position).magnitude);
                }
                return sum;
            }
        }

        /// <summary>Health regenerated per second now: the unit's own and the bonuses still running (Tango).</summary>
        public float HealthRegen
        {
            get
            {
                if (Simulation.IsProxy && remoteRegen >= 0f) return remoteRegen;
                float regen = healthRegen;
                foreach (var bonus in regenBonuses)
                    if (bonus.until > Time.time) regen += bonus.perSecond;
                return regen;
            }
        }

        readonly List<(float perSecond, float until, float duration, ItemDef source)> regenBonuses = new();
        // network client: the timed effects the server keeps (item, Time.time it ends at, its whole duration)
        readonly List<(ItemDef item, float until, float duration)> remoteTimed = new();
        /// <summary>Grows whenever a timed effect starts or ends (the network side resends them then).</summary>
        public int EffectsVersion { get; private set; }
        float remoteRegen = -1f, remoteMaxMana = -1f, remoteManaRegen = -1f;
        HeroInventory inventory;
        float lastMaxMana;
        bool cleared; // dead: health and mana cleared
        float aliveManaRegen; // the mana regeneration of the last frame alive (the death takes the unit's own aura away)

        protected virtual void Awake()
        {
            IsBot = GetComponent<BotBrain>() != null;
            inventory = GetComponent<HeroInventory>();
            Health = maxHealth;
            Mana = lastMaxMana = MaxMana;
        }

        protected virtual void OnEnable() => allUnits.Add(this);
        protected virtual void OnDisable() => allUnits.Remove(this);

        protected virtual void Update()
        {
            if (regenBonuses.RemoveAll(b => b.until <= Time.time) > 0) EffectsVersion++;
            float regen = HealthRegen;
            if (IsAlive && !Simulation.IsProxy && regen > 0f)
                Health = Mathf.Min(maxHealth, Health + regen * Time.deltaTime);
            if (Simulation.IsProxy) return;
            float max = MaxMana;
            // an item raised the maximum: as much comes into the mana at once; a lower one only cuts it down
            if (!Mathf.Approximately(max, lastMaxMana))
            {
                if (max > lastMaxMana) Mana += max - lastMaxMana;
                lastMaxMana = max;
            }
            if (IsAlive)
            {
                aliveManaRegen = ManaRegen;
                Mana = Mathf.Clamp(Mana + aliveManaRegen * Time.deltaTime, 0f, max);
            }
        }

        /// <summary>Gives <paramref name="amount"/> of mana, up to the maximum (Arcane Boots). Server / offline only.</summary>
        /// <param name="shown">From an active use (Arcane Boots, Enchanted Mango): the mana it really added pops up as a
        /// blue "+N" over the unit (<see cref="ManaPopups"/>).</param>
        public void RestoreMana(float amount, bool shown = false)
        {
            if (!IsAlive || Simulation.IsProxy || amount <= 0f) return;
            float before = Mana;
            Mana = Mathf.Min(MaxMana, Mana + amount);
            if (shown && Mana - before > 0f) ShowManaRestored(Mana - before);
        }

        /// <summary>Mana an active use gave this unit, to show (on a network client the server's amount arrives here).</summary>
        public event Action<float> ManaRestoredShown;
        /// <summary>The same for any unit.</summary>
        public static event Action<Unit, float> AnyManaRestoredShown;

        /// <summary>Shows <paramref name="amount"/> of mana given to the unit by an active use; called on a network client by the server's message.</summary>
        public void ShowManaRestored(float amount)
        {
            ManaRestoredShown?.Invoke(amount);
            AnyManaRestoredShown?.Invoke(this, amount);
        }

        /// <summary>Takes <paramref name="amount"/> of mana if the unit has that much (an ability's or an item's cost). Server / offline only.</summary>
        public bool SpendMana(float amount)
        {
            if (!IsAlive || Simulation.IsProxy || amount > Mana) return false;
            Mana -= Mathf.Max(0f, amount);
            return true;
        }

        /// <summary>Regenerates <paramref name="perSecond"/> more for <paramref name="duration"/> seconds (Tango); death ends it.</summary>
        /// <param name="source">The item that gave it (its icon and name in the effects).</param>
        public void AddRegen(float perSecond, float duration, ItemDef source = null)
        {
            if (!IsAlive || perSecond <= 0f || duration <= 0f) return;
            regenBonuses.Add((perSecond, Time.time + duration, duration, source));
            EffectsVersion++;
        }

        /// <summary>The timed effects from items now: item, Time.time each ends at, its whole duration (one per use).</summary>
        public IEnumerable<(ItemDef item, float until, float duration)> TimedEffects()
        {
            if (Simulation.IsProxy)
            {
                foreach (var e in remoteTimed)
                    if (e.until > Time.time) yield return e;
                yield break;
            }
            foreach (var b in regenBonuses)
                if (b.source != null && b.until > Time.time) yield return (b.source, b.until, b.duration);
        }

        /// <summary>Network client: the timed effects the server keeps (ends in local Time.time).</summary>
        public void ApplyRemoteEffects(IEnumerable<(ItemDef item, float until, float duration)> effects)
        {
            remoteTimed.Clear();
            remoteTimed.AddRange(effects);
        }

        /// <summary>
        /// The unit's effects now, for the effect bar: the timed ones of the same item as one (the latest end; Tango
        /// eaten twice heals twice as much), then the auras it stands in (the same one from several allies as one).
        /// </summary>
        public void Effects(List<UnitEffect> into)
        {
            into.Clear();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var timed = new Dictionary<ItemDef, (int count, float until, float duration)>();
            foreach (var (item, until, duration) in TimedEffects())
            {
                timed.TryGetValue(item, out var t);
                timed[item] = (t.count + 1, Mathf.Max(t.until, until), Mathf.Max(t.duration, duration));
            }
            foreach (var (item, t) in timed)
            {
                var use = item.Active;
                string name = use != null ? item.Abilities.FirstOrDefault(a => a.active)?.name : null;
                float perSecond = use != null ? use.regenBonus * t.count : 0f;
                string text = perSecond > 0f ? $"+{perSecond.ToString("0.#", ci)} здоровья в секунду" : "";
                if (t.count > 1) text += $" ({t.count} одновременно)";
                into.Add(new UnitEffect(item, string.IsNullOrEmpty(name) ? item.displayName : name, text, t.until, t.duration, null));
            }
            // the same aura from several allies as one: its picture is the first giver's item (the own one first)
            AurasFrom(auras);
            foreach (var group in auras.GroupBy(a => AuraName(a.item)))
            {
                var givers = group.OrderBy(a => a.giver == this ? 0 : 1).ToList();
                float perSecond = givers.Sum(a => a.item.AuraManaRegen);
                string from = string.Join(", ", givers.Select(a =>
                    a.giver == this ? $"от своего {a.item.displayName}" : $"от {a.item.displayName} героя {a.giver.DisplayName}"));
                into.Add(new UnitEffect(givers[0].item, group.Key, $"+{perSecond.ToString("0.##", ci)} маны в секунду — {from}", 0f, 0f, givers[0].giver));
            }
        }

        readonly List<(Unit giver, ItemDef item)> auras = new();

        static string AuraName(ItemDef item) =>
            item.Abilities.FirstOrDefault(a => a.name != null && a.name.Contains("Aura"))?.name ?? item.displayName;

        /// <summary>
        /// The allies (itself included) whose items give this unit an aura now, one entry for each such item (see
        /// <see cref="AuraManaRegen"/>).
        /// </summary>
        public void AurasFrom(List<(Unit giver, ItemDef item)> into)
        {
            into.Clear();
            foreach (var ally in allUnits)
            {
                if (ally.team != team || !ally.IsAlive || ally.inventory == null) continue;
                foreach (var source in ally.inventory.AuraSourcesWithin((ally.transform.position - transform.position).magnitude))
                    into.Add((ally, source));
            }
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

        /// <summary>Back to full health and mana (a respawn).</summary>
        protected void RestoreHealth()
        {
            cleared = false;
            Health = maxHealth;
            Mana = lastMaxMana = MaxMana;
        }

        /// <summary>Mana the unit had as it died (what Ring of Basilius takes a share of); 0 before any death.</summary>
        public float ManaAtDeath { get; private set; }

        /// <summary>
        /// Mana per second the unit regenerated as it died, all of it (its own, its items', the auras it stood in): what
        /// mana vampirism adds once, as a plain number of mana; 0 before any death.
        /// </summary>
        public float ManaRegenAtDeath { get; private set; }

        /// <summary>Death (falling into the pit or damage): health and mana show as zero, the regeneration bonuses end.</summary>
        protected void ClearHealth()
        {
            if (!cleared) // a network client clears again with every state: the first one counts
            {
                ManaAtDeath = Mana;
                ManaRegenAtDeath = aliveManaRegen > 0f ? aliveManaRegen : ManaRegen; // as alive, its own aura still on
            }
            cleared = true;
            Health = 0f;
            Mana = 0f;
            if (regenBonuses.Count > 0) EffectsVersion++;
            regenBonuses.Clear();
        }

        /// <summary>Network client: the health the server keeps.</summary>
        public void ApplyRemoteHealth(float health) => Health = Mathf.Clamp(health, 0f, maxHealth);

        /// <summary>Network client: the regeneration per second the server counts (bonuses included).</summary>
        public void ApplyRemoteRegen(float regen) => remoteRegen = regen;

        /// <summary>Network client: mana, its maximum and its regeneration as the server counts them (items included).</summary>
        public void ApplyRemoteMana(float mana, float max, float regen)
        {
            remoteMaxMana = max;
            remoteManaRegen = regen;
            Mana = Mathf.Clamp(mana, 0f, max);
        }
    }
}
