using Microsoft.EntityFrameworkCore;
using VoxScript.Core.Persistence;

namespace VoxScript.Core.Dictionary;

public sealed class WordReplacementRepository : IWordReplacementRepository
{
    private readonly AppDbContext _db;
    public WordReplacementRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<WordReplacementRecord>> GetAllAsync(CancellationToken ct) =>
        await _db.WordReplacements.ToListAsync(ct);

    public async Task AddAsync(WordReplacementRecord record, CancellationToken ct)
    {
        _db.WordReplacements.Add(record);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(WordReplacementRecord record, CancellationToken ct)
    {
        var existing = await _db.WordReplacements.FindAsync(new object[] { record.Id }, ct);
        if (existing is not null)
        {
            existing.Original = record.Original;
            existing.Replacement = record.Replacement;
            existing.CaseSensitive = record.CaseSensitive;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var r = await _db.WordReplacements.FindAsync(new object[] { id }, ct);
        if (r is not null) { _db.WordReplacements.Remove(r); await _db.SaveChangesAsync(ct); }
    }
}
