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

        IntPtr frenchHandle;
        IntPtr englishHandle;
        volatile bool disposed;

        public bool Ready { get; private set; }
        public bool IsInitializing { get; private set; }

        public IEnumerator Prepare()
        {
            if (Ready || IsInitializing || disposed)
                yield break;

            IsInitializing = true;
            string root = Path.Combine(Application.persistentDataPath, ExtractedFolderName);
            string frModelPath = Path.Combine(root, FrenchModelFile);
            string enModelPath = Path.Combine(root, EnglishModelFile);
            string espeakPath = Path.Combine(root, EspeakDirName);

            string tempArchive = null;
            string sourceArchivePath = null;
            if (!File.Exists(frModelPath) || !File.Exists(enModelPath) || !Directory.Exists(espeakPath))
            {
                string directStreamingPath = Path.Combine(Application.streamingAssetsPath, ArchiveFileName);
                if (!Application.streamingAssetsPath.Contains("://") && File.Exists(directStreamingPath))
                {
                    sourceArchivePath = directStreamingPath;
                }
                else
                {
                    string streamingUrl = Application.streamingAssetsPath + "/" + ArchiveFileName;
                    if (!streamingUrl.Contains("://"))
                        streamingUrl = "file://" + streamingUrl;

                    tempArchive = Path.Combine(Application.temporaryCachePath, "piper_" + Guid.NewGuid().ToString("N") + ".zip");
                    using (var request = UnityWebRequest.Get(streamingUrl))
                    {
                        request.downloadHandler = new DownloadHandlerFile(tempArchive) { removeFileOnAbort = true };
                        var op = request.SendWebRequest();
                        while (!op.isDone)
                            yield return null;

                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            if (File.Exists(tempArchive)) { try { File.Delete(tempArchive); } catch { } }
                            Debug.LogWarning("[PiperLocalTts] Could not load " + ArchiveFileName + " from StreamingAssets: " + request.error);
                            IsInitializing = false;
                            yield break;
                        }
                    }
                    sourceArchivePath = tempArchive;
                }
            }

            var initTask = Task.Run(() =>
            {
                try
                {
                    lock (installLock)
                    {
                        if ((!File.Exists(frModelPath) || !File.Exists(enModelPath) || !Directory.Exists(espeakPath))
                            && !string.IsNullOrEmpty(sourceArchivePath) && File.Exists(sourceArchivePath))
                        {
                            string staging = root + ".tmp_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                            if (Directory.Exists(staging)) Directory.Delete(staging, true);
                            ZipFile.ExtractToDirectory(sourceArchivePath, staging);
                            if (Directory.Exists(root)) Directory.Delete(root, true);
                            Directory.Move(staging, root);
                        }
                    }

                    if (!File.Exists(frModelPath) || !File.Exists(enModelPath))
                        return;

                    string frTokensPath = Path.Combine(root, FrenchTokensFile);
                    string enTokensPath = Path.Combine(root, EnglishTokensFile);

                    IntPtr fr = CreateVitsHandle(frModelPath, frTokensPath, espeakPath);
                    IntPtr en = CreateVitsHandle(enModelPath, enTokensPath, espeakPath);

                    lock (engineLock)
                    {
                        if (disposed)
                        {
                            if (fr != IntPtr.Zero) SherpaOnnxDestroyOfflineTts(fr);
                            if (en != IntPtr.Zero) SherpaOnnxDestroyOfflineTts(en);
                            return;
                        }
                        frenchHandle = fr;
                        englishHandle = en;
                        Ready = fr != IntPtr.Zero && en != IntPtr.Zero;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[PiperLocalTts] Initialization failed: " + ex.Message);
                }
                finally
                {
                    if (tempArchive != null && File.Exists(tempArchive))
                    {
                        try { File.Delete(tempArchive); } catch { }
                    }
                }
            });

            while (!initTask.IsCompleted)
                yield return null;

            IsInitializing = false;
            if (Ready)
                Debug.Log("[PiperLocalTts] Ready (fr_FR-siwis-medium INT8 + en_US-amy-low INT8).");
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
                    NumThreads = 2,
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

        public Task<AudioResult> SynthesizeAsync(string text, bool isFrench, float speed = 1.0f)
        {
            return Task.Run(() =>
            {
                if (disposed || !Ready || string.IsNullOrWhiteSpace(text))
                    return default;

                lock (engineLock)
                {
                    if (disposed || !Ready)
                        return default;

                    IntPtr handle = isFrench ? frenchHandle : englishHandle;
                    if (handle == IntPtr.Zero)
                        return default;

                    byte[] utf8 = Encoding.UTF8.GetBytes(text.Trim());
                    byte[] nullTerminated = new byte[utf8.Length + 1];
                    Buffer.BlockCopy(utf8, 0, nullTerminated, 0, utf8.Length);
                    nullTerminated[utf8.Length] = 0;

                    IntPtr audioPtr = SherpaOnnxOfflineTtsGenerate(handle, nullTerminated, 0, speed);
                    if (audioPtr == IntPtr.Zero)
                        return default;

                    try
                    {
                        GeneratedAudioImpl impl = Marshal.PtrToStructure<GeneratedAudioImpl>(audioPtr);
                        if (impl.NumSamples <= 0 || impl.Samples == IntPtr.Zero || impl.SampleRate <= 0)
                            return default;

                        float[] pcm = new float[impl.NumSamples];
                        Marshal.Copy(impl.Samples, pcm, 0, impl.NumSamples);
                        return new AudioResult { samples = pcm, sampleRate = impl.SampleRate };
                    }
                    finally
                    {
                        SherpaOnnxDestroyOfflineTtsGeneratedAudio(audioPtr);
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
                if (frenchHandle != IntPtr.Zero)
                {
                    try { SherpaOnnxDestroyOfflineTts(frenchHandle); } catch { }
                    frenchHandle = IntPtr.Zero;
                }
                if (englishHandle != IntPtr.Zero)
                {
                    try { SherpaOnnxDestroyOfflineTts(englishHandle); } catch { }
                    englishHandle = IntPtr.Zero;
                }
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
