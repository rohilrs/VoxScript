using VoxScript.Core.Persistence;

namespace VoxScript.Core.History;

public interface ITranscriptionRepository
{
    Task<TranscriptionRecord> AddAsync(TranscriptionRecord record, CancellationToken ct);
    Task<TranscriptionRecord?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<TranscriptionRecord>> GetPageAsync(int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<TranscriptionRecord>> GetRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<IReadOnlyList<TranscriptionRecord>> SearchAsync(string query, int take, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct);
    Task<int> CountAsync(CancellationToken ct);
    Task<(int TotalWords, double TotalSeconds)> GetAggregateStatsAsync(CancellationToken ct);
}
