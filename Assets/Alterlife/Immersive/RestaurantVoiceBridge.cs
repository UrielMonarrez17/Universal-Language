using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Online Google AI Studio (Gemini) multimodal voice & dual-speaker spatial TTS bridge for Le Café de Paris.
    /// Captures natural speech via continuous Quest/PC microphone VAD, sends audio + full non-linear rubric state
    /// to Gemini with strict structured JSON schema output, and synthesizes independent spatial voices for
    /// Lucas Moreau (waiter, French) and the Fox Tutor (assistant, English + French pronunciation modeling).
    /// </summary>
    public sealed class RestaurantVoiceBridge : MonoBehaviour
    {
        [Header("Google AI Studio (Gemini)")]
        [Tooltip("Optional Inspector override; if blank, loads from Resources/GeminiLocalKey (git-ignored).")]
        [SerializeField] private string geminiApiKeyOverride;
        [SerializeField] private string geminiChatModel = "gemini-3.5-flash-lite";
        [SerializeField] private string geminiTtsModel = "gemini-3.8-flash-lite-tts";

        static readonly string[] ChatFallbackModels = { "gemini-3.6-flash", "gemini-3.8-flash" };
        static readonly string[] TtsFallbackModels = { "gemini-3.8-flash-tts", "gemini-2.5-flash-preview-tts" };

        [Header("Continuous Voice Activity Detection (VAD)")]
        public bool autoListenVad = true;
        [SerializeField] private float vadStartRms = 0.015f;
        [SerializeField] private float vadStopRms = 0.009f;
        [SerializeField] private float minSpeechDuration = 0.14f;
        [SerializeField] private float endSilenceDuration = 1.35f;
        [SerializeField] private float maxUtteranceSeconds = 14.0f;

        [Header("Scene References")]
        public RestaurantEvaluation evaluation;
        public CommunicativeEncounter encounter;
        public NPCPersonality npcPersonality;
        public AudioSource speaker;
        public AudioSource foxSpeaker;
        public TMP_Text microphoneLabel;

        // Legacy serialized field kept empty/unused for serialization stability with existing scene YAML
        [HideInInspector] public string serviceUrl;

        /// <summary>Disables live Gemini HTTP calls during automated Editor tests.</summary>
        public static bool DisableGeminiForTests;

        /// <summary>Optional deterministic handler used exclusively by automated Play Mode tests.</summary>
        public static Func<string, GeminiWaiterTurn> TestTurnHandler;

        public bool HasGeminiKey => !DisableGeminiForTests && !string.IsNullOrWhiteSpace(ResolveGeminiKey());
        public bool IsConfigured => HasGeminiKey;
        public bool IsAiReady => HasGeminiKey || TestTurnHandler != null;
        public bool IsRecording { get; private set; }
        public bool IsBusy { get; private set; }
        public bool IsAnyActorSpeaking =>
            isSynthesizingSpeech ||
            (speaker != null && speaker.isPlaying) ||
            (foxSpeaker != null && foxSpeaker.isPlaying);
        public bool IsWaiterSpeaking => speaker != null && speaker.isPlaying;
        public bool IsPlayerSpeaking => isCapturingUtterance;
        public bool IsThinking => IsBusy || isSynthesizingSpeech;
        public string LastTranscript { get; private set; }

        const int SampleRate = 16000;
        const int RingBufferSeconds = 20;
        const int MaxCachedTtsClips = 48;

        AudioClip micRingClip;
        string device;
        int lastReadSamplePos;
        bool isCapturingUtterance;
        int utteranceStartSample;
        float speechStartAccum;
        float silenceEndAccum;
        float utteranceLengthSeconds;
        float nextMicAttempt;
        bool micPermissionDenied;
        bool micBootstrapped;
        bool wasAnyActorSpeaking;
        bool isSynthesizingSpeech;
        string cachedResourceKey;

        Coroutine speechRoutine;
        UnityWebRequest speechRequest;
        UnityWebRequest operationRequest;

        readonly List<HistoryEntry> history = new List<HistoryEntry>();
        readonly float[] sampleScratch = new float[SampleRate * 2];
        readonly Dictionary<string, AudioClip> ttsCache = new Dictionary<string, AudioClip>();
        readonly Queue<string> ttsCacheOrder = new Queue<string>();

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

        void Start()
        {
            if (npcPersonality == null)
                npcPersonality = FindAnyObjectByType<NPCPersonality>();
            ResolveFoxSpeaker();
        }

        void ResolveFoxSpeaker()
        {
            if (foxSpeaker == null && evaluation != null && evaluation.companion != null)
                foxSpeaker = evaluation.companion.AudioSource;
        }

        public void SeedWaiterOpening(string openingFrench)
        {
            if (!string.IsNullOrWhiteSpace(openingFrench))
                RecordHistory("Lucas (waiter)", openingFrench);
        }

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
            SetLabel("● LISTENING (FR)");
        }

        void Update()
        {
            if (Application.isBatchMode || evaluation == null)
                return;

            if (!HasGeminiKey)
            {
                SetLabel("NO GEMINI API KEY");
                return;
            }

            if (!autoListenVad)
                return;

            bool anyTalking = IsAnyActorSpeaking;
            if (wasAnyActorSpeaking && !anyTalking)
                ResetVadCapture();
            wasAnyActorSpeaking = anyTalking;

            if (IsBusy || anyTalking || evaluation.Stage == RestaurantStage.Complete)
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
                    StartCoroutine(EnsureMicrophoneRingRunning());
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

            if (!isCapturingUtterance)
            {
                SetLabel("● LISTENING (FR)");
                if (rms >= vadStartRms)
                {
                    speechStartAccum += dt;
                    if (speechStartAccum >= minSpeechDuration)
                    {
                        isCapturingUtterance = true;
                        int preRollSamples = Mathf.RoundToInt(0.45f * SampleRate);
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
                    byte[] wav = EncodeWav(pcm, micRingClip.channels, micRingClip.frequency);

                    ResetVadCapture();
                    evaluation?.NotifyPlayerThinking();
                    StartCoroutine(SendPayloadToGemini(wav, null));
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
            if (!HasGeminiKey)
            {
                SetLabel("NO GEMINI API KEY");
                evaluation?.Feedback("Google AI Studio (Gemini) API key is required. Add Assets/Resources/GeminiLocalKey.txt.");
                return;
            }
            if (!micBootstrapped)
                StartCoroutine(EnsureMicrophoneRingRunning());
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
            StartCoroutine(SendPayloadToGemini(null, userPhrase));
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
            string userInstruction = wavBytes != null
                ? "Listen carefully to the player's audio clip. Transcribe accurately what they actually said (French or English). Decide speakerMode ('waiter' for French conversation with Lucas, 'fox_tutor' for English help/translation/vocabulary questions or purely English replies), evaluate any newly fulfilled milestones, and respond naturally."
                : $"The player said: \"{textInput}\". Decide speakerMode, evaluate any newly fulfilled milestones, and respond naturally.";

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

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                SetLabel("GEMINI ERROR · RETRY");
                evaluation.Feedback("Could not reach Google AI Studio (Gemini). Check your Wi-Fi connection and API key, then speak again.");
                ResetVadCapture();
                yield break;
            }

            GeminiWaiterTurn turn = ParseGeminiTurn(responseBody);
            if (turn == null)
            {
                SetLabel("● LISTENING (FR)");
                evaluation.Feedback("I did not catch that clearly. Please try speaking again.");
                ResetVadCapture();
                yield break;
            }

            bool isTutor = string.Equals(turn.speakerMode, "fox_tutor", StringComparison.OrdinalIgnoreCase);
            bool hasReply = isTutor
                ? !string.IsNullOrWhiteSpace(turn.foxTutorReplyEnglish)
                : !string.IsNullOrWhiteSpace(turn.npcReplyFrench);

            if (!hasReply)
            {
                SetLabel("● LISTENING (FR)");
                evaluation.Feedback("I could not hear clear speech. Please try speaking again.");
                ResetVadCapture();
                yield break;
            }

            ApplyParsedTurn(turn, textInput);
            SetLabel("● LISTENING (FR)");
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
            if (history.Count > 18)
                history.RemoveRange(0, history.Count - 18);
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

        public void Speak(string text, AudioClip prerecorded = null) => SpeakAsWaiter(text);

        public void SpeakAsWaiter(string frenchText)
        {
            if (string.IsNullOrWhiteSpace(frenchText) || speaker == null)
                return;

            string voiceName = npcPersonality != null && !string.IsNullOrWhiteSpace(npcPersonality.geminiVoiceName)
                ? npcPersonality.geminiVoiceName
                : "Puck";

            SpeakOnTarget(
                speaker,
                frenchText,
                voiceName,
                "waiter_fr",
                frenchText.Trim());
        }

        public void SpeakAsFox(string text, bool isFrenchModelPhrase)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            ResolveFoxSpeaker();
            AudioSource target = foxSpeaker != null ? foxSpeaker : speaker;
            if (target == null)
                return;

            string voiceName = npcPersonality != null && !string.IsNullOrWhiteSpace(npcPersonality.foxVoiceName)
                ? npcPersonality.foxVoiceName
                : "Aoede";

            string styleTag = isFrenchModelPhrase ? "fox_fr" : "fox_en";
            SpeakOnTarget(target, text, voiceName, styleTag, text.Trim());
        }

        void SpeakOnTarget(AudioSource target, string text, string voiceName, string styleTag, string prompt)
        {
            StopAllSpeech();

            string cacheKey = $"{voiceName}|{styleTag}|{text}";
            if (ttsCache.TryGetValue(cacheKey, out AudioClip cached) && cached != null)
            {
                target.clip = cached;
                target.Play();
                return;
            }

            if (Application.isBatchMode || !HasGeminiKey)
                return;

            speechRoutine = StartCoroutine(SynthesizeWithGeminiTts(target, cacheKey, voiceName, prompt));
        }

        public void StopAllSpeech()
        {
            if (speaker != null) speaker.Stop();
            if (foxSpeaker != null) foxSpeaker.Stop();
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
        }

        IEnumerator SynthesizeWithGeminiTts(AudioSource target, string cacheKey, string voiceName, string verbatimText)
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
                            AudioClip clip = DecodeGeminiAudio(rawAudio, inline.mimeType, $"GeminiVoice_{voiceName}");
                            if (clip != null)
                            {
                                CacheTtsClip(cacheKey, clip);
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

        void CacheTtsClip(string key, AudioClip clip)
        {
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

        /// <summary>
        /// Decodes Gemini TTS audio supporting both RIFF/WAV containers (audio/wav) and headerless 16-bit PCM (audio/L16).
        /// </summary>
        public static AudioClip DecodeGeminiAudio(byte[] bytes, string mimeType, string clipName)
        {
            if (bytes == null || bytes.Length < 4)
                return null;

            int channels = 1;
            int sampleRate = ParseSampleRate(mimeType, 24000);
            int dataOffset = 0;
            int dataLength = bytes.Length;

            // Detect RIFF WAV container header cleanly
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

            float[] samples = new float[framesPerChannel * channels];
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
            StopAllSpeech();
            StopAllCoroutines();

            if (micRingClip) Destroy(micRingClip);
            micRingClip = null;
            history.Clear();
            LastTranscript = null;
            ResetVadCapture();

            if (Application.isBatchMode)
                return;

            if (!HasGeminiKey)
            {
                SetLabel("NO GEMINI API KEY");
                return;
            }

            if (autoListenVad && isActiveAndEnabled && gameObject.activeInHierarchy)
            {
                SetLabel("STARTING MICROPHONE");
                StartCoroutine(EnsureMicrophoneRingRunning());
            }
            else
            {
                SetLabel("● READY");
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
            StopAllSpeech();
            StopAllCoroutines();
            if (micRingClip) Destroy(micRingClip);
            micRingClip = null;
        }

        void OnDestroy()
        {
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
