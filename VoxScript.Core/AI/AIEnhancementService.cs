using VoxScript.Core.Settings;

namespace VoxScript.Core.AI;

public sealed class AIEnhancementService : IAIEnhancementService
{
    private readonly AIService _aiService;
    private readonly AIEnhancementOutputFilter _outputFilter;
    private readonly AppSettings _settings;

    // Fallback if no prompt is configured in settings
    private const string DefaultSystemPrompt =
        "You are a transcription editor. Fix grammar, punctuation, and formatting " +
        "of the following speech transcription. Preserve all meaning. " +
        "Return only the corrected text with no explanation.";

    public AIEnhancementService(AIService aiService,
        AIEnhancementOutputFilter outputFilter, AppSettings settings)
    {
        _aiService = aiService;
        _outputFilter = outputFilter;
        _settings = settings;
    }

    public bool IsConfigured => _aiService.IsConfigured;

    public Task<string?> EnhanceAsync(string rawText, CancellationToken ct)
    {
        var prompt = !string.IsNullOrWhiteSpace(_settings.EnhancementSystemPrompt)
            ? _settings.EnhancementSystemPrompt
            : DefaultSystemPrompt;
        return EnhanceWithPromptAsync(rawText, prompt, ct);
    }

    public async Task<string?> EnhanceWithPromptAsync(string rawText,
        string systemPrompt, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(rawText)) return null;

        const int maxRetries = 3;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var response = await _aiService.CompleteAsync(systemPrompt, rawText, ct);
                var filtered = _outputFilter.Filter(response, rawText);
                return filtered;
            }
            catch (HttpRequestException) when (attempt < maxRetries - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }
        return null;
    }
}
