// VoxScript.Core/PowerMode/PowerModeConfig.cs
using VoxScript.Core.AI;

namespace VoxScript.Core.PowerMode;

public sealed class PowerModeConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public string? ProcessNameFilter { get; set; }    // comma-separated, e.g. "WhatsApp,Discord,Telegram"
    public string? UrlPatternFilter { get; set; }     // regex, e.g. "mail\.google\.com"
    public string? WindowTitleFilter { get; set; }    // regex fallback
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }                 // higher = tried first
    public EnhancementPreset Preset { get; set; } = EnhancementPreset.SemiCasual;
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Returns the effective system prompt for this mode.
    /// Uses the preset's composed prompt unless Custom, in which case uses SystemPrompt directly.
    /// </summary>
    public string GetEffectivePrompt()
    {
        if (Preset == EnhancementPreset.Custom && !string.IsNullOrWhiteSpace(SystemPrompt))
            return SystemPrompt;

        var (punctuation, capitalization, fillers) = Preset switch
        {
            EnhancementPreset.Formal => ("Standard", "SentenceCase", true),
            EnhancementPreset.SemiCasual => ("Minimal", "SentenceCase", true),
            EnhancementPreset.Casual => ("Minimal", "AsSpoken", true),
            _ => ("Standard", "SentenceCase", true),
        };
        return EnhancementPrompts.Compose(Preset, punctuation, capitalization, fillers);
    }

    /// <summary>
    /// Returns the list of process names from the comma-separated filter.
    /// </summary>
    public IReadOnlyList<string> GetProcessNames() =>
        string.IsNullOrWhiteSpace(ProcessNameFilter)
            ? []
            : ProcessNameFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Returns individual URL patterns stored as pipe-separated in UrlPatternFilter.
    /// e.g. "mail\.google\.com|outlook\.live\.com" → ["mail.google.com", "outlook.live.com"]
    /// </summary>
    public IReadOnlyList<string> GetUrlPatterns() =>
        string.IsNullOrWhiteSpace(UrlPatternFilter)
            ? []
            : UrlPatternFilter
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p.Replace(@"\.", ".")) // display-friendly
                .ToList();

    /// <summary>
    /// Builds a regex UrlPatternFilter from a list of plain domain strings.
    /// </summary>
    public static string? BuildUrlPattern(IEnumerable<string> domains)
    {
        var escaped = domains
            .Select(d => d.Trim())
            .Where(d => d.Length > 0)
            .Select(d => d.Replace(".", @"\."))
            .ToList();
        return escaped.Count > 0 ? string.Join("|", escaped) : null;
    }
}
