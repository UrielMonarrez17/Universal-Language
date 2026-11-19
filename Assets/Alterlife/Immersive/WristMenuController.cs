using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Minimalist Left-Wrist Menu that tracks either the Left Controller or Left Hand (depending on active modality).
    /// Displays when looking at the inner wrist (like a watch) OR toggled via the Left Controller X button.
    /// Houses Movement Mode, Posture (Standing/Seated), Return to Previous Scene (hidden in ImmersiveHub),
    /// and Paris-only quick actions (Fox Help & Restart Conversation).
    /// </summary>
    public sealed class WristMenuController : MonoBehaviour
    {
        [Header("Tracking & Activation")]
        [SerializeField] private Transform headCamera;
        [SerializeField] private QuestMultimodalRig multimodalRig;
        [SerializeField] private GameObject menuCanvas;
        [SerializeField] private float activationAngle = 52f;
        [SerializeField] private bool forceVisibleForDebug = false;

        [Header("Wrist Attachment Offsets")]
        [SerializeField] private Vector3 controllerLocalOffset = new Vector3(0.02f, 0.055f, -0.04f);
        [SerializeField] private Vector3 handLocalOffset = new Vector3(0.0f, 0.065f, -0.06f);

        [Header("Scene Systems")]
        [SerializeField] private ComfortOptions comfortOptions;
        [SerializeField] private SceneJourney sceneJourney;
        [SerializeField] private RestaurantEvaluation restaurantEvaluation;
        [SerializeField] private HummingbirdCompanion companion;

        [Header("Buttons & Labels")]
        [SerializeField] private TMP_Text movementStatusText;
        [SerializeField] private TMP_Text seatedStandingStatusText;
        [SerializeField] private GameObject returnButtonObject;
        [SerializeField] private TMP_Text returnButtonText;
        [SerializeField] private GameObject foxHelpButtonObject;
        [SerializeField] private GameObject restartButtonObject;

        private bool pinnedOpenByButton;
        private bool wasLeftXPressed;
        private bool isVisible = true;
        private float visibleSince;
        private readonly List<Renderer> cachedRenderers = new List<Renderer>();
        private readonly List<Collider> cachedColliders = new List<Collider>();
        private readonly List<TMP_Text> cachedTexts = new List<TMP_Text>();

        private void Awake()
        {
            if (menuCanvas == null)
                menuCanvas = gameObject;

            CacheVisualComponents();
            SetMenuVisible(false);
        }

        private void Start()
        {
            if (headCamera == null && Camera.main != null)
                headCamera = Camera.main.transform;

            if (multimodalRig == null)
                multimodalRig = FindAnyObjectByType<QuestMultimodalRig>();

            if (comfortOptions == null)
                comfortOptions = FindAnyObjectByType<ComfortOptions>(FindObjectsInactive.Include);

            if (sceneJourney == null)
                sceneJourney = FindAnyObjectByType<SceneJourney>(FindObjectsInactive.Include);

            if (restaurantEvaluation == null)
                restaurantEvaluation = FindAnyObjectByType<RestaurantEvaluation>(FindObjectsInactive.Include);

            if (companion == null)
                companion = FindAnyObjectByType<HummingbirdCompanion>(FindObjectsInactive.Include);

            ConfigureSceneButtons();
            RefreshLabels();
            SetMenuVisible(forceVisibleForDebug);
        }

        void CacheVisualComponents()
        {
            cachedRenderers.Clear();
            cachedColliders.Clear();
            cachedTexts.Clear();

            if (menuCanvas == null) return;

            foreach (var r in menuCanvas.GetComponentsInChildren<Renderer>(true))
            {
                if (r != null && r.GetComponent<TMP_Text>() == null)
                    cachedRenderers.Add(r);
            }
            foreach (var c in menuCanvas.GetComponentsInChildren<Collider>(true))
            {
                if (c != null)
                    cachedColliders.Add(c);
            }
            foreach (var t in menuCanvas.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t != null)
                    cachedTexts.Add(t);
            }
        }

        void ConfigureSceneButtons()
        {
            string activePath = SceneManager.GetActiveScene().path;
            bool isHub = activePath == SceneJourney.WorldScene || activePath.EndsWith("ImmersiveHub.unity");
            bool isBook = activePath == SceneJourney.BookScene || activePath.EndsWith("BookSelection.unity");
            bool isParis = restaurantEvaluation != null || activePath.EndsWith("Paris.unity");

            if (returnButtonObject != null)
            {
                returnButtonObject.SetActive(!isHub);
                if (returnButtonText != null)
                    returnButtonText.text = isParis ? "BACK TO BOOK" : (isBook ? "BACK TO GLOBE" : "BACK");
            }

            if (foxHelpButtonObject != null)
                foxHelpButtonObject.SetActive(isParis);

            if (restartButtonObject != null)
                restartButtonObject.SetActive(isParis);
        }

        private void LateUpdate()
        {
            if (headCamera == null && Camera.main != null)
                headCamera = Camera.main.transform;

            var wrist = FollowActiveLeftWrist();
            PollLeftControllerXButton();

            if (menuCanvas == null || headCamera == null || !wrist)
            {
                SetMenuVisible(false);
                return;
            }

            Vector3 fromHead = transform.position - headCamera.position;
            if (fromHead.sqrMagnitude < 0.0001f) { SetMenuVisible(false); return; }
            // Labels and buttons sit on the panel's negative Z face.
            transform.rotation = Quaternion.LookRotation(fromHead, headCamera.up);

            if (forceVisibleForDebug || pinnedOpenByButton)
            {
                SetMenuVisible(true);
                return;
            }

            float angle = Vector3.Angle(wrist.up, -fromHead);
            SetMenuVisible(angle <= activationAngle && Vector3.Angle(headCamera.forward, fromHead) < 55f);
        }

        Transform FollowActiveLeftWrist()
        {
            if (multimodalRig == null)
                return null;

            bool useController = multimodalRig.ControllersActive;
            GameObject target = useController ? multimodalRig.leftController : multimodalRig.leftHand;
            if (target == null || !target.activeInHierarchy)
            {
                target = useController ? multimodalRig.leftHand : multimodalRig.leftController;
                useController = !useController;
            }

            if (target == null || !target.activeInHierarchy)
                return null;

            Vector3 localPos = useController ? controllerLocalOffset : handLocalOffset;

            transform.position = target.transform.TransformPoint(localPos);
            return target.transform;
        }

        internal bool AcceptsPress(IXRInteractor interactor, bool activated)
        {
            if (!isVisible || Time.unscaledTime - visibleSince < .25f || !multimodalRig ||
                !(interactor is XRBaseInputInteractor input)) return false;
            var t = input.transform;
            bool onRight = (multimodalRig.rightHand && t.IsChildOf(multimodalRig.rightHand.transform)) ||
                           (multimodalRig.rightController && t.IsChildOf(multimodalRig.rightController.transform));
            return onRight && (activated ? input.activateInput : input.selectInput).ReadWasPerformedThisFrame();
        }

        void PollLeftControllerXButton()
        {
            InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!leftDevice.isValid)
            {
                wasLeftXPressed = false;
                return;
            }

            bool xPressed = leftDevice.TryGetFeatureValue(CommonUsages.primaryButton, out bool pressed) && pressed;
            if (xPressed && !wasLeftXPressed)
            {
                pinnedOpenByButton = !pinnedOpenByButton;
            }
            wasLeftXPressed = xPressed;
        }

        void SetMenuVisible(bool visible)
        {
            if (isVisible == visible)
                return;

            isVisible = visible;
            if (visible) visibleSince = Time.unscaledTime;

            // Keep GameObjects active in hierarchy so scene queries/validations continue to find controls cleanly.
            for (int i = 0; i < cachedRenderers.Count; i++)
                if (cachedRenderers[i] != null) cachedRenderers[i].enabled = visible;

            for (int i = 0; i < cachedColliders.Count; i++)
                if (cachedColliders[i] != null) cachedColliders[i].enabled = visible;

            for (int i = 0; i < cachedTexts.Count; i++)
                if (cachedTexts[i] != null) cachedTexts[i].enabled = visible;
        }

        public void ToggleMovementMode()
        {
            if (comfortOptions != null)
                comfortOptions.ToggleSmoothMovement();
            RefreshLabels();
        }

        public void ToggleSeatedStandingMode()
        {
            if (comfortOptions != null)
                comfortOptions.TogglePosture();
            RefreshLabels();
        }

        public void ReturnToPreviousScene()
        {
            if (restaurantEvaluation != null)
            {
                restaurantEvaluation.ReturnToBook();
                return;
            }

            if (sceneJourney != null && sceneJourney.bookRoom)
            {
                sceneJourney.OpenWorld();
                return;
            }

            ExperienceNavigator.ReturnToHub();
        }

        public void SummonFoxHelp()
        {
            if (restaurantEvaluation != null)
            {
                restaurantEvaluation.ToggleFoxHelp();
                return;
            }

            if (companion != null)
            {
                companion.ResetToObserving();
                companion.Celebrate();
            }
        }

        public void RestartConversation()
        {
            if (restaurantEvaluation != null)
                restaurantEvaluation.Begin();
        }

        // Backwards-compatible aliases used by legacy Editor builders (e.g. ApartmentPilotBuilder)
        public void ToggleDayNight() { }
        public void SummonCompanion() => SummonFoxHelp();
        public void ReturnToHub() => ReturnToPreviousScene();

        public void RefreshLabels()
        {
            if (movementStatusText != null)
            {
                bool smooth = comfortOptions != null && comfortOptions.leftController != null && comfortOptions.leftController.smoothMotionEnabled;
                movementStatusText.text = smooth ? "MOVE: SMOOTH" : "MOVE: TELEPORT";
            }

            if (seatedStandingStatusText != null)
            {
                bool seated = comfortOptions != null && comfortOptions.IsSeatedMode;
                seatedStandingStatusText.text = seated ? "POSTURE: SEATED" : "POSTURE: STANDING";
            }
        }
    }
}
