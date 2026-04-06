namespace VoxScript.Core.Notes;

public interface INoteRepository
{
    Task<IReadOnlyList<NoteRecord>> GetAllAsync(CancellationToken ct);
    Task<NoteRecord?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<NoteRecord>> SearchAsync(string query, CancellationToken ct);
    Task<NoteRecord> CreateAsync(NoteRecord note, CancellationToken ct);
    Task UpdateAsync(NoteRecord note, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
