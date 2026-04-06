namespace VoxScript.Core.Transcription.Models;

public sealed record TranscriptionModel(
    ModelProvider Provider,
    string Name,
    string DisplayName,
    bool SupportsStreaming,
    bool IsLocal,
    string? DownloadUrl = null,
    long? FileSizeBytes = null
) : ITranscriptionModel;
