# Recording Indicator Bar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a floating always-on-top dark translucent pill that shows recording status, waveform, timer, and contextual controls (Finish/Cancel), visible across the full recording → transcription → paste lifecycle.

**Architecture:** A second WinUI 3 `Window` managed by a new `RecordingIndicatorManager` in the Shell layer. The engine exposes audio level and recording mode; the indicator window subscribes via `PropertyChanged`. Win32 interop handles always-on-top, no-taskbar, and borderless styling.

**Tech Stack:** WinUI 3, CommunityToolkit.Mvvm, Win32 P/Invoke (SetWindowPos, SetWindowLongPtr), NAudio (existing WASAPI capture)

---

### Task 1: Add RecordingIndicatorMode Enum and Update Settings

**Files:**
- Create: `VoxScript.Core/Settings/RecordingIndicatorMode.cs`
- Modify: `VoxScript.Core/Settings/AppSettings.cs:118-122`

- [ ] **Step 1: Create the enum**

Create `VoxScript.Core/Settings/RecordingIndicatorMode.cs`:

```csharp
namespace VoxScript.Core.Settings;

public enum RecordingIndicatorMode
{
    Off,
    AlwaysVisible,
    DuringRecording
}
```

- [ ] **Step 2: Replace the bool property in AppSettings**

In `VoxScript.Core/Settings/AppSettings.cs`, replace lines 118-122:

```csharp
// OLD:
public bool RecordingIndicatorEnabled
{
    get => _store.Get<bool?>(nameof(RecordingIndicatorEnabled)) ?? false;
    set => _store.Set(nameof(RecordingIndicatorEnabled), value);
}
```

With:

```csharp
public RecordingIndicatorMode RecordingIndicatorMode
{
    get => _store.Get<RecordingIndicatorMode?>(nameof(RecordingIndicatorMode)) ?? RecordingIndicatorMode.Off;
    set => _store.Set(nameof(RecordingIndicatorMode), value);
}
```

- [ ] **Step 3: Build to verify no compile errors**

Run: `dotnet build VoxScript.Core`
Expected: Build succeeds. (Other projects referencing `RecordingIndicatorEnabled` will break — fixed in Task 4.)

- [ ] **Step 4: Commit**

```bash
git add VoxScript.Core/Settings/RecordingIndicatorMode.cs VoxScript.Core/Settings/AppSettings.cs
git commit -m "feat: replace RecordingIndicatorEnabled bool with RecordingIndicatorMode enum"
```

---

### Task 2: Expose Audio Level from VoxScriptEngine

**Files:**
- Modify: `VoxScript.Core/Transcription/Core/VoiceInkEngine.cs:28-29,78-83,109-112`

The engine's audio callbacks already receive PCM byte arrays. We add an `AudioLevel` property (float 0–1) computed from the RMS of each chunk, and a helper method.

- [ ] **Step 1: Add AudioLevel observable property and RMS helper**

In `VoxScript.Core/Transcription/Core/VoiceInkEngine.cs`, after line 29 (`private RecordingState _state = RecordingState.Idle;`), add:

```csharp
[ObservableProperty]
private float _audioLevel; // 0.0 to 1.0, RMS of current audio chunk
```

At the bottom of the class (before the closing `}`), add a static helper:

```csharp
/// <summary>
/// Compute RMS of 16-bit PCM samples, normalized to 0.0–1.0.
/// </summary>
private static float ComputeRms(byte[] data, int count)
{
    int sampleCount = count / 2; // 16-bit = 2 bytes per sample
    if (sampleCount == 0) return 0f;

    double sumSquares = 0;
    for (int i = 0; i < count - 1; i += 2)
    {
        short sample = (short)(data[i] | (data[i + 1] << 8));
        sumSquares += sample * (double)sample;
    }

    double rms = Math.Sqrt(sumSquares / sampleCount);
    return (float)Math.Min(rms / short.MaxValue, 1.0);
}
```

- [ ] **Step 2: Wire RMS into the file-based audio callback**

In `VoxScript.Core/Transcription/Core/VoiceInkEngine.cs`, replace the file-based callback (lines 109-112):

```csharp
// OLD:
await _audio.StartAsync(_settings.AudioDeviceId, (data, count) =>
{
    _wavStream?.Write(data, 0, count);
}, ct);
```

With:

```csharp
await _audio.StartAsync(_settings.AudioDeviceId, (data, count) =>
{
    _wavStream?.Write(data, 0, count);
    AudioLevel = ComputeRms(data, count);
}, ct);
```

- [ ] **Step 3: Wire RMS into the pre-connect buffer callback**

In `VoxScript.Core/Transcription/Core/VoiceInkEngine.cs`, replace the buffer callback (lines 78-83):

```csharp
// OLD:
Action<byte[], int> bufferChunk = (data, count) =>
{
    var copy = new byte[count];
    Array.Copy(data, copy, count);
    _preConnectBuffer.Add((copy, count));
};
```

With:

```csharp
Action<byte[], int> bufferChunk = (data, count) =>
{
    var copy = new byte[count];
    Array.Copy(data, copy, count);
    _preConnectBuffer.Add((copy, count));
    AudioLevel = ComputeRms(data, count);
};
```

