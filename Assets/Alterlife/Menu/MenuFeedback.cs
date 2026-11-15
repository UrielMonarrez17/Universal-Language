using UnityEngine;
using UnityEngine.EventSystems;

namespace Alterlife.Menu
{
    /// <summary>Visual focus shared by Quest rays, desktop pointers and keyboard navigation.</summary>
    public sealed class MenuFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler
    {
        [SerializeField] RectTransform visual;
        [SerializeField] GameObject tooltip;
        [SerializeField] float highlightedScale = 1.14f;
        bool hovered;
        bool focused;
        bool selected;

        public void SetSelected(bool value) { selected = value; Refresh(); }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; Refresh(); }
        public void OnSelect(BaseEventData data) { focused = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { focused = false; Refresh(); }

        void OnDisable()
        {
            hovered = focused = selected = false;
            Refresh();
        }

        void Refresh()
        {
            bool active = hovered || focused || selected;
            if (visual != null) visual.localScale = Vector3.one * (active ? highlightedScale : 1f);
            if (tooltip != null) tooltip.SetActive(active && isActiveAndEnabled);
        }
    }
}
