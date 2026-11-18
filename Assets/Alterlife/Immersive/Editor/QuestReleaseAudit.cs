using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;

namespace Alterlife.Immersive.Editor
{
    public static class QuestReleaseAudit
    {
        public static readonly string[] Scenes = { SceneJourney.WorldScene, SceneJourney.BookScene, "Assets/Scenes/Paris.unity" };
        public static void Run()
        {
            Directory.CreateDirectory("Artifacts/QuestRelease");
            var report = new StringBuilder();
            report.AppendLine("Target=" + EditorUserBuildSettings.activeBuildTarget);
            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            report.AppendLine("XR init=" + (xr && xr.InitManagerOnStart) + " loaders=" + (xr && xr.Manager ? string.Join(",", xr.Manager.activeLoaders.Select(l => l.name)) : "missing"));
            foreach (var path in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(path);
                report.AppendLine("SCENE " + path);
                foreach (var camera in Object.FindObjectsByType<Camera>())
                    report.AppendLine("Camera " + camera.name + " enabled=" + camera.enabled + " stereo=" + camera.stereoTargetEye + " parent=" + camera.transform.parent?.name);
                foreach (var origin in Object.FindObjectsByType<XROrigin>())
                {
                    report.AppendLine("Origin pos=" + origin.transform.position + " scale=" + origin.transform.lossyScale + " mode=" + origin.RequestedTrackingOriginMode);
                    var pose = origin.Camera.GetComponent<TrackedPoseDriver>();
                    report.AppendLine("HeadPose=" + (pose ? "enabled=" + pose.enabled + " mode=" + pose.trackingType + " update=" + pose.updateType : "MISSING"));
                    if (pose)
                    {
                        report.AppendLine("Head position bindings=" + string.Join(",", pose.positionInput.action.bindings.Select(b => b.effectivePath)));
                        report.AppendLine("Head rotation bindings=" + string.Join(",", pose.rotationInput.action.bindings.Select(b => b.effectivePath)));
                    }
                }
                foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                {
                    if (!component) continue;
                    var name = component.GetType().Name;
                    if (!(name.Contains("Locomotion") || name.Contains("Teleport") || name.Contains("MoveProvider") || name.Contains("TurnProvider") || name.Contains("InputActionManager") || name.Contains("Simulator") || name.Contains("XRBody"))) continue;
                    report.AppendLine(name + " at " + component.name + " enabled=" + component.enabled + " active=" + component.gameObject.activeInHierarchy);
                    var props = new SerializedObject(component).GetIterator();
                    while (props.NextVisible(true))
                        if (props.propertyType == SerializedPropertyType.ObjectReference && props.objectReferenceValue)
                            report.AppendLine("  " + props.propertyPath + " -> " + props.objectReferenceValue.name);
                }
                report.AppendLine("MissingScripts=" + scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)));
            }
            File.WriteAllText("Artifacts/QuestRelease/baseline.txt", report.ToString());
            QuestBuildPipeline.ValidateQuestReadiness(false);
            Debug.Log("QUEST_RELEASE_AUDIT_COMPLETE");
        }
    }
}
