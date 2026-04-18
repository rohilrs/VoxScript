using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using VoxScript.Core.Audio;
using VoxScript.Core.Settings;

namespace VoxScript.Onboarding.Steps;

public sealed partial class MicStepViewModel : ObservableObject
{
    private readonly IAudioCaptureService _capture;
    private readonly AppSettings _settings;
    private readonly OnboardingViewModel _onboarding;

    // Threshold on raw linear RMS. Typical PC mic idle sits at 0.001–0.008;
    // quiet speech starts at ~0.02. 0.015 separates silence from any real voice,
    // and also acts as the visual noise gate — below this, meter pins at 0.
    private const float NoiseFloorThreshold = 0.015f;
    private const double SignalRequiredSeconds = 0.5;
    private const double NoSignalTimeoutSeconds = 8.0;

    // Release envelope: once the signal crosses threshold, below-threshold frames
    // still count as signal for this long. Bridges consonants and word trail-offs
    // so short utterances like "testing" don't fall below the gate mid-syllable.
    private const double ReleaseHangSeconds = 0.3;

    // Display curve: sqrt-with-gain. sqrt spreads the speech band wider than
    // cube root (whispers and normal speech visibly differ), gain pushes loud
    // sounds into the top. Whisper ~0.28, normal speech ~0.6, flicks ~0.9.
    private const double DisplayGain = 2.0;

    private double _continuousSignalSeconds;
    private double _noSignalSeconds;
    private double _sinceLastAboveThreshold = double.PositiveInfinity;
    private bool _skipUsed;
    private CancellationTokenSource? _monitorCts;
    private DateTime _lastSampleTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNextEnabled))]
    public partial bool NoDevicesFound { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNextEnabled))]
    public partial bool SignalDetected { get; private set; }

    [ObservableProperty]
    public partial bool ShowNoSignalHint { get; private set; }

    [ObservableProperty]
    public partial float AudioLevel { get; private set; }

    [ObservableProperty]
    public partial AudioDeviceInfo? SelectedDevice { get; set; }

    public IReadOnlyList<AudioDeviceInfo> Devices { get; private set; }

    public bool IsNextEnabled => (SignalDetected || _skipUsed) && !NoDevicesFound;

    public MicStepViewModel(IAudioCaptureService capture, AppSettings settings, OnboardingViewModel onboarding)
    {
        _capture = capture;
        _settings = settings;
        _onboarding = onboarding;

        Devices = capture.EnumerateDevices();
        NoDevicesFound = Devices.Count == 0;

        if (!NoDevicesFound)
            SelectedDevice = capture.DefaultDevice ?? Devices[0];
    }

    /// <summary>
    /// Called with the current LINEAR RMS level [0..1] and elapsed time since
    /// the last call. Gating uses the linear value; display uses a perceptual
    /// curve so normal speech reads in the middle of the bar.
    /// </summary>
    public void OnAudioLevel(float rmsLinear, double deltaSeconds)
    {
        // Hard gate at the noise floor — anything quieter is displayed as 0 so
        // idle mic hiss/thermal noise doesn't make the bar wobble.
        if (rmsLinear < NoiseFloorThreshold)
        {
            AudioLevel = 0f;
        }
        else
        {
            var perceptual = Math.Sqrt(Math.Clamp(rmsLinear, 0f, 1f)) * DisplayGain;
            AudioLevel = (float)Math.Clamp(perceptual, 0.0, 1.0);
        }

        if (SignalDetected) return;

        if (rmsLinear >= NoiseFloorThreshold)
            _sinceLastAboveThreshold = 0;
        else
            _sinceLastAboveThreshold += deltaSeconds;

        // A frame counts as signal if it's above threshold, or if it fell below
        // threshold recently enough that it's still within the release envelope
        // (covers consonants and word trail-offs). Also require that we've had at
        // least one above-threshold frame, so idle silence never counts.
        bool countAsSignal = _sinceLastAboveThreshold < ReleaseHangSeconds;

        if (countAsSignal)
        {
            _continuousSignalSeconds += deltaSeconds;
            _noSignalSeconds = 0;
            if (_continuousSignalSeconds >= SignalRequiredSeconds)
                MarkSignalDetected();
        }
        else
        {
            _noSignalSeconds += deltaSeconds;
            if (_noSignalSeconds >= NoSignalTimeoutSeconds && !ShowNoSignalHint && _continuousSignalSeconds <= 0)
                ShowNoSignalHint = true;
        }
    }

    private void MarkSignalDetected()
    {
        SignalDetected = true;
        ShowNoSignalHint = false;
        _onboarding.UnlockMicStep();
    }

    public void SkipCheck()
    {
        _skipUsed = true;
        _onboarding.UnlockMicStep();
        OnPropertyChanged(nameof(IsNextEnabled));
    }

    public void ConfirmDevice()
    {
        if (SelectedDevice is not null)
            _settings.AudioDeviceId = SelectedDevice.Id;
    }

    /// <summary>
    /// Open the mic device and start driving OnAudioLevel from real PCM frames.
    /// Safe to call multiple times — subsequent calls are no-ops while active.
    /// </summary>
    public async Task StartMonitoringAsync()
    {
        if (_monitorCts is not null) return;
        if (SelectedDevice is null) return;

        _monitorCts = new CancellationTokenSource();
        _lastSampleTime = DateTime.UtcNow;

        // WASAPI callbacks come in on an audio thread; [ObservableProperty] setters raise
        // PropertyChanged synchronously, which WinUI requires to be on the UI thread for
        // bound elements. Capture the dispatcher and marshal each sample across.
        var dispatcher = DispatcherQueue.GetForCurrentThread();

        try
        {
            await _capture.StartAsync(SelectedDevice.Id, (data, count) =>
            {
                var rms = ComputeRms(data, count);
                dispatcher.TryEnqueue(() =>
                {
                    var now = DateTime.UtcNow;
                    var delta = (now - _lastSampleTime).TotalSeconds;
                    _lastSampleTime = now;
                    OnAudioLevel(rms, delta);
                });
            }, _monitorCts.Token);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Onboarding: mic monitoring failed to start");
            _monitorCts?.Dispose();
            _monitorCts = null;
        }
    }

    public async Task StopMonitoringAsync()
    {
        if (_monitorCts is null) return;
        try { _monitorCts.Cancel(); } catch { }
        _monitorCts.Dispose();
        _monitorCts = null;
        try { await _capture.StopAsync(); } catch { }
    }

    /// <summary>
    /// Linear RMS of 16-bit PCM samples, normalized to [0..1]. The perceptual
    /// display curve is applied later in OnAudioLevel.
    /// </summary>
    private static float ComputeRms(byte[] data, int count)
    {
        int sampleCount = count / 2;
        if (sampleCount == 0) return 0f;
        double sumSquares = 0;
        for (int i = 0; i < count - 1; i += 2)
        {
            short sample = (short)(data[i] | (data[i + 1] << 8));
            sumSquares += sample * (double)sample;
        }
        double rms = Math.Sqrt(sumSquares / sampleCount) / short.MaxValue;
        return (float)Math.Min(rms, 1.0);
    }


    public void Retry()
    {
        Devices = _capture.EnumerateDevices();
        if (Devices.Count > 0)
        {
            NoDevicesFound = false;
            SelectedDevice = _capture.DefaultDevice ?? Devices[0];
        }
    }

    internal void SimulateSignalDetected() => MarkSignalDetected();

    internal void SimulateNoSignalTimeout()
    {
        _noSignalSeconds = NoSignalTimeoutSeconds;
        ShowNoSignalHint = true;
    }
}
