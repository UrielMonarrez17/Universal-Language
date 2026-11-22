using System;
using System.Collections.Generic;
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
    /// Clean, Quest-optimized Fox Companion controller for Le Café de Paris (and compatible pilot scenes):
    /// - Rests unobtrusively in the player's lower-right periphery during Observing.
    /// - Glides smoothly beside the left edge of the Level 1 Hint or Level 2 Rescue panel when open.
    /// - Provides clear visual motion & glow cues while Alibaba Cloud TTS is thinking or speaking.
    /// - Performs a smooth lightweight celebratory orbit upon milestone / exam success.
    /// - Caches visual footprint ONCE at Awake (zero per-frame Renderer.bounds scans or allocations)
    ///   and guards card controls against duplicate persistent + runtime UnityEvent invocations.
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

        [Header("Legacy Pilot Vocab Label (ApartmentPilot compatibility)")]
        [SerializeField] private GameObject vocabLabelRoot;
        [SerializeField] private TMP_Text vocabLabelText;

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

        /// <summary>Raised when the player presses Left Controller Y to cycle Fox help.</summary>
        public event Action HelpRequestedByButton;

        static readonly Color ThinkingPulseColor = new Color(0.49f, 0.83f, 0.99f, 1f);
        static readonly Color SpeakingGlowColor = new Color(0.20f, 0.83f, 0.60f, 1f);
        static readonly Color ErrorTintColor = new Color(0.97f, 0.44f, 0.44f, 1f);
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        // Cached once in Awake — zero per-frame allocations or Renderer.bounds loops
        readonly List<Renderer> foxMeshRenderers = new List<Renderer>(12);
        MaterialPropertyBlock glowPropBlock;
        Vector3 baseLocalScale = Vector3.one;
        Vector3 cachedVisualCenterLocal = Vector3.zero;
        float cachedVisualHalfWidth = 0.14f;
        float cachedVisualHalfHeight = 0.14f;
        float panelHalfWidthL1 = 0.36f;
        float panelHalfWidthL2 = 0.46f;
        bool footprintCached;

        // Runtime panel / target / animation state
        Transform level1WorldTarget;
        Vector3 panelAnchorPos;
        Quaternion panelAnchorRot = Quaternion.identity;
        bool hasPanelAnchor;
        string lastDiagnosticSuffix = "";
        string baseCaptionText = "";

        // Voice visual cues driven by RestaurantVoiceBridge (Alibaba Cloud TTS)
        bool isVoiceThinking;
        bool isVoiceSpeaking;
        bool hasVoiceError;
        float pulsePhase;

        // Celebration lightweight orbit state
        float celebrateElapsed;
        const float CelebrateDuration = 2.4f;
        Vector3 celebrateOriginPos;
        Quaternion celebrateOriginRot = Quaternion.identity;

        // Single-channel card callbacks (no duplicate persistent + runtime invocation)
        Action<int> onIndexedCardSelected;
        Action onLegacyCardASelected;
        Action onLegacyCardBSelected;
        bool cardControlAWired;
        bool cardControlBWired;
        bool cardControlCWired;
        bool wasLeftYPressed;

        void Awake()
        {
            baseLocalScale = transform.localScale;
            glowPropBlock = new MaterialPropertyBlock();

            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

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
            if (cardControlAWired && cardOptionAControl != null)
                cardOptionAControl.pressed.RemoveListener(HandleCardAPressed);
            if (cardControlBWired && cardOptionBControl != null)
                cardOptionBControl.pressed.RemoveListener(HandleCardBPressed);
            if (cardControlCWired && cardOptionCControl != null)
                cardOptionCControl.pressed.RemoveListener(HandleCardCPressed);
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
            if (headCamera != null) return;
            Camera main = Camera.main;
            if (main != null) headCamera = main.transform;
        }

        /// <summary>
        /// Computes the Fox's local-space visual bounds ONCE at startup so runtime positioning
        /// beside Level 1 / Level 2 panels is pure O(1) vector math with zero per-frame bounds queries.
        /// </summary>
        void CacheVisualFootprintOnce()
        {
            if (footprintCached) return;
            footprintCached = true;

            foxMeshRenderers.Clear();
            Bounds worldBounds = default;
            bool hasBounds = false;

            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                if (r is ParticleSystemRenderer) continue;
                if (r.GetComponent<TMP_Text>() != null) continue;
                if (hintPanelRoot != null && r.transform.IsChildOf(hintPanelRoot.transform)) continue;
                if (dialogueCardsRoot != null && r.transform.IsChildOf(dialogueCardsRoot.transform)) continue;
                if (vocabLabelRoot != null && r.transform.IsChildOf(vocabLabelRoot.transform)) continue;

                foxMeshRenderers.Add(r);
                if (!hasBounds)
                {
                    worldBounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    worldBounds.Encapsulate(r.bounds);
                }
            }

            if (hasBounds)
            {
                cachedVisualCenterLocal = transform.InverseTransformPoint(worldBounds.center);
                cachedVisualHalfWidth = Mathf.Clamp(Mathf.Max(worldBounds.extents.x, worldBounds.extents.z), 0.08f, 0.28f);
                cachedVisualHalfHeight = Mathf.Clamp(worldBounds.extents.y, 0.08f, 0.28f);
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

        /// <summary>
        /// Only attaches runtime listeners when a PhysicalControl has NO persistent scene UnityEvent
        /// (such as Paris.unity's UseAnswerA/B/C), eliminating double-firing completely.
        /// </summary>
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

        void HandleCardAPressed()
        {
            if (onIndexedCardSelected != null) onIndexedCardSelected.Invoke(0);
            else onLegacyCardASelected?.Invoke();
        }

        void HandleCardBPressed()
        {
            if (onIndexedCardSelected != null) onIndexedCardSelected.Invoke(1);
            else onLegacyCardBSelected?.Invoke();
        }

        void HandleCardCPressed()
        {
            onIndexedCardSelected?.Invoke(2);
        }

        void SnapToRestPose()
        {
            if (headCamera == null) return;
            ComputeRestPose(out Vector3 targetPos, out Quaternion targetRot);
            transform.position = targetPos;
            transform.rotation = targetRot;
        }

        void Update()
        {
            if (headCamera == null)
            {
                ResolveCamera();
                if (headCamera == null) return;
            }

            PollLeftControllerYButton();
            UpdatePoseAndMotion(Time.deltaTime);
            UpdateVoiceVisualCues(Time.deltaTime);
        }

        void PollLeftControllerYButton()
        {
            if (!enableLeftYButtonSummon) return;

            InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!leftHand.isValid)
            {
                wasLeftYPressed = false;
                return;
            }

            bool yPressed = leftHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool pressed) && pressed;
            if (yPressed && !wasLeftYPressed)
                HelpRequestedByButton?.Invoke();
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
                    if (level1WorldTarget != null && hintPanelRoot == null)
                    {
                        // ApartmentPilot world-object hint mode
                        targetPos = level1WorldTarget.position + Vector3.up * (0.32f + bobY);
                        targetRot = ComputeLookAtPlayerRotation(targetPos);
                    }
                    else
                    {
                        EnsurePanelAnchor();
                        ComputeBesidePanelPose(panelHalfWidthL1, bobY, out targetPos, out targetRot);
                        UpdateAnchoredPanelTransform(hintPanelRoot, 0f);
                    }
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

                default: // Observing
                    ComputeRestPose(out targetPos, out targetRot);
                    targetPos.y += bobY;
                    break;
            }

            float posLerp = 1f - Mathf.Exp(-Mathf.Max(1f, followDamping) * dt);
            float rotLerp = 1f - Mathf.Exp(-Mathf.Max(1f, rotationDamping) * dt);
            transform.position = Vector3.Lerp(transform.position, targetPos, posLerp);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotLerp);
        }

        void ComputeRestPose(out Vector3 pos, out Quaternion rot)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(headCamera.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
            flatForward.Normalize();
            Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward).normalized;

            pos = headCamera.position
                + flatRight * shoulderOffset.x
                + Vector3.up * shoulderOffset.y
                + flatForward * shoulderOffset.z;

            // Gentle lift when speaking in rest mode (e.g., Paris Guide or short cue)
            if (isVoiceSpeaking || isVoiceThinking)
                pos.y += 0.05f;

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

            // Sit cleanly beside the left border of the active panel without overlapping text
            float lateralClearance = panelHalfWidth + cachedVisualHalfWidth + 0.11f;
            Vector3 desiredVisualCenter = panelAnchorPos
                - panelRight * lateralClearance
                + Vector3.up * (0.04f + bobY)
                - panelForward * 0.04f;

            rot = ComputeLookAtPlayerRotation(desiredVisualCenter);
            Vector3 scaledLocalCenter = Vector3.Scale(cachedVisualCenterLocal, transform.localScale);
            pos = desiredVisualCenter - (rot * scaledLocalCenter);
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
            // Smooth, lightweight 180-degree celebratory arc across the upper-front periphery
            float ease = Mathf.SmoothStep(0f, 1f, t);
            float angle = Mathf.Lerp(-0.55f, 1.35f, ease) * Mathf.PI;
            float radius = 0.78f;
            float arcLift = Mathf.Sin(t * Mathf.PI) * 0.22f;

            Vector3 forward = celebrateOriginRot * Vector3.forward;
            Vector3 right = celebrateOriginRot * Vector3.right;

            Vector3 orbitOffset = right * (Mathf.Cos(angle) * radius * 0.65f)
                + forward * (0.85f + Mathf.Sin(angle) * 0.25f)
                + Vector3.up * (-0.10f + arcLift + bobY);

            pos = celebrateOriginPos + orbitOffset;
            rot = ComputeLookAtPlayerRotation(pos);
        }

        Quaternion ComputeLookAtPlayerRotation(Vector3 fromPos)
        {
            Vector3 toHead = headCamera.position - fromPos;
            toHead.y *= 0.35f; // gentle pitch toward player without awkward tilt
            if (toHead.sqrMagnitude < 0.0001f)
                return Quaternion.identity;
            return Quaternion.LookRotation(toHead.normalized, Vector3.up);
        }

        void UpdateVoiceVisualCues(float dt)
        {
            // Subtle breathing scale pulse while Fox voice is synthesizing or speaking
            float targetScaleFactor = 1f;
            if (isVoiceSpeaking)
                targetScaleFactor = 1f + 0.045f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f));
            else if (isVoiceThinking)
                targetScaleFactor = 1f + 0.025f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f));

            Vector3 desiredScale = baseLocalScale * targetScaleFactor;
            transform.localScale = Vector3.Lerp(transform.localScale, desiredScale, 1f - Mathf.Exp(-10f * dt));

            if (CurrentState == CompanionState.Celebrating)
            {
                ApplyAuraColor(goldCelebrationColor, 0.9f);
            }
            else if (hasVoiceError)
            {
                ApplyAuraColor(ErrorTintColor, 0.65f);
            }
            else if (isVoiceSpeaking)
            {
                float glow = 0.55f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 11f));
                ApplyAuraColor(SpeakingGlowColor, glow);
            }
            else if (isVoiceThinking)
            {
                float glow = 0.35f + 0.25f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.5f));
                ApplyAuraColor(ThinkingPulseColor, glow);
            }
            else if (IsShowingAnyHelp)
            {
                ApplyAuraColor(cyanHintColor, 0.45f);
            }
            else
            {
                ApplyAuraColor(cyanHintColor, 0.12f);
            }
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
            if (vocabLabelRoot != null) vocabLabelRoot.SetActive(false);
        }

        // =====================================================================
        // Public API (100% compatible with Paris & ApartmentPilot callers)
        // =====================================================================

        /// <summary>
        /// Drives visual pulse & animation feedback while Alibaba Cloud TTS prepares or plays Fox audio.
        /// </summary>
        public void SetVoiceActivity(bool thinking, bool speaking)
        {
            isVoiceThinking = thinking;
            isVoiceSpeaking = speaking;
            if (speaking)
                hasVoiceError = false;
        }

        /// <summary>
        /// Optionally surfaces a Fox voice diagnostic on the active Fox panel.
        /// </summary>
        public void ShowVoiceDiagnostic(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                hasVoiceError = false;
                lastDiagnosticSuffix = "";
                return;
            }

            hasVoiceError = true;
            lastDiagnosticSuffix = $"\n<size=72%><color=#FCA5A5>[Fox Voice] {message.Trim()}</color></size>";

            if (hintPanelText != null)
            {
                if (hintPanelRoot != null && !hintPanelRoot.activeSelf && CurrentState == CompanionState.Observing)
                {
                    ResolveCamera();
                    hasPanelAnchor = false;
                    EnsurePanelAnchor();
                    hintPanelRoot.SetActive(true);
                    UpdateAnchoredPanelTransform(hintPanelRoot, 0f);
                }

                string core = string.IsNullOrEmpty(baseCaptionText) ? hintPanelText.text : baseCaptionText;
                int marker = core.IndexOf("[Fox Voice]", StringComparison.Ordinal);
                if (marker >= 0) core = core.Substring(0, marker).TrimEnd();
                hintPanelText.text = (string.IsNullOrWhiteSpace(core) ? "Fox Voice Diagnostic" : core) + lastDiagnosticSuffix;
            }
        }

        /// <summary>Opens the Fox Level 1 Hint / Tutor Explanation panel beside the Fox.</summary>
        public void ShowHintPanel(string formattedText)
        {
            ResolveCamera();
            CurrentState = CompanionState.HintLevel1;
            level1WorldTarget = null;
            hasVoiceError = false;
            lastDiagnosticSuffix = "";
            baseCaptionText = formattedText ?? "";

            if (!hasPanelAnchor) EnsurePanelAnchor();

            if (dialogueCardsRoot != null) dialogueCardsRoot.SetActive(false);
            if (vocabLabelRoot != null) vocabLabelRoot.SetActive(false);

            if (hintPanelRoot != null)
            {
                hintPanelRoot.SetActive(true);
                UpdateAnchoredPanelTransform(hintPanelRoot, 0f);
            }
            if (hintPanelText != null)
                hintPanelText.text = baseCaptionText;

            if (auraParticles != null && !auraParticles.isPlaying)
                auraParticles.Play();
            if (chirpHintClip != null && audioSource != null && !audioSource.isPlaying)
                audioSource.PlayOneShot(chirpHintClip, 0.45f);
        }

        /// <summary>Legacy ApartmentPilot Level 1 hint anchored above a world prop.</summary>
        public void ShowHintLevel1(Transform worldTarget, string vocabWordFrench)
        {
            ResolveCamera();
            CurrentState = CompanionState.HintLevel1;
            level1WorldTarget = worldTarget;
            hasVoiceError = false;
            lastDiagnosticSuffix = "";

            if (dialogueCardsRoot != null) dialogueCardsRoot.SetActive(false);

            if (vocabLabelRoot != null)
            {
                vocabLabelRoot.SetActive(true);
                if (vocabLabelText != null) vocabLabelText.text = vocabWordFrench ?? "";
            }
            else if (hintPanelRoot != null)
            {
                ShowHintPanel($"<b>{vocabWordFrench}</b>");
            }
        }

        /// <summary>Opens Level 2 Rescue with up to 3 contextual French/English phrase cards.</summary>
        public void ShowRescueOptions(FoxHelpOption[] options, Action<int> onOptionSelected)
        {
            ResolveCamera();
            CurrentState = CompanionState.RescueLevel2;
            level1WorldTarget = null;
            onIndexedCardSelected = onOptionSelected;
            onLegacyCardASelected = null;
            onLegacyCardBSelected = null;
            hasVoiceError = false;
            lastDiagnosticSuffix = "";

            if (!hasPanelAnchor) EnsurePanelAnchor();

            if (vocabLabelRoot != null) vocabLabelRoot.SetActive(false);
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

        /// <summary>Legacy ApartmentPilot 2-card Level 2 rescue API.</summary>
        public void ShowRescueLevel2(string phraseA, Action onA, string phraseB, Action onB)
        {
            ResolveCamera();
            CurrentState = CompanionState.RescueLevel2;
            level1WorldTarget = null;
            onIndexedCardSelected = null;
            onLegacyCardASelected = onA;
            onLegacyCardBSelected = onB;

            if (!hasPanelAnchor) EnsurePanelAnchor();
            if (hintPanelRoot != null) hintPanelRoot.SetActive(false);
            if (vocabLabelRoot != null) vocabLabelRoot.SetActive(false);

            if (dialogueCardsRoot != null)
            {
                dialogueCardsRoot.SetActive(true);
                UpdateAnchoredPanelTransform(dialogueCardsRoot, 0f);
            }
            if (cardOptionAText != null) cardOptionAText.text = phraseA ?? "";
            if (cardOptionBText != null) cardOptionBText.text = phraseB ?? "";
            if (cardOptionCControl != null) cardOptionCControl.gameObject.SetActive(false);
        }

        /// <summary>Updates the caption banner shown above Level 2 cards (e.g. pronunciation prompt).</summary>
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
            if (hintPanelText != null)
                hintPanelText.text = baseCaptionText + lastDiagnosticSuffix;
        }

        /// <summary>Triggers a smooth lightweight celebratory orbit and gold aura feedback.</summary>
        public void Celebrate()
        {
            ResolveCamera();
            HideAllPanels();
            hasPanelAnchor = false;
            level1WorldTarget = null;
            hasVoiceError = false;
            lastDiagnosticSuffix = "";

            CurrentState = CompanionState.Celebrating;
            celebrateElapsed = 0f;

            if (headCamera != null)
            {
                celebrateOriginPos = headCamera.position;
                Vector3 flatFwd = Vector3.ProjectOnPlane(headCamera.forward, Vector3.up);
                if (flatFwd.sqrMagnitude < 0.001f) flatFwd = Vector3.forward;
                celebrateOriginRot = Quaternion.LookRotation(flatFwd.normalized, Vector3.up);
            }
            else
            {
                celebrateOriginPos = transform.position;
                celebrateOriginRot = Quaternion.identity;
            }

            if (goldCelebrationParticles != null)
                goldCelebrationParticles.Play();
            if (celebrateClip != null && audioSource != null && !audioSource.isPlaying)
                audioSource.PlayOneShot(celebrateClip, 0.55f);
        }

        /// <summary>Returns the Fox quietly to its lower-right Observing rest position and closes help panels.</summary>
        public void ResetToObserving()
        {
            CurrentState = CompanionState.Observing;
            level1WorldTarget = null;
            hasPanelAnchor = false;
            hasVoiceError = false;
            lastDiagnosticSuffix = "";
            baseCaptionText = "";
            onIndexedCardSelected = null;
            onLegacyCardASelected = null;
            onLegacyCardBSelected = null;
            HideAllPanels();

            if (auraParticles != null && auraParticles.isPlaying)
                auraParticles.Stop();
        }

        static void ApplyCardText(TMP_Text label, PhysicalControl control, FoxHelpOption[] options, int index)
        {
            bool hasOption = index < options.Length && !string.IsNullOrWhiteSpace(options[index].french);
            if (control != null)
                control.gameObject.SetActive(hasOption);

            if (label != null && hasOption)
            {
                string fr = options[index].french.Trim();
                string en = (options[index].englishDescription ?? "").Trim();
                label.text = string.IsNullOrEmpty(en)
                    ? $"<b><color=#F8FAFC>{fr}</color></b>"
                    : $"<b><color=#F8FAFC>{fr}</color></b>\n<size=68%><color=#94A3B8>{en}</color></size>";
            }
        }
    }
}
