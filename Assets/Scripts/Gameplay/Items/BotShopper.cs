using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>Bot shopping: buys the first catalog item it can afford, checking now and then.</summary>
    public class BotShopper : MonoBehaviour
    {
        [Tooltip("Wish list in buying order (the arena shop's catalog).")]
        public ItemDef[] catalog = new ItemDef[0];
        [Tooltip("Seconds between shopping checks.")] public float checkInterval = 1f;

        HeroInventory inventory;
        Hookable self;
        float nextCheck;

        void Awake()
        {
            inventory = GetComponent<HeroInventory>();
            self = GetComponent<Hookable>();
        }

        void Update()
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;
            if (inventory == null || (self != null && !self.IsAlive)) return;
            foreach (var item in catalog)
                if (inventory.TryBuy(item)) return;
        }
    }
}
