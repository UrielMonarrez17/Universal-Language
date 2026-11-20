using System.Collections.Generic;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace Alterlife.Immersive
{
    public sealed class ComfortOptions : MonoBehaviour
    {
        const string SeatedPrefKey = "Alterlife.Comfort.Seated";
        const string SmoothMovePrefKey = "Alterlife.Comfort.SmoothMove";
        public static bool PersistenceEnabled = true;

        public ControllerInputActionManager leftController;
        public TMP_Text label;

        [Header("Posture & Height Calibration")]
        public TMP_Text postureLabel;
        public XROrigin xrOrigin;
        public float seatedHeightOffset = 0.40f;

        public bool IsSeatedMode { get; private set; }
        public bool IsSmoothMovementEnabled =>
            leftController != null
                ? leftController.smoothMotionEnabled
                : (PersistenceEnabled && PlayerPrefs.GetInt(SmoothMovePrefKey, 0) == 1);

        void Awake()
        {
            ResolveReferences();
            ApplySavedMovementMode();
        }

        void Start()
        {
            ResolveReferences();
            ApplySavedMovementMode();

            IsSeatedMode = PersistenceEnabled && PlayerPrefs.GetInt(SeatedPrefKey, 0) == 1;
            ApplyPosture(IsSeatedMode);
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
            if (leftController == null)
                return;

            bool smooth = PersistenceEnabled && PlayerPrefs.GetInt(SmoothMovePrefKey, 0) == 1;
            leftController.smoothMotionEnabled = smooth;
        }

        public void ToggleSmoothMovement()
        {
            if (leftController)
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

        public void TogglePosture()
        {
            IsSeatedMode = !IsSeatedMode;
            if (PersistenceEnabled)
            {
                PlayerPrefs.SetInt(SeatedPrefKey, IsSeatedMode ? 1 : 0);
                PlayerPrefs.Save();
            }

            ApplyPosture(IsSeatedMode);
            RecenterView();
            Refresh();
        }

        public void RecenterView()
        {
            var inputs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var input in inputs)
            {
                if (input != null && input.running)
                    input.TryRecenter();
            }
        }

        public static void ApplySavedPosture(XROrigin origin, float seatedOffset = 0.40f)
        {
            if (origin == null || origin.CameraFloorOffsetObject == null)
                return;

            bool seated = PersistenceEnabled && PlayerPrefs.GetInt(SeatedPrefKey, 0) == 1;
            var offsetTransform = origin.CameraFloorOffsetObject.transform;
            var pos = offsetTransform.localPosition;
            offsetTransform.localPosition = new Vector3(pos.x, seated ? seatedOffset : 0f, pos.z);
        }

        void ApplyPosture(bool seated)
        {
            if (xrOrigin == null)
                xrOrigin = FindAnyObjectByType<XROrigin>();

            if (xrOrigin == null || xrOrigin.CameraFloorOffsetObject == null)
                return;

            var offsetTransform = xrOrigin.CameraFloorOffsetObject.transform;
            var pos = offsetTransform.localPosition;
            offsetTransform.localPosition = new Vector3(pos.x, seated ? seatedHeightOffset : 0f, pos.z);
        }

        void Refresh()
        {
            if (label && leftController)
            {
                label.text = leftController.smoothMotionEnabled
                    ? "MOVEMENT: SMOOTH\nLeft stick to walk • Right stick to teleport\nSelect to use teleport only"
                    : "MOVE FREELY\nWalk within your play area.\nPush stick forward, aim at floor, release to teleport.\nSelect for optional smooth movement.";
            }

            if (postureLabel)
            {
                postureLabel.text = IsSeatedMode ? "POSTURE: SEATED" : "POSTURE: STANDING";
            }
        }
    }
}
