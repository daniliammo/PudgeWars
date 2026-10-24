using System.IO;
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
        const string ItemDir = "Assets/_Project/ScriptableObjects/Items";
        const string AetherLensIcon = "Assets/_Project/Art/UI/Items/aether_lens.jpg";

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
            item.icon = AssetDatabase.LoadAssetAtPath<Sprite>(AetherLensIcon);
            item.cost = 2275;
            item.castRangeBonus = 2.75f;
            item.unique = true;
            EditorUtility.SetDirty(item);
            AssetDatabase.SaveAssets();
            return item;
        }

        public static ShopUI Build(Transform canvas, Font font, ItemDef[] catalog)
        {
            if (Object.FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var shop = canvas.gameObject.AddComponent<ShopUI>();

            // always visible bar: gold, item slots, shop button
            var bar = Rect(canvas, "ShopBar", new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(560f, 128f));
            Img(bar, Panel);
            var goldCaption = Txt(bar, "GoldCaption", font, 30, "Золото:", TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(16f, -10f), new Vector2(130f, 44f));
            goldCaption.color = GoldColor;
            shop.goldLabel = Txt(bar, "Gold", font, 34, "0", TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(140f, -10f), new Vector2(160f, 44f));
            shop.goldLabel.color = GoldColor;
            shop.toggleButton = Btn(bar, "ShopButton", font, "Магазин (B)", new Vector2(1f, 1f), new Vector2(-12f, -10f), new Vector2(210f, 44f));
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
