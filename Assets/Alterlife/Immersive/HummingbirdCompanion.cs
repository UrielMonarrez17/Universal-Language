using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace Alterlife.Immersive
{
    public enum CompanionState { Observing, HintLevel1, RescueLevel2, Celebrating }

    [Serializable]
    public struct FoxHelpOption
    {
        public string french;
        public string englishDescription;
    }

    /// <summary>
    /// Quest-optimized Fox Companion controller for Le Café de Paris:
    /// - Rests unobtrusively in the player's lower-right periphery during Observing.
    /// - Glides beside the Level 1 Hint or Level 2 Rescue panel when open (Left Controller Y button).
    /// - Provides smooth glow & scale motion cues while Alibaba Cloud TTS is thinking or speaking.
    /// </summary>
    public sealed class HummingbirdCompanion : MonoBehaviour
    {
        [Header("Follow & Placement")]
        [SerializeField] private Transform headCamera;
        [SerializeField] private Vector3 shoulderOffset = new Vector3(0.48f, -0.38f, 0.72f);
        [SerializeField] private float followDamping = 5f;
        [SerializeField] private float rotationDamping = 4f;
        [SerializeField] private float bobbingSpeed = 2.5f;
        [SerializeField] private float bobbingAmplitude = 0.04f;

        [Header("Visual Feedback & Particles")]
        [SerializeField] private Color cyanHintColor = new Color(0.31f, 0.82f, 0.77f, 1f);
        [SerializeField] private Color goldCelebrationColor = new Color(0.92f, 0.70f, 0.03f, 1f);
        [SerializeField] private Light auraLight;
        [SerializeField] private ParticleSystem auraParticles;
        [SerializeField] private ParticleSystem goldCelebrationParticles;

        [Header("Paris Level 1 Hint & Fox Tutor Panel")]
        [SerializeField] private GameObject hintPanelRoot;
        [SerializeField] private TMP_Text hintPanelText;

        [Header("Level 2 Dynamic Rescue Cards")]
        [SerializeField] private GameObject dialogueCardsRoot;
        [SerializeField] private TMP_Text cardOptionAText;
        [SerializeField] private TMP_Text cardOptionBText;
        [SerializeField] private TMP_Text cardOptionCText;
        [SerializeField] private PhysicalControl cardOptionAControl;
        [SerializeField] private PhysicalControl cardOptionBControl;
        [SerializeField] private PhysicalControl cardOptionCControl;

        [Header("Input & Spatial Audio")]
        [SerializeField] private bool enableLeftYButtonSummon = true;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip chirpHintClip;
        [SerializeField] private AudioClip celebrateClip;

        public CompanionState CurrentState { get; private set; } = CompanionState.Observing;
        public bool IsShowingLevel1 => CurrentState == CompanionState.HintLevel1;
        public bool IsShowingHelp => CurrentState == CompanionState.RescueLevel2;
        public bool IsShowingAnyHelp => CurrentState == CompanionState.HintLevel1 || CurrentState == CompanionState.RescueLevel2;
        public AudioSource AudioSource => audioSource;

        public event Action HelpRequestedByButton;

        static readonly Color ThinkingPulseColor = new Color(0.49f, 0.83f, 0.99f, 1f);
        static readonly Color SpeakingGlowColor = new Color(0.20f, 0.83f, 0.60f, 1f);
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        readonly List<Renderer> foxMeshRenderers = new List<Renderer>(12);
        MaterialPropertyBlock glowPropBlock;
        Vector3 baseLocalScale = Vector3.one;
        Vector3 cachedVisualCenterLocal = Vector3.zero;
        float cachedVisualHalfWidth = 0.14f;
        float panelHalfWidthL1 = 0.36f;
        float panelHalfWidthL2 = 0.46f;
        bool footprintCached;

        Vector3 panelAnchorPos;
        Quaternion panelAnchorRot = Quaternion.identity;
        bool hasPanelAnchor;
        string baseCaptionText = "";

        bool isVoiceThinking;
        bool isVoiceSpeaking;
        float pulsePhase;

        float celebrateElapsed;
        const float CelebrateDuration = 2.4f;
        Vector3 celebrateOriginPos;
        Quaternion celebrateOriginRot = Quaternion.identity;

        Action<int> onIndexedCardSelected;
        bool cardControlAWired, cardControlBWired, cardControlCWired;
        bool wasLeftYPressed;

        void Awake()
        {
            baseLocalScale = transform.localScale;
            glowPropBlock = new MaterialPropertyBlock();
            if (audioSource == null) audioSource = GetComponent<AudioSource>();

            ConfigureSpatialAudio();
            CacheVisualFootprintOnce();
            WireCardControlsSafely();
            HideAllPanels();
        }

        void Start()
        {
            ResolveCamera();
            SnapToRestPose();
        }

        void OnDestroy()
        {
            if (cardControlAWired && cardOptionAControl != null) cardOptionAControl.pressed.RemoveListener(HandleCardAPressed);
            if (cardControlBWired && cardOptionBControl != null) cardOptionBControl.pressed.RemoveListener(HandleCardBPressed);
            if (cardControlCWired && cardOptionCControl != null) cardOptionCControl.pressed.RemoveListener(HandleCardCPressed);
        }

        void ConfigureSpatialAudio()
        {
            if (audioSource == null) return;
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.mute = false;
            audioSource.volume = 1f;
            audioSource.spatialBlend = 0.65f;
            audioSource.minDistance = 1.2f;
            audioSource.maxDistance = 12f;
        }

        void ResolveCamera()
        {
            if (headCamera == null && Camera.main != null)
                headCamera = Camera.main.transform;
        }

        void CacheVisualFootprintOnce()
        {
            if (footprintCached) return;
            footprintCached = true;

            foxMeshRenderers.Clear();
            Bounds worldBounds = default;
            bool hasBounds = false;

            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer || r.GetComponent<TMP_Text>() != null) continue;
                if (hintPanelRoot != null && r.transform.IsChildOf(hintPanelRoot.transform)) continue;
                if (dialogueCardsRoot != null && r.transform.IsChildOf(dialogueCardsRoot.transform)) continue;

                foxMeshRenderers.Add(r);
                if (!hasBounds) { worldBounds = r.bounds; hasBounds = true; }
                else worldBounds.Encapsulate(r.bounds);
            }

            if (hasBounds)
            {
                cachedVisualCenterLocal = transform.InverseTransformPoint(worldBounds.center);
                cachedVisualHalfWidth = Mathf.Clamp(Mathf.Max(worldBounds.extents.x, worldBounds.extents.z), 0.08f, 0.28f);
            }

            panelHalfWidthL1 = EstimatePanelHalfWidth(hintPanelRoot, 0.36f);
            panelHalfWidthL2 = EstimatePanelHalfWidth(dialogueCardsRoot, 0.46f);
        }

        static float EstimatePanelHalfWidth(GameObject root, float fallback)
        {
            if (root == null) return fallback;
            var mr = root.GetComponentInChildren<MeshRenderer>(true);
            if (mr == null) return fallback;
            float maxExtent = Mathf.Max(mr.bounds.extents.x, mr.bounds.extents.z);
            return maxExtent > 0.08f ? Mathf.Clamp(maxExtent, 0.22f, 0.65f) : fallback;
        }

        void WireCardControlsSafely()
        {
            if (cardOptionAControl != null && cardOptionAControl.pressed.GetPersistentEventCount() == 0)
            {
                cardOptionAControl.pressed.AddListener(HandleCardAPressed);
                cardControlAWired = true;
            }
            if (cardOptionBControl != null && cardOptionBControl.pressed.GetPersistentEventCount() == 0)
            {
                cardOptionBControl.pressed.AddListener(HandleCardBPressed);
                cardControlBWired = true;
            }
            if (cardOptionCControl != null && cardOptionCControl.pressed.GetPersistentEventCount() == 0)
            {
                cardOptionCControl.pressed.AddListener(HandleCardCPressed);
                cardControlCWired = true;
            }
        }

        void HandleCardAPressed() => onIndexedCardSelected?.Invoke(0);
        void HandleCardBPressed() => onIndexedCardSelected?.Invoke(1);
        void HandleCardCPressed() => onIndexedCardSelected?.Invoke(2);

        void SnapToRestPose()
        {
            if (headCamera == null) return;
            ComputeRestPose(out Vector3 targetPos, out Quaternion targetRot);
            transform.position = targetPos;
            transform.rotation = targetRot;
        }

        void Update()
        {
            if (headCamera == null) { ResolveCamera(); if (headCamera == null) return; }
            PollLeftControllerYButton();
            UpdatePoseAndMotion(Time.deltaTime);
            UpdateVoiceVisualCues(Time.deltaTime);
        }

        void PollLeftControllerYButton()
        {
            if (!enableLeftYButtonSummon) return;
            InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!leftHand.isValid) { wasLeftYPressed = false; return; }

            bool yPressed = leftHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool pressed) && pressed;
            if (yPressed && !wasLeftYPressed) HelpRequestedByButton?.Invoke();
            wasLeftYPressed = yPressed;
        }

        void UpdatePoseAndMotion(float dt)
        {
            float bobRate = isVoiceSpeaking ? bobbingSpeed * 1.9f : (isVoiceThinking ? bobbingSpeed * 1.4f : bobbingSpeed);
            float bobAmp = isVoiceSpeaking ? bobbingAmplitude * 1.35f : (isVoiceThinking ? bobbingAmplitude * 0.85f : bobbingAmplitude);
            pulsePhase += dt * bobRate;
            float bobY = Mathf.Sin(pulsePhase) * bobAmp;

            Vector3 targetPos;
            Quaternion targetRot;

            switch (CurrentState)
            {
                case CompanionState.HintLevel1:
                    EnsurePanelAnchor();
                    ComputeBesidePanelPose(panelHalfWidthL1, bobY, out targetPos, out targetRot);
                    UpdateAnchoredPanelTransform(hintPanelRoot, 0f);
                    break;

                case CompanionState.RescueLevel2:
                    EnsurePanelAnchor();
                    ComputeBesidePanelPose(panelHalfWidthL2, bobY, out targetPos, out targetRot);
                    UpdateAnchoredPanelTransform(dialogueCardsRoot, 0f);
                    if (hintPanelRoot != null && hintPanelRoot.activeSelf)
                        UpdateAnchoredPanelTransform(hintPanelRoot, 0.34f);
                    break;

                case CompanionState.Celebrating:
                    celebrateElapsed += dt;
                    float progress = Mathf.Clamp01(celebrateElapsed / CelebrateDuration);
                    ComputeCelebrationOrbitPose(progress, bobY, out targetPos, out targetRot);
                    if (celebrateElapsed >= CelebrateDuration)
                    {
                        CurrentState = CompanionState.Observing;
                        ApplyAuraColor(cyanHintColor, 0.25f);
                    }
                    break;

                default:
                    ComputeRestPose(out targetPos, out targetRot);
                    targetPos.y += bobY;
                    break;
            }

            transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-Mathf.Max(1f, followDamping) * dt));
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-Mathf.Max(1f, rotationDamping) * dt));
        }

        void ComputeRestPose(out Vector3 pos, out Quaternion rot)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(headCamera.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
            flatForward.Normalize();
            Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward).normalized;

            pos = headCamera.position + flatRight * shoulderOffset.x + Vector3.up * shoulderOffset.y + flatForward * shoulderOffset.z;
            if (isVoiceSpeaking || isVoiceThinking) pos.y += 0.05f;
            rot = ComputeLookAtPlayerRotation(pos);
        }

        void EnsurePanelAnchor()
        {
            if (hasPanelAnchor || headCamera == null) return;
            Vector3 flatForward = Vector3.ProjectOnPlane(headCamera.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
            flatForward.Normalize();

            panelAnchorPos = headCamera.position + flatForward * 1.18f + Vector3.up * -0.05f;
            panelAnchorRot = Quaternion.LookRotation(flatForward, Vector3.up);
            hasPanelAnchor = true;
        }

        void ComputeBesidePanelPose(float panelHalfWidth, float bobY, out Vector3 pos, out Quaternion rot)
        {
            Vector3 panelRight = panelAnchorRot * Vector3.right;
            Vector3 panelForward = panelAnchorRot * Vector3.forward;
            float lateralClearance = panelHalfWidth + cachedVisualHalfWidth + 0.11f;

            Vector3 desiredVisualCenter = panelAnchorPos - panelRight * lateralClearance + Vector3.up * (0.04f + bobY) - panelForward * 0.04f;
            rot = ComputeLookAtPlayerRotation(desiredVisualCenter);
            pos = desiredVisualCenter - (rot * Vector3.Scale(cachedVisualCenterLocal, transform.localScale));
        }

        void UpdateAnchoredPanelTransform(GameObject panel, float verticalOffset)
        {
            if (panel == null || !hasPanelAnchor) return;
            Transform tf = panel.transform;
            if (tf.IsChildOf(transform)) return;
            tf.position = panelAnchorPos + Vector3.up * verticalOffset;
            tf.rotation = panelAnchorRot;
        }

        void ComputeCelebrationOrbitPose(float t, float bobY, out Vector3 pos, out Quaternion rot)
        {
            float angle = Mathf.Lerp(-0.55f, 1.35f, Mathf.SmoothStep(0f, 1f, t)) * Mathf.PI;
            Vector3 forward = celebrateOriginRot * Vector3.forward;
            Vector3 right = celebrateOriginRot * Vector3.right;
            Vector3 offset = right * (Mathf.Cos(angle) * 0.51f) + forward * (0.85f + Mathf.Sin(angle) * 0.25f) + Vector3.up * (-0.10f + Mathf.Sin(t * Mathf.PI) * 0.22f + bobY);
            pos = celebrateOriginPos + offset;
            rot = ComputeLookAtPlayerRotation(pos);
        }

        Quaternion ComputeLookAtPlayerRotation(Vector3 fromPos)
        {
            Vector3 toHead = headCamera.position - fromPos;
            toHead.y *= 0.35f;
            return toHead.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(toHead.normalized, Vector3.up);
        }

        void UpdateVoiceVisualCues(float dt)
        {
            float targetScaleFactor = 1f;
            if (isVoiceSpeaking) targetScaleFactor = 1f + 0.045f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f));
            else if (isVoiceThinking) targetScaleFactor = 1f + 0.025f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f));

            transform.localScale = Vector3.Lerp(transform.localScale, baseLocalScale * targetScaleFactor, 1f - Mathf.Exp(-10f * dt));

            if (CurrentState == CompanionState.Celebrating) ApplyAuraColor(goldCelebrationColor, 0.9f);
            else if (isVoiceSpeaking) ApplyAuraColor(SpeakingGlowColor, 0.55f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 11f)));
            else if (isVoiceThinking) ApplyAuraColor(ThinkingPulseColor, 0.35f + 0.25f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.5f)));
            else ApplyAuraColor(cyanHintColor, IsShowingAnyHelp ? 0.45f : 0.12f);
        }

        void ApplyAuraColor(Color color, float intensity)
        {
            if (auraLight != null)
            {
                auraLight.color = color;
                auraLight.intensity = Mathf.Clamp(intensity * 1.4f, 0.1f, 2.0f);
            }
            if (glowPropBlock == null || foxMeshRenderers.Count == 0) return;

            Color emissive = color * Mathf.Clamp01(intensity) * 0.28f;
            for (int i = 0; i < foxMeshRenderers.Count; i++)
            {
                Renderer r = foxMeshRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(glowPropBlock);
                glowPropBlock.SetColor(EmissionColorId, emissive);
                r.SetPropertyBlock(glowPropBlock);
            }
        }

        void HideAllPanels()
        {
            if (hintPanelRoot != null) hintPanelRoot.SetActive(false);
            if (dialogueCardsRoot != null) dialogueCardsRoot.SetActive(false);
        }

        public void SetVoiceActivity(bool thinking, bool speaking)
        {
            isVoiceThinking = thinking;
            isVoiceSpeaking = speaking;
        }

        public void ShowHintPanel(string formattedText)
        {
            ResolveCamera();
            CurrentState = CompanionState.HintLevel1;
            baseCaptionText = formattedText ?? "";
            if (!hasPanelAnchor) EnsurePanelAnchor();

            if (dialogueCardsRoot != null) dialogueCardsRoot.SetActive(false);
            if (hintPanelRoot != null)
            {
                hintPanelRoot.SetActive(true);
                UpdateAnchoredPanelTransform(hintPanelRoot, 0f);
            }
            if (hintPanelText != null) hintPanelText.text = baseCaptionText;

            if (auraParticles != null && !auraParticles.isPlaying) auraParticles.Play();
            if (chirpHintClip != null && audioSource != null && !audioSource.isPlaying)
                audioSource.PlayOneShot(chirpHintClip, 0.45f);
        }

        public void ShowRescueOptions(FoxHelpOption[] options, Action<int> onOptionSelected)
        {
            ResolveCamera();
            CurrentState = CompanionState.RescueLevel2;
            onIndexedCardSelected = onOptionSelected;
            if (!hasPanelAnchor) EnsurePanelAnchor();

            if (dialogueCardsRoot != null)
            {
                dialogueCardsRoot.SetActive(true);
                UpdateAnchoredPanelTransform(dialogueCardsRoot, 0f);
            }

            if (options != null)
            {
                ApplyCardText(cardOptionAText, cardOptionAControl, options, 0);
                ApplyCardText(cardOptionBText, cardOptionBControl, options, 1);
                ApplyCardText(cardOptionCText, cardOptionCControl, options, 2);
            }
        }

        public void SetHelpCaption(string caption)
        {
            baseCaptionText = caption ?? "";
            if (hintPanelRoot != null)
            {
                hintPanelRoot.SetActive(!string.IsNullOrWhiteSpace(baseCaptionText));
                if (hintPanelRoot.activeSelf)
                {
                    EnsurePanelAnchor();
                    UpdateAnchoredPanelTransform(hintPanelRoot, CurrentState == CompanionState.RescueLevel2 ? 0.34f : 0f);
                }
            }
            if (hintPanelText != null) hintPanelText.text = baseCaptionText;
        }

        public void Celebrate()
        {
            ResolveCamera();
            HideAllPanels();
            hasPanelAnchor = false;
            CurrentState = CompanionState.Celebrating;
            celebrateElapsed = 0f;

            if (headCamera != null)
            {
                celebrateOriginPos = headCamera.position;
                Vector3 flatFwd = Vector3.ProjectOnPlane(headCamera.forward, Vector3.up);
                celebrateOriginRot = Quaternion.LookRotation(flatFwd.sqrMagnitude < 0.001f ? Vector3.forward : flatFwd.normalized, Vector3.up);
            }
            else
            {
                celebrateOriginPos = transform.position;
                celebrateOriginRot = Quaternion.identity;
            }

            if (goldCelebrationParticles != null) goldCelebrationParticles.Play();
            if (celebrateClip != null && audioSource != null && !audioSource.isPlaying)
                audioSource.PlayOneShot(celebrateClip, 0.55f);
        }

        public void ResetToObserving()
        {
            CurrentState = CompanionState.Observing;
            hasPanelAnchor = false;
            baseCaptionText = "";
            onIndexedCardSelected = null;
            HideAllPanels();
            if (auraParticles != null && auraParticles.isPlaying) auraParticles.Stop();
        }

        static void ApplyCardText(TMP_Text label, PhysicalControl control, FoxHelpOption[] options, int index)
        {
            bool hasOption = index < options.Length && !string.IsNullOrWhiteSpace(options[index].french);
            if (control != null) control.gameObject.SetActive(hasOption);

            if (label != null && hasOption)
            {
                string fr = options[index].french.Trim();
                string en = (options[index].englishDescription ?? "").Trim();
                label.text = string.IsNullOrEmpty(en)
                    ? $"<b><color=#F8FAFC>{fr}</color></b>"
                    : $"<b><color=#F8FAFC>{fr}</color></b>\n<size=72%><color=#E2E8F0>{en}</color></size>";
            }
        }
    }
}
