using FluentAssertions;
using NSubstitute;
using VoxScript.Core.History;
using VoxScript.Core.Home;
using VoxScript.Core.Persistence;
using VoxScript.ViewModels;

namespace VoxScript.Tests.Home;

public class HomeViewModelTests
{
    private static (IHomeStatusService status, IHomeStatsService stats, ITranscriptionRepository repo)
        BuildFakes(
            StatusResult? modelStatus = null,
            StatusResult? aiStatus = null,
            StatusResult? llmStatus = null,
            int totalWords = 0,
            double avgWpm = 0,
            TranscriptionRecord? latest = null)
    {
        var status = Substitute.For<IHomeStatusService>();
        status.GetModelStatusAsync(Arg.Any<CancellationToken>())
              .Returns(modelStatus ?? new StatusResult(StatusLevel.Ready, "Model Ready"));
        status.GetAiEnhancementStatusAsync(Arg.Any<CancellationToken>())
              .Returns(aiStatus ?? new StatusResult(StatusLevel.Off, "AI Off"));
        status.GetLlmFormattingStatusAsync(Arg.Any<CancellationToken>())
              .Returns(llmStatus ?? new StatusResult(StatusLevel.Off, "LLM Off"));
        status.Rollup(Arg.Any<StatusResult[]>())
              .Returns(new StatusResult(StatusLevel.Ready, "Ready"));

        var statsService = Substitute.For<IHomeStatsService>();
        statsService.GetTotalWordsAsync(Arg.Any<CancellationToken>()).Returns(totalWords);
        statsService.GetAverageWpmAsync(Arg.Any<CancellationToken>()).Returns(avgWpm);
        statsService.GetBucketedWordsAsync(
                Arg.Any<TimeSpan>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<int>)new int[HomeViewModel.ActivityBucketCount]);

        var repo = Substitute.For<ITranscriptionRepository>();
        var records = latest is not null
            ? new List<TranscriptionRecord> { latest }
            : new List<TranscriptionRecord>();
        repo.GetPageAsync(0, 1, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TranscriptionRecord>)records);

        return (status, statsService, repo);
    }

    [Fact]
    public async Task RefreshAsync_fetches_all_three_statuses()
    {
        var (status, stats, repo) = BuildFakes();
        var vm = new HomeViewModel(status, stats, repo);

        await vm.RefreshAsync(CancellationToken.None);

        await status.Received(1).GetModelStatusAsync(Arg.Any<CancellationToken>());
        await status.Received(1).GetAiEnhancementStatusAsync(Arg.Any<CancellationToken>());
        await status.Received(1).GetLlmFormattingStatusAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_sets_stats_properties()
    {
        var (status, stats, repo) = BuildFakes(totalWords: 42318, avgWpm: 147.0);
        var vm = new HomeViewModel(status, stats, repo);

        await vm.RefreshAsync(CancellationToken.None);

        vm.TotalWords.Should().Be(42318);
        vm.AvgWpm.Should().BeApproximately(147.0, 0.01);
    }

    [Fact]
    public async Task RefreshAsync_sets_HasLatestTranscript_false_when_empty()
    {
        var (status, stats, repo) = BuildFakes();
        var vm = new HomeViewModel(status, stats, repo);

        await vm.RefreshAsync(CancellationToken.None);

        vm.HasLatestTranscript.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAsync_sets_LatestTranscriptText_when_record_exists()
    {
        var record = new TranscriptionRecord
        {
            Text = "Hello world",
            DurationSeconds = 10,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
        };
        var (status, stats, repo) = BuildFakes(latest: record);
        var vm = new HomeViewModel(status, stats, repo);

        await vm.RefreshAsync(CancellationToken.None);

        vm.HasLatestTranscript.Should().BeTrue();
        vm.LatestTranscriptText.Should().Be("Hello world");
    }

    [Fact]
    public async Task OnTranscriptionCompleted_calls_InvalidateCache_and_refreshes_stats()
    {
        var record = new TranscriptionRecord
        {
            Text = "New transcript",
            DurationSeconds = 5,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var (status, stats, repo) = BuildFakes(latest: record);
        var vm = new HomeViewModel(status, stats, repo);

        await vm.OnTranscriptionCompletedAsync("New transcript", CancellationToken.None);

        stats.Received(1).InvalidateCache();
        await stats.Received().GetTotalWordsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnTranscriptionCompleted_does_not_re_fetch_statuses()
    {
        var (status, stats, repo) = BuildFakes();
        var vm = new HomeViewModel(status, stats, repo);

        await vm.OnTranscriptionCompletedAsync("text", CancellationToken.None);

        await status.DidNotReceive().GetModelStatusAsync(Arg.Any<CancellationToken>());
        await status.DidNotReceive().GetAiEnhancementStatusAsync(Arg.Any<CancellationToken>());
        await status.DidNotReceive().GetLlmFormattingStatusAsync(Arg.Any<CancellationToken>());
    }
}
