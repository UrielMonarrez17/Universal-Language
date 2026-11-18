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
    /// Quest 2 continuous Microphone (VAD) + Google AI Studio (Gemini) multimodal dialogue & TTS adapter.
    /// Supports both direct Gemini API integration (key loaded from ignored local Resources/GeminiLocalKey)
    /// and the optional local loopback serverUrl.
    /// </summary>
    public sealed class RestaurantVoiceBridge : MonoBehaviour
    {
        [Tooltip("Optional custom loopback backend base URL (e.g. http://127.0.0.1:8787).")]
        public string serviceUrl;

        [Header("Google AI Studio (Gemini)")]
        [Tooltip("Optional Inspector override; if blank, loads from Resources/GeminiLocalKey (git-ignored).")]
        [SerializeField] private string geminiApiKeyOverride;
        [SerializeField] private string geminiChatModel = "gemini-2.5-flash";
        [SerializeField] private string geminiTtsModel = "gemini-2.5-flash-preview-tts";

        [Header("Continuous Voice Activity Detection (VAD)")]
        public bool autoListenVad = true;
        [SerializeField] private float vadStartRms = 0.016f;
        [SerializeField] private float vadStopRms = 0.010f;
        [SerializeField] private float minSpeechDuration = 0.14f;
        [SerializeField] private float endSilenceDuration = 1.15f;
        [SerializeField] private float maxUtteranceSeconds = 9.5f;
        [SerializeField] private float prolongedPauseSeconds = 8.0f;

        [Header("Scene References")]
        public RestaurantEvaluation evaluation;
        public CommunicativeEncounter encounter;
        public NPCPersonality npcPersonality;
        public AudioSource speaker;
        public TMP_Text microphoneLabel;

        public bool IsConfigured => Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "https" || (uri.IsLoopback && uri.Scheme == "http"));

        public static bool DisableGeminiForTests;
        public bool HasGeminiKey => !DisableGeminiForTests && !string.IsNullOrWhiteSpace(ResolveGeminiKey());
        public bool IsAiReady => HasGeminiKey || IsConfigured;
        public bool IsRecording { get; private set; }
        public bool IsBusy { get; private set; }
        public string LastTranscript { get; private set; }
        public float SilenceDuration { get; private set; }

        const int SampleRate = 16000;
        const int RingBufferSeconds = 16;

        AudioClip micRingClip;
        string device;
        int lastReadSamplePos;
        bool isCapturingUtterance;
        int utteranceStartSample;
        float speechStartAccum;
        float silenceEndAccum;
        float utteranceLengthSeconds;
        bool silenceHelpTriggered;
        bool micPermissionDenied;
        bool micBootstrapped;
        bool wasSpeakerPlaying;
        string cachedResourceKey;

        Coroutine speechRoutine;
        UnityWebRequest speechRequest, operationRequest;
        readonly List<Turn> history = new List<Turn>();
        readonly float[] sampleScratch = new float[SampleRate * 2];

        [Serializable] class Transcript { public string text; }
        [Serializable] class SpokenLine { public string text; public string language = "fr-FR"; }
        [Serializable] class Turn { public string role; public string content; }
        [Serializable] class ChatRequest { public string language = "fr-FR"; public string scenario = "paris-restaurant"; public string dish; public string drink; public Turn[] history; }
        [Serializable] class ChatReply { public string reply; }

        // Gemini REST JSON DTOs
        [Serializable] class GeminiResponse { public GeminiCandidate[] candidates; }
        [Serializable] class GeminiCandidate { public GeminiContent content; }
        [Serializable] class GeminiContent { public GeminiPart[] parts; }
        [Serializable] class GeminiPart { public string text; public GeminiInlineData inlineData; }
        [Serializable] class GeminiInlineData { public string mimeType; public string data; }

        [Serializable]
        public sealed class GeminiWaiterTurn
        {
            public string userTranscript;
            public string npcReplyFrench;
            public string feedbackEnglish;
            public bool advanceObjective;
            public string detectedDish;
            public string detectedDrink;
            public string detectedWaterType;
            public int matchedFoxOptionIndex = -1;
            public FoxHelpOption[] nextFoxOptions;
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

        void Start()
        {
            if (npcPersonality == null)
                npcPersonality = FindAnyObjectByType<NPCPersonality>();

            if (Application.isBatchMode)
            {
                SetLabel("VOICE OFFLINE\nUSE RESPONSE CARDS");
                return;
            }

            if (HasGeminiKey)
            {
                SetLabel(autoListenVad ? "● LISTENING (FR)" : "READY");
                if (autoListenVad)
                    StartCoroutine(EnsureMicrophoneRingRunning());
            }
            else if (IsConfigured)
            {
                SetLabel("START SPEAKING");
                if (evaluation != null)
                    evaluation.Feedback("The waiter's voice is AI-generated. Speak in French.");
            }
            else
            {
                SetLabel("VOICE OFFLINE\nUSE RESPONSE CARDS");
            }
        }

        IEnumerator EnsureMicrophoneRingRunning()
        {
            if (micBootstrapped || micPermissionDenied || Application.isBatchMode)
                yield break;

            micBootstrapped = true;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                float deadline = Time.realtimeSinceStartup + 15f;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone) &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                {
                    micPermissionDenied = true;
                    SetLabel("MIC DENIED · PRESS Y");
                    if (evaluation != null)
                        evaluation.Feedback("Microphone permission needed for voice. Press Y for Fox options.");
                    yield break;
                }
            }
