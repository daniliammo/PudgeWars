using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Text = PudgeWars.Gameplay.UiText;

namespace PudgeWars.Gameplay
{
    /// <summary>A shared recipe tree: clickable and scrollable in the codex, compact and passive in hover descriptions.</summary>
    public sealed class CraftingGraph : MonoBehaviour
    {
        const float Padding = 16f;
        bool preview;
        float NodeWidth => preview ? 116f : 148f;
        float NodeHeight => preview ? 106f : 132f;
        float Column => preview ? 136f : 172f;
        float Row => preview ? 152f : 190f;
        static readonly Color Gold = new(0.89f, 0.73f, 0.43f);
        static readonly Color Muted = new(0.61f, 0.67f, 0.72f);

        sealed class Branch
        {
            public KnowledgeEntry entry;
            public int count;
            public float width;
            public readonly List<Branch> parts = new();
            public int Depth => parts.Count == 0 ? 0 : 1 + parts.Max(p => p.Depth);
        }

        RectTransform viewport, content;
        ScrollRect scroll;
        Font font;
        ShopUI descriptions;
        Action<KnowledgeEntry> select;
        float graphWidth, graphHeight, previousWidth = -1f;

        public static void Create(RectTransform parent, KnowledgeEntry entry, IReadOnlyList<KnowledgeEntry> entries,
            Font font, ShopUI descriptions, Action<KnowledgeEntry> select)
        {
            var root = Rect(parent, "CraftingGraph");
            var graph = root.gameObject.AddComponent<CraftingGraph>();
            graph.font = font;
            graph.descriptions = descriptions;
            graph.select = select;
            graph.Build(root, entry, entries);
        }

        /// <summary>The same complete recipe, fitted into a hover description without taking its pointer input.</summary>
        public static CraftingGraph CreatePreview(RectTransform parent, ItemDef item, Font font, float width)
        {
            var upgrades = item.BuildsInto().ToArray();
            if (!item.IsAssembled && upgrades.Length == 0) return null;
            var entries = new Dictionary<ItemDef, KnowledgeEntry>();
            void Visit(ItemDef definition)
            {
                if (definition == null || entries.ContainsKey(definition)) return;
                entries.Add(definition, new KnowledgeEntry
                {
                    item = definition, kind = KnowledgeKind.Item, title = definition.displayName, icon = definition.icon
                });
                foreach (var part in definition.components ?? Array.Empty<ItemDef>()) Visit(part);
            }
            Visit(item);
            foreach (var upgrade in upgrades) Visit(upgrade);
            var root = Rect(parent, "CraftingGraph");
            var graph = root.gameObject.AddComponent<CraftingGraph>();
            graph.preview = true;
            graph.font = font;
            graph.Build(root, entries[item], entries.Values.ToList(), width);
            return graph;
        }

