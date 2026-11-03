using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PudgeWars.Gameplay;
using PudgeWars.Net;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Text = PudgeWars.Gameplay.UiText;

namespace PudgeWars.Tests
{
    public class KnowledgeBaseTests
    {
        [Test]
        public void CatalogIncludesEveryItemItsInheritedAbilitiesAndRecipeLinks()
        {
            var assets = NetPrefabs.Load();
            Assert.IsNotNull(assets);
            var catalog = KnowledgeCatalog.Build(assets.catalog, assets.playerHero);
            foreach (var item in assets.catalog)
            {
                var article = catalog.Single(e => e.kind == KnowledgeKind.Item && e.item == item);
                foreach (var (source, ability) in item.AbilitySources)
                {
                    var skill = article.related.Single(e => e.title == ability.name && e.kind == (ability.active ? KnowledgeKind.Active : KnowledgeKind.Passive));
                    Assert.AreEqual(source, skill.item);
                    Assert.Contains(catalog.Single(e => e.kind == KnowledgeKind.Item && e.item == source), skill.related);
                    if (source != item) Assert.Contains(article, skill.inheritors);
                }
                foreach (var part in item.components)
                {
                    var component = article.related.Single(e => e.kind == KnowledgeKind.Item && e.item == part);
                    Assert.Contains(article, component.related);
                }
            }
            var pudge = catalog.Single(e => e.kind == KnowledgeKind.Hero);
            Assert.IsTrue(pudge.Matches("пУдЖ"));
            var hook = pudge.related.Single(e => e.kind == KnowledgeKind.Active);
            Assert.Contains(pudge, hook.related);
            StringAssert.Contains(KnowledgeCatalog.Number(assets.playerHero.GetComponent<HookAbility>().manaCost), hook.numbers);
            var aura = catalog.Single(e => e.kind == KnowledgeKind.Passive && e.title == "Basilius Aura");
            Assert.AreEqual("Ring of Basilius", aura.item.displayName);
            Assert.IsTrue(aura.inheritors.Any(e => e.title == "Arcane Boots"));
            Assert.IsTrue(aura.Matches("Arcane Boots"), "searching a crafted item must find its inherited abilities too");
        }

        [Test]
        public void CraftingAncestorsKeepTheirStatsAbilitiesAndIndividualAuraRadii()
        {
            var ancestor = ScriptableObject.CreateInstance<ItemDef>();
            var middle = ScriptableObject.CreateInstance<ItemDef>();
            var crafted = ScriptableObject.CreateInstance<ItemDef>();
            var go = new GameObject("InheritanceInventory");
            try
            {
                ancestor.displayName = "Ancestor";
                ancestor.manaBonus = 250f;
                ancestor.manaRegenBonus = 0.8f;
                ancestor.moveSpeedBonus = 0.1f;
                ancestor.strengthBonus = 1f;
                ancestor.agilityBonus = 3f;
                ancestor.intelligenceBonus = 2f;
                ancestor.castRangeBonus = 2.75f;
                ancestor.heroDamageBonus = 16f;
                ancestor.creepDamageBonus = 4f;
                ancestor.killManaSteal = 0.13f;
                ancestor.auraManaRegen = 1f;
                ancestor.auraRadius = 3f;
                ancestor.action = ItemAction.ChopTree;
                ancestor.cooldown = 4f;
                ancestor.abilities = new[] { new ItemAbility { name = "Ancestor Ability", active = true } };
                middle.components = new[] { ancestor };
                middle.manaRegenBonus = 1.5f;
                middle.moveSpeedBonus = 0.45f;
                middle.auraManaRegen = 2f;
                middle.auraRadius = 8f;
                crafted.components = new[] { middle };
                crafted.manaRegenBonus = 1.5f;
                var wallet = go.AddComponent<HeroInventory>();
                wallet.ApplyRemoteState(0, new[] { (crafted, 0) });

                Assert.AreEqual(250f, wallet.ManaBonus);
                Assert.AreEqual(3.8f, wallet.ManaRegenBonus, 0.001f);
                Assert.AreEqual(0.45f, crafted.MoveSpeedBonus);
                Assert.AreEqual(1f, crafted.StrengthBonus);
                Assert.AreEqual(3f, crafted.AgilityBonus);
                Assert.AreEqual(2f, crafted.IntelligenceBonus);
                Assert.AreEqual(2.75f, wallet.CastRangeBonus);
                Assert.AreEqual(16f, crafted.HeroDamageBonus);
                Assert.AreEqual(4f, crafted.CreepDamageBonus);
                Assert.AreEqual(0.13f, wallet.KillManaSteal);
                Assert.AreSame(ancestor, crafted.Active);
                Assert.AreSame(ancestor, crafted.AbilitySources.Single().source);
                Assert.AreEqual(4f, wallet.CooldownOf(wallet.SlotOf(crafted)));
                CollectionAssert.AreEquivalent(new[] { middle, ancestor }, wallet.AuraSourcesWithin(2f));
                CollectionAssert.AreEqual(new[] { middle }, wallet.AuraSourcesWithin(5f));
                Assert.AreEqual(3f, wallet.AuraManaRegenWithin(2f));
                Assert.AreEqual(2f, wallet.AuraManaRegenWithin(5f), "a narrow ancestor aura must not acquire a wider descendant radius");
                Assert.AreEqual(0f, wallet.AuraManaRegenWithin(9f));

                ancestor.manaBonus = 300f;
                ancestor.manaRegenBonus = 1f;
                Assert.AreEqual(300f, wallet.ManaBonus, "ancestor changes must propagate without copying stats to descendants");
                Assert.AreEqual(4f, wallet.ManaRegenBonus);
                StringAssert.Contains("+300 к мане", ItemDescription.Bonuses(crafted, false));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(crafted);
                Object.DestroyImmediate(middle);
                Object.DestroyImmediate(ancestor);
            }
        }

