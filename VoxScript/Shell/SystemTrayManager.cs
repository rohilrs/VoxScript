// VoxScript/Shell/SystemTrayManager.cs
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoxScript.Core.Audio;
using VoxScript.Core.Platform;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Infrastructure;
using VoxScript.Views;

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
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "VoxScript",
            // SecondWindow renders the menu as a XAML popup so we get Mica
            // backdrop and rounded corners, and click events on MenuFlyoutItem
            // fire correctly. PopupMenu (Win32 TrackPopupMenu) handles screen-edge
            // collision better but doesn't translate Win32 menu clicks back to
            // MenuFlyoutItem.Click handlers — every item silently did nothing.
            ContextMenuMode = ContextMenuMode.SecondWindow,
        };

        // Load icon from .ico file via Win32 LoadImage
        try
        {
            var handle = NativeMethods.LoadImage(
                IntPtr.Zero, iconPath,
                NativeMethods.IMAGE_ICON,
                0, 0,
                NativeMethods.LR_LOADFROMFILE | NativeMethods.LR_DEFAULTSIZE);

            if (handle != IntPtr.Zero)
            {
                _trayIcon.Icon = System.Drawing.Icon.FromHandle(handle);
                Serilog.Log.Information("Tray icon loaded from {Path}", iconPath);
            }
            else
            {
                Serilog.Log.Warning("LoadImage returned null for tray icon at {Path}", iconPath);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load tray icon from {Path}", iconPath);
        }

        // ForceCreate registers the icon with Shell_NotifyIcon.
        // Required when TaskbarIcon is created in code rather than XAML.
        _trayIcon.ForceCreate();

        // Right-click context menu
        _trayIcon.ContextFlyout = BuildContextMenu();

        // Left-click opens the window
        _trayIcon.LeftClickCommand = new RelayCommand(() =>
        {
            if (_mainWindow is MainWindow mw)
                mw.BringToFront();
            else
                _mainWindow.Activate();
        });

        _engine.PropertyChanged += OnEngineStateChanged;
        UpdateTrayTooltip(_engine.State);
    }

    private MenuFlyout BuildContextMenu()
    {
        var menu = new MenuFlyout();

        // Home
        var homeItem = new MenuFlyoutItem { Text = "Home" };
        homeItem.Click += (_, _) =>
        {
            if (_mainWindow is MainWindow mw)
            {
                mw.BringToFront();
                mw.NavigateTo(typeof(TranscribePage));
            }
        };
        menu.Items.Add(homeItem);

        // Paste Last Transcript
        var pasteItem = new MenuFlyoutItem { Text = "Paste Last Transcript" };
        pasteItem.Click += (_, _) =>
        {
            _mainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                var text = _engine.LastTranscription;
                if (!string.IsNullOrEmpty(text))
                {
                    try
                    {
                        var paste = ServiceLocator.Get<IPasteService>();
                        await paste.PasteAtCursorAsync(text, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Tray paste-last failed");
                    }
                }
            });
        };
        menu.Items.Add(pasteItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        // Microphone submenu
        var micSub = new MenuFlyoutSubItem { Text = "Microphone" };
        try
        {
            var audio = ServiceLocator.Get<IAudioCaptureService>();
            var settings = ServiceLocator.Get<AppSettings>();
            var devices = audio.EnumerateDevices();
            var selectedId = settings.AudioDeviceId;

            foreach (var device in devices)
            {
                var item = new ToggleMenuFlyoutItem { Text = device.DisplayName };

                // Check the currently selected device (null = system default)
                var isSelected = selectedId is null
                    ? device.IsDefault
                    : device.Id == selectedId;
                item.IsChecked = isSelected;

                var capturedDevice = device;
                item.Click += (_, _) =>
                {
                    // null means "use system default"
                    settings.AudioDeviceId = capturedDevice.IsDefault ? null : capturedDevice.Id;
                    Serilog.Log.Information("Microphone changed via tray: {Device}", capturedDevice.DisplayName);
                };

                micSub.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to enumerate microphones for tray menu");
            var errorItem = new MenuFlyoutItem { Text = "No devices found", IsEnabled = false };
            micSub.Items.Add(errorItem);
        }
        menu.Items.Add(micSub);

        // Settings
        var settingsItem = new MenuFlyoutItem { Text = "Settings" };
        settingsItem.Click += (_, _) =>
        {
            if (_mainWindow is MainWindow mw)
            {
                mw.BringToFront();
                mw.NavigateTo(typeof(SettingsPage));
            }
        };
        menu.Items.Add(settingsItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        // Exit
        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (_, _) =>
        {
            _mainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                Dispose();
                // App.ExitApp guarantees process termination via Environment.Exit.
                // Application.Current.Exit() previously left the process lingering
                // as a background process because the keyboard hook, WASAPI capture,
                // and native whisper threads kept the runtime alive.
                App.ExitApp();
            });
        };
        menu.Items.Add(exitItem);

        return menu;
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

    private static class NativeMethods
    {
        public const uint IMAGE_ICON = 1;
        public const uint LR_LOADFROMFILE = 0x00000010;
        public const uint LR_DEFAULTSIZE = 0x00000040;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);
    }
}
