using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Ensures the XR rig supports multimodal Quest Touch Controllers and Hand Tracking (Near-Far + Direct Poke)
    /// while keeping teleportation exclusive to controllers and room-scale movement for hands.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QuestMultimodalRig : MonoBehaviour
    {
        public XROrigin xrOrigin;
        public XRInputModalityManager modalityManager;
        public XRInteractionManager interactionManager;
        public GameObject leftHand;
        public GameObject rightHand;
        public GameObject leftController;
        public GameObject rightController;

        void Awake()
        {
            ResolveReferences();
            ConfigureCastersAndInteractors();
            ConfigureHandVisuals();
            DisableHandTeleportation();
#if !UNITY_EDITOR
            DisableSimulatorInPlayer();
#endif
        }

        void ResolveReferences()
        {
            if (xrOrigin == null)
                xrOrigin = GetComponent<XROrigin>();

            if (modalityManager == null)
                modalityManager = GetComponent<XRInputModalityManager>() ?? GetComponentInChildren<XRInputModalityManager>(true);

            if (interactionManager == null)
                interactionManager = FindAnyObjectByType<XRInteractionManager>();

            if (modalityManager != null)
            {
                if (leftHand == null) leftHand = modalityManager.leftHand;
                if (rightHand == null) rightHand = modalityManager.rightHand;
                if (leftController == null) leftController = modalityManager.leftController;
                if (rightController == null) rightController = modalityManager.rightController;
            }
        }

        void ConfigureCastersAndInteractors()
        {
            foreach (var caster in GetComponentsInChildren<CurveInteractionCaster>(true))
                caster.hitDetectionType = CurveInteractionCaster.HitDetectionType.Raycast;

            foreach (var poke in GetComponentsInChildren<XRPokeInteractor>(true))
                poke.requirePokeFilter = true;

            if (interactionManager == null)
                return;

            foreach (var interactor in GetComponentsInChildren<XRBaseInteractor>(true))
            {
                if (interactor.interactionManager == null)
                    interactor.interactionManager = interactionManager;
            }

            foreach (var group in GetComponentsInChildren<XRInteractionGroup>(true))
            {
                if (group.interactionManager == null)
                    group.interactionManager = interactionManager;
            }
        }

        void ConfigureHandVisuals()
        {
            ConfigureVisualHierarchy(leftHand);
            ConfigureVisualHierarchy(rightHand);
        }

        static void ConfigureVisualHierarchy(GameObject handRoot)
        {
            if (handRoot == null)
                return;

            foreach (var child in handRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child.gameObject == handRoot)
                    continue;

                if (child.name.Contains("Quest Visual", StringComparison.OrdinalIgnoreCase))
                    child.gameObject.SetActive(true);
                else if (child.name.Contains("Android XR Visual", StringComparison.OrdinalIgnoreCase))
                    child.gameObject.SetActive(false);
            }
        }

        void DisableHandTeleportation()
        {
            DisableTeleportUnder(leftHand);
            DisableTeleportUnder(rightHand);
        }

        static void DisableTeleportUnder(GameObject handRoot)
        {
            if (handRoot == null)
                return;

            foreach (var component in handRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                    continue;

                var typeName = component.GetType().Name;
                if (typeName.Contains("TeleportInteractor", StringComparison.OrdinalIgnoreCase)
                    || typeName.Contains("TeleportHandGesture", StringComparison.OrdinalIgnoreCase))
                {
                    component.enabled = false;
                    if (component.gameObject != handRoot)
                        component.gameObject.SetActive(false);
                }
            }
        }

        static void DisableSimulatorInPlayer()
        {
            foreach (var simulator in FindObjectsByType<XRDeviceSimulator>(FindObjectsInactive.Include))
            {
                if (simulator != null && simulator.gameObject.activeSelf)
                    simulator.gameObject.SetActive(false);
            }
        }
    }
}
