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

    public async Task<IReadOnlyList<int>> GetBucketedWordsAsync(
        TimeSpan interval, int count, CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var window = TimeSpan.FromTicks(interval.Ticks * count);
        var from = now - window;

        var records = await _repository.GetRangeAsync(from, now, ct);

        var buckets = new int[count];
        foreach (var record in records)
        {
            var age = now - record.CreatedAt;
            if (age < TimeSpan.Zero || age >= window) continue;

            int bucketsFromEnd = (int)(age.TotalMilliseconds / interval.TotalMilliseconds);
            int slot = count - 1 - bucketsFromEnd;
            if (slot >= 0 && slot < count)
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