        void Build(RectTransform root, KnowledgeEntry entry, IReadOnlyList<KnowledgeEntry> entries, float previewWidth = 0f)
        {
            var items = entries.Where(e => e.kind == KnowledgeKind.Item && e.item != null).ToDictionary(e => e.item);
            var tree = Tree(entry, 1, items, new HashSet<ItemDef>());
            var upgrades = items.Values.Where(e => e.item.components != null && e.item.components.Contains(entry.item)).ToList();
            graphWidth = Mathf.Max(tree.width, upgrades.Count * Column) + Padding * 2f;
            graphHeight = (tree.Depth + (upgrades.Count > 0 ? 1 : 0)) * Row + NodeHeight + Padding * 2f;
            var layout = root.gameObject.AddComponent<LayoutElement>();
            float scale = preview ? Mathf.Min(1f, Mathf.Max(1f, previewWidth) / graphWidth) : 1f;
            layout.minHeight = layout.preferredHeight = preview ? graphHeight * scale : graphHeight + 20f;
            var background = root.gameObject.AddComponent<Image>();
            background.color = new Color(0.045f, 0.06f, 0.08f);
            background.raycastTarget = !preview;
            if (!preview) scroll = root.gameObject.AddComponent<CraftingGraphScroll>();
            viewport = Rect(root, "Viewport");
            Stretch(viewport);
            if (!preview)
            {
                viewport.offsetMin = new Vector2(0f, 20f);
                viewport.gameObject.AddComponent<RectMask2D>();
            }
            content = Rect(viewport, "Content");
            if (preview) Stretch(content);
            else
            {
                content.anchorMin = content.anchorMax = content.pivot = new Vector2(0f, 1f);
                content.sizeDelta = new Vector2(graphWidth, graphHeight);
                scroll.viewport = viewport;
                scroll.content = content;
                scroll.horizontal = true;
                scroll.vertical = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 35f;
            }

            var diagram = Rect(content, "Diagram");
            diagram.anchorMin = diagram.anchorMax = diagram.pivot = new Vector2(0.5f, 1f);
            diagram.sizeDelta = new Vector2(graphWidth, graphHeight);
            diagram.localScale = Vector3.one * scale;
            var edges = Rect(diagram, "Connections");
            Stretch(edges);
            var nodes = Rect(diagram, "Nodes");
            Stretch(nodes);
            float rootY = Padding + NodeHeight / 2f + (upgrades.Count > 0 ? Row : 0f);
            DrawTree(tree, graphWidth / 2f, rootY, edges, nodes, entry);
            for (int i = 0; i < upgrades.Count; i++)
            {
                var position = new Vector2((graphWidth - upgrades.Count * Column) / 2f + (i + 0.5f) * Column,
                    Padding + NodeHeight / 2f);
                Node(nodes, upgrades[i], position, 1, false);
                Connect(edges, new Vector2(graphWidth / 2f, rootY - NodeHeight / 2f),
                    position + new Vector2(0f, NodeHeight / 2f));
            }

            if (preview) return;
            var track = Rect(root, "Scrollbar");
            track.anchorMin = Vector2.zero;
            track.anchorMax = new Vector2(1f, 0f);
            track.pivot = new Vector2(0.5f, 0f);
            track.sizeDelta = new Vector2(-16f, 8f);
            track.anchoredPosition = new Vector2(0f, 6f);
            track.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.1f, 0.13f);
            var handle = Rect(track, "Handle");
            Stretch(handle);
            var grip = handle.gameObject.AddComponent<Image>();
            grip.color = Gold;
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.LeftToRight;
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = grip;
            scroll.horizontalScrollbar = scrollbar;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        // A repeated part is one branch marked with its required quantity. The path guard also accepts bad cyclic data safely.
        Branch Tree(KnowledgeEntry entry, int count, Dictionary<ItemDef, KnowledgeEntry> items, HashSet<ItemDef> path)
        {
            var branch = new Branch { entry = entry, count = count, width = Column };
            if (!path.Add(entry.item)) return branch;
            foreach (var group in (entry.item.components ?? Array.Empty<ItemDef>()).Where(i => i != null).GroupBy(i => i))
                if (items.TryGetValue(group.Key, out var part) && !path.Contains(group.Key))
                    branch.parts.Add(Tree(part, group.Count(), items, path));
            path.Remove(entry.item);
            if (branch.parts.Count > 0) branch.width = Mathf.Max(Column, branch.parts.Sum(p => p.width));
            return branch;
        }

        void DrawTree(Branch branch, float x, float y, RectTransform edges, RectTransform nodes, KnowledgeEntry selected)
        {
            Node(nodes, branch.entry, new Vector2(x, y), branch.count, branch.entry == selected);
            float left = x - branch.width / 2f;
            foreach (var part in branch.parts)
            {
                float childX = left + part.width / 2f;
                Connect(edges, new Vector2(childX, y + Row - NodeHeight / 2f), new Vector2(x, y + NodeHeight / 2f));
                DrawTree(part, childX, y + Row, edges, nodes, selected);
                left += part.width;
            }
        }

