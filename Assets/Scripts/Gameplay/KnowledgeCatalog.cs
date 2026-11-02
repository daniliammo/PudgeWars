using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace PudgeWars.Gameplay
{
    public enum KnowledgeKind { Hero, Active, Passive, Item }

    /// <summary>A searchable article and its links. Items and inherited abilities use the gameplay definitions.</summary>
    public class KnowledgeEntry
    {
        public string title, subtitle, description, numbers;
        public KnowledgeKind kind;
        public Sprite icon;
        public ItemDef item;
        public readonly List<KnowledgeEntry> related = new();

        public bool Matches(string query) => string.IsNullOrWhiteSpace(query) ||
            new[] { title, subtitle, description, numbers, item != null ? item.description : null,
                item != null ? item.details : null }.Any(s => s != null && s.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public static class KnowledgeCatalog
    {
        public static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        public static List<KnowledgeEntry> Build(IEnumerable<ItemDef> items, GameObject heroPrefab)
        {
            var entries = new List<KnowledgeEntry>();
            var art = Resources.Load<KnowledgeArt>("KnowledgeArt");
            var hero = heroPrefab != null ? heroPrefab.GetComponent<Pudge>() : null;
            if (hero != null)
            {
                var pudge = new KnowledgeEntry
                {
                    title = "Пудж", subtitle = "Pudge · герой арены", kind = KnowledgeKind.Hero, icon = art != null ? art.pudge : null,
                    description = "Мясник с дальнобойным хуком. Лови противников и утаскивай их в яму, спасай союзников и усиливай героя предметами.\n\n" +
                        "Пудж одинаково играет за Radiant и Dire. Игроки и боты используют одни и те же способности. После смерти герой возрождается, а его тело остаётся до возрождения.\n\n" +
                        "В текущей версии уровень героя — 1. Сила, ловкость и интеллект предметов пока хранятся без влияния на характеристики.",
                    numbers = $"Здоровье  {Number(hero.maxHealth)}     ·     Мана  {Number(hero.maxMana)}\n" +
                        $"Восстановление здоровья  {Number(hero.healthRegen)}/с     ·     Маны  {Number(hero.manaRegen)}/с"
                };
                var motor = heroPrefab.GetComponent<PudgeMotor>();
                if (motor != null) pudge.numbers += $"\nСкорость передвижения  {Number(motor.moveSpeed)} м/с";
                entries.Add(pudge);
                var hook = heroPrefab.GetComponent<HookAbility>();
                if (hook != null)
                {
                    var meatHook = new KnowledgeEntry
                    {
                        title = "Meat Hook", subtitle = "Пудж · Q · направленная способность", kind = KnowledgeKind.Active, icon = art != null ? art.hook : null,
                        description = "Бросает хук в выбранном направлении. Первая пойманная цель получает урон и притягивается к Пуджу; дополнительный урон наносится за каждый метр протаскивания. Попадание в яму убивает героя. Хук может задеть и союзника.\n\n" +
                            "Союзника можно притянуть к себе и отпустить. Если его уже тащит враг, союзный хук удерживает его: вражеское притягивание замедляется, а спасатель тянется следом. Когда вражеская цепь рвётся, спасатель дотягивает союзника к себе.\n\n" +
                            "При промахе хук возвращается к владельцу, даже если того тащат или он падает в яму. После возрождения старый хук обрывается. Предметы могут увеличить дальность и урон.",
                        numbers = $"Мана  {Number(hook.manaCost)}     ·     Перезарядка  {Number(hook.cooldown)} с\n" +
                            $"Дальность  {Number(hook.range)} м     ·     Урон попадания  {Number(hook.hitDamage)}\n" +
                            $"Урон протаскивания  {Number(hook.damagePerMeter)}/м     ·     Скорость полёта  {Number(hook.flySpeed)} м/с"
                    };
                    meatHook.related.Add(pudge);
                    pudge.related.Add(meatHook);
                    entries.Add(meatHook);
                }
            }

            // Include recipes and nested parts even when the shop does not sell them separately.
            var seen = new HashSet<ItemDef>();
            var definitions = new List<ItemDef>();
            void Visit(ItemDef item)
            {
                if (item == null || !seen.Add(item)) return;
                definitions.Add(item);
                foreach (var part in item.components ?? Array.Empty<ItemDef>()) Visit(part);
            }
            foreach (var item in items ?? Enumerable.Empty<ItemDef>()) Visit(item);
            var articles = new Dictionary<ItemDef, KnowledgeEntry>();
            foreach (var item in definitions.OrderBy(i => i.cost).ThenBy(i => i.displayName))
            {
                var article = new KnowledgeEntry
                {
                    title = item.displayName, kind = KnowledgeKind.Item, icon = item.icon, item = item,
                    subtitle = item.recipe ? "Рецепт" : item.IsUsable ? "Предмет · активное применение" : "Предмет · пассивный",
                    description = string.Join("\n\n", new[] { item.description, item.details }.Where(s => !string.IsNullOrWhiteSpace(s))),
                    numbers = ItemDescription.Bonuses(item, false)
                };
                articles.Add(item, article);
                entries.Add(article);
                foreach (var ability in item.Abilities)
                {
                    var skill = new KnowledgeEntry
                    {
                        title = string.IsNullOrWhiteSpace(ability.name) ? item.displayName : ability.name,
                        subtitle = item.displayName, icon = item.icon, item = item,
                        kind = ability.active ? KnowledgeKind.Active : KnowledgeKind.Passive,
                        description = ability.description,
                        numbers = ability.active ? ItemDescription.UseNumbers(item) : ""
                    };
                    skill.related.Add(article);
                    article.related.Add(skill);
                    entries.Add(skill);
                }
                if (item.IsUsable && !item.Abilities.Any(a => a.active))
                {
                    var skill = new KnowledgeEntry
                    {
                        title = item.displayName, subtitle = "Применение предмета", icon = item.icon, item = item,
                        kind = KnowledgeKind.Active, description = string.IsNullOrWhiteSpace(item.details) ? item.description : item.details,
                        numbers = ItemDescription.UseNumbers(item)
                    };
                    skill.related.Add(article);
                    article.related.Add(skill);
                    entries.Add(skill);
                }
            }
            foreach (var pair in articles)
            foreach (var part in pair.Key.components ?? Array.Empty<ItemDef>())
            {
                if (part == null || !articles.TryGetValue(part, out var component)) continue;
                if (!pair.Value.related.Contains(component)) pair.Value.related.Add(component);
                if (!component.related.Contains(pair.Value)) component.related.Add(pair.Value);
            }
            return entries;
        }
    }
}
