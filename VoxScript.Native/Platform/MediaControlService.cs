using System.Runtime.InteropServices;
using VoxScript.Core.Platform;
using Serilog;

namespace VoxScript.Native.Platform;

public sealed class MediaControlService : IMediaControlService
{
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private bool _paused;

    public void PauseMedia()
    {
        if (_paused) return;
        Log.Debug("Sending media pause key");
        SendMediaKey();
        _paused = true;
    }

    public void ResumeMedia()
    {
        if (!_paused) return;
        Log.Debug("Sending media resume key");
        SendMediaKey();
        _paused = false;
    }

    private static void SendMediaKey()
    {
        // Media keys are NOT extended keys — do not use KEYEVENTF_EXTENDEDKEY.
        // Use keybd_event (not SendInput) because SendInput is blocked by UIPI
        // for unpackaged apps.
        keybd_event(VK_MEDIA_PLAY_PAUSE, 0, 0, 0);
        keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_KEYUP, 0);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);
}
