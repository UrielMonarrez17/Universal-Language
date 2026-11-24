using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Alterlife.Immersive
{
    /// <summary>Reject destinations occupied by furniture/walls, including the player's full standing capsule.</summary>
    public sealed class ClearTeleportArea : TeleportationArea
    {
        public LayerMask obstacleLayers = 1;
        readonly Collider[] overlaps = new Collider[24];
        public bool IsClear(Vector3 point)
        {
            int count = Physics.OverlapCapsuleNonAlloc(point + Vector3.up * .32f, point + Vector3.up * 1.7f,
                .27f, overlaps, obstacleLayers, QueryTriggerInteraction.Ignore);
            return count == 0;
        }
        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if (!base.IsSelectableBy(interactor)) return false;
            return !(interactor is XRRayInteractor ray) || !ray.TryGetCurrent3DRaycastHit(out var hit) || IsClear(hit.point);
        }
        protected override bool GenerateTeleportRequest(IXRInteractor interactor, RaycastHit hit, ref TeleportRequest request)
            => IsClear(hit.point) && base.GenerateTeleportRequest(interactor, hit, ref request);
    }
}
