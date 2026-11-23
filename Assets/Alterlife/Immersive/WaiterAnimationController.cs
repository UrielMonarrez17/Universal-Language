using UnityEngine;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Drives Lucas Moreau's Animator Controller states and real-time audio RMS lip-sync
    /// from <see cref="RestaurantVoiceBridge"/> and <see cref="RestaurantEvaluation"/>.
    /// </summary>
    public sealed class WaiterAnimationController : MonoBehaviour
    {
        [Header("References")]
        public Animator animator;
        public SkinnedMeshRenderer faceRenderer;
        public RestaurantVoiceBridge voice;
        public RestaurantEvaluation evaluation;
        public AudioSource waiterAudio;

        [Header("Audio Lip-Sync")]
        public bool driveAudioLipSync = true;
        public float lipSyncGain = 14f;
        public float lipSyncSmoothSpeed = 18f;
        [Range(0f, 100f)]
        public float maxMouthOpenWeight = 95f;

        [Header("Orientation")]
        [Tooltip("Resets the animated FBX child's local Y rotation at runtime so WaiterPresentation aims Lucas's face (+Z) directly at the player.")]
        public bool alignModelToActorForward = true;

        static readonly int IsTalkingHash = Animator.StringToHash("IsTalking");
        static readonly int TalkVariantHash = Animator.StringToHash("TalkVariant");
        static readonly int IsListeningHash = Animator.StringToHash("IsListening");
        static readonly int IsThinkingHash = Animator.StringToHash("IsThinking");
        static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
        static readonly int IsCarryingTrayHash = Animator.StringToHash("IsCarryingTray");
        static readonly int WaveHash = Animator.StringToHash("Wave");
        static readonly int HandGestureHash = Animator.StringToHash("HandGesture");
        static readonly int ServePresentHash = Animator.StringToHash("ServePresent");
        static readonly int ClearTableHash = Animator.StringToHash("ClearTable");

        bool isWalking;
        bool isCarryingTray;

        readonly float[] audioSamples = new float[256];

        int mouthOpenIdx = -1;
        int mouthOIdx = -1;
        int mouthWideIdx = -1;

        bool wasWaiterSpeaking;
        int talkVariant;
        bool subscribed;

        float currentMouthOpen;
        float currentMouthO;
        float currentMouthWide;
        bool audioOverrideActive;

        void Awake()
        {
            ResolveReferences();
            if (alignModelToActorForward && animator != null && animator.transform.parent == transform)
            {
                animator.transform.localRotation = Quaternion.identity;
                animator.transform.localPosition = new Vector3(0f, animator.transform.localPosition.y, 0f);
            }
            CacheBlendShapes();
        }

        void OnEnable()
        {
            ResolveReferences();
            SubscribeEvaluation();
        }

        void Start()
        {
            ResolveReferences();
            CacheBlendShapes();
            SubscribeEvaluation();
        }

        void OnDisable()
        {
            UnsubscribeEvaluation();
        }

        void ResolveReferences()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (faceRenderer == null)
                faceRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            if (waiterAudio == null)
                waiterAudio = GetComponent<AudioSource>();
            if (voice == null)
                voice = FindAnyObjectByType<RestaurantVoiceBridge>();
            if (evaluation == null)
                evaluation = FindAnyObjectByType<RestaurantEvaluation>();
        }

        void CacheBlendShapes()
        {
            mouthOpenIdx = -1;
            mouthOIdx = -1;
            mouthWideIdx = -1;
            if (faceRenderer == null || faceRenderer.sharedMesh == null)
                return;

            Mesh mesh = faceRenderer.sharedMesh;
            mouthOpenIdx = mesh.GetBlendShapeIndex("MouthOpen");
            mouthOIdx = mesh.GetBlendShapeIndex("MouthO");
            mouthWideIdx = mesh.GetBlendShapeIndex("MouthWide");
        }

        void SubscribeEvaluation()
        {
            if (subscribed || evaluation == null)
                return;
            evaluation.WaiterGreeted += OnWaiterGreeted;
            evaluation.WaiterOrderServed += OnWaiterOrderServed;
            evaluation.WaiterGesturePrompted += OnWaiterGesturePrompted;
            subscribed = true;
        }

        void UnsubscribeEvaluation()
        {
            if (!subscribed || evaluation == null)
                return;
            evaluation.WaiterGreeted -= OnWaiterGreeted;
            evaluation.WaiterOrderServed -= OnWaiterOrderServed;
            evaluation.WaiterGesturePrompted -= OnWaiterGesturePrompted;
            subscribed = false;
        }

        void OnWaiterGreeted()
        {
            if (animator != null)
                animator.SetTrigger(WaveHash);
        }

        void OnWaiterOrderServed()
        {
            if (animator != null)
                animator.SetTrigger(ServePresentHash);
        }

        void OnWaiterGesturePrompted()
        {
            if (animator != null)
                animator.SetTrigger(HandGestureHash);
        }

        public void SetWalkingState(bool walking, bool carryingTray)
        {
            isWalking = walking;
            isCarryingTray = carryingTray;
            if (animator != null)
            {
                animator.SetBool(IsWalkingHash, walking);
                animator.SetBool(IsCarryingTrayHash, carryingTray);
            }
        }

        public void TriggerServePresent()
        {
            if (animator != null)
                animator.SetTrigger(ServePresentHash);
        }

        public void TriggerClearTable()
        {
            if (animator != null)
                animator.SetTrigger(ClearTableHash);
        }

        void Update()
        {
            if (animator == null || voice == null)
                return;

            bool isSpeaking = voice.IsWaiterSpeaking;
            if (isSpeaking && !wasWaiterSpeaking)
            {
                talkVariant = (talkVariant + 1) & 1;
                animator.SetInteger(TalkVariantHash, talkVariant);
            }
            wasWaiterSpeaking = isSpeaking;

            bool isThinking = !isSpeaking && !isWalking && voice.IsThinking;
            bool isListening = !isSpeaking && !isThinking && !isWalking && voice.IsPlayerSpeaking;

            animator.SetBool(IsWalkingHash, isWalking);
            animator.SetBool(IsCarryingTrayHash, isCarryingTray);
            animator.SetBool(IsTalkingHash, isSpeaking && !isWalking);
            animator.SetBool(IsThinkingHash, isThinking);
            animator.SetBool(IsListeningHash, isListening);
        }

        void LateUpdate()
        {
            if (!driveAudioLipSync || faceRenderer == null || waiterAudio == null)
                return;

            bool speaking = waiterAudio.isPlaying;
            if (speaking)
                audioOverrideActive = true;
            else if (!audioOverrideActive)
                return;

            float targetOpen = 0f;
            float targetO = 0f;
            float targetWide = 0f;

            if (speaking)
            {
                waiterAudio.GetOutputData(audioSamples, 0);
                float sumSq = 0f;
                for (int i = 0; i < audioSamples.Length; i++)
                {
                    float s = audioSamples[i];
                    sumSq += s * s;
                }

                float rms = Mathf.Sqrt(sumSq / audioSamples.Length);
                float level = Mathf.Clamp01(rms * lipSyncGain);

                float t = Time.time;
                float roundBias = 0.5f + 0.5f * Mathf.Sin(t * 7.3f);
                float wideBias = 0.5f + 0.5f * Mathf.Cos(t * 5.9f + 1.1f);

                targetOpen = level * maxMouthOpenWeight;
                targetO = level * 42f * roundBias;
                targetWide = level * 36f * wideBias;
            }

            float lerpT = 1f - Mathf.Exp(-lipSyncSmoothSpeed * Time.deltaTime);
            currentMouthOpen = Mathf.Lerp(currentMouthOpen, targetOpen, lerpT);
            currentMouthO = Mathf.Lerp(currentMouthO, targetO, lerpT);
            currentMouthWide = Mathf.Lerp(currentMouthWide, targetWide, lerpT);

            if (!speaking && currentMouthOpen < 0.25f && currentMouthO < 0.25f && currentMouthWide < 0.25f)
            {
                currentMouthOpen = 0f;
                currentMouthO = 0f;
                currentMouthWide = 0f;
                ApplyMouthBlendShapes(0f, 0f, 0f);
                audioOverrideActive = false;
                return;
            }

            ApplyMouthBlendShapes(currentMouthOpen, currentMouthO, currentMouthWide);
        }

        void ApplyMouthBlendShapes(float open, float o, float wide)
        {
            if (mouthOpenIdx >= 0)
                faceRenderer.SetBlendShapeWeight(mouthOpenIdx, open);
            if (mouthOIdx >= 0)
                faceRenderer.SetBlendShapeWeight(mouthOIdx, o);
            if (mouthWideIdx >= 0)
                faceRenderer.SetBlendShapeWeight(mouthWideIdx, wide);
        }
    }
}
