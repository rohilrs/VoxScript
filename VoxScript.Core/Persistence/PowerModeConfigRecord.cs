namespace VoxScript.Core.Persistence;

public sealed class PowerModeConfigRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public string? ProcessNameFilter { get; set; }
    public string? UrlPatternFilter { get; set; }
    public string? WindowTitleFilter { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }
    public int Preset { get; set; }  // EnhancementPreset as int
    public bool IsBuiltIn { get; set; }
}
