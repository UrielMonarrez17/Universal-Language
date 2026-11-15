using UnityEngine;
namespace Alterlife.Immersive
{
    public sealed class LampSwitch : MonoBehaviour
    {
        public Light lamp;
        public void Toggle() { lamp.enabled = !lamp.enabled; }
    }
}
