using VoxScript.Core.History;

namespace VoxScript.Core.Home;

public sealed class HomeStatsService : IHomeStatsService
{
    private readonly ITranscriptionRepository _repository;

    private (int TotalWords, double TotalSeconds)? _cache;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public HomeStatsService(ITranscriptionRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> GetTotalWordsAsync(CancellationToken ct)
    {
        var (words, _) = await GetCachedAggregateAsync(ct);
        return words;
    }

    public async Task<double> GetAverageWpmAsync(CancellationToken ct)
    {
        var (words, seconds) = await GetCachedAggregateAsync(ct);
        return seconds > 0 ? (words / seconds) * 60.0 : 0.0;
    }

    public async Task<IReadOnlyList<int>> GetHourlyWordBucketsAsync(int hours, CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var from = now.AddHours(-hours);

        var records = await _repository.GetRangeAsync(from, now, ct);

        var buckets = new int[hours];
        var currentHour = now.LocalDateTime.Hour;

        foreach (var record in records)
        {
            var localHour = record.CreatedAt.LocalDateTime.Hour;
            var hoursAgo = ((currentHour - localHour) + 24) % 24;
            var slot = hours - 1 - hoursAgo;
            if (slot >= 0 && slot < hours)
                buckets[slot] += record.WordCount;
        }

        return buckets;
    }

    public void InvalidateCache()
    {
        _cache = null;
    }

    private async Task<(int TotalWords, double TotalSeconds)> GetCachedAggregateAsync(
        CancellationToken ct)
    {
        if (_cache.HasValue)
            return _cache.Value;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.HasValue)
                return _cache.Value;

            _cache = await _repository.GetAggregateStatsAsync(ct);
            return _cache.Value;
        }
        finally
        {
            _lock.Release();
        }
    }
}
