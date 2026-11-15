using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace Alterlife.Immersive
{
    public sealed class ComfortOptions : MonoBehaviour
    {
        public ControllerInputActionManager leftController;
        public TMP_Text label;
        public void ToggleSmoothMovement()
        {
            leftController.smoothMotionEnabled = !leftController.smoothMotionEnabled;
            Refresh();
        }
        void Start() { leftController.smoothMotionEnabled = false; Refresh(); }
        void Refresh() => label.text = leftController.smoothMotionEnabled
            ? "MOVEMENT: SMOOTH\nLeft stick to walk • Right stick to teleport\nSelect to use teleport only"
            : "MOVEMENT: TELEPORT\nPush stick forward • Aim at floor • Release\nSelect for optional smooth movement";
    }
}
