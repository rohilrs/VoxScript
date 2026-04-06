using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using VoxScript.Core.Transcription.Core;
using VoxScript.ViewModels;
using WinRT.Interop;
using Windows.Graphics;

namespace VoxScript.Shell;

public sealed partial class RecordingIndicatorWindow : Window
{
    // ── Win32 constants ──────────────────────────────────────
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const int PillWidth = 400;
    private const int PillHeight = 60;
    private const int BottomMargin = 40;

    private RecordingIndicatorViewModel? _viewModel;
    private readonly Border[] _bars;
    private readonly DispatcherTimer _pulseTimer;
    private readonly Random _random = new();
    private bool _pulsingHigh = true;

    public RecordingIndicatorWindow()
    {
        InitializeComponent();

        _bars = [Bar0, Bar1, Bar2, Bar3, Bar4, Bar5, Bar6];

        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        _pulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _pulseTimer.Tick += (_, _) =>
        {
            _pulsingHigh = !_pulsingHigh;
            PulsingDot.Opacity = _pulsingHigh ? 1.0 : 0.4;
        };
    }

    // ── Initialization ───────────────────────────────────────

    public void Initialize(RecordingIndicatorViewModel viewModel)
    {
        _viewModel = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ShowRequested += OnShowRequested;
        _viewModel.HideRequested += OnHideRequested;
        _viewModel.DismissWithPastedRequested += OnDismissWithPasted;
        _viewModel.ReturnToIdleRequested += OnReturnToIdle;

        FinishButton.Click += (_, _) => _viewModel.FinishCommand.Execute(null);
        CancelButton.Click += (_, _) => _viewModel.CancelCommand.Execute(null);
    }

    // ── ViewModel property changes ───────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RecordingIndicatorViewModel.State):
                DispatcherQueue.TryEnqueue(UpdateVisualState);
                break;

            case nameof(RecordingIndicatorViewModel.AudioLevel):
                DispatcherQueue.TryEnqueue(() => UpdateWaveform(_viewModel!.AudioLevel));
                break;

            case nameof(RecordingIndicatorViewModel.ElapsedTime):
                DispatcherQueue.TryEnqueue(() => TimerText.Text = _viewModel!.ElapsedTime);
                break;

