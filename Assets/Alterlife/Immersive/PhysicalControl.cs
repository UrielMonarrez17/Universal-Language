using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Alterlife.Immersive
{
    /// <summary>A real collider-backed control, reachable by near or distant controller/hand interaction.</summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class PhysicalControl : MonoBehaviour
    {
        public UnityEvent pressed = new UnityEvent();
        public Renderer feedbackRenderer;
        public Color normalColor = new Color(0.06f, 0.09f, 0.14f, 0.78f);
        public Color hoverColor = new Color(0.24f, 0.62f, 0.88f, 0.92f);
        public bool applyMinimalistTintOnAwake = true;

        XRSimpleInteractable interactable;
        PhysicalPokeFilter pokeFilter;
        WristMenuController wristMenu;
        MaterialPropertyBlock block;
        Color original;
        float lastPress = -10f;

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            wristMenu = GetComponentInParent<WristMenuController>();
            block = new MaterialPropertyBlock();

            if (feedbackRenderer != null)
            {
                original = applyMinimalistTintOnAwake
                    ? normalColor
                    : (feedbackRenderer.sharedMaterial != null ? feedbackRenderer.sharedMaterial.color : normalColor);
                Tint(original, false);
            }
            else
            {
                original = normalColor;
            }

            pokeFilter = new PhysicalPokeFilter(() => isActiveAndEnabled);
            interactable.selectFilters.Add(pokeFilter);
            interactable.interactionStrengthFilters.Add(pokeFilter);
            interactable.selectEntered.AddListener(Selected);
            interactable.activated.AddListener(Activated);
            interactable.firstHoverEntered.AddListener(Hover);
            interactable.lastHoverExited.AddListener(Unhover);
        }

        void OnDestroy()
        {
            if (!interactable) return;
            if (pokeFilter != null)
            {
                interactable.selectFilters.Remove(pokeFilter);
                interactable.interactionStrengthFilters.Remove(pokeFilter);
            }
            interactable.selectEntered.RemoveListener(Selected);
            interactable.activated.RemoveListener(Activated);
            interactable.firstHoverEntered.RemoveListener(Hover);
            interactable.lastHoverExited.RemoveListener(Unhover);
        }

        void Selected(SelectEnterEventArgs args)
        {
            if ((wristMenu && !wristMenu.AcceptsPress(args.interactorObject, false)) || IsControllerGripOnly(args.interactorObject))
                return;

            Press();
        }

        void Activated(ActivateEventArgs args)
        {
            if (!wristMenu || wristMenu.AcceptsPress(args.interactorObject, true)) Press();
        }
        void Hover(HoverEnterEventArgs _) => Tint(hoverColor, true);
        void Unhover(HoverExitEventArgs _) => Tint(original, false);

        void Tint(Color color, bool highlighted)
        {
            if (!feedbackRenderer) return;
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            block.SetColor("_EmissionColor", highlighted ? new Color(color.r * 0.35f, color.g * 0.45f, color.b * 0.55f, 1f) : Color.black);
            feedbackRenderer.SetPropertyBlock(block);
        }

        public void SetBaseColor(Color baseColor)
        {
            normalColor = baseColor;
            original = baseColor;
            Tint(original, false);
        }

        public void Press()
        {
            if (!isActiveAndEnabled || Time.unscaledTime - lastPress < .35f) return;
            lastPress = Time.unscaledTime;
            pressed.Invoke();
        }

        /// <summary>
        /// Returns true when a tracked motion controller presses Grip without pressing Trigger,
        /// so UI/button/country selection uses Trigger (or hand pinch/poke) instead of Grip.
        /// </summary>
        public static bool IsControllerGripOnly(IXRInteractor interactor)
        {
            if (interactor == null || interactor is XRPokeInteractor)
                return false;

            Transform t = interactor.transform;
            bool isLeft = false;
            bool isRight = false;
            while (t != null)
            {
                string name = t.name;
                if (name.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    name.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }

                if (name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0) isLeft = true;
                if (name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0) isRight = true;
                t = t.parent;
            }

            if (isLeft == isRight)
                return false;

            XRNode node = isLeft ? XRNode.LeftHand : XRNode.RightHand;
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
                return false;

            device.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerBtn);
            device.TryGetFeatureValue(CommonUsages.trigger, out float triggerVal);
            device.TryGetFeatureValue(CommonUsages.gripButton, out bool gripBtn);
            device.TryGetFeatureValue(CommonUsages.grip, out float gripVal);

            bool triggerPressed = triggerBtn || triggerVal > 0.25f;
            bool gripPressed = gripBtn || gripVal > 0.35f;
            return gripPressed && !triggerPressed;
        }
    }
}
