namespace VoxScript.Core.AI;

public interface IAiCompleter
{
    Task<string> CompleteAsync(
        AiCompletionConfig config,
        string systemPrompt,
        string userMessage,
        CancellationToken ct);
}
