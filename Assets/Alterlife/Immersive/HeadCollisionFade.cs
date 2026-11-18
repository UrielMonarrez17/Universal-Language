using UnityEngine;

namespace Alterlife.Immersive
{
    /// <summary>Room-scale tracking cannot stop a physical head. Obscure geometry penetration instead.</summary>
    public sealed class HeadCollisionFade : MonoBehaviour
    {
        public Renderer veil;
        public LayerMask obstacles = 1;
        MaterialPropertyBlock properties;
        float opacity;
        public float TransitionOpacity { get; set; }
        void Awake() { properties = new MaterialPropertyBlock(); }
        void LateUpdate()
        {
            bool blocked = Physics.CheckSphere(transform.position, .09f, obstacles, QueryTriggerInteraction.Ignore);
            opacity = Mathf.MoveTowards(opacity, blocked ? 1 : 0, Time.unscaledDeltaTime * 7);
            float combined = Mathf.Max(opacity, TransitionOpacity);
            veil.enabled = combined > .001f;
            properties.SetColor("_BaseColor", new Color(0, 0, 0, combined));
            veil.SetPropertyBlock(properties);
        }
    }
}