- [ ] **Step 4: Reset AudioLevel when recording stops**

In `StopAndTranscribeAsync()`, after `State = RecordingState.Transcribing;` (line 124), add:

```csharp
AudioLevel = 0f;
```

In `CancelRecordingAsync()`, after `State = RecordingState.Idle;` (line 207), add:

```csharp
AudioLevel = 0f;
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build VoxScript.Core`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add VoxScript.Core/Transcription/Core/VoiceInkEngine.cs
git commit -m "feat: expose AudioLevel property on VoxScriptEngine for waveform visualization"
```

---

### Task 3: Expose Recording Mode (Hold vs Toggle) from GlobalHotkeyService

**Files:**
- Modify: `VoxScript.Native/Platform/GlobalHotkeyService.cs:48-49`
- Modify: `VoxScript.Core/Transcription/Core/VoiceInkEngine.cs`

The indicator needs to know if the current session is hold or toggle to show/hide the Finish button. `GlobalHotkeyService` already tracks `_holdActive` and `_toggleLocked` — expose a public property.

- [ ] **Step 1: Add IsToggleMode property to GlobalHotkeyService**

In `VoxScript.Native/Platform/GlobalHotkeyService.cs`, after line 27 (`public event EventHandler? RecordingCancelRequested;`), add:

```csharp
/// <summary>
/// True when recording is in toggle-locked mode (Space converted hold to toggle).
/// False when in hold mode or not recording.
/// </summary>
public bool IsToggleMode => _toggleLocked;
```

- [ ] **Step 2: Add IsToggleMode observable to VoxScriptEngine**

The engine is in Core (no reference to Native), so it can't read GlobalHotkeyService directly. Instead, add a settable property on the engine that App.xaml.cs sets when recording starts.

In `VoxScript.Core/Transcription/Core/VoiceInkEngine.cs`, after the `AudioLevel` property, add:

```csharp
[ObservableProperty]
private bool _isToggleMode;
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build VoxScript.slnx`
Expected: Build may fail on SettingsViewModel referencing old `RecordingIndicatorEnabled` — that's expected, fixed in Task 4.

- [ ] **Step 4: Commit**

```bash
git add VoxScript.Native/Platform/GlobalHotkeyService.cs VoxScript.Core/Transcription/Core/VoiceInkEngine.cs
git commit -m "feat: expose IsToggleMode on GlobalHotkeyService and VoxScriptEngine"
```

---

### Task 4: Update SettingsViewModel and SettingsPage for RecordingIndicatorMode

**Files:**
- Modify: `VoxScript/ViewModels/SettingsViewModel.cs:201-203,495-535`
- Modify: `VoxScript/Views/SettingsPage.xaml:388-404`

- [ ] **Step 1: Replace bool property with enum in SettingsViewModel**

In `VoxScript/ViewModels/SettingsViewModel.cs`, replace lines 201-203:

```csharp
// OLD:
// Disabled features (stored but UI is non-interactive)
[ObservableProperty]
public partial bool RecordingIndicatorEnabled { get; set; }
```

With:

```csharp
[ObservableProperty]
public partial int RecordingIndicatorModeIndex { get; set; }
partial void OnRecordingIndicatorModeIndexChanged(int value)
{
    _settings.RecordingIndicatorMode = (RecordingIndicatorMode)value;
}

public List<string> RecordingIndicatorModes { get; } = ["Off", "Always visible", "Only during recording"];
```

Add the using at the top of the file if not already present:

```csharp
using VoxScript.Core.Settings;
```

- [ ] **Step 2: Update LoadSettings to use new property**

In `VoxScript/ViewModels/SettingsViewModel.cs`, in the `LoadSettings()` method, replace line 501:

```csharp
// OLD:
RecordingIndicatorEnabled = _settings.RecordingIndicatorEnabled;
```

With:

```csharp
RecordingIndicatorModeIndex = (int)_settings.RecordingIndicatorMode;
```

- [ ] **Step 3: Update SettingsPage XAML**

In `VoxScript/Views/SettingsPage.xaml`, replace lines 388-404 (the disabled recording indicator toggle):

```xml
<!-- OLD: disabled ToggleSwitch -->
<Grid Opacity="0.5" ColumnSpacing="32">
    ...
    <ToggleSwitch ... IsEnabled="False" ... />
</Grid>
```

With:

```xml
<Grid ColumnSpacing="32">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <StackPanel VerticalAlignment="Center">
        <TextBlock Text="Recording indicator"
                   FontSize="15" Foreground="{StaticResource BrandForegroundBrush}" />
        <TextBlock Text="Show floating bar when dictating"
                   FontSize="13" Foreground="{StaticResource BrandMutedBrush}" />
    </StackPanel>
    <ComboBox Grid.Column="1"
              ItemsSource="{x:Bind ViewModel.RecordingIndicatorModes}"
              SelectedIndex="{x:Bind ViewModel.RecordingIndicatorModeIndex, Mode=TwoWay}"
              Width="200"
              VerticalAlignment="Center" />
