using System.Collections;
using System.Linq;
using NUnit.Framework;
using PudgeWars.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PudgeWars.Tests
{
    public class SdfTextTests
    {
        [UnityTest]
        public IEnumerator OutlineKeepsTheColoredFaceAndPanelVisibleAtSmallSizes()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Text rendering needs a GPU.");
            var root = new GameObject("SdfColorTest");
            var target = new RenderTexture(512, 256, 24);
            var capture = new Texture2D(512, 256, TextureFormat.RGBA32, false);
            var previousTarget = RenderTexture.active;
            try
            {
                var cameraGo = new GameObject("Camera", typeof(Camera));
                cameraGo.transform.SetParent(root.transform, false);
                var camera = cameraGo.GetComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 128f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 10f;
                camera.cullingMask = 1 << 30;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.targetTexture = target;
                var request = new RenderPipeline.StandardRequest { destination = target };
                if (GraphicsSettings.currentRenderPipeline != null && !RenderPipeline.SupportsRenderRequest(camera, request))
                    Assert.Ignore("The active render pipeline does not support camera render requests.");
                var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
                canvasGo.transform.SetParent(root.transform, false);
                canvasGo.layer = 30;
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
                panel.transform.SetParent(canvas.transform, false);
                panel.layer = 30;
                var panelRect = (RectTransform)panel.transform;
                panelRect.anchorMin = Vector2.zero; panelRect.anchorMax = Vector2.one;
                panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
                panel.GetComponent<Image>().color = new Color(0f, 0.25f, 0.65f);
                var labelGo = new GameObject("Label", typeof(RectTransform));
                labelGo.transform.SetParent(canvas.transform, false);
                labelGo.layer = 30;
                var label = labelGo.AddComponent<UiText>();
                label.font = Resources.Load<UiFontLibrary>("Text/UiFonts").cyrillic;
                label.color = new Color(1f, 0.75f, 0.2f);
                label.text = "Здоровье 100/100";
                label.alignment = TextAnchor.MiddleCenter;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.rectTransform.sizeDelta = new Vector2(500f, 128f);
                var outline = TextOutline.Add(labelGo, 2f, Color.black);
                yield return null;
                foreach (int size in new[] { 18, 28, 44 })
                {
                    label.fontSize = size;
                    outline.enabled = false;
                    var plain = Draw();
                    int plainFace = plain.Count(IsFace);
                    Assert.Greater(plainFace, 10, "The reference label must render colored glyphs at size " + size);
                    outline.enabled = true;
                    var outlined = Draw();
                    Assert.GreaterOrEqual(outlined.Count(IsFace), plainFace * 0.8f,
                        "The black outline must not consume the colored glyph interiors at size " + size);
                    Assert.Greater(outlined.Count(c => c.b > c.r + 30 && c.b > c.g + 20), target.width * target.height / 2,
                        "The surrounding panel must retain its color.");
                }

                Color32[] Draw()
                {
                    Canvas.ForceUpdateCanvases();
                    label.ForceMeshUpdate();
                    if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
                    else RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = target;
                    capture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    capture.Apply();
                    return capture.GetPixels32();
                }
            }
            finally
            {
                RenderTexture.active = previousTarget;
                Object.Destroy(root);
                Object.Destroy(capture);
                target.Release();
                Object.Destroy(target);
            }
        }

        static bool IsFace(Color32 color) => color.r > 170 && color.g > 90 && color.b < 120;

        [UnityTest]
        public IEnumerator CyrillicUsesSdfAndOutlineDoesNotMultiplyGlyphGeometry()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Text rendering needs a GPU.");
            var root = new GameObject("SdfTestCanvas", typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            try
            {
                var go = new GameObject("SdfLabel", typeof(RectTransform));
                go.transform.SetParent(root.transform, false);
                var text = go.AddComponent<UiText>();
                var library = Resources.Load<UiFontLibrary>("Text/UiFonts");
                Assert.IsNotNull(library);
                text.font = library.cyrillic;
                text.fontSize = 32;
                text.rectTransform.sizeDelta = new Vector2(800, 160);
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.text = "Здоровье 100/100";
                TextOutline.Add(go, 1.6f, Color.black, 0.5f);
                yield return null;
                Canvas.ForceUpdateCanvases();
                text.ForceMeshUpdate();
                var font = ((TMP_Text)text).font;
                Assert.AreSame(UiFonts.For(library.cyrillic), font, "Labels must share one SDF atlas per source font.");
                Assert.IsTrue(font.HasCharacters(text.text), "The Russian UI must have real glyphs.");
                StringAssert.Contains("Distance Field", text.fontSharedMaterial.shader.name);
                Assert.Greater(text.fontSharedMaterial.GetFloat("_OutlineWidth"), 0f);
                Assert.AreEqual(text.textInfo.characterInfo.Take(text.textInfo.characterCount).Count(c => c.isVisible) * 4,
                    text.textInfo.meshInfo.Sum(m => m.vertexCount), "An outline must keep one quad per visible glyph.");
                Assert.IsNull(go.GetComponent<BaseMeshEffect>(), "No raster mesh duplication remains.");
                var another = new GameObject("AnotherLabel", typeof(RectTransform));
                another.transform.SetParent(root.transform, false);
                var label = another.AddComponent<UiText>();
                label.font = library.cyrillic; label.fontSize = 32;
                TextOutline.Add(another, 1.6f, Color.black, 0.5f);
                Assert.AreSame(text.fontSharedMaterial, label.fontSharedMaterial, "Equal styles must share their material.");

                // Reproduce the cache reset with labels left alive, as in this project's fast Enter Play Mode.
                UiFonts.ResetLoadedLabels();
                text.text = "Новые символы: ЯЁЖ";
                yield return null;
                text.ForceMeshUpdate();
                var recreated = ((TMP_Text)text).font;
                Assert.AreNotSame(font, recreated);
                Assert.IsNotNull(recreated.atlasTexture, "Re-entering Play must not leave destroyed atlas references.");
                Assert.IsTrue(recreated.HasCharacters(text.text));
                Assert.AreSame(recreated.atlasTexture, text.fontSharedMaterial.mainTexture);
            }
            finally { Object.Destroy(root); }
        }
    }
}
