using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Enforces Controllers-first priority over Hand Tracking so visible hands never interrupt
    /// controller gameplay or switch into camera/passthrough view, while seamlessly falling back
    /// to full Hand Tracking (Near-Far pinch, Direct Poke, and Hand Teleport) when no controllers are detected.
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

        [Header("Input Priority")]
        [SerializeField] private float pollIntervalSeconds = 0.2f;

        public bool ControllersActive { get; private set; } = true;

        private float nextPollTime;
        private readonly List<InputDevice> controllerDevices = new List<InputDevice>();
        private readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();

        void Awake()
        {
            ResolveReferences();
            ConfigureCastersAndInteractors();
            ConfigureHandVisuals();
            EnableHandTeleportation();
            EnforceOpaqueSkyboxCamera();

            // Start in controller mode until we confirm controllers are absent.
            ApplyModalityState(preferControllers: true);

#if !UNITY_EDITOR
            DisableSimulatorInPlayer();
            if (!GetComponent<QuestTrackingDiagnostics>()) gameObject.AddComponent<QuestTrackingDiagnostics>();
#endif
        }

        void Update()
        {
            EnforceOpaqueSkyboxCamera();

            if (Time.unscaledTime < nextPollTime)
                return;

            nextPollTime = Time.unscaledTime + pollIntervalSeconds;
            UpdateInputModalityPriority();
        }

        void ResolveReferences()
        {
            if (xrOrigin == null)
                xrOrigin = GetComponent<XROrigin>();

            if (modalityManager == null)
            {
                modalityManager = GetComponent<XRInputModalityManager>();
                if (modalityManager == null)
                    modalityManager = GetComponentInChildren<XRInputModalityManager>(true);
            }

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

            foreach (var nearFar in GetComponentsInChildren<NearFarInteractor>(true))
                nearFar.allowHoveredActivate = true;

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

        void EnableHandTeleportation()
        {
            EnableTeleportUnder(leftHand);
            EnableTeleportUnder(rightHand);
        }

        static void EnableTeleportUnder(GameObject handRoot)
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
                    component.enabled = true;
                    if (component.gameObject != handRoot)
                        component.gameObject.SetActive(true);
                }
            }
        }

        void UpdateInputModalityPriority()
        {
            bool anyControllerActive = AreMotionControllersTracked();
            if (anyControllerActive)
            {
                ApplyModalityState(preferControllers: true);
                return;
            }

            bool anyHandTracked = AreHandsTracked();
#if UNITY_EDITOR
            // In desktop Play Mode without an HMD, preserve controllers unless hand tracking is explicitly active.
            bool preferControllers = !anyHandTracked;
#else
            bool preferControllers = !anyHandTracked && !Application.isPlaying;
#endif
            ApplyModalityState(preferControllers);
        }

        bool AreMotionControllersTracked()
        {
            controllerDevices.Clear();
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.HeldInHand,
                controllerDevices);

            for (int i = 0; i < controllerDevices.Count; i++)
            {
                InputDevice device = controllerDevices[i];
                if (!device.isValid)
                    continue;

                if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked)
                    return true;

                if (device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis) && axis.sqrMagnitude > 0.01f)
                    return true;

                if (device.TryGetFeatureValue(CommonUsages.trigger, out float trigger) && trigger > 0.05f)
                    return true;

                if (device.TryGetFeatureValue(CommonUsages.grip, out float grip) && grip > 0.05f)
                    return true;
            }

            return false;
        }

        bool AreHandsTracked()
        {
            handSubsystems.Clear();
            SubsystemManager.GetSubsystems(handSubsystems);
            for (int i = 0; i < handSubsystems.Count; i++)
            {
                XRHandSubsystem subsystem = handSubsystems[i];
                if (subsystem != null && subsystem.running &&
                    (subsystem.leftHand.isTracked || subsystem.rightHand.isTracked))
                {
                    return true;
                }
            }

            return false;
        }

        void ApplyModalityState(bool preferControllers)
        {
            ControllersActive = preferControllers;

            // Prevent XRInputModalityManager from auto-switching to hands when controllers are active.
            if (modalityManager != null && modalityManager.enabled)
                modalityManager.enabled = false;

            if (leftController != null && leftController.activeSelf != preferControllers)
                leftController.SetActive(preferControllers);
            if (rightController != null && rightController.activeSelf != preferControllers)
                rightController.SetActive(preferControllers);

            if (leftHand != null && leftHand.activeSelf == preferControllers)
                leftHand.SetActive(!preferControllers);
            if (rightHand != null && rightHand.activeSelf == preferControllers)
                rightHand.SetActive(!preferControllers);
        }

        void EnforceOpaqueSkyboxCamera()
        {
            Camera cam = xrOrigin != null ? xrOrigin.Camera : Camera.main;
            if (cam == null)
                return;

            if (cam.clearFlags != CameraClearFlags.Skybox && cam.clearFlags != CameraClearFlags.Color)
                cam.clearFlags = CameraClearFlags.Skybox;

            if (cam.backgroundColor.a < 0.99f)
                cam.backgroundColor = new Color(0.05f, 0.07f, 0.1f, 1f);
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
