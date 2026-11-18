using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Extensions.PerformanceSettings;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.Meta;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace Alterlife.Immersive.Editor
{
    /// <summary>Configures Android PlayerSettings, XR Management, OpenXR Quest features, URP asset, and Build Settings.</summary>
    public static class QuestProjectSetup
    {
        const string PipelineAssetPath = "Assets/Alterlife/Immersive/Generated/Hub Quest Pipeline.asset";
        const string GeneralSettingsAssetPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        const string OpenXRLoaderAssetPath = "Assets/XR/Loaders/OpenXRLoader.asset";
        const string ImmersiveHubScenePath = "Assets/Scenes/ImmersiveHub.unity";
        const string BookSelectionScenePath = "Assets/Scenes/BookSelection.unity";
        const string ParisScenePath = "Assets/Scenes/Paris.unity";

        static readonly string[] QuestDeviceManifestNames =
        {
            "quest",
            "quest2",
            "cambria",
            "eureka",
            "quest3s",
        };

        [MenuItem("Alterlife/Quest/Apply Project & OpenXR Settings")]
        public static void ApplyAll()
        {
            ConfigureAndroidPlayerSettings();
            ConfigureXRGeneralSettings();
            ConfigureOpenXRSettings();
            ConfigurePipelineAsset();
            ConfigureBuildScenes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("ALTERLIFE_QUEST_PROJECT_SETUP_APPLIED");
        }

        static void ConfigureAndroidPlayerSettings()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                if (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                }
                else
                {
                    Debug.LogWarning("Android build support is not installed in this Unity Editor; applying Android PlayerSettings without switching active build target.");
                }
            }

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.Alterlife.UniversalLanguage");
            PlayerSettings.companyName = "Alterlife";
            PlayerSettings.productName = "Universal Language";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        }

        static void ConfigureXRGeneralSettings()
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget generalSettings);
            if (generalSettings == null)
            {
                generalSettings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(GeneralSettingsAssetPath);
                if (generalSettings != null)
                {
                    EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, generalSettings, true);
                }
            }

            if (generalSettings == null)
            {
                Debug.LogWarning("Could not find XRGeneralSettingsPerBuildTarget at " + GeneralSettingsAssetPath);
                return;
            }

            if (!generalSettings.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                generalSettings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            }

            if (!generalSettings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                generalSettings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            }

            var androidSettings = generalSettings.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (androidSettings == null)
            {
                Debug.LogWarning("Android XRGeneralSettings could not be created or retrieved.");
                return;
            }

            androidSettings.InitManagerOnStart = true;

            if (androidSettings.Manager != null && (androidSettings.Manager.activeLoaders == null || androidSettings.Manager.activeLoaders.Count == 0))
            {
                var openxrLoader = AssetDatabase.LoadAssetAtPath<XRLoader>(OpenXRLoaderAssetPath);
                if (openxrLoader != null)
                {
                    androidSettings.Manager.TryAddLoader(openxrLoader);
                    EditorUtility.SetDirty(androidSettings.Manager);
                }
            }

            EditorUtility.SetDirty(androidSettings);
            EditorUtility.SetDirty(generalSettings);
        }

        static void ConfigureOpenXRSettings()
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null)
            {
                Debug.LogWarning("OpenXRSettings for BuildTargetGroup.Android could not be found.");
                return;
            }

            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            settings.foveatedRenderingApi = OpenXRSettings.BackendFovationApi.SRPFoveation;
            settings.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;

            var metaQuestFeature = EnableFeature<MetaQuestFeature>(settings);
            if (metaQuestFeature != null)
            {
                var serialized = new SerializedObject(metaQuestFeature);
                var targetDevicesProp = serialized.FindProperty("targetDevices");
                if (targetDevicesProp != null && targetDevicesProp.isArray)
                {
                    for (int i = 0; i < targetDevicesProp.arraySize; i++)
                    {
                        var element = targetDevicesProp.GetArrayElementAtIndex(i);
                        var manifestProp = element.FindPropertyRelative("manifestName");
                        var enabledProp = element.FindPropertyRelative("enabled");
                        if (enabledProp == null)
                            continue;

                        if (manifestProp == null || string.IsNullOrEmpty(manifestProp.stringValue) ||
                            QuestDeviceManifestNames.Contains(manifestProp.stringValue, StringComparer.OrdinalIgnoreCase))
                        {
                            enabledProp.boolValue = true;
                        }
                    }
                }

                var optimizeDiscardProp = serialized.FindProperty("m_optimizeBufferDiscards")
                    ?? serialized.FindProperty("optimizeBufferDiscards");
                if (optimizeDiscardProp != null)
                {
                    optimizeDiscardProp.boolValue = true;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(metaQuestFeature);
            }

            EnableFeature<OculusTouchControllerProfile>(settings);
            EnableFeature<MetaQuestTouchPlusControllerProfile>(settings);
            EnableFeature<MetaQuestTouchProControllerProfile>(settings);
            EnableFeature<HandTracking>(settings);
            EnableFeature<HandInteractionProfile>(settings);
            EnableFeature<FoveatedRenderingFeature>(settings);
            EnableFeature<DisplayUtilitiesFeature>(settings);
            EnableFeature<XrPerformanceSettingsFeature>(settings);

            EditorUtility.SetDirty(settings);
        }

        static T EnableFeature<T>(OpenXRSettings settings) where T : OpenXRFeature
        {
            var feature = settings.GetFeature<T>();
            if (feature == null)
            {
                Debug.LogWarning($"OpenXR feature {typeof(T).FullName} was not found in Android OpenXRSettings.");
                return null;
            }

            feature.enabled = true;
            EditorUtility.SetDirty(feature);
            return feature;
        }

        static void ConfigurePipelineAsset()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (pipeline == null)
            {
                Debug.LogWarning("Could not find Hub Quest Pipeline asset at " + PipelineAssetPath);
                return;
            }

            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1f;
            pipeline.shadowDistance = 12f;
            pipeline.mainLightShadowmapResolution = 1024;
            pipeline.shadowCascadeCount = 1;
            pipeline.supportsHDR = false;

            var serialized = new SerializedObject(pipeline);
            var additionalLightsProp = serialized.FindProperty("m_AdditionalLightsRenderingMode");
            if (additionalLightsProp != null)
            {
                additionalLightsProp.intValue = (int)LightRenderingMode.PerVertex;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
        }

        static void ConfigureBuildScenes()
        {
            var requiredScenes = new[]
            {
                ImmersiveHubScenePath,
                BookSelectionScenePath,
                ParisScenePath,
            };

            var existingByPath = EditorBuildSettings.scenes
                .Where(s => s != null && !string.IsNullOrEmpty(s.path))
                .GroupBy(s => s.path)
                .ToDictionary(g => g.Key, g => g.First());

            var updated = new List<EditorBuildSettingsScene>();
            foreach (var path in requiredScenes)
            {
                if (System.IO.File.Exists(path) || existingByPath.ContainsKey(path))
                {
                    updated.Add(new EditorBuildSettingsScene(path, true));
                }
                else
                {
                    Debug.LogWarning("Required build scene not found on disk: " + path);
                }
            }

            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene == null || string.IsNullOrEmpty(scene.path))
                    continue;

                if (requiredScenes.Contains(scene.path))
                    continue;

                updated.Add(scene);
            }

            EditorBuildSettings.scenes = updated.ToArray();
        }
    }
}
