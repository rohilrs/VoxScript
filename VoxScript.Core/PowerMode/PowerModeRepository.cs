using Microsoft.EntityFrameworkCore;
using VoxScript.Core.Persistence;

namespace VoxScript.Core.PowerMode;

public sealed class PowerModeRepository : IPowerModeRepository
{
    private readonly AppDbContext _db;
    public PowerModeRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<PowerModeConfigRecord>> GetAllAsync(CancellationToken ct) =>
        await _db.PowerModeConfigs.OrderByDescending(c => c.Priority).ToListAsync(ct);

    public async Task AddAsync(PowerModeConfigRecord record, CancellationToken ct)
    {
        _db.PowerModeConfigs.Add(record);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PowerModeConfigRecord record, CancellationToken ct)
    {
        var existing = await _db.PowerModeConfigs.FindAsync(new object[] { record.Id }, ct);
        if (existing is not null)
        {
            existing.Name = record.Name;
            existing.SystemPrompt = record.SystemPrompt;
            existing.ProcessNameFilter = record.ProcessNameFilter;
            existing.UrlPatternFilter = record.UrlPatternFilter;
            existing.WindowTitleFilter = record.WindowTitleFilter;
            existing.IsEnabled = record.IsEnabled;
            existing.Priority = record.Priority;
            existing.Preset = record.Preset;
            existing.IsBuiltIn = record.IsBuiltIn;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var r = await _db.PowerModeConfigs.FindAsync(new object[] { id }, ct);
        if (r is not null) { _db.PowerModeConfigs.Remove(r); await _db.SaveChangesAsync(ct); }
    }

    public async Task<bool> AnyAsync(CancellationToken ct) =>
        await _db.PowerModeConfigs.AnyAsync(ct);
}