#else
            yield return null;
#endif

            if (Microphone.devices.Length == 0)
            {
                SetLabel("NO MIC · PRESS Y");
                yield break;
            }

            device = Microphone.devices[0];
            micRingClip = Microphone.Start(device, true, RingBufferSeconds, SampleRate);
            if (micRingClip == null)
            {
                SetLabel("MIC UNAVAILABLE");
                yield break;
            }

            lastReadSamplePos = 0;
            IsRecording = true;
        }

        void Update()
        {
            if (Application.isBatchMode)
                return;

            // Manual non-VAD loopback recording cap
            if (!autoListenVad && !HasGeminiKey && IsRecording && Time.realtimeSinceStartup - started > 11.5f)
            {
                FinishRecording();
                return;
            }

            if (!autoListenVad || !HasGeminiKey || evaluation == null)
                return;

            bool waiterTalking = speaker != null && speaker.isPlaying;
            if (wasSpeakerPlaying && !waiterTalking)
            {
                // Reset silence window cleanly as soon as the waiter finishes his turn.
                ResetVadCapture(resetProlongedPause: true);
            }
            wasSpeakerPlaying = waiterTalking;

            if (IsBusy || waiterTalking || evaluation.Stage == RestaurantStage.Complete)
            {
                if (IsBusy) SetLabel("● THINKING...");
                else if (waiterTalking) SetLabel("● WAITER SPEAKING");
                return;
            }

            // Track prolonged pause while it is the player's turn to respond.
            if (!isCapturingUtterance)
            {
                SilenceDuration += Time.unscaledDeltaTime;
                if (SilenceDuration >= prolongedPauseSeconds && !silenceHelpTriggered)
                {
                    silenceHelpTriggered = true;
                    evaluation.OnProlongedUserPause();
                }
            }

            if (micRingClip == null || string.IsNullOrEmpty(device) || !Microphone.IsRecording(device))
            {
                SetLabel("● PRESS Y FOR HELP");
                return;
            }

            UpdateContinuousVad();
        }

        float started;

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
                        int preRollSamples = Mathf.RoundToInt(0.40f * SampleRate);
                        utteranceStartSample = (currentPos - readCount - preRollSamples + totalSamples) % totalSamples;
                        utteranceLengthSeconds = speechStartAccum;
                        silenceEndAccum = 0f;
                        SilenceDuration = 0f;
                        SetLabel("● SPEAKING...");
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

                    ResetVadCapture(resetProlongedPause: true);
                    StartCoroutine(SendAudioToGemini(wav));
                }
            }
        }

        void ReadRingSlice(int startSample, int count, float[] dest)
        {
            int total = micRingClip.samples;
            if (startSample + count <= total)
            {
                var temp = new float[count];
                micRingClip.GetData(temp, startSample);
                Array.Copy(temp, 0, dest, 0, count);
            }
            else
            {
                int first = total - startSample;
                int second = count - first;
                var part1 = new float[first];
                var part2 = new float[second];
                micRingClip.GetData(part1, startSample);
                micRingClip.GetData(part2, 0);
                Array.Copy(part1, 0, dest, 0, first);
                Array.Copy(part2, 0, dest, first, second);
            }
        }

        void ResetVadCapture(bool resetProlongedPause)
        {
            isCapturingUtterance = false;
            speechStartAccum = 0f;
            silenceEndAccum = 0f;
            utteranceLengthSeconds = 0f;
            if (resetProlongedPause)
            {
                SilenceDuration = 0f;
                silenceHelpTriggered = false;
            }
            if (micRingClip != null && !string.IsNullOrEmpty(device))
            {
                int pos = Microphone.GetPosition(device);
                if (pos >= 0) lastReadSamplePos = pos;
            }
        }

        public void NotifyUserActivity()
        {
            SilenceDuration = 0f;
            silenceHelpTriggered = false;
        }

        public void ToggleRecording()
        {
            if (IsBusy) return;

            if (HasGeminiKey)
            {
                if (!micBootstrapped)
                    StartCoroutine(EnsureMicrophoneRingRunning());
                NotifyUserActivity();
                return;
            }

            if (!IsConfigured)
            {
                if (evaluation != null)
                    evaluation.Feedback("Voice is unavailable offline. Press Y for Fox help cards.");
                return;
            }

            if (IsRecording) FinishRecording();
            else if (speaker && speaker.isPlaying) evaluation?.Feedback("Let the waiter finish, then speak.");
            else StartCoroutine(StartLegacyRecording());
        }

        IEnumerator StartLegacyRecording()
        {
            IsBusy = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                float deadline = Time.realtimeSinceStartup + 15;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                { Fail("Microphone permission is needed to speak. Press Y for response cards."); yield break; }
            }
