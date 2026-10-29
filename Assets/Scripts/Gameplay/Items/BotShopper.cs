using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Bot shopping: goes down a build order of items (<see cref="catalog"/>), checking now and then. The first item it
    /// does not own yet is the goal: the bot buys it whole if it can, otherwise one of its missing parts it can afford
    /// (an assembled item comes piece by piece, as in Dota; a recipe only with the item it completes), and saves up for
    /// the rest before the next goal.
    /// </summary>
    public class BotShopper : MonoBehaviour
    {
        [Tooltip("Build order: the items the bot buys one after another (assembled ones piece by piece).")]
        public ItemDef[] catalog = new ItemDef[0];
        [Tooltip("Seconds between shopping checks.")] public float checkInterval = 1f;

        HeroInventory inventory;
        Pudge self;
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
        }

        void Update()
        {
            if (Simulation.IsProxy || Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;
            if (inventory == null || (self != null && !self.IsAlive)) return;
            var goal = Goal;
            if (goal != null) BuyToward(goal, inventory.MissingParts(goal));
        }

        /// <summary>
        /// Buys the item whole, or else a piece of it still <paramref name="missing"/>: down its tree, the first part
        /// (an assembled one whole) it can afford. A recipe comes only with the item it completes, as in the shop.
        /// </summary>
        bool BuyToward(ItemDef item, List<ItemDef> missing)
        {
            if (inventory.TryBuy(item)) return true;
            if (!item.IsAssembled) return false;
            foreach (var part in item.components)
            {
                if (part == null || part.recipe) continue;
                bool needed = part.IsAssembled ? !inventory.Owns(part) : missing.Contains(part);
                if (needed && BuyToward(part, missing)) return true;
            }
            return false;
        }
    }
}
