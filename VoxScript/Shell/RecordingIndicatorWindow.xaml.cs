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
using WinUIEx;
using Windows.Graphics;

namespace VoxScript.Shell;

public sealed partial class RecordingIndicatorWindow : Window
{
    // ── Win32 constants ──────────────────────────────────────
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_BORDER = 0x00800000;
    private const int WS_CAPTION = 0x00C00000; // WS_BORDER | WS_DLGFRAME
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const int PillHeight = 48;
    private const int BottomMargin = 40;

    private RecordingIndicatorViewModel? _viewModel;
    private readonly Border[] _bars;
    private readonly DispatcherTimer _pulseTimer;
    private readonly Random _random = new();
    private bool _pulsingHigh = true;
    private Storyboard? _activeDismissStoryboard;
    private CancellationTokenSource? _dismissDelayCts;

    public RecordingIndicatorWindow()
    {
        InitializeComponent();

        _bars = [Bar0, Bar1, Bar2, Bar3, Bar4, Bar5, Bar6];

        // Fully transparent window — only the pill Border is visible
        SystemBackdrop = new TransparentTintBackdrop();

        // Force dark theme for consistent element styling
        if (Content is FrameworkElement fe)
            fe.RequestedTheme = ElementTheme.Dark;

        // Strip all window chrome — no border, no title bar, no resize handles.
        // Use the default OverlappedPresenter (not CreateForToolWindow) because
        // tool-window presenters don't support system backdrops on all builds,
        // which causes TransparentTintBackdrop to fall back to an opaque surface.
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
        }

        // Apply Win32 transparency styles immediately after presenter setup.
        // DwmExtendFrameIntoClientArea must run before the first paint to avoid
        // a flash of opaque grey/white background beneath the XAML content.
        ApplyWindowStyles();

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

        // Resize and reposition the window to fit the current state's content
        if (AppWindow.IsVisible)
            PositionBottomCenter();

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
            // Cancel any in-flight dismiss animation from a previous cycle.
            // Without this, a quick release followed by a new recording will have
            // the old fade-out animation overwrite Opacity back to 0 and hide the window.
            CancelDismissAnimation();

            RootGrid.Opacity = 1;
            UpdateVisualState();

            // Apply Win32 styles BEFORE showing the window. DwmExtendFrameIntoClientArea
            // must be in effect before the first paint to prevent the opaque grey/white
            // rectangle from flashing beneath the transparent XAML content.
            ApplyWindowStyles();
            PositionBottomCenter();
            AppWindow.Show();
        });
    }

    private void OnHideRequested()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            CancelDismissAnimation();
            StopPulsingDot();
            AppWindow.Hide();
        });
    }

    private async void OnDismissWithPasted()
    {
        // Cancel any previous dismiss that may still be in-flight
        CancelDismissAnimation();

        var cts = new CancellationTokenSource();
        _dismissDelayCts = cts;

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

            // Shrink window to fit the compact "Pasted" content
            ResizeWindowForWidth(160);
        });

        try
        {
            await Task.Delay(1000, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return; // A new show/dismiss cycle superseded this one
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            // Double-check we haven't been cancelled between the delay and this dispatch
            if (cts.IsCancellationRequested) return;

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

            _activeDismissStoryboard = fadeOut;

            fadeOut.Completed += (_, _) =>
            {
                _activeDismissStoryboard = null;
                if (_viewModel is not null && !_viewModel.IsAlwaysVisible)
                    AppWindow.Hide();
            };

            fadeOut.Begin();
        });
    }

    private void CancelDismissAnimation()
    {
        // Cancel the 1-second "Pasted" linger delay
        _dismissDelayCts?.Cancel();
        _dismissDelayCts?.Dispose();
        _dismissDelayCts = null;

        // Stop any in-flight fade-out storyboard
        if (_activeDismissStoryboard is not null)
        {
            _activeDismissStoryboard.Stop();
            _activeDismissStoryboard = null;
        }
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

        // Convert to a popup window and strip the frame. Popup windows don't get
        // DWM drop shadows, eliminating the rectangular shadow behind the pill.
        var style = GetWindowLongPtr(hwnd, GWL_STYLE);
        style = (style & ~(nint)(WS_THICKFRAME | WS_CAPTION)) | WS_POPUP;
        SetWindowLongPtr(hwnd, GWL_STYLE, style);

        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        // Extend DWM frame into entire client area to achieve per-pixel alpha
        // transparency. Without this, the Win32 window surface paints an opaque
        // background (grey/white rectangle) beneath the XAML content.
        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        // Suppress the Windows 11 automatic 1px DWM border around the window.
        // Even after stripping WS_THICKFRAME/WS_CAPTION, DWM still draws a thin
        // border on all top-level windows. Setting DWMWA_BORDER_COLOR to
        // DWMWA_COLOR_NONE (0xFFFFFFFE) removes it entirely.
        const int DWMWA_BORDER_COLOR = 34;
        uint colorNone = 0xFFFFFFFE; // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorNone, sizeof(uint));

        // Disable DWM non-client rendering so the compositor does not draw a
        // rectangular window shadow around the transparent surface. Without this,
        // a faint shadow rectangle is visible on light-colored backgrounds.
        const int DWMWA_NCRENDERING_POLICY = 2;
        uint ncRenderingDisabled = 1; // DWMNCRP_DISABLED
        DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref ncRenderingDisabled, sizeof(uint));
    }

    // ── Positioning ──────────────────────────────────────────

    private int GetPillWidthForState()
    {
        if (_viewModel is null) return 160;

        return _viewModel.State switch
        {
            RecordingState.Idle => 160,
            RecordingState.Recording => _viewModel.IsToggleMode ? 320 : 280,
            RecordingState.Transcribing or RecordingState.Enhancing => 200,
            _ => 200,
        };
    }

    private void ResizeWindowForWidth(int logicalWidth) => PositionBottomCenter(logicalWidth);

    private void PositionBottomCenter() => PositionBottomCenter(GetPillWidthForState());

    private void PositionBottomCenter(int pillWidth)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi / 96.0;

        var physicalWidth = (int)(pillWidth * scale);
        var physicalHeight = (int)(PillHeight * scale);

        var monitor = MonitorFromWindow(hwnd, 0x00000002); // MONITOR_DEFAULTTONEAREST
        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref monitorInfo);

        var workArea = monitorInfo.rcWork;
        var x = workArea.left + ((workArea.right - workArea.left - physicalWidth) / 2);
        var y = workArea.bottom - physicalHeight - (int)(BottomMargin * scale);

        AppWindow.MoveAndResize(new RectInt32(x, y, physicalWidth, physicalHeight));
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

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);

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
