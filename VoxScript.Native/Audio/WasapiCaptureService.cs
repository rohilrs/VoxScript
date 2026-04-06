// VoxScript.Native/Audio/WasapiCaptureService.cs
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoxScript.Core.Audio;

namespace VoxScript.Native.Audio;

public sealed class WasapiCaptureService : IAudioCaptureService, IDisposable
{
    private WasapiCapture? _capture;
    private Action<byte[], int>? _onChunk;
    private CancellationToken _ct;
    private bool _disposed;

    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices() =>
        AudioDeviceEnumerator.EnumerateCapture();

    public AudioDeviceInfo? DefaultDevice => AudioDeviceEnumerator.GetDefault();

    public event EventHandler<AudioDeviceInfo>? DeviceChanged;

    public Task StartAsync(string? deviceId, Action<byte[], int> onChunk, CancellationToken ct)
    {
        if (_capture is not null) throw new InvalidOperationException("Capture already running.");

        _onChunk = onChunk;
        _ct = ct;

        MMDevice device;
        using var enumerator = new MMDeviceEnumerator();
        if (deviceId is not null)
            device = enumerator.GetDevice(deviceId);
        else
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

        _capture = new WasapiCapture(device);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();

        ct.Register(async () => await StopAsync());

        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _onChunk is null) return;
        var converted = AudioFormatConverter.Convert(e.Buffer, e.BytesRecorded, _capture!.WaveFormat);
        if (converted.Length > 0)
            _onChunk(converted, converted.Length);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) { }

    public Task StopAsync()
    {
        if (_capture is null) return Task.CompletedTask;
        _capture.StopRecording();
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();
        _capture = null;
        _onChunk = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _capture?.Dispose();
            _disposed = true;
        }
    }
}
