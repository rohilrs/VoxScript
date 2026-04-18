using FluentAssertions;
using NSubstitute;
using VoxScript.Core.History;
using VoxScript.Core.Home;
using VoxScript.Core.Persistence;

namespace VoxScript.Tests.Home;

public class HomeStatsServiceTests
{
    private static ITranscriptionRepository MakeRepo(
        (int TotalWords, double TotalSeconds) aggregate,
        IReadOnlyList<TranscriptionRecord>? rangeRecords = null)
    {
        var repo = Substitute.For<ITranscriptionRepository>();
        repo.GetAggregateStatsAsync(Arg.Any<CancellationToken>())
            .Returns(aggregate);
        repo.GetRangeAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(rangeRecords ?? Array.Empty<TranscriptionRecord>());
        return repo;
    }

    [Fact]
    public async Task GetTotalWordsAsync_returns_aggregate_value()
    {
        var repo = MakeRepo((1234, 600.0));
        var svc = new HomeStatsService(repo);

        var total = await svc.GetTotalWordsAsync(CancellationToken.None);

        total.Should().Be(1234);
    }

    [Fact]
    public async Task GetTotalWordsAsync_returns_zero_when_no_records()
    {
        var repo = MakeRepo((0, 0.0));
        var svc = new HomeStatsService(repo);

        var total = await svc.GetTotalWordsAsync(CancellationToken.None);

        total.Should().Be(0);
    }

    [Fact]
    public async Task GetAverageWpmAsync_correct_formula()
    {
        var repo = MakeRepo((600, 60.0));
        var svc = new HomeStatsService(repo);

        var wpm = await svc.GetAverageWpmAsync(CancellationToken.None);

        wpm.Should().BeApproximately(600.0, 0.01);
    }

    [Fact]
    public async Task GetAverageWpmAsync_zero_when_no_duration()
    {
        var repo = MakeRepo((100, 0.0));
        var svc = new HomeStatsService(repo);

        var wpm = await svc.GetAverageWpmAsync(CancellationToken.None);

        wpm.Should().Be(0.0);
    }

    [Fact]
    public async Task Cache_is_used_on_second_call_without_invalidation()
    {
        var repo = MakeRepo((100, 60.0));
        var svc = new HomeStatsService(repo);

        await svc.GetTotalWordsAsync(CancellationToken.None);
        await svc.GetTotalWordsAsync(CancellationToken.None);

        await repo.Received(1).GetAggregateStatsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateCache_forces_re_query()
    {
        var repo = MakeRepo((100, 60.0));
        var svc = new HomeStatsService(repo);

        await svc.GetTotalWordsAsync(CancellationToken.None);
        svc.InvalidateCache();
        await svc.GetTotalWordsAsync(CancellationToken.None);

        await repo.Received(2).GetAggregateStatsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetHourlyWordBucketsAsync_returns_12_slots()
    {
        var repo = MakeRepo((0, 0.0));
        var svc = new HomeStatsService(repo);

        var buckets = await svc.GetHourlyWordBucketsAsync(12, CancellationToken.None);

        buckets.Count.Should().Be(12);
    }

    [Fact]
    public async Task GetHourlyWordBucketsAsync_sums_words_into_correct_hour_slot()
    {
        var now = DateTimeOffset.Now;
        var oneHourAgo = now.AddHours(-1);

        var record = new TranscriptionRecord
        {
            Text = "hello world",
            WordCount = 50,
            DurationSeconds = 30,
            CreatedAt = oneHourAgo,
        };

        var repo = MakeRepo((50, 30.0), new[] { record });
        var svc = new HomeStatsService(repo);

        var buckets = await svc.GetHourlyWordBucketsAsync(12, CancellationToken.None);

        buckets[10].Should().Be(50);
        buckets[11].Should().Be(0);
    }

    [Fact]
    public async Task GetHourlyWordBucketsAsync_all_zero_when_no_records()
    {
        var repo = MakeRepo((0, 0.0));
        var svc = new HomeStatsService(repo);

        var buckets = await svc.GetHourlyWordBucketsAsync(12, CancellationToken.None);

        buckets.Should().AllSatisfy(b => b.Should().Be(0));
    }
}
