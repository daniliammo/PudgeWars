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

        /// <summary>
        /// Aether Lens: assembled from Energy Booster, Void Stone and its recipe (800 + 700 + 775 = 2275 gold, as in
        /// Dota 2); +275 cast range (1 Dota unit = 1 cm) and the parts' mana: +250 and +1.75 per second.
        /// </summary>
        public static ItemDef AetherLens()
        {
            var parts = new[] { EnergyBooster(), VoidStone(), AetherLensRecipe() };
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
            item.description = "+275 к дальности способностей, +250 маны, +1.75 к восстановлению маны";
            item.details = "";
            item.abilities = new[] { Ability("Aetheral Focus", false, "+275 к дальности способностей: хук летит на 2.75 м дальше. Вторая линза не складывается с первой.") };
            item.lore = "Подарок хворому сыну, наделённый силой последнего вздоха умирающего мага.";
            item.icon = AssetDatabase.LoadAssetAtPath<Sprite>(AetherLensIcon);
            item.cost = parts.Sum(p => p.cost);
            item.castRangeBonus = 2.75f;
            item.manaBonus = 250f;
            item.manaRegenBonus = 0f; // Void Stone's 1.75, inherited: not twice
            item.components = parts;
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
            var catalog = new[]
            {
                Tango(), EnchantedMango(), IronBranch(), QuellingBlade(), SagesMask(), RingOfBasiliusRecipe(), RingOfBasilius(), SlippersOfAgility(),
                BootsOfSpeedRecipe(), BootsOfSpeed(),
                ArcaneBoots(), EnergyBooster(), VoidStone(), AetherLensRecipe(), lens,
            };
            var net = Net.NetPrefabs.Load();
            if (net != null)
            {
                net.catalog = new[] { lens }.Concat(catalog.Where(i => i != lens)).ToArray();
                EditorUtility.SetDirty(net);
                AssetDatabase.SaveAssets();
            }
            return catalog.Where(i => !i.recipe).ToArray(); // recipes are bought with their items, not on their own
        }

        /// <summary>Tango: 70 gold (by request; 90 in Dota 2), 3 charges, eats a tree for +7 health per second over 16 s, twice from a planted one.</summary>
        public static ItemDef Tango() => Item("Tango", "tango", item =>
        {
            item.displayName = "Tango";
            item.description = "Съесть дерево: +7 здоровья в секунду 16 с. 3 штуки";
            item.details = "Повторная покупка добавляет ещё 3 штуки в тот же слот.";
            item.abilities = new[] { Ability("Eat Tree", true, "Пудж съедает выбранное дерево — оно падает, а здоровье восстанавливается на 7 в секунду 16 секунд (112 за всё время). Дерево, посаженное веткой, вдвое сытнее: 14 в секунду. Несколько съеденных деревьев лечат одновременно; смерть прерывает лечение. Срубленное дерево отрастает через 20 секунд.") };
            item.lore = "Натуральная пища для выживания на поле боя.";
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

        /// <summary>
        /// Enchanted Mango: 65 gold as in Dota 2; +0.7 mana per second while carried (by request), eaten at once for
        /// 100 mana (Dota 2's Eat Mango), used up.
        /// </summary>
        public static ItemDef EnchantedMango() => Item("EnchantedMango", "enchanted_mango", item =>
        {
            item.displayName = "Enchanted Mango";
            item.description = "+0.7 к восстановлению маны. Съесть: +100 маны";
            item.details = "";
            item.abilities = new[] { Ability("Eat Mango", true, "Пудж съедает манго и сразу получает 100 маны. Цели не нужно: клавиша или клик по слоту применяют сразу. Манго одноразовое: вместе с ним уходит и прибавка к восстановлению маны.") };
            item.lore = "Горьковато-сладкий вкус плодов с острова Джиди неотразим для амфибий.";
            item.cost = 65;
            item.unique = false;
            item.manaRegenBonus = 0.7f;
            item.action = ItemAction.RestoreMana;
            item.restoreMana = 100f;
            item.charges = 1;
            item.stacks = false;
            item.castRange = 0f;
            item.cooldown = 0f;
        });

        /// <summary>Iron Branch: 50 gold, plants a tree for good (by request; Dota 2's Happy Little Tree stands 20 s) within 200 units, used up.</summary>
        public static ItemDef IronBranch() => Item("IronBranch", "branches", item =>
        {
            item.displayName = "Iron Branch";
            item.description = "+1 ко всем атрибутам. Посадить дерево (навсегда)";
            item.details = "";
            item.abilities = new[] { Ability("Happy Little Tree", true, "Сажает дерево в выбранной точке рядом с Пуджем. Дерево стоит навсегда: герои его обходят, хук пролетает насквозь. Его можно съесть танго (вдвое сытнее обычного) или срубить топориком — тогда оно уже не отрастёт. Нельзя сажать над ямой, на другое дерево и на героя. Ветка одноразовая.") };
            item.lore = "Обычная, казалось бы, веточка дарует владельцу стойкость железного дерева, с которого она срублена.";
            item.strengthBonus = 1f;
            item.agilityBonus = 1f;
            item.intelligenceBonus = 1f;
            item.cost = 50;
            item.unique = false;
            item.action = ItemAction.PlantTree;
            item.charges = 1;
            item.stacks = false;
            item.castRange = 2f;
            item.cooldown = 0f;
            item.treeLifetime = 0f;
        });

        /// <summary>Quelling Blade: 100 gold as in Dota 2, cuts a tree down within 350 units, cooldown 4 s; +16 damage on heroes, +4 on creeps (every hit, and every metre of the hook's drag).</summary>
        public static ItemDef QuellingBlade() => Item("QuellingBlade", "quelling_blade", item =>
        {
            item.displayName = "Quelling Blade";
            item.description = "Срубить дерево (перезарядка 4 с). +16 урона по героям, +4 по крипам";
            item.details = "";
            item.abilities = new[] { Ability("Quell", false, "+16 урона по героям и +4 по крипам к каждому удару — атак, предметов и способностей: у хука к удару при попадании и к каждому метру, на который он протащил цель (10 + 16 = 26 за метр по герою)."), Ability("Chop Tree", true, "Срубает выбранное дерево: остаётся пень, крона разлетается, а через 20 секунд дерево вырастает снова. Дерево, посаженное веткой, срубается насовсем. Если дерево дальше 350, Пудж сначала подойдёт к нему.") };
            item.lore = "Топор павшего гнома, позволяющий с лёгкостью продираться сквозь лес.";
            item.heroDamageBonus = 16f;
            item.creepDamageBonus = 4f;
            item.cost = 100;
            item.unique = true;
            item.action = ItemAction.ChopTree;
            item.charges = 0;
            item.stacks = false;
            item.castRange = 3.5f;
            item.cooldown = 4f;
        });

        /// <summary>Energy Booster: 800 gold as in Dota 2, +250 maximum mana.</summary>
        public static ItemDef EnergyBooster() => Item("EnergyBooster", "energy_booster", item =>
        {
            item.displayName = "Energy Booster";
            item.description = "+250 к максимальной мане";
            item.details = "Эти 250 сразу добавляются и к текущей мане.";
            item.abilities = System.Array.Empty<ItemAbility>();
            item.lore = "";
            item.cost = 800;
            item.unique = false;
            item.manaBonus = 250f;
        });

        /// <summary>Void Stone: 700 gold as in Dota 2, +1.75 mana per second.</summary>
        public static ItemDef VoidStone() => Item("VoidStone", "void_stone", item =>
        {
            item.displayName = "Void Stone";
            item.description = "+1.75 к восстановлению маны";
            item.details = "";
            item.abilities = System.Array.Empty<ItemAbility>();
            item.lore = "";
            item.cost = 700;
            item.unique = false;
            item.manaRegenBonus = 1.75f;
        });

        /// <summary>The Aether Lens recipe: 775 gold as in Dota 2, Dota's recipe scroll icon; gives nothing by itself.</summary>
        public static ItemDef AetherLensRecipe() => Item("AetherLensRecipe", "recipe", item =>
        {
            item.displayName = "Рецепт: Aether Lens";
            item.description = "Покупается вместе с Aether Lens";
            item.details = "Сам ничего не даёт.";
            item.abilities = System.Array.Empty<ItemAbility>();
            item.lore = "";
            item.recipe = true;
            item.cost = 775;
            item.unique = false;
            item.sound = ItemSound.Recipe;
        });

        /// <summary>Radius of Basilius Aura and of Arcane Boots' mana: four times Iron Branch's planting reach (2 m × 4 = 8 m).</summary>
        static float AuraRadius => IronBranch().castRange * 4f;

        /// <summary>Sage's Mask: 175 gold as in Dota 2, +0.8 mana per second (by request; 0.7 in Dota 2).</summary>
        public static ItemDef SagesMask() => Item("SagesMask", "sobi_mask", item =>
        {
            item.displayName = "Sage's Mask";
            item.description = "+0.8 к восстановлению маны";
            item.details = "";
            item.abilities = System.Array.Empty<ItemAbility>();
            item.lore = "Маска, которую зачастую используют в своих ритуалах маги и колдуны.";
            item.cost = 175;
            item.unique = false;
            item.manaRegenBonus = 0.8f;
        });

        /// <summary>The Ring of Basilius recipe: 250 gold as in Dota 2 (Sage's Mask 175 + 250 = 425).</summary>
        public static ItemDef RingOfBasiliusRecipe() => Item("RingOfBasiliusRecipe", "recipe", item =>
        {
            item.displayName = "Рецепт: Ring of Basilius";
            item.description = "Покупается вместе с Ring of Basilius";
            item.details = "Сам ничего не даёт.";
            item.abilities = System.Array.Empty<ItemAbility>();
            item.lore = "";
            item.recipe = true;
            item.cost = 250;
            item.unique = false;
            item.sound = ItemSound.Recipe;
        });

        /// <summary>Ring of Basilius: Sage's Mask + recipe; +1.5 mana per second and Basilius Aura (+1 to allies within 8 m).</summary>
        public static ItemDef RingOfBasilius()
        {
            var parts = new[] { SagesMask(), RingOfBasiliusRecipe() };
            float radius = AuraRadius;
            return Item("RingOfBasilius", "ring_of_basilius", item =>
            {
                item.displayName = "Ring of Basilius";
                item.description = "+2.3 к восстановлению маны (1.5 и 0.8 маски). Аура маны и вампиризм маны";
                item.details = "";
                item.abilities = new[] { BasiliusAura(radius), Ability("Mana Leech", false, "За убийство владелец получает 13 % маны, которая была у убитого, и сразу столько маны, сколько убитый восстанавливал в секунду. Вампиризм нескольких предметов складывается.") };
                item.lore = "Кольцо, служившее наградой величайшим магам.";
                item.cost = parts.Sum(p => p.cost);
                item.components = parts;
                item.unique = false;
                item.manaRegenBonus = 1.5f;
                item.auraManaRegen = 1f;
                item.auraRadius = radius;
                item.killManaSteal = 0.13f;
                item.sound = ItemSound.Ring;
            });
        }

        /// <summary>Slippers of Agility: 140 gold as in Dota 2, +10 movement speed (by request) and +3 agility (stored, not used yet).</summary>
        public static ItemDef SlippersOfAgility() => Item("SlippersOfAgility", "slippers", item =>
        {
            item.displayName = "Slippers of Agility";
            item.description = "+10 к скорости передвижения, +3 к ловкости";
            item.details = "Ловкость пока ни на что не влияет. Скорость складывается с сапогами.";
            item.abilities = System.Array.Empty<ItemAbility>();
            item.lore = "Лёгкие ботинки, сделанные из кожи паука, обостряют ваше чутьё.";
            item.cost = 140;
            item.unique = false;
            item.moveSpeedBonus = 0.1f;
            item.agilityBonus = 3f;
        });

        /// <summary>The Boots of Speed recipe: Slippers of Agility 140 + 360 = 500 gold, the boots' price in Dota 2.</summary>
        public static ItemDef BootsOfSpeedRecipe() => Item("BootsOfSpeedRecipe", "recipe", item =>
        {
            item.displayName = "Рецепт: Boots of Speed";
            item.description = "Покупается вместе с Boots of Speed";
            item.details = "Сам ничего не даёт.";
            item.abilities = System.Array.Empty<ItemAbility>();
            item.lore = "";
            item.recipe = true;
            item.cost = 360;
            item.unique = false;
            item.sound = ItemSound.Recipe;
        });

        /// <summary>Boots of Speed: Slippers of Agility + recipe, 500 gold; +45 movement speed.</summary>
        public static ItemDef BootsOfSpeed()
        {
            var parts = new[] { SlippersOfAgility(), BootsOfSpeedRecipe() };
            return Item("BootsOfSpeed", "boots", item =>
            {
                item.displayName = "Boots of Speed";
                item.description = "+45 к скорости передвижения";
                item.details = "Бонусы нескольких сапог к скорости не складываются.";
                item.abilities = System.Array.Empty<ItemAbility>();
                item.lore = "Удобные скороходы, ускоряющие передвижение.";
                item.cost = parts.Sum(p => p.cost);
                item.components = parts;
                item.unique = false;
                item.boots = true;
                item.moveSpeedBonus = 0.45f;
            });
        }

        /// <summary>
        /// Arcane Boots: Boots of Speed + Ring of Basilius (925 gold); +1.5 mana per second, +45 movement speed,
        /// Basilius Aura, and on use +175 mana to the allies within 8 m, cooldown 55 s.
        /// </summary>
        public static ItemDef ArcaneBoots()
        {
            var parts = new[] { BootsOfSpeed(), RingOfBasilius() };
            float radius = AuraRadius;
            return Item("ArcaneBoots", "arcane_boots", item =>
            {
                item.displayName = "Arcane Boots";
                item.description = "+3.8 к восстановлению маны (1.5 и 2.3 кольца), +45 к скорости. Применение: +175 маны союзникам рядом";
                item.details = "Бонусы нескольких сапог к скорости не складываются.";
                item.abilities = new[] { Ability("Replenish Mana", true, $"+175 маны себе и всем союзникам в радиусе {radius * 100f:0}. Цели не нужно: клавиша или клик по слоту применяют сразу.") }; // Basilius Aura and Mana Leech: the ring's
                item.lore = "В этих ботинках маг становится более ценным в бою.";
                item.cost = parts.Sum(p => p.cost);
                item.components = parts;
                item.unique = true;
                item.manaRegenBonus = 1.5f;
                item.moveSpeedBonus = 0.45f;
                item.boots = true;
                item.auraManaRegen = 0f; // the ring's aura, inherited: not twice
                item.action = ItemAction.RestoreAllyMana;
                item.restoreMana = 175f;
                item.killManaSteal = 0f; // the ring's vampirism, inherited: not twice
                item.castRange = radius;
                item.cooldown = 55f;
                item.charges = 0;
                item.stacks = false;
            });
        }

        static ItemAbility Ability(string name, bool active, string description) =>
            new() { name = name, active = active, description = description };

        /// <summary>Basilius Aura of Ring of Basilius (and of Arcane Boots through it).</summary>
        static ItemAbility BasiliusAura(float radius) =>
            Ability("Basilius Aura", false, $"+1 маны в секунду всем союзникам в радиусе {radius * 100f:0}, и самому владельцу. Ауры складываются: и разных союзников, и нескольких предметов одного героя.");

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

            // shop panel: the rows scroll (the wheel, a finger) once they do not fit over the bar
            const float rowStep = 150f, header = 90f, maxPanelHeight = 860f;
            var panel = Rect(canvas, "ShopPanel", new Vector2(1f, 0f), new Vector2(-24f, 168f),
                new Vector2(640f, Mathf.Min(header + 10f + rowStep * catalog.Length, maxPanelHeight)));
            Img(panel, Panel);
            Txt(panel, "Title", font, 40, "Магазин", TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(400f, 56f));
            shop.closeButton = Btn(panel, "Close", font, "×", new Vector2(1f, 1f), new Vector2(-16f, -16f), new Vector2(56f, 56f));
            var viewport = Rect(panel, "Viewport", new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0.5f, 1f);
            viewport.offsetMin = new Vector2(0f, 10f);
            viewport.offsetMax = new Vector2(0f, -header);
            viewport.gameObject.AddComponent<RectMask2D>();
            Img(viewport, Color.clear); // takes the drag and the wheel between the rows
            var content = Rect(viewport, "Content", new Vector2(0f, 1f), Vector2.zero, new Vector2(0f, rowStep * catalog.Length));
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, rowStep * catalog.Length);
            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            shop.rows = new ShopItemRow[catalog.Length];
            for (int i = 0; i < catalog.Length; i++)
                shop.rows[i] = Row(content, font, catalog[i], new Vector2(24f, -rowStep * i));
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
