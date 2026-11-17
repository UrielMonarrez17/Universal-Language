using System;
using UnityEngine;
using TMPro;

namespace Alterlife.Immersive
{
    public enum CompanionState
    {
        Observing,
        HintLevel1,
        RescueLevel2,
        Celebrating
    }

    /// <summary>
    /// Organic companion (Hummingbird) implementing Krashen's Low Affective Filter and i+1 scaffolded assistance.
    /// Uses lazy-follow damped movement anchored to the player's peripheral comfort zone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HummingbirdCompanion : MonoBehaviour
    {
        [Header("Target Tracking (VR Comfort)")]
        [Tooltip("The main XR Camera (head) to anchor relative comfort positioning.")]
        [SerializeField] private Transform headCamera;
        [Tooltip("Local offset relative to head: X=right shoulder, Y=height, Z=forward distance.")]
        [SerializeField] private Vector3 shoulderOffset = new Vector3(0.35f, -0.1f, 0.45f);
        [SerializeField] private float followDamping = 3.5f;
        [SerializeField] private float rotationDamping = 4.0f;
        [SerializeField] private float bobbingSpeed = 2.5f;
        [SerializeField] private float bobbingAmplitude = 0.04f;

        [Header("Visual Feedback & Palette")]
        [Tooltip("Cyan glow color for Level 1 help (#4FD1C5).")]
        [SerializeField] private Color cyanHintColor = new Color(0.31f, 0.82f, 0.77f, 1f);
        [Tooltip("Gold color for Celebration/Success (#EAB308).")]
        [SerializeField] private Color goldCelebrationColor = new Color(0.92f, 0.70f, 0.03f, 1f);
        [SerializeField] private Light auraLight;
        [SerializeField] private ParticleSystem auraParticles;
        [SerializeField] private ParticleSystem goldCelebrationParticles;

        [Header("Floating 3D Vocab Label (Level 1)")]
        [SerializeField] private GameObject vocabLabelRoot;
        [SerializeField] private TextMeshPro vocabLabelText;

        [Header("Dialogue Cards Container (Level 2 Rescue)")]
        [SerializeField] private GameObject dialogueCardsRoot;
        [SerializeField] private TextMeshPro cardOptionAText;
        [SerializeField] private TextMeshPro cardOptionBText;
        [SerializeField] private PhysicalControl cardOptionAControl;
        [SerializeField] private PhysicalControl cardOptionBControl;

        [Header("Audio Feedback")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip chirpHintClip;
        [SerializeField] private AudioClip celebrateClip;

        // Runtime State
        public CompanionState CurrentState { get; private set; } = CompanionState.Observing;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private Transform activeHintObject;
        private Action onCardASelected;
        private Action onCardBSelected;
        private float stateTimer;

        private void Awake()
        {
            if (headCamera == null && Camera.main != null)
            {
                headCamera = Camera.main.transform;
            }

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (cardOptionAControl != null) cardOptionAControl.pressed.AddListener(SelectCardA);
            if (cardOptionBControl != null) cardOptionBControl.pressed.AddListener(SelectCardB);

            SetState(CompanionState.Observing);
        }

        private void OnDestroy()
        {
            if (cardOptionAControl != null) cardOptionAControl.pressed.RemoveListener(SelectCardA);
            if (cardOptionBControl != null) cardOptionBControl.pressed.RemoveListener(SelectCardB);
        }

        private void Update()
        {
            UpdateMovement();
            UpdateStateBehavior();
        }

        private void UpdateMovement()
        {
            if (headCamera == null) return;

            switch (CurrentState)
            {
                case CompanionState.Observing:
                    // Lazy-follow player's shoulder yaw (ignoring pitch/roll for stability)
                    Vector3 forwardYaw = Vector3.ProjectOnPlane(headCamera.forward, Vector3.up).normalized;
                    if (forwardYaw.sqrMagnitude < 0.01f) forwardYaw = Vector3.forward;
                    Quaternion yawRotation = Quaternion.LookRotation(forwardYaw, Vector3.up);

                    Vector3 desiredPosition = headCamera.position + (yawRotation * shoulderOffset);
                    // Add subtle natural hovering bob
                    desiredPosition.y += Mathf.Sin(Time.time * bobbingSpeed) * bobbingAmplitude;

                    targetPosition = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * followDamping);
                    targetRotation = Quaternion.Slerp(transform.rotation, yawRotation, Time.deltaTime * rotationDamping);

                    transform.position = targetPosition;
                    transform.rotation = targetRotation;
                    break;

                case CompanionState.HintLevel1:
                    if (activeHintObject != null)
                    {
                        // Hover directly above the key communicative object
                        Vector3 hintHoverPos = activeHintObject.position + Vector3.up * 0.25f;
                        hintHoverPos.y += Mathf.Sin(Time.time * bobbingSpeed * 1.5f) * (bobbingAmplitude * 0.5f);
                        transform.position = Vector3.Lerp(transform.position, hintHoverPos, Time.deltaTime * (followDamping * 1.5f));

                        // Face the player
                        Vector3 toHead = headCamera.position - transform.position;
                        if (toHead.sqrMagnitude > 0.01f)
                        {
                            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toHead, Vector3.up), Time.deltaTime * rotationDamping);
                        }
                    }
                    break;

                case CompanionState.RescueLevel2:
                    // Position in comfortable conversational plane between user and NPC
                    Vector3 midpoint = headCamera.position + (headCamera.forward * 0.9f) + (Vector3.down * 0.15f);
                    midpoint.y += Mathf.Sin(Time.time * bobbingSpeed) * (bobbingAmplitude * 0.5f);
                    transform.position = Vector3.Lerp(transform.position, midpoint, Time.deltaTime * followDamping);

                    Vector3 toUser = headCamera.position - transform.position;
                    if (toUser.sqrMagnitude > 0.01f)
                    {
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toUser, Vector3.up), Time.deltaTime * rotationDamping);
                    }
                    break;

                case CompanionState.Celebrating:
                    stateTimer += Time.deltaTime;
                    // Joyful circular sweep around player
                    float angle = stateTimer * 4.0f;
                    Vector3 circleOffset = new Vector3(Mathf.Cos(angle) * 0.5f, 0.2f + Mathf.Sin(angle * 2.0f) * 0.1f, Mathf.Sin(angle) * 0.5f);
                    transform.position = Vector3.Lerp(transform.position, headCamera.position + circleOffset, Time.deltaTime * 6f);
                    transform.rotation = Quaternion.LookRotation(headCamera.position - transform.position);

                    if (stateTimer >= 2.5f)
                    {
                        SetState(CompanionState.Observing);
                    }
                    break;
            }
        }

        private void UpdateStateBehavior()
        {
            // Face the vocabulary label towards the player's HMD
            if (vocabLabelRoot != null && vocabLabelRoot.activeSelf && headCamera != null)
            {
                vocabLabelRoot.transform.LookAt(headCamera.position);
                vocabLabelRoot.transform.Rotate(0, 180f, 0); // Correct for TMP text forward facing
            }

            // Face dialogue cards towards user
            if (dialogueCardsRoot != null && dialogueCardsRoot.activeSelf && headCamera != null)
            {
                dialogueCardsRoot.transform.LookAt(headCamera.position);
                dialogueCardsRoot.transform.Rotate(0, 180f, 0);
            }
        }

        /// <summary>
        /// Level 1 Scaffold: flies to the target 3D object, emits cyan aura and reveals vocabulary label.
        /// </summary>
        public void ShowHintLevel1(Transform targetProp, string frenchWord)
        {
            activeHintObject = targetProp;
            if (vocabLabelText != null) vocabLabelText.text = frenchWord;
            SetState(CompanionState.HintLevel1);

            PlaySound(chirpHintClip);
        }

        /// <summary>
        /// Level 2 Scaffold: Deploys two readable dialogue option cards for manual or spoken selection.
        /// </summary>
        public void ShowRescueLevel2(string phraseA, Action onSelectA, string phraseB, Action onSelectB)
        {
            onCardASelected = onSelectA;
            onCardBSelected = onSelectB;

            if (cardOptionAText != null) cardOptionAText.text = phraseA;
            if (cardOptionBText != null) cardOptionBText.text = phraseB;

            SetState(CompanionState.RescueLevel2);
            PlaySound(chirpHintClip);
        }

        /// <summary>
        /// Called when Card A is clicked via ray or physical hand poke.
        /// </summary>
        public void SelectCardA()
        {
            onCardASelected?.Invoke();
            Celebrate();
        }

        /// <summary>
        /// Called when Card B is clicked via ray or physical hand poke.
        /// </summary>
        public void SelectCardB()
        {
            onCardBSelected?.Invoke();
            Celebrate();
        }

        /// <summary>
        /// Positive Krashen reinforcement: golden particles, cheerful chirp, return to observing.
        /// </summary>
        public void Celebrate()
        {
            stateTimer = 0f;
            SetState(CompanionState.Celebrating);

            if (goldCelebrationParticles != null)
            {
                goldCelebrationParticles.Play();
            }

            PlaySound(celebrateClip);
        }

        /// <summary>
        /// Resets companion to peaceful observing state.
        /// </summary>
        public void ResetToObserving()
        {
            activeHintObject = null;
            onCardASelected = null;
            onCardBSelected = null;
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
            {
                audioSource.PlayOneShot(clip);
            }
        }
    }
}
