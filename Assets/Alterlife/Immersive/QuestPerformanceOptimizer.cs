using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Extensions.PerformanceSettings;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Applies Meta Quest runtime performance targets (72 Hz display refresh, Fixed Foveated Rendering,
    /// OpenXR sustained performance hints, posture restoration, and standalone simulator disabling).
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class QuestPerformanceOptimizer : MonoBehaviour
    {
        public float targetRefreshRate = 72f;
        [Range(0f, 1f)]
        public float foveatedRenderingLevel = 0.85f;
        [Tooltip("Maximum camera far clip distance on Meta Quest to prevent rendering unnecessary distant geometry.")]
        public float maxCameraFarClip = 360f;

        static bool hookRegistered;
        static readonly List<XRDisplaySubsystem> Displays = new List<XRDisplaySubsystem>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!hookRegistered)
            {
                hookRegistered = true;
                SceneManager.sceneLoaded += OnSceneLoaded;
            }

            ApplyToActiveScene();
        }

        static void OnSceneLoaded(Scene _, LoadSceneMode __) => ApplyToActiveScene();

        void Awake()
        {
            ApplyRuntimeSettings();
        }

        static void ApplyToActiveScene()
        {
            var existing = FindAnyObjectByType<QuestPerformanceOptimizer>(FindObjectsInactive.Include);
            if (existing != null)
            {
                existing.ApplyRuntimeSettings();
                return;
            }

            var host = FindAnyObjectByType<HubRendering>(FindObjectsInactive.Include)?.gameObject
                ?? FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include)?.gameObject;
            if (host != null)
            {
                host.AddComponent<QuestPerformanceOptimizer>();
            }
            else
            {
                ApplyStaticDefaults(72f, 0.85f, 360f);
            }
        }

        public void ApplyRuntimeSettings()
        {
            ApplyStaticDefaults(targetRefreshRate, foveatedRenderingLevel, maxCameraFarClip);
        }

        static void ApplyStaticDefaults(float refreshRate, float ffrLevel, float maxFarClip)
        {
            Application.targetFrameRate = Mathf.RoundToInt(refreshRate);
            QualitySettings.vSyncCount = 0;

            ConfigureDisplaySubsystems(refreshRate, ffrLevel);
            ConfigurePerformanceHints();

#if !UNITY_EDITOR
            DisableSimulatorInPlayer();
#endif

            var origin = FindAnyObjectByType<XROrigin>();
            if (origin != null)
            {
                ComfortOptions.ApplySavedPosture(origin);
                if (origin.Camera != null && maxFarClip > 20f && origin.Camera.farClipPlane > maxFarClip)
                {
                    origin.Camera.farClipPlane = maxFarClip;
                }
            }
        }

        static void ConfigureDisplaySubsystems(float refreshRate, float ffrLevel)
        {
            Displays.Clear();
            SubsystemManager.GetSubsystems(Displays);
            bool hasRefreshExtension = OpenXRRuntime.IsExtensionEnabled("XR_FB_display_refresh_rate");

            foreach (var display in Displays)
            {
                if (display == null || !display.running)
                    continue;

                try
                {
                    display.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
                    display.foveatedRenderingLevel = ffrLevel;
                }
                catch (Exception)
                {
                    // Desktop or mock XR displays may not expose foveation.
                }

                if (!hasRefreshExtension)
                    continue;

                try
                {
                    if (display.TryGetSupportedDisplayRefreshRates(Allocator.Temp, out var rates))
                    {
                        using (rates)
                        {
                            for (int i = 0; i < rates.Length; i++)
                            {
                                if (Mathf.Approximately(rates[i], refreshRate))
                                {
                                    display.TryRequestDisplayRefreshRate(refreshRate);
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Ignore when running outside Meta Quest runtime.
                }
            }
        }

        static void ConfigurePerformanceHints()
        {
            if (!OpenXRRuntime.IsExtensionEnabled(XrPerformanceSettingsFeature.extensionString))
                return;

            XrPerformanceSettingsFeature.SetPerformanceLevelHint(PerformanceDomain.Gpu, PerformanceLevelHint.SustainedHigh);
            XrPerformanceSettingsFeature.SetPerformanceLevelHint(PerformanceDomain.Cpu, PerformanceLevelHint.SustainedHigh);
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
