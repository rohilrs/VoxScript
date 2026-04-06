using Serilog;
using VoxScript.Core.AI;
using VoxScript.Core.History;
using VoxScript.Core.Persistence;
using VoxScript.Core.PowerMode;
using VoxScript.Core.Transcription.Processing;

namespace VoxScript.Core.Transcription.Core;

public sealed class TranscriptionPipeline
{
    private readonly TranscriptionOutputFilter _filter;
    private readonly WhisperTextFormatter _formatter;
    private readonly WordReplacementService _wordReplacement;
    private readonly IAIEnhancementService _aiEnhancement;
    private readonly ITranscriptionRepository _repository;
    private readonly PowerModeSessionManager _powerMode;

    /// <summary>
    /// After the last transcription, holds the matched Power Mode config name
    /// and process name for display on the TranscribePage badge.
    /// </summary>
    public string? LastMatchedModeName { get; private set; }
    public string? LastMatchedProcessName { get; private set; }

    public TranscriptionPipeline(
        TranscriptionOutputFilter filter,
        WhisperTextFormatter formatter,
        WordReplacementService wordReplacement,
        IAIEnhancementService aiEnhancement,
        ITranscriptionRepository repository,
        PowerModeSessionManager powerMode)
    {
        _filter = filter;
        _formatter = formatter;
        _wordReplacement = wordReplacement;
        _aiEnhancement = aiEnhancement;
        _repository = repository;
        _powerMode = powerMode;
    }

    /// <summary>
    /// Runs the full pipeline: transcribe -> filter -> format -> word-replace -> AI enhance -> save.
    /// Post-transcription steps degrade gracefully on failure so the user never loses their transcript.
    /// </summary>
    public async Task<string?> RunAsync(
        ITranscriptionSession session,
        string audioFilePath,
        double durationSeconds,
        bool aiEnhancementEnabled,
        CancellationToken ct)
    {
        // 1. Transcribe — critical, let exceptions propagate
        var rawText = await session.TranscribeAsync(audioFilePath, ct);

        // 2. Filter hallucinations
        string filtered;
        try
        {
            filtered = _filter.Filter(rawText);
            if (string.IsNullOrWhiteSpace(filtered)) return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Hallucination filter failed, using raw text");
            filtered = rawText;
            if (string.IsNullOrWhiteSpace(filtered)) return null;
        }

        // 3. Format
        string formatted;
        try
        {
            formatted = _formatter.Format(filtered);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Text formatter failed, using filtered text");
            formatted = filtered;
        }

        // 4. Word replacement
        string replaced;
        try
        {
            replaced = await _wordReplacement.ApplyAsync(formatted, ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Word replacement failed, skipping");
            replaced = formatted;
        }

        // 5. AI enhancement — only runs when a context mode matches the active app
        string? enhancedText = null;
        LastMatchedModeName = null;
        LastMatchedProcessName = null;

        if (aiEnhancementEnabled)
        {
            try
            {
                var mode = await _powerMode.ResolveCurrentAsync(ct);
                if (mode is not null)
                {
                    LastMatchedModeName = mode.Name;
                    LastMatchedProcessName = _powerMode.LastMatchedProcessName;
                    var prompt = mode.GetEffectivePrompt();
                    Log.Information("Power Mode matched: {Mode} (preset: {Preset})", mode.Name, mode.Preset);
                    enhancedText = await _aiEnhancement.EnhanceWithPromptAsync(replaced, prompt, ct);
                }
                // No match = no enhancement (context modes are the only trigger)
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AI enhancement failed, using unenhanced text");
            }
        }

        var finalText = enhancedText ?? replaced;

        // 6. Persist — failure should not lose the transcript
        try
        {
            await _repository.AddAsync(new TranscriptionRecord
            {
                Text = replaced,
                EnhancedText = enhancedText,
                AudioFilePath = audioFilePath,
                DurationSeconds = durationSeconds,
                ModelName = session.Model.Name,
                WasAiEnhanced = enhancedText is not null,
            }, ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist transcription record");
        }

        return finalText;
    }
}
