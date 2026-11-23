using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Alterlife.Immersive
{
    public enum AlibabaTtsProtocol
    {
        QwenHttp,
        SambertWebSocket
    }

    public readonly struct AlibabaTtsModelSpec
    {
        public readonly string ModelId;
        public readonly AlibabaTtsProtocol Protocol;
        public readonly string Voice;
        public readonly bool IncludeLanguageType;

        public AlibabaTtsModelSpec(string modelId, AlibabaTtsProtocol protocol, string voice, bool includeLanguageType)
        {
            ModelId = modelId;
            Protocol = protocol;
            Voice = voice;
            IncludeLanguageType = includeLanguageType;
        }
    }

    /// <summary>
    /// Alibaba Cloud Model Studio (DashScope International — Singapore / ap-southeast-1) TTS client
    /// with automatic multi-model quota/error failover across the Qwen TTS family (HTTP) and the
    /// Sambert TTS family (WebSocket SpeechSynthesizer).
    ///
    /// Design goals for Meta Quest 2:
    /// - Zero native plugins, zero ONNX RAM/CPU overhead, zero third-party SDK dependencies.
    /// - Strictly stays within Alibaba models (no fallback to Gemini); if all quotas are exhausted,
    ///   returns failure cleanly so the Fox Companion stays silent while displaying its visual text panels.
    /// - Remembers permanently quota-exhausted / unavailable models for the session so follow-up Fox lines
    ///   jump immediately to the next working backup model without wasting latency.
    /// - Upgrades OSS http:// audio download URLs to https:// so Android / Quest 2 never blocks cleartext HTTP.
    /// </summary>
    public sealed class AlibabaTtsClient
    {
        public const string DefaultInternationalHttpEndpoint =
            "https://dashscope-intl.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation";

        public const string DefaultInternationalWsEndpoint =
            "wss://dashscope-intl.aliyuncs.com/api-ws/v1/inference";

        public const string DefaultPrimaryModel = "qwen3-tts-flash";
        public const string DefaultVoice = "Cherry";

        // Qwen TTS HTTP family ordered from fastest/primary to backups
        static readonly (string modelId, bool includeLanguageType)[] QwenBackupModels =
        {
            ("qwen3-tts-flash", true),
            ("qwen3-tts-instruct-flash", true),
            ("qwen3-tts-plus", true),
            ("qwen-audio-3.1-tts-flash", true),
            ("qwen-audio-3.1-tts-next", true),
            ("qwen-tts", false),
            ("qwen-tts-latest", false)
        };

        // Sambert WebSocket family — native French female voice (clara) prioritized for French cards
        static readonly string[] SambertFrenchModels =
        {
            "sambert-clara-v1",
            "sambert-beth-v1",
            "sambert-betty-v1",
            "sambert-cindy-v1",
            "sambert-eva-v1",
            "sambert-cally-v1",
            "sambert-donna-v1"
        };

        // Sambert WebSocket family — native US English female voices prioritized for English Fox tutor
        static readonly string[] SambertEnglishModels =
        {
            "sambert-beth-v1",
            "sambert-betty-v1",
            "sambert-cindy-v1",
            "sambert-eva-v1",
            "sambert-cally-v1",
            "sambert-donna-v1",
            "sambert-clara-v1"
        };

        readonly HashSet<string> exhaustedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        UnityWebRequest activeWebRequest;
        CancellationTokenSource activeWsCts;

        public string ActiveModelId { get; private set; } = "";
        public string LastError { get; private set; } = "";
        public int ExhaustedModelCount => exhaustedModels.Count;

        public sealed class SynthesisOutput
        {
            public bool Success;
            public string ModelUsed;
            public AudioClip Clip;
            public byte[] WavBytesForDiskCache;
            public string Error;
            public bool AuthInvalid;
        }

        [Serializable]
        sealed class DashScopeTtsResponse
        {
            public string code;
            public string message;
            public string request_id;
            public DashScopeTtsOutput output;
        }

        [Serializable]
        sealed class DashScopeTtsOutput
        {
            public string finish_reason;
            public DashScopeTtsAudio audio;
        }

        [Serializable]
        sealed class DashScopeTtsAudio
        {
            public string url;
            public string data;
            public string id;
        }

        [Serializable]
        sealed class WsEventEnvelope
        {
            public WsEventHeader header;
        }

        [Serializable]
        sealed class WsEventHeader
        {
            public string @event;
            public string error_code;
            public string error_message;
        }

        /// <summary>
        /// Sanitizes a raw API key file (ignoring blank lines, comments starting with '#', and placeholder templates).
        /// </summary>
        public static string NormalizeApiKey(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return "";

            string[] lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string candidate = lines[i].Trim();
                if (string.IsNullOrEmpty(candidate) || candidate.StartsWith("#", StringComparison.Ordinal))
                    continue;
                if (candidate.StartsWith("PASTE_", StringComparison.OrdinalIgnoreCase) ||
                    candidate.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
                    candidate.Equals("sk-xxxxxxxxxxxxxxxx", StringComparison.OrdinalIgnoreCase))
                    continue;
                return candidate;
            }

            return "";
        }

        public static string ResolveHttpEndpoint(string workspaceId)
        {
            string ws = (workspaceId ?? "").Trim();
            if (string.IsNullOrEmpty(ws))
                return DefaultInternationalHttpEndpoint;
            return $"https://{ws}.ap-southeast-1.maas.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation";
        }

        public static string ResolveWsEndpoint(string workspaceId)
        {
            string ws = (workspaceId ?? "").Trim();
            if (string.IsNullOrEmpty(ws))
                return DefaultInternationalWsEndpoint;
            return $"wss://{ws}.ap-southeast-1.maas.aliyuncs.com/api-ws/v1/inference";
        }

        /// <summary>
        /// Builds the ordered Alibaba TTS failover chain: primary Qwen model -> Qwen TTS backups -> Sambert backups.
        /// </summary>
        public static List<AlibabaTtsModelSpec> BuildModelChain(string primaryModel, string voiceName, bool isFrench)
        {
            var chain = new List<AlibabaTtsModelSpec>(16);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string resolvedVoice = string.IsNullOrWhiteSpace(voiceName) ? DefaultVoice : voiceName.Trim();
            string first = string.IsNullOrWhiteSpace(primaryModel) ? DefaultPrimaryModel : primaryModel.Trim();

            void AddSpec(string modelId, AlibabaTtsProtocol protocol, string voice, bool includeLang)
            {
                if (string.IsNullOrWhiteSpace(modelId) || !seen.Add(modelId))
                    return;
                chain.Add(new AlibabaTtsModelSpec(modelId, protocol, voice, includeLang));
            }

            // 1. Primary model first
            if (first.StartsWith("sambert-", StringComparison.OrdinalIgnoreCase))
            {
                AddSpec(first, AlibabaTtsProtocol.SambertWebSocket, first, false);
            }
            else
            {
                bool includeLang = !first.Equals("qwen-tts", StringComparison.OrdinalIgnoreCase) &&
                                   !first.Equals("qwen-tts-latest", StringComparison.OrdinalIgnoreCase);
                AddSpec(first, AlibabaTtsProtocol.QwenHttp, resolvedVoice, includeLang);
            }

            // 2. Full Qwen TTS HTTP family backups
            for (int i = 0; i < QwenBackupModels.Length; i++)
            {
                var entry = QwenBackupModels[i];
                AddSpec(entry.modelId, AlibabaTtsProtocol.QwenHttp, resolvedVoice, entry.includeLanguageType);
            }

            // 3. Full Sambert WebSocket family backups (language-appropriate voice first)
            string[] sambertList = isFrench ? SambertFrenchModels : SambertEnglishModels;
            for (int i = 0; i < sambertList.Length; i++)
            {
                AddSpec(sambertList[i], AlibabaTtsProtocol.SambertWebSocket, sambertList[i], false);
            }

            return chain;
        }

        public static string BuildQwenHttpPayload(AlibabaTtsModelSpec spec, string text, bool isFrench)
        {
            string languageType = isFrench ? "French" : "English";
            var sb = new StringBuilder(256);
            sb.Append("{\"model\":");
            sb.Append(RestaurantVoiceBridge.JsonEscape(spec.ModelId));
            sb.Append(",\"input\":{\"text\":");
            sb.Append(RestaurantVoiceBridge.JsonEscape(text));
            sb.Append(",\"voice\":");
            sb.Append(RestaurantVoiceBridge.JsonEscape(string.IsNullOrWhiteSpace(spec.Voice) ? DefaultVoice : spec.Voice));
            if (spec.IncludeLanguageType)
            {
                sb.Append(",\"language_type\":");
                sb.Append(RestaurantVoiceBridge.JsonEscape(languageType));
            }
            sb.Append("}}");
            return sb.ToString();
        }

        public static string BuildSambertRunTaskPayload(string taskId, string modelId, string text, float speed, int sampleRate = 16000)
        {
            float clampedRate = Mathf.Clamp(speed > 0f ? speed : 1.0f, 0.5f, 2.0f);
            string rateStr = clampedRate.ToString("0.##", CultureInfo.InvariantCulture);
            var sb = new StringBuilder(384);
            sb.Append("{\"header\":{\"action\":\"run-task\",\"task_id\":");
            sb.Append(RestaurantVoiceBridge.JsonEscape(taskId));
            sb.Append(",\"streaming\":\"out\"},\"payload\":{\"task_group\":\"audio\",\"task\":\"tts\",\"function\":\"SpeechSynthesizer\",\"model\":");
            sb.Append(RestaurantVoiceBridge.JsonEscape(modelId));
            sb.Append(",\"parameters\":{\"text_type\":\"PlainText\",\"format\":\"pcm\",\"sample_rate\":");
            sb.Append(sampleRate.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"volume\":50,\"rate\":");
            sb.Append(rateStr);
            sb.Append(",\"pitch\":1.0,\"word_timestamp_enabled\":false,\"phoneme_timestamp_enabled\":false},\"input\":{\"text\":");
            sb.Append(RestaurantVoiceBridge.JsonEscape(text));
            sb.Append("}}}");
            return sb.ToString();
        }

        public static string UpgradeToHttpsIfNeeded(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "";
            string trimmed = url.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return "https://" + trimmed.Substring("http://".Length);
            return trimmed;
        }

        public static bool TryParseQwenHttpResponse(string json, out string audioUrl, out string inlineBase64, out string errorCode, out string errorMessage)
        {
            audioUrl = "";
            inlineBase64 = "";
            errorCode = "";
            errorMessage = "";

            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var parsed = JsonUtility.FromJson<DashScopeTtsResponse>(json);
                if (parsed == null)
                    return false;

                errorCode = parsed.code ?? "";
                errorMessage = parsed.message ?? "";

                if (parsed.output?.audio != null)
                {
                    audioUrl = UpgradeToHttpsIfNeeded(parsed.output.audio.url);
                    inlineBase64 = parsed.output.audio.data ?? "";
                }

                return !string.IsNullOrEmpty(audioUrl) || !string.IsNullOrEmpty(inlineBase64);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsAuthenticationError(long httpCode, string errorCode, string errorBody)
        {
            if (httpCode == 401)
                return true;
            string combined = ((errorCode ?? "") + " " + (errorBody ?? "")).ToLowerInvariant();
            return combined.Contains("invalidapikey") ||
                   combined.Contains("invalid_api_key") ||
                   combined.Contains("authenticationerror") ||
                   combined.Contains("unauthorized");
        }

        public static bool IsPermanentModelExhaustedError(long httpCode, string errorCode, string errorBody)
        {
            if (httpCode == 402 || httpCode == 404)
                return true;

            string combined = ((errorCode ?? "") + " " + (errorBody ?? "")).ToLowerInvariant();
            return combined.Contains("freetier") ||
                   combined.Contains("allocationquota") ||
                   combined.Contains("arrearage") ||
                   combined.Contains("quota") ||
                   combined.Contains("insufficient") ||
                   combined.Contains("exhausted") ||
                   combined.Contains("modelnotfound") ||
                   combined.Contains("model_not_found") ||
                   combined.Contains("invalidmodel") ||
                   combined.Contains("accessdenied") ||
                   combined.Contains("not supported") ||
                   combined.Contains("unsupported model");
        }

        public bool IsModelMarkedExhausted(string modelId) =>
            !string.IsNullOrWhiteSpace(modelId) && exhaustedModels.Contains(modelId);

        public void MarkModelExhausted(string modelId)
        {
            if (!string.IsNullOrWhiteSpace(modelId))
                exhaustedModels.Add(modelId);
        }

        public void ResetExhaustedModels()
        {
            exhaustedModels.Clear();
            LastError = "";
        }

        public void CancelActiveRequest()
        {
            if (activeWebRequest != null)
            {
                try { activeWebRequest.Abort(); } catch { }
                try { activeWebRequest.Dispose(); } catch { }
                activeWebRequest = null;
            }

            if (activeWsCts != null)
            {
                try { activeWsCts.Cancel(); } catch { }
                try { activeWsCts.Dispose(); } catch { }
                activeWsCts = null;
            }
        }

        /// <summary>
        /// Walks the Alibaba model chain (Qwen HTTP -> Sambert WebSocket) until one synthesizes valid audio.
        /// Never calls Gemini; if every model fails or is exhausted, output.Success remains false cleanly.
        /// </summary>
        public IEnumerator SynthesizeWithFailover(
            string apiKey,
            string workspaceId,
            string primaryModel,
            string voiceName,
            string text,
            bool isFrench,
            float speed,
            SynthesisOutput output)
        {
            if (output == null)
                yield break;

            output.Success = false;
            output.ModelUsed = "";
            output.Clip = null;
            output.WavBytesForDiskCache = null;
            output.Error = "";
            output.AuthInvalid = false;

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(text))
            {
                output.Error = "Missing Alibaba API key or text.";
                LastError = output.Error;
                yield break;
            }

            List<AlibabaTtsModelSpec> chain = BuildModelChain(primaryModel, voiceName, isFrench);
            string httpEndpoint = ResolveHttpEndpoint(workspaceId);
            string wsEndpoint = ResolveWsEndpoint(workspaceId);
            string lastAttemptError = "All Alibaba TTS models exhausted or unavailable.";

            for (int i = 0; i < chain.Count; i++)
            {
                AlibabaTtsModelSpec spec = chain[i];
                if (exhaustedModels.Contains(spec.ModelId))
                    continue;

                if (spec.Protocol == AlibabaTtsProtocol.QwenHttp)
                {
                    yield return TryQwenHttpModel(httpEndpoint, apiKey, workspaceId, spec, text, isFrench, output);
                }
                else
                {
                    yield return TrySambertWebSocketModel(wsEndpoint, apiKey, workspaceId, spec, text, speed, output);
                }

                if (output.Success && output.Clip != null)
                {
                    ActiveModelId = spec.ModelId;
                    LastError = "";
                    yield break;
                }

                if (!string.IsNullOrWhiteSpace(output.Error))
                    lastAttemptError = $"{spec.ModelId}: {output.Error}";

                if (output.AuthInvalid)
                {
                    LastError = lastAttemptError;
                    Debug.LogWarning("[AlibabaTTS] Invalid Alibaba DashScope API key; stopping failover chain.");
                    yield break;
                }
            }

            output.Error = lastAttemptError;
            LastError = lastAttemptError;
            Debug.LogWarning("[AlibabaTTS] All Alibaba Qwen/Sambert backup models failed or exhausted quota; Fox will display text silently. Last error: " + lastAttemptError);
        }

        IEnumerator TryQwenHttpModel(
            string endpoint,
            string apiKey,
            string workspaceId,
            AlibabaTtsModelSpec spec,
            string text,
            bool isFrench,
            SynthesisOutput output)
        {
            output.Success = false;
            output.Error = "";

            string payload = BuildQwenHttpPayload(spec, text, isFrench);
            string audioUrl = "";
            string inlineBase64 = "";

            using (var request = new UnityWebRequest(endpoint, "POST"))
            {
                activeWebRequest = request;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
                if (!string.IsNullOrWhiteSpace(workspaceId))
                    request.SetRequestHeader("X-DashScope-WorkSpace", workspaceId.Trim());
                request.timeout = 14;

                yield return request.SendWebRequest();
                if (activeWebRequest == request)
                    activeWebRequest = null;

                long code = request.responseCode;
                string body = request.downloadHandler != null ? request.downloadHandler.text : "";

                if (request.result != UnityWebRequest.Result.Success)
                {
                    TryParseQwenHttpResponse(body, out _, out _, out string errCode, out string errMsg);
                    string detail = !string.IsNullOrWhiteSpace(errMsg) ? $"{errCode} ({errMsg})" : $"{code} {request.error}";
                    output.Error = detail;

                    if (IsAuthenticationError(code, errCode, body))
                    {
                        output.AuthInvalid = true;
                        yield break;
                    }

                    if (IsPermanentModelExhaustedError(code, errCode, body))
                    {
                        exhaustedModels.Add(spec.ModelId);
                        Debug.LogWarning($"[AlibabaTTS] Model '{spec.ModelId}' quota exhausted or unavailable ({detail}); switching to next backup.");
                    }
                    else
                    {
                        Debug.LogWarning($"[AlibabaTTS] Model '{spec.ModelId}' transient failure ({detail}); trying next backup.");
                    }
                    yield break;
                }

                if (!TryParseQwenHttpResponse(body, out audioUrl, out inlineBase64, out string respErrCode, out string respErrMsg))
                {
                    string detail = !string.IsNullOrWhiteSpace(respErrMsg) ? $"{respErrCode} ({respErrMsg})" : "No audio URL or inline data in response.";
                    output.Error = detail;
                    if (IsAuthenticationError(code, respErrCode, body))
                    {
                        output.AuthInvalid = true;
                        yield break;
                    }
                    if (IsPermanentModelExhaustedError(code, respErrCode, body))
                        exhaustedModels.Add(spec.ModelId);
                    Debug.LogWarning($"[AlibabaTTS] Model '{spec.ModelId}' returned no audio ({detail}); trying next backup.");
                    yield break;
                }
            }

            // Case 1: Inline base64 audio payload (0 extra HTTP download)
            if (!string.IsNullOrEmpty(inlineBase64))
            {
                try
                {
                    byte[] rawBytes = Convert.FromBase64String(inlineBase64);
                    if (TryBuildClipAndWav(rawBytes, "audio/L16;codec=pcm;rate=24000", $"Alibaba_{spec.ModelId}", out AudioClip inlineClip, out byte[] wavBytes))
                    {
                        output.Success = true;
                        output.ModelUsed = spec.ModelId;
                        output.Clip = inlineClip;
                        output.WavBytesForDiskCache = wavBytes;
                        yield break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AlibabaTTS] Inline audio decode failed for '{spec.ModelId}': {ex.Message}");
                }
            }

            // Case 2: Signed OSS audio download URL (upgraded to HTTPS for Android / Quest 2)
            if (string.IsNullOrEmpty(audioUrl))
            {
                output.Error = "Empty audio URL.";
                yield break;
            }

            byte[] downloadedBytes = null;
            string contentType = "";

            using (var dl = UnityWebRequest.Get(audioUrl))
            {
                activeWebRequest = dl;
                dl.timeout = 14;
                yield return dl.SendWebRequest();
                if (activeWebRequest == dl)
                    activeWebRequest = null;

                if (dl.result != UnityWebRequest.Result.Success)
                {
                    output.Error = $"Audio download failed ({dl.responseCode}): {dl.error}";
                    Debug.LogWarning($"[AlibabaTTS] Download failed for '{spec.ModelId}': {output.Error}");
                    yield break;
                }

                downloadedBytes = dl.downloadHandler?.data;
                contentType = dl.GetResponseHeader("Content-Type") ?? "";
            }

            if (downloadedBytes == null || downloadedBytes.Length < 44)
            {
                output.Error = "Downloaded audio was empty.";
                yield break;
            }

            bool isWav = downloadedBytes[0] == (byte)'R' && downloadedBytes[1] == (byte)'I' &&
                         downloadedBytes[2] == (byte)'F' && downloadedBytes[3] == (byte)'F';

            if (isWav)
            {
                if (TryBuildClipAndWav(downloadedBytes, "audio/wav", $"Alibaba_{spec.ModelId}", out AudioClip wavClip, out byte[] wavForDisk))
                {
                    output.Success = true;
                    output.ModelUsed = spec.ModelId;
                    output.Clip = wavClip;
                    output.WavBytesForDiskCache = wavForDisk;
                    yield break;
                }
            }

            // Fallback decoder if an Alibaba model returns MP3/MPEG instead of WAV
            bool looksMp3 = contentType.IndexOf("mpeg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            contentType.IndexOf("mp3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            audioUrl.IndexOf(".mp3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (downloadedBytes[0] == (byte)'I' && downloadedBytes[1] == (byte)'D' && downloadedBytes[2] == (byte)'3') ||
                            (downloadedBytes[0] == 0xFF && (downloadedBytes[1] & 0xE0) == 0xE0);

            if (looksMp3)
            {
                using (var mp3Req = UnityWebRequestMultimedia.GetAudioClip(audioUrl, AudioType.MPEG))
                {
                    activeWebRequest = mp3Req;
                    mp3Req.timeout = 14;
                    yield return mp3Req.SendWebRequest();
                    if (activeWebRequest == mp3Req)
                        activeWebRequest = null;

                    if (mp3Req.result == UnityWebRequest.Result.Success)
                    {
                        AudioClip mp3Clip = DownloadHandlerAudioClip.GetContent(mp3Req);
                        if (mp3Clip != null && mp3Clip.samples > 0)
                        {
                            mp3Clip.name = $"Alibaba_{spec.ModelId}";
                            output.Success = true;
                            output.ModelUsed = spec.ModelId;
                            output.Clip = mp3Clip;
                            output.WavBytesForDiskCache = ExtractWavBytesFromClip(mp3Clip);
                            yield break;
                        }
                    }
                }
            }

            output.Error = $"Unsupported audio format returned by '{spec.ModelId}' ({contentType}).";
        }

        sealed class WsSynthesisResult
        {
            public byte[] PcmBytes;
            public string ErrorCode;
            public string ErrorMessage;
            public bool AuthInvalid;
            public bool PermanentExhausted;
        }

        IEnumerator TrySambertWebSocketModel(
            string wsEndpoint,
            string apiKey,
            string workspaceId,
            AlibabaTtsModelSpec spec,
            string text,
            float speed,
            SynthesisOutput output)
        {
            output.Success = false;
            output.Error = "";

            const int SambertSampleRate = 16000;
            activeWsCts?.Cancel();
            activeWsCts?.Dispose();
            activeWsCts = new CancellationTokenSource(TimeSpan.FromSeconds(14));
            CancellationToken ct = activeWsCts.Token;

            string taskId = Guid.NewGuid().ToString("N");
            string runTaskJson = BuildSambertRunTaskPayload(taskId, spec.ModelId, text, speed, SambertSampleRate);

            Task<WsSynthesisResult> wsTask = Task.Run(
                () => RunSambertWebSocketAsync(wsEndpoint, apiKey, workspaceId, runTaskJson, ct),
                ct);

            while (!wsTask.IsCompleted)
                yield return null;

            if (wsTask.IsCanceled)
            {
                output.Error = "WebSocket request cancelled or timed out.";
                yield break;
            }

            if (wsTask.IsFaulted)
            {
                string exMsg = wsTask.Exception?.GetBaseException()?.Message ?? "Unknown WebSocket error";
                output.Error = exMsg;
                if (IsAuthenticationError(0, "", exMsg))
                    output.AuthInvalid = true;
                else if (IsPermanentModelExhaustedError(0, "", exMsg))
                    exhaustedModels.Add(spec.ModelId);
                Debug.LogWarning($"[AlibabaTTS] Sambert '{spec.ModelId}' WebSocket fault ({exMsg}); trying next backup.");
                yield break;
            }

            WsSynthesisResult res = wsTask.Result;
            if (res == null || res.PcmBytes == null || res.PcmBytes.Length < 64)
            {
                string detail = !string.IsNullOrWhiteSpace(res?.ErrorMessage)
                    ? $"{res.ErrorCode} ({res.ErrorMessage})"
                    : "Sambert returned empty PCM stream.";
                output.Error = detail;

                if (res != null && res.AuthInvalid)
                {
                    output.AuthInvalid = true;
                    yield break;
                }

                if (res != null && res.PermanentExhausted)
                {
                    exhaustedModels.Add(spec.ModelId);
                    Debug.LogWarning($"[AlibabaTTS] Sambert '{spec.ModelId}' exhausted/unavailable ({detail}); switching to next backup.");
                }
                else
                {
                    Debug.LogWarning($"[AlibabaTTS] Sambert '{spec.ModelId}' failed ({detail}); trying next backup.");
                }
                yield break;
            }

            string mime = $"audio/L16;codec=pcm;rate={SambertSampleRate}";
            if (TryBuildClipAndWav(res.PcmBytes, mime, $"Alibaba_{spec.ModelId}", out AudioClip clip, out byte[] wavBytes))
            {
                output.Success = true;
                output.ModelUsed = spec.ModelId;
                output.Clip = clip;
                output.WavBytesForDiskCache = wavBytes;
            }
            else
            {
                output.Error = "Failed to decode Sambert PCM audio.";
            }
        }

        static async Task<WsSynthesisResult> RunSambertWebSocketAsync(
            string wsEndpoint,
            string apiKey,
            string workspaceId,
            string runTaskJson,
            CancellationToken ct)
        {
            var result = new WsSynthesisResult();

            using (var ws = new ClientWebSocket())
            using (var pcmBuffer = new MemoryStream(32768))
            {
                ws.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
                if (!string.IsNullOrWhiteSpace(workspaceId))
                    ws.Options.SetRequestHeader("X-DashScope-WorkSpace", workspaceId.Trim());

                try
                {
                    await ws.ConnectAsync(new Uri(wsEndpoint), ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                    result.AuthInvalid = IsAuthenticationError(0, "", ex.Message) ||
                                         ex.Message.IndexOf("401", StringComparison.Ordinal) >= 0;
                    result.PermanentExhausted = IsPermanentModelExhaustedError(0, "", ex.Message);
                    return result;
                }

                byte[] sendBytes = Encoding.UTF8.GetBytes(runTaskJson);
                await ws.SendAsync(new ArraySegment<byte>(sendBytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

                byte[] recvChunk = new byte[16384];
                var textAccumulator = new MemoryStream(1024);

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult frame = await ws.ReceiveAsync(new ArraySegment<byte>(recvChunk), ct).ConfigureAwait(false);
                    if (frame.MessageType == WebSocketMessageType.Close)
                        break;

                    if (frame.MessageType == WebSocketMessageType.Binary)
                    {
                        if (frame.Count > 0)
                            pcmBuffer.Write(recvChunk, 0, frame.Count);
                        continue;
                    }

                    if (frame.MessageType == WebSocketMessageType.Text)
                    {
                        if (frame.Count > 0)
                            textAccumulator.Write(recvChunk, 0, frame.Count);

                        if (!frame.EndOfMessage)
                            continue;

                        string msgText = Encoding.UTF8.GetString(textAccumulator.ToArray());
                        textAccumulator.SetLength(0);

                        WsEventEnvelope env = null;
                        try { env = JsonUtility.FromJson<WsEventEnvelope>(msgText); } catch { }
                        string evt = env?.header?.@event ?? "";

                        if (string.Equals(evt, "task-finished", StringComparison.OrdinalIgnoreCase))
                            break;

                        if (string.Equals(evt, "task-failed", StringComparison.OrdinalIgnoreCase))
                        {
                            result.ErrorCode = env?.header?.error_code ?? "task-failed";
                            result.ErrorMessage = env?.header?.error_message ?? msgText;
                            result.AuthInvalid = IsAuthenticationError(0, result.ErrorCode, result.ErrorMessage);
                            result.PermanentExhausted = IsPermanentModelExhaustedError(0, result.ErrorCode, result.ErrorMessage);
                            break;
                        }
                    }
                }

                if (ws.State == WebSocketState.Open)
                {
                    try
                    {
                        using (var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", closeCts.Token).ConfigureAwait(false);
                    }
                    catch { }
                }

                if (pcmBuffer.Length >= 64)
                    result.PcmBytes = pcmBuffer.ToArray();
            }

            return result;
        }

        static bool TryBuildClipAndWav(byte[] rawBytes, string mimeType, string clipName, out AudioClip clip, out byte[] wavBytes)
        {
            clip = RestaurantVoiceBridge.DecodeGeminiAudio(rawBytes, mimeType, clipName, out float[] samples, out int rate);
            if (clip == null || samples == null || samples.Length == 0 || rate <= 0)
            {
                wavBytes = null;
                return false;
            }

            bool isAlreadyRiffWav = rawBytes.Length >= 44 &&
                                    rawBytes[0] == (byte)'R' && rawBytes[1] == (byte)'I' &&
                                    rawBytes[2] == (byte)'F' && rawBytes[3] == (byte)'F';
            wavBytes = isAlreadyRiffWav ? rawBytes : RestaurantVoiceBridge.EncodeWav(samples, clip.channels, rate);
            return true;
        }

        static byte[] ExtractWavBytesFromClip(AudioClip clip)
        {
            if (clip == null || clip.samples <= 0)
                return null;
            try
            {
                float[] data = new float[clip.samples * clip.channels];
                if (!clip.GetData(data, 0))
                    return null;
                return RestaurantVoiceBridge.EncodeWav(data, clip.channels, clip.frequency);
            }
            catch
            {
                return null;
            }
        }
    }
}
