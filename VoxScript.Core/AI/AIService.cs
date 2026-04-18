using VoxScript.Core.Settings;

namespace VoxScript.Core.AI;

public sealed class AIService
{
    private readonly IAiCompleter _completer;
    private readonly ApiKeyManager _keyManager;
    private readonly AppSettings _settings;

    public bool IsConfigured => _settings.AiProvider switch
    {
        AiProvider.OpenAI    => _keyManager.GetOpenAiKey() is { Length: > 0 },
        AiProvider.Anthropic => _keyManager.GetAnthropicKey() is { Length: > 0 },
        AiProvider.Local     => true,
        _                    => false,
    };

    public AIService(IAiCompleter completer, ApiKeyManager keyManager, AppSettings settings)
    {
        _completer  = completer;
        _keyManager = keyManager;
        _settings   = settings;
    }

    public Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        var config = new AiCompletionConfig(
            _settings.AiProvider,
            _settings.AiModelName,
            _settings.OllamaEndpoint,
            _settings.AiProvider switch
            {
                AiProvider.OpenAI    => _keyManager.GetOpenAiKey(),
                AiProvider.Anthropic => _keyManager.GetAnthropicKey(),
                _                    => null,
            });
        return _completer.CompleteAsync(config, systemPrompt, userMessage, ct);
    }
}
