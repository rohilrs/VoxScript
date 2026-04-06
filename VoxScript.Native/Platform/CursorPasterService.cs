// VoxScript.Native/Platform/CursorPasterService.cs
using System.Runtime.InteropServices;
using Serilog;
using VoxScript.Core.Platform;

namespace VoxScript.Native.Platform;

public sealed class CursorPasterService : IPasteService
{
    private const byte VK_CONTROL = 0x11;
    private const byte VK_LWIN = 0x5B;
    private const byte VK_RWIN = 0x5C;
    private const byte VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>
    /// Write text to clipboard via Win32 API (works even when app is in background),
    /// then send Ctrl+V via keybd_event to the focused window.
    /// Uses keybd_event instead of SendInput for better compatibility with UIPI.
    /// </summary>
    public async Task PasteAtCursorAsync(string text, CancellationToken ct)
    {
        SetClipboardText(text);

        // Wait for clipboard + any held modifier keys from the hotkey to be physically released
        await Task.Delay(100, ct);

        // Release any modifier keys that might still be logically held (Win, Ctrl)
        // to avoid the target app seeing Ctrl+Win+V instead of Ctrl+V
        PasteNative.keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, 0);
        PasteNative.keybd_event(VK_RWIN, 0, KEYEVENTF_KEYUP, 0);

        // Send Ctrl+V
        PasteNative.keybd_event(VK_CONTROL, 0, 0, 0);           // Ctrl down
        PasteNative.keybd_event(VK_V, 0, 0, 0);                 // V down
        PasteNative.keybd_event(VK_V, 0, KEYEVENTF_KEYUP, 0);   // V up
        PasteNative.keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0); // Ctrl up

        Log.Debug("Paste keystrokes sent via keybd_event");
    }

    private static void SetClipboardText(string text)
    {
        if (!ClipboardNative.OpenClipboard(IntPtr.Zero))
        {
            Log.Warning("OpenClipboard failed (error {Err})", Marshal.GetLastWin32Error());
            return;
        }
        try
        {
            ClipboardNative.EmptyClipboard();

            // CF_UNICODETEXT requires GlobalAlloc with GMEM_MOVEABLE
            int byteCount = (text.Length + 1) * sizeof(char);
            IntPtr hGlobal = ClipboardNative.GlobalAlloc(ClipboardNative.GMEM_MOVEABLE, (nuint)byteCount);
            if (hGlobal == IntPtr.Zero)
            {
                Log.Warning("GlobalAlloc failed for clipboard text");
                return;
            }

            IntPtr locked = ClipboardNative.GlobalLock(hGlobal);
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, locked, text.Length);
                Marshal.WriteInt16(locked, text.Length * sizeof(char), 0); // null terminator
            }
            finally
            {
                ClipboardNative.GlobalUnlock(hGlobal);
            }

            if (ClipboardNative.SetClipboardData(ClipboardNative.CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
            {
                ClipboardNative.GlobalFree(hGlobal);
                Log.Warning("SetClipboardData failed (error {Err})", Marshal.GetLastWin32Error());
            }
            // On success the system owns hGlobal — do NOT free it
        }
        finally
        {
            ClipboardNative.CloseClipboard();
        }
    }

    private static class PasteNative
    {
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);
    }

    private static class ClipboardNative
    {
        public const uint CF_UNICODETEXT = 13;
        public const uint GMEM_MOVEABLE = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalFree(IntPtr hMem);
    }
}
