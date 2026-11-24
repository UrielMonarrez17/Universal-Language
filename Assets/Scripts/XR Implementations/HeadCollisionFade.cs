using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Alterlife.Immersive
{
    /// <summary>Room-scale tracking cannot stop a physical head. Obscure world-geometry penetration while ignoring rig/hand/UI colliders.</summary>
    public sealed class HeadCollisionFade : MonoBehaviour
    {
        public Renderer veil;
        public LayerMask obstacles = 1;
        MaterialPropertyBlock properties;
        float opacity;
        Transform rigRoot;
        readonly Collider[] overlapHits = new Collider[16];

        public float TransitionOpacity { get; set; }

        void Awake()
        {
            properties = new MaterialPropertyBlock();
            var origin = GetComponentInParent<XROrigin>();
            rigRoot = origin != null ? origin.transform : transform.root;
        }

        void LateUpdate()
        {
            bool blocked = false;
            int count = Physics.OverlapSphereNonAlloc(transform.position, 0.08f, overlapHits, obstacles, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = overlapHits[i];
                overlapHits[i] = null;
                if (hit == null || hit.isTrigger)
                    continue;

                if (rigRoot != null && hit.transform.IsChildOf(rigRoot))
                    continue;

                if (hit.GetComponentInParent<XRBaseInteractor>() != null ||
                    hit.GetComponentInParent<WristMenuController>() != null ||
                    hit.GetComponentInParent<HummingbirdCompanion>() != null ||
                    hit.GetComponentInParent<VrBriefingOverlay>() != null)
                {
                    continue;
                }

                blocked = true;
                break;
            }

            opacity = Mathf.MoveTowards(opacity, blocked ? 1f : 0f, Time.unscaledDeltaTime * 7f);
            float combined = Mathf.Max(opacity, TransitionOpacity);
            if (veil != null)
            {
                veil.enabled = combined > .001f;
                properties.SetColor("_BaseColor", new Color(0, 0, 0, combined));
                veil.SetPropertyBlock(properties);
            }
        }
    }
}
