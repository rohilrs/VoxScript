// VoxScript.Native/Whisper/WhisperBackend.cs
using System.Runtime.InteropServices;
using Serilog;
using VoxScript.Core.Transcription.Core;

namespace VoxScript.Native.Whisper;

public sealed class WhisperBackend : IWhisperBackend, ILocalTranscriptionBackend, IDisposable
{
    private IntPtr _ctx = IntPtr.Zero;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SileroVadDetector _vad = new();
    private bool _disposed;

    public bool IsModelLoaded => _ctx != IntPtr.Zero;
    public bool IsVadLoaded => _vad.IsLoaded;

    public void LoadVadModel(string onnxPath) => _vad.LoadModel(onnxPath);

    public async Task LoadModelAsync(string modelPath, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_ctx != IntPtr.Zero)
            {
                WhisperNativeMethods.whisper_free(_ctx);
                _ctx = IntPtr.Zero;
            }
            _ctx = await Task.Run(() => WhisperNativeMethods.whisper_init_from_file(modelPath), ct);
            if (_ctx == IntPtr.Zero)
                throw new InvalidOperationException($"whisper_init_from_file returned null for: {modelPath}");

            var sysInfo = Marshal.PtrToStringUTF8(WhisperNativeMethods.whisper_print_system_info());
            Log.Information("Whisper CPU features: {SystemInfo}", sysInfo);

            // Enumerate ggml backends to confirm GPU acceleration
            try
            {
                var devCount = WhisperNativeMethods.ggml_backend_dev_count();
                for (nuint i = 0; i < devCount; i++)
                {
                    var dev = WhisperNativeMethods.ggml_backend_dev_get(i);
                    var name = Marshal.PtrToStringUTF8(WhisperNativeMethods.ggml_backend_dev_name(dev));
                    var desc = Marshal.PtrToStringUTF8(WhisperNativeMethods.ggml_backend_dev_description(dev));
                    Log.Information("ggml backend [{Index}]: {Name} — {Description}", i, name, desc);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not enumerate ggml backends");
            }
        }
        finally { _gate.Release(); }
    }

    public void UnloadModel()
    {
        if (_ctx != IntPtr.Zero)
        {
            WhisperNativeMethods.whisper_free(_ctx);
            _ctx = IntPtr.Zero;
        }
    }

    public async Task<string> TranscribeAsync(float[] samples, string? language,
        string? initialPrompt, CancellationToken ct)
    {
        if (!IsModelLoaded) throw new InvalidOperationException("Whisper model not loaded.");

        await _gate.WaitAsync(ct);
        try
        {
            return await Task.Run(() => RunInference(samples, language, initialPrompt), ct);
        }
        finally { _gate.Release(); }
    }

    private string RunInference(float[] rawSamples, string? language, string? initialPrompt)
    {
        // Run VAD to strip silence if model is loaded
        var samples = _vad.IsLoaded ? _vad.ExtractSpeech(rawSamples) : rawSamples;
        if (samples.Length == 0) return string.Empty;

        // Get default params as a heap-allocated pointer (avoids struct marshaling issues)
        IntPtr pParams = WhisperNativeMethods.whisper_full_default_params_by_ref(0 /* GREEDY */);
        if (pParams == IntPtr.Zero)
            throw new InvalidOperationException("whisper_full_default_params_by_ref returned null");

        try
        {
            // Set fields by writing directly to known offsets
            int nThreads = Math.Min(Environment.ProcessorCount, 8);
            Marshal.WriteInt32(pParams, WhisperNativeMethods.ParamOffsets.NThreads, nThreads);
            Marshal.WriteByte(pParams, WhisperNativeMethods.ParamOffsets.NoTimestamps, 1);
            Marshal.WriteByte(pParams, WhisperNativeMethods.ParamOffsets.PrintProgress, 0);
            Marshal.WriteByte(pParams, WhisperNativeMethods.ParamOffsets.PrintRealtime, 0);

            int ret = WhisperNativeMethods.whisper_full(_ctx, pParams, samples, samples.Length);
            if (ret != 0) throw new InvalidOperationException($"whisper_full failed with code {ret}");
        }
        finally
        {
            WhisperNativeMethods.whisper_free_params(pParams);
        }

        int nSegments = WhisperNativeMethods.whisper_full_n_segments(_ctx);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < nSegments; i++)
        {
            IntPtr ptr = WhisperNativeMethods.whisper_full_get_segment_text(_ctx, i);
            sb.Append(Marshal.PtrToStringUTF8(ptr));
        }
        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            UnloadModel();
            _vad.Dispose();
            _gate.Dispose();
            _disposed = true;
        }
    }
}
