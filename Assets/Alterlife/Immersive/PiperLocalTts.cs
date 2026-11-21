using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Alterlife.Immersive
{
    /// <summary>
    /// On-device neural TTS powered by Sherpa-ONNX + Piper VITS INT8 models for the Fox Companion:
    /// - French pronunciation modeling: vits-piper-fr_FR-siwis-medium (INT8)
    /// - English tutor explanations: vits-piper-en_US-amy-low (INT8)
    /// Runs extraction, initialization, and waveform generation off the main XR render thread.
    /// </summary>
    public sealed class PiperLocalTts : IDisposable
    {
        const string ArchiveFileName = "piper-fox-voices.zip";
        const string ExtractedFolderName = "piper-fox-voices-v1";
        const string FrenchModelFile = "fr_FR-siwis-medium.onnx";
        const string FrenchTokensFile = "fr_FR-siwis-medium.tokens.txt";
        const string EnglishModelFile = "en_US-amy-low.onnx";
        const string EnglishTokensFile = "en_US-amy-low.tokens.txt";
        const string EspeakDirName = "espeak-ng-data";
        const string DllName = "sherpa-onnx-c-api";

        public struct AudioResult
        {
            public float[] samples;
            public int sampleRate;
        }

        readonly object engineLock = new object();
        static readonly object installLock = new object();

        string extractedRoot;
        Task<bool> activePrepareTask;
        volatile bool disposed;

        public bool Ready { get; private set; }
        public bool IsInitializing => activePrepareTask != null && !activePrepareTask.IsCompleted;
        public string LastError { get; private set; } = "";

        /// <summary>
        /// Ensures Piper models are extracted into persistentDataPath.
        /// Safe against StopAllCoroutines(): download and extraction completion run via
        /// AsyncOperation.completed + TaskCompletionSource so cancelling a caller coroutine
        /// can never strand IsInitializing=true.
        /// </summary>
        public IEnumerator Prepare()
        {
            if (Ready || disposed)
                yield break;

            string root = GetOrCacheRootPath();
            if (AreModelFilesPresent(root))
            {
                Ready = true;
                LastError = "";
                yield break;
            }

            Task<bool> task = GetOrStartPrepareTask(root);
            while (task != null && !task.IsCompleted && !disposed)
                yield return null;

            if (Ready)
                Debug.Log("[PiperLocalTts] Ready (on-demand VITS INT8: 0 MB idle native RAM).");
        }

        public void BeginPrepareNonBlocking()
        {
            if (Ready || disposed)
                return;

            string root = GetOrCacheRootPath();
            if (AreModelFilesPresent(root))
            {
                Ready = true;
                LastError = "";
                return;
            }

            GetOrStartPrepareTask(root);
        }

        Task<bool> GetOrStartPrepareTask(string root)
        {
            if (activePrepareTask != null && !activePrepareTask.IsCompleted)
                return activePrepareTask;

            string directStreamingPath = Path.Combine(Application.streamingAssetsPath, ArchiveFileName);
            if (!Application.streamingAssetsPath.Contains("://") && File.Exists(directStreamingPath))
            {
                activePrepareTask = Task.Run(() => RunExtraction(root, directStreamingPath, null));
                return activePrepareTask;
            }

            string streamingUrl = Application.streamingAssetsPath + "/" + ArchiveFileName;
            if (!streamingUrl.Contains("://"))
                streamingUrl = "file://" + streamingUrl;

            string tempArchive = Path.Combine(Application.temporaryCachePath, "piper_" + Guid.NewGuid().ToString("N") + ".zip");
            var tcs = new TaskCompletionSource<bool>();
            activePrepareTask = tcs.Task;

            var request = UnityWebRequest.Get(streamingUrl);
            request.downloadHandler = new DownloadHandlerFile(tempArchive) { removeFileOnAbort = true };
            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                bool ok = request.result == UnityWebRequest.Result.Success;
                string err = request.error;
                request.Dispose();

                if (!ok || disposed)
                {
                    if (File.Exists(tempArchive)) { try { File.Delete(tempArchive); } catch { } }
                    LastError = "Download failed: " + err;
                    Debug.LogWarning("[PiperLocalTts] Could not load " + ArchiveFileName + " from StreamingAssets: " + err);
                    tcs.TrySetResult(false);
                    return;
                }

                Task.Run(() =>
                {
                    bool extracted = RunExtraction(root, tempArchive, tempArchive);
                    tcs.TrySetResult(extracted);
                });
            };

            return activePrepareTask;
        }

        bool RunExtraction(string root, string sourceArchivePath, string tempArchiveToDelete)
        {
            try
            {
                ExtractArchiveIfNeeded(root, sourceArchivePath);
                if (!disposed && AreModelFilesPresent(root))
                {
                    Ready = true;
                    LastError = "";
                    return true;
                }
                LastError = "Model files missing after extraction at " + root;
                Debug.LogWarning("[PiperLocalTts] " + LastError);
                return false;
            }
            catch (Exception ex)
            {
                LastError = "Extraction failed: " + ex.Message;
                Debug.LogWarning("[PiperLocalTts] " + LastError);
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempArchiveToDelete) && File.Exists(tempArchiveToDelete))
                {
                    try { File.Delete(tempArchiveToDelete); } catch { }
                }
            }
        }

        string GetOrCacheRootPath()
        {
            if (string.IsNullOrEmpty(extractedRoot))
                extractedRoot = Path.Combine(Application.persistentDataPath, ExtractedFolderName);
            return extractedRoot;
        }

        static bool AreModelFilesPresent(string root)
        {
            if (string.IsNullOrEmpty(root)) return false;
            return File.Exists(Path.Combine(root, FrenchModelFile)) &&
                   File.Exists(Path.Combine(root, FrenchTokensFile)) &&
                   File.Exists(Path.Combine(root, EnglishModelFile)) &&
                   File.Exists(Path.Combine(root, EnglishTokensFile)) &&
                   Directory.Exists(Path.Combine(root, EspeakDirName));
        }

        static void ExtractArchiveIfNeeded(string root, string sourceArchivePath)
        {
            lock (installLock)
            {
                if (!AreModelFilesPresent(root) && !string.IsNullOrEmpty(sourceArchivePath) && File.Exists(sourceArchivePath))
                {
                    string staging = root + ".tmp_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    ZipFile.ExtractToDirectory(sourceArchivePath, staging);
                    if (Directory.Exists(root)) Directory.Delete(root, true);
                    Directory.Move(staging, root);
                }
            }
        }

        static IntPtr CreateVitsHandle(string modelPath, string tokensPath, string dataDir)
        {
            var config = new OfflineTtsConfig
            {
                Model = new OfflineTtsModelConfig
                {
                    Vits = new OfflineTtsVitsModelConfig
                    {
                        Model = modelPath,
                        Lexicon = "",
                        Tokens = tokensPath,
                        DataDir = dataDir,
                        NoiseScale = 0.667f,
                        NoiseScaleW = 0.8f,
                        LengthScale = 1.0f,
                        DictDir = ""
                    },
                    NumThreads = 1,
                    Debug = 0,
                    Provider = "cpu",
                    Matcha = OfflineTtsMatchaModelConfig.CreateEmpty(),
                    Kokoro = OfflineTtsKokoroModelConfig.CreateEmpty(),
                    Kitten = OfflineTtsKittenModelConfig.CreateEmpty(),
                    ZipVoice = OfflineTtsZipVoiceModelConfig.CreateEmpty(),
                    Pocket = OfflineTtsPocketModelConfig.CreateEmpty(),
                    Supertonic = OfflineTtsSupertonicModelConfig.CreateEmpty()
                },
                RuleFsts = "",
                MaxNumSentences = 1,
                RuleFars = "",
                SilenceScale = 0.2f
            };

            return SherpaOnnxCreateOfflineTts(ref config);
        }

        /// <summary>
        /// Synthesizes speech on demand by loading ONLY the requested language model (FR or EN),
        /// generating the waveform, and immediately destroying the native ONNX session so ~250+ MB
        /// of ONNX Runtime BFCArena activation buffers are freed back to the OS right away.
        /// </summary>
        public Task<AudioResult> SynthesizeAsync(string text, bool isFrench, float speed = 1.0f)
        {
            string root = GetOrCacheRootPath();
            float clampedSpeed = Mathf.Clamp(speed, 0.55f, 1.45f);
            return Task.Run(() =>
            {
                if (disposed || string.IsNullOrWhiteSpace(text))
                    return default;

                lock (engineLock)
                {
                    if (disposed || !Ready || !AreModelFilesPresent(root))
                    {
                        Debug.LogWarning($"[PiperLocalTts] Skip synthesis: disposed={disposed}, ready={Ready}, filesPresent={AreModelFilesPresent(root)}");
                        return default;
                    }

                    string modelPath = Path.Combine(root, isFrench ? FrenchModelFile : EnglishModelFile);
                    string tokensPath = Path.Combine(root, isFrench ? FrenchTokensFile : EnglishTokensFile);
                    string espeakPath = Path.Combine(root, EspeakDirName);

                    IntPtr handle = IntPtr.Zero;
                    try
                    {
                        handle = CreateVitsHandle(modelPath, tokensPath, espeakPath);
                    }
                    catch (Exception ex)
                    {
                        LastError = "CreateVitsHandle exception: " + ex.Message;
                        Debug.LogError("[PiperLocalTts] " + LastError);
                        return default;
                    }

                    if (handle == IntPtr.Zero)
                    {
                        LastError = "SherpaOnnxCreateOfflineTts returned null handle for " + modelPath;
                        Debug.LogError("[PiperLocalTts] " + LastError);
                        return default;
                    }

                    try
                    {
                        byte[] utf8 = Encoding.UTF8.GetBytes(text.Trim());
                        byte[] nullTerminated = new byte[utf8.Length + 1];
                        Buffer.BlockCopy(utf8, 0, nullTerminated, 0, utf8.Length);
                        nullTerminated[utf8.Length] = 0;

                        IntPtr audioPtr = SherpaOnnxOfflineTtsGenerate(handle, nullTerminated, 0, clampedSpeed);
                        if (audioPtr == IntPtr.Zero)
                        {
                            LastError = "SherpaOnnxOfflineTtsGenerate returned null audio pointer.";
                            Debug.LogWarning("[PiperLocalTts] " + LastError);
                            return default;
                        }

                        try
                        {
                            GeneratedAudioImpl impl = Marshal.PtrToStructure<GeneratedAudioImpl>(audioPtr);
                            if (impl.NumSamples <= 0 || impl.Samples == IntPtr.Zero || impl.SampleRate <= 0)
                            {
                                LastError = $"Empty Piper audio output (samples={impl.NumSamples}, rate={impl.SampleRate}).";
                                Debug.LogWarning("[PiperLocalTts] " + LastError);
                                return default;
                            }

                            float[] pcm = new float[impl.NumSamples];
                            Marshal.Copy(impl.Samples, pcm, 0, impl.NumSamples);
                            return new AudioResult { samples = pcm, sampleRate = impl.SampleRate };
                        }
                        finally
                        {
                            SherpaOnnxDestroyOfflineTtsGeneratedAudio(audioPtr);
                        }
                    }
                    catch (Exception ex)
                    {
                        LastError = "Synthesis exception: " + ex.Message;
                        Debug.LogError("[PiperLocalTts] " + LastError);
                        return default;
                    }
                    finally
                    {
                        SherpaOnnxDestroyOfflineTts(handle);
                    }
                }
            });
        }

        public void Dispose()
        {
            disposed = true;
            lock (engineLock)
            {
                Ready = false;
            }
        }

        // =====================================================================
        // Sherpa-ONNX v1.13.8 C-API P/Invoke Structs
        // =====================================================================

        [StructLayout(LayoutKind.Sequential)]
        struct GeneratedAudioImpl
        {
            public IntPtr Samples;
            public int NumSamples;
            public int SampleRate;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OfflineTtsVitsModelConfig
        {
            [MarshalAs(UnmanagedType.LPStr)] public string Model;
            [MarshalAs(UnmanagedType.LPStr)] public string Lexicon;
            [MarshalAs(UnmanagedType.LPStr)] public string Tokens;
            [MarshalAs(UnmanagedType.LPStr)] public string DataDir;
            public float NoiseScale;
            public float NoiseScaleW;
            public float LengthScale;
            [MarshalAs(UnmanagedType.LPStr)] public string DictDir;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OfflineTtsMatchaModelConfig
        {
            [MarshalAs(UnmanagedType.LPStr)] public string AcousticModel;
            [MarshalAs(UnmanagedType.LPStr)] public string Vocoder;
            [MarshalAs(UnmanagedType.LPStr)] public string Lexicon;
            [MarshalAs(UnmanagedType.LPStr)] public string Tokens;
            [MarshalAs(UnmanagedType.LPStr)] public string DataDir;
            public float NoiseScale;
            public float LengthScale;
            [MarshalAs(UnmanagedType.LPStr)] public string DictDir;

            public static OfflineTtsMatchaModelConfig CreateEmpty() => new OfflineTtsMatchaModelConfig
            {
                AcousticModel = "", Vocoder = "", Lexicon = "", Tokens = "", DataDir = "",
                NoiseScale = 0.667f, LengthScale = 1.0f, DictDir = ""
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OfflineTtsKokoroModelConfig
        {
            [MarshalAs(UnmanagedType.LPStr)] public string Model;
            [MarshalAs(UnmanagedType.LPStr)] public string Voices;
            [MarshalAs(UnmanagedType.LPStr)] public string Tokens;
            [MarshalAs(UnmanagedType.LPStr)] public string DataDir;
            public float LengthScale;
            [MarshalAs(UnmanagedType.LPStr)] public string DictDir;
            [MarshalAs(UnmanagedType.LPStr)] public string Lexicon;
            [MarshalAs(UnmanagedType.LPStr)] public string Lang;

            public static OfflineTtsKokoroModelConfig CreateEmpty() => new OfflineTtsKokoroModelConfig
            {
                Model = "", Voices = "", Tokens = "", DataDir = "", LengthScale = 1.0f,
                DictDir = "", Lexicon = "", Lang = ""
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OfflineTtsKittenModelConfig
        {
            [MarshalAs(UnmanagedType.LPStr)] public string Model;
            [MarshalAs(UnmanagedType.LPStr)] public string Voices;
            [MarshalAs(UnmanagedType.LPStr)] public string Tokens;
            [MarshalAs(UnmanagedType.LPStr)] public string DataDir;
            public float LengthScale;

            public static OfflineTtsKittenModelConfig CreateEmpty() => new OfflineTtsKittenModelConfig
            {
                Model = "", Voices = "", Tokens = "", DataDir = "", LengthScale = 1.0f
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OfflineTtsZipVoiceModelConfig
        {
            [MarshalAs(UnmanagedType.LPStr)] public string Tokens;
            [MarshalAs(UnmanagedType.LPStr)] public string Encoder;
            [MarshalAs(UnmanagedType.LPStr)] public string Decoder;
            [MarshalAs(UnmanagedType.LPStr)] public string Vocoder;
            [MarshalAs(UnmanagedType.LPStr)] public string DataDir;
            [MarshalAs(UnmanagedType.LPStr)] public string Lexicon;
            public float FeatScale;
            public float Tshift;
            public float TargetRms;
            public float GuidanceScale;

            public static OfflineTtsZipVoiceModelConfig CreateEmpty() => new OfflineTtsZipVoiceModelConfig
            {
                Tokens = "", Encoder = "", Decoder = "", Vocoder = "", DataDir = "", Lexicon = "",
                FeatScale = 0.1f, Tshift = 0.5f, TargetRms = 0.1f, GuidanceScale = 1.0f
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OfflineTtsPocketModelConfig
        {
            [MarshalAs(UnmanagedType.LPStr)] public string LmFlow;
            [MarshalAs(UnmanagedType.LPStr)] public string LmMain;
            [MarshalAs(UnmanagedType.LPStr)] public string Encoder;
            [MarshalAs(UnmanagedType.LPStr)] public string Decoder;
            [MarshalAs(UnmanagedType.LPStr)] public string TextConditioner;
            [MarshalAs(UnmanagedType.LPStr)] public string VocabJson;
            [MarshalAs(UnmanagedType.LPStr)] public string TokenScoresJson;
            public int VoiceEmbeddingCacheCapacity;

            public static OfflineTtsPocketModelConfig CreateEmpty() => new OfflineTtsPocketModelConfig
            {
                LmFlow = "", LmMain = "", Encoder = "", Decoder = "", TextConditioner = "",
                VocabJson = "", TokenScoresJson = "", VoiceEmbeddingCacheCapacity = 50
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OfflineTtsSupertonicModelConfig
        {
            [MarshalAs(UnmanagedType.LPStr)] public string DurationPredictor;
            [MarshalAs(UnmanagedType.LPStr)] public string TextEncoder;
            [MarshalAs(UnmanagedType.LPStr)] public string VectorEstimator;
            [MarshalAs(UnmanagedType.LPStr)] public string Vocoder;
            [MarshalAs(UnmanagedType.LPStr)] public string TtsJson;
            [MarshalAs(UnmanagedType.LPStr)] public string UnicodeIndexer;
            [MarshalAs(UnmanagedType.LPStr)] public string VoiceStyle;

            public static OfflineTtsSupertonicModelConfig CreateEmpty() => new OfflineTtsSupertonicModelConfig
            {
                DurationPredictor = "", TextEncoder = "", VectorEstimator = "", Vocoder = "",
                TtsJson = "", UnicodeIndexer = "", VoiceStyle = ""
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OfflineTtsModelConfig
        {
            public OfflineTtsVitsModelConfig Vits;
            public int NumThreads;
            public int Debug;
            [MarshalAs(UnmanagedType.LPStr)] public string Provider;
            public OfflineTtsMatchaModelConfig Matcha;
            public OfflineTtsKokoroModelConfig Kokoro;
            public OfflineTtsKittenModelConfig Kitten;
            public OfflineTtsZipVoiceModelConfig ZipVoice;
            public OfflineTtsPocketModelConfig Pocket;
            public OfflineTtsSupertonicModelConfig Supertonic;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OfflineTtsConfig
        {
            public OfflineTtsModelConfig Model;
            [MarshalAs(UnmanagedType.LPStr)] public string RuleFsts;
            public int MaxNumSentences;
            [MarshalAs(UnmanagedType.LPStr)] public string RuleFars;
            public float SilenceScale;
        }

        [DllImport(DllName)]
        static extern IntPtr SherpaOnnxCreateOfflineTts(ref OfflineTtsConfig config);

        [DllImport(DllName)]
        static extern void SherpaOnnxDestroyOfflineTts(IntPtr handle);

        [DllImport(DllName)]
        static extern IntPtr SherpaOnnxOfflineTtsGenerate(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I1)] byte[] utf8Text,
            int sid,
            float speed);

        [DllImport(DllName)]
        static extern void SherpaOnnxDestroyOfflineTtsGeneratedAudio(IntPtr handle);
    }
}
