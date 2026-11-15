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
            transform.rotation = current * Quaternion.Inverse(initial) * globeInitial;
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
