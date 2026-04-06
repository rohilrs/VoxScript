namespace VoxScript.Core.Audio;

public interface IAudioCaptureService
{
    IReadOnlyList<AudioDeviceInfo> EnumerateDevices();
    AudioDeviceInfo? DefaultDevice { get; }

    Task StartAsync(string? deviceId, Action<byte[], int> onChunk, CancellationToken ct);
    Task StopAsync();

    event EventHandler<AudioDeviceInfo>? DeviceChanged;
}