        void Node(RectTransform parent, KnowledgeEntry entry, Vector2 position, int count, bool selected)
        {
            var root = Rect(parent, "Node");
            Place(root, position, new Vector2(NodeWidth, NodeHeight));
            var image = root.gameObject.AddComponent<Image>();
            image.color = selected ? new Color(0.19f, 0.17f, 0.115f) : new Color(0.075f, 0.095f, 0.12f);
            var outline = root.gameObject.AddComponent<Outline>();
            outline.effectColor = selected ? Gold : new Color(0.24f, 0.3f, 0.36f);
            outline.effectDistance = new Vector2(1f, -1f);
            image.raycastTarget = !preview;
            if (!preview)
            {
                var button = root.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => select(entry));
                var pointer = root.gameObject.AddComponent<KnowledgeItemPointer>();
                pointer.item = entry.item;
                pointer.descriptions = descriptions;
            }

            var icon = Rect(root, "Icon");
            Place(icon, new Vector2(NodeWidth / 2f, preview ? 27f : 34f), preview ? new Vector2(50f, 36f) : new Vector2(66f, 48f));
            var picture = icon.gameObject.AddComponent<Image>();
            picture.sprite = entry.icon;
            picture.preserveAspect = true;
            picture.raycastTarget = false;
            picture.color = entry.icon != null ? Color.white : new Color(0.18f, 0.24f, 0.3f);
            Label(root, "Name", entry.title, new Vector2(NodeWidth / 2f, preview ? 63f : 83f), new Vector2(NodeWidth - 10f, preview ? 34f : 44f), preview ? 17 : 18, Color.white);
            Label(root, "Cost", $"{entry.item.cost} з.", new Vector2(NodeWidth / 2f, NodeHeight - (preview ? 12f : 14f)), new Vector2(NodeWidth - 8f, preview ? 20f : 24f), preview ? 15 : 17, Gold);
            if (count > 1)
                Label(root, "Quantity", $"×{count}", new Vector2(NodeWidth - 21f, 17f), new Vector2(40f, 28f), 21, Gold);
        }

        void Label(RectTransform parent, string name, string value, Vector2 position, Vector2 size, int fontSize, Color color)
        {
            var rt = Rect(parent, name);
            Place(rt, position, size);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 14;
            text.resizeTextMaxSize = fontSize;
            text.raycastTarget = false;
        }

        // Arrows point from the consumed component toward the assembled item.
        static void Connect(RectTransform parent, Vector2 from, Vector2 to)
        {
            float middle = (from.y + to.y) / 2f;
            Line(parent, from, new Vector2(from.x, middle));
            Line(parent, new Vector2(from.x, middle), new Vector2(to.x, middle));
            Line(parent, new Vector2(to.x, middle), to);
            Line(parent, to, to + new Vector2(-5f, 8f));
            Line(parent, to, to + new Vector2(5f, 8f));
        }

        static void Line(RectTransform parent, Vector2 from, Vector2 to)
        {
            Vector2 delta = new(to.x - from.x, from.y - to.y);
            if (delta.sqrMagnitude < 0.01f) return;
            var rt = Rect(parent, "Edge");
            Place(rt, (from + to) / 2f, new Vector2(delta.magnitude, 2f));
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = Muted;
            image.raycastTarget = false;
        }

        void LateUpdate()
        {
            if (preview) return;
            float width = viewport.rect.width;
            if (width <= 0f || Mathf.Approximately(width, previousWidth)) return;
            previousWidth = width;
            content.sizeDelta = new Vector2(Mathf.Max(width, graphWidth), graphHeight);
            scroll.horizontalNormalizedPosition = 0.5f;
        }

        static RectTransform Rect(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        static void Place(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(position.x, -position.y);
            rt.sizeDelta = size;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
