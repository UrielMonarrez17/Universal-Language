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
        void Awake() { properties = new MaterialPropertyBlock(); }
        void LateUpdate()
        {
            bool blocked = Physics.CheckSphere(transform.position, .09f, obstacles, QueryTriggerInteraction.Ignore);
            opacity = Mathf.MoveTowards(opacity, blocked ? 1 : 0, Time.unscaledDeltaTime * 7);
            veil.enabled = opacity > .001f;
            properties.SetColor("_BaseColor", new Color(0, 0, 0, opacity));
            veil.SetPropertyBlock(properties);
        }
    }
}
