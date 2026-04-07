// VoxScript.Core/Transcription/Batch/LocalTranscriptionService.cs
using VoxScript.Core.Dictionary;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Batch;

/// <summary>
/// Bridges a local whisper backend to the ITranscriptionService interface.
/// Reads the WAV file, converts Int16 PCM to float[], and delegates to the backend.
/// Vocabulary words are passed as Whisper's initial prompt to bias spelling.
/// </summary>
public sealed class LocalTranscriptionService : ITranscriptionService
{
    private readonly ILocalTranscriptionBackend _backend;
    private readonly IVocabularyRepository _vocabulary;

    public ModelProvider Provider => ModelProvider.Local;

    public LocalTranscriptionService(ILocalTranscriptionBackend backend, IVocabularyRepository vocabulary)
    {
        _backend = backend;
        _vocabulary = vocabulary;
    }

    public async Task<string> TranscribeAsync(string audioPath, ITranscriptionModel model,
        string? language, CancellationToken ct)
    {
        var samples = await Task.Run(() => ReadWavAsFloat(audioPath), ct);

        // Build initial prompt from vocabulary words to bias Whisper toward correct spellings
        string? initialPrompt = null;
        try
        {
            var words = await _vocabulary.GetWordsAsync(ct);
            if (words.Count > 0)
                initialPrompt = string.Join(", ", words);
        }
        catch
        {
            // Non-critical — transcription works fine without the prompt
        }

        return await _backend.TranscribeAsync(samples, language, initialPrompt, ct);
    }

    /// <summary>
    /// Reads a 16-bit mono PCM WAV file, skips the 44-byte header,
    /// and converts Int16 samples to normalized float[-1.0, 1.0].
    /// </summary>
    private static float[] ReadWavAsFloat(string path)
    {
        var fileBytes = File.ReadAllBytes(path);
        const int headerSize = 44;

        if (fileBytes.Length <= headerSize)
            return [];

        int dataBytes = fileBytes.Length - headerSize;
        int sampleCount = dataBytes / 2; // 16-bit = 2 bytes per sample
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
