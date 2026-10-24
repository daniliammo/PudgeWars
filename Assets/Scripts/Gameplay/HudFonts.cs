using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Game fonts for every Text under this canvas: text with Cyrillic letters uses the Cyrillic font, other
    /// text (numbers, Latin, symbols) the Latin one. Re-checked whenever a text changes, so labels updated at
    /// runtime ("Куплено", gold, popups) switch fonts too. Every text also gets a black outline, so it reads over
    /// any background (texts made later, such as health bars and popups, get it as they appear).
    /// </summary>
    public class HudFonts : MonoBehaviour
    {
        [Tooltip("Latin-only font (Trajan Pro Bold).")] public Font latin;
        [Tooltip("Font with Cyrillic (Friz Quadrata).")] public Font cyrillic;
        public Color outlineColor = Color.black;
        public Vector2 outlineDistance = new(2f, -2f);

        readonly List<Text> texts = new();

        public Font FontFor(string text) => HasCyrillic(text) || latin == null ? cyrillic : latin;

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
                if (!t.TryGetComponent<Outline>(out _))
                {
                    var outline = t.gameObject.AddComponent<Outline>();
                    outline.effectColor = outlineColor;
                    outline.effectDistance = outlineDistance;
                }
            }
        }
    }
}