</Grid>
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build VoxScript.slnx`
Expected: Build succeeds (all references to old `RecordingIndicatorEnabled` are replaced).

- [ ] **Step 5: Commit**

```bash
git add VoxScript/ViewModels/SettingsViewModel.cs VoxScript/Views/SettingsPage.xaml
git commit -m "feat: replace recording indicator toggle with Off/AlwaysVisible/DuringRecording dropdown"
```

---

### Task 5: Create RecordingIndicatorViewModel

**Files:**
- Create: `VoxScript/ViewModels/RecordingIndicatorViewModel.cs`

This ViewModel subscribes to `VoxScriptEngine.PropertyChanged` and drives the indicator UI: state, audio level, timer, mode, and button commands.

- [ ] **Step 1: Create the ViewModel**

Create `VoxScript/ViewModels/RecordingIndicatorViewModel.cs`:

```csharp
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoxScript.Core.Transcription.Core;

namespace VoxScript.ViewModels;

public sealed partial class RecordingIndicatorViewModel : ObservableObject, IDisposable
{
    private readonly VoxScriptEngine _engine;
    private System.Threading.Timer? _timer;
    private DateTime _recordingStartTime;

    [ObservableProperty]
    private RecordingState _state = RecordingState.Idle;

    [ObservableProperty]
    private float _audioLevel;

    [ObservableProperty]
    private bool _isToggleMode;

    [ObservableProperty]
    private string _elapsedTime = "0:00";

    /// <summary>
    /// Raised when the indicator should show (recording started or always-visible idle).
    /// </summary>
    public event Action? ShowRequested;

    /// <summary>
    /// Raised when the indicator should hide (after paste dismiss or cancel).
    /// </summary>
    public event Action? HideRequested;

    /// <summary>
    /// Raised when the indicator should play the "Pasted" dismiss sequence
    /// (1s linger + 300ms fade).
    /// </summary>
    public event Action? DismissWithPastedRequested;

    public RecordingIndicatorViewModel(VoxScriptEngine engine)
    {
        _engine = engine;
        _engine.PropertyChanged += OnEnginePropertyChanged;
        _engine.TranscriptionCompleted += OnTranscriptionCompleted;
    }

