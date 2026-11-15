using UnityEngine;
using UnityEngine.UI;

namespace Alterlife.Menu
{
    /// <summary>Native uGUI rounded fills and stencil masks; no generated bitmap dependency.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RoundedGraphic : MaskableGraphic
    {
        [SerializeField] float radius = 24;
        public float Radius { get => radius; set { radius = value; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            float r = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * .5f);
            vh.AddVert(rect.center, color, Vector2.one * .5f);
            const int segments = 16;
            for (int corner = 0; corner < 4; corner++)
            {
                var center = new Vector2(corner == 0 || corner == 3 ? rect.xMax - r : rect.xMin + r,
                    corner < 2 ? rect.yMax - r : rect.yMin + r);
                for (int step = 0; step <= segments; step++)
                {
                    float angle = (corner * 90f + step * 90f / segments) * Mathf.Deg2Rad;
                    var position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                    vh.AddVert(position, color, Vector2.zero);
                }
            }
            int count = 4 * (segments + 1);
            for (int i = 1; i <= count; i++) vh.AddTriangle(0, i, i == count ? 1 : i + 1);
        }
    }
}
