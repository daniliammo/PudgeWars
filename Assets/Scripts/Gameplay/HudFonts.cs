using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Game fonts for every Text under this canvas: text with Cyrillic letters uses the Cyrillic font, other
    /// text (numbers, Latin, symbols) the Latin one. Re-checked whenever a text changes, so labels updated at
    /// runtime ("Куплено", gold, popups) switch fonts too. Every text also gets a solid black outline
    /// (<see cref="TextOutline"/>), so it reads over any background, and numbers are drawn bolder (texts made later,
    /// such as health bars and popups, get it as they appear).
    /// </summary>
    public class HudFonts : MonoBehaviour
    {
        [Tooltip("Latin-only font (Trajan Pro Bold).")] public Font latin;
        [Tooltip("Font with Cyrillic (Friz Quadrata).")] public Font cyrillic;
        public Color outlineColor = Color.black;
        [Tooltip("How far the outline reaches, canvas units.")] public float outlineWidth = 2f;
        [Tooltip("Numbers (gold, cooldowns, charges) are thickened by this much, canvas units.")] public float numberBold = 0.7f;

        readonly List<Text> texts = new();

        public Font FontFor(string text) => HasCyrillic(text) || latin == null ? cyrillic : latin;

        /// <summary>Digits and the signs around them only ("670/670", "+2.5", "1.250", "×3").</summary>
        public static bool IsNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            bool digit = false;
            foreach (char c in text)
            {
                if (char.IsDigit(c)) digit = true;
                else if (char.IsLetter(c)) return false;
            }
            return digit;
        }

        public static bool HasCyrillic(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
                if (c >= 'Ѐ' && c <= 'ӿ') return true;
            return false;
        }

        void LateUpdate()
        {
            GetComponentsInChildren(true, texts);
            foreach (var t in texts)
            {
                var font = FontFor(t.text);
                if (font != null && t.font != font) t.font = font;
                // a solid outline (uGUI's Outline leaves a gap between thin strokes and the outline); numbers drawn bolder
                if (!t.TryGetComponent<Outline>(out _) && !t.TryGetComponent<TextOutline>(out _))
                    TextOutline.Add(t.gameObject, outlineWidth, outlineColor, IsNumber(t.text) ? numberBold : 0f);
            }
        }
    }
}
