using Microsoft.EntityFrameworkCore;
using VoxScript.Core.Persistence;

namespace VoxScript.Core.Notes;

public sealed class NoteRepository : INoteRepository
{
    private readonly AppDbContext _db;
    public NoteRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<NoteRecord>> GetAllAsync(CancellationToken ct) =>
        await _db.Notes
            .OrderByDescending(n => n.ModifiedAt)
            .ToListAsync(ct);

    public Task<NoteRecord?> GetByIdAsync(int id, CancellationToken ct) =>
        _db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);

    public async Task<IReadOnlyList<NoteRecord>> SearchAsync(string query, CancellationToken ct) =>
        await _db.Notes
            .Where(n => EF.Functions.Like(n.Title, $"%{query}%")
                     || EF.Functions.Like(n.ContentPlainText, $"%{query}%"))
            .OrderByDescending(n => n.ModifiedAt)
            .ToListAsync(ct);

    public async Task<NoteRecord> CreateAsync(NoteRecord note, CancellationToken ct)
    {
        _db.Notes.Add(note);
        await _db.SaveChangesAsync(ct);
        return note;
    }

    public async Task UpdateAsync(NoteRecord note, CancellationToken ct)
    {
        _db.Notes.Update(note);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var note = await _db.Notes.FindAsync(new object[] { id }, ct);
        if (note is not null)
        {
            _db.Notes.Remove(note);
            await _db.SaveChangesAsync(ct);
        }
    }
}
