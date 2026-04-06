using System.Runtime.InteropServices;
using VoxScript.Core.Platform;
using Serilog;

namespace VoxScript.Native.Platform;

public sealed class MediaControlService : IMediaControlService
{
    private const int VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const int KEYEVENTF_KEYUP = 0x0002;

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
        keybd_event((byte)VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY, 0);
        keybd_event((byte)VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);
}
