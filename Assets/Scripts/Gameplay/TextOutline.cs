using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PudgeWars.Gameplay
{
    /// <summary>
    /// A solid outline for uGUI text: copies of the glyphs in <see cref="color"/> all around them, in two rings (half
    /// the width and the full width, <see cref="directions"/> copies each), under the text. uGUI's own Outline makes only
    /// four diagonal copies, which leave gaps between a thin stroke and its outline.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class TextOutline : BaseMeshEffect
    {
        public Color color = Color.black;
        [Tooltip("How far the outline reaches from the glyphs, canvas units.")]
        public float width = 1.6f;
        [Tooltip("Copies around the glyphs in each ring.")]
        [Range(4, 16)] public int directions = 12;

        static readonly List<UIVertex> source = new();
        static readonly List<UIVertex> result = new();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || width <= 0f) return;
            source.Clear();
            vh.GetUIVertexStream(source);
            result.Clear();
            Color32 outline = color;
            foreach (float ring in new[] { width * 0.5f, width })
                for (int d = 0; d < directions; d++)
                {
                    float a = d * Mathf.PI * 2f / directions;
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
            result.AddRange(source); // the text itself on top
            vh.Clear();
            vh.AddUIVertexTriangleStream(result);
        }

        /// <summary>Adds a solid black (or <paramref name="outlineColor"/>) outline to a text.</summary>
        public static TextOutline Add(GameObject text, float width = 1.6f, Color? outlineColor = null)
        {
            var outline = text.AddComponent<TextOutline>();
            outline.width = width;
            outline.color = outlineColor ?? Color.black;
            return outline;
        }
    }
}
