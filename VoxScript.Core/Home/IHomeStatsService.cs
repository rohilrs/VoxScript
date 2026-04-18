namespace VoxScript.Core.Home;

public interface IHomeStatsService
{
    Task<int> GetTotalWordsAsync(CancellationToken ct);
    Task<double> GetAverageWpmAsync(CancellationToken ct);

    Task<IReadOnlyList<int>> GetHourlyWordBucketsAsync(int hours, CancellationToken ct);

    void InvalidateCache();
}
