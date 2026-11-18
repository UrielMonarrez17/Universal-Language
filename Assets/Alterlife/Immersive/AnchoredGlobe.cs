using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Anchored geographic globe with realistic horizontal yaw rotation,
    /// ray/direct XR grab support, and smooth angular inertia upon release.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class AnchoredGlobe : MonoBehaviour
    {
        [Header("Inertia & Physics Feel")]
        [SerializeField] private float angularFriction = 3.0f;
        [SerializeField] private float maxAngularVelocity = 180.0f; // degrees per second

        private XRSimpleInteractable interactable;
        private IXRSelectInteractor hand;
        private Quaternion handStart, globeStart;
        private Vector3 anchor;

        private float currentYawVelocity;
        private float lastYawAngle;

        private void Awake()
        {
            anchor = transform.localPosition;
            interactable = GetComponent<XRSimpleInteractable>();
            interactable.selectEntered.AddListener(Begin);
            interactable.selectExited.AddListener(End);
        }

        private void Begin(SelectEnterEventArgs args)
        {
            hand = args.interactorObject;
            handStart = hand.transform.rotation;
            globeStart = transform.rotation;
            currentYawVelocity = 0f;
            lastYawAngle = 0f;
        }

        private void End(SelectExitEventArgs _)
        {
            hand = null;
        }

        private void LateUpdate()
        {
            if (hand != null)
            {
                ApplyRotation(hand.transform.rotation, handStart, globeStart);
            }
            else if (Mathf.Abs(currentYawVelocity) > 0.05f)
            {
                // Apply smooth angular inertia decay
                float step = currentYawVelocity * Time.deltaTime;
                transform.rotation = Quaternion.AngleAxis(step, Vector3.up) * transform.rotation;
                currentYawVelocity = Mathf.Lerp(currentYawVelocity, 0f, Time.deltaTime * angularFriction);
            }

            // Keep anchored to the pedestal
            transform.localPosition = anchor;
        }

        public void ApplyRotation(Quaternion current, Quaternion initial, Quaternion globeInitial)
        {
            // Horizontal sweep only. Preserve the globe's authored tilt; ignore wrist pitch and roll.
            Vector3 from = Vector3.ProjectOnPlane(initial * Vector3.forward, Vector3.up);
            Vector3 to = Vector3.ProjectOnPlane(current * Vector3.forward, Vector3.up);
            if (from.sqrMagnitude < .01f || to.sqrMagnitude < .01f) return;

            // The front surface must follow the player's horizontal sweep, not move against it.
            float yaw = -Vector3.SignedAngle(from, to, Vector3.up);
            transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * globeInitial;

            // Calculate instantaneous angular velocity for release inertia
            if (Time.deltaTime > 0f)
            {
                float deltaAngle = Mathf.DeltaAngle(lastYawAngle, yaw);
                float instantaneousVelocity = deltaAngle / Time.deltaTime;
                currentYawVelocity = Mathf.Clamp(instantaneousVelocity, -maxAngularVelocity, maxAngularVelocity);
                lastYawAngle = yaw;
            }
        }

        private void OnDisable()
        {
            hand = null;
            currentYawVelocity = 0f;
        }

        private void OnDestroy()
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
