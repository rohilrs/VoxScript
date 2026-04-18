using CommunityToolkit.Mvvm.ComponentModel;
using VoxScript.Core.History;
using VoxScript.Core.Home;

namespace VoxScript.ViewModels;

public sealed partial class HomeViewModel : ObservableObject
{
    // 12-hour activity window at 15-minute resolution = 48 buckets.
    public const int ActivityBucketCount = 48;
    public static readonly TimeSpan ActivityBucketInterval = TimeSpan.FromMinutes(15);

    private readonly IHomeStatusService _statusService;
    private readonly IHomeStatsService _statsService;
    private readonly ITranscriptionRepository _repository;

    [ObservableProperty]
    public partial StatusResult OverallStatus { get; set; }

    [ObservableProperty]
    public partial StatusResult ModelStatus { get; set; }

    [ObservableProperty]
    public partial StatusResult AiEnhanceStatus { get; set; }

    [ObservableProperty]
    public partial StatusResult LlmFormatStatus { get; set; }

    [ObservableProperty]
    public partial int TotalWords { get; set; }

    [ObservableProperty]
    public partial double AvgWpm { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<int> ActivityBuckets { get; set; }

    [ObservableProperty]
    public partial string LatestTranscriptText { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? LatestTranscriptTimestamp { get; set; }

    [ObservableProperty]
    public partial bool HasLatestTranscript { get; set; }

    public HomeViewModel(
        IHomeStatusService statusService,
        IHomeStatsService statsService,
        ITranscriptionRepository repository)
    {
        _statusService = statusService;
        _statsService = statsService;
        _repository = repository;

        OverallStatus = new StatusResult(StatusLevel.Ready, "Ready");
        ModelStatus = new StatusResult(StatusLevel.Ready, "Model Ready");
        AiEnhanceStatus = new StatusResult(StatusLevel.Off, "AI Enhancement off");
        LlmFormatStatus = new StatusResult(StatusLevel.Off, "LLM Formatting off");
        ActivityBuckets = new int[ActivityBucketCount];
        LatestTranscriptText = "";
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        var modelTask = _statusService.GetModelStatusAsync(ct);
        var aiTask = _statusService.GetAiEnhancementStatusAsync(ct);
        var llmTask = _statusService.GetLlmFormattingStatusAsync(ct);

        await Task.WhenAll(modelTask, aiTask, llmTask);

        ModelStatus = modelTask.Result;
        AiEnhanceStatus = aiTask.Result;
        LlmFormatStatus = llmTask.Result;
        OverallStatus = _statusService.Rollup(ModelStatus, AiEnhanceStatus, LlmFormatStatus);

        await RefreshStatsAsync(ct);
        await RefreshLatestTranscriptAsync(ct);
    }

    public async Task OnTranscriptionCompletedAsync(string text, CancellationToken ct)
    {
        _statsService.InvalidateCache();
        await RefreshStatsAsync(ct);
        await RefreshLatestTranscriptAsync(ct);
    }

    private async Task RefreshStatsAsync(CancellationToken ct)
    {
        TotalWords = await _statsService.GetTotalWordsAsync(ct);
        AvgWpm = await _statsService.GetAverageWpmAsync(ct);
        ActivityBuckets = await _statsService.GetBucketedWordsAsync(
            ActivityBucketInterval, ActivityBucketCount, ct);
    }

    private async Task RefreshLatestTranscriptAsync(CancellationToken ct)
    {
        var records = await _repository.GetPageAsync(0, 1, ct);
        if (records.Count == 0)
        {
            HasLatestTranscript = false;
            LatestTranscriptText = "";
            LatestTranscriptTimestamp = null;
            return;
        }

        var latest = records[0];
        HasLatestTranscript = true;
        LatestTranscriptText = latest.EnhancedText ?? latest.Text;
        LatestTranscriptTimestamp = latest.CreatedAt;
    }
}
