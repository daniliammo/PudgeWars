using System.Collections;
using NUnit.Framework;
using PudgeWars.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PudgeWars.Tests
{
    public class SmoothScrollingTests
    {
        GameObject canvas;
        ScrollRect view;
        SmoothScrolling smooth;
        float previousTimeScale;

        [SetUp]
        public void CreateView()
        {
            previousTimeScale = Time.timeScale;
            canvas = new GameObject("ScrollTestCanvas", typeof(RectTransform), typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var panel = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var rect = (RectTransform)panel.transform;
            rect.SetParent(canvas.transform, false);
            rect.sizeDelta = new Vector2(320f, 200f);
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(rect, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 1000f);
            view = panel.GetComponent<ScrollRect>();
            view.content = content;
            view.viewport = rect;
            view.horizontal = false;
            view.movementType = ScrollRect.MovementType.Clamped;
            view.scrollSensitivity = 80f;
            Canvas.ForceUpdateCanvases();
            view.verticalNormalizedPosition = 1f;
            smooth = SmoothScrolling.Attach(view);
        }

        [TearDown]
        public void Cleanup()
        {
            Time.timeScale = previousTimeScale;
            Object.DestroyImmediate(canvas);
        }

        void Wheel(float amount)
        {
            var data = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0f, amount) };
            // Dispatch to both components, just as the input module does.
            ExecuteEvents.Execute(view.gameObject, data, ExecuteEvents.scrollHandler);
        }

        [UnityTest]
        public IEnumerator WheelMovesGraduallyAccumulatesAndClampsWhilePaused()
        {
            yield return null;
            Time.timeScale = 0f;
            Wheel(-1f);
            Wheel(-1f);
            Assert.AreEqual(1f, view.verticalNormalizedPosition, 0.0001f, "wheel input must not jump immediately");
            Assert.IsTrue(smooth.IsAnimating);
            yield return new WaitForSecondsRealtime(0.04f);
            Assert.Less(view.verticalNormalizedPosition, 1f);
            Assert.Greater(view.verticalNormalizedPosition, 0.8f, "two wheel steps should be approached gradually");
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(0.8f, view.verticalNormalizedPosition, 0.0002f, "the queued wheel steps must accumulate");
            Assert.IsFalse(smooth.IsAnimating);
            Wheel(-100f);
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.AreEqual(0f, view.verticalNormalizedPosition, 0.0002f);
            Wheel(100f);
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.AreEqual(1f, view.verticalNormalizedPosition, 0.0002f);
        }

        [UnityTest]
        public IEnumerator ReversingTheWheelCancelsTheOldDirection()
        {
            view.verticalNormalizedPosition = 0.5f;
            smooth.Stop();
            Wheel(-2f);
            yield return new WaitForSecondsRealtime(0.04f);
            float beforeReverse = view.verticalNormalizedPosition;
            Assert.Less(beforeReverse, 0.5f);
            Wheel(1f);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(beforeReverse + 0.1f, view.verticalNormalizedPosition, 0.0002f,
                "reversing must start from the visible position, without finishing the old direction first");
        }

        [UnityTest]
        public IEnumerator DirectPositionChangesAndDraggingCancelTheWheelWithoutRemovingTouchInertia()
        {
            yield return null;
            Wheel(-2f);
            view.verticalNormalizedPosition = 0.7f; // a scrollbar or a programmatic article reset
            yield return null;
            Assert.IsFalse(smooth.IsAnimating);
            Assert.AreEqual(0.7f, view.verticalNormalizedPosition, 0.0001f);

            Wheel(-1f);
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
            };
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.beginDragHandler);
            Assert.IsFalse(smooth.IsAnimating);
            var position = view.content.anchoredPosition;
            pointer.position += Vector2.up * 40f;
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.dragHandler);
            Assert.AreEqual(position.y + 40f, view.content.anchoredPosition.y, 0.01f, "dragging must still follow the pointer directly");
            ExecuteEvents.Execute(view.gameObject, pointer, ExecuteEvents.endDragHandler);
            view.velocity = Vector2.up * 100f;
            yield return new WaitForSecondsRealtime(0.05f);
            Assert.Greater(view.velocity.y, 0f, "native touch inertia must survive after the drag ends");
        }

        [UnityTest]
        public IEnumerator ResizingAndReopeningCannotResumeOldWheelMotion()
        {
            yield return null;
            Wheel(-3f);
            view.content.sizeDelta = new Vector2(0f, 100f);
            Canvas.ForceUpdateCanvases();
            yield return null;
            Assert.IsFalse(smooth.IsAnimating);
            Assert.IsFalse(float.IsNaN(view.verticalNormalizedPosition));

            view.content.sizeDelta = new Vector2(0f, 1000f);
            Canvas.ForceUpdateCanvases();
            view.verticalNormalizedPosition = 0.5f;
            smooth.Stop();
            Wheel(-1f);
            smooth.enabled = false;
            Assert.AreEqual(80f, view.scrollSensitivity, "disabling the component must restore native wheel handling");
            smooth.enabled = true;
            yield return null;
            Assert.AreEqual(0.5f, view.verticalNormalizedPosition, 0.0001f);
            Assert.IsFalse(smooth.IsAnimating);
            Assert.AreSame(smooth, SmoothScrolling.Attach(view), "installing smoothing twice must not add another handler");
        }
    }
}