#else
            yield return null;
#endif
            if (Microphone.devices.Length == 0) { Fail("No microphone is available. Press Y for Fox help cards."); yield break; }
            device = Microphone.devices[0];
            micRingClip = Microphone.Start(device, false, 12, SampleRate);
            if (!micRingClip) { Fail("The microphone could not start. Please try again."); yield break; }
            started = Time.realtimeSinceStartup; IsRecording = true; IsBusy = false;
            SetLabel("FINISH SPEAKING"); evaluation?.Feedback("Listening in French...");
        }

        public void FinishRecording()
        {
            if (!IsRecording || HasGeminiKey) return;
            int samples = Microphone.GetPosition(device);
            Microphone.End(device); IsRecording = false;
            if (samples < 1600) { Destroy(micRingClip); Fail("I could not hear enough speech. Please try again."); return; }
            var pcm = new float[samples * micRingClip.channels]; micRingClip.GetData(pcm, 0);
            var wav = EncodeWav(pcm, micRingClip.channels, micRingClip.frequency);
            Destroy(micRingClip); micRingClip = null;
            StartCoroutine(TranscribeLegacy(wav));
        }

        IEnumerator TranscribeLegacy(byte[] wav)
        {
            IsBusy = true; SetLabel("● THINKING...");
            using (var request = new UnityWebRequest(serviceUrl.TrimEnd('/') + "/transcribe", "POST"))
            {
                operationRequest = request;
                request.uploadHandler = new UploadHandlerRaw(wav);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "audio/wav"); request.SetRequestHeader("X-Language", "fr-FR"); request.timeout = 25;
                yield return request.SendWebRequest(); operationRequest = null;
                if (request.result != UnityWebRequest.Result.Success) { Fail("I could not hear your answer. Try again or press Y."); yield break; }
                string text = null;
                try { text = JsonUtility.FromJson<Transcript>(request.downloadHandler.text)?.text; } catch (ArgumentException) { }
                IsBusy = false; SetLabel("START SPEAKING");
                LastTranscript = text;
                if (string.IsNullOrWhiteSpace(text)) { evaluation?.Feedback("No clear speech was detected. Please try again."); yield break; }
                encounter?.OnVoiceTranscriptionReceived(text);
            }
        }

        public void SubmitTextToGemini(string userPhrase)
        {
            if (!HasGeminiKey || string.IsNullOrWhiteSpace(userPhrase) || IsBusy || Application.isBatchMode)
                return;

            StartCoroutine(SendPayloadToGemini(null, userPhrase));
        }

        IEnumerator SendAudioToGemini(byte[] wav)
        {
            yield return SendPayloadToGemini(wav, null);
        }

        IEnumerator SendPayloadToGemini(byte[] wavBytes, string textInput)
        {
            string apiKey = ResolveGeminiKey();
            if (string.IsNullOrWhiteSpace(apiKey) || evaluation == null)
                yield break;

            IsBusy = true;
            SetLabel("● THINKING...");

            FoxHelpOption[] currentOptions = evaluation.GetCurrentContextualOptions();
            string systemPrompt = npcPersonality != null
                ? npcPersonality.BuildSystemPrompt(
                    evaluation.Stage.ToString(),
                    evaluation.Dish,
                    evaluation.Drink,
                    evaluation.WaterType,
                    evaluation.MenuInspected,
                    currentOptions)
                : "You are Lucas Moreau, a friendly Paris café waiter speaking simple A1/A2 French. Reply ONLY in JSON.";

            string historySummary = BuildHistoryText();
            string userInstruction = wavBytes != null
                ? "Listen to the player's French/English audio clip below. Transcribe what they actually said, evaluate if it fulfills the current conversational step, and reply naturally in character."
                : $"The player selected or spoke: \"{textInput}\". Evaluate and reply naturally in character.";

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

            string payload =
                "{" +
                $"\"systemInstruction\":{{\"parts\":[{{\"text\":{JsonEscape(systemPrompt)}}}]}}," +
                $"\"contents\":[{{\"role\":\"user\",\"parts\":[{partsJson}]}}]," +
                "\"generationConfig\":{\"responseMimeType\":\"application/json\",\"temperature\":0.55}" +
                "}";

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{geminiChatModel}:generateContent?key={apiKey}";
            using (var request = new UnityWebRequest(url, "POST"))
            {
                operationRequest = request;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                request.timeout = 25;

                yield return request.SendWebRequest();
                operationRequest = null;
                IsBusy = false;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"Gemini chat error: {request.error} - {request.downloadHandler?.text}");
                    SetLabel("● LISTENING (FR)");
                    if (!string.IsNullOrWhiteSpace(textInput))
                        evaluation.SubmitTranscript(textInput);
                    else
                        evaluation.Feedback("Could not reach Gemini right now. Try speaking again or press Y for help.");
                    yield break;
                }

                GeminiWaiterTurn turn = ParseGeminiTurn(request.downloadHandler.text);
                if (turn == null || string.IsNullOrWhiteSpace(turn.npcReplyFrench))
                {
                    SetLabel("● LISTENING (FR)");
                    if (!string.IsNullOrWhiteSpace(textInput))
                        evaluation.SubmitTranscript(textInput);
                    else
                        evaluation.Feedback("I did not catch that clearly. Please speak again.");
                    yield break;
                }

                string transcript = !string.IsNullOrWhiteSpace(turn.userTranscript) ? turn.userTranscript : textInput;
                LastTranscript = transcript;

                if (!string.IsNullOrWhiteSpace(transcript))
                    RecordHistory("user", transcript);
                RecordHistory("assistant", turn.npcReplyFrench);

                evaluation.ApplyGeminiTurn(turn, usedCardSelection: !string.IsNullOrWhiteSpace(textInput));
                SetLabel("● LISTENING (FR)");
            }
        }

        string BuildHistoryText()
        {
            if (history.Count == 0)
                return "Conversation just started.";

            var sb = new StringBuilder("Recent conversation:\n");
            for (int i = 0; i < history.Count; i++)
                sb.AppendLine($"{history[i].role}: {history[i].content}");
            return sb.ToString();
        }

        void RecordHistory(string role, string content)
        {
            history.Add(new Turn { role = role, content = content });
            if (history.Count > 12)
                history.RemoveRange(0, history.Count - 12);
        }

        static GeminiWaiterTurn ParseGeminiTurn(string rawBody)
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
                Debug.LogWarning("Failed to parse Gemini turn JSON: " + ex.Message);
                return null;
            }
        }

        public void Speak(string text, AudioClip prerecorded = null)
        {
            if (!speaker || Application.isBatchMode) return;
            speaker.Stop();
            if (speechRequest != null) { speechRequest.Abort(); speechRequest.Dispose(); speechRequest = null; }
            if (speechRoutine != null) StopCoroutine(speechRoutine);
            if (prerecorded) { speaker.clip = prerecorded; speaker.Play(); return; }

            if (HasGeminiKey)
                speechRoutine = StartCoroutine(SynthesizeWithGeminiTts(text));
            else if (IsConfigured)
                speechRoutine = StartCoroutine(SynthesizeLegacy(text));
        }

        IEnumerator SynthesizeWithGeminiTts(string frenchText)
        {
            string apiKey = ResolveGeminiKey();
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(frenchText))
                yield break;

            string voiceName = npcPersonality != null && !string.IsNullOrWhiteSpace(npcPersonality.geminiVoiceName)
                ? npcPersonality.geminiVoiceName
                : "Puck";

            string prompt = $"Read aloud warmly, naturally, and clearly in Parisian French for a beginner learner: {frenchText}";
            string payload =
                "{" +
                $"\"contents\":[{{\"parts\":[{{\"text\":{JsonEscape(prompt)}}}]}}]," +
                $"\"generationConfig\":{{\"responseModalities\":[\"AUDIO\"],\"speechConfig\":{{\"voiceConfig\":{{\"prebuiltVoiceConfig\":{{\"voiceName\":{JsonEscape(voiceName)}}}}}}}}}" +
                "}";

            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{geminiTtsModel}:generateContent?key={apiKey}";
            using (var request = new UnityWebRequest(url, "POST"))
            {
                speechRequest = request;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                request.timeout = 20;

                yield return request.SendWebRequest();
                speechRequest = null;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"Gemini TTS error: {request.error}");
                    speechRoutine = null;
                    yield break;
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
                        int rate = ParseSampleRate(inline.mimeType, 24000);
                        byte[] pcmBytes = Convert.FromBase64String(inline.data);
                        int sampleCount = pcmBytes.Length / 2;
                        float[] floatSamples = new float[sampleCount];
                        for (int i = 0; i < sampleCount; i++)
                        {
                            short s = (short)(pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8));
                            floatSamples[i] = s / 32768f;
                        }

                        if (speaker.clip && speaker.clip.name == "GeminiWaiterVoice")
                            Destroy(speaker.clip);

                        var clip = AudioClip.Create("GeminiWaiterVoice", sampleCount, 1, rate, false);
                        clip.SetData(floatSamples, 0);
                        speaker.clip = clip;
                        speaker.Play();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Gemini TTS decode error: " + ex.Message);
                }
            }
            speechRoutine = null;
        }

        static int ParseSampleRate(string mimeType, int fallback)
        {
            if (string.IsNullOrEmpty(mimeType)) return fallback;
            var match = Regex.Match(mimeType, @"rate=(\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r)
                ? r
                : fallback;
        }

        IEnumerator SynthesizeLegacy(string text)
        {
            string url = serviceUrl.TrimEnd('/') + "/speak";
            using (var request = new UnityWebRequest(url, "POST"))
            {
                speechRequest = request;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new SpokenLine { text = text })));
                request.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.WAV);
                request.SetRequestHeader("Content-Type", "application/json"); request.timeout = 20;
                yield return request.SendWebRequest(); speechRequest = null;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    if (speaker.clip && speaker.clip.name == "Generated restaurant speech") Destroy(speaker.clip);
                    speaker.clip = DownloadHandlerAudioClip.GetContent(request); speaker.clip.name = "Generated restaurant speech"; speaker.Play();
                }
                else evaluation?.Feedback("The waiter's voice is unavailable. The French dialogue remains visible.");
            }
            speechRoutine = null;
        }

        public void Practice(string text)
        {
            if (HasGeminiKey)
            {
                SubmitTextToGemini(text);
                return;
            }
            if (!IsConfigured || IsBusy) return;
            StartCoroutine(ChatLegacy(text));
        }

        IEnumerator ChatLegacy(string text)
        {
            IsBusy = true;
            RecordHistory("user", text);
            var body = new ChatRequest { dish = evaluation.Dish, drink = evaluation.Drink, history = history.ToArray() };
            using (var request = new UnityWebRequest(serviceUrl.TrimEnd('/') + "/chat", "POST"))
            {
                operationRequest = request;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                request.downloadHandler = new DownloadHandlerBuffer(); request.SetRequestHeader("Content-Type", "application/json"); request.timeout = 25;
                yield return request.SendWebRequest(); operationRequest = null; IsBusy = false;
                if (request.result != UnityWebRequest.Result.Success) { Fail("Conversation is temporarily unavailable. Your evaluation is saved."); yield break; }
                string reply = null;
                try { reply = JsonUtility.FromJson<ChatReply>(request.downloadHandler.text)?.reply; } catch (ArgumentException) { }
                if (string.IsNullOrWhiteSpace(reply)) { Fail("Please try your question again."); yield break; }
                RecordHistory("assistant", reply);
                evaluation?.ReceivePracticeReply(reply);
            }
        }

        void Fail(string message) { IsBusy = false; SetLabel("START SPEAKING"); evaluation?.Feedback(message); }
        void SetLabel(string value) { if (microphoneLabel) microphoneLabel.text = value; }

        public void ResetConversation()
        {
            if (IsRecording && !string.IsNullOrEmpty(device))
            {
                Microphone.End(device);
            }
            IsRecording = IsBusy = false;
            micBootstrapped = false;
            operationRequest?.Abort(); speechRequest?.Abort();
            StopAllCoroutines();
            if (micRingClip) Destroy(micRingClip);
            micRingClip = null; operationRequest = speechRequest = null; speechRoutine = null;
            history.Clear(); LastTranscript = null;
            ResetVadCapture(resetProlongedPause: true);
            if (speaker) speaker.Stop();

            if (HasGeminiKey && !Application.isBatchMode)
            {
                SetLabel(autoListenVad ? "● LISTENING (FR)" : "READY");
                if (autoListenVad && isActiveAndEnabled && gameObject.activeInHierarchy)
                    StartCoroutine(EnsureMicrophoneRingRunning());
            }
            else
            {
                SetLabel(IsConfigured ? "START SPEAKING" : "VOICE OFFLINE\nUSE RESPONSE CARDS");
            }
        }

        void OnDisable()
        {
            if (IsRecording && !string.IsNullOrEmpty(device))
                Microphone.End(device);
            IsRecording = IsBusy = false;
            micBootstrapped = false;
            operationRequest?.Abort();
            speechRequest?.Abort();
            StopAllCoroutines();
            if (micRingClip) Destroy(micRingClip);
            micRingClip = null;
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
