namespace VoxScript.Core.Home;

public interface IHomeStatsService
{
    Task<int> GetTotalWordsAsync(CancellationToken ct);
    Task<double> GetAverageWpmAsync(CancellationToken ct);

    /// <summary>
    /// Returns word counts bucketed by time, oldest first. Window spans interval × count
    /// backwards from now; records older than that are ignored.
    /// </summary>
    Task<IReadOnlyList<int>> GetBucketedWordsAsync(TimeSpan interval, int count, CancellationToken ct);

    void InvalidateCache();
}
