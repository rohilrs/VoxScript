using System.Runtime.InteropServices;
using VoxScript.Core.Platform;
using Serilog;

namespace VoxScript.Native.Platform;

public sealed class MediaControlService : IMediaControlService
{
    private const int WM_APPCOMMAND = 0x0319;
    private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
    private const int FAPPCOMMAND_KEY = 0;

    private bool _paused;

    public void PauseMedia()
    {
        if (_paused) return;
        Log.Debug("Sending media pause via WM_APPCOMMAND");
        SendMediaPlayPause();
        _paused = true;
    }

    public void ResumeMedia()
    {
        if (!_paused) return;
        Log.Debug("Sending media resume via WM_APPCOMMAND");
        SendMediaPlayPause();
        _paused = false;
    }

    private static void SendMediaPlayPause()
    {
        // Send WM_APPCOMMAND to the shell window (Progman).
        // This is more reliable than keybd_event for media keys because:
        // 1. It goes through the shell's app-command pipeline (same as physical media keys)
        // 2. It's not affected by UIPI restrictions on keystroke injection
        // 3. It works regardless of which window has focus
        var shell = GetShellWindow();
        if (shell == IntPtr.Zero)
        {
            Log.Warning("GetShellWindow returned null — cannot send media command");
            return;
        }

        nint lParam = (nint)(APPCOMMAND_MEDIA_PLAY_PAUSE << 16 | FAPPCOMMAND_KEY);
        SendMessageW(shell, WM_APPCOMMAND, shell, lParam);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, nint lParam);
}
