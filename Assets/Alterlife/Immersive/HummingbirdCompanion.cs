using System;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace Alterlife.Immersive
{
    public enum CompanionState
    {
        Observing,
        HintLevel1,
        RescueLevel2,
        Celebrating
    }

    [Serializable]
    public struct FoxHelpOption
    {
        public string french;
        public string englishDescription;
    }

    /// <summary>
    /// Fox companion implementing Krashen's Low Affective Filter and i+1 scaffolded assistance.
    /// Summoned automatically on prolonged user pause or with Left Controller Y button,
    /// presenting up to 3 bilingual French/English options selectable by Trigger, Pinch, or Voice.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HummingbirdCompanion : MonoBehaviour
    {
        [Header("Target Tracking (VR Comfort)")]
        [Tooltip("The main XR Camera (head) to anchor relative comfort positioning.")]
        [SerializeField] private Transform headCamera;
        [Tooltip("Local offset relative to head: X=right shoulder, Y=height, Z=forward distance.")]
        [SerializeField] private Vector3 shoulderOffset = new Vector3(0.42f, -0.14f, 0.58f);
        [SerializeField] private float followDamping = 3.5f;
        [SerializeField] private float rotationDamping = 4.0f;
        [SerializeField] private float bobbingSpeed = 2.5f;
        [SerializeField] private float bobbingAmplitude = 0.035f;

        [Header("Visual Feedback & Palette")]
        [SerializeField] private Color cyanHintColor = new Color(0.31f, 0.82f, 0.77f, 1f);
        [SerializeField] private Color goldCelebrationColor = new Color(0.92f, 0.70f, 0.03f, 1f);
        [SerializeField] private Light auraLight;
        [SerializeField] private ParticleSystem auraParticles;
        [SerializeField] private ParticleSystem goldCelebrationParticles;

        [Header("Floating 3D Vocab Label (Level 1)")]
        [SerializeField] private GameObject vocabLabelRoot;
        [SerializeField] private TextMeshPro vocabLabelText;

        [Header("Dialogue Cards Container (Level 2 Rescue - 3 Options)")]
        [SerializeField] private GameObject dialogueCardsRoot;
        [SerializeField] private TextMeshPro cardOptionAText;
        [SerializeField] private TextMeshPro cardOptionBText;
        [SerializeField] private TextMeshPro cardOptionCText;
        [SerializeField] private PhysicalControl cardOptionAControl;
        [SerializeField] private PhysicalControl cardOptionBControl;
        [SerializeField] private PhysicalControl cardOptionCControl;

        [Header("Controller Shortcut")]
        [SerializeField] private bool enableLeftYButtonSummon = true;

        [Header("Audio Feedback")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip chirpHintClip;
        [SerializeField] private AudioClip celebrateClip;

        public CompanionState CurrentState { get; private set; } = CompanionState.Observing;
        public bool IsShowingHelp => CurrentState == CompanionState.RescueLevel2;

        public event Action HelpRequestedByButton;

        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private Transform activeHintObject;
        private Action onCardASelected;
        private Action onCardBSelected;
        private Action onCardCSelected;
        private float stateTimer;
        private bool wasLeftYPressed;
        private Vector3 rescueAnchorPos;
        private Quaternion rescueAnchorRot;
        private bool hasRescueAnchor;

        private void Awake()
        {
            if (headCamera == null && Camera.main != null)
                headCamera = Camera.main.transform;

            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            if (cardOptionAControl != null) cardOptionAControl.pressed.AddListener(SelectCardA);
            if (cardOptionBControl != null) cardOptionBControl.pressed.AddListener(SelectCardB);
            if (cardOptionCControl != null) cardOptionCControl.pressed.AddListener(SelectCardC);

            SetState(CompanionState.Observing);
        }

        private void OnDestroy()
        {
            if (cardOptionAControl != null) cardOptionAControl.pressed.RemoveListener(SelectCardA);
            if (cardOptionBControl != null) cardOptionBControl.pressed.RemoveListener(SelectCardB);
            if (cardOptionCControl != null) cardOptionCControl.pressed.RemoveListener(SelectCardC);
        }

        private void Update()
        {
            if (headCamera == null && Camera.main != null)
                headCamera = Camera.main.transform;

            PollLeftControllerYButton();
            UpdateMovement();
            UpdateStateBehavior();
        }

        private void PollLeftControllerYButton()
        {
            if (!enableLeftYButtonSummon)
                return;

            InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!leftDevice.isValid)
            {
                wasLeftYPressed = false;
                return;
            }

            bool yPressed = leftDevice.TryGetFeatureValue(CommonUsages.secondaryButton, out bool pressed) && pressed;
            if (yPressed && !wasLeftYPressed)
            {
                if (HelpRequestedByButton != null)
                {
                    HelpRequestedByButton.Invoke();
                }
                else
                {
                    var eval = FindAnyObjectByType<RestaurantEvaluation>();
                    if (eval != null)
                        eval.ToggleFoxHelp();
                }
            }

            wasLeftYPressed = yPressed;
        }

        private void UpdateMovement()
        {
            if (headCamera == null) return;

            Vector3 forwardYaw = Vector3.ProjectOnPlane(headCamera.forward, Vector3.up).normalized;
            if (forwardYaw.sqrMagnitude < 0.01f) forwardYaw = Vector3.forward;
            Quaternion yawRotation = Quaternion.LookRotation(forwardYaw, Vector3.up);

            switch (CurrentState)
            {
                case CompanionState.Observing:
                    Vector3 desiredPosition = headCamera.position + (yawRotation * shoulderOffset);
                    desiredPosition.y += Mathf.Sin(Time.time * bobbingSpeed) * bobbingAmplitude;

                    targetPosition = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * followDamping);
                    targetRotation = Quaternion.Slerp(transform.rotation, yawRotation, Time.deltaTime * rotationDamping);

                    transform.position = targetPosition;
                    transform.rotation = targetRotation;
                    break;

                case CompanionState.HintLevel1:
                    if (activeHintObject != null)
                    {
                        Vector3 hintHoverPos = activeHintObject.position + Vector3.up * 0.28f;
                        hintHoverPos.y += Mathf.Sin(Time.time * bobbingSpeed * 1.5f) * (bobbingAmplitude * 0.5f);
                        transform.position = Vector3.Lerp(transform.position, hintHoverPos, Time.deltaTime * (followDamping * 1.5f));

                        Vector3 toHead = headCamera.position - transform.position;
                        if (toHead.sqrMagnitude > 0.01f)
                        {
                            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toHead, Vector3.up), Time.deltaTime * rotationDamping);
                        }
                    }
                    break;

                case CompanionState.RescueLevel2:
                    Vector3 foxTarget = hasRescueAnchor
                        ? rescueAnchorPos + (rescueAnchorRot * new Vector3(-0.48f, 0.14f, 0.02f))
                        : headCamera.position + (yawRotation * new Vector3(-0.35f, -0.05f, 0.85f));
                    foxTarget.y += Mathf.Sin(Time.time * bobbingSpeed) * (bobbingAmplitude * 0.45f);
                    transform.position = Vector3.Lerp(transform.position, foxTarget, Time.deltaTime * (followDamping * 1.3f));

                    Vector3 toUser = headCamera.position - transform.position;
                    toUser.y = 0f;
                    if (toUser.sqrMagnitude > 0.01f)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toUser, Vector3.up), Time.deltaTime * rotationDamping);
                    }
                    break;

                case CompanionState.Celebrating:
                    stateTimer += Time.deltaTime;
                    float angle = stateTimer * 4.0f;
                    Vector3 circleOffset = new Vector3(Mathf.Cos(angle) * 0.55f, 0.16f + Mathf.Sin(angle * 2.0f) * 0.08f, Mathf.Sin(angle) * 0.55f);
                    transform.position = Vector3.Lerp(transform.position, headCamera.position + circleOffset, Time.deltaTime * 6f);
                    Vector3 lookDir = headCamera.position - transform.position;
                    if (lookDir.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.LookRotation(lookDir);

                    if (stateTimer >= 2.2f)
                        SetState(CompanionState.Observing);
                    break;
            }
        }

        private void UpdateStateBehavior()
        {
            if (vocabLabelRoot != null && vocabLabelRoot.activeSelf && headCamera != null)
            {
                Vector3 toHead = headCamera.position - vocabLabelRoot.transform.position;
                if (toHead.sqrMagnitude > 0.001f)
                {
                    vocabLabelRoot.transform.rotation = Quaternion.LookRotation(-toHead, Vector3.up);
                }
            }

            if (dialogueCardsRoot != null && dialogueCardsRoot.activeSelf && headCamera != null)
            {
                if (!hasRescueAnchor)
                    CaptureRescueAnchor();

                dialogueCardsRoot.transform.position = Vector3.Lerp(
                    dialogueCardsRoot.transform.position,
                    rescueAnchorPos,
                    Time.deltaTime * 8f);
                dialogueCardsRoot.transform.rotation = Quaternion.Slerp(
                    dialogueCardsRoot.transform.rotation,
                    rescueAnchorRot,
                    Time.deltaTime * 8f);
            }
        }

        private void CaptureRescueAnchor()
        {
            if (headCamera == null) return;
            Vector3 forwardYaw = Vector3.ProjectOnPlane(headCamera.forward, Vector3.up).normalized;
            if (forwardYaw.sqrMagnitude < 0.01f) forwardYaw = Vector3.forward;
            rescueAnchorRot = Quaternion.LookRotation(forwardYaw, Vector3.up);
            rescueAnchorPos = headCamera.position + forwardYaw * 0.98f + Vector3.down * 0.06f;
            hasRescueAnchor = true;

            if (dialogueCardsRoot != null)
            {
                dialogueCardsRoot.transform.SetPositionAndRotation(rescueAnchorPos, rescueAnchorRot);
            }
        }

        public void ShowHintLevel1(Transform targetProp, string frenchWord)
        {
            activeHintObject = targetProp;
            if (vocabLabelText != null) vocabLabelText.text = frenchWord;
            SetState(CompanionState.HintLevel1);
            PlaySound(chirpHintClip);
        }

        public void ShowRescueLevel2(string phraseA, Action onSelectA, string phraseB, Action onSelectB)
        {
            ShowRescueOptions(
                new[]
                {
                    new FoxHelpOption { french = phraseA, englishDescription = "Suggested polite response" },
                    new FoxHelpOption { french = phraseB, englishDescription = "Alternative response" }
                },
                idx =>
                {
                    if (idx == 0) onSelectA?.Invoke();
                    else if (idx == 1) onSelectB?.Invoke();
                });
        }

        /// <summary>
        /// Displays up to 3 bilingual French/English options floating comfortably in front of the player.
        /// Selectable by Trigger/Pinch on any card or by speaking the French phrase aloud.
        /// </summary>
        public void ShowRescueOptions(FoxHelpOption[] options, Action<int> onSelectOption)
        {
            if (options == null || options.Length == 0)
                return;

            onCardASelected = options.Length > 0 ? () => onSelectOption?.Invoke(0) : null;
            onCardBSelected = options.Length > 1 ? () => onSelectOption?.Invoke(1) : null;
            onCardCSelected = options.Length > 2 ? () => onSelectOption?.Invoke(2) : null;

            ApplyCardVisual(cardOptionAControl, cardOptionAText, options.Length > 0 ? options[0] : default, options.Length > 0);
            ApplyCardVisual(cardOptionBControl, cardOptionBText, options.Length > 1 ? options[1] : default, options.Length > 1);
            ApplyCardVisual(cardOptionCControl, cardOptionCText, options.Length > 2 ? options[2] : default, options.Length > 2);

            hasRescueAnchor = false;
            CaptureRescueAnchor();
            SetState(CompanionState.RescueLevel2);
            PlaySound(chirpHintClip);
        }

        private static void ApplyCardVisual(PhysicalControl control, TMP_Text label, FoxHelpOption option, bool visible)
        {
            if (control != null)
                control.gameObject.SetActive(visible);

            if (!visible || label == null)
                return;

            string desc = string.IsNullOrWhiteSpace(option.englishDescription)
                ? ""
                : $"\n<size=68%><color=#94A3B8>{option.englishDescription}</color></size>";

            label.text = $"<b><color=#F8FAFC>{option.french}</color></b>{desc}";
        }

        public void SelectCardA()
        {
            var callback = onCardASelected;
            ResetToObserving();
            callback?.Invoke();
            Celebrate();
        }

        public void SelectCardB()
        {
            var callback = onCardBSelected;
            ResetToObserving();
            callback?.Invoke();
            Celebrate();
        }

        public void SelectCardC()
        {
            var callback = onCardCSelected;
            ResetToObserving();
            callback?.Invoke();
            Celebrate();
        }

        public void Celebrate()
        {
            stateTimer = 0f;
            SetState(CompanionState.Celebrating);
            if (goldCelebrationParticles != null)
                goldCelebrationParticles.Play();
            PlaySound(celebrateClip);
        }

        public void ResetToObserving()
        {
            activeHintObject = null;
            onCardASelected = null;
            onCardBSelected = null;
            onCardCSelected = null;
            hasRescueAnchor = false;
            SetState(CompanionState.Observing);
        }

        private void SetState(CompanionState newState)
        {
            CurrentState = newState;

            bool isHint = newState == CompanionState.HintLevel1;
            bool isRescue = newState == CompanionState.RescueLevel2;
            bool isCelebration = newState == CompanionState.Celebrating;

            if (vocabLabelRoot != null) vocabLabelRoot.SetActive(isHint);
            if (dialogueCardsRoot != null) dialogueCardsRoot.SetActive(isRescue);

            if (auraLight != null)
            {
                auraLight.color = isCelebration ? goldCelebrationColor : cyanHintColor;
                auraLight.intensity = (isHint || isRescue || isCelebration) ? 2.5f : 0.8f;
            }
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }
    }
}
