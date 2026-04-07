// VoxScript.Core/Transcription/Batch/ParakeetTranscriptionService.cs
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Batch;

/// <summary>
/// Bridges a Parakeet ONNX backend to the ITranscriptionService interface.
/// Unlike LocalTranscriptionService, does not pass vocabulary as initialPrompt
/// (Parakeet has no prompting mechanism).
/// </summary>
public sealed class ParakeetTranscriptionService : ITranscriptionService
{
    private readonly ILocalTranscriptionBackend _backend;

    public ModelProvider Provider => ModelProvider.Parakeet;

    public ParakeetTranscriptionService(ILocalTranscriptionBackend backend)
    {
        _backend = backend;
    }

    public async Task<string> TranscribeAsync(string audioPath, ITranscriptionModel model,
        string? language, CancellationToken ct)
    {
        var samples = await Task.Run(() => ReadWavAsFloat(audioPath), ct);
        var segments = await _backend.TranscribeAsync(samples, language: null, initialPrompt: null, ct);

        // Parakeet returns a single segment with no timestamps — just extract text
        if (segments.Length == 0) return string.Empty;
        return string.Join(" ", segments.Select(s => s.Text.Trim()));
    }

    private static float[] ReadWavAsFloat(string path)
    {
        var fileBytes = File.ReadAllBytes(path);
        const int headerSize = 44;

        if (fileBytes.Length <= headerSize)
            return [];

        int dataBytes = fileBytes.Length - headerSize;
        int sampleCount = dataBytes / 2;
        var samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            int offset = headerSize + i * 2;
            short s = (short)(fileBytes[offset] | (fileBytes[offset + 1] << 8));
            samples[i] = s / 32768f;
        }

        return samples;
    }
}