    private void OnEnginePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(VoxScriptEngine.State):
                State = _engine.State;
                OnStateChanged();
                break;
            case nameof(VoxScriptEngine.AudioLevel):
                AudioLevel = _engine.AudioLevel;
                break;
            case nameof(VoxScriptEngine.IsToggleMode):
                IsToggleMode = _engine.IsToggleMode;
                break;
        }
    }

    private void OnStateChanged()
    {
        switch (State)
        {
            case RecordingState.Recording:
                _recordingStartTime = DateTime.UtcNow;
                ElapsedTime = "0:00";
                StartTimer();
                ShowRequested?.Invoke();
                break;

            case RecordingState.Transcribing:
            case RecordingState.Enhancing:
                StopTimer();
                // Stay visible — spinner shown by UI binding to State
                break;

            case RecordingState.Idle:
                StopTimer();
                // Hide is handled by TranscriptionCompleted (pasted) or cancel (immediate)
                break;
        }
    }

    private void OnTranscriptionCompleted(object? sender, string text)
    {
        // Transcription done + paste happened → show "Pasted" then fade
        DismissWithPastedRequested?.Invoke();
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        await _engine.StopAndTranscribeAsync();
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await _engine.CancelRecordingAsync();
        HideRequested?.Invoke();
    }

    private void StartTimer()
    {
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ =>
        {
            var elapsed = DateTime.UtcNow - _recordingStartTime;
            ElapsedTime = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose()
    {
        _engine.PropertyChanged -= OnEnginePropertyChanged;
        _engine.TranscriptionCompleted -= OnTranscriptionCompleted;
        StopTimer();
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build VoxScript`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add VoxScript/ViewModels/RecordingIndicatorViewModel.cs
git commit -m "feat: add RecordingIndicatorViewModel with state, timer, and audio level tracking"
```

---

### Task 6: Create RecordingIndicatorWindow (XAML + Code-Behind)

**Files:**
- Create: `VoxScript/Shell/RecordingIndicatorWindow.xaml`
- Create: `VoxScript/Shell/RecordingIndicatorWindow.xaml.cs`

This is the secondary WinUI 3 window — dark translucent pill with waveform, timer, and buttons.

- [ ] **Step 1: Create the XAML layout**

Create `VoxScript/Shell/RecordingIndicatorWindow.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Window x:Class="VoxScript.Shell.RecordingIndicatorWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="VoxScript Recording">

    <Grid x:Name="RootGrid" Background="Transparent"
          HorizontalAlignment="Center" VerticalAlignment="Center">

        <!-- The pill container -->
        <Border x:Name="PillBorder"
                Background="#EB1E1E1E"
                CornerRadius="24"
                Padding="10,10,14,10"
                BorderThickness="1"
                BorderBrush="#66CC4444"
                HorizontalAlignment="Center"
                VerticalAlignment="Center">
            <Border.Shadow>
                <ThemeShadow />
            </Border.Shadow>

            <StackPanel Orientation="Horizontal" Spacing="14"
                        VerticalAlignment="Center">

                <!-- Recording state: pulsing dot -->
                <Ellipse x:Name="PulsingDot"
                         Width="10" Height="10"
                         Fill="#CC4444"
                         VerticalAlignment="Center"
                         Visibility="Collapsed" />

                <!-- Transcribing state: spinner -->
                <ProgressRing x:Name="Spinner"
                              Width="18" Height="18"
                              IsActive="False"
                              Foreground="#7D84B2"
                              VerticalAlignment="Center"
                              Visibility="Collapsed" />

                <!-- Idle state: mic icon (dimmed) -->
                <FontIcon x:Name="IdleMicIcon"
                          Glyph="&#xE720;"
                          FontSize="14"
                          Foreground="#666666"
                          VerticalAlignment="Center"
                          Visibility="Collapsed" />

                <!-- Pasted state: checkmark -->
                <FontIcon x:Name="PastedCheck"
                          Glyph="&#xE73E;"
                          FontSize="16"
                          Foreground="#339966"
                          VerticalAlignment="Center"
                          Visibility="Collapsed" />

                <!-- Waveform bars (recording state) -->
                <StackPanel x:Name="WaveformPanel"
                            Orientation="Horizontal" Spacing="2"
                            VerticalAlignment="Center"
                            Height="24"
                            Visibility="Collapsed">
                    <Border x:Name="Bar0" Width="3" CornerRadius="1" Background="#CC4444" Height="4" VerticalAlignment="Center" />
                    <Border x:Name="Bar1" Width="3" CornerRadius="1" Background="#CC4444" Height="4" VerticalAlignment="Center" />
                    <Border x:Name="Bar2" Width="3" CornerRadius="1" Background="#CC4444" Height="4" VerticalAlignment="Center" />
                    <Border x:Name="Bar3" Width="3" CornerRadius="1" Background="#CC4444" Height="4" VerticalAlignment="Center" />
                    <Border x:Name="Bar4" Width="3" CornerRadius="1" Background="#CC4444" Height="4" VerticalAlignment="Center" />
                    <Border x:Name="Bar5" Width="3" CornerRadius="1" Background="#CC4444" Height="4" VerticalAlignment="Center" />
                    <Border x:Name="Bar6" Width="3" CornerRadius="1" Background="#CC4444" Height="4" VerticalAlignment="Center" />
                </StackPanel>

                <!-- Timer (recording state) -->
                <TextBlock x:Name="TimerText"
                           Text="0:00"
                           FontFamily="Segoe UI"
                           FontSize="13" FontWeight="Medium"
                           Foreground="#E0E0E0"
                           VerticalAlignment="Center"
                           Visibility="Collapsed" />

                <!-- Status text (transcribing/pasted) -->
                <TextBlock x:Name="StatusText"
                           FontFamily="Segoe UI"
                           FontSize="13" FontWeight="Medium"
                           Foreground="#E0E0E0"
                           VerticalAlignment="Center"
                           Visibility="Collapsed" />

                <!-- Separator -->
                <Border x:Name="ButtonSeparator"
                        Width="1" Height="20"
                        Background="#26FFFFFF"
                        VerticalAlignment="Center"
                        Visibility="Collapsed" />

                <!-- Finish button (toggle mode only) -->
                <Button x:Name="FinishButton"
                        Content="Finish"
                        FontFamily="Segoe UI" FontSize="12" FontWeight="SemiBold"
                        Foreground="#5BBD8A"
                        Background="#40339966"
                        BorderBrush="#80339966"
                        CornerRadius="14"
                        Padding="14,4"
                        VerticalAlignment="Center"
                        Visibility="Collapsed" />

                <!-- Cancel button -->
                <Button x:Name="CancelButton"
                        Width="28" Height="28"
                        CornerRadius="8"
                        Background="#14FFFFFF"
                        BorderThickness="0"
                        Padding="0"
                        VerticalAlignment="Center"
                        Visibility="Collapsed">
                    <FontIcon Glyph="&#xE711;" FontSize="14" Foreground="#AAAAAA" />
                </Button>
            </StackPanel>
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 2: Create the code-behind**

Create `VoxScript/Shell/RecordingIndicatorWindow.xaml.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using VoxScript.Core.Transcription.Core;
using VoxScript.ViewModels;
using WinRT.Interop;

namespace VoxScript.Shell;

public sealed partial class RecordingIndicatorWindow : Window
{
    private RecordingIndicatorViewModel? _viewModel;
    private readonly Border[] _bars;
    private readonly Random _barRandom = new(); // slight per-bar variation

    // Win32 constants
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const int PillWidth = 400;
    private const int PillHeight = 60;
    private const int BottomMargin = 40;

    public RecordingIndicatorWindow()
    {
        this.InitializeComponent();

        _bars = [Bar0, Bar1, Bar2, Bar3, Bar4, Bar5, Bar6];

        ExtendsContentIntoTitleBar = true;

        // Hide the title bar buttons completely
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
    }

    public void Initialize(RecordingIndicatorViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ShowRequested += OnShowRequested;
        _viewModel.HideRequested += OnHideRequested;
        _viewModel.DismissWithPastedRequested += OnDismissWithPasted;

        FinishButton.Click += (_, _) => _viewModel.FinishCommand.Execute(null);
        CancelButton.Click += (_, _) => _viewModel.CancelCommand.Execute(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(RecordingIndicatorViewModel.State):
                    UpdateVisualState();
                    break;
                case nameof(RecordingIndicatorViewModel.AudioLevel):
                    UpdateWaveform(_viewModel!.AudioLevel);
                    break;
                case nameof(RecordingIndicatorViewModel.ElapsedTime):
                    TimerText.Text = _viewModel!.ElapsedTime;
                    break;
                case nameof(RecordingIndicatorViewModel.IsToggleMode):
                    UpdateVisualState();
                    break;
            }
        });
    }

    private void UpdateVisualState()
    {
        var state = _viewModel!.State;
        var isToggle = _viewModel.IsToggleMode;

        // Reset all
        IdleMicIcon.Visibility = Visibility.Collapsed;
        PulsingDot.Visibility = Visibility.Collapsed;
        WaveformPanel.Visibility = Visibility.Collapsed;
        TimerText.Visibility = Visibility.Collapsed;
        Spinner.Visibility = Visibility.Collapsed;
        Spinner.IsActive = false;
        PastedCheck.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Collapsed;
        ButtonSeparator.Visibility = Visibility.Collapsed;
        FinishButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;

        switch (state)
        {
            case RecordingState.Recording:
                PillBorder.BorderBrush = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(0x66, 0xCC, 0x44, 0x44));
                PulsingDot.Visibility = Visibility.Visible;
                WaveformPanel.Visibility = Visibility.Visible;
                TimerText.Visibility = Visibility.Visible;
                ButtonSeparator.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;
                if (isToggle)
                    FinishButton.Visibility = Visibility.Visible;
                StartPulsingDot();
                break;

            case RecordingState.Transcribing:
            case RecordingState.Enhancing:
                PillBorder.BorderBrush = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(0x66, 0x7D, 0x84, 0xB2));
                Spinner.Visibility = Visibility.Visible;
                Spinner.IsActive = true;
                StatusText.Text = "Transcribing...";
                StatusText.Visibility = Visibility.Visible;
                StopPulsingDot();
                break;
        }
    }

    private void UpdateWaveform(float level)
    {
        // Map level (0-1) to bar heights (4-22px) with per-bar variation
        for (int i = 0; i < _bars.Length; i++)
        {
            double variation = 0.6 + (_barRandom.NextDouble() * 0.8); // 0.6–1.4
            double height = 4 + (level * 18 * variation);
            _bars[i].Height = Math.Clamp(height, 4, 22);
        }
    }

    private DispatcherTimer? _pulseTimer;
    private bool _pulseHigh = true;

    private void StartPulsingDot()
    {
        _pulseTimer?.Stop();
        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _pulseTimer.Tick += (_, _) =>
        {
            PulsingDot.Opacity = _pulseHigh ? 0.4 : 1.0;
            _pulseHigh = !_pulseHigh;
        };
        _pulseTimer.Start();
    }

    private void StopPulsingDot()
    {
        _pulseTimer?.Stop();
        _pulseTimer = null;
        PulsingDot.Opacity = 1.0;
    }

    private void OnShowRequested()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            RootGrid.Opacity = 1.0;
            UpdateVisualState();
            AppWindow.Show();
            ApplyWindowStyles();
            PositionBottomCenter();
        });
    }

    private void OnHideRequested()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StopPulsingDot();
            AppWindow.Hide();
        });
    }

    private async void OnDismissWithPasted()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            // Show "Pasted" state
            PillBorder.BorderBrush = new SolidColorBrush(
                Windows.UI.Color.FromArgb(0x66, 0x33, 0x99, 0x66));
            PulsingDot.Visibility = Visibility.Collapsed;
            WaveformPanel.Visibility = Visibility.Collapsed;
            TimerText.Visibility = Visibility.Collapsed;
            Spinner.Visibility = Visibility.Collapsed;
            Spinner.IsActive = false;
            ButtonSeparator.Visibility = Visibility.Collapsed;
            FinishButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            StatusText.Visibility = Visibility.Collapsed;

            PastedCheck.Visibility = Visibility.Visible;
            StatusText.Text = "Pasted";
            StatusText.Visibility = Visibility.Visible;

            // 1s linger
            await Task.Delay(1000);

            // 300ms fade out
            var fadeOut = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(animation, RootGrid);
            Storyboard.SetTargetProperty(animation, "Opacity");
            fadeOut.Children.Add(animation);
            fadeOut.Completed += (_, _) => AppWindow.Hide();
            fadeOut.Begin();
        });
    }

    public void ApplyWindowStyles()
    {
        var hwnd = WindowNative.GetWindowHandle(this);

        // Set extended styles: tool window (no taskbar) + no-activate
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE,
            exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        // Always on top
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void PositionBottomCenter()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi / 96.0;

        var physicalWidth = (int)(PillWidth * scale);
        var physicalHeight = (int)(PillHeight * scale);

        // Get work area of the monitor this window is on
        var monitor = MonitorFromWindow(hwnd, 0x00000002); // MONITOR_DEFAULTTONEAREST
        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref monitorInfo);

        var workArea = monitorInfo.rcWork;
        int x = workArea.left + (workArea.right - workArea.left - physicalWidth) / 2;
        int y = workArea.bottom - physicalHeight - (int)(BottomMargin * scale);

        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            x, y, physicalWidth, physicalHeight));
    }

    /// <summary>
    /// Re-center the pill when display configuration changes.
    /// Call from DisplayInformation.DisplayContentsInvalidated or on a timer.
    /// </summary>
    public void OnDisplayChanged()
    {
        if (AppWindow.IsVisible)
            PositionBottomCenter();
    }

    // ── Win32 P/Invoke ──────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build VoxScript`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add VoxScript/Shell/RecordingIndicatorWindow.xaml VoxScript/Shell/RecordingIndicatorWindow.xaml.cs
