using System;
using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Gold and items of a hero in six slots (Z X C V B N). Usable items keep to the left, nearest Z, passive ones
    /// (Aether Lens) to the right, out of the way of the keys: when an item is used up or sold, the others close up
    /// (<see cref="Compact"/>). A stacking item bought again adds its charges to the slot it has. An item sells for
    /// half its cost (of the charges left, <see cref="SellValue"/>). A usable item (<see cref="ItemDef.action"/>) is used on a
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
        [SerializeField] ItemDef[] slots = new ItemDef[SlotCount];
        [SerializeField] int[] charges = new int[SlotCount];
        readonly float[] readyAt = new float[SlotCount]; // Time.time each slot can be used again
        readonly List<ItemDef> owned = new();

        public int Gold => gold;
        /// <summary>The items owned, in slot order (empty slots left out).</summary>
        public IReadOnlyList<ItemDef> Items => owned;
        /// <summary>The item in a slot, or null.</summary>
        public ItemDef ItemIn(int slot) => slot >= 0 && slot < SlotCount ? slots[slot] : null;
        /// <summary>The slot holding an item, or -1.</summary>
        public int SlotOf(ItemDef item) => item != null ? Array.IndexOf(slots, item) : -1;
        public event Action Changed;
        /// <summary>Gold received with a popup (a kill reward): the amount, for the "+N" popup.</summary>
        public event Action<int> GoldAdded;
        /// <summary>Network client: the player wants to buy this item; the server decides.</summary>
        public event Action<ItemDef> BuyRequested;
        /// <summary>Network client: the player uses the item in a slot on a point or a tree; the server decides.</summary>
        public event Action<int, Vector3, int> UseRequested;
        /// <summary>An item was used (its slot before it was used up, the item).</summary>
        public event Action<int, ItemDef> Used;
        /// <summary>Network client: the player sells the item in a slot; the server decides.</summary>
        public event Action<int> SellRequested;

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
                foreach (var item in owned) bonus += item.castRangeBonus;
                return bonus;
            }
        }

        /// <summary>
        /// Extra damage the hero's hits deal to <paramref name="target"/> (Quelling Blade: +16 on heroes, +4 on creeps),
        /// for every attack, item and ability (the hook adds it to its catch and to every metre it drags).
        /// </summary>
        public float DamageBonusAgainst(Unit target)
        {
            if (target == null) return 0f;
            float bonus = 0f;
            foreach (var item in owned) bonus += target.IsHero ? item.heroDamageBonus : item.creepDamageBonus;
            return bonus;
        }

        /// <summary>Net worth as in Dota: the gold in hand and what the items cost (charges by what is left).</summary>
        public int NetWorth
        {
            get
            {
                int worth = gold;
                for (int i = 0; i < SlotCount; i++)
                    if (slots[i] != null)
                        worth += slots[i].charges > 0 ? slots[i].cost * charges[i] / slots[i].charges : slots[i].cost;
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

        public bool Owns(ItemDef item) => SlotOf(item) >= 0;

        /// <summary>Charges left in a slot (0 for an item that is not used up, or an empty slot).</summary>
        public int ChargesAt(int slot) => ItemIn(slot) != null ? charges[slot] : 0;

        /// <summary>Seconds until the item in a slot can be used again.</summary>
        public float CooldownLeft(int slot) => ItemIn(slot) != null ? Mathf.Max(0f, readyAt[slot] - Time.time) : 0f;

        /// <summary>Cooldown of the item in a slot, seconds (for the slot's shading).</summary>
        public float CooldownOf(int slot) => ItemIn(slot) != null ? slots[slot].cooldown : 0f;

        /// <summary>Where a new item would go: usable ones from the left, passive ones from the right; -1 if full.</summary>
        public int FreeSlotFor(ItemDef item)
        {
            for (int k = 0; k < SlotCount; k++)
            {
                int slot = item.IsUsable ? k : SlotCount - 1 - k;
                if (slots[slot] == null) return slot;
            }
            return -1;
        }

        public bool CanBuy(ItemDef item) =>
            item != null && gold >= item.cost && !(item.unique && Owns(item))
            && ((item.stacks && Owns(item)) || FreeSlotFor(item) >= 0);

        public bool TryBuy(ItemDef item)
        {
            if (!CanBuy(item)) return false;
            if (Simulation.IsProxy)
            {
                BuyRequested?.Invoke(item);
                return true;
            }
            gold -= item.cost;
            int had = SlotOf(item);
            if (item.stacks && had >= 0) charges[had] += item.charges;
            else
            {
                int slot = FreeSlotFor(item);
                slots[slot] = item;
                charges[slot] = item.charges;
                readyAt[slot] = 0f;
            }
            Reindex();
            Changed?.Invoke();
            return true;
        }

        /// <summary>The item in a slot can be used now (by a hero that is free to act).</summary>
        public bool CanUse(int slot) =>
            ItemIn(slot) is { IsUsable: true } item && CooldownLeft(slot) <= 0f && (item.charges == 0 || charges[slot] > 0)
            && !(self is Pudge hero && !hero.IsControllable) && (self == null || self.IsAlive);

        /// <summary>
        /// Uses the item in <paramref name="slot"/>: on <paramref name="tree"/> (a <see cref="TerrainTrees"/> handle)
        /// for Tango and Quelling Blade, at <paramref name="point"/> for Iron Branch. The target must be within the
        /// item's reach. False if it cannot be used so.
        /// </summary>
        public bool TryUse(int slot, Vector3 point, int tree)
        {
            if (!CanUse(slot)) return false;
            var item = slots[slot];
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
                slots[slot] = null;
                charges[slot] = 0;
                Compact();
            }
            Used?.Invoke(slot, item);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Gold the item in a slot sells for: half its cost, of the charges left for a used-up kind.</summary>
        public int SellValue(int slot)
        {
            var item = ItemIn(slot);
            if (item == null) return 0;
            int worth = item.charges > 0 ? item.cost * charges[slot] / item.charges : item.cost;
            return worth / 2;
        }

        /// <summary>Sells the item in a slot for <see cref="SellValue"/>; the others close up. False for an empty slot.</summary>
        public bool TrySell(int slot)
        {
            if (ItemIn(slot) == null) return false;
            if (Simulation.IsProxy)
            {
                SellRequested?.Invoke(slot);
                return true;
            }
            gold += SellValue(slot);
            slots[slot] = null;
            charges[slot] = 0;
            Compact();
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Usable items close up to the left (toward Z), passive ones to the right, each in its order; charges and
        /// cooldowns go with them.
        /// </summary>
        void Compact()
        {
            var items = new (ItemDef item, int charges, float readyAt)[SlotCount];
            int left = 0, right = SlotCount - 1;
            for (int i = 0; i < SlotCount; i++)
                if (slots[i] is { IsUsable: true }) items[left++] = (slots[i], charges[i], readyAt[i]);
            for (int i = SlotCount - 1; i >= 0; i--)
                if (slots[i] is { IsUsable: false }) items[right--] = (slots[i], charges[i], readyAt[i]);
            for (int i = 0; i < SlotCount; i++)
                (slots[i], charges[i], readyAt[i]) = items[i];
            Reindex();
        }

        /// <summary>The target (a trunk of <paramref name="targetRadius"/>) is within the item's reach of the hero's edge.</summary>
        public bool InReach(Vector3 target, float targetRadius, ItemDef item)
        {
            var d = target - transform.position;
            d.y = 0f;
            float heroRadius = self != null ? self.radius : 0f;
            return d.magnitude - heroRadius - targetRadius <= item.castRange;
        }

        /// <summary>Network client: gold, and slot by slot the items and their charges as the server has them.</summary>
        public void ApplyRemoteState(int gold, IEnumerable<(ItemDef item, int charges)> perSlot)
        {
            this.gold = gold;
            int slot = 0;
            foreach (var (item, left) in perSlot)
            {
                if (slot >= SlotCount) break;
                if (slots[slot] != item) readyAt[slot] = 0f; // a slot keeps its shading while the same item is there
                slots[slot] = item;
                charges[slot] = item != null ? left : 0;
                slot++;
            }
            for (; slot < SlotCount; slot++)
            {
                slots[slot] = null;
                charges[slot] = 0;
            }
            Reindex();
            Changed?.Invoke();
        }

        /// <summary>Network client: the server gave this hero gold with a popup.</summary>
        public void ShowGoldAdded(int amount) => GoldAdded?.Invoke(amount);

        /// <summary>Six slots and their charges whatever was serialized (items may be set in the inspector).</summary>
        void SyncSlots()
        {
            if (slots == null || slots.Length != SlotCount) Array.Resize(ref slots, SlotCount);
            if (charges == null || charges.Length != SlotCount) Array.Resize(ref charges, SlotCount);
            Reindex();
        }

        void Reindex()
        {
            owned.Clear();
            foreach (var item in slots)
                if (item != null) owned.Add(item);
        }
    }
}
