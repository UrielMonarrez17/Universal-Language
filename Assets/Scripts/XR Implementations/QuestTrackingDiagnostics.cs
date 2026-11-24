using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Alterlife.Immersive
{
    /// <summary>Short startup diagnostics for on-device beta verification; no persistent recording.</summary>
    public sealed class QuestTrackingDiagnostics : MonoBehaviour
    {
        IEnumerator Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var origin = GetComponent<Unity.XR.CoreUtils.XROrigin>();
            Vector3 first = Vector3.zero; Quaternion rotation = Quaternion.identity;
            bool sampled = false; float movement = 0, angle = 0;
            for (int i = 0; i < 40; i++)
            {
                yield return new WaitForSecondsRealtime(1);
                var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                head.TryGetFeatureValue(CommonUsages.trackingState, out var state);
                if (origin && origin.Camera && (state & InputTrackingState.Position) != 0)
                {
                    var camera = origin.Camera.transform;
                    if (!sampled) { first = camera.localPosition; rotation = camera.localRotation; sampled = true; }
                    movement = Mathf.Max(movement, Vector3.Distance(first, camera.localPosition));
                    angle = Mathf.Max(angle, Quaternion.Angle(rotation, camera.localRotation));
                }
                if (i % 5 != 0) continue;
                var displays = new List<XRDisplaySubsystem>(); SubsystemManager.GetSubsystems(displays);
                bool running = displays.Exists(d => d.running);
                Debug.Log($"QUEST_BETA_TRACKING scene={gameObject.scene.name} stereoRunning={running} headValid={head.isValid} tracking={state} movementMeters={movement:F3} rotationDegrees={angle:F1} left={InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).isValid} right={InputDevices.GetDeviceAtXRNode(XRNode.RightHand).isValid}");
            }
#else
            yield break;
#endif
        }
    }
}
