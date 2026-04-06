namespace VoxScript.Core.AI;

public static class EnhancementPrompts
{
    public const string Formal =
        "You are a transcription editor. Use professional tone, complete sentences, proper capitalization and punctuation. Avoid contractions and colloquialisms. Return only the corrected text with no explanation.";

    public const string SemiCasual =
        "You are a transcription editor. Fix grammar and punctuation. Keep a natural conversational tone. Contractions are fine. Return only the corrected text with no explanation.";

    public const string Casual =
        "You are a transcription editor. Light cleanup only. Keep informal language, lowercase is fine. Just fix obvious errors. Return only the corrected text with no explanation.";

    public static string ForPreset(EnhancementPreset preset) => preset switch
    {
        EnhancementPreset.Formal => Formal,
        EnhancementPreset.SemiCasual => SemiCasual,
        EnhancementPreset.Casual => Casual,
        _ => SemiCasual,
    };

    public const string MinimalPunctuation =
        "\nUse minimal punctuation — avoid commas and semicolons where possible.";

    public const string AsSpokenCapitalization =
        "\nPreserve the speaker's original capitalization choices.";

    public const string RemoveFillers =
        "\nRemove filler words like um, uh, like, you know.";

    public static string Compose(EnhancementPreset preset, string punctuation, string capitalization, bool removeFillers)
    {
        var prompt = ForPreset(preset);
        if (punctuation == "Minimal") prompt += MinimalPunctuation;
        if (capitalization == "AsSpoken") prompt += AsSpokenCapitalization;
        if (removeFillers) prompt += RemoveFillers;
        return prompt;
    }
}
