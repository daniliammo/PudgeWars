using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Gold and items of a hero in six slots (Z X C V B N). Usable items keep to the left, nearest Z, passive ones
    /// (Aether Lens) to the right, out of the way of the keys: when an item is used up or sold, the others close up
    /// (<see cref="Compact"/>). A stacking item bought again adds its charges to the slot it has. An item sells for
    /// half its cost (of the charges left, <see cref="SellValue"/>). A usable item (<see cref="ItemDef.action"/>) is used on a
    /// tree or a point of the ground by <see cref="TryUse"/>. The player can rearrange the slots (<see cref="TryMove"/>);
    /// from then on the others no longer close up: a used-up or sold item leaves its slot empty, as in Dota, until the
    /// inventory is empty again. Items assembled from others combine as in Dota: as soon as all the parts of an
    /// assembled item (<see cref="ItemDef.components"/>, a recipe among them) are in the slots, they become that item in
    /// the slot of the first part; buying an assembled item buys only its missing parts, for what they cost
    /// (<see cref="PriceOf"/>), and a part that completes an item can be bought even into a full inventory.
    /// On a network client (<see cref="Simulation.IsProxy"/>)
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
        /// <summary>Any hero used an item (its inventory, the item): for the effects; on a network client too (<see cref="ShowUsed"/>).</summary>
        public static event Action<HeroInventory, ItemDef> AnyUsed;
        /// <summary>Network client: the player sells the item in a slot; the server decides.</summary>
        public event Action<int> SellRequested;
        /// <summary>An item went from one slot to another (what was there went the other way).</summary>
        public event Action<int, int> Moved;
        /// <summary>Network client: the player moved an item between slots (shown at once); the server does the same.</summary>
        public event Action<int, int> MoveRequested;

        Unit self;
        bool arranged; // the player has put items where it wants them: Compact keeps its order

        void Awake()
        {
            self = GetComponent<Unit>();
            SyncSlots();
            if (!Simulation.IsProxy) Combine(); // parts set in the inspector combine too
        }

        /// <summary>Bonus to maximum mana from the items (Energy Booster).</summary>
        public float ManaBonus
        {
            get
            {
                float bonus = 0f;
                foreach (var item in owned) bonus += item.ManaBonus;
                return bonus;
            }
        }

        /// <summary>Movement speed bonus from the items, m/s: of boots only the biggest one (as in Dota), the rest add up.</summary>
        public float MoveSpeedBonus
        {
            get
            {
                float boots = 0f, other = 0f;
                foreach (var item in owned)
                    if (item.IsBoots) boots = Mathf.Max(boots, item.MoveSpeedBonus);
                    else other += item.MoveSpeedBonus;
                return boots + other;
            }
        }

        /// <summary>Strength from the items (stored only: the attributes do nothing yet).</summary>
        public float StrengthBonus
        {
            get
            {
                float bonus = 0f;
                foreach (var item in owned) bonus += item.StrengthBonus;
                return bonus;
            }
        }

        /// <summary>Intelligence from the items (stored only: the attributes do nothing yet).</summary>
        public float IntelligenceBonus
        {
            get
            {
                float bonus = 0f;
                foreach (var item in owned) bonus += item.IntelligenceBonus;
                return bonus;
            }
        }

        /// <summary>Agility from the items (stored only: the attributes do nothing yet).</summary>
        public float AgilityBonus
        {
            get
            {
                float bonus = 0f;
                foreach (var item in owned) bonus += item.AgilityBonus;
                return bonus;
            }
        }

        /// <summary>The biggest share of a killed unit's mana the items give the killer (Ring of Basilius: 7.5 %).</summary>
        public float KillManaSteal
        {
            get
            {
                float best = 0f;
                foreach (var item in owned) best = Mathf.Max(best, item.KillManaSteal);
                return best;
            }
        }

        /// <summary>The item with the biggest Basilius Aura reaching <paramref name="distance"/> metres (null if none does).</summary>
        public ItemDef AuraSourceWithin(float distance)
        {
            ItemDef best = null;
            foreach (var item in owned)
                if (item.AuraManaRegen > 0f && distance <= item.AuraRadius && (best == null || item.AuraManaRegen > best.AuraManaRegen)) best = item;
            return best;
        }

        /// <summary>The biggest Basilius Aura of the items reaching <paramref name="distance"/> metres (0 if none does).</summary>
        public float AuraManaRegenWithin(float distance)
        {
            float best = 0f;
            foreach (var item in owned)
                if (item.AuraManaRegen > best && distance <= item.AuraRadius) best = item.AuraManaRegen;
            return best;
        }

        /// <summary>Bonus to mana regeneration from the items (Void Stone).</summary>
        public float ManaRegenBonus
        {
            get
            {
                float bonus = 0f;
                foreach (var item in owned) bonus += item.ManaRegenBonus;
                return bonus;
            }
        }

        public float CastRangeBonus
        {
            get
            {
                float bonus = 0f;
                foreach (var item in owned) bonus += item.CastRangeBonus;
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
            foreach (var item in owned) bonus += target.IsHero ? item.HeroDamageBonus : item.CreepDamageBonus;
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
        public float CooldownOf(int slot) => ItemIn(slot)?.Active is { } use ? use.cooldown : 0f;

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

        /// <summary>
        /// What buying the item costs this hero: its price, or for an assembled one the price of the parts it does not
        /// have yet — down through the parts' own parts (Sage's Mask counts toward Arcane Boots through Ring of
        /// Basilius); the owned parts go into it.
        /// </summary>
        public int PriceOf(ItemDef item)
        {
            if (item == null) return 0;
            return item.IsAssembled ? Plan(item, new bool[SlotCount], new List<int>()) : item.cost;
        }

        /// <summary>
        /// The price of the missing parts of an assembled item; the slots of the owned ones (its parts' parts too) go
        /// into <paramref name="consumed"/>, the missing simple parts into <paramref name="missing"/> (if given).
        /// </summary>
        int Plan(ItemDef item, bool[] used, List<int> consumed, List<ItemDef> missing = null)
        {
            int price = 0;
            foreach (var part in item.components)
            {
                if (part == null) continue;
                int slot = FindPart(part, used);
                if (slot >= 0) consumed.Add(slot);
                else if (part.IsAssembled) price += Plan(part, used, consumed, missing);
                else
                {
                    price += part.cost;
                    missing?.Add(part);
                }
            }
            return price;
        }

        /// <summary>
        /// The simple parts (no parts of their own) still to buy for an item, in its build order; the item itself for a
        /// simple one not owned (a bot buys an assembled item piece by piece).
        /// </summary>
        public List<ItemDef> MissingParts(ItemDef item)
        {
            var missing = new List<ItemDef>();
            if (item == null) return missing;
            if (item.IsAssembled) Plan(item, new bool[SlotCount], new List<int>(), missing);
            else if (!Owns(item)) missing.Add(item);
            return missing;
        }

        /// <summary>Slots of the owned items that would go into an assembled one bought now (its parts' parts too).</summary>
        List<int> PartSlots(ItemDef item)
        {
            var consumed = new List<int>();
            Plan(item, new bool[SlotCount], consumed);
            return consumed;
        }

        /// <summary>Slots of the item's own parts in the inventory, one per part (a missing part is left out).</summary>
        List<int> DirectPartSlots(ItemDef item)
        {
            var found = new List<int>();
            var used = new bool[SlotCount];
            foreach (var part in item.components)
            {
                int slot = part != null ? FindPart(part, used) : -1;
                if (slot >= 0) found.Add(slot);
            }
            return found;
        }

        /// <summary>A slot with <paramref name="part"/> not counted yet (marked now), or -1.</summary>
        int FindPart(ItemDef part, bool[] used)
        {
            for (int i = 0; i < SlotCount; i++)
                if (!used[i] && slots[i] == part)
                {
                    used[i] = true;
                    return i;
                }
            return -1;
        }

        /// <summary>An assembled item the owned items and <paramref name="added"/> would make (null if none).</summary>
        ItemDef CompletedBy(ItemDef added)
        {
            foreach (var result in added.BuildsInto())
            {
                var used = new bool[SlotCount];
                int missing = 0;
                foreach (var part in result.components)
                    if (part != null && FindPart(part, used) < 0) missing++;
                // the added item is the one part still missing (it is among the parts: BuildsInto)
                if (missing == 1) return result;
            }
            return null;
        }

        public bool CanBuy(ItemDef item)
        {
            if (item == null || gold < PriceOf(item) || (item.unique && Owns(item))) return false;
            if (item.stacks && Owns(item)) return true;
            if (FreeSlotFor(item) >= 0) return true;
            // a full inventory: an assembled item takes a slot of its parts, a part completing one goes into it
            return item.IsAssembled ? PartSlots(item).Count > 0 : CompletedBy(item) != null;
        }

        public bool TryBuy(ItemDef item)
        {
            if (!CanBuy(item)) return false;
            if (Simulation.IsProxy)
            {
                BuyRequested?.Invoke(item);
                return true;
            }
            gold -= PriceOf(item);
            int had = SlotOf(item);
            if (item.stacks && had >= 0) charges[had] += item.charges;
            else if (item.IsAssembled) Assemble(item, PartSlots(item));
            else if (FreeSlotFor(item) is var slot and >= 0) Put(slot, item);
            else
            {
                // no room, but it completes an assembled item: it goes straight into that
                var result = CompletedBy(item);
                Assemble(result, DirectPartSlots(result));
            }
            Reindex();
            Combine();
            Compact(); // an assembled item leaves its parts' other slots empty
            Changed?.Invoke();
            return true;
        }

        void Put(int slot, ItemDef item)
        {
            slots[slot] = item;
            charges[slot] = item.charges;
            readyAt[slot] = 0f;
        }

        /// <summary>The parts in <paramref name="partSlots"/> become <paramref name="result"/>, in the first of their slots.</summary>
        void Assemble(ItemDef result, List<int> partSlots)
        {
            foreach (var slot in partSlots)
            {
                slots[slot] = null;
                charges[slot] = 0;
            }
            int target = partSlots.Count > 0 ? partSlots.Min() : FreeSlotFor(result);
            if (target >= 0) Put(target, result);
            Reindex();
        }

        /// <summary>
        /// Every assembled item whose parts are all in the slots is made of them (one may lead to the next): always on,
        /// for every item, whatever brought the parts (a purchase, a move, the inspector).
        /// </summary>
        void Combine()
        {
            bool made, any = false;
            do
            {
                made = false;
                foreach (var item in owned.ToArray())
                {
                    foreach (var result in item.BuildsInto())
                    {
                        var parts = DirectPartSlots(result);
                        if (parts.Count != result.components.Count(c => c != null)) continue;
                        Assemble(result, parts);
                        made = any = true;
                        break;
                    }
                    if (made) break;
                }
            } while (made);
            if (any) Compact(); // the parts' other slots
        }

        /// <summary>The item in a slot can be used now (by a hero that is free to act).</summary>
        public bool CanUse(int slot) =>
            ItemIn(slot) is { IsUsable: true } item && CooldownLeft(slot) <= 0f && (item.charges == 0 || charges[slot] > 0)
            && !(self is Pudge hero && !hero.IsControllable) && (self == null || self.IsAlive);

        /// <summary>
        /// Uses the item in <paramref name="slot"/>: on <paramref name="tree"/> (a <see cref="TerrainTrees"/> handle)
        /// for Tango and Quelling Blade, at <paramref name="point"/> for Iron Branch, at once for an item without a
        /// target (Arcane Boots: the point and the tree are ignored). The target must be within the item's reach.
        /// False if it cannot be used so.
        /// </summary>
        public bool TryUse(int slot, Vector3 point, int tree)
        {
            if (!CanUse(slot)) return false;
            var item = slots[slot];
            var use = item.Active; // the item's own use or one inherited from a part
            var trees = TerrainTrees.Instance;
            if (!item.IsInstant)
            {
                if (trees == null) return false;
                if (item.TargetsTree ? !trees.IsStanding(tree) || !InReach(trees.Position(tree), trees.radius, use)
                        : !InReach(point, trees.radius, use) || !trees.CanPlant(point))
                    return false;
            }
            if (Simulation.IsProxy)
            {
                // the slot shades at once; the server's answer comes with the items
                readyAt[slot] = Time.time + use.cooldown;
                UseRequested?.Invoke(slot, point, tree);
                return true;
            }
            switch (use.action)
            {
                case ItemAction.EatTree:
                    bool planted = TerrainTrees.IsPlanted(tree);
                    trees.Fell(tree, transform.position);
                    if (self != null) self.AddRegen(use.regenBonus * (planted ? use.plantedTreeBonus : 1f), use.regenDuration, item);
                    break;
                case ItemAction.ChopTree:
                    trees.Fell(tree, transform.position);
                    break;
                case ItemAction.PlantTree:
                    if (trees.Plant(point, use.treeLifetime) < 0) return false;
                    break;
                case ItemAction.RestoreAllyMana:
                    foreach (var unit in Unit.AllUnits)
                    {
                        var d = unit.transform.position - transform.position;
                        d.y = 0f;
                        if (self != null && unit.team == self.team && d.magnitude <= use.castRange) unit.RestoreMana(use.restoreMana);
                    }
                    break;
            }
            readyAt[slot] = Time.time + use.cooldown;
            if (item.charges > 0 && --charges[slot] <= 0)
            {
                slots[slot] = null;
                charges[slot] = 0;
                Compact();
            }
            Used?.Invoke(slot, item);
            AnyUsed?.Invoke(this, item);
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
            int value = SellValue(slot);
            gold += value;
            slots[slot] = null;
            charges[slot] = 0;
            Compact();
            Changed?.Invoke();
            if (value > 0) GoldAdded?.Invoke(value); // the sale's price pops up over the hero, as a reward does
            return true;
        }

        /// <summary>
        /// Moves the item in slot <paramref name="from"/> to <paramref name="to"/>; the item there, if any, takes its
        /// place. Charges and cooldowns go with the items. False for an empty slot or the same one.
        /// </summary>
        public bool TryMove(int from, int to)
        {
            if (from == to || ItemIn(from) == null || to < 0 || to >= SlotCount) return false;
            (slots[from], slots[to]) = (slots[to], slots[from]);
            (charges[from], charges[to]) = (charges[to], charges[from]);
            (readyAt[from], readyAt[to]) = (readyAt[to], readyAt[from]);
            arranged = true;
            Reindex();
            // a network client shows the move at once, the server's state brings the same (and combines)
            if (Simulation.IsProxy) MoveRequested?.Invoke(from, to);
            else Combine();
            Moved?.Invoke(from, to);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Usable items close up to the left (toward Z), passive ones to the right, each in its order; charges and
        /// cooldowns go with them. Slots the player has arranged stay as they are.
        /// </summary>
        void Compact()
        {
            if (AllEmpty()) arranged = false; // an empty inventory is arranged anew
            if (arranged)
            {
                Reindex();
                return;
            }
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
            var use = item != null ? item.Active : null;
            return use != null && d.magnitude - heroRadius - targetRadius <= use.castRange;
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

        /// <summary>Network client: the server's hero used <paramref name="item"/> (its effect plays here too).</summary>
        public void ShowUsed(ItemDef item) => AnyUsed?.Invoke(this, item);

        /// <summary>Network client: the server gave this hero gold with a popup.</summary>
        public void ShowGoldAdded(int amount) => GoldAdded?.Invoke(amount);

        /// <summary>Six slots and their charges whatever was serialized (items may be set in the inspector).</summary>
        void SyncSlots()
        {
            if (slots == null || slots.Length != SlotCount) Array.Resize(ref slots, SlotCount);
            if (charges == null || charges.Length != SlotCount) Array.Resize(ref charges, SlotCount);
            Reindex();
        }

        bool AllEmpty() => Array.TrueForAll(slots, s => s == null);

        void Reindex()
        {
            owned.Clear();
            foreach (var item in slots)
                if (item != null) owned.Add(item);
        }
    }
}
