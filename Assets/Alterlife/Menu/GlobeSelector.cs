using UnityEngine;
using UnityEngine.EventSystems;

namespace Alterlife.Menu
{
    /// <summary>Cycles the three globe orientations authored in Figma, using mouse or XR UI drag.</summary>
    public sealed class GlobeSelector : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] UnityEngine.UI.RawImage globeImage;
        [SerializeField] GameObject franceMarker;
        [SerializeField] GameObject franceTooltip;
        [SerializeField] RectTransform dragSurface;
        [SerializeField] Canvas menuCanvas;
        [SerializeField] int orientation = 1;
        Vector2 lastPosition;
        float accumulatedDrag;
        bool dragging;

        public int Orientation => orientation;
        public bool FranceVisible => orientation == 1;

        void OnEnable() => Refresh();
        void OnDisable() => ResetInteraction();

        public void RotateLeft() => Rotate(-1);
        public void RotateRight() => Rotate(1);

        public void Rotate(int direction)
        {
            orientation = ((orientation + direction) % 3 + 3) % 3;
            Refresh();
        }

        void Refresh()
        {
            // Figma's image fill is a three-view atlas, not an equirectangular map.
            float[] left = { -0.0003f, -0.9498f, -1.9007f };
            float top = orientation == 0 ? -0.295f : -0.2818f;
            globeImage.uvRect = new Rect(-left[orientation] / 2.9007f,
                1f - (1f - top) / 1.5615f, 1f / 2.9007f, 1f / 1.5615f);
            franceMarker.SetActive(FranceVisible);
            franceTooltip.SetActive(false);
        }

        public void ResetInteraction()
        {
            dragging = false;
            accumulatedDrag = 0;
            if (franceTooltip != null) franceTooltip.SetActive(false);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = LocalPoint(eventData, out lastPosition);
            accumulatedDrag = 0;
            franceTooltip.SetActive(false);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || !LocalPoint(eventData, out var position)) return;
            accumulatedDrag += position.x - lastPosition.x;
            lastPosition = position;
            const float threshold = 90f;
            if (Mathf.Abs(accumulatedDrag) < threshold) return;
            Rotate(accumulatedDrag < 0 ? 1 : -1);
            accumulatedDrag = 0;
        }

        public void OnEndDrag(PointerEventData eventData) => ResetInteraction();

        bool LocalPoint(PointerEventData data, out Vector2 point) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(dragSurface, data.position,
                data.pressEventCamera != null ? data.pressEventCamera : menuCanvas.worldCamera, out point);
    }
}
