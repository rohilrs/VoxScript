// VoxScript.Native/Whisper/IWhisperBackend.cs
using VoxScript.Core.Transcription.Core;

namespace VoxScript.Native.Whisper;

public interface IWhisperBackend
{
    bool IsModelLoaded { get; }
    Task LoadModelAsync(string modelPath, CancellationToken ct);
    void UnloadModel();
    /// <summary>Transcribe 16kHz mono float32 PCM samples. Returns segment-level results with timestamps.</summary>
    Task<TranscriptionSegment[]> TranscribeAsync(float[] samples, string? language, string? initialPrompt,
        CancellationToken ct);
}