git commit -m "feat: add RecordingIndicatorWindow with dark pill UI, waveform, timer, and controls"
```

---

### Task 7: Wire Up RecordingIndicatorWindow in App.xaml.cs

**Files:**
- Modify: `VoxScript/App.xaml.cs:21-23,88-92,99-133`

Create the indicator window alongside MainWindow, pass the ViewModel, and set IsToggleMode on the engine when recording mode changes.

- [ ] **Step 1: Add indicator window field and creation**

In `VoxScript/App.xaml.cs`, after line 23 (`private GlobalHotkeyService? _hotkey;`), add:

```csharp
private RecordingIndicatorWindow? _indicatorWindow;
private RecordingIndicatorViewModel? _indicatorViewModel;
```

Add usings at top:

```csharp
using VoxScript.ViewModels;
```

- [ ] **Step 2: Create and initialize the indicator window**

In `VoxScript/App.xaml.cs`, after the SystemTrayManager initialization (line 92: `_trayManager.Initialize();`), add:

```csharp
// Recording indicator overlay
_indicatorViewModel = new RecordingIndicatorViewModel(
    ServiceLocator.Get<VoxScriptEngine>());
_indicatorWindow = new RecordingIndicatorWindow();
_indicatorWindow.Initialize(_indicatorViewModel);
```

- [ ] **Step 3: Set IsToggleMode when hotkey mode changes**

In `VoxScript/App.xaml.cs`, in the `RecordingStartRequested` handler (lines 107-117), after `await engine.StartRecordingAsync(model);` (line 114), add:

```csharp
engine.IsToggleMode = false; // hold mode starts as non-toggle
```

And in the existing `RecordingToggleRequested` handler (lines 99-106), after `await engine.ToggleRecordAsync(model);` (line 104), add:

```csharp
if (engine.State == RecordingState.Recording)
    engine.IsToggleMode = true; // toggle mode
