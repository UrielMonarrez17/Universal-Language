using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using Unity.XR.CoreUtils;

namespace Alterlife.Immersive.Editor
{
    public static class QuestReleaseSetup
    {
        public static void Run()
        {
            foreach (var path in QuestReleaseAudit.Scenes)
            {
                var scene = EditorSceneManager.OpenScene(path);
                foreach (var simulator in Object.FindObjectsByType<XRDeviceSimulator>(FindObjectsInactive.Include))
                {
                    // Strip simulated HMD/controller devices from the player, before any OnEnable can run.
                    simulator.gameObject.tag = "EditorOnly";
                    EditorUtility.SetDirty(simulator.gameObject);
                }
                var origin = Object.FindAnyObjectByType<XROrigin>();
                origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                origin.Camera.stereoTargetEye = StereoTargetEyeMask.Both;
                var pose = origin.Camera.GetComponent<TrackedPoseDriver>();
                if (!pose) throw new System.InvalidOperationException(path + ": missing HMD pose driver");
                pose.enabled = true; pose.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
                pose.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
                foreach (var controller in origin.GetComponentsInChildren<ControllerInputActionManager>(true))
                { controller.smoothMotionEnabled = false; controller.smoothTurnEnabled = false; }
                foreach (var floor in Object.FindObjectsByType<ClearTeleportArea>()) floor.enabled = true;
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            PlayerSettings.bundleVersion = "1.0.1-beta";
            PlayerSettings.Android.bundleVersionCode = Mathf.Max(2, PlayerSettings.Android.bundleVersionCode);
            AssetDatabase.SaveAssets();
            Debug.Log("QUEST_RELEASE_SCENES_PREPARED");
        }
    }
}
