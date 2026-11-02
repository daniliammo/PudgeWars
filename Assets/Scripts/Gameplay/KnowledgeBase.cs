using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>A runtime-built codex: categories, searchable index and scrollable articles with linked recipes and skills.</summary>
    public class KnowledgeBase : MonoBehaviour
    {
        static readonly Color Background = new(0.035f, 0.045f, 0.06f, 1f);
        static readonly Color Plate = new(0.065f, 0.08f, 0.1f, 1f);
        static readonly Color Muted = new(0.61f, 0.67f, 0.72f);
        static readonly Color Gold = new(0.89f, 0.73f, 0.43f);
        static readonly Color Blue = new(0.46f, 0.7f, 1f);
        static readonly Color Ink = new(0.85f, 0.88f, 0.9f);
        static readonly string[] Categories = { "Все записи", "Персонажи", "Активные", "Пассивные", "Предметы" };

        Font font;
        List<KnowledgeEntry> entries;
        readonly List<(Button button, Text label)> tabs = new();
        readonly List<(KnowledgeEntry entry, Image image)> rows = new();
        readonly Stack<KnowledgeEntry> history = new();
        RectTransform index, article;
        ScrollRect indexScroll, articleScroll;
        Text resultCount, articleType;
        InputField search;
        Button back;
        ShopUI descriptions;
        KnowledgeEntry selected;
        int category;
        bool previousCapture;

        public IReadOnlyList<KnowledgeEntry> Entries => entries;
        public KnowledgeEntry Selected => selected;
        public int VisibleCount => rows.Count;
        public bool IsOpen => gameObject.activeSelf;

        public static KnowledgeBase Create(RectTransform parent, Font font, IEnumerable<ItemDef> items, GameObject heroPrefab)
        {
            var root = Rect(parent, "KnowledgeBase");
            Fill(root);
            root.gameObject.SetActive(false);
            var view = root.gameObject.AddComponent<KnowledgeBase>();
            view.font = font;
            view.entries = KnowledgeCatalog.Build(items, heroPrefab);
            view.Build(root);
            view.SetCategory(0);
            return view;
        }

        public void Open()
        {
            if (IsOpen) return;
            previousCapture = OverlayInput.Captured;
            OverlayInput.Captured = true;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Canvas.ForceUpdateCanvases();
            if (selected == null && entries.Count > 0) Select(entries[0]);
        }

        public void Close() => gameObject.SetActive(false);

        void OnDisable()
        {
            OverlayInput.Captured = previousCapture;
            if (descriptions != null) descriptions.HideTooltip();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
        }

        public void SetCategory(int value)
        {
            category = Mathf.Clamp(value, 0, Categories.Length - 1);
            for (int i = 0; i < tabs.Count; i++)
            {
                tabs[i].button.GetComponent<Image>().color = i == category ? new Color(0.19f, 0.18f, 0.14f) : Plate;
                tabs[i].label.color = i == category ? Gold : Ink;
            }
            RebuildIndex();
        }

        public void Search(string query) => search.text = query ?? "";

        bool InCategory(KnowledgeEntry entry) => category == 0 || (int)entry.kind == category - 1;

        void RebuildIndex()
        {
            if (index == null) return;
            descriptions?.HideTooltip();
            Clear(index);
            rows.Clear();
            var visible = entries.Where(e => InCategory(e) && e.Matches(search.text)).ToList();
            resultCount.text = $"НАЙДЕНО: {visible.Count}";
            foreach (var entry in visible)
            {
                var button = EntryButton(index, entry, 94f, () => Select(entry));
                rows.Add((entry, button.GetComponent<Image>()));
            }
            if (visible.Count == 0)
            {
                Paragraph(index, "Ничего не найдено", 26, Gold);
                Paragraph(index, "Попробуй другое название или выбери раздел «Все записи».", 22, Muted);
                var reset = Button(index, "Сбросить поиск", 52f, () => { Search(""); SetCategory(0); });
                reset.GetComponent<Image>().color = Plate;
            }
            else if (selected == null || !visible.Contains(selected)) Select(visible[0]);
            Highlight();
            SmoothScrolling.Attach(indexScroll).Stop();
            indexScroll.verticalNormalizedPosition = 1f;
        }

        public void Select(KnowledgeEntry entry) => Show(entry, true);

        void Show(KnowledgeEntry entry, bool remember)
        {
            if (entry == null || !entries.Contains(entry)) return;
            descriptions.HideTooltip();
            if (remember && selected != null && selected != entry) history.Push(selected);
            selected = entry;
            back.interactable = history.Count > 0;
            Highlight();
            Clear(article);
            articleType.text = Kind(entry).ToUpperInvariant();

            var heading = Rect(article, "Heading");
            Height(heading, 112f);
            Badge(heading, entry, 96f);
            var title = Label(heading, entry.title, 42, Color.white);
            Fill(title.rectTransform, new Vector2(120f, 38f), Vector2.zero);
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 26;
            title.resizeTextMaxSize = 42;
            var subtitle = Label(heading, entry.subtitle, 22, Muted);
            Fill(subtitle.rectTransform, new Vector2(120f, 0f), new Vector2(0f, 76f));
            Divider(article, Gold);

            if (entry.item != null && entry.kind == KnowledgeKind.Item)
            {
                var cost = Paragraph(article, $"{entry.item.cost}  ЗОЛОТА", 30, Gold);
                cost.fontStyle = FontStyle.Bold;
            }
            if (!string.IsNullOrWhiteSpace(entry.numbers))
            {
                var numbers = Stack(article, "Stats", new Color(0.08f, 0.12f, 0.16f), 18, 6f);
                Paragraph(numbers, entry.numbers, 24, entry.kind == KnowledgeKind.Active ? Blue : new Color(0.73f, 0.86f, 0.72f));
            }
            Paragraph(article, entry.description, 24, Ink);

            if (entry.kind == KnowledgeKind.Item && entry.item != null)
            {
                var skills = entry.related.Where(e => e.kind is KnowledgeKind.Active or KnowledgeKind.Passive).ToList();
                if (skills.Count > 0)
                {
                    Section(article, "СПОСОБНОСТИ ПРЕДМЕТА");
                    foreach (var skill in skills)
                    {
                        var box = Stack(article, "Ability", skill.kind == KnowledgeKind.Active ? new Color(0.07f, 0.12f, 0.2f) : Plate, 16, 8f);
                        var link = Button(box, $"{Kind(skill)}: {skill.title}  →", 48f, () => Select(skill));
                        link.GetComponent<Image>().color = Color.clear;
                        link.GetComponentInChildren<Text>().color = skill.kind == KnowledgeKind.Active ? Blue : Gold;
                        if (skill.item != entry.item)
                        {
                            var source = entries.First(e => e.kind == KnowledgeKind.Item && e.item == skill.item);
                            var origin = Button(box, $"Унаследовано от: {source.title}  →", 42f, () => Select(source));
                            origin.GetComponentInChildren<Text>().color = Muted;
                        }
                        Paragraph(box, skill.description, 22, Ink);
                        if (!string.IsNullOrEmpty(skill.numbers)) Paragraph(box, skill.numbers, 21, Blue);
                    }
                }

                var parts = entry.item.components ?? Array.Empty<ItemDef>();
                var components = entry.related.Where(e => e.kind == KnowledgeKind.Item && parts.Contains(e.item)).ToList();
                if (components.Count > 0)
                {
                    Section(article, "СОСТАВ ПРЕДМЕТА");
                    Paragraph(article, "Характеристики и способности компонентов наследуются по всей цепочке сборки. В итоговых характеристиках выше они уже учтены.", 22, Muted);
                    foreach (var component in components)
                    {
                        EntryButton(article, component, 76f, () => Select(component), parts.Count(p => p == component.item));
                        if (!string.IsNullOrWhiteSpace(component.numbers)) Paragraph(article, component.numbers, 21, Muted);
                    }
                }
                var upgrades = entry.related.Where(e => e.kind == KnowledgeKind.Item && !parts.Contains(e.item)).ToList();
                if (upgrades.Count > 0)
                {
                    Section(article, "ИСПОЛЬЗУЕТСЯ В СБОРКЕ");
                    foreach (var upgrade in upgrades) EntryButton(article, upgrade, 76f, () => Select(upgrade));
                }
                if (entry.item.recipe)
                    Paragraph(article, "Рецепт докупается вместе с собираемым предметом. В магазине отдельно не продаётся.", 22, Muted);
                else if (entry.item.IsUsable)
                    Paragraph(article, "Применение: Z X C V B N или клик по слоту. Для направленного применения выбери цель. На телефоне — касание слота и цели.", 22, Muted);
                if (!string.IsNullOrWhiteSpace(entry.item.lore))
                {
                    Divider(article, new Color(0.25f, 0.24f, 0.21f));
                    Paragraph(article, entry.item.lore, 23, Gold).fontStyle = FontStyle.Italic;
                }
            }
            else if (entry.related.Count > 0)
            {
                Section(article, entry.kind == KnowledgeKind.Hero ? "СПОСОБНОСТИ ГЕРОЯ" : "ИСТОЧНИК СПОСОБНОСТИ");
                foreach (var link in entry.related) EntryButton(article, link, 84f, () => Select(link));
            }
            if (entry.inheritors.Count > 0)
            {
                Section(article, "НАСЛЕДУЕТСЯ ПРЕДМЕТАМИ");
                foreach (var inheritor in entry.inheritors) EntryButton(article, inheritor, 84f, () => Select(inheritor));
            }
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(article);
            SmoothScrolling.Attach(articleScroll).Stop();
            articleScroll.verticalNormalizedPosition = 1f;
        }

        void Highlight()
        {
            foreach (var row in rows)
                row.image.color = row.entry == selected ? new Color(0.2f, 0.19f, 0.145f) : Plate;
        }

        void Build(RectTransform root)
        {
            root.gameObject.AddComponent<Image>().color = new Color(0.012f, 0.018f, 0.025f, 0.96f);
            var frame = Rect(root, "Codex");
            frame.anchorMin = new Vector2(0.025f, 0.035f);
            frame.anchorMax = new Vector2(0.975f, 0.965f);
            frame.offsetMin = frame.offsetMax = Vector2.zero;
            frame.gameObject.AddComponent<Image>().color = Background;
            var rim = frame.gameObject.AddComponent<Outline>();
            rim.effectColor = new Color(0.45f, 0.36f, 0.21f);
            rim.effectDistance = new Vector2(1f, -1f);

            var header = Rect(frame, "Header");
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = Vector2.one;
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, 132f);
            header.gameObject.AddComponent<Image>().color = Plate;
            var title = Label(header, "БАЗА ЗНАНИЙ", 44, Gold);
            Fill(title.rectTransform, new Vector2(30f, 54f), new Vector2(300f, 12f));
            var caption = Label(header, "Изучай героя. Находи предметы. Собирай свою тактику.", 22, Muted);
            Fill(caption.rectTransform, new Vector2(32f, 10f), new Vector2(300f, 80f));
            var close = Button(header, "Закрыть  ×", 60f, Close);
            var closeRt = (RectTransform)close.transform;
            closeRt.anchorMin = closeRt.anchorMax = closeRt.pivot = Vector2.one;
            closeRt.anchoredPosition = new Vector2(-28f, -28f);
            closeRt.sizeDelta = new Vector2(220f, 60f);

            var body = Rect(frame, "Body");
            Fill(body, new Vector2(24f, 46f), new Vector2(24f, 154f));
            var sidebar = Stack(body, "Categories", Color.clear, 0, 12f);
            sidebar.anchorMin = Vector2.zero;
            sidebar.anchorMax = new Vector2(0.155f, 1f);
            sidebar.offsetMin = sidebar.offsetMax = Vector2.zero;
            Paragraph(sidebar, "КОДЕКС АРЕНЫ", 20, Gold);
            for (int i = 0; i < Categories.Length; i++)
            {
                int tab = i;
                int count = i == 0 ? entries.Count : entries.Count(e => (int)e.kind == i - 1);
                var button = Button(sidebar, $"{Categories[i]}  {count}", 68f, () => SetCategory(tab));
                var label = button.GetComponentInChildren<Text>();
                label.fontSize = 24;
                label.alignment = TextAnchor.MiddleLeft;
                Fill(label.rectTransform, new Vector2(12f, 0f), new Vector2(4f, 0f));
                tabs.Add((button, label));
            }
            Paragraph(sidebar, "Активные способности требуют применения. Пассивные работают сами, пока соблюдены их условия.", 21, Muted);

            var browser = Rect(body, "Browser");
            browser.anchorMin = new Vector2(0.17f, 0f);
            browser.anchorMax = new Vector2(0.425f, 1f);
            browser.offsetMin = browser.offsetMax = Vector2.zero;
            BuildSearch(browser);
            resultCount = Label(browser, "", 18, Muted);
            resultCount.rectTransform.anchorMin = new Vector2(0f, 1f);
            resultCount.rectTransform.anchorMax = Vector2.one;
            resultCount.rectTransform.pivot = new Vector2(0.5f, 1f);
            resultCount.rectTransform.anchoredPosition = new Vector2(0f, -74f);
            resultCount.rectTransform.sizeDelta = new Vector2(0f, 28f);
            indexScroll = Scroll(browser, "Index", 116f, out index);

            var reading = Rect(body, "Reading");
            reading.anchorMin = new Vector2(0.45f, 0f);
            reading.anchorMax = Vector2.one;
            reading.offsetMin = reading.offsetMax = Vector2.zero;
            back = Button(reading, "← Назад", 42f, () => { if (history.Count > 0) Show(history.Pop(), false); });
            var backRt = (RectTransform)back.transform;
            backRt.anchorMin = backRt.anchorMax = backRt.pivot = new Vector2(0f, 1f);
            backRt.sizeDelta = new Vector2(150f, 42f);
            articleType = Label(reading, "", 20, Gold);
            Fill(articleType.rectTransform, new Vector2(170f, 0f), Vector2.zero);
            articleType.rectTransform.anchorMin = new Vector2(0f, 1f);
            articleType.rectTransform.offsetMin = new Vector2(170f, -42f);
            articleType.rectTransform.offsetMax = Vector2.zero;
            articleScroll = Scroll(reading, "Article", 64f, out article);
            var footer = Label(frame, "PUDGE WARS  /  ПРЕДМЕТЫ И СПОСОБНОСТИ ТЕКУЩЕЙ ВЕРСИИ", 18, Muted);
            footer.alignment = TextAnchor.MiddleCenter;
            footer.rectTransform.anchorMin = Vector2.zero;
            footer.rectTransform.anchorMax = new Vector2(1f, 0f);
            footer.rectTransform.pivot = new Vector2(0.5f, 0f);
            footer.rectTransform.sizeDelta = new Vector2(0f, 36f);
            descriptions = ShopUI.CreateDescriptions(root, font);
        }

        void BuildSearch(RectTransform parent)
        {
            var box = Rect(parent, "Search");
            box.anchorMin = new Vector2(0f, 1f);
            box.anchorMax = Vector2.one;
            box.pivot = new Vector2(0.5f, 1f);
            box.sizeDelta = new Vector2(0f, 60f);
            box.gameObject.AddComponent<Image>().color = new Color(0.11f, 0.14f, 0.17f);
            var text = Label(box, "", 24, Color.white);
            Fill(text.rectTransform, new Vector2(16f, 6f), new Vector2(16f, 6f));
            text.supportRichText = false;
            var hint = Label(box, "Поиск по разделу…", 24, Muted);
            Fill(hint.rectTransform, new Vector2(16f, 6f), new Vector2(16f, 6f));
            search = box.gameObject.AddComponent<InputField>();
            search.textComponent = text;
            search.placeholder = hint;
            search.characterLimit = 100;
            search.onValueChanged.AddListener(_ => RebuildIndex());
        }

        Button EntryButton(RectTransform parent, KnowledgeEntry entry, float height, Action click, int count = 1)
        {
            var button = Button(parent, "", height, click);
            var rt = (RectTransform)button.transform;
            Badge(rt, entry, height - 22f, 10f);
            var title = button.GetComponentInChildren<Text>();
            title.text = entry.title + (count > 1 ? $" ×{count}" : "");
            title.fontSize = 25;
            title.alignment = TextAnchor.MiddleLeft;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 20;
            title.resizeTextMaxSize = 25;
            Fill(title.rectTransform, new Vector2(height, 30f), new Vector2(12f, 6f));
            var subtitle = Label(rt, entry.kind == KnowledgeKind.Item ? $"{entry.item.cost} золота · {entry.subtitle}" : entry.subtitle, 18, Muted);
            Fill(subtitle.rectTransform, new Vector2(height, 6f), new Vector2(12f, height - 36f));
            subtitle.resizeTextForBestFit = true;
            subtitle.resizeTextMinSize = 15;
            subtitle.resizeTextMaxSize = 18;
            if (entry.kind == KnowledgeKind.Item && descriptions != null)
            {
                var pointer = rt.gameObject.AddComponent<KnowledgeItemPointer>();
                pointer.descriptions = descriptions;
                pointer.item = entry.item;
            }
            return button;
        }

        void Badge(RectTransform parent, KnowledgeEntry entry, float size, float left = 0f)
        {
            var badge = Rect(parent, "Badge");
            badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0f, 0.5f);
            badge.anchoredPosition = new Vector2(left, 0f);
            badge.sizeDelta = new Vector2(size, size);
            var image = badge.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.color = entry.icon != null ? Color.white : new Color(0.14f, 0.2f, 0.23f);
            image.sprite = entry.icon;
            image.preserveAspect = true;
            if (entry.icon == null)
            {
                var letter = Label(badge, entry.kind == KnowledgeKind.Hero ? "P" : "Q", (int)(size * 0.55f), Gold);
                letter.alignment = TextAnchor.MiddleCenter;
                Fill(letter.rectTransform);
            }
        }

        ScrollRect Scroll(RectTransform parent, string name, float top, out RectTransform content)
        {
            var root = Rect(parent, name);
            Fill(root, Vector2.zero, new Vector2(0f, top));
            root.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.035f, 0.045f);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            var viewport = Rect(root, "Viewport");
            Fill(viewport, new Vector2(0f, 0f), new Vector2(10f, 0f));
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Stack(viewport, "Content", Color.clear, 4, 14f);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;

            var track = Rect(root, "Scrollbar");
            track.anchorMin = new Vector2(1f, 0f);
            track.anchorMax = Vector2.one;
            track.pivot = Vector2.one;
            track.sizeDelta = new Vector2(5f, 0f);
            track.gameObject.AddComponent<Image>().color = Plate;
            var handle = Rect(track, "Handle");
            Fill(handle);
            var grip = handle.gameObject.AddComponent<Image>();
            grip.color = new Color(0.4f, 0.36f, 0.27f);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = grip;
            scroll.verticalScrollbar = scrollbar;
            SmoothScrolling.Attach(scroll);
            return scroll;
        }

        void Section(RectTransform parent, string caption)
        {
            Divider(parent, new Color(0.2f, 0.24f, 0.27f));
            Paragraph(parent, caption, 20, Gold);
        }

        static string Kind(KnowledgeEntry entry) => entry.kind switch
        {
            KnowledgeKind.Hero => "Персонаж", KnowledgeKind.Active => "Активная", KnowledgeKind.Passive => "Пассивная", _ => "Предмет"
        };

        Text Paragraph(RectTransform parent, string value, int size, Color color)
        {
            var text = Label(parent, value ?? "", size, color);
            text.gameObject.SetActive(!string.IsNullOrWhiteSpace(value));
            return text;
        }

        Text Label(Transform parent, string value, int size, Color color)
        {
            var text = Rect(parent, "Text").gameObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.raycastTarget = false;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        Button Button(RectTransform parent, string caption, float height, Action click)
        {
            var rt = Rect(parent, "Button");
            Height(rt, height);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = Plate;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.3f, 1.25f, 1.12f);
            colors.pressedColor = new Color(0.75f, 0.8f, 0.85f);
            colors.fadeDuration = 0.1f;
            button.colors = colors;
            button.onClick.AddListener(() => click());
            var label = Label(rt, caption, 25, Ink);
            label.alignment = TextAnchor.MiddleCenter;
            Fill(label.rectTransform, new Vector2(8f, 0f), new Vector2(8f, 0f));
            return button;
        }

        static RectTransform Stack(Transform parent, string name, Color color, int padding, float spacing)
        {
            var rt = Rect(parent, name);
            if (color.a > 0f)
            {
                var image = rt.gameObject.AddComponent<Image>();
                image.color = color;
                image.raycastTarget = false;
            }
            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rt;
        }

        static void Divider(Transform parent, Color color)
        {
            var line = Rect(parent, "Line");
            Height(line, 2f);
            var image = line.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        static RectTransform Rect(Transform parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static void Fill(RectTransform rt, Vector2 bottomLeft = default, Vector2 topRight = default)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = bottomLeft;
            rt.offsetMax = -topRight;
        }

        static void Height(RectTransform rt, float height)
        {
            var layout = rt.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = height;
        }

        static void Clear(Transform parent)
        {
            foreach (Transform child in parent)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }
    }
}
