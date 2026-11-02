using UnityEngine;

namespace PudgeWars.Gameplay
{
    /// <summary>SDF outline and face dilation, drawn in the glyph shader without extra copies of the text mesh.</summary>
    [RequireComponent(typeof(UiText))]
    public class TextOutline : MonoBehaviour
    {
        public Color color = Color.black;
        public float width = 1.6f;
        public float bold;
        UiText label;
        Color previousColor;
        float previousWidth = -1f, previousBold = -1f;

        void OnEnable() => Refresh();
        void OnDisable() { if (label != null) label.RefreshOutline(); }
        void OnValidate() => Refresh();
        void LateUpdate()
        {
            if (previousColor != color || previousWidth != width || previousBold != bold) Refresh();
        }

        void Refresh()
        {
            if (label == null) label = GetComponent<UiText>();
            previousColor = color; previousWidth = width; previousBold = bold;
            if (label != null) label.RefreshOutline();
        }

        public static TextOutline Add(GameObject text, float width = 1.6f, Color? outlineColor = null, float bold = 0f)
        {
            var outline = text.GetComponent<TextOutline>();
            if (outline == null) outline = text.AddComponent<TextOutline>();
            outline.width = width;
            outline.color = outlineColor ?? Color.black;
            outline.bold = bold;
            outline.Refresh();
            return outline;
        }
    }
}
