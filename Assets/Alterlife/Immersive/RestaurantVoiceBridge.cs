using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Hybrid AI Voice & Audio Bridge for Le Café de Paris:
    /// - Waiter (Lucas Moreau): Gemini Multimodal Audio-In + Gemini TTS ('Puck') backed by a 0-latency
    ///   RAM + Persistent Disk TTS Cache (Application.persistentDataPath/TtsCache).
    /// - Fox Companion Tutor: Alibaba Cloud Model Studio (DashScope Singapore / ap-southeast-1) TTS
    ///   using 'qwen3-tts-flash' (voice 'Cherry') with automatic quota/error failover across the
    ///   Qwen TTS family and Sambert family, backed by RAM + Persistent Disk WAV Caching.
    ///   If all Alibaba models exhaust quota or fail, the Fox displays its visual text panels silently.
    /// - Local C# Acoustic Human-Speech Filter: allows short single-word replies ('Oui', 'Non', 'Café')
    ///   while rejecting clicks, bumps, breaths, and ambient noise before calling Gemini.
    /// - Right Controller B Button: instant Mic Mute/Unmute toggle (cancelling any mid-sentence capture)
    ///   with a compact bottom-right HUD indicator in the player's field of view.
    /// </summary>
    public sealed class RestaurantVoiceBridge : MonoBehaviour
    {
        [Header("Google AI Studio (Gemini)")]
        [Tooltip("Optional Inspector override; if blank, loads from Resources/GeminiLocalKey (git-ignored).")]
        [SerializeField] private string geminiApiKeyOverride;
        [SerializeField] private string geminiChatModel = "gemini-3.5-flash-lite";
        [SerializeField] private string geminiTtsModel = "gemini-3.8-flash-lite-tts";

        [Header("Alibaba Cloud Model Studio (Fox Companion TTS — Singapore / ap-southeast-1)")]
        [Tooltip("Optional Inspector override; if blank, loads from Resources/AlibabaLocalKey (git-ignored).")]
        [SerializeField] private string alibabaApiKeyOverride;
        [Tooltip("Optional Model Studio sub-workspace ID. Leave blank to use dashscope-intl.aliyuncs.com.")]
        [SerializeField] private string alibabaWorkspaceId;
        [Tooltip("Primary Alibaba TTS model; automatically fails over through Qwen TTS and Sambert backups on quota exhaustion.")]
        [SerializeField] private string alibabaPrimaryModel = AlibabaTtsClient.DefaultPrimaryModel;
        [Tooltip("Alibaba Qwen TTS voice persona for the Fox companion (e.g. Cherry, Serena, Ethan, Chelsie).")]
        [SerializeField] private string alibabaFoxVoice = AlibabaTtsClient.DefaultVoice;

        static readonly string[] ChatFallbackModels = { "gemini-3.6-flash", "gemini-3.8-flash" };
        static readonly string[] TtsFallbackModels = { "gemini-3.8-flash-tts", "gemini-2.5-flash-preview-tts", "gemini-3.1-flash-tts-preview", "gemini-2.5-pro-preview-tts" };

        [Header("Continuous Voice Activity Detection (VAD) & Anti-Noise Gate")]
        public bool autoListenVad = true;
        [SerializeField] private float vadStartRms = 0.015f;
        [SerializeField] private float vadStopRms = 0.009f;
        [Tooltip("Short enough (~0.12s) so single-syllable French words ('Oui', 'Non', 'Eau', 'Thé') always trigger.")]
        [SerializeField] private float minSpeechDuration = 0.12f;
        [SerializeField] private float endSilenceDuration = 1.25f;
        [SerializeField] private float maxUtteranceSeconds = 14.0f;

        [Header("Right Controller B Button Mute & HUD")]
        [SerializeField] private bool enableRightBButtonMute = true;
        public TMP_Text hudMicStatusLabel;
        public Renderer hudMicStatusBacking;

        [Header("Scene References")]
        public RestaurantEvaluation evaluation;
        public CommunicativeEncounter encounter;
        public NPCPersonality npcPersonality;
        public AudioSource speaker;
        public AudioSource foxSpeaker;
        public TMP_Text microphoneLabel;

        [Header("Paris City Exploration Proximity")]
        [Tooltip("Center of the café terrace. Walking further than cafeLeaveRadius pauses Lucas and switches voice to Fox Paris Guide mode.")]
        public Transform cafeAnchor;
        [SerializeField] private float cafeLeaveRadius = 6.2f;
        [SerializeField] private float cafeReturnRadius = 5.5f;

        // Legacy serialized field kept for scene YAML compatibility
        [HideInInspector] public string serviceUrl;

        /// <summary>Disables live Gemini HTTP calls during automated Editor tests.</summary>
        public static bool DisableGeminiForTests;

        /// <summary>Disables live Alibaba Cloud TTS HTTP/WebSocket calls during automated Editor tests.</summary>
        public static bool DisableAlibabaForTests;

        /// <summary>Optional deterministic handler used exclusively by automated Play Mode tests.</summary>
        public static Func<string, GeminiWaiterTurn> TestTurnHandler;

        public bool HasGeminiKey => !DisableGeminiForTests && !string.IsNullOrWhiteSpace(ResolveGeminiKey());
        public bool HasAlibabaKey => !DisableAlibabaForTests && !string.IsNullOrWhiteSpace(ResolveAlibabaKey());
        public bool IsConfigured => HasGeminiKey;
        public bool IsAiReady => HasGeminiKey || TestTurnHandler != null;
        public bool IsRecording { get; private set; }
        public bool IsBusy { get; private set; }
        public bool IsMicrophoneMuted { get; private set; }
        public bool IsExploringCity { get; private set; }
        public int FilteredNoiseCount { get; private set; }
        public int DiskCacheHitCount { get; private set; }
        public int DiskCacheEntryCount => diskCacheIndex.Count;
        public string ActiveAlibabaFoxModel => alibabaTts != null ? alibabaTts.ActiveModelId : "";
        public bool IsFoxSynthesizing => isSynthesizingFoxSpeech;
        public bool IsFoxSpeaking => foxSpeaker != null && foxSpeaker.isPlaying;
        public string LastFoxVoiceError => alibabaTts != null ? alibabaTts.LastError : "";
        public bool IsAnyActorSpeaking =>
            isSynthesizingSpeech ||
            isSynthesizingFoxSpeech ||
            (speaker != null && speaker.isPlaying) ||
            (foxSpeaker != null && foxSpeaker.isPlaying);
        public bool IsWaiterSpeaking => speaker != null && speaker.isPlaying;
        public bool IsPlayerSpeaking => isCapturingUtterance && !IsMicrophoneMuted;
        public bool IsThinking => IsBusy || isSynthesizingSpeech || isSynthesizingFoxSpeech;
        public string LastTranscript { get; private set; }

        const int SampleRate = 16000;
        const int RingBufferSeconds = 20;
        const int MaxCachedTtsClips = 12;
        const int MaxHistoryEntries = 6;
        const string DiskCacheFolderName = "TtsCache";

        static readonly Color HudMicOnColor = new Color(0.06f, 0.14f, 0.20f, 0.82f);
        static readonly Color HudMicMutedColor = new Color(0.32f, 0.08f, 0.10f, 0.88f);

        AudioClip micRingClip;
        string device;
        int lastReadSamplePos;
        bool isCapturingUtterance;
        int utteranceStartSample;
        float speechStartAccum;
        float silenceEndAccum;
        float utteranceLengthSeconds;
        float ambientNoiseFloor = 0.004f;
        float nextMicAttempt;
        bool micPermissionDenied;
        bool micBootstrapped;
        bool wasAnyActorSpeaking;
        bool isSynthesizingSpeech;
        bool isSynthesizingFoxSpeech;
        bool wasRightBPressed;
        string cachedResourceKey;
        string cachedAlibabaResourceKey;
        string diskCacheDir;
        bool diskCacheInitialized;

        Coroutine speechRoutine;
        Coroutine vadBootstrapRoutine;
        Coroutine requestRoutine;
        UnityWebRequest speechRequest;
        UnityWebRequest operationRequest;
        AlibabaTtsClient alibabaTts;
        MaterialPropertyBlock hudPropBlock;

        readonly List<HistoryEntry> history = new List<HistoryEntry>();
        readonly float[] sampleScratch = new float[SampleRate * 2];
        readonly Dictionary<string, AudioClip> ttsCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        readonly Queue<string> ttsCacheOrder = new Queue<string>();
        readonly HashSet<string> diskCacheIndex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        struct HistoryEntry
        {
            public string speaker;
            public string content;
        }

        // Gemini REST JSON DTOs
        [Serializable] class GeminiResponse { public GeminiCandidate[] candidates; }
        [Serializable] class GeminiCandidate { public GeminiContent content; }
        [Serializable] class GeminiContent { public GeminiPart[] parts; }
        [Serializable] class GeminiPart { public string text; public GeminiInlineData inlineData; }
        [Serializable] class GeminiInlineData { public string mimeType; public string data; }

        [Serializable]
        public sealed class GeminiWaiterTurn
        {
            public string speakerMode = "waiter"; // "waiter" | "fox_tutor"
            public string userTranscript;
            public string npcReplyFrench;
            public string npcReplyEnglish;
            public string foxTutorReplyEnglish;
            public string feedbackEnglish;
            public string[] newlyCompletedMilestones;
            public string detectedFood;
            public string detectedDrink;
            public string detectedDessert;
            public bool issuedConfirmationChallenge;
            public bool resolvedConfirmationChallenge;
            public bool servedOrder;
            public int matchedFoxOptionIndex = -1;
            public string[] level1Keywords;
            public string level1TipEnglish;
            public FoxHelpOption[] nextFoxOptions;
            public string finalSummaryEnglish;
        }

        static readonly string TurnResponseSchemaJson =
            "{" +
            "\"type\":\"OBJECT\"," +
            "\"properties\":{" +
                "\"speakerMode\":{\"type\":\"STRING\",\"enum\":[\"waiter\",\"fox_tutor\"]}," +
                "\"userTranscript\":{\"type\":\"STRING\"}," +
                "\"npcReplyFrench\":{\"type\":\"STRING\"}," +
                "\"npcReplyEnglish\":{\"type\":\"STRING\"}," +
                "\"foxTutorReplyEnglish\":{\"type\":\"STRING\"}," +
                "\"feedbackEnglish\":{\"type\":\"STRING\"}," +
                "\"newlyCompletedMilestones\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"STRING\",\"enum\":[\"greet-waiter\",\"read-menu\",\"ask-about-menu\",\"place-order\",\"order-drink\",\"confirm-order\",\"table-chat\",\"ask-bill-farewell\"]}}," +
                "\"detectedFood\":{\"type\":\"STRING\"}," +
                "\"detectedDrink\":{\"type\":\"STRING\"}," +
                "\"detectedDessert\":{\"type\":\"STRING\"}," +
                "\"issuedConfirmationChallenge\":{\"type\":\"BOOLEAN\"}," +
                "\"resolvedConfirmationChallenge\":{\"type\":\"BOOLEAN\"}," +
                "\"servedOrder\":{\"type\":\"BOOLEAN\"}," +
                "\"matchedFoxOptionIndex\":{\"type\":\"INTEGER\"}," +
                "\"level1Keywords\":{\"type\":\"ARRAY\",\"items\":{\"type\":\"STRING\"}}," +
                "\"level1TipEnglish\":{\"type\":\"STRING\"}," +
                "\"nextFoxOptions\":{" +
                    "\"type\":\"ARRAY\"," +
                    "\"items\":{" +
                        "\"type\":\"OBJECT\"," +
                        "\"properties\":{" +
                            "\"french\":{\"type\":\"STRING\"}," +
                            "\"englishDescription\":{\"type\":\"STRING\"}" +
                        "}," +
                        "\"required\":[\"french\",\"englishDescription\"]" +
                    "}" +
                "}," +
                "\"finalSummaryEnglish\":{\"type\":\"STRING\"}" +
            "}," +
            "\"required\":[" +
                "\"speakerMode\",\"userTranscript\",\"npcReplyFrench\",\"npcReplyEnglish\"," +
                "\"foxTutorReplyEnglish\",\"feedbackEnglish\",\"newlyCompletedMilestones\"," +
                "\"detectedFood\",\"detectedDrink\",\"detectedDessert\"," +
                "\"issuedConfirmationChallenge\",\"resolvedConfirmationChallenge\",\"servedOrder\"," +
                "\"matchedFoxOptionIndex\",\"level1Keywords\",\"level1TipEnglish\"," +
                "\"nextFoxOptions\",\"finalSummaryEnglish\"" +
            "]" +
            "}";

        void Awake()
        {
            InitializeDiskCacheIndex();
            RefreshHudMicVisual();
            CleanupLegacyKokoroFilesAsync();
        }

        void Start()
        {
            if (npcPersonality == null)
                npcPersonality = FindAnyObjectByType<NPCPersonality>();
            ResolveFoxSpeaker();
            ResolveCafeAnchor();
            InitializeDiskCacheIndex();
            RefreshHudMicVisual();
        }

        void ResolveCafeAnchor()
        {
            if (cafeAnchor != null) return;
            var table = GameObject.Find("Your table");
            if (table != null)
            {
                cafeAnchor = table.transform;
                return;
            }
            if (evaluation != null && evaluation.menu != null)
            {
                cafeAnchor = evaluation.menu;
                return;
            }
            if (speaker != null)
                cafeAnchor = speaker.transform;
        }

        void CleanupLegacyKokoroFilesAsync()
        {
            string baseDir = Application.persistentDataPath;
            if (string.IsNullOrEmpty(baseDir))
                return;

            Task.Run(() =>
            {
                string[] legacyFolders = { "kokoro-fox-v1_0", "KokoroTmp" };
                for (int i = 0; i < legacyFolders.Length; i++)
                {
                    try
                    {
                        string path = Path.Combine(baseDir, legacyFolders[i]);
                        if (Directory.Exists(path))
                            Directory.Delete(path, recursive: true);
                    }
                    catch { }
                }
            });
        }

        /// <summary>
        /// Indexes existing cached .wav files into an in-memory HashSet once at startup so cache checks take &lt; 0.02 ms.
        /// </summary>
        public void InitializeDiskCacheIndex()
        {
            if (diskCacheInitialized && !string.IsNullOrEmpty(diskCacheDir))
                return;

            try
            {
                diskCacheDir = Path.Combine(Application.persistentDataPath, DiskCacheFolderName);
                Directory.CreateDirectory(diskCacheDir);
                diskCacheIndex.Clear();
                foreach (string file in Directory.GetFiles(diskCacheDir, "*.wav"))
                {
                    string key = Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrWhiteSpace(key))
                        diskCacheIndex.Add(key);
                }
                diskCacheInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ParisVoice] Could not initialize disk TTS cache directory: " + ex.Message);
            }
        }

        /// <summary>
        /// Computes a deterministic 24-hex SHA-256 cache key for (voiceTag + normalized phrase) in &lt; 0.02 ms.
        /// </summary>
        public static string ComputeTtsCacheHash(string voiceTag, string text)
        {
            string normalized = Regex.Replace((text ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
            string raw = $"{(voiceTag ?? "default").Trim().ToLowerInvariant()}|{normalized}";
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var sb = new StringBuilder(24);
                for (int i = 0; i < 12; i++)
                    sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Stores raw float PCM samples into both the in-memory RAM cache and persistent disk cache (.wav).
        /// </summary>
        public AudioClip CachePcmClipToMemoryAndDisk(string voiceTag, string text, float[] samples, int sampleRate, string clipName = "CachedVoice")
        {
            if (samples == null || samples.Length == 0 || sampleRate <= 0)
                return null;

            InitializeDiskCacheIndex();
            string hash = ComputeTtsCacheHash(voiceTag, text);

            var clip = AudioClip.Create(clipName, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            CacheTtsClipInRam(hash, clip);

            byte[] wavBytes = EncodeWav(samples, 1, sampleRate);
            SaveWavToDiskCache(hash, wavBytes, synchronous: true);
            return clip;
        }

        void SaveWavToDiskCache(string hash, byte[] wavBytes, bool synchronous = false)
        {
            if (string.IsNullOrWhiteSpace(hash) || wavBytes == null || wavBytes.Length < 44)
                return;

            InitializeDiskCacheIndex();
            if (string.IsNullOrEmpty(diskCacheDir))
                return;

            string path = Path.Combine(diskCacheDir, hash + ".wav");
            diskCacheIndex.Add(hash);

            if (synchronous)
            {
                try { File.WriteAllBytes(path, wavBytes); }
                catch (Exception ex) { Debug.LogWarning("[ParisVoice] Disk cache write failed: " + ex.Message); }
                return;
            }

            string targetPath = path;
            byte[] dataCopy = wavBytes;
            Task.Run(() =>
            {
                try
                {
                    string tmp = targetPath + ".tmp";
                    File.WriteAllBytes(tmp, dataCopy);
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                    File.Move(tmp, targetPath);
                }
                catch { }
            });
        }

        public bool TryLoadCachedClip(string voiceTag, string text, out AudioClip clip, bool allowRam = true)
        {
            clip = null;
            InitializeDiskCacheIndex();
            string hash = ComputeTtsCacheHash(voiceTag, text);

            if (allowRam && ttsCache.TryGetValue(hash, out AudioClip memClip) && memClip != null)
            {
                clip = memClip;
                return true;
            }

            if (!diskCacheIndex.Contains(hash) || string.IsNullOrEmpty(diskCacheDir))
                return false;

            string path = Path.Combine(diskCacheDir, hash + ".wav");
            if (!File.Exists(path))
            {
                diskCacheIndex.Remove(hash);
                return false;
            }

            try
            {
                byte[] wavBytes = File.ReadAllBytes(path);
                clip = DecodeGeminiAudio(wavBytes, "audio/wav", $"DiskCache_{hash}");
                if (clip != null)
                {
                    DiskCacheHitCount++;
                    CacheTtsClipInRam(hash, clip);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ParisVoice] Failed reading cached WAV: " + ex.Message);
            }

            return false;
        }

        string ResolveGeminiKey()
        {
            if (!string.IsNullOrWhiteSpace(geminiApiKeyOverride))
                return geminiApiKeyOverride.Trim();

            if (cachedResourceKey == null)
            {
                var keyAsset = Resources.Load<TextAsset>("GeminiLocalKey");
                cachedResourceKey = keyAsset != null ? keyAsset.text.Trim() : "";
            }
            return cachedResourceKey;
        }

        string ResolveAlibabaKey()
        {
            string overrideKey = AlibabaTtsClient.NormalizeApiKey(alibabaApiKeyOverride);
            if (!string.IsNullOrEmpty(overrideKey))
                return overrideKey;

            if (cachedAlibabaResourceKey == null)
            {
                var keyAsset = Resources.Load<TextAsset>("AlibabaLocalKey");
                cachedAlibabaResourceKey = keyAsset != null ? AlibabaTtsClient.NormalizeApiKey(keyAsset.text) : "";
            }
            return cachedAlibabaResourceKey;
        }

        void ResolveFoxSpeaker()
        {
            if (foxSpeaker == null && evaluation != null && evaluation.companion != null)
                foxSpeaker = evaluation.companion.AudioSource;

            if (foxSpeaker != null)
            {
                foxSpeaker.mute = false;
                foxSpeaker.volume = 1f;
                foxSpeaker.spatialBlend = 0.65f;
                foxSpeaker.minDistance = 1.2f;
                foxSpeaker.maxDistance = 12f;
            }
        }

        public void SeedWaiterOpening(string openingFrench)
        {
            if (!string.IsNullOrWhiteSpace(openingFrench))
                RecordHistory("Lucas (waiter)", openingFrench);
        }

        // =====================================================================
        // Right Controller B Button Mute / Unmute + Bottom-Right HUD
        // =====================================================================

        void PollRightControllerBButton()
        {
            if (!enableRightBButtonMute)
                return;

            InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!rightDevice.isValid)
            {
                wasRightBPressed = false;
                return;
            }

            bool bPressed = rightDevice.TryGetFeatureValue(CommonUsages.secondaryButton, out bool pressed) && pressed;
            if (bPressed && !wasRightBPressed)
            {
                ToggleMicrophoneMute();
            }
            wasRightBPressed = bPressed;
        }

        public void ToggleMicrophoneMute()
        {
            SetMicrophoneMuted(!IsMicrophoneMuted);
        }

        public void SetMicrophoneMuted(bool muted)
        {
            IsMicrophoneMuted = muted;

            if (IsMicrophoneMuted)
            {
                // Immediately cancel & discard any in-progress speech capture without sending to Gemini
                if (isCapturingUtterance)
                    ResetVadCapture();

                SetLabel("🔇 MIC OFF (PRESS B)");
                evaluation?.NotifyMicrophoneMuted(true);
            }
            else
            {
                ResetVadCapture();
                SetLabel(HasGeminiKey ? "● LISTENING (FR)" : "NO GEMINI API KEY");
                evaluation?.NotifyMicrophoneMuted(false);
            }

            RefreshHudMicVisual();
        }

        void RefreshHudMicVisual()
        {
            if (hudMicStatusLabel != null)
            {
                if (IsMicrophoneMuted)
                    hudMicStatusLabel.text = "<color=#FCA5A5><b>MIC OFF</b></color> <size=78%><color=#E2E8F0>· Press B</color></size>";
                else if (IsExploringCity)
                    hudMicStatusLabel.text = "<color=#7DD3FC><b>● FOX GUIDE</b></color> <size=78%><color=#CBD5E1>· Press B</color></size>";
                else
                    hudMicStatusLabel.text = "<color=#6EE7B7><b>● MIC ON</b></color> <size=78%><color=#CBD5E1>· Press B</color></size>";
            }

            if (hudMicStatusBacking != null)
            {
                if (hudPropBlock == null)
                    hudPropBlock = new MaterialPropertyBlock();
                Color c = IsMicrophoneMuted ? HudMicMutedColor : HudMicOnColor;
                hudMicStatusBacking.GetPropertyBlock(hudPropBlock);
                hudPropBlock.SetColor("_BaseColor", c);
                hudPropBlock.SetColor("_Color", c);
                hudMicStatusBacking.SetPropertyBlock(hudPropBlock);
            }
        }

        void UpdateCafeProximity()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            if (cafeAnchor == null) ResolveCafeAnchor();

            Vector3 anchorPos = cafeAnchor != null ? cafeAnchor.position : new Vector3(0f, 0f, 0.1f);
            Vector2 playerXZ = new Vector2(cam.transform.position.x, cam.transform.position.z);
            Vector2 anchorXZ = new Vector2(anchorPos.x, anchorPos.z);
            float dist = Vector2.Distance(playerXZ, anchorXZ);

            bool nextExploring = IsExploringCity
                ? dist > cafeReturnRadius
                : dist > cafeLeaveRadius;

            if (nextExploring == IsExploringCity)
                return;

            IsExploringCity = nextExploring;
            if (IsExploringCity && speaker != null && speaker.isPlaying)
                speaker.Stop();

            RefreshHudMicVisual();
            evaluation?.NotifyExploringCity(IsExploringCity);
        }

        // =====================================================================
        // Local Acoustic Anti-Noise Human Voice Gate (Allows Single Words!)
        // =====================================================================

        /// <summary>
        /// Evaluates a captured 16 kHz mono PCM buffer in &lt; 0.5 ms of pure C#.
        /// Allows short 1-syllable French replies ("Oui", "Non", "Eau", "Thé", "Plate" &gt;= ~0.12s voiced)
        /// while filtering out clicks, mic taps, coughs, breaths, and steady background noise.
        /// </summary>
        public static bool PassesHumanSpeechFilter(float[] pcm, int sampleRate, float ambientFloor = 0.004f)
        {
            if (pcm == null || sampleRate <= 0)
                return false;

            int frameLen = Mathf.Max(64, sampleRate / 50); // 20 ms frames (320 samples @ 16 kHz)
            int totalFrames = pcm.Length / frameLen;
            if (totalFrames < 5)
                return false;

            float clampedFloor = Mathf.Clamp(ambientFloor, 0.002f, 0.015f);
            float voicedRmsThreshold = Mathf.Max(0.0105f, clampedFloor * 1.85f);

            int voicedFrames = 0;
            int longestContiguousVoiced = 0;
            int currentContiguous = 0;
            float totalVoicedEnergy = 0f;
            float maxFrameEnergy = 0f;
            float maxSampleAbs = 0f;
            float zcrSum = 0f;

            for (int f = 0; f < totalFrames; f++)
            {
                int offset = f * frameLen;
                float sumSq = 0f;
                int zeroCrossings = 0;
                float prev = pcm[offset];

                for (int i = 0; i < frameLen; i++)
                {
                    float s = pcm[offset + i];
                    float abs = s >= 0f ? s : -s;
                    if (abs > maxSampleAbs) maxSampleAbs = abs;
                    sumSq += s * s;

                    if ((s >= 0.004f && prev < -0.004f) || (s < -0.004f && prev >= 0.004f))
                        zeroCrossings++;
                    prev = s;
                }

                float frameRms = Mathf.Sqrt(sumSq / frameLen);
                if (frameRms >= voicedRmsThreshold)
                {
                    voicedFrames++;
                    currentContiguous++;
                    if (currentContiguous > longestContiguousVoiced)
                        longestContiguousVoiced = currentContiguous;

                    totalVoicedEnergy += sumSq;
                    if (sumSq > maxFrameEnergy)
                        maxFrameEnergy = sumSq;

                    zcrSum += (float)zeroCrossings / frameLen;
                }
                else
                {
                    currentContiguous = 0;
                }
            }

            // 1. Minimum peak & active duration check:
            // At least 5 voiced frames (100 ms) and at least 4 contiguous voiced frames (80 ms)
            // so single-syllable words ("Oui", "Non", "Thé") pass, while <80 ms clicks/ticks fail.
            if (maxSampleAbs < 0.022f || voicedFrames < 5 || longestContiguousVoiced < 4 || totalVoicedEnergy <= 0f)
                return false;

            // 2. Impulsive transient rejection (mic bump / table knock / click):
            // If > 62% of the entire clip's voiced energy is concentrated in a single 20 ms frame, it is an impulse.
            float singleFrameEnergyRatio = maxFrameEnergy / totalVoicedEnergy;
            if (singleFrameEnergyRatio > 0.62f)
                return false;

            // 3. Human vocal Zero-Crossing Rate (ZCR) window:
            // Human voiced speech + fricatives @ 16 kHz averages between 0.015 (~120 Hz) and 0.42 (~3360 Hz).
            // Pure wind/handling rumble (< 0.014) or pure high-frequency hiss (> 0.43) is rejected.
            float meanVoicedZcr = zcrSum / voicedFrames;
            if (meanVoicedZcr < 0.014f || meanVoicedZcr > 0.43f)
                return false;

            return true;
        }

        // =====================================================================
        // Continuous Microphone Ring & VAD Loop
        // =====================================================================

        IEnumerator EnsureMicrophoneRingRunning()
        {
            if (micBootstrapped || micPermissionDenied || Application.isBatchMode)
                yield break;

            if (!HasGeminiKey)
            {
                SetLabel("NO GEMINI API KEY");
                yield break;
            }

            micBootstrapped = true;
            SetLabel("STARTING MICROPHONE");

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                bool answered = false;
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ => answered = true;
                callbacks.PermissionDenied += _ => answered = true;
                SetLabel("ALLOW MICROPHONE IN QUEST");
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone) && !answered)
                    yield return null;

                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                {
                    micPermissionDenied = true;
                    micBootstrapped = false;
                    SetLabel("MIC DENIED");
                    evaluation?.Feedback("Microphone permission is required for live French conversation.");
                    yield break;
                }
            }
