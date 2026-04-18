using Serilog;
using VoxScript.Core.Settings;

namespace VoxScript.Core.AI;

public sealed class StructuralFormattingService(
    IAiCompleter completer,
    ApiKeyManager keyManager,
    AppSettings settings) : IStructuralFormattingService
{
    private static readonly TimeSpan InternalTimeout = TimeSpan.FromSeconds(5);

    public bool IsConfigured => settings.StructuralAiProvider switch
    {
        AiProvider.OpenAI    => keyManager.GetStructuralOpenAiKey() is { Length: > 0 },
        AiProvider.Anthropic => keyManager.GetStructuralAnthropicKey() is { Length: > 0 },
        AiProvider.Local     => true,
        _                    => false,
    };

    public async Task<string?> FormatAsync(string text, CancellationToken ct)
    {
        if (!IsConfigured) return null;
        if (string.IsNullOrWhiteSpace(text)) return null;

        using var internalCts = new CancellationTokenSource(InternalTimeout);
        using var linked      = CancellationTokenSource.CreateLinkedTokenSource(ct, internalCts.Token);

        try
        {
            var config = new AiCompletionConfig(
                settings.StructuralAiProvider,
                settings.StructuralAiModel,
                settings.StructuralOllamaEndpoint,
                settings.StructuralAiProvider switch
                {
                    AiProvider.OpenAI    => keyManager.GetStructuralOpenAiKey(),
                    AiProvider.Anthropic => keyManager.GetStructuralAnthropicKey(),
                    _                    => null,
                });

            var raw       = await completer.CompleteAsync(config, StructuralFormattingPrompt.System, text, linked.Token);
            var validated = StructuralFormattingPrompt.ValidateOutput(raw, text);

            if (validated is null)
                Log.Debug("Structural formatting: validator rejected LLM output " +
                          "(orig={OrigWords}, result={ResultWords})",
                    text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .Count(t => t.Any(char.IsLetter)),
                    raw?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .Count(t => t.Any(char.IsLetter)));

            return validated;
        }
        catch (OperationCanceledException oce)
        {
            // External cancel: propagate so the pipeline knows the user stopped
            if (ct.IsCancellationRequested)
                throw;

            // Internal timeout: fall back silently
            Log.Warning(oce, "Structural formatting timed out after {Timeout}s, using rule-based output",
                InternalTimeout.TotalSeconds);
            return null;
        }
        catch (HttpRequestException hex)
        {
            Log.Debug(hex, "Structural formatting HTTP error (provider not reachable?), using rule-based output");
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Structural formatting unexpected error, using rule-based output");
            return null;
        }
    }
}
