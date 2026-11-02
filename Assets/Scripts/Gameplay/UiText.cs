using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// Actual TextMesh Pro UI with the project's existing Font/Text authoring API. Retains serialized font data
    /// and object references from saved scenes; layout, masking, input and glyph rendering are handled by TMP.
    /// </summary>
    [AddComponentMenu("Pudge Wars/UI Text (SDF)")]
    public sealed class UiText : TextMeshProUGUI
    {
        [SerializeField] FontData m_FontData = FontData.defaultFontData;
        [SerializeField, TextArea] string m_Text = "";

        public override string text { get => base.text; set { m_Text = value; base.text = value; } }
        public new Font font
        {
            get => m_FontData.font;
            set { m_FontData.font = value; base.font = UiFonts.For(value); RefreshOutline(); }
        }
        public new int fontSize
        {
            get => m_FontData.fontSize;
            set { m_FontData.fontSize = value; base.fontSize = value; RefreshOutline(); }
        }
        public new FontStyle fontStyle
        {
            get => m_FontData.fontStyle;
            set
            {
                m_FontData.fontStyle = value;
                base.fontStyle = value switch
                {
                    FontStyle.Bold => FontStyles.Bold,
                    FontStyle.Italic => FontStyles.Italic,
                    FontStyle.BoldAndItalic => FontStyles.Bold | FontStyles.Italic,
                    _ => FontStyles.Normal,
                };
            }
        }
        public new TextAnchor alignment
        {
            get => m_FontData.alignment;
            set
            {
                m_FontData.alignment = value;
                base.alignment = value switch
                {
                    TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
                    TextAnchor.UpperCenter => TextAlignmentOptions.Top,
                    TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
                    TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
                    TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
                    TextAnchor.MiddleRight => TextAlignmentOptions.Right,
                    TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
                    TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
                    _ => TextAlignmentOptions.BottomRight,
                };
            }
        }
        public bool supportRichText { get => richText; set { m_FontData.richText = value; richText = value; } }
        public bool resizeTextForBestFit { get => enableAutoSizing; set { m_FontData.bestFit = value; enableAutoSizing = value; } }
        public int resizeTextMinSize { get => m_FontData.minSize; set { m_FontData.minSize = value; fontSizeMin = value; } }
        public int resizeTextMaxSize { get => m_FontData.maxSize; set { m_FontData.maxSize = value; fontSizeMax = value; } }
        public HorizontalWrapMode horizontalOverflow
        {
            get => m_FontData.horizontalOverflow;
            set { m_FontData.horizontalOverflow = value; textWrappingMode = value == HorizontalWrapMode.Wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap; }
        }
        public VerticalWrapMode verticalOverflow
        {
            get => m_FontData.verticalOverflow;
            set { m_FontData.verticalOverflow = value; overflowMode = value == VerticalWrapMode.Overflow ? TextOverflowModes.Overflow : TextOverflowModes.Truncate; }
        }
        public new float lineSpacing
        {
            get => m_FontData.lineSpacing;
            set { m_FontData.lineSpacing = value; base.lineSpacing = (value - 1f) * fontSize; }
        }

        protected override void Awake()
        {
            base.Awake();
            ApplyAuthoringSettings();
        }

        /// <summary>Apply the font/layout data shared by saved legacy scenes and this component's inspector.</summary>
        public void ApplyAuthoringSettings()
        {
            font = m_FontData.font;
            fontSize = m_FontData.fontSize;
            fontStyle = m_FontData.fontStyle;
            alignment = m_FontData.alignment;
            supportRichText = m_FontData.richText;
            resizeTextMinSize = m_FontData.minSize;
            resizeTextMaxSize = m_FontData.maxSize;
            resizeTextForBestFit = m_FontData.bestFit;
            horizontalOverflow = m_FontData.horizontalOverflow;
            verticalOverflow = m_FontData.verticalOverflow;
            lineSpacing = m_FontData.lineSpacing;
            text = m_Text;
            extraPadding = true;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (base.font == null) font = m_FontData.font;
        }

        internal void RefreshOutline()
        {
            if (base.font == null) return;
            var outline = GetComponent<TextOutline>();
            var material = outline != null && outline.enabled
                ? UiFonts.Style(base.font, fontSize, outline.color, outline.width, outline.bold)
                : base.font.material;
            if (fontSharedMaterial != material) { fontSharedMaterial = material; UpdateMeshPadding(); }
        }
    }
}
