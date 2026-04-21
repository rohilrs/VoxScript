namespace VoxScript.Core.Audio;

public interface IAudioCaptureService
{
    IReadOnlyList<AudioDeviceInfo> EnumerateDevices();
    AudioDeviceInfo? DefaultDevice { get; }

    Task StartAsync(string? deviceId, Action<byte[], int> onChunk, CancellationToken ct);
    Task StopAsync();

    /// <summary>
    /// Raised when the set of capture endpoints or the system default
    /// changes (device added/removed, default reassigned, state toggled).
    /// Fired on a WASAPI notification thread — subscribers must marshal
    /// to the UI thread before touching UI state.
    /// </summary>
    event EventHandler? DevicesChanged;
}
