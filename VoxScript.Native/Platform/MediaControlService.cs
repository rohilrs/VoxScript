using System.Runtime.InteropServices;
using VoxScript.Core.Platform;
using Serilog;

namespace VoxScript.Native.Platform;

public sealed class MediaControlService : IMediaControlService
{
    private const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
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
        var inputs = new INPUT[2];

        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].ki.wVk = VK_MEDIA_PLAY_PAUSE;
        inputs[0].ki.dwFlags = KEYEVENTF_EXTENDEDKEY;

        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].ki.wVk = VK_MEDIA_PLAY_PAUSE;
        inputs[1].ki.dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP;

        uint sent = SendInput(2, inputs, Marshal.SizeOf<INPUT>());
        if (sent != 2)
            Log.Warning("SendInput for media key returned {Sent}/2", sent);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
        // Pad to match the union size of INPUT (mouse input is larger)
        private readonly IntPtr _pad1;
        private readonly IntPtr _pad2;
    }
}
