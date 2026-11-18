using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Diegetic VR Wrist Menu attached to the non-dominant hand.
    /// Provides comfort controls (Day/Night, Seated/Standing calibration)
    /// and quick access to the pet companion and Hub navigation.
    /// </summary>
    public sealed class WristMenuController : MonoBehaviour
    {
        [Header("Tracking & Activation")]
        [Tooltip("The main XR HMD Camera.")]
        [SerializeField] private Transform headCamera;
        [Tooltip("Canvas root of the wrist menu.")]
        [SerializeField] private GameObject menuCanvas;
        [Tooltip("Maximum angle (degrees) between wrist up vector and gaze to display menu.")]
        [SerializeField] private float activationAngle = 50f;
        [SerializeField] private bool forceVisibleForDebug = false;

        [Header("Day / Night Toggle")]
        [SerializeField] private Material daySkyboxMaterial;
        [SerializeField] private Material nightSkyboxMaterial;
        [SerializeField] private Light directionalSunLight;
        [SerializeField] private Color daySunColor = new Color(1f, 0.95f, 0.86f);
        [SerializeField] private Color nightSunColor = new Color(0.2f, 0.25f, 0.45f);
        [SerializeField] private TextMeshProUGUI dayNightStatusText;
        private bool isNight = false;

        [Header("Comfort & Posture Calibration")]
        [Tooltip("XR Origin or Camera Floor Offset object to adjust height for seated mode.")]
        [SerializeField] private Transform xrOriginOffsetTransform;
        [SerializeField] private float seatedHeightAdjustment = 0.4f;
        [SerializeField] private TextMeshProUGUI seatedStandingStatusText;
        private bool isSeatedMode = false;

        [Header("Companion & Navigation Access")]
        [SerializeField] private HummingbirdCompanion companion;

        private void Start()
        {
            if (headCamera == null && Camera.main != null)
            {
                headCamera = Camera.main.transform;
            }

            if (companion == null)
            {
                companion = FindAnyObjectByType<HummingbirdCompanion>();
            }

            UpdateDayNightUI();
            UpdateSeatedStandingUI();
        }

        private void Update()
        {
            if (menuCanvas == null || headCamera == null) return;

            if (forceVisibleForDebug)
            {
                menuCanvas.SetActive(true);
                return;
            }

            // Gaze / Wrist angle check: activate when looking at the inner wrist
            Vector3 wristNormal = transform.up;
            Vector3 gazeDir = (headCamera.position - transform.position).normalized;
            float angle = Vector3.Angle(wristNormal, gazeDir);

            bool shouldBeVisible = angle <= activationAngle;
            if (menuCanvas.activeSelf != shouldBeVisible)
            {
                menuCanvas.SetActive(shouldBeVisible);
            }
        }

        /// <summary>
        /// Toggles between Day and Night environments dynamically.
        /// </summary>
        public void ToggleDayNight()
        {
            isNight = !isNight;

            if (isNight && nightSkyboxMaterial != null)
            {
                RenderSettings.skybox = nightSkyboxMaterial;
                if (directionalSunLight != null)
                {
                    directionalSunLight.color = nightSunColor;
                    directionalSunLight.intensity = 0.3f;
                }
            }
            else if (!isNight && daySkyboxMaterial != null)
            {
                RenderSettings.skybox = daySkyboxMaterial;
                if (directionalSunLight != null)
                {
                    directionalSunLight.color = daySunColor;
                    directionalSunLight.intensity = 1.0f;
                }
            }

            DynamicGI.UpdateEnvironment();
            UpdateDayNightUI();
        }

        /// <summary>
        /// Calibrates user height between seated and standing play.
        /// </summary>
        public void ToggleSeatedStandingMode()
        {
            isSeatedMode = !isSeatedMode;

            if (xrOriginOffsetTransform != null)
            {
                Vector3 pos = xrOriginOffsetTransform.localPosition;
                pos.y += isSeatedMode ? seatedHeightAdjustment : -seatedHeightAdjustment;
                xrOriginOffsetTransform.localPosition = pos;
            }

            UpdateSeatedStandingUI();
        }

        /// <summary>
        /// Instantly recalls the companion to the player's shoulder.
        /// </summary>
        public void SummonCompanion()
        {
            if (companion != null)
            {
                companion.ResetToObserving();
                companion.Celebrate();
            }
        }

        /// <summary>
        /// Returns the player to the Living Memory Album / World Hub.
        /// </summary>
        public void ReturnToHub()
        {
            ExperienceNavigator.ReturnToHub();
        }

        private void UpdateDayNightUI()
        {
            if (dayNightStatusText != null)
            {
                dayNightStatusText.text = isNight ? "Nuit (Night)" : "Jour (Day)";
            }
        }

        private void UpdateSeatedStandingUI()
        {
            if (seatedStandingStatusText != null)
            {
                seatedStandingStatusText.text = isSeatedMode ? "Mode: Assis (Seated)" : "Mode: Debout (Standing)";
            }
        }
    }
}
