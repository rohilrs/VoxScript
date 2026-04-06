// VoxScript.Native/Platform/ActiveWindowService.cs
using System.Diagnostics;
using VoxScript.Core.PowerMode;

namespace VoxScript.Native.Platform;

public sealed class ActiveWindowService : IActiveWindowService
{
    public string? GetForegroundProcessName()
    {
        var hwnd = Win32NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        Win32NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch { return null; }
    }

    public string? GetForegroundWindowTitle()
    {
        var hwnd = Win32NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        var sb = new System.Text.StringBuilder(512);
        Win32NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.Length > 0 ? sb.ToString() : null;
    }

    public Task<string?> TryGetBrowserUrlAsync(CancellationToken ct) =>
        Task.Run(() => BrowserUrlService.TryGetActiveTabUrl(), ct);
}