        [Test]
        public void ArcaneBootsUseTheRingsAuraSourceInGameplayAndDescriptions()
        {
            var items = NetPrefabs.Load().catalog;
            var boots = items.Single(i => i.displayName == "Arcane Boots");
            var ring = items.Single(i => i.displayName == "Ring of Basilius");
            var go = new GameObject("AuraSourceHero");
            go.SetActive(false);
            go.transform.position = new Vector3(10000f, 0f, 10000f);
            var wallet = go.AddComponent<HeroInventory>();
            var hero = go.AddComponent<Pudge>();
            var canvas = new GameObject("AuraSourceTooltip", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            try
            {
                go.SetActive(true);
                wallet.ApplyRemoteState(0, new[] { (boots, 0) });
                Assert.AreSame(ring, wallet.AuraSourcesWithin(0f).Single());
                Assert.AreEqual(ring.auraManaRegen, hero.AuraManaRegen);
                Assert.AreEqual(hero.manaRegen + boots.ManaRegenBonus + ring.auraManaRegen, hero.ManaRegen, 0.001f);
                var effects = new List<UnitEffect>();
                hero.Effects(effects);
                Assert.AreSame(ring, effects.Single().item, "the aura icon and effect must refer to Ring of Basilius");
                Assert.AreEqual("Basilius Aura", effects.Single().name);

                var presenter = ShopUI.CreateDescriptions((RectTransform)canvas.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                presenter.ShowTooltip(boots, -1, (RectTransform)canvas.transform);
                StringAssert.Contains("Унаследовано от: Ring of Basilius", presenter.TooltipText);
            }
            finally
            {
                Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(go);
            }
        }

        [UnityTest]
        public IEnumerator CodexFiltersArticlesLinksItemsAndRestoresInputOnClose()
        {
            var canvas = new GameObject("KnowledgeTestCanvas", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var assets = NetPrefabs.Load();
            bool captured = OverlayInput.Captured;
            var view = KnowledgeBase.Create((RectTransform)canvas.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), assets.catalog, assets.playerHero);
            try
            {
                OverlayInput.Captured = false;
                view.Open();
                yield return null;
                Assert.IsTrue(OverlayInput.Captured);
                Assert.IsTrue(view.IsOpen);
                view.SetCategory(4);
                Assert.AreEqual(view.Entries.Count(e => e.kind == KnowledgeKind.Item), view.VisibleCount);
                var input = view.GetComponentInChildren<TMPro.TMP_InputField>();
                Assert.IsNotNull(input, "Search must use the TMP input component.");
                Assert.IsNotNull(input.textViewport.GetComponent<RectMask2D>());
                Assert.AreNotSame(input.textViewport, input.textComponent.rectTransform, "The viewport must not move with the edited text.");
                Assert.IsFalse(input.richText, "Typed markup must stay literal.");
                input.text = "aEtHeR lEnS";
                Assert.AreEqual(2, view.VisibleCount, "Search finds both Aether Lens and its recipe.");
                view.Select(view.Entries.Single(e => e.kind == KnowledgeKind.Item && e.title == "Aether Lens"));
                Assert.AreEqual("Aether Lens", view.Selected.title);
                var part = view.Selected.related.First(e => e.kind == KnowledgeKind.Item);
                view.Select(part);
                Assert.AreEqual(part, view.Selected);
                input.text = "нет такого предмета 987654";
                Assert.AreEqual(0, view.VisibleCount);
                Assert.IsTrue(view.GetComponentsInChildren<Text>().Any(t => t.text == "Ничего не найдено"));
                view.Close();
                Assert.IsFalse(OverlayInput.Captured);
                view.Open();
                Assert.IsTrue(OverlayInput.Captured);
                view.Close();
            }
            finally
            {
                Object.DestroyImmediate(canvas);
                OverlayInput.Captured = captured;
            }
        }

        [UnityTest]
        public IEnumerator ItemTooltipFadesWhilePausedDoesNotRestartOnRefreshAndResetsForAnotherItem()
        {
            var canvas = new GameObject("TooltipTestCanvas", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var over = new GameObject("Item", typeof(RectTransform)).GetComponent<RectTransform>();
            over.SetParent(canvas.transform, false);
            var items = NetPrefabs.Load().catalog.Where(i => !i.recipe).Take(2).ToArray();
            var presenter = ShopUI.CreateDescriptions((RectTransform)canvas.transform, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            float timeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                presenter.ShowTooltip(items[0], -1, over);
                var fade = presenter.GetComponentInChildren<TooltipFade>();
                Assert.IsNotNull(fade);
                var group = fade.GetComponent<CanvasGroup>();
                Assert.AreEqual(0f, group.alpha);
                Assert.IsFalse(group.blocksRaycasts);
                yield return new WaitForSecondsRealtime(TooltipFade.Duration + 0.08f);
                Assert.AreEqual(1f, group.alpha, 0.001f, "hover descriptions must finish fading even when the game is paused");
                presenter.ShowTooltip(items[0], -1, over);
                Assert.AreEqual(1f, group.alpha, "refreshing an inventory must not restart its current tooltip");
                presenter.ShowTooltip(items[1], -1, over);
                Assert.AreEqual(0f, group.alpha, "moving to another item must fade its new description from zero");
                presenter.HideTooltip();
                Assert.IsNull(presenter.TooltipText);
                presenter.ShowTooltip(items[1], -1, over);
                Assert.AreEqual(0f, group.alpha);
            }
            finally
            {
                Time.timeScale = timeScale;
                Object.DestroyImmediate(canvas);
            }
        }
    }
}
