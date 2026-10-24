using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Game fonts for every Text under this canvas: text with Cyrillic letters uses the Cyrillic font, other
    /// text (numbers, Latin, symbols) the Latin one. Re-checked whenever a text changes, so labels updated at
    /// runtime ("Куплено", gold, popups) switch fonts too.
    /// </summary>
    public class HudFonts : MonoBehaviour
    {
        [Tooltip("Latin-only font (Trajan Pro Bold).")] public Font latin;
        [Tooltip("Font with Cyrillic (Friz Quadrata).")] public Font cyrillic;

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
            }
        }
    }
}
