// VoxScript/ViewModels/PersonalizeViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VoxScript.Core.AI;
using VoxScript.Core.Persistence;
using VoxScript.Core.PowerMode;
using VoxScript.Core.Settings;
using VoxScript.Infrastructure;

namespace VoxScript.ViewModels;

public sealed partial class PersonalizeViewModel : ObservableObject
{
    private readonly IPowerModeRepository _repo;
    private readonly PowerModeManager _manager;

    public ObservableCollection<PowerModeConfig> PowerModes { get; } = new();

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    public PersonalizeViewModel()
    {
        _repo = ServiceLocator.Get<IPowerModeRepository>();
        _manager = ServiceLocator.Get<PowerModeManager>();
    }

    public async Task LoadAsync()
    {
        var records = await _repo.GetAllAsync(CancellationToken.None);
        PowerModes.Clear();
        foreach (var r in records)
            PowerModes.Add(PowerModeMapper.ToConfig(r));
    }

    public async Task ToggleModeAsync(int id, bool enabled)
    {
        var mode = PowerModes.FirstOrDefault(c => c.Id == id);
        if (mode is null) return;
        mode.IsEnabled = enabled;
        await _repo.UpdateAsync(PowerModeMapper.ToRecord(mode), CancellationToken.None);
        ReloadManager();
    }

    public async Task SetModePresetAsync(int id, EnhancementPreset preset)
    {
        var mode = PowerModes.FirstOrDefault(c => c.Id == id);
        if (mode is null) return;
        mode.Preset = preset;
        if (preset != EnhancementPreset.Custom)
            mode.SystemPrompt = null;
        await _repo.UpdateAsync(PowerModeMapper.ToRecord(mode), CancellationToken.None);
        ReloadManager();
    }

    public async Task SetModePromptAsync(int id, string prompt)
    {
        var mode = PowerModes.FirstOrDefault(c => c.Id == id);
        if (mode is null) return;
        mode.SystemPrompt = prompt;
        await _repo.UpdateAsync(PowerModeMapper.ToRecord(mode), CancellationToken.None);
        ReloadManager();
    }

    public async Task AddAppToModeAsync(int id, string app)
    {
        var mode = PowerModes.FirstOrDefault(c => c.Id == id);
        if (mode is null) return;
        var apps = mode.GetProcessNames().ToList();
        var trimmed = app.Trim();
        if (string.IsNullOrEmpty(trimmed) || apps.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) return;
        apps.Add(trimmed);
        mode.ProcessNameFilter = string.Join(",", apps);
        await _repo.UpdateAsync(PowerModeMapper.ToRecord(mode), CancellationToken.None);
        ReloadManager();
    }

    public async Task RemoveAppFromModeAsync(int id, string app)
    {
        var mode = PowerModes.FirstOrDefault(c => c.Id == id);
        if (mode is null) return;
        var apps = mode.GetProcessNames().Where(a => !a.Equals(app, StringComparison.OrdinalIgnoreCase)).ToList();
        mode.ProcessNameFilter = apps.Count > 0 ? string.Join(",", apps) : null;
        await _repo.UpdateAsync(PowerModeMapper.ToRecord(mode), CancellationToken.None);
        ReloadManager();
    }

    public async Task AddUrlToModeAsync(int id, string url)
    {
        var mode = PowerModes.FirstOrDefault(c => c.Id == id);
        if (mode is null) return;
        var urls = mode.GetUrlPatterns().ToList();
        var trimmed = url.Trim();
        if (string.IsNullOrEmpty(trimmed) || urls.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) return;
        urls.Add(trimmed);
        mode.UrlPatternFilter = PowerModeConfig.BuildUrlPattern(urls);
        await _repo.UpdateAsync(PowerModeMapper.ToRecord(mode), CancellationToken.None);
        ReloadManager();
    }

    public async Task RemoveUrlFromModeAsync(int id, string url)
    {
        var mode = PowerModes.FirstOrDefault(c => c.Id == id);
        if (mode is null) return;
        var urls = mode.GetUrlPatterns().Where(u => !u.Equals(url, StringComparison.OrdinalIgnoreCase)).ToList();
        mode.UrlPatternFilter = PowerModeConfig.BuildUrlPattern(urls);
        await _repo.UpdateAsync(PowerModeMapper.ToRecord(mode), CancellationToken.None);
        ReloadManager();
    }

    public async Task<PowerModeConfig> AddCustomModeAsync(string name)
    {
        var record = new PowerModeConfigRecord
        {
            Name = name,
            Preset = (int)EnhancementPreset.SemiCasual,
            Priority = 10,
            IsEnabled = true,
            IsBuiltIn = false,
        };
        await _repo.AddAsync(record, CancellationToken.None);
        await LoadAsync();
        ReloadManager();
        return PowerModes.Last();
    }

    public async Task DeleteModeAsync(int id)
    {
        await _repo.DeleteAsync(id, CancellationToken.None);
        await LoadAsync();
        ReloadManager();
    }

    private void ReloadManager()
    {
        _manager.LoadAll(PowerModes);
    }
}
