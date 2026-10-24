using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Health bars over the heroes in the manner of Dota 2: a black frame with the health fill (green for the
    /// local player's team, red for enemies), a light gloss on its upper half, thin notches every <see cref="segmentHealth"/>,
    /// "current / max" health in white with a black outline over the bar and the hero's nickname above. Follows each hero on screen (never leaving it at the top, where the far
    /// heroes stand) and hides while it is dead or behind the camera.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class HealthBars : MonoBehaviour
    {
        public Font font;
        public int nameFontSize = 26;
        [Tooltip("Height above the hero's feet the bar is anchored to, metres.")] public float anchorHeight = 3.3f;
        [Tooltip("Inner size of the bar, canvas units.")] public Vector2 size = new(150f, 20f);
        public float border = 2f;
        [Tooltip("A notch every this much health.")] public float segmentHealth = 250f;
        public Color playerColor = new(0.25f, 0.8f, 0.18f);
        public Color enemyColor = new(0.8f, 0.17f, 0.12f);
        public Color frameColor = new(0f, 0f, 0f, 0.85f);
        public Color emptyColor = new(0.12f, 0.12f, 0.12f, 0.9f);
        [Tooltip("{0} = current health, {1} = maximum, both rounded up.")]
        public string healthFormat = "{0}/{1}";
        public int healthFontSize = 16;
        public Color healthTextColor = Color.white;
        [Tooltip("Bars of heroes at the far edge of the view are held this far below the top of the screen, canvas units.")]
        public float topMargin = 40f;

        class Bar
        {
            public RectTransform root, fill;
            public Image fillImage;
            public Text name, health;
            public int shownHealth = -1, shownMax = -1;
        }

        readonly Dictionary<Hookable, Bar> bars = new();
        readonly List<Hookable> gone = new();
        RectTransform rect;
        Canvas canvas;

        public int Count => bars.Count;

        void Awake()
        {
            rect = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
            LocalPlayer.Changed += Rebuild;
        }

        void OnDestroy() => LocalPlayer.Changed -= Rebuild;

        /// <summary>The local team may have changed (network game): colours are made anew.</summary>
        void Rebuild()
        {
            foreach (var bar in bars.Values)
                if (bar.root != null) Destroy(bar.root.gameObject);
            bars.Clear();
        }

        /// <summary>The bar's root, health fill and nickname label for a hero (null if it has none yet).</summary>
        public (RectTransform root, RectTransform fill, Text name) Of(Hookable hero) =>
            bars.TryGetValue(hero, out var b) ? (b.root, b.fill, b.name) : default;

        /// <summary>The "current / max" health label on a hero's bar (null if it has none yet).</summary>
        public Text HealthLabel(Hookable hero) => bars.TryGetValue(hero, out var b) ? b.health : null;

        void LateUpdate()
        {
            foreach (var hero in Hookable.All)
                if (!bars.ContainsKey(hero)) bars[hero] = Create(hero);

            gone.Clear();
            foreach (var (hero, bar) in bars)
            {
                if (hero == null || !hero.isActiveAndEnabled)
                {
                    gone.Add(hero);
                    continue;
                }
                bar.name.text = hero.Nickname;
                bar.fillImage.color = hero.team == LocalPlayer.Team ? playerColor : enemyColor; // teams may change in a network game
                bar.fill.anchorMax = new Vector2(hero.maxHealth > 0f ? Mathf.Clamp01(hero.Health / hero.maxHealth) : 0f, 1f);
                ShowHealth(hero, bar);
                Place(hero, bar);
            }
            foreach (var hero in gone)
            {
                if (bars[hero].root != null) Destroy(bars[hero].root.gameObject);
                bars.Remove(hero);
            }
        }

        /// <summary>Rebuilds the text only when the numbers change (no garbage every frame).</summary>
        void ShowHealth(Hookable hero, Bar bar)
        {
            int health = Mathf.CeilToInt(Mathf.Max(0f, hero.Health)), max = Mathf.CeilToInt(hero.maxHealth);
            if (health == bar.shownHealth && max == bar.shownMax) return;
            bar.shownHealth = health;
            bar.shownMax = max;
            bar.health.text = string.Format(healthFormat, health, max);
        }

        void Place(Hookable hero, Bar bar)
        {
            var cam = Camera.main;
            var screen = cam != null ? cam.WorldToScreenPoint(hero.transform.position + Vector3.up * anchorHeight) : Vector3.zero;
            bool visible = hero.IsAlive && screen.z > 0f;
            bar.root.gameObject.SetActive(visible);
            if (!visible) return;
            var uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, uiCam, out var local))
                bar.root.anchoredPosition = new Vector2(local.x, Mathf.Min(local.y, rect.rect.yMax - topMargin));
        }

        Bar Create(Hookable hero)
        {
            var root = Rect(rect, "HealthBar_" + hero.name, size + Vector2.one * (2f * border));
            Img(root, frameColor);

            var inner = Rect(root, "Inner", size);
            Img(inner, emptyColor);
            var fill = Rect(inner, "Fill", Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0f, 0.5f);
            var fillImage = Img(fill, hero.team == LocalPlayer.Team ? playerColor : enemyColor);

            // gloss on the upper half of the fill, as on Dota's bars
            var gloss = Rect(fill, "Gloss", Vector2.zero);
            gloss.anchorMin = new Vector2(0f, 0.5f);
            gloss.anchorMax = Vector2.one;
            Img(gloss, new Color(1f, 1f, 1f, 0.22f));

            for (float hp = segmentHealth; segmentHealth > 0f && hp < hero.maxHealth; hp += segmentHealth)
            {
                var notch = Rect(inner, "Notch", new Vector2(1.5f, 0f));
                notch.anchorMin = new Vector2(hp / hero.maxHealth, 0f);
                notch.anchorMax = new Vector2(hp / hero.maxHealth, 1f);
                Img(notch, new Color(0f, 0f, 0f, 0.6f));
            }

            // over the fill and the notches: white bold digits in a black outline read on green, red and empty alike
            var healthRt = Rect(root, "Health", size + new Vector2(0f, 6f));
            var health = healthRt.gameObject.AddComponent<Text>();
            health.font = font;
            health.fontSize = healthFontSize;
            health.fontStyle = FontStyle.Bold;
            health.alignment = TextAnchor.MiddleCenter;
            health.color = healthTextColor;
            health.raycastTarget = false;
            health.horizontalOverflow = HorizontalWrapMode.Overflow;
            health.verticalOverflow = VerticalWrapMode.Overflow;
            var healthOutline = healthRt.gameObject.AddComponent<Outline>();
            healthOutline.effectColor = Color.black;
            healthOutline.effectDistance = new Vector2(1.5f, -1.5f);

            var nameRt = Rect(root, "Nickname", new Vector2(400f, nameFontSize * 1.4f));
            nameRt.anchoredPosition = new Vector2(0f, size.y / 2f + border + nameFontSize * 0.75f);
            var name = nameRt.gameObject.AddComponent<Text>();
            name.font = font;
            name.fontSize = nameFontSize;
            name.fontStyle = FontStyle.Bold;
            name.alignment = TextAnchor.MiddleCenter;
            name.color = Color.white;
            name.raycastTarget = false;
            name.text = hero.Nickname;
            var outline = nameRt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var bar = new Bar { root = root, fill = fill, fillImage = fillImage, name = name, health = health };
            ShowHealth(hero, bar);
            return bar;
        }

        static RectTransform Rect(Transform parent, string name, Vector2 sizeDelta)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = sizeDelta;
            return rt;
        }

        static Image Img(RectTransform rt, Color color)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
    }
}
