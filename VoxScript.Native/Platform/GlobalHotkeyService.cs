// VoxScript.Native/Platform/GlobalHotkeyService.cs
using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace VoxScript.Native.Platform;

/// <summary>
/// Global hotkey service supporting combo keys via low-level keyboard hook.
///
/// Behavior:
///   - Ctrl+Win held → recording starts immediately (push-to-talk / hold mode)
///   - Space pressed while holding Ctrl+Win → locks recording on (toggle mode);
///     releasing Ctrl+Win will NOT stop recording
///   - Ctrl+Win released then re-pressed while toggle-locked → recording stops
///   - Ctrl+Win+Space while toggle-locked → recording stops
///
/// Note: Windows intercepts Win+Space for input-language switching, which causes
/// a Win keyup to arrive before Space keydown. The hold-stop is deferred briefly
/// (200ms) so Space can still convert to toggle mode.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    public event EventHandler? RecordingStartRequested;
    public event EventHandler? RecordingStopRequested;
    public event EventHandler? RecordingToggleRequested;
    public event EventHandler? RecordingCancelRequested;

    private IntPtr _hookHandle = IntPtr.Zero;
    private Win32NativeMethods.LowLevelKeyboardProc? _hookProc;
    private bool _disposed;

    // Modifier state tracking
    private bool _ctrlDown;
    private bool _lWinDown;
    private bool _rWinDown;
    private bool _shiftDown;
    private bool _altDown;

    // Toggle hotkey: combo keys that toggle recording on/off
    private HotkeyCombo? _toggleCombo;

    // Hold hotkey: modifier-only combo for push-to-talk
    private HotkeyCombo? _holdCombo;

    // Cancel hotkey: abort recording without transcribing
    private HotkeyCombo? _cancelCombo;
    private bool _holdActive;       // Ctrl+Win held, recording started via hold
    private bool _toggleLocked;     // Space converted hold to toggle mode
    private DateTime _holdStartTime;

    // After toggle-lock, require modifiers to be fully released before the
    // hold combo can stop the toggle (prevents immediate stop while user
    // is still holding Ctrl+Win from the original hold).
    private bool _awaitModRelease;

    // After cancel, suppress hold-start until modifiers are fully released
    // (prevents Ctrl+Win still being held from immediately restarting recording)
    private bool _cancelledAwaitRelease;

    // Track trigger key state to suppress key-repeat floods
    private bool _triggerKeyHeld;

    // Deferred stop: when hold modifiers release, delay stop briefly so Space
    // can still arrive and convert to toggle (Win+Space OS interception workaround)
    private Timer? _deferredStopTimer;
    private bool _stopDeferred;

    // Virtual key codes
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LMENU = 0xA4;  // Left Alt
    private const int VK_RMENU = 0xA5;  // Right Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;
    private const int VK_MENU = 0x12;   // Alt
    private const int VK_SPACE = 0x20;

    public GlobalHotkeyService()
    {
        // Default: Ctrl+Win+Space to toggle, Ctrl+Win hold for push-to-talk, Esc to cancel
        _toggleCombo = new HotkeyCombo(Modifiers: ModifierKeys.Ctrl | ModifierKeys.Win, TriggerKey: VK_SPACE);
        _holdCombo = new HotkeyCombo(Modifiers: ModifierKeys.Ctrl | ModifierKeys.Win, TriggerKey: null);
        _cancelCombo = new HotkeyCombo(Modifiers: ModifierKeys.None, TriggerKey: 0x1B); // Esc
    }

    /// <summary>
    /// Configure the toggle hotkey (press to start, press again to stop).
    /// </summary>
    public void SetToggleHotkey(ModifierKeys modifiers, int? triggerKey)
    {
        _toggleCombo = new HotkeyCombo(modifiers, triggerKey);
    }

    /// <summary>
    /// Configure the hold hotkey (hold to record, release to stop).
    /// Typically modifier-only (e.g., Ctrl+Win held down).
    /// </summary>
    public void SetHoldHotkey(ModifierKeys modifiers, int? triggerKey)
    {
        _holdCombo = new HotkeyCombo(modifiers, triggerKey);
    }

    /// <summary>
    /// Configure the cancel hotkey (cancel recording without transcribing).
    /// </summary>
    public void SetCancelHotkey(ModifierKeys modifiers, int? triggerKey)
    {
        _cancelCombo = new HotkeyCombo(modifiers, triggerKey);
    }

    public void Register()
    {
        if (_hookHandle != IntPtr.Zero) return;

        _hookProc = HookCallback;

        using var proc = Process.GetCurrentProcess();
        using var mod = proc.MainModule!;
        var hMod = Win32NativeMethods.GetModuleHandle(mod.ModuleName);

        _hookHandle = Win32NativeMethods.SetWindowsHookEx(
            Win32NativeMethods.WH_KEYBOARD_LL,
            _hookProc,
            hMod,
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            Log.Error("SetWindowsHookEx failed with error {Error}", err);
        }
        else
        {
            Log.Debug("Low-level keyboard hook installed (handle: {Handle})", _hookHandle);
        }
    }

    public void Unregister()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            Win32NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _holdActive = false;
            _toggleLocked = false;
            _awaitModRelease = false;
            CancelDeferredStop();
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            int msg = (int)wParam;
            bool isDown = msg == Win32NativeMethods.WM_KEYDOWN
                       || msg == Win32NativeMethods.WM_SYSKEYDOWN;
            bool isUp = msg == Win32NativeMethods.WM_KEYUP
                     || msg == Win32NativeMethods.WM_SYSKEYUP;

            // Update modifier state
            UpdateModifierState(vkCode, isDown, isUp);

            // Track trigger key state to suppress key-repeat floods
            if (_toggleCombo is { TriggerKey: not null } tck)
            {
                if (vkCode == tck.TriggerKey)
                {
                    if (isUp) _triggerKeyHeld = false;
                    else if (isDown && _triggerKeyHeld) goto passThrough; // repeat — ignore
                    else if (isDown) _triggerKeyHeld = true;
                }
            }

            var currentMods = GetCurrentModifiers();
            bool consumed = false;

            // 0. Check cancel hotkey (e.g. Esc) — while recording is active,
            //    accept the cancel trigger key regardless of which modifiers are held
            //    (so Esc cancels even while Ctrl+Win are still down in hold mode)
            if (_cancelCombo is { TriggerKey: not null } cc
                && isDown && vkCode == cc.TriggerKey
                && (_holdActive || _toggleLocked || _stopDeferred))
            {
                _holdActive = false;
                _toggleLocked = false;
                _awaitModRelease = false;
                _cancelledAwaitRelease = true;
                CancelDeferredStop();
                Log.Debug("Recording cancelled via {Combo}", cc);
                ThreadPool.QueueUserWorkItem(_ =>
                    RecordingCancelRequested?.Invoke(this, EventArgs.Empty));
                consumed = true;
            }

            // 1. Check trigger key (Space)
            if (_toggleCombo is { TriggerKey: not null } tc
                && isDown && vkCode == tc.TriggerKey)
            {
                if (_holdActive || _stopDeferred)
                {
                    // Space arrived while hold is active (or just deferred due to
                    // Windows swallowing Win key) — lock recording on (toggle mode)
                    _holdActive = false;
                    _toggleLocked = true;
                    _awaitModRelease = true; // must release mods before hold can stop toggle
                    CancelDeferredStop();
                    Log.Debug("Hold converted to toggle-locked mode");
                    consumed = true;
                }
                else if (_toggleLocked)
                {
                    // Already toggle-locked — stop recording
                    _toggleLocked = false;
                    _awaitModRelease = false;
                    Log.Debug("Toggle-locked recording stopped via trigger key");
                    ThreadPool.QueueUserWorkItem(_ =>
                        RecordingToggleRequested?.Invoke(this, EventArgs.Empty));
                    consumed = true;
                }
                else if (currentMods == tc.Modifiers)
                {
                    // No hold active — plain toggle (requires full modifier match)
                    Log.Debug("Toggle requested via {Combo}", tc);
                    ThreadPool.QueueUserWorkItem(_ =>
                        RecordingToggleRequested?.Invoke(this, EventArgs.Empty));
                    consumed = true;
                }
            }

            // 2. Check hold combo (modifier-only push-to-talk)
            if (!consumed && _holdCombo is { } hc)
            {
                bool holdModsMatch = (currentMods & hc.Modifiers) == hc.Modifiers;

                // Clear the "await mod release" gate once modifiers are fully released
                if (_awaitModRelease && !holdModsMatch)
                    _awaitModRelease = false;

                // Clear the cancel gate once hold modifiers are fully released
                if (_cancelledAwaitRelease && !holdModsMatch)
                    _cancelledAwaitRelease = false;

                if (holdModsMatch && !_holdActive && !_toggleLocked && !_cancelledAwaitRelease)
                {
                    CancelDeferredStop();
                    _holdActive = true;
                    _holdStartTime = DateTime.UtcNow;
                    Log.Debug("Hold recording started (modifiers: {Mods})", currentMods);
                    ThreadPool.QueueUserWorkItem(_ =>
                        RecordingStartRequested?.Invoke(this, EventArgs.Empty));
                }
                else if (holdModsMatch && _toggleLocked && !_awaitModRelease)
                {
                    // Ctrl+Win re-pressed after release while toggle-locked — stop recording
                    _toggleLocked = false;
                    Log.Debug("Toggle-locked recording stopped via hold combo re-press");
                    ThreadPool.QueueUserWorkItem(_ =>
                        RecordingStopRequested?.Invoke(this, EventArgs.Empty));
                }
                else if (_holdActive && !holdModsMatch)
                {
                    // Modifiers released — defer stop to allow Space to arrive
                    // (Windows Win+Space interception sends Win keyup before Space keydown)
                    _holdActive = false;
                    ScheduleDeferredStop();
                }
                // If _toggleLocked and modifiers released → do nothing, recording continues
            }
        }

        passThrough:
        return Win32NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void ScheduleDeferredStop()
    {
        CancelDeferredStop();
        _stopDeferred = true;
        _deferredStopTimer = new Timer(_ =>
        {
            if (_stopDeferred)
            {
                _stopDeferred = false;
                Log.Debug("Hold recording stopped (deferred, held {Held:F0}ms)",
                    (DateTime.UtcNow - _holdStartTime).TotalMilliseconds);
                ThreadPool.QueueUserWorkItem(_ =>
                    RecordingStopRequested?.Invoke(this, EventArgs.Empty));
            }
        }, null, 200, Timeout.Infinite);
    }

    private void CancelDeferredStop()
    {
        _stopDeferred = false;
        _deferredStopTimer?.Dispose();
        _deferredStopTimer = null;
    }

    private void UpdateModifierState(int vkCode, bool isDown, bool isUp)
    {
        switch (vkCode)
        {
            case VK_LCONTROL or VK_RCONTROL or VK_CONTROL:
                if (isDown) _ctrlDown = true;
                else if (isUp) _ctrlDown = false;
                break;
            case VK_LSHIFT or VK_RSHIFT or VK_SHIFT:
                if (isDown) _shiftDown = true;
                else if (isUp) _shiftDown = false;
                break;
            case VK_LMENU or VK_RMENU or VK_MENU:
                if (isDown) _altDown = true;
                else if (isUp) _altDown = false;
                break;
            case VK_LWIN or VK_RWIN:
                if (isDown) { _lWinDown = vkCode == VK_LWIN; _rWinDown = vkCode == VK_RWIN; }
                else if (isUp) { if (vkCode == VK_LWIN) _lWinDown = false; else _rWinDown = false; }
                break;
        }
    }

    private ModifierKeys GetCurrentModifiers()
    {
        var m = ModifierKeys.None;
        if (_ctrlDown) m |= ModifierKeys.Ctrl;
        if (_shiftDown) m |= ModifierKeys.Shift;
        if (_altDown) m |= ModifierKeys.Alt;
        if (_lWinDown || _rWinDown) m |= ModifierKeys.Win;
        return m;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Unregister();
            _disposed = true;
        }
    }
}

[Flags]
public enum ModifierKeys
{
    None  = 0,
    Ctrl  = 1,
    Shift = 2,
    Alt   = 4,
    Win   = 8,
}

public sealed record HotkeyCombo(ModifierKeys Modifiers, int? TriggerKey);
