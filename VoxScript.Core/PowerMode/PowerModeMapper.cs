// VoxScript.Core/PowerMode/PowerModeMapper.cs
using VoxScript.Core.AI;
using VoxScript.Core.Persistence;

namespace VoxScript.Core.PowerMode;

public static class PowerModeMapper
{
    public static PowerModeConfig ToConfig(PowerModeConfigRecord r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        SystemPrompt = r.SystemPrompt,
        ProcessNameFilter = r.ProcessNameFilter,
        UrlPatternFilter = r.UrlPatternFilter,
        WindowTitleFilter = r.WindowTitleFilter,
        IsEnabled = r.IsEnabled,
        Priority = r.Priority,
        Preset = (EnhancementPreset)r.Preset,
        IsBuiltIn = r.IsBuiltIn,
    };

    public static PowerModeConfigRecord ToRecord(PowerModeConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        SystemPrompt = c.SystemPrompt,
        ProcessNameFilter = c.ProcessNameFilter,
        UrlPatternFilter = c.UrlPatternFilter,
        WindowTitleFilter = c.WindowTitleFilter,
        IsEnabled = c.IsEnabled,
        Priority = c.Priority,
        Preset = (int)c.Preset,
        IsBuiltIn = c.IsBuiltIn,
    };
}