```

Also wire a check in the hold callback: when GlobalHotkeyService transitions from hold to toggle, we need to update the engine. After the hotkey event handlers block (after line 133), add:

```csharp
_hotkey.RecordingToggleRequested += (_, _) =>
{
    // This is also fired when hold converts to toggle via Space
    // The main handler above already sets IsToggleMode=true
};
```

Actually, this is already handled above. But we should also track when Space converts hold to toggle. The simpler approach: check `_hotkey.IsToggleMode` on a timer or just let the ViewModel check. The simplest solution is to poll `_hotkey.IsToggleMode` when the engine state is Recording and update periodically. However, a cleaner approach:

Add after the hotkey event handlers (after line 133):

```csharp
// Poll toggle mode while recording (catches hold→toggle conversion via Space)
engine.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(VoxScriptEngine.State)
        && engine.State == RecordingState.Recording
        && _hotkey!.IsToggleMode)
    {
        _mainWindow!.DispatcherQueue.TryEnqueue(() =>
            engine.IsToggleMode = _hotkey.IsToggleMode);
    }
};
```

Wait — this only fires once on state change. A better approach: add a simple timer in the RecordingStartRequested handler that polls IsToggleMode while recording, since the hold→toggle conversion can happen at any time during recording.

Replace the approach above. Instead, in the `RecordingStartRequested` handler, after setting `engine.IsToggleMode = false;`, start a simple poll:

```csharp
// Poll for hold→toggle conversion (Space key during hold)
_ = Task.Run(async () =>
{
    while (engine.State == RecordingState.Recording)
    {
        await Task.Delay(100);
        if (_hotkey!.IsToggleMode && !engine.IsToggleMode)
        {
            _mainWindow!.DispatcherQueue.TryEnqueue(() =>
                engine.IsToggleMode = true);
        }
    }
});
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build VoxScript`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add VoxScript/App.xaml.cs
git commit -m "feat: wire RecordingIndicatorWindow lifecycle in App.xaml.cs"
```

---

### Task 8: Respect RecordingIndicatorMode Setting

**Files:**
- Modify: `VoxScript/ViewModels/RecordingIndicatorViewModel.cs`

The ViewModel's `ShowRequested` event should only fire based on the setting (Off, AlwaysVisible, DuringRecording).

- [ ] **Step 1: Add settings dependency and gate show logic**

In `VoxScript/ViewModels/RecordingIndicatorViewModel.cs`, add to constructor parameters and field:

```csharp
private readonly AppSettings _settings;

public RecordingIndicatorViewModel(VoxScriptEngine engine, AppSettings settings)
{
    _engine = engine;
    _settings = settings;
    _engine.PropertyChanged += OnEnginePropertyChanged;
    _engine.TranscriptionCompleted += OnTranscriptionCompleted;
}
```

Add using:

```csharp
using VoxScript.Core.Settings;
```

- [ ] **Step 2: Gate ShowRequested on setting value**

