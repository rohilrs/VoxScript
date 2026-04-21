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
    private MenuFlyoutSubItem? _micSub;
    private IAudioCaptureService? _audioService;
    private AppSettings? _settings;
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

        // H.NotifyIcon SecondWindow mode reparents our items into a separately
        // constructed MenuFlyout ONCE (in PrepareContextMenuWindow), so the
        // source MenuFlyout.Opening event we'd normally hook never fires and
        // the version of H.NotifyIcon we ship with (2.4.1) doesn't expose an
        // equivalent "about to show" event. Instead of waiting for a menu-open
        // signal, we push-update the mic list whenever WASAPI reports a device
        // change. The MenuFlyoutSubItem reference survives reparenting, so
        // mutating `_micSub.Items` still updates what the user sees next time
        // they open the tray menu.
        _audioService = ServiceLocator.Get<IAudioCaptureService>();
        _audioService.DevicesChanged += OnAudioDevicesChanged;

        // Keep the tray's radio selection in sync with whoever else writes to
        // AudioDeviceId (settings page, onboarding, future surfaces). The
        // setter raises this on the writer's thread; rebuild is queued to the
        // UI thread either way.
        _settings = ServiceLocator.Get<AppSettings>();
        _settings.AudioDeviceIdChanged += OnAudioDeviceIdChanged;

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

    // H.NotifyIcon's SecondWindow mode sizes its host popup window by calling
    // MenuFlyoutItem.Measure() on each item and using DesiredSize.Width (see
    // HavenDV/H.NotifyIcon issue #21, TaskbarIcon.ContextMenu.WinUI.SecondWindow
    // line 209). On the very first right-click, font metrics haven't fully
    // resolved and that measurement comes back too narrow, causing the host
    // window to clip item text. Setting MinWidth on each item floors the
    // measurement at a sensible size so the host window never under-sizes.
    // Tuned to roughly match the natural rendered width of the longest static
    // item ("Paste Last Transcript" at default Segoe UI 14pt), so the fix
    // protects against first-render underestimates without inflating the
    // overall menu. Longer content (e.g. long mic device names) still expands
    // past it on subsequent measures.
    private const double MenuItemMinWidth = 200.0;

    private MenuFlyout BuildContextMenu()
    {
        var menu = new MenuFlyout();

        // Home
        var homeItem = new MenuFlyoutItem { Text = "Home", MinWidth = MenuItemMinWidth };
        homeItem.Click += (_, _) =>
        {
            if (_mainWindow is MainWindow mw)
            {
                mw.BringToFront();
                mw.NavigateTo(typeof(HomePage));
            }
        };
        menu.Items.Add(homeItem);

        // Paste Last Transcript
        var pasteItem = new MenuFlyoutItem { Text = "Paste Last Transcript", MinWidth = MenuItemMinWidth };
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

        // Microphone submenu. Populated once here so items exist when
        // H.NotifyIcon reparents them into its SecondWindow flyout at init
        // time; refreshed on every right-click via SecondWindowContextMenuOpened
        // so mics plugged in after launch show up without restarting the app.
        _micSub = new MenuFlyoutSubItem { Text = "Microphone", MinWidth = MenuItemMinWidth };
        PopulateMicrophoneSubMenu(_micSub);
        menu.Items.Add(_micSub);

        // Settings
        var settingsItem = new MenuFlyoutItem { Text = "Settings", MinWidth = MenuItemMinWidth };
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
        var exitItem = new MenuFlyoutItem { Text = "Exit", MinWidth = MenuItemMinWidth };
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

    // Shared GroupName so WinUI's RadioMenuFlyoutItem coordination auto-
    // unchecks the previously-selected mic when the user picks a new one
    // (ToggleMenuFlyoutItem gave checkbox semantics, letting several stay
    // checked at once).
    private const string MicRadioGroupName = "TrayMicrophone";

    private static void PopulateMicrophoneSubMenu(MenuFlyoutSubItem micSub)
    {
        micSub.Items.Clear();
        try
        {
            var audio = ServiceLocator.Get<IAudioCaptureService>();
            var settings = ServiceLocator.Get<AppSettings>();
            var devices = audio.EnumerateDevices();
            var selectedId = settings.AudioDeviceId;

            // "System default" entry mirrors the Settings page so auto-detect
            // is always representable, even when the current default device
            // is unplugged.
            AddMicRadioItem(
                micSub,
                text: "System default (auto-detect)",
                isChecked: selectedId is null,
                onPicked: () =>
                {
                    settings.AudioDeviceId = null;
                    Serilog.Log.Information("Microphone changed via tray: System default");
                });

            foreach (var device in devices)
            {
                var capturedDevice = device;
                AddMicRadioItem(
                    micSub,
                    text: device.DisplayName,
                    isChecked: selectedId is not null && device.Id == selectedId,
                    onPicked: () =>
                    {
                        settings.AudioDeviceId = capturedDevice.Id;
                        Serilog.Log.Information("Microphone changed via tray: {Device}", capturedDevice.DisplayName);
                    });
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to enumerate microphones for tray menu");
            var errorItem = new MenuFlyoutItem { Text = "No devices found", IsEnabled = false };
            micSub.Items.Add(errorItem);
        }
    }

    private static void AddMicRadioItem(
        MenuFlyoutSubItem micSub,
        string text,
        bool isChecked,
        Action onPicked)
    {
        var item = new RadioMenuFlyoutItem
        {
            Text = text,
            GroupName = MicRadioGroupName,
            IsChecked = isChecked,
        };
        micSub.Items.Add(item);

        // Click is the primary signal, but MenuFlyoutSubItem children under
        // H.NotifyIcon's SecondWindow host have been observed to swallow Click
        // in some configurations while still toggling IsChecked. Listening to
        // IsChecked directly is a resilient fallback that also picks up
        // selections driven by the group-name coordination itself.
        // Registered after setting IsChecked above so the initial state
        // assignment doesn't re-fire onPicked.
        item.Click += (_, _) =>
        {
            if (item.IsChecked) onPicked();
        };
        item.RegisterPropertyChangedCallback(
            RadioMenuFlyoutItem.IsCheckedProperty,
            (_, _) =>
            {
                if (item.IsChecked) onPicked();
            });
    }

    private void OnAudioDevicesChanged(object? sender, EventArgs e)
    {
        // Fires on a WASAPI notification thread — marshal to the UI thread
        // before touching MenuFlyoutSubItem.Items (XAML requires UI-thread
        // affinity for dependency-object mutations).
        RefreshMicrophoneSubMenuOnUiThread();
    }

    private void OnAudioDeviceIdChanged(object? sender, string? newValue)
    {
        RefreshMicrophoneSubMenuOnUiThread();
    }

    private void RefreshMicrophoneSubMenuOnUiThread()
    {
        _mainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (_micSub is not null)
            {
                PopulateMicrophoneSubMenu(_micSub);
            }
        });
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
        if (_audioService is not null)
        {
            _audioService.DevicesChanged -= OnAudioDevicesChanged;
            _audioService = null;
        }
        if (_settings is not null)
        {
            _settings.AudioDeviceIdChanged -= OnAudioDeviceIdChanged;
            _settings = null;
        }
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
