using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Smooth wheel/trackpad input for any existing ScrollRect. Native dragging, touch inertia and scrollbars stay
    /// direct. Runs before ScrollRect.LateUpdate so its scrollbar and value events follow the rendered position.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ScrollRect))]
    public class SmoothScrolling : MonoBehaviour, IScrollHandler, IInitializePotentialDragHandler,
        IBeginDragHandler, IEndDragHandler, IPointerDownHandler
    {
        [Tooltip("Wheel smoothing time, seconds; independent of the game's time scale.")]
        public float smoothTime = 0.08f;
        const float PixelTolerance = 0.1f;

        ScrollRect scroll;
        RectTransform lastContent;
        Vector2 target, velocity, lastPosition, lastContentSize, lastViewSize;
        float sensitivity;
        bool initialized, moving, dragging, ownsWheel;
        readonly Vector3[] corners = new Vector3[4];

        public bool IsAnimating => moving;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            if (ServerRuntime.IsDedicated) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            InstallLoaded();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => InstallLoaded();

        static void InstallLoaded()
        {
            foreach (var view in FindObjectsByType<ScrollRect>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Attach(view);
        }

        /// <summary>Also used by panels built at runtime, after their content and sensitivity have been assigned.</summary>
        public static SmoothScrolling Attach(ScrollRect view) =>
            view.TryGetComponent<SmoothScrolling>(out var smooth) ? smooth : view.gameObject.AddComponent<SmoothScrolling>();

        void OnEnable()
        {
            scroll = GetComponent<ScrollRect>();
            // The native wheel handler may run before or after ours. Zero sensitivity prevents its immediate jump;
            // all its other input and layout behavior stays enabled.
            sensitivity = scroll.scrollSensitivity;
            scroll.scrollSensitivity = 0f;
            ownsWheel = true;
            initialized = moving = dragging = false;
        }

        void OnDisable()
        {
            if (ownsWheel && scroll != null) scroll.scrollSensitivity = sensitivity;
            ownsWheel = false;
            initialized = moving = dragging = false;
            velocity = Vector2.zero;
        }

        RectTransform View => scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;

        bool Ready => scroll != null && scroll.isActiveAndEnabled && scroll.content != null;

        bool ChangedExternally() => !initialized || lastContent != scroll.content ||
            (lastPosition - scroll.content.anchoredPosition).sqrMagnitude > 0.0001f ||
            lastContentSize != scroll.content.rect.size || lastViewSize != View.rect.size;

        void Remember()
        {
            lastContent = scroll.content;
            lastPosition = lastContent.anchoredPosition;
            lastContentSize = lastContent.rect.size;
            lastViewSize = View.rect.size;
            initialized = true;
        }

        /// <summary>Cancel queued wheel motion before switching articles or resetting a panel's position.</summary>
        public void Stop()
        {
            CancelWheel();
            if (Ready) scroll.StopMovement();
        }

        void CancelWheel()
        {
            moving = false;
            velocity = Vector2.zero;
            if (!Ready) { initialized = false; return; }
            target = scroll.normalizedPosition;
            Remember();
        }

        Vector2 Overflow()
        {
            var view = View;
            scroll.content.GetWorldCorners(corners);
            var min = (Vector2)view.InverseTransformPoint(corners[0]);
            var max = min;
            for (int i = 1; i < corners.Length; i++)
            {
                var point = (Vector2)view.InverseTransformPoint(corners[i]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return Vector2.Max(Vector2.zero, max - min - view.rect.size);
        }

        public void OnScroll(PointerEventData data)
        {
            if (!Ready || dragging) return;
            Canvas.ForceUpdateCanvases();
            if (ChangedExternally()) Stop();
            if (!moving) target = scroll.normalizedPosition;

            // Match uGUI's single-axis mapping, including horizontal wheel/trackpad events.
            Vector2 delta = data.scrollDelta;
            delta.y *= -1f;
            if (scroll.vertical && !scroll.horizontal)
            {
                if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y)) delta.y = delta.x;
                delta.x = 0f;
            }
            else if (scroll.horizontal && !scroll.vertical)
            {
                if (Mathf.Abs(delta.y) > Mathf.Abs(delta.x)) delta.x = delta.y;
                delta.y = 0f;
            }

            var overflow = Overflow();
            var current = scroll.normalizedPosition;
            for (int axis = 0; axis < 2; axis++)
            {
                bool enabledAxis = axis == 0 ? scroll.horizontal : scroll.vertical;
                if (!enabledAxis || overflow[axis] <= PixelTolerance) continue;
                float step = -delta[axis] * sensitivity / overflow[axis];
                // A reversal takes effect now instead of first finishing the previous wheel movement.
                if (step * (target[axis] - current[axis]) < 0f)
                {
                    target[axis] = current[axis];
                    velocity[axis] = 0f;
                }
                target[axis] = Mathf.Clamp01(target[axis] + step);
            }
            moving = Vector2.Scale(target - current, overflow).sqrMagnitude > PixelTolerance * PixelTolerance;
            scroll.StopMovement();
        }

        void LateUpdate()
        {
            if (!Ready) { initialized = moving = false; return; }
            if (dragging || ChangedExternally()) { CancelWheel(); return; }
            if (moving)
            {
                var current = scroll.normalizedPosition;
                var next = Vector2.SmoothDamp(current, target, ref velocity, Mathf.Max(0.01f, smoothTime),
                    Mathf.Infinity, Time.unscaledDeltaTime);
                if (Vector2.Scale(target - next, Overflow()).sqrMagnitude <= PixelTolerance * PixelTolerance)
                {
                    next = target;
                    moving = false;
                    velocity = Vector2.zero;
                }
                scroll.normalizedPosition = next;
                scroll.StopMovement();
            }
            Remember();
        }

        public void OnInitializePotentialDrag(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) Stop();
        }

        public void OnBeginDrag(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left) return;
            Stop();
            dragging = true;
        }

        public void OnEndDrag(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left) return;
            dragging = false;
            // Remember the drag's position without stopping ScrollRect's native touch inertia.
            if (Ready) Remember();
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) Stop();
        }
    }
}
