using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Persists the player's chosen locomotion mode (Smooth vs Teleport) across all Alterlife scenes.
    /// </summary>
    public sealed class ComfortOptions : MonoBehaviour
    {
        const string SmoothMovePrefKey = "Alterlife.Comfort.SmoothMove";
        public static bool PersistenceEnabled = true;

        public ControllerInputActionManager leftController;
        public TMP_Text label;
        public XROrigin xrOrigin;
        public bool defaultSmoothMovement = true;

        public bool IsSmoothMovementEnabled =>
            leftController != null
                ? leftController.smoothMotionEnabled
                : (PersistenceEnabled
                    ? PlayerPrefs.GetInt(SmoothMovePrefKey, defaultSmoothMovement ? 1 : 0) == 1
                    : defaultSmoothMovement);

        void Awake()
        {
            ResolveReferences();
            ApplySavedMovementMode();
        }

        void Start()
        {
            ResolveReferences();
            ApplySavedMovementMode();
            Refresh();
        }

        void ResolveReferences()
        {
            if (xrOrigin == null)
                xrOrigin = FindAnyObjectByType<XROrigin>();

            if (leftController == null && xrOrigin != null)
            {
                foreach (var c in xrOrigin.GetComponentsInChildren<ControllerInputActionManager>(true))
                {
                    if (c != null && c.name.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        leftController = c;
                        break;
                    }
                }
            }
        }

        public void ApplySavedMovementMode()
        {
            if (leftController == null) return;
            int defaultVal = defaultSmoothMovement ? 1 : 0;
            leftController.smoothMotionEnabled = PersistenceEnabled
                ? PlayerPrefs.GetInt(SmoothMovePrefKey, defaultVal) == 1
                : defaultSmoothMovement;
        }

        public void ToggleSmoothMovement()
        {
            if (leftController != null)
            {
                bool next = !leftController.smoothMotionEnabled;
                leftController.smoothMotionEnabled = next;
                if (PersistenceEnabled)
                {
                    PlayerPrefs.SetInt(SmoothMovePrefKey, next ? 1 : 0);
                    PlayerPrefs.Save();
                }
            }
            Refresh();
        }

        void Refresh()
        {
            if (label && leftController)
            {
                label.text = leftController.smoothMotionEnabled
                    ? "MOVEMENT: SMOOTH\nLeft stick to walk • Right stick to teleport\nSelect to use teleport only"
                    : "MOVE FREELY\nWalk within your play area.\nPush stick forward, aim at floor, release to teleport.\nSelect for optional smooth movement.";
            }
        }
    }
}
