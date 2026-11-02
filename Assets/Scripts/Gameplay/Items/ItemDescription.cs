using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>Shared item numbers for the shop, inventory and knowledge base.</summary>
    public static class ItemDescription
    {
        /// <summary>The plain bonuses, one a line ("+250 к мане"); what an ability frame tells is left to it.</summary>
        public static string Bonuses(ItemDef item, bool hasAbilities)
        {
            var ci = CultureInfo.InvariantCulture;
            var lines = new List<string>();
            if (item.ManaBonus > 0f) lines.Add($"+{item.ManaBonus.ToString("0", ci)} к мане");
            if (item.ManaRegenBonus > 0f) lines.Add($"+{item.ManaRegenBonus.ToString("0.##", ci)} к восстановлению маны");
            if (item.MoveSpeedBonus > 0f) lines.Add($"+{(item.MoveSpeedBonus * 100f).ToString("0", ci)} к скорости передвижения");
            if (item.StrengthBonus > 0f && Mathf.Approximately(item.StrengthBonus, item.AgilityBonus) && Mathf.Approximately(item.AgilityBonus, item.IntelligenceBonus))
                lines.Add($"+{item.StrengthBonus.ToString("0", ci)} ко всем атрибутам");
            else
            {
                if (item.StrengthBonus > 0f) lines.Add($"+{item.StrengthBonus.ToString("0", ci)} к силе");
                if (item.AgilityBonus > 0f) lines.Add($"+{item.AgilityBonus.ToString("0", ci)} к ловкости");
                if (item.IntelligenceBonus > 0f) lines.Add($"+{item.IntelligenceBonus.ToString("0", ci)} к интеллекту");
            }
            if (!hasAbilities) // an item without named abilities still tells what it does
            {
                if (item.CastRangeBonus > 0f) lines.Add($"+{(item.CastRangeBonus * 100f).ToString("0", ci)} к дальности способностей");
                if (item.HeroDamageBonus > 0f) lines.Add($"+{item.HeroDamageBonus.ToString("0", ci)} урона по героям");
                if (item.CreepDamageBonus > 0f) lines.Add($"+{item.CreepDamageBonus.ToString("0", ci)} урона по крипам");
                if (item.AuraManaRegen > 0f) lines.Add($"Аура: +{item.AuraManaRegen.ToString("0.##", ci)} маны в секунду союзникам в радиусе {(item.AuraRadius * 100f).ToString("0", ci)}");
                if (item.KillManaSteal > 0f) lines.Add($"Вампиризм маны: {(item.KillManaSteal * 100f).ToString("0.#", ci)} % маны убитого и сверх того его реген маны в секунду");
            }
            return string.Join("\n", lines);
        }

        /// <summary>The use's numbers under the active frame: reach, cooldown, charges.</summary>
        public static string UseNumbers(ItemDef item, HeroInventory owner = null, int slot = -1)
        {
            var use = item.Active;
            if (use == null) return "";
            var ci = CultureInfo.InvariantCulture;
            var parts = new List<string>();
            if (!item.IsInstant && use.castRange > 0f) parts.Add($"Дальность {(use.castRange * 100f).ToString("0", ci)}");
            if (use.cooldown > 0f) parts.Add($"Перезарядка {use.cooldown.ToString("0.#", ci)} с");
            if (item.charges > 0) parts.Add(owner != null ? $"Зарядов осталось: {owner.ChargesAt(slot)}" : $"Зарядов: {item.charges}");
            return string.Join("   ·   ", parts);
        }
    }
}
