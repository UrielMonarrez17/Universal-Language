using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Alterlife.Immersive
{
    /// <summary>A real collider-backed control, reachable by near or distant controller interaction.</summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class PhysicalControl : MonoBehaviour
    {
        public UnityEvent pressed = new UnityEvent();
        public Renderer feedbackRenderer;
        public Color hoverColor = new Color(1, .72f, .26f);
        XRSimpleInteractable interactable;
        MaterialPropertyBlock block;
        Color original;
        float lastPress = -10;
        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            block = new MaterialPropertyBlock();
            if (feedbackRenderer) original = feedbackRenderer.sharedMaterial.color;
            interactable.selectEntered.AddListener(Selected);
            interactable.activated.AddListener(Activated);
            interactable.firstHoverEntered.AddListener(Hover);
            interactable.lastHoverExited.AddListener(Unhover);
        }
        void OnDestroy()
        {
            if (!interactable) return;
            interactable.selectEntered.RemoveListener(Selected);
            interactable.activated.RemoveListener(Activated);
            interactable.firstHoverEntered.RemoveListener(Hover);
            interactable.lastHoverExited.RemoveListener(Unhover);
        }
        void Selected(SelectEnterEventArgs _) => Press();
        void Activated(ActivateEventArgs _) => Press();
        void Hover(HoverEnterEventArgs _) => Tint(hoverColor);
        void Unhover(HoverExitEventArgs _) => Tint(original);
        void Tint(Color color)
        {
            if (!feedbackRenderer) return;
            block.SetColor("_BaseColor", color);
            feedbackRenderer.SetPropertyBlock(block);
        }
        public void Press()
        {
            if (!isActiveAndEnabled || Time.unscaledTime - lastPress < .35f) return;
            lastPress = Time.unscaledTime;
            pressed.Invoke();
        }
    }
}
