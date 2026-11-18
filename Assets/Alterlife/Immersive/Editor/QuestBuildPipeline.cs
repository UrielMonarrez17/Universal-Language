using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands.OpenXR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Extensions.PerformanceSettings;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.Meta;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace Alterlife.Immersive.Editor
{
    /// <summary>
    /// Handles Android manifest permissions/features for Meta Quest, pre-flight readiness validation,
    /// and automated Development APK build & ADB deployment.
    /// </summary>
    public sealed class QuestBuildPipeline : IPostGenerateGradleAndroidProject
    {
        const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
        const string BuildOutputDir = "Builds/Quest";
        const string ApkOutputPath = "Builds/Quest/UniversalLanguage-Dev.apk";
        const string DefaultPlaceholderBundleId = "com.DefaultCompany.VRTemplate";

        static readonly string[] RequiredScenes =
        {
            "Assets/Scenes/ImmersiveHub.unity",
            "Assets/Scenes/BookSelection.unity",
            "Assets/Scenes/Paris.unity",
        };

        public int callbackOrder => 999;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning("QuestBuildPipeline: AndroidManifest.xml not found at " + manifestPath);
                return;
            }

            var doc = new XmlDocument();
            doc.Load(manifestPath);

            var manifestNode = doc.SelectSingleNode("/manifest") as XmlElement;
            if (manifestNode == null)
            {
                Debug.LogWarning("QuestBuildPipeline: Missing <manifest> root in " + manifestPath);
                return;
            }

            EnsurePermission(doc, manifestNode, "android.permission.RECORD_AUDIO");
            EnsurePermission(doc, manifestNode, "android.permission.INTERNET");
            EnsurePermission(doc, manifestNode, "com.oculus.permission.HAND_TRACKING");

            EnsureFeature(doc, manifestNode, "android.hardware.vr.headtracking", required: true, version: "1");
            EnsureFeature(doc, manifestNode, "oculus.software.handtracking", required: false, version: null);

            // MetaQuestFeature writes com.oculus.supportedDevices into :unityLibrary:xrmanifest.androidlib;
            // remove any stale copy in :unityLibrary to prevent Gradle manifest merger collisions.
            RemoveMetaData(doc, "com.oculus.supportedDevices");

            doc.Save(manifestPath);
        }

        [MenuItem("Alterlife/Quest/Validate Quest Readiness")]
        public static void ValidateQuestMenu()
        {
            bool passed = ValidateQuestReadiness(throwOnError: false);
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    "Quest Readiness Validation",
                    passed
                        ? "All Meta Quest readiness checks passed. See Console for the full report."
                        : "Some Meta Quest readiness checks failed. See Console for details.",
                    "OK");
            }
        }

        [MenuItem("Alterlife/Quest/Build Development APK")]
        public static void BuildDevelopmentApk() => BuildQuestApk(autoRun: false);

        [MenuItem("Alterlife/Quest/Build Quest 2 Beta APK")]
        public static void BuildBetaApk() => BuildQuestApk(autoRun: false, development: false);

        [MenuItem("Alterlife/Quest/Build & Run (via ADB)")]
        public static void BuildAndRun() => BuildQuestApk(autoRun: true);

        public static bool ValidateQuestReadiness(bool throwOnError = false)
        {
            var issues = new List<string>();
            var report = new List<string>();

            // 1. Platform & Player Settings
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
                report.Add("[PASS] Active BuildTarget is Android.");
            else
                issues.Add($"Active BuildTarget is {EditorUserBuildSettings.activeBuildTarget} (expected Android). Run Alterlife > Quest > Apply Project & OpenXR Settings.");

            var arch = PlayerSettings.Android.targetArchitectures;
            if ((arch & AndroidArchitecture.ARM64) != 0)
                report.Add("[PASS] Android target architecture includes ARM64.");
            else
                issues.Add($"Android target architecture is {arch} (must include ARM64).");

            var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
            if (backend == ScriptingImplementation.IL2CPP)
                report.Add("[PASS] Android scripting backend is IL2CPP.");
            else
                issues.Add($"Android scripting backend is {backend} (expected IL2CPP).");

            var minSdk = PlayerSettings.Android.minSdkVersion;
            if (minSdk >= AndroidSdkVersions.AndroidApiLevel29 && minSdk <= AndroidSdkVersions.AndroidApiLevel32)
                report.Add($"[PASS] Android MinSdkVersion is {minSdk}.");
            else
                issues.Add($"Android MinSdkVersion is {minSdk} (must be between API 29 and API 32 for Quest 2/3 compatibility).");

            var bundleId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (!string.IsNullOrWhiteSpace(bundleId) && bundleId != DefaultPlaceholderBundleId)
                report.Add($"[PASS] Android Bundle ID is {bundleId}.");
            else
                issues.Add($"Android Bundle ID is '{bundleId}' (must be customized away from {DefaultPlaceholderBundleId}).");

            // 2. Build Settings Scenes
            var enabledScenes = EditorBuildSettings.scenes.Where(s => s != null && s.enabled).Select(s => s.path).ToList();
            if (enabledScenes.Count > 0 && enabledScenes[0] == RequiredScenes[0])
                report.Add("[PASS] Build index 0 is ImmersiveHub.unity.");
            else
                issues.Add("Build Settings index 0 must be Assets/Scenes/ImmersiveHub.unity.");

            foreach (var scenePath in RequiredScenes)
            {
                if (!enabledScenes.Contains(scenePath))
                    issues.Add($"Required scene is missing or disabled in Build Settings: {scenePath}");
            }

            // 3. OpenXR Android Settings
            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (openXr == null)
            {
                issues.Add("OpenXRSettings for BuildTargetGroup.Android not found.");
            }
            else
            {
                if (openXr.renderMode == OpenXRSettings.RenderMode.SinglePassInstanced)
                    report.Add("[PASS] OpenXR render mode is SinglePassInstanced.");
                else
                    issues.Add($"OpenXR render mode is {openXr.renderMode} (expected SinglePassInstanced).");

                if (openXr.foveatedRenderingApi == OpenXRSettings.BackendFovationApi.SRPFoveation)
                    report.Add("[PASS] OpenXR foveated rendering API is SRPFoveation.");
                else
                    issues.Add($"OpenXR foveated rendering API is {openXr.foveatedRenderingApi} (expected SRPFoveation).");

                CheckFeature<MetaQuestFeature>(openXr, "MetaQuestFeature", report, issues);
                CheckFeature<OculusTouchControllerProfile>(openXr, "OculusTouchControllerProfile", report, issues);
                CheckFeature<MetaQuestTouchPlusControllerProfile>(openXr, "MetaQuestTouchPlusControllerProfile", report, issues);
                CheckFeature<MetaQuestTouchProControllerProfile>(openXr, "MetaQuestTouchProControllerProfile", report, issues);
                CheckFeature<HandTracking>(openXr, "HandTracking", report, issues);
                CheckFeature<HandInteractionProfile>(openXr, "HandInteractionProfile", report, issues);
                CheckFeature<FoveatedRenderingFeature>(openXr, "FoveatedRenderingFeature", report, issues);
                CheckFeature<DisplayUtilitiesFeature>(openXr, "DisplayUtilitiesFeature", report, issues);
                CheckFeature<XrPerformanceSettingsFeature>(openXr, "XrPerformanceSettingsFeature", report, issues);
            }

            // 4. Scene Rig & Component Checks (preserve currently active scene)
            string previousScenePath = SceneManager.GetActiveScene().path;
            bool canSwitchScenes = Application.isBatchMode || !SceneManager.GetActiveScene().isDirty;
            if (!canSwitchScenes)
            {
                report.Add("[WARN] Active scene has unsaved changes; save scenes before running full scene hierarchy validation.");
            }
            else
            {
                foreach (var scenePath in RequiredScenes)
                    ValidateScene(scenePath, report, issues);

                if (!string.IsNullOrEmpty(previousScenePath) && File.Exists(previousScenePath) && SceneManager.GetActiveScene().path != previousScenePath)
                    EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
            }

            // 5. Android Module Check (advisory during validate, blocking during build)
            bool androidSupported = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);
            report.Add(androidSupported
                ? "[PASS] Android Build Support module is installed."
                : "[WARN] Android Build Support (SDK/NDK/OpenJDK) is not installed in this Unity Editor.");

            foreach (var line in report)
                Debug.Log("QuestValidate: " + line);

            foreach (var issue in issues)
                Debug.LogError("QuestValidate FAIL: " + issue);

            bool passed = issues.Count == 0;
            Debug.Log(passed ? "ALTERLIFE_QUEST_VALIDATION_PASSED" : $"ALTERLIFE_QUEST_VALIDATION_FAILED ({issues.Count} issues)");

            if (throwOnError && !passed)
                throw new InvalidOperationException("Quest readiness validation failed:\n- " + string.Join("\n- ", issues));

            return passed;
        }

        static void BuildQuestApk(bool autoRun, bool development = true)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                const string message = "Android Build Support (SDK/NDK/OpenJDK) is not installed for this Unity Editor. Install it via Unity Hub and retry Alterlife > Quest > Build Development APK.";
                Debug.LogError(message);
                throw new InvalidOperationException(message);
            }

            ValidateQuestReadiness(throwOnError: true);

            Directory.CreateDirectory(BuildOutputDir);
            string[] scenes = development ? EditorBuildSettings.scenes
                .Where(s => s != null && s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray() : RequiredScenes;

            var options = development ? BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.CompressWithLz4 : BuildOptions.CompressWithLz4HC;
            string output = development ? ApkOutputPath : "Builds/Quest/UniversalLanguage-Quest2-Beta.apk";
            EditorUserBuildSettings.buildAppBundle = false;
            if (autoRun)
                options |= BuildOptions.AutoRunPlayer;

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = options,
            };

            BuildReport buildReport = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = buildReport.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                double sizeMb = summary.totalSize / (1024.0 * 1024.0);
                Debug.Log($"ALTERLIFE_QUEST_BUILD_SUCCEEDED: {output} ({sizeMb:F2} MB, {summary.totalTime.TotalSeconds:F1}s, warnings={summary.totalWarnings})");
                File.WriteAllText(Path.Combine(BuildOutputDir, "build-summary.txt"), $"{summary.result}\n{output}\n{PlayerSettings.bundleVersion}\nARM64 IL2CPP\n{summary.totalSize} bytes\n{summary.totalTime.TotalSeconds:F1} seconds\n{summary.totalErrors} errors, {summary.totalWarnings} warnings\n" + string.Join("\n", scenes));
            }
            else
            {
                string error = $"ALTERLIFE_QUEST_BUILD_FAILED: result={summary.result}, errors={summary.totalErrors}, warnings={summary.totalWarnings}";
                Debug.LogError(error);
                throw new InvalidOperationException(error);
            }
        }

        static void ValidateScene(string scenePath, List<string> report, List<string> issues)
        {
            if (!File.Exists(scenePath))
            {
                issues.Add($"Scene file missing: {scenePath}");
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var origins = UnityEngine.Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include);
            if (origins.Length != 1)
                issues.Add($"{scenePath}: Expected 1 XROrigin, found {origins.Length}.");
            foreach (var origin in origins)
            {
                var pose = origin.Camera ? origin.Camera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>() : null;
                if (!pose || !pose.enabled || !origin.Camera.enabled || pose.trackingType != UnityEngine.InputSystem.XR.TrackedPoseDriver.TrackingType.RotationAndPosition)
                    issues.Add($"{scenePath}: Camera requires an enabled position-and-rotation tracked pose driver.");
            }
            foreach (var simulator in UnityEngine.Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRDeviceSimulator>(FindObjectsInactive.Include))
                if (!simulator.CompareTag("EditorOnly")) issues.Add($"{scenePath}: Desktop XR simulator must be EditorOnly for device builds.");
            var teleport = UnityEngine.Object.FindAnyObjectByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider>();
            var floor = UnityEngine.Object.FindAnyObjectByType<ClearTeleportArea>();
            if (!teleport || !teleport.enabled || !floor || !floor.enabled)
                issues.Add($"{scenePath}: Enabled teleport provider and clear floor are required.");

            var modality = UnityEngine.Object.FindAnyObjectByType<XRInputModalityManager>(FindObjectsInactive.Include);
            if (modality == null)
            {
                issues.Add($"{scenePath}: Missing XRInputModalityManager.");
            }
            else if (modality.leftController == null || modality.rightController == null || modality.leftHand == null || modality.rightHand == null)
            {
                issues.Add($"{scenePath}: XRInputModalityManager must have leftController, rightController, leftHand, and rightHand assigned.");
            }

            if (UnityEngine.Object.FindAnyObjectByType<QuestMultimodalRig>(FindObjectsInactive.Include) == null)
                issues.Add($"{scenePath}: Missing QuestMultimodalRig.");

            if (UnityEngine.Object.FindAnyObjectByType<QuestPerformanceOptimizer>(FindObjectsInactive.Include) == null)
                issues.Add($"{scenePath}: Missing QuestPerformanceOptimizer.");

            if (UnityEngine.Object.FindAnyObjectByType<HubRendering>(FindObjectsInactive.Include) == null)
                issues.Add($"{scenePath}: Missing HubRendering.");

            int missingScripts = 0;
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            }

            if (missingScripts > 0)
                issues.Add($"{scenePath}: Found {missingScripts} missing script component(s).");

            if (origins.Length == 1 && missingScripts == 0 && modality != null
                && modality.leftController != null && modality.rightController != null
                && modality.leftHand != null && modality.rightHand != null)
            {
                report.Add($"[PASS] Scene validated: {scenePath}");
            }
        }

        static void CheckFeature<T>(OpenXRSettings openXr, string label, List<string> report, List<string> issues) where T : OpenXRFeature
        {
            var feature = openXr.GetFeature<T>();
            if (feature != null && feature.enabled)
                report.Add($"[PASS] OpenXR feature enabled: {label}");
            else
                issues.Add($"OpenXR feature missing or disabled for Android: {label}");
        }

        static void EnsurePermission(XmlDocument doc, XmlElement manifestNode, string permissionName)
        {
            foreach (XmlNode child in manifestNode.ChildNodes)
            {
                if (child is XmlElement el && el.Name == "uses-permission"
                    && el.GetAttribute("name", AndroidNamespace) == permissionName)
                {
                    return;
                }
            }

            var node = doc.CreateElement("uses-permission");
            node.SetAttribute("name", AndroidNamespace, permissionName);
            manifestNode.AppendChild(node);
        }

        static void EnsureFeature(XmlDocument doc, XmlElement manifestNode, string featureName, bool required, string version)
        {
            foreach (XmlNode child in manifestNode.ChildNodes)
            {
                if (child is XmlElement el && el.Name == "uses-feature"
                    && el.GetAttribute("name", AndroidNamespace) == featureName)
                {
                    el.SetAttribute("required", AndroidNamespace, required ? "true" : "false");
                    if (!string.IsNullOrEmpty(version))
                        el.SetAttribute("version", AndroidNamespace, version);
                    return;
                }
            }

            var node = doc.CreateElement("uses-feature");
            node.SetAttribute("name", AndroidNamespace, featureName);
            node.SetAttribute("required", AndroidNamespace, required ? "true" : "false");
            if (!string.IsNullOrEmpty(version))
                node.SetAttribute("version", AndroidNamespace, version);
            manifestNode.AppendChild(node);
        }

        static void RemoveMetaData(XmlDocument doc, string name)
        {
            var applicationNode = doc.SelectSingleNode("/manifest/application") as XmlElement;
            if (applicationNode == null)
                return;

            var toRemove = new List<XmlNode>();
            foreach (XmlNode child in applicationNode.ChildNodes)
            {
                if (child is XmlElement el && el.Name == "meta-data"
                    && el.GetAttribute("name", AndroidNamespace) == name)
                {
                    toRemove.Add(el);
                }
            }

            foreach (var node in toRemove)
                applicationNode.RemoveChild(node);
        }
    }
}
