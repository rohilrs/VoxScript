// VoxScript.Core/Transcription/Core/ILocalTranscriptionBackend.cs
namespace VoxScript.Core.Transcription.Core;

/// <summary>
/// Core-level abstraction for a local transcription backend (e.g. whisper.cpp).
/// Implemented in VoxScript.Native so that Core has no dependency on Native.
/// </summary>
public interface ILocalTranscriptionBackend
{
    bool IsModelLoaded { get; }
    Task LoadModelAsync(string modelPath, CancellationToken ct);
    void UnloadModel();
    /// <summary>Transcribe 16kHz mono float32 PCM samples.</summary>
    Task<string> TranscribeAsync(float[] samples, string? language, string? initialPrompt,
        CancellationToken ct);
}
