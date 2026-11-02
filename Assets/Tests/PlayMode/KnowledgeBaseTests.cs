using System.Collections;
using System.Linq;
using NUnit.Framework;
using PudgeWars.Gameplay;
using PudgeWars.Net;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
                foreach (var ability in item.Abilities)
                {
                    var skill = article.related.Single(e => e.title == ability.name && e.kind == (ability.active ? KnowledgeKind.Active : KnowledgeKind.Passive));
                    Assert.Contains(article, skill.related);
                    Assert.AreEqual(item, skill.item);
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
                view.Search("aEtHeR lEnS");
                Assert.AreEqual(1, view.VisibleCount);
                Assert.AreEqual("Aether Lens", view.Selected.title);
                var part = view.Selected.related.First(e => e.kind == KnowledgeKind.Item);
                view.Select(part);
                Assert.AreEqual(part, view.Selected);
                view.Search("no such item 987654");
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
