using Microsoft.EntityFrameworkCore;
using VoxScript.Core.Persistence;

namespace VoxScript.Core.Dictionary;

public sealed class VocabularyRepository : IVocabularyRepository
{
    private readonly AppDbContext _db;
    public VocabularyRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> GetWordsAsync(CancellationToken ct) =>
        await _db.VocabularyWords.Select(w => w.Word).ToListAsync(ct);

    public async Task AddWordAsync(string word, CancellationToken ct)
    {
        _db.VocabularyWords.Add(new VocabularyWordRecord { Word = word });
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteWordAsync(string word, CancellationToken ct)
    {
        var r = await _db.VocabularyWords.FirstOrDefaultAsync(w => w.Word == word, ct);
        if (r is not null) { _db.VocabularyWords.Remove(r); await _db.SaveChangesAsync(ct); }
    }
}
