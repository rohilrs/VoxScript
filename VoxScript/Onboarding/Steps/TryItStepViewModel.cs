using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
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
    private readonly DispatcherQueue? _dispatcher;
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
        // Capture the UI dispatcher at construction time (VM is built on UI thread).
        // GlobalHotkeyService fires events on the low-level keyboard hook thread and
        // VoxScriptEngine fires TranscriptionCompleted on whatever thread the pipeline
        // resolved on — setting [ObservableProperty] values from those threads would
        // raise PropertyChanged synchronously and crash when the bound UI tries to update.
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        SubState = TryItSubState.Idle;
        TranscriptText = string.Empty;

        _hotkey.RecordingStartRequested += OnRecordingStartRequested;
        _hotkey.RecordingStopRequested += OnRecordingStopRequested;
        _hotkey.RecordingToggleRequested += OnRecordingToggleRequested;
        _hotkey.RecordingCancelRequested += OnRecordingCancelRequested;
        _hotkey.ToggleLockActivated += OnToggleLockActivated;
        _engine.TranscriptionCompleted += OnTranscriptionCompleted;
    }

    private void OnToggleLockActivated() => RunOnUi(() =>
    {
        // Space converted a hold into a toggle-locked recording. Reflect this on the
        // engine so the recording indicator bar switches to toggle-mode UI
        // (Finish button instead of hold-only Cancel).
        if (!IsActiveStep) return;
        if (_engine.State == RecordingState.Recording)
            _engine.IsToggleMode = true;
    });

    private void RunOnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }

    private bool IsActiveStep => _onboarding.CurrentStep == OnboardingStep.TryIt;

    private void OnRecordingStartRequested(object? sender, EventArgs e) => RunOnUi(() =>
    {
        // Ignore hotkey presses on any other step — we only own the engine here.
        if (!IsActiveStep) return;
        // Allow re-record from Success (pressing the hotkey again just tries another clip).
        if (SubState != TryItSubState.Idle && SubState != TryItSubState.Success) return;

        TranscriptText = string.Empty;
        ShowEmptyHint = false;
        SubState = TryItSubState.Recording;

        var model = PredefinedModels.Default;
        _ = _engine.StartRecordingAsync(model, suppressAutoPaste: true);
    });

    private void OnRecordingStopRequested(object? sender, EventArgs e) => RunOnUi(() =>
    {
        if (!IsActiveStep) return;
        if (SubState != TryItSubState.Recording) return;
        SubState = TryItSubState.Transcribing;
        _ = _engine.StopAndTranscribeAsync();
    });

    private void OnRecordingToggleRequested(object? sender, EventArgs e) => RunOnUi(() =>
    {
        if (!IsActiveStep) return;

        if (SubState == TryItSubState.Recording)
        {
            // Second toggle press stops & transcribes
            SubState = TryItSubState.Transcribing;
            _ = _engine.StopAndTranscribeAsync();
        }
        else if (SubState == TryItSubState.Idle || SubState == TryItSubState.Success)
        {
            TranscriptText = string.Empty;
            ShowEmptyHint = false;
            SubState = TryItSubState.Recording;
            _ = StartToggleRecordingAsync();
        }
    });

    private async Task StartToggleRecordingAsync()
    {
        await _engine.ToggleRecordAsync(PredefinedModels.Default, suppressAutoPaste: true);
        // Mark the session as toggle-locked so the recording indicator bar shows
        // the toggle-mode UI (Finish button) instead of the hold-mode Cancel-only UI.
        if (_engine.State == RecordingState.Recording)
            _engine.IsToggleMode = true;
    }

    private void OnRecordingCancelRequested(object? sender, EventArgs e) => RunOnUi(() =>
    {
        if (!IsActiveStep) return;
        if (SubState == TryItSubState.Recording || SubState == TryItSubState.Transcribing)
        {
            _ = _engine.CancelRecordingAsync();
            SubState = TryItSubState.Idle;
        }
    });

    private void OnTranscriptionCompleted(object? sender, string text) => RunOnUi(() =>
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
    });

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
        _hotkey.RecordingToggleRequested -= OnRecordingToggleRequested;
        _hotkey.RecordingCancelRequested -= OnRecordingCancelRequested;
        _hotkey.ToggleLockActivated -= OnToggleLockActivated;
        _engine.TranscriptionCompleted -= OnTranscriptionCompleted;
    }
}
