using VoxScript.Core.Persistence;

namespace VoxScript.Core.Dictionary;

public interface IWordReplacementRepository
{
    Task<IReadOnlyList<WordReplacementRecord>> GetAllAsync(CancellationToken ct);
    Task AddAsync(WordReplacementRecord record, CancellationToken ct);
    Task UpdateAsync(WordReplacementRecord record, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
