using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>What using an item does (most give only stats and are not used).</summary>
    public enum ItemAction
    {
        None,
        /// <summary>Tango: eats a tree (it is cut down) and regenerates health for a while.</summary>
        EatTree,
        /// <summary>Iron Branch: plants a tree (for good, or for a while).</summary>
        PlantTree,
        /// <summary>Quelling Blade: cuts a tree down.</summary>
        ChopTree,
    }

    /// <summary>The sounds of an item put into a slot and picked from it (Dota: Item.DropShop and its kinds).</summary>
    public enum ItemSound { Default, Ring, Gem, Recipe }

    /// <summary>A shop item, the stats it gives its owner and what using it does.</summary>
    [CreateAssetMenu(menuName = "Pudge Wars/Item", fileName = "Item")]
    public class ItemDef : ScriptableObject
    {
        public string displayName;
        [TextArea] public string description;
        [Tooltip("The full description shown when the item is hovered (in the shop and in a slot).")]
        [TextArea(3, 8)] public string details;
        public Sprite icon;
        [Tooltip("The sound of putting it into a slot and picking it from one: rings (Ring of Protection, Ring of Health…), Gem of True Sight and recipes have their own.")]
        public ItemSound sound;
        public int cost;
        [Tooltip("Bonus to ability range, metres (Dota units / 100).")]
        public float castRangeBonus;
        [Tooltip("A hero can own only one (Dota: cast range bonuses of the same item do not stack).")]
        public bool unique = true;
        [Tooltip("Extra damage of every hit (an attack, an item, an ability — the hook's catch and every metre it drags) on a hero.")]
        public float heroDamageBonus;
        [Tooltip("The same on a creep (any unit that is not a hero).")]
        public float creepDamageBonus;

        [Header("Use")]
        public ItemAction action;
        [Tooltip("Uses before the item is gone (Tango 3, Iron Branch 1); 0 — it is never used up.")]
        public int charges;
        [Tooltip("Buying it again adds its charges to the one already owned instead of taking a slot (Tango).")]
        public bool stacks;
        [Tooltip("Reach from the hero's edge to the target's (a tree's trunk), metres (Dota units / 100).")]
        public float castRange;
        [Tooltip("Seconds before it can be used again.")]
        public float cooldown;
        [Tooltip("Tango: health regenerated per second while the tree is eaten, and for how many seconds.")]
        public float regenBonus;
        public float regenDuration;
        [Tooltip("Tango: a planted tree (Iron Branch) feeds this many times more.")]
        public float plantedTreeBonus = 1f;
        [Tooltip("Iron Branch: seconds the planted tree stands; 0 — for good.")]
        public float treeLifetime;

        public bool IsUsable => action != ItemAction.None;
        /// <summary>Used on a tree (Tango, Quelling Blade) rather than on a point of the ground (Iron Branch).</summary>
        public bool TargetsTree => action is ItemAction.EatTree or ItemAction.ChopTree;
    }
}
