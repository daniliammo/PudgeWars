using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Blue "+N" over a unit whenever an active use gives it mana (<see cref="Unit.AnyManaRestoredShown"/>: Arcane
    /// Boots, Enchanted Mango; mana vampirism is passive and pops nothing): it pops in, floats up slowing down and
    /// dissolves, following the unit on screen. Several at once stack upward. Made at runtime on the HUD canvas, beside
    /// the gold popups (<see cref="GoldPopups"/>), whose font it takes; on a network client the server's amounts show.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ManaPopups : MonoBehaviour
    {
        public Font font;
        public string format = "+{0}";
        public Color textColor = new(0.35f, 0.62f, 1f);
        public int fontSize = 36;
        [Tooltip("Height above the unit's feet the popup is anchored to, metres (under the gold popups).")]
        public float anchorHeight = 3.4f;
        public float lifetime = 1.6f;
        [Tooltip("Text rise over the lifetime, canvas units.")] public float rise = 90f;
        [Tooltip("Share of the lifetime after which the text starts to dissolve.")] [Range(0f, 1f)] public float fadeFrom = 0.5f;
        [Tooltip("A popup for a unit whose previous one is younger than this stacks above it, seconds.")]
        public float stackWindow = 0.5f;
        public float stackStep = 44f;

        class Popup
        {
            public Transform unit;
            public RectTransform root;
            public Text label;
            public float age, stackOffset;
        }

        static ManaPopups instance;
        readonly List<Popup> popups = new();
        RectTransform rect;
        Canvas canvas;

        public static int ActiveCount => instance != null ? instance.popups.Count : 0;
        /// <summary>The text of the newest popup (for tests); null before any.</summary>
        public static string LastText { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Listen()
        {
            Unit.AnyManaRestoredShown -= OnManaRestored;
            Unit.AnyManaRestoredShown += OnManaRestored;
        }

        static void OnManaRestored(Unit unit, float amount)
        {
            if (unit == null || amount <= 0f) return;
            var popups = instance != null ? instance : Create();
            if (popups != null) popups.Show(unit.transform, amount);
        }

        /// <summary>A layer over the HUD canvas beside the gold popups; null without a HUD (the menu).</summary>
        static ManaPopups Create()
        {
            var gold = FindAnyObjectByType<GoldPopups>();
            if (gold == null) return null;
            var parent = (RectTransform)gold.transform.parent;
            var rt = new GameObject("ManaPopups", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetSiblingIndex(gold.transform.GetSiblingIndex()); // under the gold popups
            instance = rt.gameObject.AddComponent<ManaPopups>();
            instance.font = gold.font;
            return instance;
        }

        void Awake()
        {
            rect = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        public void Show(Transform unit, float amount)
        {
            var root = new GameObject("ManaPopup", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(rect, false);
            root.sizeDelta = Vector2.zero;
            var labelRt = new GameObject("Amount", typeof(RectTransform)).GetComponent<RectTransform>();
            labelRt.SetParent(root, false);
            labelRt.sizeDelta = new Vector2(300f, fontSize * 1.5f);
            var label = labelRt.gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = textColor;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.text = LastText = string.Format(format, Mathf.RoundToInt(amount));
            TextOutline.Add(labelRt.gameObject, 2f, bold: 0.6f);

            float offset = 0f;
            foreach (var p in popups)
                if (p.unit == unit && p.age < stackWindow) offset = Mathf.Max(offset, p.stackOffset + stackStep);
            var popup = new Popup { unit = unit, root = root, label = label, stackOffset = offset };
            popups.Add(popup);
            Place(popup);
        }

        void LateUpdate()
        {
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var p = popups[i];
                p.age += Time.deltaTime;
                if (p.age >= lifetime || p.unit == null)
                {
                    Destroy(p.root.gameObject);
                    popups.RemoveAt(i);
                    continue;
                }
                Place(p);
                float t = p.age / lifetime;
                var labelRt = p.label.rectTransform;
                // pop in (overshoot, settle), then float up slowing down and dissolve
                labelRt.anchoredPosition = new Vector2(0f, rise * (1f - Mathf.Pow(1f - t, 3f)));
                labelRt.localScale = Vector3.one * (p.age < 0.1f
                    ? Mathf.Lerp(0.3f, 1.35f, p.age / 0.1f)
                    : Mathf.Lerp(1.35f, 1f, Mathf.SmoothStep(0f, 1f, (p.age - 0.1f) / 0.18f)));
                var c = textColor;
                c.a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeFrom, 1f, t));
                p.label.color = c;
            }
        }

        void Place(Popup p)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var screen = cam.WorldToScreenPoint(p.unit.position + Vector3.up * anchorHeight);
            bool visible = screen.z > 0f;
            p.root.gameObject.SetActive(visible);
            if (!visible) return;
            var uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, uiCam, out var local))
                p.root.anchoredPosition = local + new Vector2(0f, p.stackOffset);
        }
    }
}
