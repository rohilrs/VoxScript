using VoxScript.Core.Transcription.Models;

namespace VoxScript.Core.Transcription.Core;

public interface ITranscriptionSession
{
    ITranscriptionModel Model { get; }
    bool IsStreaming { get; }
    /// <summary>Called once before recording starts. Returns chunk callback for streaming sessions,
    /// null for file-based sessions.</summary>
    Task<Action<byte[], int>?> PrepareAsync(CancellationToken ct);
    Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct);
    Task CancelAsync();
}
