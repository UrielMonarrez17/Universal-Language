using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Alterlife.Immersive
{
    public sealed class RestaurantMenu : MonoBehaviour
    {
        public RestaurantEvaluation evaluation;
        XRGrabInteractable grab;
        Vector3 restPosition;
        Quaternion restRotation;
        void Awake()
        {
            restPosition = transform.position; restRotation = transform.rotation;
            grab = GetComponent<XRGrabInteractable>();
            grab.selectEntered.AddListener(Inspect);
        }
        void Inspect(SelectEnterEventArgs _) => evaluation.InspectMenu();
        void OnDestroy() { if (grab) grab.selectEntered.RemoveListener(Inspect); }
        void Update()
        {
            if (!grab.isSelected && transform.position.y < -.5f)
            {
                transform.SetPositionAndRotation(restPosition, restRotation);
                var body = GetComponent<Rigidbody>(); body.linearVelocity = body.angularVelocity = Vector3.zero;
            }
        }
    }
}
