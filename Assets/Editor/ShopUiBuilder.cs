using System.IO;
using System.Linq;
using PudgeWars.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PudgeWars.EditorTools
{
    /// <summary>Item assets and the uGUI shop (panel + gold/slots bar) for the test arena.</summary>
    public static class ShopUiBuilder
    {
        const string ItemDir = "Assets/ScriptableObjects/Items";
        const string AetherLensIcon = "Assets/Art/UI/Items/aether_lens.jpg";
        const string DotaIcons = "Assets/Art/UI/Items/Dota";

        internal static readonly Color Panel = new(0.07f, 0.08f, 0.10f, 0.92f);
        internal static readonly Color Frame = new(0.16f, 0.17f, 0.20f, 1f);
        static readonly Color GoldColor = new(1f, 0.82f, 0.25f);
        static readonly Color ButtonColor = new(0.20f, 0.45f, 0.22f, 1f);

        /// <summary>Aether Lens: 2275 gold, +275 cast range as in the current Dota 2 shop (1 Dota unit = 1 cm).</summary>
        public static ItemDef AetherLens()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(AetherLensIcon);
            if (ti != null && ti.textureType != TextureImporterType.Sprite)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }
            Directory.CreateDirectory(ItemDir);
            var path = $"{ItemDir}/AetherLens.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDef>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemDef>();
                AssetDatabase.CreateAsset(item, path);
            }
            item.displayName = "Aether Lens";
            item.description = "+275 к дальности способностей";
            item.details = "Пассивно: хук летит на 275 дальше (на 2.75 м). Вторая линза не складывается с первой.";
            item.icon = AssetDatabase.LoadAssetAtPath<Sprite>(AetherLensIcon);
            item.cost = 2275;
            item.castRangeBonus = 2.75f;
            item.unique = true;
            EditorUtility.SetDirty(item);
            AssetDatabase.SaveAssets();
            return item;
        }

        /// <summary>
        /// The arena shop in its order (cheap consumables first, as in Dota); the network catalog keeps Aether Lens
        /// first and the rest after it, so the indices the clients know stay valid.
        /// </summary>
        public static ItemDef[] Catalog()
        {
            var lens = AetherLens();
            var catalog = new[] { Tango(), IronBranch(), QuellingBlade(), lens };
            var net = Net.NetPrefabs.Load();
            if (net != null)
            {
                net.catalog = new[] { lens }.Concat(catalog.Where(i => i != lens)).ToArray();
                EditorUtility.SetDirty(net);
                AssetDatabase.SaveAssets();
            }
            return catalog;
        }

        /// <summary>Tango: 70 gold (by request; 90 in Dota 2), 3 charges, eats a tree for +7 health per second over 16 s, twice from a planted one.</summary>
        public static ItemDef Tango() => Item("Tango", "tango", item =>
        {
            item.displayName = "Tango";
            item.description = "Съесть дерево: +7 здоровья в секунду 16 с (посаженное веткой — вдвое). 3 штуки";
            item.details = "Пудж съедает выбранное дерево — оно падает, а здоровье восстанавливается на 7 в секунду 16 секунд "
                           + "(112 за всё время). Дерево, посаженное веткой, вдвое сытнее: 14 в секунду. Несколько съеденных деревьев "
                           + "лечат одновременно; смерть прерывает лечение. Срубленное дерево отрастает через 20 секунд. "
                           + "Повторная покупка добавляет ещё 3 штуки в тот же слот.";
            item.cost = 70;
            item.unique = false;
            item.action = ItemAction.EatTree;
            item.charges = 3;
            item.stacks = true;
            item.castRange = 1.65f;
            item.cooldown = 0f;
            item.regenBonus = 7f;
            item.regenDuration = 16f;
            item.plantedTreeBonus = 2f;
        });

        /// <summary>Iron Branch: 50 gold, plants a tree for good (by request; Dota 2's Happy Little Tree stands 20 s) within 200 units, used up.</summary>
        public static ItemDef IronBranch() => Item("IronBranch", "branches", item =>
        {
            item.displayName = "Iron Branch";
            item.description = "Посадить дерево (навсегда)";
            item.details = "Сажает дерево в выбранной точке рядом с Пуджем. Дерево стоит навсегда: герои его обходят, "
                           + "хук пролетает насквозь. Его можно съесть танго (вдвое сытнее обычного) или срубить топориком — "
                           + "тогда оно уже не отрастёт. Нельзя сажать над ямой, на другое дерево и на героя. Ветка одноразовая.";
            item.cost = 50;
            item.unique = false;
            item.action = ItemAction.PlantTree;
            item.charges = 1;
            item.stacks = false;
            item.castRange = 2f;
            item.cooldown = 0f;
            item.treeLifetime = 0f;
        });

        /// <summary>Quelling Blade: 100 gold as in Dota 2, cuts a tree down within 350 units, cooldown 4 s.</summary>
        public static ItemDef QuellingBlade() => Item("QuellingBlade", "quelling_blade", item =>
        {
            item.displayName = "Quelling Blade";
            item.description = "Срубить дерево (перезарядка 4 с)";
            item.details = "Срубает выбранное дерево: остаётся пень, крона разлетается, а через 20 секунд дерево вырастает снова. "
                           + "Дерево, посаженное веткой, срубается насовсем. Если дерево дальше 350, Пудж сначала подойдёт к нему.";
            item.cost = 100;
            item.unique = true;
            item.action = ItemAction.ChopTree;
            item.charges = 0;
            item.stacks = false;
            item.castRange = 3.5f;
            item.cooldown = 4f;
        });

        /// <summary>The item asset (made if missing) with the Dota icon and the values <paramref name="fill"/> sets.</summary>
        static ItemDef Item(string asset, string icon, System.Action<ItemDef> fill)
        {
            Directory.CreateDirectory(ItemDir);
            var path = $"{ItemDir}/{asset}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDef>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemDef>();
                AssetDatabase.CreateAsset(item, path);
            }
            item.icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{DotaIcons}/{icon}.png");
            fill(item);
            EditorUtility.SetDirty(item);
            AssetDatabase.SaveAssets();
            return item;
        }

        public static ShopUI Build(Transform canvas, Font font, ItemDef[] catalog)
        {
            if (Object.FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var shop = canvas.gameObject.AddComponent<ShopUI>();

            // always visible bar: gold, item slots, shop button
            var bar = Rect(canvas, "ShopBar", new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(560f, 128f));
            Img(bar, Panel);
            var goldCaption = Txt(bar, "GoldCaption", font, 30, "Золото:", TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(16f, -10f), new Vector2(130f, 44f));
            goldCaption.color = GoldColor;
            shop.goldLabel = Txt(bar, "Gold", font, 34, "0", TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(140f, -10f), new Vector2(160f, 44f));
            shop.goldLabel.color = GoldColor;
            shop.toggleButton = Btn(bar, "ShopButton", font, "Магазин (F4)", new Vector2(1f, 1f), new Vector2(-12f, -10f), new Vector2(210f, 44f));
            shop.slots = new Image[HeroInventory.SlotCount];
            for (int i = 0; i < shop.slots.Length; i++)
            {
                var slot = Rect(bar, $"Slot_{i + 1}", new Vector2(0f, 0f), new Vector2(16f + i * 89f, 12f), new Vector2(82f, 60f));
                Img(slot, Frame);
                var icon = Rect(slot, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(76f, 54f));
                shop.slots[i] = Img(icon, Color.clear);
                shop.slots[i].preserveAspect = true;
            }

            // shop panel
            var panel = Rect(canvas, "ShopPanel", new Vector2(1f, 0f), new Vector2(-24f, 168f), new Vector2(640f, 150f + 150f * catalog.Length));
            Img(panel, Panel);
            Txt(panel, "Title", font, 40, "Магазин", TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(400f, 56f));
            shop.closeButton = Btn(panel, "Close", font, "×", new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(56f, 56f));
            shop.rows = new ShopItemRow[catalog.Length];
            for (int i = 0; i < catalog.Length; i++)
                shop.rows[i] = Row(panel, font, catalog[i], new Vector2(24f, -90f - 150f * i));
            shop.panel = panel.gameObject;
            return shop;
        }

        static ShopItemRow Row(RectTransform panel, Font font, ItemDef item, Vector2 pos)
        {
            var rowRt = Rect(panel, "Item_" + item.name, new Vector2(0f, 1f), pos, new Vector2(592f, 136f));
            Img(rowRt, Frame);
            var row = rowRt.gameObject.AddComponent<ShopItemRow>();
            row.item = item;
            var icon = Rect(rowRt, "Icon", new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(160f, 116f));
            row.icon = Img(icon, Color.white);
            row.icon.sprite = item.icon;
            row.icon.preserveAspect = true;
            row.title = Txt(rowRt, "Name", font, 32, item.displayName, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(184f, -10f), new Vector2(390f, 42f));
            row.description = Txt(rowRt, "Description", font, 22, item.description, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(184f, -54f), new Vector2(390f, 60f));
            row.price = Txt(rowRt, "Price", font, 30, item.cost.ToString(), TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(184f, 8f), new Vector2(150f, 40f));
            row.price.color = GoldColor;
            row.buyButton = Btn(rowRt, "Buy", font, "Купить", new Vector2(1f, 0f), new Vector2(-10f, 10f), new Vector2(170f, 50f));
            row.buyLabel = row.buyButton.GetComponentInChildren<Text>();
            return row;
        }

        internal static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        internal static Image Img(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        internal static Text Txt(RectTransform parent, string name, Font font, int size, string text, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 box)
        {
            var t = Rect(parent, name, anchor, pos, box).gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.text = text;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        internal static Button Btn(RectTransform parent, string name, Font font, string label, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = Rect(parent, name, anchor, pos, size);
            var img = Img(rt, ButtonColor);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            button.colors = colors;
            var text = Txt(rt, "Label", font, 28, label, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            text.raycastTarget = false;
            return button;
        }
    }
}
