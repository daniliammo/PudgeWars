using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Stats of the inspected hero, as Dota shows a selected unit's: its own kills / deaths / rescues (У/С/С, like the
    /// player's own in the corner of the HUD) and how many times it was rescued, level, movement speed and the hook's range (item bonuses in green),
    /// flight and pull speed (health and mana are on its bars, the hook's cooldown on the ability bar, which shows the
    /// inspected hero's). Speeds and range are in Dota units like the
    /// tooltips (1 m = 100). A dark panel over the inspect tab, refreshed a few times a second while shown.
    /// </summary>
    public class HeroStatsPanel : MonoBehaviour
    {
        const float RowHeight = 30f, Padding = 10f, RefreshInterval = 0.25f;

        public Font font;
        public int fontSize = 22;
        public Color labelColor = new(0.78f, 0.78f, 0.74f);
        public Color valueColor = Color.white;
        public Color bonusColor = new(0.45f, 0.95f, 0.4f);

        Pudge hero;
        float nextRefresh;
        int shownRows;
        // one label and one value per row: the two can be in different fonts (HudFonts), so they cannot share a Text
        readonly List<(Text label, Text value)> rows = new();

        /// <summary>A panel standing on <paramref name="parent"/>'s top-left corner at <paramref name="position"/>, hidden.</summary>
        public static HeroStatsPanel Create(Transform parent, Font font, Vector2 position, float width)
        {
            var rt = new GameObject("InspectStats", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = Vector2.zero; // grows upward from the tab
            rt.anchoredPosition = position;
            rt.sizeDelta = new Vector2(width, 0f);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.08f, 0.9f);
            bg.raycastTarget = false;
            var panel = rt.gameObject.AddComponent<HeroStatsPanel>();
            panel.font = font;
            rt.gameObject.SetActive(false);
            return panel;
        }

        /// <summary>Shows the hero's stats; null hides the panel.</summary>
        public void Show(Pudge shown)
        {
            hero = shown;
            gameObject.SetActive(hero != null);
            Refresh();
        }

        void Update()
        {
            if (hero == null)
            {
                gameObject.SetActive(false); // the hero left the game
                return;
            }
            if (Time.unscaledTime >= nextRefresh) Refresh();
        }

        void Refresh()
        {
            if (hero == null) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            shownRows = 0;
            var score = Arena.Instance != null ? Arena.Instance.ScoreOf(hero) : default;
            Row("У/С/С", $"{score.kills}/{score.deaths}/{score.rescues}");
            Row("Спасён", score.rescued.ToString());
            Row("Уровень", hero.Level.ToString());
            if (hero.TryGetComponent<PudgeMotor>(out var motor)) Row("Скорость", Units(motor.MoveSpeed));
            if (hero.TryGetComponent<HookAbility>(out var hook))
            {
                float bonus = hook.EffectiveRange - hook.range;
                var range = Units(hook.range);
                if (bonus > 0.005f) range += $" <color=#{ColorUtility.ToHtmlStringRGB(bonusColor)}>+{Units(bonus)}</color>";
                Row("Дальность хука", range);
                Row("Скорость хука", Units(hook.flySpeed));
                Row("Подтягивание", Units(hook.pullSpeed));
            }
            for (int i = shownRows; i < rows.Count; i++)
            {
                rows[i].label.gameObject.SetActive(false);
                rows[i].value.gameObject.SetActive(false);
            }
            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, 2f * Padding + shownRows * RowHeight);
        }

        static string Units(float metres) => Mathf.RoundToInt(metres * 100f).ToString();

        void Row(string label, string value)
        {
            if (shownRows == rows.Count)
                rows.Add((Cell("Label", TextAnchor.MiddleLeft, labelColor), Cell("Value", TextAnchor.MiddleRight, valueColor)));
            var (l, v) = rows[shownRows];
            l.text = label;
            v.text = value;
            l.gameObject.SetActive(true);
            v.gameObject.SetActive(true);
            shownRows++;
        }

        Text Cell(string name, TextAnchor alignment, Color color)
        {
            int row = rows.Count;
            var rt = new GameObject($"{name}{row}", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(16f, -Padding - (row + 1) * RowHeight);
            rt.offsetMax = new Vector2(-16f, -Padding - row * RowHeight);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.supportRichText = true;
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(2f, -2f);
            return text;
        }
    }
}
