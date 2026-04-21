// VoxScript.Native/Audio/WasapiCaptureService.cs
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using VoxScript.Core.Audio;

namespace VoxScript.Native.Audio;

public sealed class WasapiCaptureService : IAudioCaptureService, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly DeviceChangeNotificationClient _notificationClient;
    private WasapiCapture? _capture;
    private Action<byte[], int>? _onChunk;
    private CancellationToken _ct;
    private bool _disposed;

    public WasapiCaptureService()
    {
        _notificationClient = new DeviceChangeNotificationClient(
            () => DevicesChanged?.Invoke(this, EventArgs.Empty));
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
    }

    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices() =>
        AudioDeviceEnumerator.EnumerateCapture();

    public AudioDeviceInfo? DefaultDevice => AudioDeviceEnumerator.GetDefault();

    public event EventHandler? DevicesChanged;

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
        if (_disposed) return;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        }
        catch
        {
            // Swallow: registration/unregistration can throw during shutdown
            // if the COM apartment is already torn down, and leaking the
            // registration briefly is benign compared to crashing exit.
        }
        _capture?.Dispose();
        _enumerator.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// IMMNotificationClient that forwards every WASAPI endpoint event to a
    /// single "something changed" callback. We don't distinguish between
    /// add/remove/default-change because every consumer today just wants to
    /// re-enumerate. Callbacks fire on a WASAPI MTA thread.
    /// </summary>
    private sealed class DeviceChangeNotificationClient : IMMNotificationClient
    {
        private readonly Action _onChange;

        public DeviceChangeNotificationClient(Action onChange) => _onChange = onChange;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _onChange();
        public void OnDeviceAdded(string pwstrDeviceId) => _onChange();
        public void OnDeviceRemoved(string deviceId) => _onChange();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => _onChange();

        // Property changes fire rapidly (volume, format, etc.) and never
        // affect the device list we care about, so we deliberately ignore
        // them to avoid rebuilding the tray menu dozens of times per second.
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
