using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Health bars over the heroes in the manner of Dota 2: a black frame with the health fill (green for the
    /// local player's team, red for enemies), a light gloss on its upper half, thin notches every <see cref="segmentHealth"/>,
    /// "current / max" health in white with a black outline over the bar, the hero's name above ("Бот …" for bots)
    /// and its level in a dark circle with a light gold digit at the bar's left end, just below the name, with the
    /// multi-kill ("×3", kills within the multi-kill window, from Double Kill on) in orange to the left of the circle. Follows each hero on screen (never leaving it at the top, where the far
    /// heroes stand), or its body while it has one (<see cref="Corpse"/>), and hides behind the camera.
    /// Lost health shows as in Dota: the fill drops at once, a white trail stays where it was and after a moment runs
    /// down to it. Regeneration shows the other way: a dark strip (dark green for allies, dark red for enemies) runs
    /// ahead of the fill over the health the next <see cref="regenPreviewSeconds"/> bring back, and "+2.5" (health per
    /// second) stands inside the bar at its right end. A dead hero's bar stays a while at 0 (the trail running out), then fades
    /// away until the respawn.
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
        [Tooltip("Diameter of the level circle, canvas units.")] public float levelSize = 30f;
        public int levelFontSize = 19;
        public Color levelTextColor = new(1f, 0.9f, 0.6f);
        public Color levelCircleColor = new(0.08f, 0.08f, 0.1f, 0.95f);
        public Color levelRimColor = new(0.85f, 0.68f, 0.3f);
        [Tooltip("{0} = kills of the current multi-kill; shown from a Double Kill on, while its window lasts.")]
        public string multiKillFormat = "×{0}";
        public int multiKillFontSize = 20;
        public Color multiKillTextColor = new(1f, 0.55f, 0.15f);
        [Tooltip("Bars of heroes at the far edge of the view are held this far below the top of the screen, canvas units.")]
        public float topMargin = 40f;
        [Tooltip("The lost health's trail behind the fill.")] public Color trailColor = new(1f, 1f, 1f, 0.9f);
        [Tooltip("Seconds the trail stays where the health was before it runs down (each new hit starts it again).")]
        public float trailHold = 0.35f;
        [Tooltip("How fast the trail runs down, bars per second.")] public float trailSpeed = 1.1f;
        [Header("Regeneration")]
        [Tooltip("The strip ahead of the fill shows the health regenerated over this many seconds.")]
        public float regenPreviewSeconds = 10f;
        public Color playerRegenColor = new(0.1f, 0.34f, 0.07f);
        public Color enemyRegenColor = new(0.36f, 0.06f, 0.04f);
        [Tooltip("{0} = health regenerated per second; shown inside the bar at its right end.")]
        public string regenFormat = "+{0:0.#}";
        public int regenFontSize = 13;
        public Color regenTextColor = new(0.65f, 1f, 0.5f);
        [Header("First person")]
        [Tooltip("In first person the own hero's bar stands at the bottom of the screen, its centre this high, canvas units (over the ability bar).")]
        public float firstPersonY = 205f;
        [Tooltip("And this many times bigger.")]
        public float firstPersonScale = 1.8f;
        [Tooltip("Seconds a dead hero's bar stays shown at 0, its fading included.")] public float deadShowTime = 2f;
        [Tooltip("Seconds of fading at the end of that.")] public float deadFadeTime = 0.4f;

        class Bar
        {
            public RectTransform root, fill, trail, regen;
            public CanvasGroup group;
            public float trailFraction, lastFraction, holdUntil;
            public float deadSince = -1f; // -1 while alive
            public Image fillImage, regenImage;
            public Text name, health, level, multiKill, regenText;
            public int shownHealth = -1, shownMax = -1, shownLevel = -1, shownMultiKill = -1;
            public float shownRegen = -1f;
        }

        readonly Dictionary<Pudge, Bar> bars = new();
        readonly List<Pudge> gone = new();
        RectTransform rect;
        Canvas canvas;
        TopDownCamera follow;

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
        public (RectTransform root, RectTransform fill, Text name) Of(Pudge hero) =>
            bars.TryGetValue(hero, out var b) ? (b.root, b.fill, b.name) : default;

        /// <summary>The lost health's white trail behind a hero's fill (null if it has no bar yet).</summary>
        public RectTransform Trail(Pudge hero) => bars.TryGetValue(hero, out var b) ? b.trail : null;

        /// <summary>The regeneration strip ahead of a hero's fill (null if it has no bar yet).</summary>
        public RectTransform RegenStrip(Pudge hero) => bars.TryGetValue(hero, out var b) ? b.regen : null;

        /// <summary>The "+2.5" regeneration label at the right end of a hero's bar (null if it has no bar yet; inactive without regeneration).</summary>
        public Text RegenLabel(Pudge hero) => bars.TryGetValue(hero, out var b) ? b.regenText : null;

        /// <summary>The "current / max" health label on a hero's bar (null if it has none yet).</summary>
        public Text HealthLabel(Pudge hero) => bars.TryGetValue(hero, out var b) ? b.health : null;

        /// <summary>The level digit in the circle on a hero's bar (null if it has none yet).</summary>
        public Text LevelLabel(Pudge hero) => bars.TryGetValue(hero, out var b) ? b.level : null;

        /// <summary>The multi-kill next to the level circle (null if the hero has no bar yet; inactive below a Double Kill).</summary>
        public Text MultiKillLabel(Pudge hero) => bars.TryGetValue(hero, out var b) ? b.multiKill : null;

        void LateUpdate()
        {
            foreach (var hero in Pudge.All)
                if (!bars.ContainsKey(hero)) bars[hero] = Create(hero);

            gone.Clear();
            foreach (var (hero, bar) in bars)
            {
                if (hero == null || !hero.isActiveAndEnabled)
                {
                    gone.Add(hero);
                    continue;
                }
                bar.name.text = hero.DisplayName;
                bool ally = hero.team == LocalPlayer.Team; // teams may change in a network game
                bar.fillImage.color = ally ? playerColor : enemyColor;
                bar.regenImage.color = ally ? playerRegenColor : enemyRegenColor;
                ShowFill(hero, bar);
                ShowRegen(hero, bar);
                ShowHealth(hero, bar);
                ShowLevel(hero, bar);
                ShowMultiKill(hero, bar);
                Place(hero, bar);
            }
            foreach (var hero in gone)
            {
                if (bars[hero].root != null) Destroy(bars[hero].root.gameObject);
                bars.Remove(hero);
            }
        }

        static float Fraction(Pudge hero) => hero.maxHealth > 0f ? Mathf.Clamp01(hero.Health / hero.maxHealth) : 0f;

        /// <summary>The fill follows the health at once; the trail stays on a drop, then runs down; a heal takes it along.</summary>
        void ShowFill(Pudge hero, Bar bar)
        {
            float fraction = Fraction(hero), now = Time.time;
            if (fraction < bar.lastFraction - 1e-4f) bar.holdUntil = now + trailHold;
            bar.lastFraction = fraction;
            if (fraction >= bar.trailFraction) bar.trailFraction = fraction;
            else if (now >= bar.holdUntil) bar.trailFraction = Mathf.MoveTowards(bar.trailFraction, fraction, trailSpeed * Time.deltaTime);
            bar.fill.anchorMax = new Vector2(fraction, 1f);
            bar.trail.anchorMax = new Vector2(bar.trailFraction, 1f);
        }

        /// <summary>The strip over the health coming back in <see cref="regenPreviewSeconds"/>, and the regeneration per second.</summary>
        void ShowRegen(Pudge hero, Bar bar)
        {
            float regen = hero.IsAlive ? Mathf.Max(0f, hero.HealthRegen) : 0f;
            float ahead = hero.maxHealth > 0f
                ? Mathf.Clamp01((Mathf.Max(0f, hero.Health) + regen * regenPreviewSeconds) / hero.maxHealth)
                : 0f;
            bar.regen.anchorMax = new Vector2(ahead, 1f);
            if (Mathf.Approximately(regen, bar.shownRegen)) return;
            bar.shownRegen = regen;
            bar.regenText.text = string.Format(CultureInfo.InvariantCulture, regenFormat, regen); // "+2.5" in any locale
            bar.regenText.gameObject.SetActive(regen > 0f);
        }

        /// <summary>Rebuilds the text only when the numbers change (no garbage every frame).</summary>
        void ShowHealth(Pudge hero, Bar bar)
        {
            int health = Mathf.CeilToInt(Mathf.Max(0f, hero.Health)), max = Mathf.CeilToInt(hero.maxHealth);
            if (health == bar.shownHealth && max == bar.shownMax) return;
            bar.shownHealth = health;
            bar.shownMax = max;
            bar.health.text = string.Format(healthFormat, health, max);
        }

        void ShowLevel(Pudge hero, Bar bar)
        {
            if (hero.Level == bar.shownLevel) return;
            bar.shownLevel = hero.Level;
            bar.level.text = hero.Level.ToString();
        }

        void ShowMultiKill(Pudge hero, Bar bar)
        {
            int kills = Arena.Instance != null ? Arena.Instance.MultiKillOf(hero) : 0;
            if (kills == bar.shownMultiKill) return;
            bar.shownMultiKill = kills;
            bar.multiKill.text = string.Format(multiKillFormat, kills);
            bar.multiKill.gameObject.SetActive(kills >= 2);
        }

        /// <summary>The player's camera looks from the hero's eyes.</summary>
        bool IsFirstPerson
        {
            get
            {
                var cam = Camera.main;
                if (follow == null || follow.gameObject != (cam != null ? cam.gameObject : null))
                    follow = cam != null ? cam.GetComponent<TopDownCamera>() : null;
                return follow != null && follow.IsFirstPerson;
            }
        }

        /// <summary>The bar of the local hero at the bottom of the screen (first person), or over its head.</summary>
        public bool OwnBarAtBottom => LocalPlayer.Hero != null && IsFirstPerson;

        void Place(Pudge hero, Bar bar)
        {
            float now = Time.time;
            if (hero.IsAlive) bar.deadSince = -1f;
            else if (bar.deadSince < 0f) bar.deadSince = now;
            float deadFor = bar.deadSince < 0f ? 0f : now - bar.deadSince;
            bar.group.alpha = Mathf.Clamp01((deadShowTime - deadFor) / Mathf.Max(deadFadeTime, 0.01f));

            // first person: the own hero is not in the picture, its bar stands at the bottom of the screen
            if (hero == LocalPlayer.Hero && IsFirstPerson)
            {
                bar.root.gameObject.SetActive(deadFor < deadShowTime);
                bar.root.anchoredPosition = new Vector2(0f, rect.rect.yMin + firstPersonY);
                bar.root.localScale = Vector3.one * firstPersonScale;
                return;
            }
            bar.root.localScale = Vector3.one;

            // over the body while there is one: it took the hero's place, its head as high under the bar as the hero's was
            var body = hero.Body;
            var anchor = body != null && body.Head != null
                ? body.Head.position - body.HeadOffset + Vector3.up * anchorHeight
                : hero.transform.position + Vector3.up * anchorHeight;
            var cam = Camera.main;
            var screen = cam != null ? cam.WorldToScreenPoint(anchor) : Vector3.zero;
            bool visible = deadFor < deadShowTime && screen.z > 0f;
            bar.root.gameObject.SetActive(visible);
            if (!visible) return;
            var uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, uiCam, out var local))
                bar.root.anchoredPosition = new Vector2(local.x, Mathf.Min(local.y, rect.rect.yMax - topMargin));
        }

        Bar Create(Pudge hero)
        {
            var root = Rect(rect, "HealthBar_" + hero.name, size + Vector2.one * (2f * border));
            Img(root, frameColor);

            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var inner = Rect(root, "Inner", size);
            Img(inner, emptyColor);
            bool ally = hero.team == LocalPlayer.Team;
            // the health regeneration brings back, under the trail and the fill: only its part ahead of the fill shows
            var regen = Rect(inner, "Regen", Vector2.zero);
            regen.anchorMin = Vector2.zero;
            regen.anchorMax = Vector2.one;
            regen.pivot = new Vector2(0f, 0.5f);
            var regenImage = Img(regen, ally ? playerRegenColor : enemyRegenColor);
            // the lost health's trail, under the fill
            var trail = Rect(inner, "Trail", Vector2.zero);
            trail.anchorMin = Vector2.zero;
            trail.anchorMax = Vector2.one;
            trail.pivot = new Vector2(0f, 0.5f);
            Img(trail, trailColor);
            var fill = Rect(inner, "Fill", Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0f, 0.5f);
            var fillImage = Img(fill, ally ? playerColor : enemyColor);

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
            TextOutline.Add(health.gameObject, 1.6f, bold: 0.6f);

            var nameRt = Rect(root, "Nickname", new Vector2(400f, nameFontSize * 1.4f));
            nameRt.anchoredPosition = new Vector2(0f, size.y / 2f + border + nameFontSize * 0.75f);
            var name = nameRt.gameObject.AddComponent<Text>();
            name.font = font;
            name.fontSize = nameFontSize;
            name.fontStyle = FontStyle.Bold;
            name.alignment = TextAnchor.MiddleCenter;
            name.color = Color.white;
            name.raycastTarget = false;
            name.text = hero.DisplayName;
            TextOutline.Add(name.gameObject, 1.6f, new Color(0f, 0f, 0f, 0.9f));

            // level: a dark circle with a gold rim hanging off the bar's left end, as on Dota's bars
            var circle = Rect(root, "Level", Vector2.one * levelSize);
            circle.anchoredPosition = new Vector2(-(size.x / 2f + border), 0f);
            Img(circle, levelRimColor).sprite = Circle();
            var face = Rect(circle, "Face", Vector2.one * (levelSize - 4f));
            Img(face, levelCircleColor).sprite = Circle();
            var levelRt = Rect(circle, "Digit", Vector2.one * levelSize);
            var level = levelRt.gameObject.AddComponent<Text>();
            level.font = font;
            level.fontSize = levelFontSize;
            level.fontStyle = FontStyle.Bold;
            level.alignment = TextAnchor.MiddleCenter;
            level.color = levelTextColor;
            level.raycastTarget = false;
            level.horizontalOverflow = HorizontalWrapMode.Overflow;
            level.verticalOverflow = VerticalWrapMode.Overflow;
            TextOutline.Add(level.gameObject, 1.2f, bold: 0.5f);

            // multi-kill: orange "×N" right-aligned against the level circle's left side
            var multiKillRt = Rect(root, "MultiKill", new Vector2(80f, levelSize));
            multiKillRt.pivot = new Vector2(1f, 0.5f);
            multiKillRt.anchoredPosition = new Vector2(circle.anchoredPosition.x - levelSize / 2f - 3f, 0f);
            var multiKill = multiKillRt.gameObject.AddComponent<Text>();
            multiKill.font = font;
            multiKill.fontSize = multiKillFontSize;
            multiKill.fontStyle = FontStyle.Bold;
            multiKill.alignment = TextAnchor.MiddleRight;
            multiKill.color = multiKillTextColor;
            multiKill.raycastTarget = false;
            multiKill.horizontalOverflow = HorizontalWrapMode.Overflow;
            multiKill.verticalOverflow = VerticalWrapMode.Overflow;
            TextOutline.Add(multiKill.gameObject, 1.6f, bold: 0.6f);

            // regeneration per second: "+2.5" inside the bar at its right end, clear of the centred health
            var regenTextRt = Rect(root, "RegenRate", new Vector2(size.x - 8f, size.y + 6f));
            var regenText = regenTextRt.gameObject.AddComponent<Text>();
            regenText.font = font;
            regenText.fontSize = regenFontSize;
            regenText.fontStyle = FontStyle.Bold;
            regenText.alignment = TextAnchor.MiddleRight;
            regenText.color = regenTextColor;
            regenText.raycastTarget = false;
            regenText.horizontalOverflow = HorizontalWrapMode.Overflow;
            regenText.verticalOverflow = VerticalWrapMode.Overflow;
            TextOutline.Add(regenText.gameObject, 1.4f, bold: 0.5f);

            float fraction = Fraction(hero);
            var bar = new Bar
            {
                root = root, fill = fill, trail = trail, regen = regen, group = group, trailFraction = fraction,
                lastFraction = fraction, fillImage = fillImage, regenImage = regenImage, name = name, health = health,
                level = level, multiKill = multiKill, regenText = regenText,
            };
            ShowFill(hero, bar);
            ShowRegen(hero, bar);
            ShowHealth(hero, bar);
            ShowLevel(hero, bar);
            ShowMultiKill(hero, bar);
            return bar;
        }

        static Sprite circle;

        /// <summary>A white antialiased disc, tinted by the Image colour.</summary>
        static Sprite Circle()
        {
            if (circle != null) return circle;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float r = size / 2f - 1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - size / 2f, dy = y + 0.5f - size / 2f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f)));
            }
            tex.Apply();
            circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return circle;
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