            case nameof(RecordingIndicatorViewModel.IsToggleMode):
                DispatcherQueue.TryEnqueue(UpdateVisualState);
                break;
        }
    }

    // ── Visual state management ──────────────────────────────

    private void UpdateVisualState()
    {
        if (_viewModel is null) return;

        // Reset all elements to collapsed
        IdleMicIcon.Visibility = Visibility.Collapsed;
        PulsingDot.Visibility = Visibility.Collapsed;
        Spinner.Visibility = Visibility.Collapsed;
        Spinner.IsActive = false;
        PastedCheck.Visibility = Visibility.Collapsed;
        WaveformPanel.Visibility = Visibility.Collapsed;
        TimerText.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Collapsed;
        ButtonSeparator.Visibility = Visibility.Collapsed;
        FinishButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;

        switch (_viewModel.State)
        {
            case RecordingState.Idle:
                IdleMicIcon.Visibility = Visibility.Visible;
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = "Ready";
                StatusText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x88, 0x88, 0x88));
                PillBorder.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
                StopPulsingDot();
                break;

            case RecordingState.Recording:
                PulsingDot.Visibility = Visibility.Visible;
                WaveformPanel.Visibility = Visibility.Visible;
                TimerText.Visibility = Visibility.Visible;
                ButtonSeparator.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Visible;

                if (_viewModel.IsToggleMode)
                    FinishButton.Visibility = Visibility.Visible;

                PillBorder.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x66, 0xCC, 0x44, 0x44));
                StartPulsingDot();
                break;

            case RecordingState.Transcribing:
            case RecordingState.Enhancing:
                Spinner.Visibility = Visibility.Visible;
                Spinner.IsActive = true;
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = "Transcribing...";
                StatusText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE0, 0xE0, 0xE0));
                PillBorder.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x66, 0x7D, 0x84, 0xB2));
                StopPulsingDot();
                break;
        }
    }

    // ── Waveform ─────────────────────────────────────────────

    private void UpdateWaveform(float level)
    {
        var clamped = Math.Clamp(level, 0f, 1f);
        foreach (var bar in _bars)
        {
            var variation = 0.6 + (_random.NextDouble() * 0.8); // 0.6 to 1.4
            var height = 4.0 + (clamped * 18.0 * variation);
            bar.Height = Math.Clamp(height, 4.0, 22.0);
        }
    }

    // ── Pulsing dot animation ────────────────────────────────

    private void StartPulsingDot()
    {
        _pulsingHigh = true;
        PulsingDot.Opacity = 1.0;
        _pulseTimer.Start();
    }

    private void StopPulsingDot()
    {
        _pulseTimer.Stop();
        PulsingDot.Opacity = 1.0;
    }

    // ── Event handlers ───────────────────────────────────────

    private void OnShowRequested()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            RootGrid.Opacity = 1;
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
        await DispatcherQueue.EnqueueAsync(() =>
        {
            // Show green "Pasted" state
            IdleMicIcon.Visibility = Visibility.Collapsed;
            PulsingDot.Visibility = Visibility.Collapsed;
            Spinner.Visibility = Visibility.Collapsed;
            Spinner.IsActive = false;
            WaveformPanel.Visibility = Visibility.Collapsed;
            TimerText.Visibility = Visibility.Collapsed;
            ButtonSeparator.Visibility = Visibility.Collapsed;
            FinishButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            StopPulsingDot();

            PastedCheck.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = "Pasted";
            StatusText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x33, 0x99, 0x66));
            PillBorder.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x66, 0x33, 0x99, 0x66));
        });

        await Task.Delay(1000);

        DispatcherQueue.TryEnqueue(() =>
        {
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

            fadeOut.Completed += (_, _) =>
            {
                if (_viewModel is not null && !_viewModel.IsAlwaysVisible)
                    AppWindow.Hide();
            };

            fadeOut.Begin();
        });
    }

    private async void OnReturnToIdle()
    {
        await Task.Delay(1400);

        DispatcherQueue.TryEnqueue(() =>
        {
            RootGrid.Opacity = 1;
            UpdateVisualState();
        });
    }

    // ── Public API ───────────────────────────────────────────

    public void OnDisplayChanged()
    {
        if (AppWindow.IsVisible)
            PositionBottomCenter();
    }

    // ── Window styles (Win32) ────────────────────────────────

    private void ApplyWindowStyles()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // ── Positioning ──────────────────────────────────────────

    private void PositionBottomCenter()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi / 96.0;

        var physicalWidth = (int)(PillWidth * scale);
        var physicalHeight = (int)(PillHeight * scale);

        var monitor = MonitorFromWindow(hwnd, 0x00000002); // MONITOR_DEFAULTTONEAREST
        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref monitorInfo);

        var workArea = monitorInfo.rcWork;
        var x = workArea.left + ((workArea.right - workArea.left - physicalWidth) / 2);
        var y = workArea.bottom - physicalHeight - (int)(BottomMargin * scale);

        AppWindow.MoveAndResize(new RectInt32(x, y, physicalWidth, physicalHeight));

        // Clip the window to a rounded rectangle so no background shows around the pill
        var cornerDiameter = (int)(48 * scale); // 24px radius * 2
        var rgn = CreateRoundRectRgn(0, 0, physicalWidth + 1, physicalHeight + 1,
            cornerDiameter, cornerDiameter);
        SetWindowRgn(hwnd, rgn, true);
    }

    // ── P/Invoke ─────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}

// ── DispatcherQueue helper ───────────────────────────────────
// Minimal helper so we can await a DispatcherQueue.TryEnqueue call.

internal static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this Microsoft.UI.Dispatching.DispatcherQueue queue, Action action)
    {
        var tcs = new TaskCompletionSource();
        if (!queue.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }))
        {
            tcs.SetException(new InvalidOperationException("Failed to enqueue to DispatcherQueue."));
        }
        return tcs.Task;
    }
}
