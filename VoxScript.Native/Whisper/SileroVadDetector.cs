// VoxScript.Native/Whisper/SileroVadDetector.cs
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VoxScript.Native.Whisper;

/// <summary>
/// Silero VAD v5 detector using ONNX Runtime.
/// Processes audio in 512-sample (32ms @ 16kHz) windows and returns speech segments.
/// </summary>
public sealed class SileroVadDetector : IDisposable
{
    private InferenceSession? _session;
    private float[] _state = new float[2 * 1 * 128]; // combined hidden+cell state [2, 1, 128]
    private bool _disposed;

    private const int SampleRate = 16000;
    private const int WindowSize = 512; // 32ms at 16kHz — Silero v5 expects this
    private const float DefaultThreshold = 0.5f;
    private const float MinSpeechDurationMs = 250f;
    private const float MinSilenceDurationMs = 100f;

    public bool IsLoaded => _session is not null;

    public void LoadModel(string onnxModelPath)
    {
        var opts = new SessionOptions();
        opts.InterOpNumThreads = 1;
        opts.IntraOpNumThreads = 1;
        _session = new InferenceSession(onnxModelPath, opts);
        ResetState();
    }

    public void ResetState()
    {
        Array.Clear(_state);
    }

    /// <summary>
    /// Run VAD on audio samples (16kHz mono float32) and return speech segments.
    /// Each segment is a (startSample, endSample) tuple.
    /// </summary>
    public List<(int start, int end)> DetectSpeech(float[] samples, float threshold = DefaultThreshold)
    {
        if (_session is null)
            return [(0, samples.Length)]; // passthrough if no model loaded

        ResetState();

        var segments = new List<(int start, int end)>();
        bool inSpeech = false;
        int speechStart = 0;
        int silenceStart = 0;

        int minSpeechSamples = (int)(MinSpeechDurationMs / 1000f * SampleRate);
        int minSilenceSamples = (int)(MinSilenceDurationMs / 1000f * SampleRate);

        for (int offset = 0; offset + WindowSize <= samples.Length; offset += WindowSize)
        {
            float prob = RunWindow(samples, offset);

            if (prob >= threshold)
            {
                if (!inSpeech)
                {
                    inSpeech = true;
                    speechStart = offset;
                }
                silenceStart = 0;
            }
            else
            {
                if (inSpeech)
                {
                    if (silenceStart == 0) silenceStart = offset;
                    int silenceDuration = offset + WindowSize - silenceStart;
                    if (silenceDuration >= minSilenceSamples)
                    {
                        int speechEnd = silenceStart;
                        if (speechEnd - speechStart >= minSpeechSamples)
                            segments.Add((speechStart, speechEnd));
                        inSpeech = false;
                        silenceStart = 0;
                    }
                }
            }
        }

        // Close any open speech segment
        if (inSpeech)
        {
            int speechEnd = samples.Length;
            if (speechEnd - speechStart >= minSpeechSamples)
                segments.Add((speechStart, speechEnd));
        }

        // If no speech detected, return all audio (fallback)
        if (segments.Count == 0)
            segments.Add((0, samples.Length));

        return segments;
    }

    /// <summary>
    /// Extract speech segments from the full audio, concatenated into a single float array.
    /// Adds small padding around each segment for context.
    /// </summary>
    public float[] ExtractSpeech(float[] samples, float threshold = DefaultThreshold, int padSamples = 480)
    {
        var segments = DetectSpeech(samples, threshold);

        // Calculate total length
        int total = 0;
        foreach (var (start, end) in segments)
        {
            int s = Math.Max(0, start - padSamples);
            int e = Math.Min(samples.Length, end + padSamples);
            total += e - s;
        }

        var result = new float[total];
        int pos = 0;
        foreach (var (start, end) in segments)
        {
            int s = Math.Max(0, start - padSamples);
            int e = Math.Min(samples.Length, end + padSamples);
            Array.Copy(samples, s, result, pos, e - s);
            pos += e - s;
        }

        return result;
    }

    private float RunWindow(float[] samples, int offset)
    {
        // Input: [1, windowSize]
        var input = new DenseTensor<float>(new[] { 1, WindowSize });
        for (int i = 0; i < WindowSize; i++)
            input[0, i] = samples[offset + i];

        // Sample rate: scalar (int64) — shape [] means 0-dimensional tensor
        var sr = new DenseTensor<long>(new long[] { SampleRate }, new int[0]);

        // Combined state: [2, 1, 128]
        var stateTensor = new DenseTensor<float>(_state.AsMemory(), new[] { 2, 1, 128 });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", input),
            NamedOnnxValue.CreateFromTensor("state", stateTensor),
            NamedOnnxValue.CreateFromTensor("sr", sr),
        };

        using var results = _session!.Run(inputs);
        var outputList = results.ToList();

        // Output: probability [1, 1]
        float prob = outputList[0].AsTensor<float>()[0, 0];

        // Updated state — copy back
        var stateN = outputList[1].AsTensor<float>();
        int idx = 0;
        for (int d0 = 0; d0 < 2; d0++)
            for (int d2 = 0; d2 < 128; d2++)
            {
                _state[idx] = stateN[d0, 0, d2];
                idx++;
            }

        return prob;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _session?.Dispose();
            _disposed = true;
        }
    }
}
