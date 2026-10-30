using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// The effects (<see cref="Unit.Effects"/>) of the picked creature (<see cref="SelectionMarker"/>: a hero clicked to
    /// look at it, the own hero by default) as a row of round icons over the ability bar, in the
    /// bottom centre: the item's picture in a circle with a light rim, a timed effect darkening clockwise as it runs
    /// out. Hovering an icon shows the effect's name, what it does and the seconds left; an aura's also shows its reach on the
    /// ground around the allies giving it (<see cref="AuraRange"/>). Made by
    /// <see cref="AbilityBar"/> at start; follows its panel (it rises in first person).
    /// </summary>
    public class EffectBar : MonoBehaviour
    {
        [Tooltip("Diameter of an icon, canvas units.")] public float size = 58f;
        public float gap = 10f;
        [Tooltip("Height over the ability bar's top edge, canvas units.")] public float lift = 12f;
        public Color rimColor = new(0.55f, 0.85f, 0.5f);
        [Tooltip("Rim of an aura from another hero or the hero's own item.")] public Color auraRimColor = new(0.45f, 0.7f, 1f);
        public Color timerColor = new(0f, 0f, 0f, 0.6f);
        [Tooltip("Seconds between refreshes of the list (the timers run every frame).")] public float refreshInterval = 0.1f;

        class Icon
        {
            public RectTransform root;
            public Image rim, picture, timer;
        }

        /// <summary>The ability bar's panel the row stands over.</summary>
        public RectTransform anchor;
        public Font font;

        readonly List<UnitEffect> effects = new();
        readonly List<Icon> icons = new();
        RectTransform rect, tip;
        Text tipName, tipText;
        int hovered = -1;
        float nextRefresh;
        static Sprite circle;

        /// <summary>The effects shown now (for tests).</summary>
        public IReadOnlyList<UnitEffect> Shown => effects;
        /// <summary>The icons shown now.</summary>
        public int IconCount => icons.FindAll(i => i.root.gameObject.activeSelf).Count;
        /// <summary>The hover description's text, or null while hidden.</summary>
        public string TipText => tip != null && tip.gameObject.activeSelf ? tipName.text + "\n" + tipText.text : null;

        /// <summary>A row over <paramref name="abilityPanel"/>, on its canvas.</summary>
        public static EffectBar Create(RectTransform abilityPanel, Font font)
        {
            var parent = abilityPanel.parent;
            var old = parent.Find("EffectBar");
            if (old != null) Destroy(old.gameObject); // left from an earlier start
            var go = new GameObject("EffectBar", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            var bar = go.AddComponent<EffectBar>();
            bar.anchor = abilityPanel;
            bar.font = font;
            return bar;
        }

        void Awake() => rect = (RectTransform)transform;

        void Start() => BuildTip(); // after Create has given the font

        void Update()
        {
            if (anchor != null)
            {
                rect.anchoredPosition = new Vector2(anchor.anchoredPosition.x,
                    anchor.anchoredPosition.y + (1f - anchor.pivot.y) * anchor.sizeDelta.y + lift);
            }
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + refreshInterval;
                Refresh();
            }
            for (int i = 0; i < effects.Count && i < icons.Count; i++)
            {
                var e = effects[i];
                icons[i].timer.enabled = e.IsTimed;
                if (e.IsTimed) icons[i].timer.fillAmount = 1f - Mathf.Clamp01(e.Left / e.duration); // the spent part darkens
            }
            if (tip == null) return;
            if (hovered >= 0 && hovered < effects.Count) FillTip(effects[hovered]);
            else if (tip.gameObject.activeSelf) tip.gameObject.SetActive(false);
        }

        /// <summary>Whose effects the row shows: the picked creature, or the own hero when none is picked.</summary>
        public Unit Shows
        {
            get
            {
                var picked = SelectionMarker.HasInstance ? SelectionMarker.Instance.SelectedUnit : null;
                return picked != null ? picked : LocalPlayer.Hero;
            }
        }

        void Refresh()
        {
            var hero = Shows;
            if (hero != null && hero.IsAlive) hero.Effects(effects);
            else effects.Clear();
            while (icons.Count < effects.Count) icons.Add(BuildIcon(icons.Count));
            float width = effects.Count * size + Mathf.Max(0, effects.Count - 1) * gap;
            rect.sizeDelta = new Vector2(width, size);
            for (int i = 0; i < icons.Count; i++)
            {
                bool shown = i < effects.Count;
                icons[i].root.gameObject.SetActive(shown);
                if (!shown) continue;
                var e = effects[i];
                icons[i].root.anchoredPosition = new Vector2(i * (size + gap) + size / 2f, size / 2f);
                icons[i].picture.sprite = e.item != null ? e.item.icon : null;
                icons[i].rim.color = e.IsTimed ? rimColor : auraRimColor;
            }
            if (hovered >= effects.Count) hovered = -1;
            ShowAuraReach(); // the givers may come and go while it is hovered
        }

        void FillTip(UnitEffect e)
        {
            tip.gameObject.SetActive(true);
            tipName.text = e.name;
            string text = e.description;
            if (e.IsTimed) text += $"\n<color=#B8C4D0>Осталось {e.Left.ToString("0.0", CultureInfo.InvariantCulture)} с</color>";
            tipText.text = text;
            var icon = icons[hovered].root;
            tip.anchoredPosition = new Vector2(icon.anchoredPosition.x - rect.sizeDelta.x / 2f, size + 10f);
            tip.sizeDelta = new Vector2(380f, tipName.preferredHeight + tipText.preferredHeight + 26f);
        }

        Icon BuildIcon(int index)
        {
            var root = new GameObject("Effect", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(rect, false);
            root.anchorMin = root.anchorMax = new Vector2(0f, 0f);
            root.sizeDelta = Vector2.one * size;
            var rim = root.gameObject.AddComponent<Image>();
            rim.sprite = Circle();
            rim.raycastTarget = true; // the hover
            var hover = root.gameObject.AddComponent<EffectIconPointer>();
            hover.bar = this;
            hover.index = index;

            var mask = new GameObject("Mask", typeof(RectTransform)).GetComponent<RectTransform>();
            mask.SetParent(root, false);
            mask.sizeDelta = Vector2.one * (size - 6f);
            var maskImage = mask.gameObject.AddComponent<Image>();
            maskImage.sprite = Circle();
            maskImage.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var pictureRt = new GameObject("Picture", typeof(RectTransform)).GetComponent<RectTransform>();
            pictureRt.SetParent(mask, false);
            pictureRt.sizeDelta = new Vector2((size - 6f) * 88f / 64f, size - 6f); // Dota's 88 × 64 icons fill the circle
            var picture = pictureRt.gameObject.AddComponent<Image>();
            picture.raycastTarget = false;

            var timerRt = new GameObject("Timer", typeof(RectTransform)).GetComponent<RectTransform>();
            timerRt.SetParent(root, false);
            timerRt.sizeDelta = Vector2.one * (size - 6f);
            var timer = timerRt.gameObject.AddComponent<Image>();
            timer.sprite = Circle();
            timer.color = timerColor;
            timer.type = Image.Type.Filled;
            timer.fillMethod = Image.FillMethod.Radial360;
            timer.fillOrigin = (int)Image.Origin360.Top;
            timer.fillClockwise = true;
            timer.raycastTarget = false;
            return new Icon { root = root, rim = rim, picture = picture, timer = timer };
        }

        internal void Hover(int index, bool on)
        {
            if (on) hovered = index;
            else if (hovered == index) hovered = -1;
            ShowAuraReach();
        }

        /// <summary>A hovered aura shows its reach on the ground around the allies giving it (<see cref="AuraRange"/>).</summary>
        void ShowAuraReach()
        {
            if (hovered >= 0 && hovered < effects.Count && !effects[hovered].IsTimed) AuraRange.ShowFor(Shows);
            else AuraRange.Hide();
        }

        void OnDisable() => AuraRange.Hide();

        void BuildTip()
        {
            tip = new GameObject("EffectTip", typeof(RectTransform)).GetComponent<RectTransform>();
            tip.SetParent(transform, false);
            tip.anchorMin = tip.anchorMax = new Vector2(0.5f, 0f);
            tip.pivot = new Vector2(0.5f, 0f);
            var bg = tip.gameObject.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.035f, 0.045f, 0.97f);
            bg.raycastTarget = false;
            var outline = tip.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.45f, 0.7f, 1f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            tipName = TipLabel("Name", 22, FontStyle.Bold, Color.white, -10f);
            tipText = TipLabel("Text", 19, FontStyle.Normal, new Color(0.88f, 0.9f, 0.93f), -42f);
            tip.gameObject.SetActive(false);
        }

        Text TipLabel(string name, int fontSize, FontStyle style, Color color, float top)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(tip, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(14f, 0f);
            rt.offsetMax = new Vector2(-14f, top);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, 200f);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.supportRichText = true;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>A white antialiased disc, tinted by the Image colour.</summary>
        static Sprite Circle()
        {
            if (circle != null) return circle;
            const int px = 128;
            var tex = new Texture2D(px, px, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float r = px / 2f - 1f;
            for (int y = 0; y < px; y++)
            for (int x = 0; x < px; x++)
            {
                float dx = x + 0.5f - px / 2f, dy = y + 0.5f - px / 2f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f)));
            }
            tex.Apply();
            circle = Sprite.Create(tex, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f), 100f);
            return circle;
        }
    }
}
