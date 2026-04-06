namespace VoxScript.Core.Audio;

public interface IAudioFileProcessor
{
    /// <summary>Convert any audio file to 16kHz mono PCM WAV at targetPath.</summary>
    Task<string> ConvertToWavAsync(string sourcePath, string targetPath, CancellationToken ct);
}
