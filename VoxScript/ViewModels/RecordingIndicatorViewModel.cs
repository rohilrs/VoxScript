using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;

namespace VoxScript.ViewModels;

public sealed partial class RecordingIndicatorViewModel : ObservableObject, IDisposable
{
    private readonly VoxScriptEngine _engine;
    private readonly AppSettings _settings;
    private readonly DispatcherQueueTimer _elapsedTimer;
    private DateTime _recordingStartTime;
    private bool _disposed;

    [ObservableProperty]
    public partial RecordingState State { get; set; }

    [ObservableProperty]
    public partial float AudioLevel { get; set; }

    [ObservableProperty]
    public partial bool IsToggleMode { get; set; }

    [ObservableProperty]
    public partial string ElapsedTime { get; set; } = "0:00";

    // True when we expect a TranscriptionCompleted event to follow the Idle transition.
    // The engine sets State=Idle BEFORE firing TranscriptionCompleted, so we use a
    // short timer to check whether the event arrives. If it doesn't, we hide ourselves.
    private bool _awaitingTranscriptionResult;
    private CancellationTokenSource? _idleHideCts;

    public bool IsAlwaysVisible =>
        _settings.RecordingIndicatorMode == RecordingIndicatorMode.AlwaysVisible;

    // UI events — the view subscribes to these to show/hide/animate the indicator window.
    public event Action? ShowRequested;
    public event Action? HideRequested;
    public event Action? DismissWithPastedRequested;
    public event Action? ReturnToIdleRequested;

    public RecordingIndicatorViewModel(VoxScriptEngine engine, AppSettings settings)
    {
        _engine = engine;
        _settings = settings;

        // Sync initial values
        State = _engine.State;
        AudioLevel = _engine.AudioLevel;
        IsToggleMode = _engine.IsToggleMode;

        _engine.PropertyChanged += OnEnginePropertyChanged;
        _engine.TranscriptionCompleted += OnTranscriptionCompleted;
        _settings.RecordingIndicatorModeChanged += OnIndicatorModeChanged;

        // Timer ticks every second on the UI thread to update elapsed time.
        _elapsedTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _elapsedTimer.Interval = TimeSpan.FromSeconds(1);
        _elapsedTimer.IsRepeating = true;
        _elapsedTimer.Tick += (_, _) => UpdateElapsedTime();
    }

    /// <summary>
    /// Called once after the indicator window is created so it can appear immediately
    /// when the user has chosen "AlwaysVisible" mode.
    /// </summary>
    public void ApplyInitialVisibility()
    {
        if (IsAlwaysVisible)
            ShowRequested?.Invoke();
    }

    // ── Commands ──────────────────────────────────────────────

    [RelayCommand]
    private async Task FinishAsync() => await _engine.StopAndTranscribeAsync();

    [RelayCommand]
    private async Task CancelAsync()
    {
        // Hide first so the window disappears instantly — don't wait for
        // the engine's State→Idle transition which would flash an empty pill.
        HideRequested?.Invoke();
        await _engine.CancelRecordingAsync();
    }

    // ── Setting change handling ────────────────────────────

    private void OnIndicatorModeChanged(object? sender, RecordingIndicatorMode newMode)
    {
        switch (newMode)
        {
            case RecordingIndicatorMode.Off:
                // Hide immediately regardless of state
                HideRequested?.Invoke();
                break;

            case RecordingIndicatorMode.AlwaysVisible:
                // Show immediately (idle state if not recording)
                ShowRequested?.Invoke();
                break;

            case RecordingIndicatorMode.DuringRecording:
                // Show only if currently recording, hide otherwise
                if (_engine.State == RecordingState.Recording
                    || _engine.State == RecordingState.Transcribing
                    || _engine.State == RecordingState.Enhancing)
                    ShowRequested?.Invoke();
                else
                    HideRequested?.Invoke();
                break;
        }
    }

    // ── Engine property forwarding ───────────────────────────

    private void OnEnginePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(VoxScriptEngine.State):
                State = _engine.State;
                HandleStateTransition(_engine.State);
                break;

            case nameof(VoxScriptEngine.AudioLevel):
                AudioLevel = _engine.AudioLevel;
                break;

            case nameof(VoxScriptEngine.IsToggleMode):
                IsToggleMode = _engine.IsToggleMode;
                break;
        }
    }

    private void HandleStateTransition(RecordingState newState)
    {
        switch (newState)
        {
            case RecordingState.Recording:
                _awaitingTranscriptionResult = false;
                CancelDeferredIdleHide();
                _recordingStartTime = DateTime.UtcNow;
                ElapsedTime = "0:00";
                _elapsedTimer.Start();

                if (_settings.RecordingIndicatorMode != RecordingIndicatorMode.Off)
                    ShowRequested?.Invoke();
                break;

            case RecordingState.Transcribing:
            case RecordingState.Enhancing:
                // Stop the clock but keep the bar visible while work completes.
                _elapsedTimer.Stop();
                _awaitingTranscriptionResult = true;
                break;

            case RecordingState.Idle:
                _elapsedTimer.Stop();

                // The engine sets State=Idle BEFORE firing TranscriptionCompleted.
                // We schedule a short deferred hide; if TranscriptionCompleted arrives
                // it cancels the deferred hide and runs the dismiss animation instead.
                if (_awaitingTranscriptionResult)
                {
                    _awaitingTranscriptionResult = false;
                    ScheduleDeferredIdleHide();
                }
                break;
        }
    }

    // ── Transcription completed ──────────────────────────────

    private void OnTranscriptionCompleted(object? sender, string text)
    {
        _awaitingTranscriptionResult = false;
        CancelDeferredIdleHide();

        DismissWithPastedRequested?.Invoke();

        if (IsAlwaysVisible)
            ReturnToIdleRequested?.Invoke();
    }

    /// <summary>
    /// After the engine transitions to Idle, wait briefly for TranscriptionCompleted.
    /// If it doesn't arrive (null transcription / hallucination / error), hide the window.
    /// </summary>
    private async void ScheduleDeferredIdleHide()
    {
        CancelDeferredIdleHide();
        var cts = new CancellationTokenSource();
        _idleHideCts = cts;

        try
        {
            // Short delay — TranscriptionCompleted fires synchronously right after
            // State=Idle in the same method, so it will arrive before this fires.
            // 100ms is more than enough margin.
            await Task.Delay(100, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return; // TranscriptionCompleted arrived and cancelled us
        }

        if (IsAlwaysVisible)
        {
            // Stay visible in idle state (UpdateVisualState already ran)
        }
        else if (_settings.RecordingIndicatorMode == RecordingIndicatorMode.DuringRecording)
        {
            HideRequested?.Invoke();
        }
    }

    private void CancelDeferredIdleHide()
    {
        _idleHideCts?.Cancel();
        _idleHideCts?.Dispose();
        _idleHideCts = null;
    }

    // ── Elapsed time helper ──────────────────────────────────

    private void UpdateElapsedTime()
    {
        var elapsed = DateTime.UtcNow - _recordingStartTime;
        ElapsedTime = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
    }

    // ── Cleanup ──────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _engine.PropertyChanged -= OnEnginePropertyChanged;
        _engine.TranscriptionCompleted -= OnTranscriptionCompleted;
        _settings.RecordingIndicatorModeChanged -= OnIndicatorModeChanged;
        _elapsedTimer.Stop();
        CancelDeferredIdleHide();
    }
}
