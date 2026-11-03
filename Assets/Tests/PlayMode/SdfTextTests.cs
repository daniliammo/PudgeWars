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

                // Actual HP/mana sizes use bold Latin digits. Empty glyph padding must show the original bar,
                // including when the 1920x1080 HUD is scaled down on a smaller screen.
                label.font = Resources.Load<UiFontLibrary>("Text/UiFonts").latin;
                label.text = "8";
                label.color = Color.white;
                label.fontStyle = FontStyle.Bold;
                label.enabled = false;
                var panelOnly = Draw();
                label.enabled = true;
                int transparentSamples = 0;
                foreach (float scale in new[] { 1f, 2f / 3f })
                foreach (int size in new[] { 11, 13, 16 })
                {
                    label.rectTransform.localScale = Vector3.one * scale;
                    label.fontSize = size;
                    TextOutline.Add(labelGo, size == 16 ? 1.6f : 1.2f, Color.black, size == 16 ? 0.6f : 0.5f);
                    var pixels = Draw();
                    transparentSamples += AssertTransparentPadding(pixels, panelOnly, size, scale);
                }
                Assert.Greater(transparentSamples, 10, "The test must sample transparent atlas padding.");

                int AssertTransparentPadding(Color32[] pixels, Color32[] background, int size, float scale)
                {
                    var character = label.textInfo.characterInfo[0];
                    var mesh = label.textInfo.meshInfo[character.materialReferenceIndex];
                    int index = character.vertexIndex;
                    var bottom = camera.WorldToScreenPoint(label.transform.TransformPoint(mesh.vertices[index]));
                    var top = camera.WorldToScreenPoint(label.transform.TransformPoint(mesh.vertices[index + 2]));
                    var uvBottom = mesh.uvs0[index];
                    var uvTop = mesh.uvs0[index + 2];
                    var atlas = (Texture2D)label.fontSharedMaterial.mainTexture;
                    var atlasPixels = atlas.GetPixels32();
                    int sampled = 0, biggestDifference = 0;
                    for (int y = Mathf.Max(0, Mathf.FloorToInt(bottom.y)); y < Mathf.Min(target.height, Mathf.CeilToInt(top.y)); y++)
                    for (int x = Mathf.Max(0, Mathf.FloorToInt(bottom.x)); x < Mathf.Min(target.width, Mathf.CeilToInt(top.x)); x++)
                    {
                        float fx = (x + 0.5f - bottom.x) / (top.x - bottom.x);
                        float fy = (y + 0.5f - bottom.y) / (top.y - bottom.y);
                        if (fx < 0f || fx > 1f || fy < 0f || fy > 1f) continue;
                        // A pixel with an empty centre can still cover glyph texels at its MSAA samples.
                        // Only compare padding when the whole pixel footprint and bilinear filter taps are empty.
                        float u0 = Mathf.LerpUnclamped(uvBottom.x, uvTop.x, (x - bottom.x) / (top.x - bottom.x));
                        float u1 = Mathf.LerpUnclamped(uvBottom.x, uvTop.x, (x + 1f - bottom.x) / (top.x - bottom.x));
                        float v0 = Mathf.LerpUnclamped(uvBottom.y, uvTop.y, (y - bottom.y) / (top.y - bottom.y));
                        float v1 = Mathf.LerpUnclamped(uvBottom.y, uvTop.y, (y + 1f - bottom.y) / (top.y - bottom.y));
                        int ax0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(u0, u1) * atlas.width - 0.5f), 0, atlas.width - 1);
                        int ax1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(u0, u1) * atlas.width - 0.5f), 0, atlas.width - 1);
                        int ay0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(v0, v1) * atlas.height - 0.5f), 0, atlas.height - 1);
                        int ay1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(v0, v1) * atlas.height - 0.5f), 0, atlas.height - 1);
                        bool empty = true;
                        for (int ay = ay0; ay <= ay1 && empty; ay++)
                        for (int ax = ax0; ax <= ax1; ax++)
                            if (atlasPixels[ay * atlas.width + ax].a != 0) { empty = false; break; }
                        if (!empty) continue;
                        int pixel = y * target.width + x;
                        var actual = pixels[pixel];
                        var expected = background[pixel];
                        biggestDifference = Mathf.Max(biggestDifference, Mathf.Abs(actual.r - expected.r),
                            Mathf.Abs(actual.g - expected.g), Mathf.Abs(actual.b - expected.b));
                        sampled++;
                    }
                    Assert.LessOrEqual(biggestDifference, 4,
                        $"Empty glyph quads must not put a white/gray veil over the bar (size {size}, scale {scale}).");
                    return sampled;
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
