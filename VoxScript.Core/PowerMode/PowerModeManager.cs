// VoxScript.Core/PowerMode/PowerModeManager.cs
using System.Text.RegularExpressions;

namespace VoxScript.Core.PowerMode;

public sealed class PowerModeManager
{
    private readonly List<PowerModeConfig> _configs = new();

    public IReadOnlyList<PowerModeConfig> Configs => _configs;

    public void Add(PowerModeConfig config) => _configs.Add(config);
    public void Remove(int id) => _configs.RemoveAll(c => c.Id == id);

    public void Clear() => _configs.Clear();

    public void LoadAll(IEnumerable<PowerModeConfig> configs)
    {
        _configs.Clear();
        _configs.AddRange(configs);
    }

    /// <summary>
    /// Finds the highest-priority matching config for the current context.
    /// Returns null if no config matches.
    /// </summary>
    public PowerModeConfig? Resolve(string? processName, string? windowTitle, string? url)
    {
        return _configs
            .Where(c => c.IsEnabled)
            .OrderByDescending(c => c.Priority)
            .FirstOrDefault(c => Matches(c, processName, windowTitle, url));
    }

    private static bool Matches(PowerModeConfig config,
        string? processName, string? windowTitle, string? url)
    {
        // Process name: comma-separated list, match any
        if (config.ProcessNameFilter is { Length: > 0 })
        {
            if (processName is null) return false;
            var names = config.GetProcessNames();
            if (!names.Any(n => processName.Contains(n, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        if (config.UrlPatternFilter is { Length: > 0 } uf)
        {
            if (url is null) return false;
            if (!Regex.IsMatch(url, uf, RegexOptions.IgnoreCase)) return false;
        }

        if (config.WindowTitleFilter is { Length: > 0 } wf)
        {
            if (windowTitle is null) return false;
            if (!Regex.IsMatch(windowTitle, wf, RegexOptions.IgnoreCase)) return false;
        }

        // At least one filter must be set for a config to match
        return config.ProcessNameFilter is { Length: > 0 }
            || config.UrlPatternFilter is { Length: > 0 }
            || config.WindowTitleFilter is { Length: > 0 };
    }
}
