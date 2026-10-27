using System;
using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Gold and items of a hero. Items with charges (Tango, Iron Branch) are used up and leave their slot; a stacking
    /// one bought again adds its charges to the slot it has. A usable item (<see cref="ItemDef.action"/>) is used on a
    /// tree or a point of the ground by <see cref="TryUse"/>. On a network client (<see cref="Simulation.IsProxy"/>)
    /// the server keeps them: a purchase or a use becomes a <see cref="BuyRequested"/> / <see cref="UseRequested"/>
    /// request and the result arrives with <see cref="ApplyRemoteState"/>.
    /// </summary>
    public class HeroInventory : MonoBehaviour
    {
        public const int SlotCount = 6;
        /// <summary>No tree targeted (a point of the ground, or nothing).</summary>
        public const int NoTree = -1;

        [SerializeField] int gold;
        [SerializeField] List<ItemDef> items = new();
        [SerializeField] List<int> charges = new();
        readonly List<float> readyAt = new(); // Time.time each slot can be used again

        public int Gold => gold;
        public IReadOnlyList<ItemDef> Items => items;
        public event Action Changed;
        /// <summary>Gold received with a popup (a kill reward): the amount, for the "+N" popup.</summary>
        public event Action<int> GoldAdded;
        /// <summary>Network client: the player wants to buy this item; the server decides.</summary>
        public event Action<ItemDef> BuyRequested;
        /// <summary>Network client: the player uses the item in a slot on a point or a tree; the server decides.</summary>
        public event Action<int, Vector3, int> UseRequested;
        /// <summary>An item was used (its slot before it was used up, the item).</summary>
        public event Action<int, ItemDef> Used;

        Unit self;

        void Awake()
        {
            self = GetComponent<Unit>();
            SyncSlots();
        }

        public float CastRangeBonus
        {
            get
            {
                float bonus = 0f;
                foreach (var item in items) bonus += item.castRangeBonus;
                return bonus;
            }
        }

        /// <summary>Net worth as in Dota: the gold in hand and what the items cost (charges by what is left).</summary>
        public int NetWorth
        {
            get
            {
                int worth = gold;
                for (int i = 0; i < items.Count; i++)
                    worth += items[i].charges > 0 ? items[i].cost * ChargesAt(i) / items[i].charges : items[i].cost;
                return worth;
            }
        }

        /// <param name="popup">false for quiet income (the gold trickle) that shows no "+N" and no coins.</param>
        public void AddGold(int amount, bool popup = true)
        {
            gold += amount;
            Changed?.Invoke();
            if (popup && amount > 0) GoldAdded?.Invoke(amount);
        }

        public bool Owns(ItemDef item) => items.Contains(item);

        /// <summary>Charges left in a slot (0 for an item that is not used up, or an empty slot).</summary>
        public int ChargesAt(int slot) => slot >= 0 && slot < charges.Count ? charges[slot] : 0;

        /// <summary>Seconds until the item in a slot can be used again.</summary>
        public float CooldownLeft(int slot) => slot >= 0 && slot < readyAt.Count ? Mathf.Max(0f, readyAt[slot] - Time.time) : 0f;

        /// <summary>Cooldown of the item in a slot, seconds (for the slot's shading).</summary>
        public float CooldownOf(int slot) => slot >= 0 && slot < items.Count ? items[slot].cooldown : 0f;

        public bool CanBuy(ItemDef item) =>
            item != null && gold >= item.cost && !(item.unique && Owns(item))
            && (items.Count < SlotCount || (item.stacks && Owns(item)));

        public bool TryBuy(ItemDef item)
        {
            if (!CanBuy(item)) return false;
            if (Simulation.IsProxy)
            {
                BuyRequested?.Invoke(item);
                return true;
            }
            gold -= item.cost;
            int owned = items.IndexOf(item);
            if (item.stacks && owned >= 0) charges[owned] += item.charges;
            else
            {
                items.Add(item);
                charges.Add(item.charges);
                readyAt.Add(0f);
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>The item in a slot can be used now (by a hero that is free to act).</summary>
        public bool CanUse(int slot) =>
            slot >= 0 && slot < items.Count && items[slot].IsUsable && CooldownLeft(slot) <= 0f
            && (items[slot].charges == 0 || ChargesAt(slot) > 0)
            && !(self is Pudge hero && !hero.IsControllable) && (self == null || self.IsAlive);

        /// <summary>
        /// Uses the item in <paramref name="slot"/>: on <paramref name="tree"/> (a <see cref="TerrainTrees"/> handle)
        /// for Tango and Quelling Blade, at <paramref name="point"/> for Iron Branch. The target must be within the
        /// item's reach. False if it cannot be used so.
        /// </summary>
        public bool TryUse(int slot, Vector3 point, int tree)
        {
            if (!CanUse(slot)) return false;
            var item = items[slot];
            var trees = TerrainTrees.Instance;
            if (trees == null) return false;
            if (item.TargetsTree ? !trees.IsStanding(tree) || !InReach(trees.Position(tree), trees.radius, item)
                    : !InReach(point, trees.radius, item) || !trees.CanPlant(point))
                return false;
            if (Simulation.IsProxy)
            {
                // the slot shades at once; the server's answer comes with the items
                readyAt[slot] = Time.time + item.cooldown;
                UseRequested?.Invoke(slot, point, tree);
                return true;
            }
            switch (item.action)
            {
                case ItemAction.EatTree:
                    bool planted = TerrainTrees.IsPlanted(tree);
                    trees.Fell(tree, transform.position);
                    if (self != null) self.AddRegen(item.regenBonus * (planted ? item.plantedTreeBonus : 1f), item.regenDuration);
                    break;
                case ItemAction.ChopTree:
                    trees.Fell(tree, transform.position);
                    break;
                case ItemAction.PlantTree:
                    if (trees.Plant(point, item.treeLifetime) < 0) return false;
                    break;
            }
            readyAt[slot] = Time.time + item.cooldown;
            if (item.charges > 0 && --charges[slot] <= 0)
            {
                items.RemoveAt(slot);
                charges.RemoveAt(slot);
                readyAt.RemoveAt(slot);
            }
            Used?.Invoke(slot, item);
            Changed?.Invoke();
            return true;
        }

        /// <summary>The target (a trunk of <paramref name="targetRadius"/>) is within the item's reach of the hero's edge.</summary>
        public bool InReach(Vector3 target, float targetRadius, ItemDef item)
        {
            var d = target - transform.position;
            d.y = 0f;
            float heroRadius = self != null ? self.radius : 0f;
            return d.magnitude - heroRadius - targetRadius <= item.castRange;
        }

        /// <summary>Network client: gold, items and their charges as the server has them.</summary>
        public void ApplyRemoteState(int gold, IEnumerable<(ItemDef item, int charges)> owned)
        {
            this.gold = gold;
            var oldItems = new List<ItemDef>(items);
            var oldReady = new List<float>(readyAt);
            items.Clear();
            charges.Clear();
            readyAt.Clear();
            foreach (var (item, left) in owned)
            {
                // a slot keeps its shading if the same item is still there
                int was = oldItems.IndexOf(item);
                items.Add(item);
                charges.Add(left);
                readyAt.Add(was >= 0 ? oldReady[was] : 0f);
                if (was >= 0) oldItems[was] = null;
            }
            Changed?.Invoke();
        }

        /// <summary>Network client: the server gave this hero gold with a popup.</summary>
        public void ShowGoldAdded(int amount) => GoldAdded?.Invoke(amount);

        /// <summary>Charges and cooldowns for every slot (items may be set in the inspector or by older saves).</summary>
        void SyncSlots()
        {
            while (charges.Count < items.Count) charges.Add(items[charges.Count].charges);
            if (charges.Count > items.Count) charges.RemoveRange(items.Count, charges.Count - items.Count);
            while (readyAt.Count < items.Count) readyAt.Add(0f);
        }
    }
}
