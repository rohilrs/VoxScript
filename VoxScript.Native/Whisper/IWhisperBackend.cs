// VoxScript.Native/Whisper/IWhisperBackend.cs
namespace VoxScript.Native.Whisper;

public interface IWhisperBackend
{
    bool IsModelLoaded { get; }
    Task LoadModelAsync(string modelPath, CancellationToken ct);
    void UnloadModel();
    /// <summary>Transcribe 16kHz mono float32 PCM samples.</summary>
    Task<string> TranscribeAsync(float[] samples, string? language, string? initialPrompt,
        CancellationToken ct);
}
