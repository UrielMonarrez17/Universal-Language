using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace Alterlife.Immersive
{
    /// <summary>Microphone/WAV adapter to an application-owned speech service. No provider secrets in the player.</summary>
    public sealed class RestaurantVoiceBridge : MonoBehaviour
    {
        [Tooltip("HTTPS backend base URL. /transcribe accepts WAV, /speak returns WAV, /chat returns JSON. No API keys here.")]
        public string serviceUrl;
        public RestaurantEvaluation evaluation;
        public CommunicativeEncounter encounter;
        public AudioSource speaker;
        public TMP_Text microphoneLabel;
        public bool IsConfigured => Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "https" || (uri.IsLoopback && uri.Scheme == "http"));
        public bool IsRecording { get; private set; }
        public bool IsBusy { get; private set; }
        public string LastTranscript { get; private set; }
        AudioClip recording;
        string device;
        float started;
        Coroutine speechRoutine;
        UnityWebRequest speechRequest, operationRequest;
        readonly List<Turn> history = new List<Turn>();
        [Serializable] class Transcript { public string text; }
        [Serializable] class SpokenLine { public string text; public string language = "fr-FR"; }
        [Serializable] class Turn { public string role; public string content; }
        [Serializable] class ChatRequest { public string language = "fr-FR"; public string scenario = "paris-restaurant"; public string dish; public string drink; public Turn[] history; }
        [Serializable] class ChatReply { public string reply; }

        void Start()
        {
            SetLabel(IsConfigured ? "START SPEAKING" : "VOICE OFFLINE\nUSE RESPONSE CARDS");
            if (IsConfigured) evaluation.Feedback("The waiter's voice is AI-generated. Select START SPEAKING to respond in French.");
        }
        public void ToggleRecording()
        {
            if (IsBusy) return;
            if (!IsConfigured) { evaluation.Feedback("Voice is unavailable offline. Select HELP to practice with response cards."); return; }
            if (IsRecording) FinishRecording();
            else if (speaker && speaker.isPlaying) evaluation.Feedback("Let the waiter finish, then speak.");
            else StartCoroutine(StartRecording());
        }
        IEnumerator StartRecording()
        {
            IsBusy = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                float deadline = Time.realtimeSinceStartup + 15;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                { Fail("Microphone permission is needed to speak. Response cards remain available."); yield break; }
            }
#else
            yield return null;
#endif
            if (Microphone.devices.Length == 0) { Fail("No microphone is available. Use the response cards to continue."); yield break; }
            device = Microphone.devices[0];
            recording = Microphone.Start(device, false, 12, 16000);
            if (!recording) { Fail("The microphone could not start. Please try again."); yield break; }
            started = Time.realtimeSinceStartup; IsRecording = true; IsBusy = false;
            SetLabel("FINISH SPEAKING"); evaluation.Feedback("Listening in French… Select FINISH SPEAKING when you are done.");
        }
        void Update() { if (IsRecording && Time.realtimeSinceStartup - started >= 11.5f) FinishRecording(); }
        public void FinishRecording()
        {
            if (!IsRecording) return;
            int samples = Microphone.GetPosition(device);
            Microphone.End(device); IsRecording = false;
            if (samples < 1600) { Destroy(recording); Fail("I could not hear enough speech. Please try again."); return; }
            var pcm = new float[samples * recording.channels]; recording.GetData(pcm, 0);
            var wav = EncodeWav(pcm, recording.channels, recording.frequency);
            Destroy(recording); recording = null;
            StartCoroutine(Transcribe(wav));
        }
        IEnumerator Transcribe(byte[] wav)
        {
            IsBusy = true; SetLabel("LISTENING TO YOUR ANSWER…");
            using (var request = new UnityWebRequest(serviceUrl.TrimEnd('/') + "/transcribe", "POST"))
            {
                operationRequest = request;
                request.uploadHandler = new UploadHandlerRaw(wav);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "audio/wav"); request.SetRequestHeader("X-Language", "fr-FR"); request.timeout = 25;
                yield return request.SendWebRequest(); operationRequest = null;
                if (request.result != UnityWebRequest.Result.Success) { Fail("I could not hear your answer. Try again or use HELP. No progress was lost."); yield break; }
                string text = null;
                try { text = JsonUtility.FromJson<Transcript>(request.downloadHandler.text)?.text; } catch (ArgumentException) { }
                IsBusy = false; SetLabel("START SPEAKING");
                LastTranscript = text;
                if (string.IsNullOrWhiteSpace(text)) { evaluation.Feedback("No clear speech was detected. Please try again."); yield break; }
                encounter.OnVoiceTranscriptionReceived(text);
            }
        }
        public void Speak(string text, AudioClip prerecorded = null)
        {
            if (!speaker || IsRecording) return;
            speaker.Stop();
            if (speechRequest != null) { speechRequest.Abort(); speechRequest.Dispose(); speechRequest = null; }
            if (speechRoutine != null) StopCoroutine(speechRoutine);
            if (prerecorded) { speaker.clip = prerecorded; speaker.Play(); return; }
            if (IsConfigured) speechRoutine = StartCoroutine(Synthesize(text));
        }
        IEnumerator Synthesize(string text)
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
                else evaluation.Feedback("The waiter's voice is unavailable. The French dialogue remains visible.");
            }
            speechRoutine = null;
        }
        public void Practice(string text)
        {
            if (!IsConfigured || IsBusy) return;
            StartCoroutine(Chat(text));
        }
        IEnumerator Chat(string text)
        {
            IsBusy = true;
            history.Add(new Turn { role = "user", content = text });
            if (history.Count > 10) history.RemoveRange(0, history.Count - 10);
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
                history.Add(new Turn { role = "assistant", content = reply });
                evaluation.ReceivePracticeReply(reply);
            }
        }
        void Fail(string message) { IsBusy = false; SetLabel("START SPEAKING"); evaluation.Feedback(message); }
        void SetLabel(string value) { if (microphoneLabel) microphoneLabel.text = value; }
        public void ResetConversation()
        {
            if (IsRecording) Microphone.End(device);
            IsRecording = IsBusy = false;
            operationRequest?.Abort(); speechRequest?.Abort();
            StopAllCoroutines();
            if (recording) Destroy(recording);
            recording = null; operationRequest = speechRequest = null; speechRoutine = null;
            history.Clear(); LastTranscript = null;
            if (speaker) speaker.Stop();
            SetLabel(IsConfigured ? "START SPEAKING" : "VOICE OFFLINE\nUSE RESPONSE CARDS");
        }
        void OnDisable() { ResetConversation(); }
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
