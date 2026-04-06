using Microsoft.EntityFrameworkCore;
using VoxScript.Core.Persistence;

namespace VoxScript.Core.Dictionary;

public sealed class CorrectionRepository : ICorrectionRepository
{
    private readonly AppDbContext _db;
    public CorrectionRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<CorrectionRecord>> GetAllAsync(CancellationToken ct) =>
        await _db.Corrections.ToListAsync(ct);

    public async Task AddAsync(CorrectionRecord record, CancellationToken ct)
    {
        _db.Corrections.Add(record);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(CorrectionRecord record, CancellationToken ct)
    {
        var existing = await _db.Corrections.FindAsync(new object[] { record.Id }, ct);
        if (existing is not null)
        {
            existing.Wrong = record.Wrong;
            existing.Correct = record.Correct;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var r = await _db.Corrections.FindAsync(new object[] { id }, ct);
        if (r is not null) { _db.Corrections.Remove(r); await _db.SaveChangesAsync(ct); }
    }
}
