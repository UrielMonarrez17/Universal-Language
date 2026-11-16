using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Alterlife.Immersive
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class AnchoredGlobe : MonoBehaviour
    {
        XRSimpleInteractable interactable;
        IXRSelectInteractor hand;
        Quaternion handStart, globeStart;
        Vector3 anchor;
        void Awake()
        {
            anchor = transform.localPosition;
            interactable = GetComponent<XRSimpleInteractable>();
            interactable.selectEntered.AddListener(Begin);
            interactable.selectExited.AddListener(End);
        }
        void Begin(SelectEnterEventArgs args)
        {
            hand = args.interactorObject;
            handStart = hand.transform.rotation;
            globeStart = transform.rotation;
        }
        void End(SelectExitEventArgs _) { hand = null; }
        void LateUpdate()
        {
            if (hand != null) ApplyRotation(hand.transform.rotation, handStart, globeStart);
            transform.localPosition = anchor;
        }
        public void ApplyRotation(Quaternion current, Quaternion initial, Quaternion globeInitial)
        {
            // Horizontal sweep only. Preserve the globe's authored tilt; ignore wrist pitch and roll.
            Vector3 from = Vector3.ProjectOnPlane(initial * Vector3.forward, Vector3.up);
            Vector3 to = Vector3.ProjectOnPlane(current * Vector3.forward, Vector3.up);
            if (from.sqrMagnitude < .01f || to.sqrMagnitude < .01f) return;
            float yaw = Vector3.SignedAngle(from, to, Vector3.up);
            transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * globeInitial;
        }
        void OnDisable() { hand = null; }
        void OnDestroy()
        {
            if (!interactable) return;
            interactable.selectEntered.RemoveListener(Begin);
            interactable.selectExited.RemoveListener(End);
        }
        public static Vector3 GeographicPoint(float longitude, float latitude, float radius)
        {
            float lon = longitude * Mathf.Deg2Rad, lat = latitude * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), -Mathf.Cos(lat) * Mathf.Cos(lon)) * radius;
        }
    }
}
