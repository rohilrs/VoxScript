using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Core;

public interface ITranscriptionService
{
    ModelProvider Provider { get; }
    Task<string> TranscribeAsync(string audioPath, ITranscriptionModel model,
        string? language, CancellationToken ct);
}
