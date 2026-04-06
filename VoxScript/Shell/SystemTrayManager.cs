// VoxScript/Shell/SystemTrayManager.cs
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using VoxScript.Core.Transcription.Core;

namespace VoxScript.Shell;

public sealed class SystemTrayManager : IDisposable
{
    private TaskbarIcon? _trayIcon;
    private readonly VoxScriptEngine _engine;
    private readonly Window _mainWindow;

    public SystemTrayManager(VoxScriptEngine engine, Window mainWindow)
    {
        _engine = engine;
        _mainWindow = mainWindow;
    }

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "VoxScript",
            ContextMenuMode = ContextMenuMode.PopupMenu,
        };

        // TODO: Set icon when asset is available
        // _trayIcon.IconSource = new BitmapImage(new Uri("ms-appx:///Assets/Icons/tray.ico"));

        _trayIcon.LeftClickCommand = new RelayCommand(() => _mainWindow.Activate());
        _engine.PropertyChanged += OnEngineStateChanged;

        UpdateTrayTooltip(_engine.State);
    }

    private void OnEngineStateChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VoxScriptEngine.State)) return;
        _mainWindow.DispatcherQueue.TryEnqueue(() =>
            UpdateTrayTooltip(_engine.State));
    }

    private void UpdateTrayTooltip(RecordingState state)
    {
        if (_trayIcon is null) return;
        _trayIcon.ToolTipText = state switch
        {
            RecordingState.Recording    => "VoxScript -- Recording...",
            RecordingState.Transcribing => "VoxScript -- Transcribing...",
            RecordingState.Enhancing    => "VoxScript -- Enhancing...",
            _                           => "VoxScript",
        };
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
        _engine.PropertyChanged -= OnEngineStateChanged;
    }
}
