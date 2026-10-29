using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>Arcane Boots: at once, without a target, gives mana to the allies around (the owner too).</summary>
        RestoreAllyMana,
    }

    /// <summary>A named ability of an item, shown in its own frame in the description: blue for an active one, grey for a passive one.</summary>
    [Serializable]
    public class ItemAbility
    {
        [Tooltip("As in Dota: Aetheral Focus, Basilius Aura, Replenish Mana…")] public string name;
        [Tooltip("Used on a key (blue frame); otherwise it works by itself (grey frame).")] public bool active;
        [TextArea(2, 5)] public string description;
    }

    /// <summary>The sounds of an item put into a slot and picked from it (Dota: Item.DropShop and its kinds).</summary>
    public enum ItemSound { Default, Ring, Gem, Recipe }

    /// <summary>
    /// A shop item, the stats it gives its owner and what using it does. An assembled item (<see cref="components"/>,
    /// Aether Lens) is made of others, a recipe scroll among them, as in Dota: once its owner has them all in the slots
    /// they combine into it, and buying it buys only the parts still missing (<see cref="HeroInventory.PriceOf"/>).
    /// An assembled item inherits everything of its cheaper ancestors (its parts, their parts…): every bonus is at least
    /// what its parts give together (<see cref="ManaBonus"/> and the like — its own value wins when bigger), auras and
    /// mana vampirism the biggest one, a part's active use (<see cref="Active"/>) when it has none of its own.
    /// The gameplay reads these properties, not the fields.
    /// </summary>
    [CreateAssetMenu(menuName = "Pudge Wars/Item", fileName = "Item")]
    public class ItemDef : ScriptableObject
    {
        public string displayName;
        [TextArea] public string description;
        [Tooltip("More about the item, shown when it is hovered (in the shop and in a slot), under its bonuses.")]
        [TextArea(3, 8)] public string details;
        [Tooltip("Its abilities, each in its own frame in the description; an assembled item shows its parts' too (its own one of the same name wins).")]
        public ItemAbility[] abilities = Array.Empty<ItemAbility>();
        [Tooltip("A line of lore, in italics at the end of the description.")]
        [TextArea(2, 4)] public string lore;
        [Tooltip("A recipe scroll: not sold in the shop by itself, bought with the item it makes.")]
        public bool recipe;
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
        [Tooltip("Bonus to maximum mana (Energy Booster +250).")]
        public float manaBonus;
        [Tooltip("Bonus to mana regenerated per second (Void Stone +1.75).")]
        public float manaRegenBonus;
        [Tooltip("Bonus to movement speed, m/s (Dota units / 100: Boots of Speed +45 = 0.45). Other items' bonuses add up; of boots only the biggest counts.")]
        public float moveSpeedBonus;
        [Tooltip("Boots (Boots of Speed, Arcane Boots): their speed bonuses do not stack with each other, as in Dota.")]
        public bool boots;
        [Tooltip("Bonus to strength (Iron Branch +1). Stored only: strength, agility and intelligence do nothing yet.")]
        public float strengthBonus;
        [Tooltip("Bonus to agility (Slippers of Agility +3, Iron Branch +1). Stored only.")]
        public float agilityBonus;
        [Tooltip("Bonus to intelligence (Iron Branch +1). Stored only.")]
        public float intelligenceBonus;
        [Tooltip("Ring of Basilius: the share of a killed unit's mana (as it had at its death) its killer gets, 0.075 = 7.5 %.")]
        [Range(0f, 1f)] public float killManaSteal;
        [Tooltip("Basilius Aura: mana regenerated per second by every ally within auraRadius, the owner too. Several heroes' auras add up; one hero's several count once (the biggest).")]
        public float auraManaRegen;
        [Tooltip("Radius of the aura, metres from the owner's centre to the ally's.")]
        public float auraRadius;
        [Tooltip("Arcane Boots: mana given to every ally within castRange (from the owner's centre), the owner too.")]
        public float restoreMana;

        [Header("Crafting")]
        [Tooltip("What the item is assembled from, a recipe scroll too if it has one (a part may repeat). Its cost must be their sum.")]
        public ItemDef[] components = Array.Empty<ItemDef>();

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

        // inherited from the parts (see the summary); the fields are the item's own values
        public float ManaBonus => Inherited(i => i.manaBonus);
        public float ManaRegenBonus => Inherited(i => i.manaRegenBonus);
        public float MoveSpeedBonus => Inherited(i => i.moveSpeedBonus);
        public float AgilityBonus => Inherited(i => i.agilityBonus);
        public float StrengthBonus => Inherited(i => i.strengthBonus);
        public float IntelligenceBonus => Inherited(i => i.intelligenceBonus);
        public float CastRangeBonus => Inherited(i => i.castRangeBonus);
        public float HeroDamageBonus => Inherited(i => i.heroDamageBonus);
        public float CreepDamageBonus => Inherited(i => i.creepDamageBonus);
        public float KillManaSteal => Best(i => i.killManaSteal);
        /// <summary>Boots, or made of boots: the speed does not stack with other boots.</summary>
        public bool IsBoots => boots || Parts.Any(p => p.IsBoots);
        /// <summary>Basilius Aura of the item or of the part with the biggest one.</summary>
        public float AuraManaRegen => AuraSource != null ? AuraSource.auraManaRegen : 0f;
        public float AuraRadius => AuraSource != null ? AuraSource.auraRadius : 0f;

        /// <summary>
        /// What the item does when used: its own action, or the first part's (down the tree) that has one; null for a
        /// passive item. Its fields (castRange, cooldown, restoreMana…) are the use's numbers.
        /// </summary>
        public ItemDef Active => action != ItemAction.None ? this : Parts.Select(p => p.Active).FirstOrDefault(a => a != null);

        IEnumerable<ItemDef> Parts => components != null ? components.Where(c => c != null && c != this) : Enumerable.Empty<ItemDef>();

        /// <summary>The item's own value or what its parts give together, whichever is bigger.</summary>
        float Inherited(Func<ItemDef, float> own) => Mathf.Max(own(this), Parts.Sum(p => p.Inherited(own)));

        /// <summary>The biggest value of the item and its parts (what does not add up: an aura, vampirism).</summary>
        float Best(Func<ItemDef, float> own) => Parts.Select(p => p.Best(own)).DefaultIfEmpty(0f).Prepend(own(this)).Max();

        ItemDef AuraSource
        {
            get
            {
                ItemDef best = auraManaRegen > 0f ? this : null;
                foreach (var part in Parts)
                {
                    var aura = part.AuraSource;
                    if (aura != null && (best == null || aura.auraManaRegen > best.auraManaRegen)) best = aura;
                }
                return best;
            }
        }

        public bool IsUsable => Active != null;
        /// <summary>Made of other items (<see cref="components"/>).</summary>
        public bool IsAssembled => components != null && components.Length > 0;

        /// <summary>The assembled items loaded now (those the shop and the network catalog know).</summary>
        public static IEnumerable<ItemDef> Assembled() => Resources.FindObjectsOfTypeAll<ItemDef>().Where(i => i.IsAssembled);

        /// <summary>The item's abilities and those inherited from its parts (one per name, the item's own first).</summary>
        public IEnumerable<ItemAbility> Abilities
        {
            get
            {
                var seen = new HashSet<string>();
                foreach (var a in AllAbilities())
                    if (a != null && seen.Add(a.name ?? "")) yield return a;
            }
        }

        IEnumerable<ItemAbility> AllAbilities()
        {
            foreach (var a in abilities ?? Array.Empty<ItemAbility>()) yield return a;
            foreach (var part in Parts)
            foreach (var a in part.AllAbilities())
                yield return a;
        }

        /// <summary>The assembled items this one is a part of.</summary>
        public IEnumerable<ItemDef> BuildsInto() => Assembled().Where(r => r.components.Contains(this));
        /// <summary>Used at once on a key or a click, with no target (Arcane Boots).</summary>
        public bool IsInstant => Active is { action: ItemAction.RestoreAllyMana };
        /// <summary>Used on a tree (Tango, Quelling Blade) rather than on a point of the ground (Iron Branch).</summary>
        public bool TargetsTree => Active is { action: ItemAction.EatTree or ItemAction.ChopTree };
    }
}
