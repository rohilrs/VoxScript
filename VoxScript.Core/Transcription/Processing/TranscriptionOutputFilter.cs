using System.Text.RegularExpressions;

namespace VoxScript.Core.Transcription.Processing;

/// <summary>Strips whisper hallucination artifacts from raw transcript text.</summary>
public sealed class TranscriptionOutputFilter
{
    // Whisper commonly hallucinates these phrases on silence
    private static readonly string[] HallucinationPhrases =
    [
        "Thank you for watching",
        "Thanks for watching",
        "Like and subscribe",
        "Subtitles by",
        "Transcribed by",
        "www.",
        "youtube.com",
        "[BLANK_AUDIO]",
        "(BLANK_AUDIO)",
        "[blank_audio]",
    ];

    private static readonly Regex RepetitionPattern = new(
        @"(.{10,}?)\1{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Filter(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var text = raw.Trim();

        foreach (var phrase in HallucinationPhrases)
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

        // Remove excessive repetition (hallucination loops)
        text = RepetitionPattern.Replace(text, "$1");

        return text.Trim();
    }
}
