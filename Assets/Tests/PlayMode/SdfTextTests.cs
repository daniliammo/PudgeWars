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
            }
            finally { Object.Destroy(root); }
        }
    }
}
