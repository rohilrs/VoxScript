namespace VoxScript.Core.AI;

public sealed record AiCompletionConfig(
    AiProvider Provider,
    string Model,
    string OllamaEndpoint,
    string? ApiKey);
