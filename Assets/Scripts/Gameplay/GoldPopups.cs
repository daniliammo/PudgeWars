using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// HUD popups for gold rewards in the manner of Dota's bounty: "+N монет" pops up over the hero who got the
    /// gold, floats up slowing down and dissolves, while a few coins (the original's Gold icon) jump out of the
    /// hero, spin and fall, steadily fading. Follows the hero on screen, so it works in every
    /// camera view.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class GoldPopups : MonoBehaviour
    {
        public Font font;
        public string format = "+{0} монет";
        public Color textColor = new(1f, 0.82f, 0.25f);
        public int fontSize = 40;
        [Tooltip("Height above the hero's feet the popup is anchored to, metres.")] public float anchorHeight = 3.9f;
        public float lifetime = 2f;
        [Tooltip("Text rise over the lifetime, canvas units.")] public float rise = 110f;
        [Tooltip("Share of the lifetime after which the text starts to dissolve.")] [Range(0f, 1f)] public float textFadeFrom = 0.5f;
        [Tooltip("Coin picture (Shop_Game/Gold of the original); a coin drawn at runtime if empty.")]
        public Sprite coinSprite;
        public int coinCount = 5;
        public float coinSize = 40f;
        public float coinLifetime = 1.2f;
        [Tooltip("Coins start this far below the text: from the hero's body, canvas units.")] public float coinStartDrop = 150f;
        public Vector2 coinSpeedX = new(-150f, 150f);
        public Vector2 coinSpeedY = new(260f, 440f);
        public float coinGravity = 900f;

        class Coin
        {
            public RectTransform rt;
            public Image image;
            public Vector2 position, velocity;
            public float spin, phase;
        }

        class Popup
        {
            public Transform hero;
            public RectTransform root;
            public Text label;
            public readonly List<Coin> coins = new();
            public float age;
        }

        readonly List<Popup> popups = new();
        readonly List<(HeroInventory wallet, System.Action<int> handler)> subscriptions = new();
        RectTransform rect;
        Canvas canvas;
        static Sprite drawnCoin;

        public int ActiveCount => popups.Count;
        public string LastText { get; private set; }

        void Awake()
        {
            rect = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
        }

        void Start()
        {
            foreach (var wallet in FindObjectsByType<HeroInventory>(FindObjectsSortMode.None))
            {
                var hero = wallet.transform;
                System.Action<int> handler = amount => Show(hero, amount);
                wallet.GoldAdded += handler;
                subscriptions.Add((wallet, handler));
            }
        }

        void OnDestroy()
        {
            foreach (var (wallet, handler) in subscriptions)
                if (wallet != null) wallet.GoldAdded -= handler;
        }

        public void Show(Transform hero, int amount)
        {
            var root = new GameObject("GoldPopup", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(rect, false);
            root.sizeDelta = Vector2.zero;
            var popup = new Popup { hero = hero, root = root };

            for (int i = 0; i < coinCount; i++)
            {
                var coin = new GameObject("Coin", typeof(RectTransform)).GetComponent<RectTransform>();
                coin.SetParent(root, false);
                coin.sizeDelta = new Vector2(coinSize, coinSize);
                var image = coin.gameObject.AddComponent<Image>();
                image.sprite = coinSprite != null ? coinSprite : DrawnCoin();
                image.preserveAspect = true;
                image.raycastTarget = false;
                popup.coins.Add(new Coin
                {
                    rt = coin,
                    image = image,
                    position = new Vector2(0f, -coinStartDrop),
                    velocity = new Vector2(Random.Range(coinSpeedX.x, coinSpeedX.y), Random.Range(coinSpeedY.x, coinSpeedY.y)),
                    spin = Random.Range(5f, 10f),
                    phase = Random.Range(0f, Mathf.PI),
                });
            }

            var labelRt = new GameObject("Amount", typeof(RectTransform)).GetComponent<RectTransform>();
            labelRt.SetParent(root, false);
            labelRt.sizeDelta = new Vector2(400f, fontSize * 1.5f);
            popup.label = labelRt.gameObject.AddComponent<Text>();
            popup.label.font = font;
            popup.label.fontSize = fontSize;
            popup.label.fontStyle = FontStyle.Bold;
            popup.label.alignment = TextAnchor.MiddleCenter;
            popup.label.color = textColor;
            popup.label.raycastTarget = false;
            popup.label.text = LastText = string.Format(format, amount);
            labelRt.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2f, -2f);

            popups.Add(popup);
            Place(popup);
        }

        void LateUpdate()
        {
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var p = popups[i];
                p.age += Time.deltaTime;
                if (p.age >= lifetime || p.hero == null)
                {
                    Destroy(p.root.gameObject);
                    popups.RemoveAt(i);
                    continue;
                }
                Place(p);

                float t = p.age / lifetime;
                var labelRt = p.label.rectTransform;
                // pop in (overshoot, settle), then float up slowing down (ease-out cubic) and dissolve
                labelRt.anchoredPosition = new Vector2(0f, rise * (1f - Mathf.Pow(1f - t, 3f)));
                labelRt.localScale = Vector3.one * (p.age < 0.12f
                    ? Mathf.Lerp(0.3f, 1.3f, p.age / 0.12f)
                    : Mathf.Lerp(1.3f, 1f, Mathf.SmoothStep(0f, 1f, (p.age - 0.12f) / 0.18f)));
                var c = textColor;
                c.a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(textFadeFrom, 1f, t));
                p.label.color = c;

                foreach (var coin in p.coins)
                {
                    float ct = p.age / coinLifetime;
                    if (ct >= 1f)
                    {
                        coin.image.enabled = false;
                        continue;
                    }
                    coin.velocity.y -= coinGravity * Time.deltaTime;
                    coin.position += coin.velocity * Time.deltaTime;
                    coin.rt.anchoredPosition = coin.position;
                    // a spinning coin: its width follows the cosine of the turn; it shrinks a little and fades steadily
                    float size = Mathf.Lerp(1f, 0.7f, ct);
                    coin.rt.localScale = new Vector3(size * Mathf.Max(0.15f, Mathf.Abs(Mathf.Cos(coin.phase + p.age * coin.spin))), size, 1f);
                    coin.image.color = new Color(1f, 1f, 1f, 1f - ct);
                }
            }
        }

        void Place(Popup p)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var screen = cam.WorldToScreenPoint(p.hero.position + Vector3.up * anchorHeight);
            bool visible = screen.z > 0f;
            p.root.gameObject.SetActive(visible);
            if (!visible) return;
            var uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, uiCam, out var local))
                p.root.anchoredPosition = local;
        }

        /// <summary>A gold coin drawn once at runtime: rim, face and a highlight.</summary>
        static Sprite DrawnCoin()
        {
            if (drawnCoin != null) return drawnCoin;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var rim = new Color(0.72f, 0.45f, 0.05f);
            var face = new Color(1f, 0.80f, 0.20f);
            var shine = new Color(1f, 0.95f, 0.65f);
            float r = size / 2f - 1f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - size / 2f, dy = y + 0.5f - size / 2f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                Color col = d > r - 7f ? rim : face;
                if (d < r - 7f && d > r - 11f) col = Color.Lerp(face, rim, 0.45f); // inner ring
                if (dx < -4f && dy > 4f && d < r - 13f && d > r - 20f) col = shine;
                col.a = Mathf.Clamp01(r - d + 0.5f); // antialiased edge
                tex.SetPixel(x, y, col);
            }
            tex.Apply();
            drawnCoin = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return drawnCoin;
        }
    }
}
