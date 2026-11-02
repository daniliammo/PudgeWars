using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Text = PudgeWars.Gameplay.UiText;

namespace PudgeWars.Gameplay
{
    /// <summary>Shared, non-interactive progress bars. Independent operations cannot dismiss each other's UI.</summary>
    public sealed class LoadingProgress : MonoBehaviour
    {
        public sealed class Operation : IDisposable
        {
            internal LoadingProgress owner;
            internal GameObject row;
            internal Text stage, percent;
            internal Slider bar;
            public float Progress { get; private set; }
            public bool IsDisposed { get; private set; }

            public void Report(float progress, string description = null)
            {
                if (IsDisposed) return;
                if (!float.IsNaN(progress)) Progress = Mathf.Max(Progress, Mathf.Clamp01(progress));
                if (bar != null) bar.SetValueWithoutNotify(Progress);
                if (percent != null) percent.text = $"{Mathf.FloorToInt(Progress * 100f)} %";
                if (stage != null && description != null) stage.text = description;
            }

            public void Dispose()
            {
                if (IsDisposed) return;
                IsDisposed = true;
                if (owner != null) owner.Remove(this);
                owner = null;
            }
        }

        static LoadingProgress instance;
        readonly List<Operation> operations = new();
        GameObject canvasObject;
        RectTransform rows;
        Button blocker;
        EventSystem capturedSystem;
        GameObject previousSelection;
        Font font;
        public static bool IsVisible => instance != null && instance.operations.Count > 0;

        public static Operation Begin(string title)
        {
            var operation = new Operation();
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return operation;
            if (instance == null)
            {
                var go = new GameObject("LoadingProgress");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<LoadingProgress>();
                instance.Build();
            }
            operation.owner = instance;
            instance.Add(operation, title);
            return operation;
        }

        void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvasObject = new GameObject("LoadingCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var background = Rect("Background", canvasObject.transform, Vector2.zero, Vector2.one);
            background.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.03f, 0.04f, 1f);
            blocker = background.gameObject.AddComponent<Button>();
            blocker.transition = Selectable.Transition.None;
            blocker.navigation = new Navigation { mode = Navigation.Mode.None };
            Label("Heading", background, "Подготовка игры", 36, new Vector2(0.12f, 0.72f), new Vector2(0.88f, 0.82f));
            rows = Rect("Operations", background, new Vector2(0.12f, 0.22f), new Vector2(0.88f, 0.68f));
            var layout = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 28;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleCenter;
        }

        void Add(Operation operation, string title)
        {
            var row = Rect("ProgressOperation", rows, Vector2.zero, Vector2.one);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 130;
            operation.row = row.gameObject;
            Label("Title", row, title, 28, new Vector2(0, 0.7f), Vector2.one);
            operation.stage = Label("Stage", row, "", 21, new Vector2(0, 0.4f), new Vector2(1, 0.68f));
            operation.stage.color = new Color(0.7f, 0.73f, 0.78f);
            var track = Rect("ProgressBar", row, new Vector2(0, 0.1f), new Vector2(0.88f, 0.32f));
            track.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f);
            var fill = Rect("Fill", track, Vector2.zero, Vector2.one);
            fill.gameObject.AddComponent<Image>().color = new Color(0.86f, 0.66f, 0.27f);
            operation.bar = track.gameObject.AddComponent<Slider>();
            operation.bar.fillRect = fill;
            operation.bar.interactable = false;
            operation.bar.navigation = new Navigation { mode = Navigation.Mode.None };
            operation.percent = Label("Percent", row, "0 %", 26, new Vector2(0.89f, 0.04f), new Vector2(1, 0.38f));
            operation.percent.alignment = TextAnchor.MiddleRight;
            operations.Add(operation);
            canvasObject.SetActive(true);
            operation.Report(0);
            CaptureSelection();
        }

        void Update() { if (operations.Count > 0) CaptureSelection(); }

        void CaptureSelection()
        {
            var system = EventSystem.current;
            if (system == null || system == capturedSystem) return;
            capturedSystem = system;
            previousSelection = system.currentSelectedGameObject;
            system.SetSelectedGameObject(blocker.gameObject);
        }

        void Remove(Operation operation)
        {
            operations.Remove(operation);
            if (operation.row != null) { operation.row.SetActive(false); Destroy(operation.row); }
            if (operations.Count > 0) return;
            if (capturedSystem != null && blocker != null && capturedSystem.currentSelectedGameObject == blocker.gameObject)
                capturedSystem.SetSelectedGameObject(previousSelection);
            capturedSystem = null;
            previousSelection = null;
            canvasObject.SetActive(false);
        }

        static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        Text Label(string name, Transform parent, string content, int size, Vector2 min, Vector2 max)
        {
            var label = Rect(name, parent, min, max).gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = size; label.text = content;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = new Color(0.9f, 0.86f, 0.72f);
            label.raycastTarget = false;
            return label;
        }

        void OnDestroy()
        {
            if (capturedSystem != null && blocker != null && capturedSystem.currentSelectedGameObject == blocker.gameObject)
                capturedSystem.SetSelectedGameObject(previousSelection);
            foreach (var operation in operations) operation.owner = null;
            if (instance == this) instance = null;
        }
    }
}
