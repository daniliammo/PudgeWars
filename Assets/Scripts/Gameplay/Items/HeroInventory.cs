using System;
using System.Collections.Generic;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>Gold and items of a hero.</summary>
    public class HeroInventory : MonoBehaviour
    {
        public const int SlotCount = 6;

        [SerializeField] int gold;
        [SerializeField] List<ItemDef> items = new();

        public int Gold => gold;
        public IReadOnlyList<ItemDef> Items => items;
        public event Action Changed;
        /// <summary>Gold received with a popup (a kill reward): the amount, for the "+N" popup.</summary>
        public event Action<int> GoldAdded;

        public float CastRangeBonus
        {
            get
            {
                float bonus = 0f;
                foreach (var item in items) bonus += item.castRangeBonus;
                return bonus;
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

        public bool CanBuy(ItemDef item) =>
            item != null && gold >= item.cost && items.Count < SlotCount && !(item.unique && Owns(item));

        public bool TryBuy(ItemDef item)
        {
            if (!CanBuy(item)) return false;
            gold -= item.cost;
            items.Add(item);
            Changed?.Invoke();
            return true;
        }
    }
}
