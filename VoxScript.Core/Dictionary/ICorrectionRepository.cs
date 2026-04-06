using VoxScript.Core.Persistence;

namespace VoxScript.Core.Dictionary;

public interface ICorrectionRepository
{
    Task<IReadOnlyList<CorrectionRecord>> GetAllAsync(CancellationToken ct);
    Task AddAsync(CorrectionRecord record, CancellationToken ct);
    Task UpdateAsync(CorrectionRecord record, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
