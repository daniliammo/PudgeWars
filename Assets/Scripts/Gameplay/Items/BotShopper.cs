using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// A bot's items, the strongest way for now: it shops and uses what it bought.
    /// Shopping goes down a build order (<see cref="catalog"/>): the first item not owned yet is the goal. The bot buys
    /// it whole once it can afford what is still missing, otherwise the missing part worth the most to it now — mana
    /// regeneration per gold first (its hook lives on mana), the cheaper first among equals; a recipe only with the item it
    /// completes. With the build done it stacks <see cref="extras"/> (their regeneration, auras and vampirism add up)
    /// while a slot stays free for <see cref="emergency"/>. When the hook waits for mana longer than
    /// <see cref="manaWaitLimit"/> it buys that emergency item (Enchanted Mango), one at a time, unless the goal can
    /// be bought right now. It shops dead or alive, as a player can.
    /// Using: the mango is eaten as soon as the hook is ready but short of mana; Arcane Boots are used when their mana
    /// would not go to waste — the bot's own missing mana takes most of it, or allies around miss enough together.
    /// </summary>
    public class BotShopper : MonoBehaviour
    {
        [Tooltip("Build order: the items the bot buys one after another (assembled ones piece by piece).")]
        public ItemDef[] catalog = new ItemDef[0];
        [Tooltip("Bought one at a time once the build is done, while a slot stays free (Ring of Basilius, Void Stone…).")]
        public ItemDef[] extras = new ItemDef[0];
        [Tooltip("Bought when the hook would wait too long for its mana: something eaten for mana (Enchanted Mango).")]
        public ItemDef emergency;
        [Tooltip("The hook waiting this many seconds for its mana makes the bot buy the emergency item.")]
        public float manaWaitLimit = 6f;
        [Tooltip("Arcane Boots are used once this share of their mana would go to the bot itself and its allies around.")]
        [Range(0f, 1f)] public float restoreUsefulShare = 0.8f;
        [Tooltip("Seconds between shopping checks.")] public float checkInterval = 1f;

        HeroInventory inventory;
        Pudge self;
        HookAbility hook;
        float nextCheck;

        /// <summary>The item the bot is buying now (null once it has them all).</summary>
        public ItemDef Goal
        {
            get
            {
                if (inventory == null) return null;
                foreach (var item in catalog)
                    if (item != null && !inventory.Owns(item)) return item;
                return null;
            }
        }

        void Awake()
        {
            inventory = GetComponent<HeroInventory>();
            self = GetComponent<Pudge>();
            hook = GetComponent<HookAbility>();
        }

        void Update()
        {
            if (Simulation.IsProxy || inventory == null) return;
            UseItems();
            // a shorter interval set meanwhile counts at once, not after the check already planned
            nextCheck = Mathf.Min(nextCheck, Time.time + checkInterval);
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;
            Shop();
        }

        /// <summary>One shopping round: the goal (or its best part), the emergency mana, the extras.</summary>
        public void Shop()
        {
            var goal = Goal;
            // the goal first when it can be had right now: nothing else delays it
            if (goal != null && inventory.CanBuy(goal) && inventory.TryBuy(goal)) return;
            if (NeedsEmergency() && inventory.TryBuy(emergency)) return;
            if (goal != null)
            {
                BuyToward(goal);
                return;
            }
            if (FreeSlots() <= 1) return; // the last slot is kept for the emergency item
            foreach (var extra in extras)
                if (extra != null && BuyToward(extra)) return;
        }

        int FreeSlots()
        {
            int free = 0;
            for (int i = 0; i < HeroInventory.SlotCount; i++)
                if (inventory.ItemIn(i) == null) free++;
            return free;
        }

        /// <summary>The hook is short of mana for longer than <see cref="manaWaitLimit"/> and no emergency item is at hand.</summary>
        bool NeedsEmergency()
        {
            if (emergency == null || hook == null || self == null || inventory.Owns(emergency)) return false;
            float missing = hook.manaCost - self.Mana;
            if (missing <= 0f) return false;
            float regen = Mathf.Max(0.01f, self.ManaRegen);
            return missing / regen > manaWaitLimit;
        }

        /// <summary>
        /// Buys the item whole if it can, or else its most useful missing part it can afford (down the tree; an
        /// assembled part whole if it can, or its own best part). A recipe comes only with the item it completes.
        /// </summary>
        bool BuyToward(ItemDef item)
        {
            if (inventory.TryBuy(item)) return true;
            if (!item.IsAssembled) return false;
            var missing = inventory.MissingParts(item);
            var parts = item.components
                .Where(p => p != null && !p.recipe && (p.IsAssembled ? !inventory.Owns(p) : missing.Contains(p)))
                .Distinct()
                .OrderByDescending(Worth)
                .ThenBy(p => inventory.PriceOf(p));
            foreach (var part in parts)
                if (BuyToward(part)) return true;
            return false;
        }

        /// <summary>What a part is worth to the bot per gold: mana regeneration most, then mana, then the rest.</summary>
        static float Worth(ItemDef item)
        {
            float cost = Mathf.Max(1f, item.cost);
            return (item.ManaRegenBonus * 1000f + item.KillManaSteal * 2000f + item.AuraManaRegen * 600f + item.ManaBonus * 3f
                    + item.CastRangeBonus * 300f + item.MoveSpeedBonus * 400f + item.HeroDamageBonus * 5f) / cost;
        }

        /// <summary>Eats the mango when the hook is ready but short of mana; uses Arcane Boots when their mana is not wasted.</summary>
        void UseItems()
        {
            if (self == null || !self.IsControllable) return;
            for (int slot = 0; slot < HeroInventory.SlotCount; slot++)
            {
                var item = inventory.ItemIn(slot);
                var use = item != null ? item.Active : null;
                if (use == null || !inventory.CanUse(slot)) continue;
                switch (use.action)
                {
                    case ItemAction.RestoreMana:
                        // ready but short of mana, and not about to have it anyway
                        if (hook != null && hook.IsReady && !hook.HasMana
                            && (hook.manaCost - self.Mana) / Mathf.Max(0.01f, self.ManaRegen) > 1.5f)
                            inventory.TryUse(slot, transform.position, HeroInventory.NoTree);
                        break;
                    case ItemAction.RestoreAllyMana:
                        if (RestoreIsUseful(use)) inventory.TryUse(slot, transform.position, HeroInventory.NoTree);
                        break;
                }
            }
        }

        /// <summary>
        /// The mana Arcane Boots give would mostly land: the bot's own missing mana takes most of it, or the allies
        /// within reach miss at least as much together.
        /// </summary>
        bool RestoreIsUseful(ItemDef use)
        {
            float useful = 0f;
            foreach (var unit in Unit.AllUnits)
            {
                if (unit.team != self.team || !unit.IsAlive) continue;
                var d = unit.transform.position - transform.position;
                d.y = 0f;
                if (d.magnitude > use.castRange) continue;
                useful += Mathf.Min(use.restoreMana, unit.MaxMana - unit.Mana);
            }
            float own = Mathf.Min(use.restoreMana, self.MaxMana - self.Mana);
            return own >= use.restoreMana * restoreUsefulShare || useful >= use.restoreMana * (1f + restoreUsefulShare);
        }
    }
}
