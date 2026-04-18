namespace VoxScript.Native.Whisper;

/// <summary>
/// Abstraction over <see cref="WhisperModelManager"/> to allow the onboarding
/// ModelStepViewModel to be unit-tested with NSubstitute.
/// </summary>
public interface IWhisperModelManager
{
    string GetModelPath(string modelName);
    string VadModelPath { get; }
    bool IsVadDownloaded { get; }
    bool IsDownloaded(string modelName);
    IReadOnlyList<string> ListDownloaded();
    Task DownloadAsync(string modelName, IProgress<double>? progress, CancellationToken ct);
    Task DownloadVadAsync(IProgress<double>? progress, CancellationToken ct);
    void DeleteModel(string modelName);
}
