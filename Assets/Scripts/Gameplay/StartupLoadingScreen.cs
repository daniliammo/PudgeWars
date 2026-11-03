using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>The first rendered UI uses only built-in uGUI resources, before game fonts or URP setup.</summary>
    public sealed class StartupLoadingScreen : MonoBehaviour
    {
        static StartupLoadingScreen instance;
        Slider bar;
        UnityEngine.UI.Text stage, percent;
        float target, displayed;

        public static bool IsVisible => instance != null && instance.bar != null && instance.gameObject.activeInHierarchy;

        public static StartupLoadingScreen Create(Transform parent)
        {
            var go = new GameObject("StartupLoadingScreen");
            go.transform.SetParent(parent, false);
            var screen = go.AddComponent<StartupLoadingScreen>();
            instance = screen;
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) screen.Build();
            return screen;
        }

        void Build()
        {
            var root = new GameObject("StartupCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var background = Rect("Background", root.transform, Vector2.zero, Vector2.one);
            background.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.03f, 0.04f);
            Label("Heading", background, "Подготовка игры", 36, new Vector2(0.2f, 0.29f), new Vector2(0.8f, 0.35f));
            stage = Label("StartupStage", background, "Запуск игры", 24, new Vector2(0.2f, 0.22f), new Vector2(0.8f, 0.28f));
            stage.color = new Color(0.7f, 0.73f, 0.78f);
            var track = Rect("StartupProgressBar", background, new Vector2(0.2f, 0.17f), new Vector2(0.74f, 0.19f));
            track.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f);
            var fill = Rect("Fill", track, Vector2.zero, Vector2.one);
            fill.gameObject.AddComponent<Image>().color = new Color(0.86f, 0.66f, 0.27f);
            bar = track.gameObject.AddComponent<Slider>();
            bar.fillRect = fill;
            bar.interactable = false;
            bar.navigation = new Navigation { mode = Navigation.Mode.None };
            percent = Label("StartupPercent", background, "0 %", 26, new Vector2(0.76f, 0.155f), new Vector2(0.8f, 0.205f));
            percent.alignment = TextAnchor.MiddleRight;
        }

        public IEnumerator Present(float progress, string description)
        {
            target = Mathf.Max(target, Mathf.Clamp01(progress));
            if (stage != null) stage.text = description;
            // Start can run before the first render, and UI geometry is built later in the frame.
            yield return null;
            yield return null;
        }

        public IEnumerator Complete()
        {
            target = 1f;
            if (stage != null) stage.text = "Готово к загрузке меню";
            while (bar != null && displayed < 1f) yield return null;
            yield return null;
        }

        void Update()
        {
            displayed = Mathf.MoveTowards(displayed, target,
                Mathf.Max(0.4f, (target - displayed) * 6f) * Mathf.Min(Time.unscaledDeltaTime, 0.033f));
            if (bar != null) bar.SetValueWithoutNotify(displayed);
            if (percent != null) percent.text = $"{Mathf.FloorToInt(displayed * 100f)} %";
        }

        public void Close() { gameObject.SetActive(false); Destroy(gameObject); }

        static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static UnityEngine.UI.Text Label(string name, Transform parent, string content, int size, Vector2 min, Vector2 max)
        {
            var label = Rect(name, parent, min, max).gameObject.AddComponent<UnityEngine.UI.Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = size;
            label.text = content;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = new Color(0.9f, 0.86f, 0.72f);
            label.raycastTarget = false;
            return label;
        }

        void OnDestroy() { if (instance == this) instance = null; }
    }
}
