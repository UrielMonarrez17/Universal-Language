using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Alterlife.Immersive
{
    // One native model per active restaurant. Initialization and decoding run off the XR thread.
    internal sealed class FrenchSpeechRecognizer : IDisposable
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        public static bool Supported => true;
#else
        public static bool Supported => false;
#endif
        readonly string ModelName;
        public FrenchSpeechRecognizer(string modelName = "vosk-model-small-fr-0.22") { ModelName = modelName; }
        readonly object modelLock = new object();
        static readonly object installLock = new object();
        IntPtr model;
        volatile bool disposed;
        public bool Ready { get; private set; }

        [DllImport("vosk")] static extern IntPtr vosk_model_new(string path);
        [DllImport("vosk")] static extern void vosk_model_free(IntPtr model);
        [DllImport("vosk")] static extern IntPtr vosk_recognizer_new(IntPtr model, float sampleRate);
        [DllImport("vosk")] static extern void vosk_recognizer_free(IntPtr recognizer);
        [DllImport("vosk")] static extern int vosk_recognizer_accept_waveform(IntPtr recognizer, byte[] buffer, int bytes);
        [DllImport("vosk")] static extern IntPtr vosk_recognizer_result(IntPtr recognizer);
        [DllImport("vosk")] static extern IntPtr vosk_recognizer_final_result(IntPtr recognizer);
        [Serializable] class Result { public string text; }

        public IEnumerator Prepare()
        {
            string folder = Application.persistentDataPath;
            string root = Path.Combine(folder, ModelName);
            string archive = null;
            if (!Directory.Exists(root))
            {
                archive = Path.Combine(Application.temporaryCachePath, Guid.NewGuid() + ".zip");
                using (var request = UnityWebRequest.Get(Application.streamingAssetsPath + "/" + ModelName + ".zip"))
                {
                    request.downloadHandler = new DownloadHandlerFile(archive) { removeFileOnAbort = true };
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        if (File.Exists(archive)) File.Delete(archive);
                        Debug.LogWarning("French speech model could not be read from the APK.");
                        yield break;
                    }
                }
            }
            var work = Task.Run(() =>
            {
                try
                {
                    lock (installLock)
                    {
                        if (!Directory.Exists(root))
                        {
                            var staging = Path.Combine(folder, ModelName + ".installing");
                            try
                            {
                                ZipFile.ExtractToDirectory(archive, staging);
                                Directory.Move(Path.Combine(staging, ModelName), root);
                            }
                            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                        }
                    }
                    lock (modelLock)
                    {
                        if (!disposed) model = vosk_model_new(root);
                    }
                }
                finally { if (archive != null && File.Exists(archive)) File.Delete(archive); }
            });
            while (!work.IsCompleted) yield return null;
            Ready = !work.IsFaulted && !disposed && model != IntPtr.Zero;
            if (!Ready) Debug.LogWarning("French speech model initialization failed: " + work.Exception?.GetBaseException().Message);
        }

        public Task<string> Decode(byte[] pcm, int rate) => Task.Run(() =>
        {
            lock (modelLock)
            {
                if (disposed || model == IntPtr.Zero) return "";
                IntPtr recognizer = vosk_recognizer_new(model, rate);
                if (recognizer == IntPtr.Zero) return "";
                try
                {
                    int complete = vosk_recognizer_accept_waveform(recognizer, pcm, pcm.Length);
                    if (complete < 0) return "";
                    var result = complete == 1 ? vosk_recognizer_result(recognizer) : vosk_recognizer_final_result(recognizer);
                    return JsonUtility.FromJson<Result>(Marshal.PtrToStringAnsi(result))?.text ?? "";
                }
                finally { vosk_recognizer_free(recognizer); }
            }
        });

        public void Dispose()
        {
            disposed = true;
            Ready = false;
            Task.Run(() =>
            {
                lock (modelLock)
                {
                    if (model != IntPtr.Zero) vosk_model_free(model);
                    model = IntPtr.Zero;
                }
            });
        }
    }
}
