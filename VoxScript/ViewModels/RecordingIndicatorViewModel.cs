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
        await _engine.CancelRecordingAsync();
        HideRequested?.Invoke();
    }

    // ── Engine property forwarding ───────────────────────────

    private void OnEnginePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(VoxScriptEngine.State):
                State = _engine.State;
                OnStateChanged(_engine.State);
                break;

            case nameof(VoxScriptEngine.AudioLevel):
                AudioLevel = _engine.AudioLevel;
                break;

            case nameof(VoxScriptEngine.IsToggleMode):
                IsToggleMode = _engine.IsToggleMode;
                break;
        }
    }

    private void OnStateChanged(RecordingState newState)
    {
        switch (newState)
        {
            case RecordingState.Recording:
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
                break;

            case RecordingState.Idle:
                // Timer cleanup — hide is driven by TranscriptionCompleted or CancelAsync.
                _elapsedTimer.Stop();
                break;
        }
    }

    // ── Transcription completed ──────────────────────────────

    private void OnTranscriptionCompleted(object? sender, string text)
    {
        DismissWithPastedRequested?.Invoke();

        if (IsAlwaysVisible)
            ReturnToIdleRequested?.Invoke();
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
        _elapsedTimer.Stop();
    }
}
