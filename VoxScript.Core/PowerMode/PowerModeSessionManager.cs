// VoxScript.Core/PowerMode/PowerModeSessionManager.cs
namespace VoxScript.Core.PowerMode;

public sealed class PowerModeSessionManager
{
    private readonly PowerModeManager _manager;
    private readonly IActiveWindowService _windowService;

    /// <summary>The process name that matched during the last ResolveCurrentAsync call.</summary>
    public string? LastMatchedProcessName { get; private set; }

    public PowerModeSessionManager(PowerModeManager manager, IActiveWindowService windowService)
    {
        _manager = manager;
        _windowService = windowService;
    }

    /// <summary>Captures current context and resolves the active Power Mode config.</summary>
    public async Task<PowerModeConfig?> ResolveCurrentAsync(CancellationToken ct)
    {
        var processName = _windowService.GetForegroundProcessName();
        var windowTitle = _windowService.GetForegroundWindowTitle();
        var url = await _windowService.TryGetBrowserUrlAsync(ct);
        var config = _manager.Resolve(processName, windowTitle, url);
        LastMatchedProcessName = config is not null ? processName : null;
        return config;
    }
}
