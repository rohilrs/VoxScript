namespace VoxScript.Core.PowerMode;

public interface IActiveWindowService
{
    string? GetForegroundProcessName();
    string? GetForegroundWindowTitle();
    Task<string?> TryGetBrowserUrlAsync(CancellationToken ct);
}
