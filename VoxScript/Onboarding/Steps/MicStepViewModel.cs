using CommunityToolkit.Mvvm.ComponentModel;
using VoxScript.Core.Audio;
using VoxScript.Core.Settings;

namespace VoxScript.Onboarding.Steps;

public sealed partial class MicStepViewModel : ObservableObject
{
    private readonly IAudioCaptureService _capture;
    private readonly AppSettings _settings;
    private readonly OnboardingViewModel _onboarding;

    private const float NoiseFloorThreshold = 0.01f;
    private const double SignalRequiredSeconds = 2.0;
    private const double NoSignalTimeoutSeconds = 8.0;

    private double _continuousSignalSeconds;
    private double _noSignalSeconds;
    private bool _skipUsed;

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
    /// Called with the current RMS level [0..1] and elapsed time since the last call.
    /// Accumulates time above the noise floor to detect sustained speech.
    /// </summary>
    public void OnAudioLevel(float rms, double deltaSeconds)
    {
        AudioLevel = rms;

        if (SignalDetected) return;

        if (rms >= NoiseFloorThreshold)
        {
            _continuousSignalSeconds += deltaSeconds;
            _noSignalSeconds = 0;
            if (_continuousSignalSeconds >= SignalRequiredSeconds)
                MarkSignalDetected();
        }
        else
        {
            _continuousSignalSeconds = 0;
            _noSignalSeconds += deltaSeconds;
            if (_noSignalSeconds >= NoSignalTimeoutSeconds && !ShowNoSignalHint)
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

    public void ConfirmDevice(AppSettings settings)
    {
        if (SelectedDevice is not null)
            settings.AudioDeviceId = SelectedDevice.Id;
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
