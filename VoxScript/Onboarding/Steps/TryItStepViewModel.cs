using CommunityToolkit.Mvvm.ComponentModel;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;

namespace VoxScript.Onboarding.Steps;

public enum TryItSubState { Idle, Recording, Transcribing, Success }

public sealed partial class TryItStepViewModel : ObservableObject, IDisposable
{
    private readonly IGlobalHotkeyEvents _hotkey;
    private readonly IWizardEngine _engine;
    private readonly AppSettings _settings;
    private readonly OnboardingViewModel _onboarding;
    private bool _disposed;

    [ObservableProperty]
    public partial TryItSubState SubState { get; private set; }

    [ObservableProperty]
    public partial string TranscriptText { get; private set; }

    [ObservableProperty]
    public partial bool ShowEmptyHint { get; private set; }

    public TryItStepViewModel(
        IGlobalHotkeyEvents hotkey,
        IWizardEngine engine,
        AppSettings settings,
        OnboardingViewModel onboarding)
    {
        _hotkey = hotkey;
        _engine = engine;
        _settings = settings;
        _onboarding = onboarding;
        SubState = TryItSubState.Idle;
        TranscriptText = string.Empty;

        _hotkey.RecordingStartRequested += OnRecordingStartRequested;
        _hotkey.RecordingStopRequested += OnRecordingStopRequested;
        _hotkey.RecordingCancelRequested += OnRecordingCancelRequested;
        _engine.TranscriptionCompleted += OnTranscriptionCompleted;
    }

    private void OnRecordingStartRequested(object? sender, EventArgs e)
    {
        if (SubState != TryItSubState.Idle) return;
        SubState = TryItSubState.Recording;
        ShowEmptyHint = false;

        var model = PredefinedModels.Default;
        _ = _engine.StartRecordingAsync(model, suppressAutoPaste: true);
    }

    private void OnRecordingStopRequested(object? sender, EventArgs e)
    {
        if (SubState != TryItSubState.Recording) return;
        SubState = TryItSubState.Transcribing;
        _ = _engine.StopAndTranscribeAsync();
    }

    private void OnRecordingCancelRequested(object? sender, EventArgs e)
    {
        if (SubState == TryItSubState.Recording || SubState == TryItSubState.Transcribing)
        {
            _ = _engine.CancelRecordingAsync();
            SubState = TryItSubState.Idle;
        }
    }

    private void OnTranscriptionCompleted(object? sender, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            SubState = TryItSubState.Idle;
            ShowEmptyHint = true;
        }
        else
        {
            TranscriptText = text;
            SubState = TryItSubState.Success;
            _onboarding.UnlockTryItStep();
        }
    }

    public void SkipForNow() => _onboarding.UnlockTryItStep();

    public void TryAgain()
    {
        TranscriptText = string.Empty;
        ShowEmptyHint = false;
        SubState = TryItSubState.Idle;
    }

    internal void SimulateTranscriptionCompleted(string text) =>
        OnTranscriptionCompleted(null, text);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hotkey.RecordingStartRequested -= OnRecordingStartRequested;
        _hotkey.RecordingStopRequested -= OnRecordingStopRequested;
        _hotkey.RecordingCancelRequested -= OnRecordingCancelRequested;
        _engine.TranscriptionCompleted -= OnTranscriptionCompleted;
    }
}
