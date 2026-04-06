namespace VoxScript.Core.AI;

public interface IAIEnhancementService
{
    Task<string?> EnhanceAsync(string rawText, CancellationToken ct);
    Task<string?> EnhanceWithPromptAsync(string rawText, string systemPrompt, CancellationToken ct);
    bool IsConfigured { get; }
}
