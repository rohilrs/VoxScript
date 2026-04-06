using VoxScript.Core.Persistence;

namespace VoxScript.Core.PowerMode;

public interface IPowerModeRepository
{
    Task<IReadOnlyList<PowerModeConfigRecord>> GetAllAsync(CancellationToken ct);
    Task AddAsync(PowerModeConfigRecord record, CancellationToken ct);
    Task UpdateAsync(PowerModeConfigRecord record, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task<bool> AnyAsync(CancellationToken ct);
}
