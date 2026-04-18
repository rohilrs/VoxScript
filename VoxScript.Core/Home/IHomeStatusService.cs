namespace VoxScript.Core.Home;

public interface IHomeStatusService
{
    Task<StatusResult> GetModelStatusAsync(CancellationToken ct);
    Task<StatusResult> GetAiEnhancementStatusAsync(CancellationToken ct);
    Task<StatusResult> GetLlmFormattingStatusAsync(CancellationToken ct);

    StatusResult Rollup(params StatusResult[] components);
}
