using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// A solid outline for uGUI text: copies of the glyphs in <see cref="color"/> all around them, in two rings (half
    /// the width and the full width, <see cref="directions"/> copies each), under the text. uGUI's own Outline makes only
    /// four diagonal copies, which leave gaps between a thin stroke and its outline. With <see cref="bold"/> the glyphs
    /// are thickened too (copies in their own colour, shifted a little), whatever the font has.
    /// A long text gets fewer copies (or none) so the mesh stays within uGUI's vertex limit.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class TextOutline : BaseMeshEffect
    {
        public Color color = Color.black;
        [Tooltip("How far the outline reaches from the glyphs, canvas units.")]
        public float width = 1.6f;
        [Tooltip("Copies around the glyphs in each ring.")]
        [Range(4, 16)] public int directions = 12;
        [Tooltip("Thickens the glyphs by this much, canvas units (0 — as the font draws them).")]
        public float bold;

        const int VertexLimit = 60000; // uGUI's mesh holds 65000
        static readonly List<UIVertex> source = new();
        static readonly List<UIVertex> result = new();
        static readonly Vector2[] BoldShifts = { new(1f, 0f), new(-1f, 0f), new(0.5f, 0f), new(-0.5f, 0f), new(0f, 0.4f), new(0f, -0.4f) };

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || (width <= 0f && bold <= 0f)) return;
            source.Clear();
            vh.GetUIVertexStream(source);
            if (source.Count == 0) return;
            int boldCopies = bold > 0f ? BoldShifts.Length : 0;
            int rings = width > 0f ? 2 : 0, dirs = directions;
            while (rings > 0 && (rings * dirs + boldCopies + 1) * source.Count > VertexLimit)
            {
                if (dirs > 6) dirs /= 2;
                else rings--;
            }
            if ((rings * dirs + boldCopies + 1) * source.Count > VertexLimit) return; // too long to outline

            result.Clear();
            Color32 outline = color;
            for (int r = 1; r <= rings; r++)
            {
                float ring = width * r / rings;
                for (int d = 0; d < dirs; d++)
                {
                    float a = d * Mathf.PI * 2f / dirs;
                    var offset = new Vector3(Mathf.Cos(a) * ring, Mathf.Sin(a) * ring, 0f);
                    foreach (var vertex in source)
                    {
                        var copy = vertex;
                        copy.position += offset;
                        var c = outline;
                        c.a = (byte)(outline.a * vertex.color.a / 255);
                        copy.color = c;
                        result.Add(copy);
                    }
                }
            }
            // the thickening in the glyphs' own colour (it stays within the outline while bold < width)
            if (boldCopies > 0)
                foreach (var shift in BoldShifts)
                    AddShifted(shift * bold);
            result.AddRange(source); // the text itself on top
            vh.Clear();
            vh.AddUIVertexTriangleStream(result);
        }

        static void AddShifted(Vector2 shift)
        {
            foreach (var vertex in source)
            {
                var copy = vertex;
                copy.position += (Vector3)shift;
                result.Add(copy);
            }
        }

        /// <summary>Adds a solid black (or <paramref name="outlineColor"/>) outline to a text, thickening it by <paramref name="bold"/>.</summary>
        public static TextOutline Add(GameObject text, float width = 1.6f, Color? outlineColor = null, float bold = 0f)
        {
            var outline = text.AddComponent<TextOutline>();
            outline.width = width;
            outline.color = outlineColor ?? Color.black;
            outline.bold = bold;
            return outline;
        }
    }
}
