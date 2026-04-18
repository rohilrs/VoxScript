namespace VoxScript.Core.AI;

public interface IStructuralFormattingService
{
    bool IsConfigured { get; }
    Task<string?> FormatAsync(string text, CancellationToken ct);

    /// <summary>
    /// Fire-and-forget warmup: pings the configured local LLM with a tiny request
    /// so the model gets loaded into VRAM before the user's first dictation.
    /// No-op for cloud providers (always warm) or when the service is not configured.
    /// All errors are swallowed; safe to call from app startup or settings toggle.
    /// </summary>
    Task WarmupAsync();
}
