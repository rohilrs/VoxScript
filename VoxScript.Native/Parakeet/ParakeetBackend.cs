// VoxScript.Native/Parakeet/ParakeetBackend.cs
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Serilog;
using VoxScript.Core.Transcription.Core;

namespace VoxScript.Native.Parakeet;

/// <summary>
/// Runs Parakeet TDT inference via ONNX Runtime.
/// The ONNX model is exported from NeMo (nvidia/parakeet-tdt-0.6b-v2) using:
///   nemo_asr.export("parakeet.onnx") from nemo toolkit.
/// Preprocessing: 80-dim log-mel spectrogram (n_fft=512, hop=160, win=400).
/// Postprocessing: CTC/TDT greedy decode + SentencePiece BPE tokenizer.
/// </summary>
public sealed class ParakeetBackend : IParakeetBackend, ILocalTranscriptionBackend, IDisposable
{
    private InferenceSession? _session;
    private ParakeetTokenizer? _tokenizer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public bool IsModelLoaded => _session is not null;

    public async Task LoadModelAsync(string modelPath, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _session?.Dispose();
            var opts = new SessionOptions();
            opts.AppendExecutionProvider_DML(); // DirectML GPU
            _session = await Task.Run(() => new InferenceSession(modelPath, opts), ct);

            // Log model input/output metadata
            foreach (var input in _session.InputMetadata)
                Log.Information("Parakeet ONNX input: {Name} shape={Shape} type={Type}",
                    input.Key, string.Join(",", input.Value.Dimensions), input.Value.ElementDataType);
            foreach (var output in _session.OutputMetadata)
                Log.Information("Parakeet ONNX output: {Name} shape={Shape} type={Type}",
                    output.Key, string.Join(",", output.Value.Dimensions), output.Value.ElementDataType);

            // Load SentencePiece tokenizer (co-located .model file)
            var tokPath = Path.ChangeExtension(modelPath, ".model");
            if (File.Exists(tokPath))
            {
                _tokenizer = new ParakeetTokenizer(tokPath);
                Log.Information("Parakeet tokenizer loaded from {Path}", tokPath);
            }
            else
            {
                Log.Warning("Parakeet tokenizer not found at {Path}", tokPath);
            }
        }
        finally { _gate.Release(); }
    }

    public void UnloadModel()
    {
        _gate.Wait();
        try
        {
            _tokenizer?.Dispose();
            _tokenizer = null;
            _session?.Dispose();
            _session = null;
        }
        finally { _gate.Release(); }
    }

    public async Task<ParakeetResult> TranscribeAsync(float[] samples, CancellationToken ct)
    {
        if (_session is null) throw new InvalidOperationException("Parakeet model not loaded.");

        await _gate.WaitAsync(ct);
        try
        {
            return await Task.Run(() => RunInference(samples), ct);
        }
        finally { _gate.Release(); }
    }

    async Task<string> ILocalTranscriptionBackend.TranscribeAsync(
        float[] samples, string? language, string? initialPrompt, CancellationToken ct)
    {
        var result = await TranscribeAsync(samples, ct);
        return result.Text;
    }

    private ParakeetResult RunInference(float[] samples)
    {
        Log.Information("Parakeet inference: {Samples} samples ({Duration:F2}s)",
            samples.Length, samples.Length / 16000.0);

        // 1. Compute 80-dim log-mel spectrogram
        var melSpec = MelSpectrogram.Compute(samples,
            sampleRate: 16000, nFft: 512, hopLength: 160, nMels: 80);

        // Input tensor: [batch=1, n_mels=80, time_frames]
        int timeFrames = melSpec.GetLength(1);
        Log.Information("Parakeet mel spectrogram: {Mels}x{Frames}", melSpec.GetLength(0), timeFrames);
        var inputTensor = new DenseTensor<float>(new[] { 1, 80, timeFrames });
        for (int m = 0; m < 80; m++)
            for (int t = 0; t < timeFrames; t++)
                inputTensor[0, m, t] = melSpec[m, t];

        var lengthTensor = new DenseTensor<long>(new[] { 1 });
        lengthTensor[0] = timeFrames;

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("audio_signal", inputTensor),
            NamedOnnxValue.CreateFromTensor("length", lengthTensor),
        };

        using var outputs = _session!.Run(inputs);

        // 2. Greedy CTC/TDT decode
        var logits = outputs[0].AsTensor<float>();
        Log.Information("Parakeet logits shape: [{D0},{D1},{D2}]",
            logits.Dimensions[0], logits.Dimensions[1], logits.Dimensions[2]);
        var decoded = GreedyCtcDecode(logits);
        Log.Information("Parakeet CTC decoded: {Count} tokens: [{Tokens}]",
            decoded.Count, string.Join(", ", decoded.Take(50)));

        // 3. Detokenize
        var text = _tokenizer?.Decode(decoded) ?? string.Join("", decoded.Select(t => t.ToString()));
        Log.Information("Parakeet result: \"{Text}\"", text);
        return new ParakeetResult(text, []);
    }

    internal static List<int> GreedyCtcDecode(Tensor<float> logits)
    {
        // logits shape: [batch=1, time, vocab_size]
        int time = (int)logits.Dimensions[1];
        int vocab = (int)logits.Dimensions[2];

        var result = new List<int>();
        int lastToken = -1;

        for (int t = 0; t < time; t++)
        {
            // Argmax over vocab dimension
            int best = 0;
            float bestVal = logits[0, t, 0];
            for (int v = 1; v < vocab; v++)
            {
                float val = logits[0, t, v];
                if (val > bestVal) { bestVal = val; best = v; }
            }

            // CTC collapse: skip blank (token 0) and repeated tokens
            if (best != 0 && best != lastToken)
                result.Add(best);
            lastToken = best;
        }
        return result;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _tokenizer?.Dispose();
            _session?.Dispose();
            _gate.Dispose();
            _disposed = true;
        }
    }
}