In the `OnStateChanged()` method, wrap the `Recording` case's `ShowRequested` call:

```csharp
case RecordingState.Recording:
    _recordingStartTime = DateTime.UtcNow;
    ElapsedTime = "0:00";
    StartTimer();
    if (_settings.RecordingIndicatorMode != RecordingIndicatorMode.Off)
        ShowRequested?.Invoke();
    break;
```

- [ ] **Step 3: Handle cancel hide — only hide if we were shown**

In `CancelAsync()`, gate the hide:

```csharp
[RelayCommand]
private async Task CancelAsync()
{
    await _engine.CancelRecordingAsync();
    HideRequested?.Invoke();
}
```

(This is fine as-is — if the window wasn't shown, Hide is a no-op.)

- [ ] **Step 4: Update App.xaml.cs to pass settings**

In `VoxScript/App.xaml.cs`, update the indicator ViewModel creation:

```csharp
_indicatorViewModel = new RecordingIndicatorViewModel(
    ServiceLocator.Get<VoxScriptEngine>(),
    ServiceLocator.Get<AppSettings>());
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build VoxScript`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add VoxScript/ViewModels/RecordingIndicatorViewModel.cs VoxScript/App.xaml.cs
git commit -m "feat: gate recording indicator visibility on RecordingIndicatorMode setting"
```

---

### Task 9: Handle AlwaysVisible Idle State

**Files:**
- Modify: `VoxScript/Shell/RecordingIndicatorWindow.xaml.cs`
- Modify: `VoxScript/ViewModels/RecordingIndicatorViewModel.cs`

When mode is `AlwaysVisible`, the pill should show a dimmed idle state when not recording.

- [ ] **Step 1: Add idle visual state to the window**

In `RecordingIndicatorWindow.xaml.cs`, in the `UpdateVisualState()` method, add an `Idle` case:

```csharp
case RecordingState.Idle:
    // If always-visible mode, show idle state with mic icon
    PillBorder.BorderBrush = new SolidColorBrush(
        Windows.UI.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    // Show mic icon (dimmed) — reuse PulsingDot position with mic FontIcon
    // The IdleMicIcon element (added in XAML) handles this
    IdleMicIcon.Visibility = Visibility.Visible;
    StatusText.Text = "Ready";
    StatusText.Visibility = Visibility.Visible;
    StatusText.Foreground = new SolidColorBrush(
        Windows.UI.Color.FromArgb(0xFF, 0x88, 0x88, 0x88));
    StopPulsingDot();
    break;
```

- [ ] **Step 2: Show window on startup if AlwaysVisible**

In `RecordingIndicatorViewModel.cs`, add a public method called after initialization:

```csharp
/// <summary>
/// Call after Initialize to show the indicator if mode is AlwaysVisible.
/// </summary>
public void ApplyInitialVisibility()
{
    if (_settings.RecordingIndicatorMode == RecordingIndicatorMode.AlwaysVisible)
        ShowRequested?.Invoke();
}
```

- [ ] **Step 3: Call from App.xaml.cs**

After `_indicatorWindow.Initialize(_indicatorViewModel);`, add:

```csharp
_indicatorViewModel.ApplyInitialVisibility();
```

- [ ] **Step 4: In OnStateChanged, keep visible if AlwaysVisible**

In `RecordingIndicatorViewModel.cs`, update the `Idle` case in `OnStateChanged()`:

The current code has no explicit handling for `Idle` in `OnStateChanged` — the hide is driven by `TranscriptionCompleted` or `CancelAsync`. But for AlwaysVisible, after the pasted dismiss animation completes, we need to return to the idle state instead of staying hidden.

Update the `OnTranscriptionCompleted` handler:

```csharp
private void OnTranscriptionCompleted(object? sender, string text)
{
    if (_settings.RecordingIndicatorMode == RecordingIndicatorMode.AlwaysVisible)
    {
        // Show "Pasted" briefly then return to idle (not hidden)
        DismissWithPastedRequested?.Invoke();
        // The window's dismiss animation will need to return to idle
        // instead of hiding. Signal via a new event:
        ReturnToIdleRequested?.Invoke();
    }
    else
    {
        DismissWithPastedRequested?.Invoke();
    }
}
```

Add the event:

```csharp
public event Action? ReturnToIdleRequested;
```

- [ ] **Step 5: Handle ReturnToIdleRequested in the window**

In `RecordingIndicatorWindow.xaml.cs`, in `Initialize()`, add:

```csharp
_viewModel.ReturnToIdleRequested += OnReturnToIdle;
```

Add the handler:

```csharp
private async void OnReturnToIdle()
{
    // Wait for the pasted dismiss animation (1s + 300ms), then show idle
    await Task.Delay(1400);
    DispatcherQueue.TryEnqueue(() =>
    {
        RootGrid.Opacity = 1.0;
        UpdateVisualState(); // will show idle state since engine.State is Idle
    });
}
```

Also update `OnDismissWithPasted` to NOT call `AppWindow.Hide()` when AlwaysVisible — instead just fade out and let `OnReturnToIdle` restore. Simplest approach: always fade, and let the return-to-idle handler bring it back. The existing `fadeOut.Completed` handler hides the window — we need to conditionally skip that.

Refactor `OnDismissWithPasted` to accept a parameter or check the mode. Since the window doesn't have direct settings access, pass a flag. The simplest fix: expose a public `IsAlwaysVisible` property on the ViewModel:

In `RecordingIndicatorViewModel.cs`:

```csharp
public bool IsAlwaysVisible =>
    _settings.RecordingIndicatorMode == RecordingIndicatorMode.AlwaysVisible;
```

In `OnDismissWithPasted`, change the `fadeOut.Completed` handler:

```csharp
fadeOut.Completed += (_, _) =>
{
    if (!_viewModel!.IsAlwaysVisible)
        AppWindow.Hide();
};
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build VoxScript`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add VoxScript/Shell/RecordingIndicatorWindow.xaml.cs VoxScript/ViewModels/RecordingIndicatorViewModel.cs VoxScript/App.xaml.cs
git commit -m "feat: add AlwaysVisible idle state for recording indicator"
```

---

### Task 10: Handle Window Transparency and Background

**Files:**
- Modify: `VoxScript/Shell/RecordingIndicatorWindow.xaml`
- Modify: `VoxScript/Shell/RecordingIndicatorWindow.xaml.cs`

WinUI 3 windows have a default opaque background. We need the area around the pill to be transparent so it looks like a floating pill on the desktop.

- [ ] **Step 1: Set transparent window background**

In `RecordingIndicatorWindow.xaml.cs`, in the constructor after `this.InitializeComponent();`, add:

```csharp
// Make window background transparent
// WinUI 3 doesn't natively support fully transparent windows,
// so we use a very dark near-black background that matches the pill
this.SystemBackdrop = null;
```

In `ApplyWindowStyles()`, after setting `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, add layered window setup:

```csharp
// Set the window region or use DWM to make non-content areas transparent
// For WinUI 3, the simplest approach is to size the window to exactly
// match the pill and use the pill's dark background as the window background
```

Actually, WinUI 3 transparent windows are tricky. The pragmatic approach: size the window to exactly match the pill dimensions and use the pill background as the window background. The pill's rounded corners will show the dark background in the corners — this is acceptable since the pill background is very dark (#1E1E1E at 92% opacity) and the corners are tiny.

In `RecordingIndicatorWindow.xaml`, update the root Grid:

```xml
<Grid x:Name="RootGrid" Background="#EB1E1E1E">
```

And remove the Background from PillBorder (it inherits from the Grid):

Actually, keep it simple — the window IS the pill. The Grid background matches the pill background. The rounded corners of the Border will show the Grid background in the corner areas, which is the same color. This means the window appears as a dark rectangle but visually looks like a pill because the border and content have rounded corners.

For truly transparent corners, we'd need `DesktopAcrylicBackdrop` with transparent tint or Win32 layered windows. This is a polish item — the dark rectangle approach works well on dark taskbars and is the simplest path.

- [ ] **Step 2: Remove the SystemBackdrop/MicaBackdrop**

Ensure the XAML does NOT have `<Window.SystemBackdrop>` set. The default is fine.

- [ ] **Step 3: Build and test**

Run: `dotnet build VoxScript`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add VoxScript/Shell/RecordingIndicatorWindow.xaml VoxScript/Shell/RecordingIndicatorWindow.xaml.cs
git commit -m "feat: configure indicator window background and sizing"
```

---

### Task 11: Run Tests and Fix Compilation

**Files:**
- Modify: any files with compile errors referencing old `RecordingIndicatorEnabled`

- [ ] **Step 1: Build the full solution**

Run: `dotnet build VoxScript.slnx`

Fix any remaining compile errors (likely in tests referencing `RecordingIndicatorEnabled`).

- [ ] **Step 2: Run all tests**

Run: `dotnet test VoxScript.Tests`
Expected: All existing tests pass. Fix any failures caused by the settings change.

- [ ] **Step 3: Commit any fixes**

```bash
git add -A
git commit -m "fix: resolve compile errors from RecordingIndicatorEnabled → RecordingIndicatorMode migration"
```

---

### Task 12: Manual Smoke Test

**Files:** None (testing only)

- [ ] **Step 1: Launch the app**

Run: `dotnet run --project VoxScript`

- [ ] **Step 2: Verify settings**

Open Settings > App card. Verify the "Recording indicator" dropdown shows "Off", "Always visible", "Only during recording". Change the selection and verify it persists after closing and reopening Settings.

- [ ] **Step 3: Test DuringRecording mode**

Set indicator to "Only during recording". Use the hotkey to start recording. Verify:
- Dark pill appears at bottom center
- Waveform bars animate with voice
- Timer counts up
- Cancel (X) dismisses immediately
- On stop: spinner shows "Transcribing...", then "Pasted" checkmark, then fades

- [ ] **Step 4: Test toggle mode**

Use toggle hotkey (Ctrl+Win+Space). Verify Finish button appears. Click Finish to stop.

- [ ] **Step 5: Test AlwaysVisible mode**

Set to "Always visible". Verify pill shows "Ready" in dimmed state. Start recording — verify it transitions to recording state. After paste, verify it returns to "Ready" instead of disappearing.

- [ ] **Step 6: Test Off mode**

Set to "Off". Start recording. Verify no pill appears.
