// VoxScript.Native/Audio/AudioDeviceEnumerator.cs
using NAudio.CoreAudioApi;
using VoxScript.Core.Audio;

namespace VoxScript.Native.Audio;

public static class AudioDeviceEnumerator
{
    public static IReadOnlyList<AudioDeviceInfo> EnumerateCapture()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = enumerator
            .GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)?.ID;

        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName, d.ID == defaultId))
            .ToList();
    }

    public static AudioDeviceInfo? GetDefault()
    {
        using var enumerator = new MMDeviceEnumerator();
        var dev = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        return dev is null ? null : new AudioDeviceInfo(dev.ID, dev.FriendlyName, true);
    }
}
