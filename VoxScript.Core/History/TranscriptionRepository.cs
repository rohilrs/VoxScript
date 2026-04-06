using Microsoft.EntityFrameworkCore;
using VoxScript.Core.Persistence;

namespace VoxScript.Core.History;

public sealed class TranscriptionRepository : ITranscriptionRepository
{
    private readonly AppDbContext _db;
    public TranscriptionRepository(AppDbContext db) => _db = db;

    public async Task<TranscriptionRecord> AddAsync(TranscriptionRecord record, CancellationToken ct)
    {
        _db.Transcriptions.Add(record);
        await _db.SaveChangesAsync(ct);
        return record;
    }

    public Task<TranscriptionRecord?> GetByIdAsync(int id, CancellationToken ct) =>
        _db.Transcriptions.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<TranscriptionRecord>> GetPageAsync(int skip, int take, CancellationToken ct) =>
        await _db.Transcriptions
            .OrderByDescending(r => r.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TranscriptionRecord>> GetRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        await _db.Transcriptions
            .Where(r => r.CreatedAt >= from && r.CreatedAt < to)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TranscriptionRecord>> SearchAsync(string query, int take, CancellationToken ct) =>
        await _db.Transcriptions
            .Where(r => EF.Functions.Like(r.Text, $"%{query}%")
                     || (r.EnhancedText != null && EF.Functions.Like(r.EnhancedText, $"%{query}%")))
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var record = await _db.Transcriptions.FindAsync(new object[] { id }, ct);
        if (record is not null)
        {
            _db.Transcriptions.Remove(record);
            await _db.SaveChangesAsync(ct);
        }
    }

    public Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct) =>
        _db.Transcriptions
            .Where(r => r.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

    public Task<int> CountAsync(CancellationToken ct) =>
        _db.Transcriptions.CountAsync(ct);
}
