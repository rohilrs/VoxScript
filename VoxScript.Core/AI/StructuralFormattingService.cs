using Serilog;
using VoxScript.Core.Settings;

namespace VoxScript.Core.AI;

public sealed class StructuralFormattingService(
    IAiCompleter completer,
    ApiKeyManager keyManager,
    AppSettings settings) : IStructuralFormattingService
{
    private static readonly TimeSpan InternalTimeout = TimeSpan.FromSeconds(30);
    private const int MinContentWords = 10;

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

        // Skip the LLM entirely for short input. Lists need multiple items —
        // short utterances never benefit from structural reformatting and just
        // burn API cost / Ollama warmup time.
        if (CountContentWords(text) < MinContentWords) return null;

        using var internalCts = new CancellationTokenSource(InternalTimeout);
        using var linked      = CancellationTokenSource.CreateLinkedTokenSource(ct, internalCts.Token);

        try
        {
            var systemPrompt = string.IsNullOrWhiteSpace(settings.StructuralFormattingPromptOverride)
                ? StructuralFormattingPrompt.System
                : settings.StructuralFormattingPromptOverride!;
            var raw       = await completer.CompleteAsync(BuildConfig(), systemPrompt, text, linked.Token);
            var validated = StructuralFormattingPrompt.ValidateOutput(raw, text);

            int origWords   = CountContentWords(text);
            int resultWords = CountContentWords(raw);

            if (validated is null)
                Log.Information(
                    "Structural formatting: validator REJECTED (orig={OrigWords} result={ResultWords} ratio={Ratio:F2}). LLM output was: {Raw}",
                    origWords, resultWords,
                    origWords == 0 ? 0.0 : (double)resultWords / origWords,
                    raw);
            else
                Log.Information(
                    "Structural formatting: validator ACCEPTED (orig={OrigWords} result={ResultWords} ratio={Ratio:F2})",
                    origWords, resultWords,
                    origWords == 0 ? 0.0 : (double)resultWords / origWords);

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

    public Task WarmupAsync()
    {
        // Cloud providers are always warm; only Local needs a wake-up.
        if (!IsConfigured || settings.StructuralAiProvider != AiProvider.Local)
            return Task.CompletedTask;

        return Task.Run(async () =>
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                // Tiny request just to force model load. Output is discarded.
                await completer.CompleteAsync(BuildConfig(), "Reply with: ok", "ok", CancellationToken.None);
                Log.Information("Structural formatting model warmed up in {Elapsed}ms",
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Structural formatting warmup failed (Ollama not reachable?)");
            }
        });
    }

    private AiCompletionConfig BuildConfig() => new(
        settings.StructuralAiProvider,
        settings.StructuralAiModel,
        settings.StructuralOllamaEndpoint,
        settings.StructuralAiProvider switch
        {
            AiProvider.OpenAI    => keyManager.GetStructuralOpenAiKey(),
            AiProvider.Anthropic => keyManager.GetStructuralAnthropicKey(),
            _                    => null,
        });

    private static int CountContentWords(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                  .Count(t => t.Any(char.IsLetter));
}