#else
            yield return null;
#endif

            if (Microphone.devices.Length == 0)
            {
                micBootstrapped = false;
                nextMicAttempt = Time.realtimeSinceStartup + 3f;
                SetLabel("NO MICROPHONE");
                yield break;
            }

            device = Microphone.devices[0];
            micRingClip = Microphone.Start(device, true, RingBufferSeconds, SampleRate);
            if (micRingClip == null)
            {
                micBootstrapped = false;
                nextMicAttempt = Time.realtimeSinceStartup + 3f;
                SetLabel("MIC UNAVAILABLE");
                yield break;
            }

            Debug.Log("[ParisVoice] Continuous Gemini VAD started at " + micRingClip.frequency + " Hz.");
            lastReadSamplePos = 0;
            IsRecording = true;
            SetLabel(IsMicrophoneMuted ? "🔇 MIC OFF (PRESS B)" : "● LISTENING (FR)");
            RefreshHudMicVisual();
        }

        void Update()
        {
            PollRightControllerBButton();

            if (evaluation != null && evaluation.companion != null)
                evaluation.companion.SetVoiceActivity(isSynthesizingFoxSpeech, IsFoxSpeaking);

            if (Application.isBatchMode || evaluation == null)
                return;

            UpdateCafeProximity();

            if (!HasGeminiKey)
            {
                SetLabel(IsMicrophoneMuted ? "🔇 MIC OFF (PRESS B)" : "NO GEMINI API KEY");
                return;
            }

            if (!autoListenVad)
                return;

            bool anyTalking = IsAnyActorSpeaking;
            if (wasAnyActorSpeaking && !anyTalking)
                ResetVadCapture();
            wasAnyActorSpeaking = anyTalking;

            if (IsMicrophoneMuted)
            {
                SetLabel("🔇 MIC OFF (PRESS B)");
                return;
            }

            bool examEndedInsideCafe = evaluation.Stage == RestaurantStage.Complete && !IsExploringCity;
            if (IsBusy || anyTalking || examEndedInsideCafe)
            {
                if (IsBusy) SetLabel("● THINKING...");
                else if (foxSpeaker != null && foxSpeaker.isPlaying) SetLabel("● FOX SPEAKING");
                else if (speaker != null && speaker.isPlaying) SetLabel("● LUCAS SPEAKING");
                else if (isSynthesizingSpeech) SetLabel("● PREPARING VOICE");
                return;
            }

            if (!IsRecording || !micRingClip || !Microphone.IsRecording(device))
            {
                if (IsRecording)
                {
                    IsRecording = micBootstrapped = false;
                    if (micRingClip) Destroy(micRingClip);
                    micRingClip = null;
                    ResetVadCapture();
                }
#if UNITY_ANDROID && !UNITY_EDITOR
                if (micPermissionDenied && UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                    micPermissionDenied = false;
#endif
                if (!micBootstrapped && !micPermissionDenied && Time.realtimeSinceStartup >= nextMicAttempt)
                    vadBootstrapRoutine = StartCoroutine(EnsureMicrophoneRingRunning());
                return;
            }

            UpdateContinuousVad();
        }

        void UpdateContinuousVad()
        {
            int currentPos = Microphone.GetPosition(device);
            int totalSamples = micRingClip.samples;
            if (currentPos < 0 || totalSamples <= 0)
                return;

            int delta = currentPos - lastReadSamplePos;
            if (delta < 0) delta += totalSamples;
            if (delta < 256)
                return;

            int readCount = Mathf.Min(delta, sampleScratch.Length);
            int startIdx = (currentPos - readCount + totalSamples) % totalSamples;
            ReadRingSlice(startIdx, readCount, sampleScratch);
            lastReadSamplePos = currentPos;

            float sumSq = 0f;
            for (int i = 0; i < readCount; i++)
            {
                float s = sampleScratch[i];
                sumSq += s * s;
            }
            float rms = Mathf.Sqrt(sumSq / readCount);
            float dt = (float)readCount / SampleRate;

            float dynamicStartThreshold = Mathf.Max(vadStartRms, ambientNoiseFloor * 2.15f);

            if (!isCapturingUtterance)
            {
                SetLabel(IsExploringCity ? "● FOX GUIDE" : "● LISTENING (FR)");
                if (rms >= dynamicStartThreshold)
                {
                    speechStartAccum += dt;
                    if (speechStartAccum >= minSpeechDuration)
                    {
                        isCapturingUtterance = true;
                        int preRollSamples = Mathf.RoundToInt(0.42f * SampleRate);
                        utteranceStartSample = (currentPos - readCount - preRollSamples + totalSamples) % totalSamples;
                        utteranceLengthSeconds = speechStartAccum;
                        silenceEndAccum = 0f;
                        SetLabel("● SPEAKING...");
                        evaluation?.NotifyPlayerSpeaking();
                    }
                }
                else
                {
                    speechStartAccum = 0f;
                    ambientNoiseFloor = Mathf.Clamp(Mathf.Lerp(ambientNoiseFloor, rms, 0.04f), 0.002f, 0.012f);
                }
            }
            else
            {
                utteranceLengthSeconds += dt;
                SetLabel("● SPEAKING...");

                if (rms < vadStopRms)
                    silenceEndAccum += dt;
                else
                    silenceEndAccum = 0f;

                if (silenceEndAccum >= endSilenceDuration || utteranceLengthSeconds >= maxUtteranceSeconds)
                {
                    int captureLen = currentPos - utteranceStartSample;
                    if (captureLen < 0) captureLen += totalSamples;
                    captureLen = Mathf.Clamp(captureLen, 1600, totalSamples - 16);

                    var pcm = new float[captureLen];
                    ReadRingSlice(utteranceStartSample, captureLen, pcm);
                    ResetVadCapture();

                    // Run local C# acoustic human-voice filter BEFORE spending a Gemini API call
                    if (!PassesHumanSpeechFilter(pcm, micRingClip.frequency, ambientNoiseFloor))
                    {
                        FilteredNoiseCount++;
                        Debug.Log("[ParisVoice] Filtered ambient noise/transient — saved 1 Gemini API call.");
                        SetLabel(IsExploringCity ? "● FOX GUIDE" : "● LISTENING (FR)");
                        evaluation?.NotifyNoiseFiltered();
                        return;
                    }

                    byte[] wav = EncodeWav(pcm, micRingClip.channels, micRingClip.frequency);
                    evaluation?.NotifyPlayerThinking();
                    requestRoutine = StartCoroutine(SendPayloadToGemini(wav, null));
                }
            }
        }

        void ReadRingSlice(int startSample, int count, float[] dest)
        {
            micRingClip.GetData(dest.AsSpan(0, count), startSample);
        }

        void ResetVadCapture()
        {
            isCapturingUtterance = false;
            speechStartAccum = 0f;
            silenceEndAccum = 0f;
            utteranceLengthSeconds = 0f;
            if (micRingClip != null && !string.IsNullOrEmpty(device))
            {
                int pos = Microphone.GetPosition(device);
                if (pos >= 0) lastReadSamplePos = pos;
            }
        }

        public void NotifyUserActivity() { }

        public void ToggleRecording()
        {
            if (IsBusy) return;
            if (IsMicrophoneMuted)
            {
                SetMicrophoneMuted(false);
                return;
            }
            if (!HasGeminiKey)
            {
                SetLabel("NO GEMINI API KEY");
                evaluation?.Feedback("Google AI Studio (Gemini) API key is required. Add Assets/Resources/GeminiLocalKey.txt.");
                return;
            }
            if (!micBootstrapped)
                vadBootstrapRoutine = StartCoroutine(EnsureMicrophoneRingRunning());
        }

        public void SubmitTextToGemini(string userPhrase)
        {
            if (string.IsNullOrWhiteSpace(userPhrase) || IsBusy)
                return;

            if (TestTurnHandler != null)
            {
                var mocked = TestTurnHandler(userPhrase);
                if (mocked != null)
                {
                    ApplyParsedTurn(mocked, userPhrase);
                }
                return;
            }

            if (!HasGeminiKey)
            {
                SetLabel("NO GEMINI API KEY");
                evaluation?.Feedback("Online Gemini connection is required. Add Assets/Resources/GeminiLocalKey.txt and connect to Wi-Fi.");
                return;
            }

            if (Application.isBatchMode)
                return;

            evaluation?.NotifyPlayerThinking();
            requestRoutine = StartCoroutine(SendPayloadToGemini(null, userPhrase));
        }

        IEnumerator SendPayloadToGemini(byte[] wavBytes, string textInput)
        {
            string apiKey = ResolveGeminiKey();
            if (string.IsNullOrWhiteSpace(apiKey) || evaluation == null)
            {
                SetLabel("NO GEMINI API KEY");
                evaluation?.Feedback("Google AI Studio API key is missing. Add Assets/Resources/GeminiLocalKey.txt.");
                yield break;
            }

            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                SetLabel("OFFLINE · CONNECT WI-FI");
                evaluation?.Feedback("Internet connection is required for Gemini AI conversation. Please connect to Wi-Fi and speak again.");
                yield break;
            }

            IsBusy = true;
            SetLabel("● THINKING...");

            RestaurantPromptContext promptContext = evaluation.BuildPromptContext();
            string systemPrompt = npcPersonality != null
                ? npcPersonality.BuildDynamicCafeSystemPrompt(promptContext)
                : "You are Lucas Moreau, a Parisian waiter speaking natural A2/B1 French, and a Fox tutor speaking English. Reply ONLY in JSON.";

            string historySummary = BuildHistoryText();
            string userInstruction;
            if (promptContext.isExploringCity)
            {
                userInstruction = wavBytes != null
                    ? "The player is strolling around Paris away from the café. Transcribe their audio accurately, set speakerMode=\"fox_tutor\", and answer warmly in English in foxTutorReplyEnglish as their Paris Tour Guide & French Tutor."
                    : $"The player is strolling around Paris and said: \"{textInput}\". Set speakerMode=\"fox_tutor\" and answer warmly in English in foxTutorReplyEnglish as their Paris Tour Guide & French Tutor.";
            }
            else
            {
                userInstruction = wavBytes != null
                    ? "Listen carefully to the player's audio clip (even a single French word like 'Oui', 'Non', 'Café', 'Plate', OR an English question). Transcribe accurately in userTranscript. PRIORITY ROUTING: If the player speaks in English, asks what the waiter/Lucas said, asks for a translation/explanation/meaning/help, or mixes French words into an English question (e.g. 'What did the waiter say?', 'What does quiche mean?', 'Can you explain?'), set speakerMode=\"fox_tutor\" and directly answer their question in English in foxTutorReplyEnglish. Otherwise, if they are speaking French to Lucas, set speakerMode=\"waiter\" and reply in natural French."
                    : $"The player said: \"{textInput}\". PRIORITY ROUTING: If this is in English or asks what the waiter said / for translation / explanation / help (even with French words mixed in), set speakerMode=\"fox_tutor\" and directly answer in English in foxTutorReplyEnglish; otherwise if it is French for Lucas, set speakerMode=\"waiter\".";
            }

            string partsJson;
            if (wavBytes != null)
            {
                string b64 = Convert.ToBase64String(wavBytes);
                partsJson =
                    $"{{\"text\":{JsonEscape(historySummary + "\n" + userInstruction)}}}," +
                    $"{{\"inlineData\":{{\"mimeType\":\"audio/wav\",\"data\":\"{b64}\"}}}}";
            }
            else
            {
                partsJson = $"{{\"text\":{JsonEscape(historySummary + "\n" + userInstruction)}}}";
            }

            var candidateModels = BuildModelChain(geminiChatModel, "gemini-3.5-flash-lite", ChatFallbackModels);
            string responseBody = null;
            string lastError = null;
            long lastCode = 0;

            for (int m = 0; m < candidateModels.Count; m++)
            {
                string model = candidateModels[m];
                string thinkingLevel = model.IndexOf("3.8", StringComparison.OrdinalIgnoreCase) >= 0 ? "low" : "minimal";
                string payload =
                    "{" +
                    $"\"systemInstruction\":{{\"parts\":[{{\"text\":{JsonEscape(systemPrompt)}}}]}}," +
                    $"\"contents\":[{{\"role\":\"user\",\"parts\":[{partsJson}]}}]," +
                    "\"generationConfig\":{" +
                        "\"responseMimeType\":\"application/json\"," +
                        $"\"thinkingConfig\":{{\"thinkingLevel\":\"{thinkingLevel}\"}}," +
                        $"\"responseSchema\":{TurnResponseSchemaJson}" +
                    "}" +
                    "}";

                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
                using (var request = new UnityWebRequest(url, "POST"))
                {
                    operationRequest = request;
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                    request.SetRequestHeader("x-goog-api-key", apiKey);
                    request.timeout = 20;

                    yield return request.SendWebRequest();
                    operationRequest = null;

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        responseBody = request.downloadHandler.text;
                        break;
                    }

                    lastCode = request.responseCode;
                    lastError = $"{request.error} - {request.downloadHandler?.text}";
                    Debug.LogWarning($"[ParisVoice] Gemini chat ({model}) error {lastCode}: {lastError}");

                    bool retryable = lastCode == 404 || lastCode == 429 || lastCode == 500 || lastCode == 503 || lastCode == 0;
                    if (!retryable)
                        break;
                }
            }

            IsBusy = false;
            requestRoutine = null;

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                SetLabel(IsMicrophoneMuted ? "🔇 MIC OFF (PRESS B)" : "GEMINI ERROR · RETRY");
                evaluation.Feedback("Could not reach Google AI Studio (Gemini). Check your Wi-Fi connection and API key, then speak again.");
                ResetVadCapture();
                yield break;
            }

            GeminiWaiterTurn turn = ParseGeminiTurn(responseBody);
            if (turn == null)
            {
                SetLabel(IsMicrophoneMuted ? "🔇 MIC OFF (PRESS B)" : "● LISTENING (FR)");
                evaluation.Feedback("I did not catch that clearly. Please try speaking again.");
                ResetVadCapture();
                yield break;
            }

            string effectiveTranscript = !string.IsNullOrWhiteSpace(turn.userTranscript)
                ? turn.userTranscript.Trim()
                : (textInput ?? "");

            bool isTutor = string.Equals(turn.speakerMode, "fox_tutor", StringComparison.OrdinalIgnoreCase);

            // Deterministic C# safety net: never let Lucas hijack an explicit English question or a city-guide stroll
            if (!isTutor && (promptContext.isExploringCity || RestaurantTutor.IsDefinitelyEnglishForFox(effectiveTranscript)))
            {
                Debug.Log($"[ParisVoice] Overriding speakerMode to fox_tutor for transcript: \"{effectiveTranscript}\"");
                turn.speakerMode = "fox_tutor";
                isTutor = true;
                turn.newlyCompletedMilestones = Array.Empty<string>();
                turn.npcReplyFrench = "";
                turn.npcReplyEnglish = "";
            }

            if (isTutor && string.IsNullOrWhiteSpace(turn.foxTutorReplyEnglish))
            {
                if (!string.IsNullOrWhiteSpace(turn.feedbackEnglish))
                    turn.foxTutorReplyEnglish = turn.feedbackEnglish.Trim();
                else if (evaluation != null && evaluation.Tutor != null)
                    turn.foxTutorReplyEnglish = evaluation.Tutor.BuildFallbackEnglishTutorReply(effectiveTranscript);
                else
                    turn.foxTutorReplyEnglish = "Try speaking a short phrase in French to Lucas, or press Y for phrase cards.";
            }

            bool hasReply = isTutor
                ? !string.IsNullOrWhiteSpace(turn.foxTutorReplyEnglish)
                : !string.IsNullOrWhiteSpace(turn.npcReplyFrench);

            if (!hasReply)
            {
                SetLabel(IsMicrophoneMuted ? "🔇 MIC OFF (PRESS B)" : (IsExploringCity ? "● FOX GUIDE READY" : "● LISTENING (FR)"));
                evaluation.Feedback("I could not hear clear speech. Please try speaking again.");
                ResetVadCapture();
                yield break;
            }

            ApplyParsedTurn(turn, textInput);
            SetLabel(IsMicrophoneMuted ? "🔇 MIC OFF (PRESS B)" : (IsExploringCity ? "● FOX GUIDE READY" : "● LISTENING (FR)"));
            ResetVadCapture();
        }

        static List<string> BuildModelChain(string primary, string defaultPrimary, string[] fallbacks)
        {
            var chain = new List<string>();
            string first = string.IsNullOrWhiteSpace(primary) || primary.Equals("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase)
                ? defaultPrimary
                : primary.Trim();
            chain.Add(first);
            if (!chain.Contains(defaultPrimary))
                chain.Add(defaultPrimary);
            if (fallbacks != null)
            {
                for (int i = 0; i < fallbacks.Length; i++)
                    if (!string.IsNullOrWhiteSpace(fallbacks[i]) && !chain.Contains(fallbacks[i]))
                        chain.Add(fallbacks[i]);
            }
            return chain;
        }

        void ApplyParsedTurn(GeminiWaiterTurn turn, string fallbackTranscript)
        {
            string transcript = !string.IsNullOrWhiteSpace(turn.userTranscript) ? turn.userTranscript.Trim() : fallbackTranscript;
            LastTranscript = transcript;

            if (!string.IsNullOrWhiteSpace(transcript))
                RecordHistory("Player", transcript);

            bool isTutor = string.Equals(turn.speakerMode, "fox_tutor", StringComparison.OrdinalIgnoreCase);
            if (isTutor && !string.IsNullOrWhiteSpace(turn.foxTutorReplyEnglish))
                RecordHistory("Fox (tutor)", turn.foxTutorReplyEnglish.Trim());
            else if (!isTutor && !string.IsNullOrWhiteSpace(turn.npcReplyFrench))
                RecordHistory("Lucas (waiter)", turn.npcReplyFrench.Trim());

            evaluation?.ApplyGeminiTurn(turn);
        }

        string BuildHistoryText()
        {
            if (history.Count == 0)
                return "Conversation just started.";

            var sb = new StringBuilder("Conversation history so far:\n");
            for (int i = 0; i < history.Count; i++)
                sb.AppendLine($"{history[i].speaker}: {history[i].content}");
            return sb.ToString();
        }

        void RecordHistory(string who, string content)
        {
            history.Add(new HistoryEntry { speaker = who, content = content });
            if (history.Count > MaxHistoryEntries)
                history.RemoveRange(0, history.Count - MaxHistoryEntries);
        }

        public static GeminiWaiterTurn ParseGeminiTurn(string rawBody)
        {
            try
            {
                var root = JsonUtility.FromJson<GeminiResponse>(rawBody);
                string text = root?.candidates != null && root.candidates.Length > 0 &&
                              root.candidates[0].content?.parts != null && root.candidates[0].content.parts.Length > 0
                    ? root.candidates[0].content.parts[0].text
                    : null;

                if (string.IsNullOrWhiteSpace(text))
                    return null;

                text = text.Trim();
                if (text.StartsWith("```"))
                {
                    int firstNewline = text.IndexOf('\n');
                    int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
                    if (firstNewline >= 0 && lastFence > firstNewline)
                        text = text.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
                }

                return JsonUtility.FromJson<GeminiWaiterTurn>(text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ParisVoice] Failed to parse Gemini turn JSON: " + ex.Message);
                return null;
            }
        }

        // =====================================================================
        // Waiter Speech (Gemini TTS + RAM & Persistent Disk Cache)
        // & Fox Speech (Alibaba Cloud Model Studio TTS: Qwen family -> Sambert backups)
        // =====================================================================

        public void Speak(string text, AudioClip prerecorded = null) => SpeakAsWaiter(text);

        public void SpeakAsWaiter(string frenchText)
        {
            if (string.IsNullOrWhiteSpace(frenchText) || speaker == null)
                return;

            string voiceName = npcPersonality != null && !string.IsNullOrWhiteSpace(npcPersonality.geminiVoiceName)
                ? npcPersonality.geminiVoiceName
                : "Puck";

            string voiceTag = $"gemini_{voiceName}_fr";
            StopAllSpeech();

            // 1. Check 0ms RAM Cache + ~2ms Persistent Disk Cache first (0 API calls!)
            if (TryLoadCachedClip(voiceTag, frenchText, out AudioClip cachedClip))
            {
                speaker.clip = cachedClip;
                speaker.Play();
                return;
            }

            if (Application.isBatchMode || !HasGeminiKey)
                return;

            string hash = ComputeTtsCacheHash(voiceTag, frenchText);
            speechRoutine = StartCoroutine(SynthesizeWithGeminiTts(speaker, hash, voiceName, frenchText.Trim()));
        }

        /// <summary>
        /// Speaks using the Fox Companion's Alibaba Cloud Model Studio TTS (Singapore / ap-southeast-1):
        /// starts with 'qwen3-tts-flash' (voice 'Cherry') and fails over through Qwen TTS and Sambert
        /// backup models on quota exhaustion, backed by RAM + persistent disk WAV caching.
        /// Strictly Alibaba-only: if all models fail or no key is configured, Fox displays text panels silently.
        /// </summary>
        public void SpeakAsFox(string text, bool isFrenchModelPhrase, float speed = -1f)
        {
            string cleanText = RestaurantTutor.SanitizeForLocalSpeech(text);
            if (string.IsNullOrWhiteSpace(cleanText))
                return;

            ResolveFoxSpeaker();
            AudioSource target = foxSpeaker != null ? foxSpeaker : speaker;
            if (target == null)
                return;

            string resolvedVoice = !string.IsNullOrWhiteSpace(alibabaFoxVoice)
                ? alibabaFoxVoice.Trim()
                : (npcPersonality != null && !string.IsNullOrWhiteSpace(npcPersonality.foxVoiceName)
                    ? npcPersonality.foxVoiceName.Trim()
                    : AlibabaTtsClient.DefaultVoice);

            float resolvedSpeed = speed > 0f ? speed : (isFrenchModelPhrase ? 0.85f : 1.0f);
            int speedTag = Mathf.RoundToInt(resolvedSpeed * 100f);
            string langTag = isFrenchModelPhrase ? "fr" : "en";
            string voiceTag = $"alibaba_{resolvedVoice.ToLowerInvariant()}_{langTag}_{speedTag}";
            StopAllSpeech();

            // 1. Check 0ms RAM Cache + ~2ms Persistent Disk Cache first (0 API calls!)
            if (TryLoadCachedClip(voiceTag, cleanText, out AudioClip cachedClip))
            {
                target.clip = cachedClip;
                target.Play();
                return;
            }

            if (Application.isBatchMode || !HasAlibabaKey)
                return;

            if (Application.internetReachability == NetworkReachability.NotReachable)
                return;

            string hash = ComputeTtsCacheHash(voiceTag, cleanText);
            speechRoutine = StartCoroutine(SynthesizeWithAlibabaTts(target, hash, cleanText, resolvedVoice, isFrenchModelPhrase, resolvedSpeed));
        }

        IEnumerator SynthesizeWithAlibabaTts(AudioSource target, string cacheHash, string text, string voiceName, bool isFrench, float speed)
        {
            string apiKey = ResolveAlibabaKey();
            if (string.IsNullOrWhiteSpace(apiKey) || target == null)
                yield break;

            isSynthesizingSpeech = true;
            isSynthesizingFoxSpeech = true;

            if (alibabaTts == null)
                alibabaTts = new AlibabaTtsClient();

            var result = new AlibabaTtsClient.SynthesisOutput();
            yield return alibabaTts.SynthesizeWithFailover(
                apiKey,
                alibabaWorkspaceId,
                alibabaPrimaryModel,
                voiceName,
                text,
                isFrench,
                speed,
                result);

            isSynthesizingSpeech = false;
            isSynthesizingFoxSpeech = false;
            speechRoutine = null;

            if (!result.Success || result.Clip == null || target == null)
            {
                // Strict Alibaba-only policy: do not fall back to Gemini or clutter the visual panels;
                // the Fox smoothly stays silent while its hint/tutor/card panels remain readable.
                yield break;
            }

            CacheTtsClipInRam(cacheHash, result.Clip);
            if (result.WavBytesForDiskCache != null && result.WavBytesForDiskCache.Length >= 44)
                SaveWavToDiskCache(cacheHash, result.WavBytesForDiskCache);

            evaluation?.companion?.ShowVoiceDiagnostic("");
            target.clip = result.Clip;
            target.Play();
        }

        public void StopAllSpeech()
        {
            if (speaker != null) speaker.Stop();
            if (foxSpeaker != null) foxSpeaker.Stop();
            alibabaTts?.CancelActiveRequest();
            if (speechRequest != null)
            {
                speechRequest.Abort();
                speechRequest.Dispose();
                speechRequest = null;
            }
            if (speechRoutine != null)
            {
                StopCoroutine(speechRoutine);
                speechRoutine = null;
            }
            isSynthesizingSpeech = false;
            isSynthesizingFoxSpeech = false;
        }

        IEnumerator SynthesizeWithGeminiTts(AudioSource target, string cacheHash, string voiceName, string verbatimText)
        {
            string apiKey = ResolveGeminiKey();
            if (string.IsNullOrWhiteSpace(apiKey) || target == null)
                yield break;

            if (Application.internetReachability == NetworkReachability.NotReachable)
                yield break;

            isSynthesizingSpeech = true;

            string payload =
                "{" +
                $"\"contents\":[{{\"role\":\"user\",\"parts\":[{{\"text\":{JsonEscape(verbatimText)}}}]}}]," +
                "\"generationConfig\":{" +
                    "\"responseModalities\":[\"AUDIO\"]," +
                    $"\"speechConfig\":{{\"voiceConfig\":{{\"prebuiltVoiceConfig\":{{\"voiceName\":{JsonEscape(voiceName)}}}}}}}" +
                "}" +
                "}";

            var ttsChain = BuildModelChain(geminiTtsModel, "gemini-3.8-flash-lite-tts", TtsFallbackModels);
            for (int m = 0; m < ttsChain.Count; m++)
            {
                string model = ttsChain[m];
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
                using (var request = new UnityWebRequest(url, "POST"))
                {
                    speechRequest = request;
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                    request.SetRequestHeader("x-goog-api-key", apiKey);
                    request.timeout = 18;

                    yield return request.SendWebRequest();
                    speechRequest = null;

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        long code = request.responseCode;
                        Debug.LogWarning($"[ParisVoice] Gemini TTS ({model}) error ({code}): {request.error}");
                        bool retryable = code == 404 || code == 429 || code == 500 || code == 503 || code == 0;
                        if (retryable && m + 1 < ttsChain.Count)
                            continue;
                        break;
                    }

                    try
                    {
                        var resp = JsonUtility.FromJson<GeminiResponse>(request.downloadHandler.text);
                        GeminiInlineData inline = null;
                        if (resp?.candidates != null && resp.candidates.Length > 0 && resp.candidates[0].content?.parts != null)
                        {
                            foreach (var part in resp.candidates[0].content.parts)
                            {
                                if (part.inlineData != null && !string.IsNullOrEmpty(part.inlineData.data))
                                {
                                    inline = part.inlineData;
                                    break;
                                }
                            }
                        }

                        if (inline != null)
                        {
                            byte[] rawAudio = Convert.FromBase64String(inline.data);
                            AudioClip clip = DecodeGeminiAudio(rawAudio, inline.mimeType, $"GeminiVoice_{voiceName}", out float[] floatSamples, out int decodedRate);
                            if (clip != null)
                            {
                                CacheTtsClipInRam(cacheHash, clip);

                                // Persist WAV to disk so future repetitions/sessions load in ~2ms with 0 API calls
                                bool isAlreadyRiffWav = rawAudio.Length >= 44 &&
                                    rawAudio[0] == (byte)'R' && rawAudio[1] == (byte)'I' &&
                                    rawAudio[2] == (byte)'F' && rawAudio[3] == (byte)'F';
                                byte[] wavForDisk = isAlreadyRiffWav ? rawAudio : EncodeWav(floatSamples, 1, decodedRate);
                                SaveWavToDiskCache(cacheHash, wavForDisk);

                                target.clip = clip;
                                target.Play();
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[ParisVoice] Gemini TTS decode error: " + ex.Message);
                    }
                }
            }

            isSynthesizingSpeech = false;
            speechRoutine = null;
        }

        void CacheTtsClipInRam(string key, AudioClip clip)
        {
            if (string.IsNullOrWhiteSpace(key) || clip == null) return;
            if (ttsCache.ContainsKey(key)) return;
            ttsCache[key] = clip;
            ttsCacheOrder.Enqueue(key);

            while (ttsCacheOrder.Count > MaxCachedTtsClips)
            {
                string oldest = ttsCacheOrder.Dequeue();
                if (ttsCache.TryGetValue(oldest, out AudioClip oldClip))
                {
                    ttsCache.Remove(oldest);
                    if (oldClip != null &&
                        (speaker == null || speaker.clip != oldClip) &&
                        (foxSpeaker == null || foxSpeaker.clip != oldClip))
                    {
                        Destroy(oldClip);
                    }
                }
            }
        }

        public static AudioClip DecodeGeminiAudio(byte[] bytes, string mimeType, string clipName) =>
            DecodeGeminiAudio(bytes, mimeType, clipName, out _, out _);

        public static AudioClip DecodeGeminiAudio(byte[] bytes, string mimeType, string clipName, out float[] samples, out int sampleRate)
        {
            samples = null;
            sampleRate = ParseSampleRate(mimeType, 24000);
            if (bytes == null || bytes.Length < 4)
                return null;

            int channels = 1;
            int dataOffset = 0;
            int dataLength = bytes.Length;

            if (bytes.Length >= 44 &&
                bytes[0] == (byte)'R' && bytes[1] == (byte)'I' &&
                bytes[2] == (byte)'F' && bytes[3] == (byte)'F')
            {
                int pos = 12;
                bool foundData = false;
                while (pos + 8 <= bytes.Length)
                {
                    string chunkId = Encoding.ASCII.GetString(bytes, pos, 4);
                    int chunkSize = BitConverter.ToInt32(bytes, pos + 4);
                    int chunkDataStart = pos + 8;

                    if (chunkId == "fmt " && chunkDataStart + 16 <= bytes.Length)
                    {
                        channels = Math.Max(1, (int)BitConverter.ToInt16(bytes, chunkDataStart + 2));
                        sampleRate = Math.Max(8000, BitConverter.ToInt32(bytes, chunkDataStart + 4));
                    }
                    else if (chunkId == "data")
                    {
                        dataOffset = chunkDataStart;
                        dataLength = Math.Min(chunkSize, bytes.Length - dataOffset);
                        foundData = true;
                        break;
                    }

                    if (chunkSize < 0) break;
                    pos = chunkDataStart + chunkSize + (chunkSize & 1);
                }

                if (!foundData)
                {
                    dataOffset = 44;
                    dataLength = Math.Max(0, bytes.Length - 44);
                }
            }

            int totalSamples = dataLength / 2;
            int framesPerChannel = totalSamples / channels;
            if (framesPerChannel <= 0)
                return null;

            samples = new float[framesPerChannel * channels];
            for (int i = 0; i < samples.Length; i++)
            {
                int idx = dataOffset + i * 2;
                short s = (short)(bytes[idx] | (bytes[idx + 1] << 8));
                samples[i] = s / 32768f;
            }

            var clip = AudioClip.Create(clipName, framesPerChannel, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static int ParseSampleRate(string mimeType, int fallback)
        {
            if (string.IsNullOrEmpty(mimeType)) return fallback;
            var match = Regex.Match(mimeType, @"rate=(\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r)
                ? r
                : fallback;
        }

        public void Practice(string text)
        {
            SubmitTextToGemini(text);
        }

        void SetLabel(string value)
        {
            if (microphoneLabel != null)
                microphoneLabel.text = value;
        }

        public void ResetConversation()
        {
            if (!string.IsNullOrEmpty(device) && Microphone.IsRecording(device))
                Microphone.End(device);

            IsRecording = IsBusy = false;
            micBootstrapped = false;
            if (operationRequest != null)
            {
                operationRequest.Abort();
                operationRequest.Dispose();
                operationRequest = null;
            }
            if (requestRoutine != null)
            {
                StopCoroutine(requestRoutine);
                requestRoutine = null;
            }
            if (vadBootstrapRoutine != null)
            {
                StopCoroutine(vadBootstrapRoutine);
                vadBootstrapRoutine = null;
            }
            StopAllSpeech();

            if (micRingClip) Destroy(micRingClip);
            micRingClip = null;
            history.Clear();
            LastTranscript = null;
            ResetVadCapture();
            RefreshHudMicVisual();

            if (Application.isBatchMode)
                return;

            if (!HasGeminiKey)
            {
                SetLabel(IsMicrophoneMuted ? "🔇 MIC OFF (PRESS B)" : "NO GEMINI API KEY");
                return;
            }

            if (autoListenVad && isActiveAndEnabled && gameObject.activeInHierarchy)
            {
                SetLabel(IsMicrophoneMuted ? "🔇 MIC OFF (PRESS B)" : "STARTING MICROPHONE");
                vadBootstrapRoutine = StartCoroutine(EnsureMicrophoneRingRunning());
            }
            else
            {
                SetLabel(IsMicrophoneMuted ? "🔇 MIC OFF (PRESS B)" : "● READY");
            }
        }

        void OnDisable()
        {
            if (!string.IsNullOrEmpty(device) && Microphone.IsRecording(device))
                Microphone.End(device);
            IsRecording = IsBusy = false;
            micBootstrapped = false;
            if (operationRequest != null)
            {
                operationRequest.Abort();
                operationRequest.Dispose();
                operationRequest = null;
            }
            if (requestRoutine != null)
            {
                StopCoroutine(requestRoutine);
                requestRoutine = null;
            }
            if (vadBootstrapRoutine != null)
            {
                StopCoroutine(vadBootstrapRoutine);
                vadBootstrapRoutine = null;
            }
            StopAllSpeech();
            if (micRingClip) Destroy(micRingClip);
            micRingClip = null;
        }

        void OnDestroy()
        {
            alibabaTts?.CancelActiveRequest();
            alibabaTts = null;
            foreach (var kv in ttsCache)
                if (kv.Value != null) Destroy(kv.Value);
            ttsCache.Clear();
            ttsCacheOrder.Clear();
        }

        static string JsonEscape(string value)
        {
            if (value == null) return "\"\"";
            var sb = new StringBuilder(value.Length + 8);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.AppendFormat("\\u{0:X4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static byte[] EncodeWav(float[] pcm, int channels, int rate)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Length * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
                writer.Write((short)channels); writer.Write(rate); writer.Write(rate * channels * 2);
                writer.Write((short)(channels * 2)); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length * 2);
                foreach (var sample in pcm) writer.Write((short)(Mathf.Clamp(sample, -1, 1) * short.MaxValue));
                return stream.ToArray();
            }
        }
    }
}
